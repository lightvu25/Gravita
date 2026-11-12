using System;
using UnityEngine;

/// <summary>
/// Enforces Gravita Economy GDD v1.0 Rewarded Ad (§7) and Interstitial Ad (§8) policies:
///   - Rewarded Ads allowed ONLY at Result Screen & Shop in standard Campaign (never mid-gameplay).
///   - Daily Rewarded Ad cap tracked by local date (<c>yyyy_MM_dd</c>) with anti-clock-rollback guard.
///   - Fixed non-Gem rewards only (Coins or Skill Charges within cap).
///   - Interstitial Ads strictly blocked when <c>Remove Ads</c> is owned, never count deaths/restarts,
///     fire every 2–3 completed levels on Result Screen exit, and are suppressed when the player just
///     watched a Rewarded Ad on that same Result Screen (§8.4).
/// </summary>
public static class AdPolicyCoordinator
{
    // Analytics hooks (GDD §13)
    public static event Action<RewardedAdPlacement> OnRewardedAdOffered;
    public static event Action<RewardedAdPlacement> OnRewardedAdStarted;
    public static event Action<RewardedAdPlacement, RewardItem> OnRewardedAdCompleted;
    public static event Action<RewardedAdPlacement, string> OnRewardedAdSkippedOrFailed;
    public static event Action OnInterstitialShownEvent;
    public static event Action<string> OnInterstitialSuppressedEvent;

    /// <summary>
    /// Set to true when a player completes a Rewarded Ad on the Result Screen;
    /// suppresses the Interstitial Ad on that Result Screen's exit transition (GDD §8.4).
    /// </summary>
    public static bool SuppressNextResultTransitionInterstitial { get; set; }

