using System;
using UnityEngine;

/// <summary>
/// Manages the 7-day cyclical daily login rewards.
/// Tracks claim timestamps via UTC to prevent time-zone manipulation.
/// </summary>
public class DailyRewardService : MonoBehaviour
{
    public static DailyRewardService Instance { get; private set; }

    public event Action<int, RewardBundle> OnDailyRewardClaimed;

    private long lastClaimUtcTicks;
    private int currentStreakDay = 1;

    public int CurrentStreakDay => currentStreakDay;

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
    /// Checks if a new daily reward is available to claim.
    /// </summary>
    public bool CanClaimDailyReward()
    {
        if (lastClaimUtcTicks == 0) return true;

        var lastClaimDate = new DateTime(lastClaimUtcTicks, DateTimeKind.Utc).Date;
        var todayUtc = DateTime.UtcNow.Date;

        return todayUtc > lastClaimDate;
    }

    /// <summary>
    /// Time remaining until the next calendar day UTC rollover.
    /// </summary>
    public TimeSpan GetTimeUntilNextDailyReward()
    {
        if (CanClaimDailyReward()) return TimeSpan.Zero;

        var tomorrowUtc = DateTime.UtcNow.Date.AddDays(1);
        return tomorrowUtc - DateTime.UtcNow;
    }

    /// <summary>
    /// Returns the streak day the next claim would grant, without mutating state.
    /// Mirrors UpdateStreakOnClaim so UI can preview the pending reward.
    /// </summary>
    public int GetUpcomingStreakDay()
    {
        if (lastClaimUtcTicks == 0) return 1;

        var lastClaimDate = new DateTime(lastClaimUtcTicks, DateTimeKind.Utc).Date;
        int daysDifference = (DateTime.UtcNow.Date - lastClaimDate).Days;

        if (daysDifference == 1)
            return (currentStreakDay % 7) + 1;

        if (daysDifference <= 0)
            return (currentStreakDay % 7) + 1; // already claimed today; next claim is tomorrow

        return 1; // missed day or longer gap resets the streak
    }

    /// <summary>
    /// Claims today's daily login reward if eligible.
    /// </summary>
    public bool ClaimDailyReward()
    {
        if (!CanClaimDailyReward())
        {
            Debug.LogWarning("[DailyRewardService] Daily reward already claimed today.");
            return false;
        }

        UpdateStreakOnClaim();

        var bundle = BuildStreakRewardBundle(currentStreakDay);
        var rewardService = RewardService.Instance ?? FindAnyObjectByType<RewardService>();
        if (rewardService != null)
        {
            rewardService.GrantRewardBundle(bundle, $"DailyReward_Day_{currentStreakDay}");
        }

        lastClaimUtcTicks = DateTime.UtcNow.Ticks;
        SaveToProfile();

        OnDailyRewardClaimed?.Invoke(currentStreakDay, bundle);
        Debug.Log($"[DailyRewardService] Claimed Daily Reward Day {currentStreakDay}.");
        return true;
    }

    private void UpdateStreakOnClaim()
    {
        if (lastClaimUtcTicks == 0)
        {
            currentStreakDay = 1;
            return;
        }

        var lastClaimDate = new DateTime(lastClaimUtcTicks, DateTimeKind.Utc).Date;
        var todayUtc = DateTime.UtcNow.Date;
        int daysDifference = (todayUtc - lastClaimDate).Days;

        if (daysDifference == 1)
        {
            // Consecutive day
            currentStreakDay = (currentStreakDay % 7) + 1;
        }
        else
        {
            // Missed day or longer gap: reset streak to 1
            currentStreakDay = 1;
        }
    }

    public RewardBundle BuildStreakRewardBundle(int day)
    {
        var bundle = new RewardBundle();
        switch (day)
        {
            case 1:
                bundle.Add(RewardType.Coins, 50);
                break;
            case 2:
                bundle.Add(RewardType.Coins, 75);
                break;
            case 3:
                bundle.Add(RewardType.Coins, 75);
                break;
            case 4:
                bundle.Add(RewardType.Coins, 100);
                break;
            case 5:
                bundle.Add(RewardType.Coins, 100);
                break;
            case 6:
                bundle.Add(RewardType.Coins, 125);
                break;
            case 7:
                bundle.Add(RewardType.Coins, 150);
                bundle.Add(RewardType.Gems, 2);
                break;
            default:
                bundle.Add(RewardType.Coins, 50);
                break;
        }
        return bundle;
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence Integration
    // ─────────────────────────────────────────────────────────────

    public void LoadFromProfile()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile != null)
        {
            lastClaimUtcTicks = profile.lastDailyRewardClaimUtcTicks;
            currentStreakDay = profile.dailyRewardStreakDay > 0 ? profile.dailyRewardStreakDay : 1;
        }
        else
        {
            lastClaimUtcTicks = 0;
            currentStreakDay = 1;
        }
    }

    private void SaveToProfile()
    {
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.lastDailyRewardClaimUtcTicks = lastClaimUtcTicks;
        profile.dailyRewardStreakDay = currentStreakDay;
        SaveManager.saveProfile(profile);
    }

    public void ResetForTesting()
    {
        lastClaimUtcTicks = 0;
        currentStreakDay = 1;
        SaveToProfile();
    }
}

