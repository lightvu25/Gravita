using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central observer bridging gameplay, progression, currency ledger, Power Skills,
/// Ads, IAP, and Mission events into <see cref="AnalyticsService"/> (Gravita Economy GDD §13).
/// </summary>
public class AnalyticsEventObserver : MonoBehaviour
{
    private LevelFlowController flowController;
    private ProgressionManager progression;
    private ShopManager shopManager;
    private AdRewardService adRewardService;
    private DailyRewardService dailyRewardService;
    private CoinBonusService coinBonusService;

    private LevelState lastLevelState = LevelState.Idle;
    private bool subscribed;

    private void Awake()
    {
        flowController     = FindAnyObjectByType<LevelFlowController>();
        progression        = FindAnyObjectByType<ProgressionManager>();
        shopManager        = FindAnyObjectByType<ShopManager>();
        adRewardService    = FindAnyObjectByType<AdRewardService>();
        dailyRewardService = FindAnyObjectByType<DailyRewardService>();
        coinBonusService   = FindAnyObjectByType<CoinBonusService>();

        Subscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (flowController != null)
        {
            flowController.OnLevelStarted         += HandleLevelStarted;
            flowController.OnLevelCompleted       += HandleLevelCompleted;
            flowController.OnLevelFailed          += HandleLevelFailed;
            flowController.OnLevelRestarted       += HandleLevelRestarted;
            flowController.OnStateChanged         += HandleLevelStateChanged;
            flowController.OnReturnToMapRequested += HandleReturnToMap;
            lastLevelState = flowController.CurrentState;
        }

        if (progression != null)
            progression.OnLevelUnlocked += HandleLevelUnlocked;

        if (shopManager != null)
            shopManager.OnItemPurchased += HandleItemPurchased;

        if (adRewardService != null)
            adRewardService.OnAdRewardGranted += HandleAdRewardGranted;

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed += HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed += HandleCoinBonusCollected;

        // Economy GDD §13 static domain hooks
        EconomyManager.OnCurrencyLedgerEvent              += HandleCurrencyLedgerEvent;
        CampaignEconomyService.OnLevelEconomySettled      += HandleLevelEconomySettled;
        ProgressionManager.OnSkillInventoryChanged        += HandleSkillInventoryChanged;
        PowerUpController.OnSkillActivationAttempted      += HandleSkillActivationAttempted;
        AdPolicyCoordinator.OnRewardedAdOffered           += HandleRewardedAdOffered;
        AdPolicyCoordinator.OnRewardedAdStarted           += HandleRewardedAdStarted;
        AdPolicyCoordinator.OnRewardedAdCompleted         += HandleRewardedAdCompleted;
        AdPolicyCoordinator.OnRewardedAdSkippedOrFailed   += HandleRewardedAdSkippedOrFailed;
        AdPolicyCoordinator.OnInterstitialShownEvent      += HandleInterstitialShown;
        AdPolicyCoordinator.OnInterstitialSuppressedEvent += HandleInterstitialSuppressed;
        IAPManager.OnIapPurchaseInitiated                 += HandleIapInitiated;
        IAPManager.OnIapPurchaseCompleted                 += HandleIapCompleted;
        IAPManager.OnIapPurchaseFailedEvent               += HandleIapFailed;
        IAPManager.OnIapRestoreCompleted                  += HandleIapRestored;
        MissionService.OnMissionRewardClaimed             += HandleMissionRewardClaimed;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (flowController != null)
        {
            flowController.OnLevelStarted         -= HandleLevelStarted;
            flowController.OnLevelCompleted       -= HandleLevelCompleted;
            flowController.OnLevelFailed          -= HandleLevelFailed;
            flowController.OnLevelRestarted       -= HandleLevelRestarted;
            flowController.OnStateChanged         -= HandleLevelStateChanged;
            flowController.OnReturnToMapRequested -= HandleReturnToMap;
        }

        if (progression != null)
            progression.OnLevelUnlocked -= HandleLevelUnlocked;

        if (shopManager != null)
            shopManager.OnItemPurchased -= HandleItemPurchased;

        if (adRewardService != null)
            adRewardService.OnAdRewardGranted -= HandleAdRewardGranted;

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed -= HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed -= HandleCoinBonusCollected;

        EconomyManager.OnCurrencyLedgerEvent              -= HandleCurrencyLedgerEvent;
        CampaignEconomyService.OnLevelEconomySettled      -= HandleLevelEconomySettled;
        ProgressionManager.OnSkillInventoryChanged        -= HandleSkillInventoryChanged;
        PowerUpController.OnSkillActivationAttempted      -= HandleSkillActivationAttempted;
        AdPolicyCoordinator.OnRewardedAdOffered           -= HandleRewardedAdOffered;
        AdPolicyCoordinator.OnRewardedAdStarted           -= HandleRewardedAdStarted;
        AdPolicyCoordinator.OnRewardedAdCompleted         -= HandleRewardedAdCompleted;
        AdPolicyCoordinator.OnRewardedAdSkippedOrFailed   -= HandleRewardedAdSkippedOrFailed;
        AdPolicyCoordinator.OnInterstitialShownEvent      -= HandleInterstitialShown;
        AdPolicyCoordinator.OnInterstitialSuppressedEvent -= HandleInterstitialSuppressed;
        IAPManager.OnIapPurchaseInitiated                 -= HandleIapInitiated;
        IAPManager.OnIapPurchaseCompleted                 -= HandleIapCompleted;
        IAPManager.OnIapPurchaseFailedEvent               -= HandleIapFailed;
        IAPManager.OnIapRestoreCompleted                  -= HandleIapRestored;
        MissionService.OnMissionRewardClaimed             -= HandleMissionRewardClaimed;
    }

