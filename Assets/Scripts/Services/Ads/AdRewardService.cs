using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Coordinates rewarded ad viewing with game rewards.
/// Strictly protects against duplicate callbacks and race conditions,
/// and routes all rewarded items through RewardService.
/// </summary>
public class AdRewardService : MonoBehaviour
{
    public static AdRewardService Instance { get; private set; }

    private IRewardedAdService adService;

    /// <summary>Fired when a second-chance revive/retry is granted via ad.</summary>
    public event Action OnSecondChanceGranted;

    /// <summary>Fired whenever any rewarded ad completes and awards its benefit.</summary>
    public event Action<RewardedAdPlacement, int> OnAdRewardGranted;

    // Tracks active in-flight request IDs to prevent duplicate fulfillment
    private readonly HashSet<string> inFlightRequests = new HashSet<string>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
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
        this.adService = service;
    }

    public bool IsAdAvailable(RewardedAdPlacement placement)
    {
        if (adService == null)
            adService = FindFirstAdService();

        return adService != null && adService.IsAdAvailable(placement);
    }

    /// <summary>
    /// Initiates a rewarded ad request and processes the reward atomically if watched completely.
    /// </summary>
    /// <param name="placement">The ad placement context.</param>
    /// <param name="contextValue">Contextual value, e.g. base level clear coins to double.</param>
    /// <param name="onComplete">Callback invoked with true if reward was granted, false otherwise.</param>
    public void WatchAdForReward(RewardedAdPlacement placement, int contextValue = 0, Action<bool> onComplete = null)
    {
        if (adService == null)
            adService = FindFirstAdService();

        if (adService == null || !adService.IsAdAvailable(placement))
        {
            Debug.LogWarning($"[AdRewardService] Cannot show ad for {placement}: Ad service not ready.");
            onComplete?.Invoke(false);
            return;
        }

        string requestId = Guid.NewGuid().ToString();
        inFlightRequests.Add(requestId);

        adService.ShowRewardedAd(placement, result =>
        {
            // Ensure atomic single-fulfillment protection
            if (!inFlightRequests.Remove(requestId))
            {
                Debug.LogWarning($"[AdRewardService] Request '{requestId}' was already processed or cancelled.");
                return;
            }

            if (!result.IsSuccess)
            {
                Debug.Log($"[AdRewardService] Ad not completed ({result.Status}). No reward granted.");
                onComplete?.Invoke(false);
                return;
            }

            // Grant reward based on placement
            FulfillReward(placement, contextValue);
            onComplete?.Invoke(true);
        });
    }

    private void FulfillReward(RewardedAdPlacement placement, int contextValue)
    {
        var rewardService = RewardService.Instance ?? FindAnyObjectByType<RewardService>();

        switch (placement)
        {
            case RewardedAdPlacement.LevelClearCoinDoubler:
                if (contextValue > 0 && rewardService != null)
                {
                    var bundle = new RewardBundle();
                    bundle.Add(RewardType.Coins, contextValue);
                    rewardService.GrantRewardBundle(bundle, "Ad_CoinDoubler");
                }
                break;

            case RewardedAdPlacement.CoinBonusMultiplier:
                if (contextValue > 0 && rewardService != null)
                {
                    var bundle = new RewardBundle();
                    bundle.Add(RewardType.Coins, contextValue);
                    rewardService.GrantRewardBundle(bundle, "Ad_CoinBonusMultiplier");
                }
                break;

            case RewardedAdPlacement.PostGameBonus:
                if (contextValue > 0 && rewardService != null)
                {
                    var bundle = new RewardBundle();
                    bundle.Add(RewardType.Coins, contextValue);
                    rewardService.GrantRewardBundle(bundle, "Ad_PostGameBonus");
                }
                break;

            case RewardedAdPlacement.BlindBoxDoubler:
            case RewardedAdPlacement.FreePower:
                // Re-granting is handled by the caller via the WatchAdForReward
                // completion callback — no fulfillment here.
                break;

            case RewardedAdPlacement.SecondChance:
                OnSecondChanceGranted?.Invoke();
                Debug.Log("[AdRewardService] Second chance granted via ad completion.");
                break;
        }

        OnAdRewardGranted?.Invoke(placement, contextValue);
    }

    /// <summary>Finds the first active IRewardedAdService implementation in the scene.</summary>
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

