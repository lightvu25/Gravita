using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates opt-in Rewarded Ad viewing with atomic reward fulfillment (Gravita Economy GDD §7 & §12).
/// Guarantees:
///   - Enforces placement whitelist, daily cap (<c>yyyy_MM_dd</c> local), and anti-clock-rollback via <see cref="AdPolicyCoordinator"/>.
///   - Grants ZERO reward when an ad is closed early, skipped, or fails (<see cref="RewardedAdResult.IsSuccess"/> == false).
///   - Strictly forbids granting <see cref="RewardType.Gems"/> via <see cref="RewardTransaction.ForbidGemsInPayout"/>.
///   - Suppresses the post-Result Interstitial Ad whenever a Result Screen Rewarded Ad completes (GDD §8.4).
/// </summary>
public class AdRewardService : MonoBehaviour
{
    public static AdRewardService Instance { get; private set; }

    private IRewardedAdService adService;

    /// <summary>Fired when a second-chance revive/retry is granted via ad (only when EnableMidGameplayRevive is true).</summary>
    public event Action OnSecondChanceGranted;

    /// <summary>Fired whenever any rewarded ad completes and awards its benefit.</summary>
    public event Action<RewardedAdPlacement, int> OnAdRewardGranted;

    private readonly HashSet<string> inFlightRequests = new HashSet<string>();
    private float lastRewardedClaimRealtime = float.NegativeInfinity;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        adService = GetComponent<IRewardedAdService>() ?? FindFirstAdService();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetAdService(IRewardedAdService service)
    {
        adService = service;
    }

    public bool IsAdAvailable(RewardedAdPlacement placement)
    {
        if (!AdPolicyCoordinator.CanWatchRewardedAd(placement, out _))
            return false;

        if (!IsCooldownElapsed())
            return false;

        if (adService == null)
            adService = FindFirstAdService();

        return adService != null && adService.IsAdAvailable(placement);
    }

    private bool IsCooldownElapsed()
    {
        float cd = EconomyBalanceConfig.Active.RewardedAdCooldownSeconds;
        if (cd <= 0f)
            return true;

        return Time.unscaledTime - lastRewardedClaimRealtime >= cd;
    }

    /// <summary>
    /// Initiates an opt-in Rewarded Ad request and processes the fixed non-Gem reward atomically ONLY if watched completely.
    /// </summary>
    public void WatchAdForReward(RewardedAdPlacement placement, int contextValue = 0, Action<bool> onComplete = null)
    {
        if (!AdPolicyCoordinator.CanWatchRewardedAd(placement, out string blockReason))
        {
            Debug.LogWarning($"[AdRewardService] Blocked rewarded ad for {placement}: {blockReason}.");
            AdPolicyCoordinator.NotifyAdSkippedOrFailed(placement, blockReason);
            onComplete?.Invoke(false);
            return;
        }

        if (!IsCooldownElapsed())
        {
            AdPolicyCoordinator.NotifyAdSkippedOrFailed(placement, "CooldownActive");
            onComplete?.Invoke(false);
            return;
        }

        if (adService == null)
            adService = FindFirstAdService();

        if (adService == null || !adService.IsAdAvailable(placement))
        {
            Debug.LogWarning($"[AdRewardService] Cannot show ad for {placement}: Ad service not ready.");
            AdPolicyCoordinator.NotifyAdSkippedOrFailed(placement, "AdUnavailable");
            onComplete?.Invoke(false);
            return;
        }

        string requestId = Guid.NewGuid().ToString("N");
        inFlightRequests.Add(requestId);
        AdPolicyCoordinator.NotifyAdStarted(placement);

        adService.ShowRewardedAd(placement, result =>
        {
            if (!inFlightRequests.Remove(requestId))
            {
                Debug.LogWarning($"[AdRewardService] Duplicate callback ignored for request '{requestId}'.");
                return;
            }

            if (!result.IsSuccess)
            {
                Debug.Log($"[AdRewardService] Ad not completed ({result.Status}). Zero reward granted.");
                AdPolicyCoordinator.NotifyAdSkippedOrFailed(placement, result.Status.ToString());
                onComplete?.Invoke(false);
                return;
            }

            bool granted = FulfillVerifiedAdReward(placement, contextValue, requestId);
            if (granted)
                lastRewardedClaimRealtime = Time.unscaledTime;

            onComplete?.Invoke(granted);
        });
    }