    // ─────────────────────────────────────────────────────────────
    //  Level
    // ─────────────────────────────────────────────────────────────

    private void HandleLevelStarted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        AnalyticsService.Track(AnalyticsEvents.LevelStarted, LevelParams(level));
    }

    private void HandleLevelCompleted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        var p = LevelParams(level);

        if (level != null && ProgressionManager.Instance != null)
            p["stars"] = ProgressionManager.Instance.GetLevelStars(level.LevelId);

        if (flowController != null)
        {
            p["moves_used"] = flowController.MovesUsedThisRun;
            p["used_skill"] = flowController.UsedAnySkillThisRun;
        }

        AnalyticsService.Track(AnalyticsEvents.LevelCompleted, p);
    }

    private void HandleLevelFailed(string reason)
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        var p = LevelParams(level);
        p["reason"] = reason;
        AnalyticsService.Track(AnalyticsEvents.LevelFailed, p);
    }

    private void HandleLevelRestarted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        AnalyticsService.Track(AnalyticsEvents.LevelRestarted, LevelParams(level));
    }

    private void HandleLevelStateChanged(LevelState state)
    {
        if (state != LevelState.Idle)
            lastLevelState = state;
    }

    private void HandleReturnToMap()
    {
        if (lastLevelState == LevelState.Playing)
        {
            var level = flowController != null ? flowController.CurrentLevelData : null;
            AnalyticsService.Track(AnalyticsEvents.LevelAbandoned, LevelParams(level));
        }
        lastLevelState = LevelState.Idle;
    }

    private void HandleLevelUnlocked(LevelData level)
    {
        AnalyticsService.Track(AnalyticsEvents.LevelUnlocked, LevelParams(level));
    }

    // ─────────────────────────────────────────────────────────────
    //  Economy Ledger & Campaign Settlement (GDD §13)
    // ─────────────────────────────────────────────────────────────

    private void HandleCurrencyLedgerEvent(EconomyCurrencyType currency, int signedDelta, int newBalance, string tag)
    {
        if (signedDelta == 0) return;

        string eventName;
        if (currency == EconomyCurrencyType.Coins)
            eventName = signedDelta > 0 ? AnalyticsEvents.CoinEarned : AnalyticsEvents.CoinSpent;
        else
            eventName = signedDelta > 0 ? AnalyticsEvents.GemEarned : AnalyticsEvents.GemSpent;

        AnalyticsService.Track(eventName, new Dictionary<string, object>
        {
            ["amount"]  = Mathf.Abs(signedDelta),
            ["balance"] = newBalance,
            ["source"]  = tag ?? string.Empty
        });
    }

    private void HandleLevelEconomySettled(LevelEconomySettlement s)
    {
        AnalyticsService.Track(AnalyticsEvents.LevelEconomySettled, new Dictionary<string, object>
        {
            ["level_id"]            = s.LevelId,
            ["sector"]              = s.SectorNumber,
            ["is_first_clear"]      = s.IsFirstClear,
            ["earned_stars"]        = s.EarnedStars,
            ["newly_unlocked_stars"] = s.NewlyUnlockedStarsCount,
            ["moves_used"]          = s.MovesUsed,
            ["best_moves"]          = s.BestMoves,
            ["used_any_skill"]      = s.UsedAnySkill,
            ["coins_granted"]       = s.TotalCoinsGranted,
            ["gems_granted"]        = s.TotalGemsGranted
        });

        if (s.SectorCompletedThisRun)
        {
            AnalyticsService.Track(AnalyticsEvents.SectorCompleted, new Dictionary<string, object>
            {
                ["sector"] = s.SectorNumber,
                ["coins"]  = s.SectorCompletionCoinsGranted,
                ["gems"]   = s.SectorCompletionGemsGranted
            });
        }

        if (s.CampaignCompletedThisRun)
        {
            ProfileData profile = SaveManager.loadProfile();
            AnalyticsService.Track(AnalyticsEvents.CampaignCompleted, new Dictionary<string, object>
            {
                ["watched_any_ad"]      = profile != null && profile.hasWatchedAnyAdInCampaign,
                ["made_any_iap"]        = profile != null && profile.hasMadeAnyIapInCampaign,
                ["no_skill_clears"]     = profile != null ? profile.lifetimeNoSkillLevelClears : 0,
                ["total_deaths"]        = profile != null ? profile.deaths : 0
            });
        }
    }

    private void HandleSkillInventoryChanged(string skillId, int signedDelta, int remaining, int cap, string reason)
    {
        AnalyticsService.Track(AnalyticsEvents.SkillChargeChanged, new Dictionary<string, object>
        {
            ["skill_id"]  = skillId ?? string.Empty,
            ["delta"]     = signedDelta,
            ["remaining"] = remaining,
            ["cap"]       = cap,
            ["reason"]    = reason ?? string.Empty
        });
    }

    private void HandleSkillActivationAttempted(PowerUpType skillType, bool succeeded, string reason)
    {
        AnalyticsService.Track(AnalyticsEvents.SkillActivationAttempted, new Dictionary<string, object>
        {
            ["skill_type"] = skillType.ToString(),
            ["succeeded"]  = succeeded,
            ["reason"]     = reason ?? string.Empty
        });
    }

    private void HandleRewardedAdOffered(RewardedAdPlacement placement)
    {
        AnalyticsService.Track(AnalyticsEvents.RewardedAdOffered, new Dictionary<string, object>
        {
            ["placement"] = placement.ToString()
        });
    }

    private void HandleRewardedAdStarted(RewardedAdPlacement placement)
    {
        AnalyticsService.Track(AnalyticsEvents.RewardedAdStarted, new Dictionary<string, object>
        {
            ["placement"] = placement.ToString()
        });
    }

    private void HandleRewardedAdCompleted(RewardedAdPlacement placement, RewardItem item)
    {
        AnalyticsService.Track(AnalyticsEvents.RewardedAdCompleted, new Dictionary<string, object>
        {
            ["placement"]   = placement.ToString(),
            ["reward_type"] = item.Type.ToString(),
            ["amount"]      = item.Amount,
            ["item_id"]     = item.ItemId ?? string.Empty
        });
    }

    private void HandleRewardedAdSkippedOrFailed(RewardedAdPlacement placement, string reason)
    {
        AnalyticsService.Track(AnalyticsEvents.RewardedAdSkipped, new Dictionary<string, object>
        {
            ["placement"] = placement.ToString(),
            ["reason"]    = reason ?? string.Empty
        });
    }

    private void HandleInterstitialShown()
    {
        AnalyticsService.Track(AnalyticsEvents.InterstitialShown);
    }

    private void HandleInterstitialSuppressed(string reason)
    {
        AnalyticsService.Track(AnalyticsEvents.InterstitialSuppressed, new Dictionary<string, object>
        {
            ["reason"] = reason ?? string.Empty
        });
    }

    private void HandleIapInitiated(string productId)
    {
        AnalyticsService.Track(AnalyticsEvents.IapInitiated, new Dictionary<string, object>
        {
            ["product_id"] = productId ?? string.Empty
        });
    }

    private void HandleIapCompleted(string productId, string transactionId)
    {
        AnalyticsService.Track(AnalyticsEvents.IapCompleted, new Dictionary<string, object>
        {
            ["product_id"]     = productId ?? string.Empty,
            ["transaction_id"] = transactionId ?? string.Empty
        });
    }

    private void HandleIapFailed(string productId, string reason)
    {
        AnalyticsService.Track(AnalyticsEvents.IapFailed, new Dictionary<string, object>
        {
            ["product_id"] = productId ?? string.Empty,
            ["reason"]     = reason ?? string.Empty
        });
    }

    private void HandleIapRestored(bool succeeded, int restoredCount)
    {
        AnalyticsService.Track(AnalyticsEvents.IapRestored, new Dictionary<string, object>
        {
            ["succeeded"]      = succeeded,
            ["restored_count"] = restoredCount
        });
    }

    private void HandleMissionRewardClaimed(string missionId, MissionCategory category, RewardBundle bundle)
    {
        AnalyticsService.Track(AnalyticsEvents.MissionRewardClaimed, new Dictionary<string, object>
        {
            ["mission_id"] = missionId ?? string.Empty,
            ["category"]   = category.ToString(),
            ["coins"]      = bundle != null ? bundle.CoinCount : 0,
            ["gems"]       = bundle != null ? bundle.GemCount : 0
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Shop / Legacy Meta
    // ─────────────────────────────────────────────────────────────

    private void HandleItemPurchased(ShopItemDefinition item)
    {
        if (item == null) return;
        AnalyticsService.Track(AnalyticsEvents.ShopPurchaseSuccess, new Dictionary<string, object>
        {
            ["item_id"]     = item.ItemId,
            ["category"]    = item.Category.ToString(),
            ["cost_type"]   = item.CostType.ToString(),
            ["cost_amount"] = item.CostAmount
        });
    }

    private void HandleAdRewardGranted(RewardedAdPlacement placement, int contextValue)
    {
        AnalyticsService.Track(AnalyticsEvents.RewardDoubled, new Dictionary<string, object>
        {
            ["placement"] = placement.ToString(),
            ["amount"]    = contextValue
        });
    }

    private void HandleDailyRewardClaimed(int streakDay, RewardBundle bundle)
    {
        AnalyticsService.Track(AnalyticsEvents.DailyRewardClaimed, new Dictionary<string, object>
        {
            ["streak_day"] = streakDay,
            ["coins"]      = bundle != null ? bundle.CoinCount : 0
        });
    }

    private void HandleCoinBonusCollected(int amount)
    {
        AnalyticsService.Track(AnalyticsEvents.CoinBonusCollected, new Dictionary<string, object>
        {
            ["coins"] = amount
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object> LevelParams(LevelData level)
    {
        var p = new Dictionary<string, object>();
        if (level != null)
        {
            p["level_id"]    = level.LevelId;
            p["world_index"] = level.World;
        }
        return p;
    }
}
