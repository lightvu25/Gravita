using System;
using UnityEngine;

/// <summary>
/// Manages the periodic coin bonus pickup in Kinetic Maze.
/// The bonus amount scales with the player's total star count and
/// supports cooldown tracking plus an optional rewarded-ad doubler.
/// </summary>
public class CoinBonusService : MonoBehaviour
{
    public static CoinBonusService Instance { get; private set; }

    public const double BonusCooldownHours = 4.0;
    public const int BaseBonusCoins = 30;
    public const int StarBonusCoinMultiplier = 2;

    public event Action<int> OnCoinBonusClaimed;

    private long lastClaimUtcTicks;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadFromProfile();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Checks if the coin bonus is ready for collection.
    /// </summary>
    public bool CanCollectBonus()
    {
        if (lastClaimUtcTicks == 0) return true;

        var lastClaim = new DateTime(lastClaimUtcTicks, DateTimeKind.Utc);
        return (DateTime.UtcNow - lastClaim).TotalHours >= BonusCooldownHours;
    }

    /// <summary>
    /// Time remaining until the next coin bonus is available.
    /// </summary>
    public TimeSpan GetTimeUntilNextBonus()
    {
        if (CanCollectBonus()) return TimeSpan.Zero;

        var lastClaim = new DateTime(lastClaimUtcTicks, DateTimeKind.Utc);
        var nextAvailable = lastClaim.AddHours(BonusCooldownHours);
        var diff = nextAvailable - DateTime.UtcNow;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    /// <summary>
    /// Calculates current coin bonus yield based on total stars earned.
    /// </summary>
    public int CalculateBonusAmount()
    {
        int stars = ProgressionManager.Instance != null ? ProgressionManager.Instance.TotalStars : 0;
        return BaseBonusCoins + (stars * StarBonusCoinMultiplier);
    }

    /// <summary>
    /// Collects the coin bonus.
    /// </summary>
    /// <param name="doubleWithAd">If true, attempts to watch a rewarded ad to double the bonus.</param>
    /// <param name="onComplete">Callback with true on successful collection.</param>
    public void CollectBonus(bool doubleWithAd = false, Action<bool> onComplete = null)
    {
        if (!CanCollectBonus())
        {
            Debug.LogWarning("[CoinBonusService] Bonus not ready to collect yet.");
            onComplete?.Invoke(false);
            return;
        }

        int amount = CalculateBonusAmount();
        var rewardService = RewardService.Instance ?? FindAnyObjectByType<RewardService>();

        if (doubleWithAd)
        {
            var adService = AdRewardService.Instance ?? FindAnyObjectByType<AdRewardService>();
            if (adService != null && adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier))
            {
                adService.WatchAdForReward(RewardedAdPlacement.CoinBonusMultiplier, amount, adSuccess =>
                {
                    // Base amount is always granted if ad completed, plus the ad bonus awarded by AdRewardService
                    if (adSuccess)
                    {
                        GrantBaseAndFinalize(amount, rewardService);
                        onComplete?.Invoke(true);
                    }
                    else
                    {
                        // User cancelled/skipped ad: grant standard base amount
                        GrantBaseAndFinalize(amount, rewardService);
                        onComplete?.Invoke(true);
                    }
                });
                return;
            }
        }

        GrantBaseAndFinalize(amount, rewardService);
        onComplete?.Invoke(true);
    }

    private void GrantBaseAndFinalize(int amount, RewardService rewardService)
    {
        if (rewardService != null)
        {
            var bundle = new RewardBundle();
            bundle.Add(RewardType.Coins, amount);
            rewardService.GrantRewardBundle(bundle, "CoinBonus");
        }

        lastClaimUtcTicks = DateTime.UtcNow.Ticks;
        SaveToProfile();

        OnCoinBonusClaimed?.Invoke(amount);
        Debug.Log($"[CoinBonusService] Successfully collected {amount} bonus coins.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence Integration
    // ─────────────────────────────────────────────────────────────

    public void LoadFromProfile()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile != null)
        {
            lastClaimUtcTicks = profile.lastCoinBonusClaimUtcTicks;
        }
        else
        {
            lastClaimUtcTicks = 0;
        }
    }

    private void SaveToProfile()
    {
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.lastCoinBonusClaimUtcTicks = lastClaimUtcTicks;
        SaveManager.saveProfile(profile);
    }

    public void ResetForTesting()
    {
        lastClaimUtcTicks = 0;
        SaveToProfile();
    }
}
