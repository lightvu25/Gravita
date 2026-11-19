using System;

/// <summary>
/// Canonical status codes returned by <see cref="RewardService.TryExecuteTransaction"/>.
/// </summary>
public enum RewardTransactionStatus
{
    Success = 0,
    InvalidRequest = 1,
    ReentrancyBlocked = 2,
    DuplicateRewardId = 3,
    DuplicateIapTransaction = 4,
    InsufficientFunds = 5,
    SkillCapExceeded = 6,
    CosmeticAlreadyOwned = 7,
    ForbiddenRewardType = 8,
    SaveFailed = 9
}

/// <summary>
/// Canonical RewardId builders guaranteeing consistent idempotency keys across all subsystems (GDD §12.1).
/// </summary>
public static class EconomyRewardIds
{
    public static string LevelFirstClear(string levelId) => $"level_{levelId}_first_clear";
    public static string LevelStarUnlock(string levelId, int starNumber) => $"level_{levelId}_star_{starNumber}";
    public static string SectorCompletion(int sectorNumber) => $"sector_{sectorNumber:D2}_completion";
    public static string TutorialSkillGrant(string skillId) => $"tutorial_skill_{skillId}";
    public static string DailyMission(string localDateKey, string missionId) => $"daily_{localDateKey}_{missionId}";
    public static string WeeklyMission(string localMondayKey, string missionId) => $"weekly_{localMondayKey}_{missionId}";
    public static string MonthlyWeeklyMission(string localMonthKey, string missionId) => $"monthly_weekly_{localMonthKey}_{missionId}";
    public static string WeeklyGoal(string localMondayKey, string goalId) => $"weekly_{localMondayKey}_goal_{goalId}";
    public static string SectorMission(string missionId) => $"sector_mission_{missionId}";
    public static string NewPlayerMission(string missionId) => $"newplayer_mission_{missionId}";
    public static string Achievement(string achievementId) => $"achievement_{achievementId}";
    public static string EventTask(string eventId, string taskId) => $"event_{eventId}_task_{taskId}";
    public static string RewardedAdGrant(string localDateKey, string placement, string requestGuid) => $"ad_{localDateKey}_{placement}_{requestGuid}";
    public static string IapTransaction(string storeTransactionId) => $"iap_transaction_{storeTransactionId}";
    public static string IapNonConsumable(string productId) => $"iap_nonconsumable_{productId}";
}

/// <summary>
/// Describes an atomic economy transaction (grant, purchase, IAP fulfillment, or mission/ad claim).
/// Validated, mutated in memory, and persisted in a single <see cref="SaveManager.TrySaveProfile"/> commit.
/// </summary>
public sealed class RewardTransaction
{
    /// <summary>Unique idempotency key recorded in <see cref="ProfileData.claimedRewardIds"/> when non-empty.</summary>
    public string RewardId { get; set; } = string.Empty;

    /// <summary>When true, <see cref="RewardId"/> must be non-empty and not previously claimed.</summary>
    public bool RequireUniqueRewardId { get; set; }

    /// <summary>Additional one-time RewardIds to mark claimed atomically (e.g. multi-star unlock in one clear).</summary>
    public string[] AdditionalRewardIdsToMark { get; set; }

    /// <summary>Optional verified store transaction ID recorded in <see cref="ProfileData.processedIapTransactionIds"/>.</summary>
    public string IapTransactionId { get; set; } = string.Empty;

    /// <summary>Optional currency cost deducted atomically before granting <see cref="Payout"/>.</summary>
    public ShopCostType CostType { get; set; } = ShopCostType.Free;

    /// <summary>Cost amount in Coins or Gems when <see cref="CostType"/> is Coins or Gems.</summary>
    public int CostAmount { get; set; }

    /// <summary>Items granted by this transaction (may be null if transaction only unlocks a flag like Remove Ads).</summary>
    public RewardBundle Payout { get; set; }

    /// <summary>If true, rejects the transaction when any PowerUp item would exceed its inventory cap.</summary>
    public bool RejectIfSkillCapExceeded { get; set; } = true;

    /// <summary>If true, rejects the transaction if any Cosmetic in <see cref="Payout"/> is already owned.</summary>
    public bool RejectIfCosmeticAlreadyOwned { get; set; }

    /// <summary>If true, forbids <see cref="RewardType.Gems"/> in <see cref="Payout"/> (enforced for Rewarded Ads per GDD §7.2).</summary>
    public bool ForbidGemsInPayout { get; set; }

    /// <summary>Optional one-time shop item ID recorded in <see cref="ProfileData.purchasedShopItemIDs"/>.</summary>
    public string OneTimeShopItemId { get; set; } = string.Empty;

    /// <summary>Optional non-consumable store product ID recorded in <see cref="ProfileData.purchasedProductIDs"/>.</summary>
    public string NonConsumableProductId { get; set; } = string.Empty;

    /// <summary>If true, permanently enables <see cref="ProfileData.noAdsPurchased"/> as part of the commit.</summary>
    public bool GrantsRemoveInterstitialAds { get; set; }

    /// <summary>Analytics & audit context tag (e.g. 'LevelFirstClear', 'ShopPurchase', 'MissionClaim').</summary>
    public string Reason { get; set; } = "Unspecified";

    /// <summary>Optional pre-commit mutation callback executed on <see cref="ProfileData"/> inside the atomic snapshot.</summary>
    public Action<ProfileData> OnBeforeSaveMutateProfile { get; set; }
}

/// <summary>
/// Immutable outcome returned by <see cref="RewardService.TryExecuteTransaction"/>.
/// </summary>
public readonly struct RewardTransactionResult
{
    public RewardTransactionStatus Status { get; }
    public string DiagnosticMessage { get; }
    public int NetCoinsDelta { get; }
    public int NetGemsDelta { get; }
    public RewardBundle GrantedBundle { get; }

    public bool Succeeded => Status == RewardTransactionStatus.Success;

    public RewardTransactionResult(
        RewardTransactionStatus status,
        string diagnosticMessage,
        int netCoinsDelta = 0,
        int netGemsDelta = 0,
        RewardBundle grantedBundle = null)
    {
        Status = status;
        DiagnosticMessage = diagnosticMessage ?? string.Empty;
        NetCoinsDelta = netCoinsDelta;
        NetGemsDelta = netGemsDelta;
        GrantedBundle = grantedBundle;
    }
}

