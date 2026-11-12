using UnityEngine;

public enum MissionStatus
{
    Incomplete = 0,
    Done = 1
}

public enum MissionRewardType
{
    Coins = 0,
    Gems = 1,
    PowerUp = 2,
    Cosmetic = 3
}

/// <summary>
/// Categorizes a mission's lifecycle, progress scope, and UI tab.
/// Weekly = 0 is the default so all existing weekly mission assets deserialize as Weekly automatically.
/// </summary>
public enum MissionCategory
{
    Weekly = 0,
    NewPlayer = 1,
    Daily = 2,
    Achievement = 3,
    Sector = 4
}

[CreateAssetMenu(fileName = "NewMission", menuName = "Game/Mission/Mission Definition")]
public class MissionDefinition : ScriptableObject
{
    [Header("Identity & Display")]
    [SerializeField] private string missionId = "mission_01";
    [SerializeField] private MissionCategory category = MissionCategory.Weekly;
    [SerializeField] private Sprite icon;
    [SerializeField] private string title = "New Mission";
    [TextArea(2, 4)]
    [SerializeField] private string description = "Complete the mission objective.";

    [Header("Sector Gating (GDD §10.1 & §11)")]
    [Tooltip("Minimum unlocked Sector (1..3) required before this mission is active/visible.")]
    [Min(1)]
    [SerializeField] private int requiredUnlockedSector = 1;

    [Header("Weekly Schedule & Points")]
    [Tooltip("Day grouping index (1 = Mon .. 7 = Sun). In GDD §10.2 all Weekly missions are playable throughout the week.")]
    [Range(1, 7)]
    [SerializeField] private int dayIndex = 1;
    [Tooltip("0 = Weekday-scheduled mission (resets weekly on Monday). 1..4 = Week of the month when this mission is added to the cumulative WEEKLY tab (carries over across weeks and resets monthly).")]
    [Range(0, 4)]
    [SerializeField] private int weekOfMonth = 0;
    [Tooltip("Weekly mission points granted toward the weekly goal progression bar when claimed.")]
    [Min(0)]
    [SerializeField] private int missionPoints = 25;

    [Header("Objective")]
    [SerializeField] private MissionService.MissionType type = MissionService.MissionType.CompleteLevels;
    [Min(1)]
    [SerializeField] private int target = 1;

    [Header("Reward (Coin, Gem, PowerUp, or Cosmetic)")]
    [Min(0)]
    [SerializeField] private int rewardAmount = 50;
    [SerializeField] private MissionRewardType rewardType = MissionRewardType.Coins;
    [Tooltip("Power Skill ID granted when rewardType is PowerUp ('breaker', 'freeze', 'teleport').")]
    [SerializeField] private string rewardPowerUpId = PowerUpIds.Breaker;
    [Tooltip("Cosmetic ID granted when rewardType is Cosmetic.")]
    [SerializeField] private string rewardCosmeticId = string.Empty;
    [SerializeField] private Sprite rewardIcon;

    [Header("Status")]
    [Tooltip("Legacy inspector status field kept for asset serialization compatibility.")]
    [SerializeField] private MissionStatus status = MissionStatus.Incomplete;

    public string MissionId => !string.IsNullOrWhiteSpace(missionId) ? missionId : name;
    public MissionCategory Category => category;
    public Sprite Icon => icon;
    public string Title => !string.IsNullOrWhiteSpace(title) ? title : name;
    public string Description => description ?? string.Empty;
    public int RequiredUnlockedSector => Mathf.Max(1, requiredUnlockedSector);
    public int DayIndex => Mathf.Clamp(dayIndex, 1, 7);
    public int WeekOfMonth => Mathf.Clamp(weekOfMonth, 0, 4);
    public bool IsMonthlyCumulativeWeekly => category == MissionCategory.Weekly && weekOfMonth >= 1;
    public int MissionPoints => Mathf.Max(0, missionPoints);
    public MissionService.MissionType Type => type;
    public int Target => Mathf.Max(1, target);

    public int RewardAmount => rewardType == MissionRewardType.Cosmetic ? Mathf.Max(1, rewardAmount) : Mathf.Max(0, rewardAmount);
    public MissionRewardType RewardType => rewardType;
    public string RewardPowerUpId => !string.IsNullOrWhiteSpace(rewardPowerUpId) ? rewardPowerUpId : PowerUpIds.Breaker;
    public string RewardCosmeticId => rewardCosmeticId ?? string.Empty;
    public Sprite RewardIcon => rewardIcon;

    public int RewardCoins => rewardType == MissionRewardType.Coins ? RewardAmount : 0;
    public int RewardGems => rewardType == MissionRewardType.Gems ? RewardAmount : 0;
    public int RewardPowerUps => rewardType == MissionRewardType.PowerUp ? RewardAmount : 0;

    public MissionStatus Status
    {
        get => status;
        set => status = value;
    }

    public void SetStatus(MissionStatus newStatus)
    {
        status = newStatus;
    }

    public void Configure(
        string id,
        MissionCategory missionCategory,
        string displayTitle,
        string displayDescription,
        MissionService.MissionType missionType,
        int targetValue,
        MissionRewardType rewardKind,
        int rewardValue,
        string powerUpId = "",
        int day = 1,
        int weeklyPoints = 0,
        Sprite missionIcon = null,
        Sprite rewardSprite = null,
        int requiredSector = 1,
        string cosmeticId = "",
        int weekOfMonthIndex = 0)
    {
        missionId = id;
        category = missionCategory;
        title = displayTitle;
        description = displayDescription;
        type = missionType;
        target = Mathf.Max(1, targetValue);
        rewardType = rewardKind;
        rewardAmount = Mathf.Max(0, rewardValue);
        if (!string.IsNullOrWhiteSpace(powerUpId))
            rewardPowerUpId = powerUpId;
        rewardCosmeticId = cosmeticId ?? string.Empty;
        requiredUnlockedSector = Mathf.Max(1, requiredSector);
        dayIndex = Mathf.Clamp(day, 1, 7);
        weekOfMonth = Mathf.Clamp(weekOfMonthIndex, 0, 4);
        missionPoints = Mathf.Max(0, weeklyPoints);
        if (missionIcon != null)
            icon = missionIcon;
        if (rewardSprite != null)
            rewardIcon = rewardSprite;
        status = MissionStatus.Incomplete;
    }

    public string GetFormattedRewardText()
    {
        string rewardPart;
        switch (rewardType)
        {
            case MissionRewardType.Gems:
                rewardPart = RewardAmount == 1 ? "1 Gem" : $"{RewardAmount} Gems";
                break;
            case MissionRewardType.PowerUp:
                rewardPart = RewardAmount == 1 ? $"1 {RewardPowerUpId}" : $"{RewardAmount}x {RewardPowerUpId}";
                break;
            case MissionRewardType.Cosmetic:
                rewardPart = "1 Skin";
                break;
            default:
                rewardPart = RewardAmount == 1 ? "1 Coin" : $"{RewardAmount} Coins";
                break;
        }

        return rewardPart;
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(missionId))
        {
            missionId = name;
        }
        requiredUnlockedSector = Mathf.Max(1, requiredUnlockedSector);
        dayIndex = Mathf.Clamp(dayIndex, 1, 7);
        weekOfMonth = Mathf.Clamp(weekOfMonth, 0, 4);
        missionPoints = Mathf.Max(0, missionPoints);
        target = Mathf.Max(1, target);
        rewardAmount = Mathf.Max(0, rewardAmount);
    }
}