    /// <summary>
    /// Executes the atomic post-ad transaction with <see cref="RewardTransaction.ForbidGemsInPayout"/> = true.
    /// Exposed for deterministic verification that Gems can NEVER be paid out by an ad transaction.
    /// </summary>
    public bool TryExecuteAdPayoutTransaction(
        RewardedAdPlacement placement,
        RewardBundle payout,
        string requestId,
        out RewardTransactionResult txResult)
    {
        ProfileData profile = AdPolicyCoordinator.EnsureDailyAdStateSynced(out bool rolledBack);
        if (rolledBack)
        {
            txResult = new RewardTransactionResult(RewardTransactionStatus.InvalidRequest, "Clock rollback active.");
            return false;
        }

        int limit = EconomyBalanceConfig.Active.RewardedAdDailyLimit;
        if (profile.dailyRewardedAdCount >= limit)
        {
            txResult = new RewardTransactionResult(RewardTransactionStatus.InvalidRequest, "Daily rewarded ad limit reached.");
            return false;
        }

        string dateKey = !string.IsNullOrEmpty(profile.dailyAdResetDateLocal)
            ? profile.dailyAdResetDateLocal
            : LocalClock.FormatDateKey(LocalClock.Now);
        string safeGuid = !string.IsNullOrEmpty(requestId) ? requestId : Guid.NewGuid().ToString("N");

        var tx = new RewardTransaction
        {
            RewardId = EconomyRewardIds.RewardedAdGrant(dateKey, placement.ToString(), safeGuid),
            RequireUniqueRewardId = true,
            Payout = payout,
            ForbidGemsInPayout = true, // GDD §7.2 hard rule: Rewarded Ads NEVER grant Gems
            RejectIfSkillCapExceeded = true,
            Reason = $"RewardedAd_{placement}",
            OnBeforeSaveMutateProfile = p =>
            {
                p.dailyRewardedAdCount = Mathf.Max(0, p.dailyRewardedAdCount + 1);
                p.hasWatchedAnyAdInCampaign = true;
            }
        };

        txResult = RewardService.ExecuteTransaction(tx);
        return txResult.Succeeded;
    }

    private bool FulfillVerifiedAdReward(RewardedAdPlacement placement, int contextValue, string requestId)
    {
        // Canonical GDD §7.1 placements: fixed reward from EconomyBalanceConfig (never dynamic x2 inflation)
        if (AdPolicyCoordinator.IsResultScreenPlacement(placement) || placement == RewardedAdPlacement.ShopBonus)
        {
            RewardItem fixedItem = AdPolicyCoordinator.GetFixedRewardForPlacement(placement);
            var bundle = new RewardBundle();
            bundle.Add(fixedItem);

            if (!TryExecuteAdPayoutTransaction(placement, bundle, requestId, out _))
                return false;

            if (AdPolicyCoordinator.IsResultScreenPlacement(placement))
            {
                // GDD §8.4: Watching Rewarded Ad on Result Screen suppresses the subsequent Interstitial
                AdPolicyCoordinator.SuppressNextResultTransitionInterstitial = true;
            }

            AdPolicyCoordinator.NotifyAdCompleted(placement, fixedItem);
            OnAdRewardGranted?.Invoke(placement, fixedItem.Amount);
            return true;
        }

        // Optional legacy placements (only reachable if explicitly enabled in EconomyBalanceConfig)
        switch (placement)
        {
            case RewardedAdPlacement.CoinBonusMultiplier:
            case RewardedAdPlacement.PostGameBonus:
            {
                int coins = Mathf.Max(1, contextValue);
                var bundle = new RewardBundle();
                bundle.Add(RewardType.Coins, coins);
                if (!TryExecuteAdPayoutTransaction(placement, bundle, requestId, out _))
                    return false;

                var item = new RewardItem(RewardType.Coins, coins);
                AdPolicyCoordinator.NotifyAdCompleted(placement, item);
                OnAdRewardGranted?.Invoke(placement, coins);
                return true;
            }

            case RewardedAdPlacement.SecondChance:
            {
                if (!TryExecuteAdPayoutTransaction(placement, new RewardBundle(), requestId, out _))
                    return false;

                OnSecondChanceGranted?.Invoke();
                AdPolicyCoordinator.NotifyAdCompleted(placement, default);
                OnAdRewardGranted?.Invoke(placement, 1);
                return true;
            }

            case RewardedAdPlacement.BlindBoxDoubler:
            case RewardedAdPlacement.FreePower:
            {
                if (!TryExecuteAdPayoutTransaction(placement, new RewardBundle(), requestId, out _))
                    return false;

                AdPolicyCoordinator.NotifyAdCompleted(placement, default);
                OnAdRewardGranted?.Invoke(placement, contextValue);
                return true;
            }

            default:
                return false;
        }
    }

    private IRewardedAdService FindFirstAdService()
    {
        foreach (var mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude))
        {
            if (mb is IRewardedAdService service)
                return service;
        }
        return null;
    }
}
