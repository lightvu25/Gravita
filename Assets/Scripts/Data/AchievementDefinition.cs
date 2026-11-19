using UnityEngine;

[CreateAssetMenu(fileName = "NewAchievement", menuName = "Game/Achievement/Achievement Definition")]
public class AchievementDefinition : ScriptableObject
{
    [Header("Identity & Display")]
    [SerializeField] private string achievementId = "ach_01";
    [SerializeField] private Sprite icon;
    [SerializeField] private string title = "New Achievement";
    [TextArea(2, 4)]
    [SerializeField] private string description = "Complete the lifetime achievement objective.";

    [Header("Sector Gating")]
    [Tooltip("Minimum unlocked Sector (1..3) required before this achievement is active/visible.")]
    [Min(1)]
    [SerializeField] private int requiredUnlockedSector = 1;

    [Header("Lifetime Objective")]
    [SerializeField] private MissionService.MissionType type = MissionService.MissionType.CompleteLevels;
    [Min(1)]
    [SerializeField] private int target = 1;

    [Header("Reward (Coin, Gem, PowerUp, or Cosmetic)")]
    [Min(0)]
    [SerializeField] private int rewardAmount = 100;
    [SerializeField] private MissionRewardType rewardType = MissionRewardType.Coins;
    [Tooltip("Power Skill ID granted when rewardType is PowerUp ('breaker', 'freeze', 'teleport').")]
    [SerializeField] private string rewardPowerUpId = PowerUpIds.Breaker;
    [Tooltip("Cosmetic ID granted when rewardType is Cosmetic.")]
    [SerializeField] private string rewardCosmeticId = string.Empty;
    [SerializeField] private Sprite rewardIcon;

    public string AchievementId => !string.IsNullOrWhiteSpace(achievementId) ? achievementId : name;
    public Sprite Icon => icon;
    public string Title => !string.IsNullOrWhiteSpace(title) ? title : name;
    public string Description => description ?? string.Empty;
    public int RequiredUnlockedSector => Mathf.Max(1, requiredUnlockedSector);
    public MissionService.MissionType Type => type;
    public int Target => Mathf.Max(1, target);

    public int RewardAmount => (rewardType == MissionRewardType.Cosmetic || rewardType == MissionRewardType.UnlockSkill)
        ? Mathf.Max(1, rewardAmount)
        : Mathf.Max(0, rewardAmount);
    public MissionRewardType RewardType => rewardType;
    public string RewardPowerUpId => !string.IsNullOrWhiteSpace(rewardPowerUpId) ? rewardPowerUpId : PowerUpIds.Breaker;
    public string RewardCosmeticId => rewardCosmeticId ?? string.Empty;
    public Sprite RewardIcon => rewardIcon;

    public void Configure(
        string id,
        string displayTitle,
        string displayDescription,
        MissionService.MissionType objectiveType,
        int targetValue,
        MissionRewardType rewardKind,
        int rewardValue,
        string powerUpId = "",
        Sprite achievementIcon = null,
        Sprite rewardSprite = null,
        int requiredSector = 1,
        string cosmeticId = "")
    {
        achievementId = id;
        title = displayTitle;
        description = displayDescription;
        type = objectiveType;
        target = Mathf.Max(1, targetValue);
        rewardType = rewardKind;
        rewardAmount = Mathf.Max(0, rewardValue);
        if (!string.IsNullOrWhiteSpace(powerUpId))
            rewardPowerUpId = powerUpId;
        rewardCosmeticId = cosmeticId ?? string.Empty;
        requiredUnlockedSector = Mathf.Max(1, requiredSector);
        if (achievementIcon != null)
            icon = achievementIcon;
        if (rewardSprite != null)
            rewardIcon = rewardSprite;
    }

    public string GetFormattedRewardText()
    {
        switch (rewardType)
        {
            case MissionRewardType.Gems:
                return RewardAmount == 1 ? "1 Gem" : $"{RewardAmount} Gems";
            case MissionRewardType.PowerUp:
                string skillLabel = PowerUpIds.FormatDisplayName(RewardPowerUpId);
                return RewardAmount == 1 ? $"1 {skillLabel} Shard" : $"{RewardAmount}x {skillLabel} Shards";
            case MissionRewardType.Cosmetic:
                return "1 Skin";
            case MissionRewardType.UnlockSkill:
                return $"Unlock {PowerUpIds.FormatDisplayName(RewardPowerUpId)}";
            default:
                return RewardAmount == 1 ? "1 Coin" : $"{RewardAmount} Coins";
        }
    }

    private void OnValidate()
    {
        if (!string.IsNullOrWhiteSpace(achievementId))
            achievementId = achievementId.Trim();
        requiredUnlockedSector = Mathf.Max(1, requiredUnlockedSector);
        target = Mathf.Max(1, target);
        rewardAmount = Mathf.Max(0, rewardAmount);
    }
}