    public static void ResetRuntimeFlags()
    {
        SuppressNextResultTransitionInterstitial = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Rewarded Ads Policy (GDD §7)
    // ─────────────────────────────────────────────────────────────

    public static bool IsResultScreenPlacement(RewardedAdPlacement placement)
    {
        return placement == RewardedAdPlacement.ResultScreenBonus ||
               placement == RewardedAdPlacement.LevelClearCoinDoubler;
    }

    public static bool IsPlacementAllowedInCampaign(RewardedAdPlacement placement)
    {
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        return placement switch
        {
            RewardedAdPlacement.ResultScreenBonus     => true,
            RewardedAdPlacement.LevelClearCoinDoubler => true,
            RewardedAdPlacement.ShopBonus             => true,
            RewardedAdPlacement.CoinBonusMultiplier   => true,
            RewardedAdPlacement.SecondChance          => balance.EnableMidGameplayRevive,
            RewardedAdPlacement.BlindBoxDoubler       => balance.EnablePostLevelBlindBox,
            RewardedAdPlacement.PostGameBonus         => balance.EnableLegacyExtraRewardSystems,
            RewardedAdPlacement.FreePower             => balance.EnableLegacyExtraRewardSystems,
            _                                         => false
        };
    }

    public static RewardItem GetFixedRewardForPlacement(RewardedAdPlacement placement)
    {
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        if (placement == RewardedAdPlacement.ShopBonus)
            return balance.ShopAdReward;

        return balance.ResultScreenAdReward;
    }

    public static ProfileData EnsureDailyAdStateSynced(out bool isClockRolledBack)
    {
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.MigrateIfNeeded();

        DateTime rawNow = LocalClock.Now;
        bool dirty = LocalClock.EvaluateClockAndUpdateHighWater(profile, rawNow, out DateTime effectiveNow, out isClockRolledBack);

        string todayKey = LocalClock.FormatDateKey(effectiveNow);
        if (string.IsNullOrEmpty(profile.dailyAdResetDateLocal))
        {
            profile.dailyAdResetDateLocal = todayKey;
            profile.dailyRewardedAdCount = Mathf.Max(0, profile.dailyRewardedAdCount);
            dirty = true;
        }
        else if (!isClockRolledBack && string.CompareOrdinal(todayKey, profile.dailyAdResetDateLocal) > 0)
        {
            profile.dailyAdResetDateLocal = todayKey;
            profile.dailyRewardedAdCount = 0;
            dirty = true;
        }

        if (dirty)
        {
            SaveManager.TrySaveProfile(profile);
        }

        return profile;
    }

    public static int GetUsedDailyRewardedAds()
    {
        ProfileData profile = EnsureDailyAdStateSynced(out _);
        return profile != null ? Mathf.Max(0, profile.dailyRewardedAdCount) : 0;
    }

    public static int GetRemainingDailyRewardedAds()
    {
        ProfileData profile = EnsureDailyAdStateSynced(out bool rolledBack);
        if (rolledBack || profile == null)
            return 0;

        int limit = EconomyBalanceConfig.Active.RewardedAdDailyLimit;
        return Mathf.Max(0, limit - Mathf.Max(0, profile.dailyRewardedAdCount));
    }

    public static bool CanWatchRewardedAd(RewardedAdPlacement placement, out string blockReason)
    {
        if (!IsPlacementAllowedInCampaign(placement))
        {
            blockReason = "PlacementDisabled";
            return false;
        }

        // GDD §7.1: Rewarded Ads must NEVER appear during active puzzle gameplay
        LevelFlowController flow = UnityEngine.Object.FindAnyObjectByType<LevelFlowController>();
        if (flow != null && flow.CurrentState == LevelState.Playing)
        {
            blockReason = "MidGameplayForbidden";
            return false;
        }

        ProfileData profile = EnsureDailyAdStateSynced(out bool rolledBack);
        if (rolledBack)
        {
            blockReason = "ClockRolledBack";
            return false;
        }

        int limit = EconomyBalanceConfig.Active.RewardedAdDailyLimit;
        if (profile.dailyRewardedAdCount >= limit)
        {
            blockReason = "DailyCapReached";
            return false;
        }

        // Pre-validate skill cap if the configured fixed reward is a Skill Charge (GDD §5.1)
        if (IsResultScreenPlacement(placement) || placement == RewardedAdPlacement.ShopBonus)
        {
            RewardItem reward = GetFixedRewardForPlacement(placement);
            if (reward.Type == RewardType.Gems)
            {
                blockReason = "GemsForbiddenInAds";
                return false;
            }

            if (reward.Type == RewardType.PowerUp && !string.IsNullOrEmpty(reward.ItemId))
            {
                int currentOwned = ProgressionManager.Instance != null
                    ? ProgressionManager.Instance.GetPowerUpCount(reward.ItemId)
                    : GetProfileSkillCount(profile, reward.ItemId);
                int cap = EconomyBalanceConfig.Active.GetSkillInventoryCap(reward.ItemId);
                if (currentOwned + Mathf.Max(1, reward.Amount) > cap)
                {
                    blockReason = "SkillAtCap";
                    return false;
                }
            }
        }

        blockReason = string.Empty;
        return true;
    }

    public static void NotifyAdOffered(RewardedAdPlacement placement)
    {
        OnRewardedAdOffered?.Invoke(placement);
    }

    internal static void NotifyAdStarted(RewardedAdPlacement placement)
    {
        OnRewardedAdStarted?.Invoke(placement);
    }

    internal static void NotifyAdCompleted(RewardedAdPlacement placement, RewardItem item)
    {
        OnRewardedAdCompleted?.Invoke(placement, item);
    }

    internal static void NotifyAdSkippedOrFailed(RewardedAdPlacement placement, string reason)
    {
        OnRewardedAdSkippedOrFailed?.Invoke(placement, reason ?? string.Empty);
    }

    // ─────────────────────────────────────────────────────────────
    //  Interstitial Ads Policy & Cadence (GDD §8)
    // ─────────────────────────────────────────────────────────────

    public static bool IsRemoveAdsActive()
    {
        if (IAPManager.Instance != null && IAPManager.Instance.NoAdsPurchased)
            return true;

        ProfileData profile = SaveManager.loadProfile();
        return profile != null && profile.noAdsPurchased;
    }

    public static int RollNextInterstitialInterval()
    {
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        int min = balance.MinCompletedLevelsBetweenInterstitials;
        int max = balance.MaxCompletedLevelsBetweenInterstitials;
        return UnityEngine.Random.Range(min, max + 1);
    }

    /// <summary>
    /// Increments the completed-level counter toward the next Interstitial (GDD §8.1).
    /// Must ONLY be called upon valid level completion — never on death, failure, or restart (§8.3).
    /// </summary>
    public static void RecordCompletedLevelForInterstitialCadence()
    {
        if (IsRemoveAdsActive())
            return;

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
            return;

        profile.MigrateIfNeeded();

        if (profile.interstitialTargetLevelsInterval <= 0)
            profile.interstitialTargetLevelsInterval = RollNextInterstitialInterval();

        profile.interstitialCompletedLevelsSinceLastAd = Mathf.Max(0, profile.interstitialCompletedLevelsSinceLastAd + 1);
        SaveManager.TrySaveProfile(profile);
    }

    public static bool CanShowInterstitialAtResultExit(out string suppressReason)
    {
        if (IsRemoveAdsActive())
        {
            suppressReason = "RemoveAdsPurchased";
            OnInterstitialSuppressedEvent?.Invoke(suppressReason);
            return false;
        }

        LevelFlowController flow = UnityEngine.Object.FindAnyObjectByType<LevelFlowController>();
        if (flow != null && flow.CurrentState == LevelState.Playing)
        {
            suppressReason = "MidGameplayForbidden";
            OnInterstitialSuppressedEvent?.Invoke(suppressReason);
            return false;
        }

        // GDD §8.4: Suppress Interstitial if player just watched a Rewarded Ad on this Result Screen
        if (SuppressNextResultTransitionInterstitial)
        {
            SuppressNextResultTransitionInterstitial = false;
            suppressReason = "SuppressedAfterResultRewardedAd";
            OnInterstitialSuppressedEvent?.Invoke(suppressReason);
            return false;
        }

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
        {
            suppressReason = "NoProfile";
            return false;
        }

        if (profile.interstitialTargetLevelsInterval <= 0)
        {
            profile.interstitialTargetLevelsInterval = RollNextInterstitialInterval();
            SaveManager.TrySaveProfile(profile);
        }

        if (profile.interstitialCompletedLevelsSinceLastAd < profile.interstitialTargetLevelsInterval)
        {
            suppressReason = "CadenceNotReached";
            return false;
        }

        suppressReason = string.Empty;
        return true;
    }

    /// <summary>
    /// Evaluates GDD §8 rules and, if eligible, displays an Interstitial Ad on Result Screen exit
    /// (Next Level / Return to Map after completing a level).
    /// </summary>
    public static bool TryShowPostResultTransitionInterstitial(IInterstitialAdService explicitService = null)
    {
        if (!CanShowInterstitialAtResultExit(out _))
            return false;

        IInterstitialAdService service = explicitService ?? GoogleMobileAdsInterstitialService.Instance;
        if (service == null || !service.IsAdAvailable())
        {
            OnInterstitialSuppressedEvent?.Invoke("AdNotLoaded");
            return false;
        }

        bool shownSynchronously = false;
        if (service is GoogleMobileAdsInterstitialService admobService)
        {
            shownSynchronously = admobService.TryShowFromCoordinator();
        }
        else
        {
            service.ShowInterstitial(shown =>
            {
                if (shown)
                    FinalizeInterstitialImpression();
            });
            shownSynchronously = true;
        }

        if (shownSynchronously && service is GoogleMobileAdsInterstitialService)
        {
            FinalizeInterstitialImpression();
        }

        return shownSynchronously;
    }

    private static void FinalizeInterstitialImpression()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile != null)
        {
            profile.interstitialCompletedLevelsSinceLastAd = 0;
            profile.interstitialTargetLevelsInterval = RollNextInterstitialInterval();
            SaveManager.TrySaveProfile(profile);
        }

        OnInterstitialShownEvent?.Invoke();
    }

    private static int GetProfileSkillCount(ProfileData profile, string skillId)
    {
        if (profile == null || string.IsNullOrEmpty(skillId) || profile.powerUpIds == null || profile.powerUpCounts == null)
            return 0;

        int idx = profile.powerUpIds.IndexOf(skillId);
        return (idx >= 0 && idx < profile.powerUpCounts.Count) ? Mathf.Max(0, profile.powerUpCounts[idx]) : 0;
    }
}

