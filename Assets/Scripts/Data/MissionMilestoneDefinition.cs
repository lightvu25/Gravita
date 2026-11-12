using UnityEngine;

/// <summary>
/// ScriptableObject defining a concrete Weekly Mission Milestone along the weekly progression bar.
/// Progress is measured strictly by the concrete count of completed/claimed weekly missions
/// (<see cref="RequiredCompletedMissions"/>) rather than arbitrary test points.
/// </summary>
[CreateAssetMenu(fileName = "Milestone_Week_01", menuName = "Game/Mission/Mission Milestone Definition")]
public class MissionMilestoneDefinition : ScriptableObject
{
    [Header("Identity & Concrete Task Threshold")]
    [SerializeField] private string milestoneId = "weekly_milestone_01";
    [SerializeField] private string title = "3 Weekly Tasks";
    [Tooltip("Concrete number of completed weekly missions required to unlock this milestone reward (e.g. 3, 7, 10, 14).")]
    [Min(1)]
    [SerializeField] private int requiredCompletedMissions = 3;

    [Header("Reward (Coin, Gem, PowerUp, or Cosmetic)")]
    [Min(1)]
    [SerializeField] private int rewardAmount = 100;
    [SerializeField] private MissionRewardType rewardType = MissionRewardType.Coins;
    [Tooltip("Power Skill ID granted when rewardType is PowerUp ('breaker', 'freeze', 'teleport').")]
    [SerializeField] private string powerUpId = PowerUpIds.Breaker;
    [Tooltip("Cosmetic ID granted when rewardType is Cosmetic.")]
    [SerializeField] private string cosmeticId = string.Empty;

    [Header("State Sprites (Rendered at 100% Opacity)")]
    [Tooltip("Sprite displayed when RequiredCompletedMissions has not been reached yet.")]
    [SerializeField] private Sprite incompleteSprite;
    [Tooltip("Sprite displayed when RequiredCompletedMissions is reached and ready to claim.")]
    [SerializeField] private Sprite completeSprite;
    [Tooltip("Optional sprite displayed after the milestone reward has been claimed (falls back to completeSprite).")]
    [SerializeField] private Sprite claimedSprite;
    [Tooltip("Optional icon representing the reward payload.")]
    [SerializeField] private Sprite rewardIcon;

    public string MilestoneId => !string.IsNullOrWhiteSpace(milestoneId) ? milestoneId : name;
    public string Title => !string.IsNullOrWhiteSpace(title) ? title : $"{RequiredCompletedMissions} Tasks";
    public int RequiredCompletedMissions => Mathf.Max(1, requiredCompletedMissions);
    public int RewardAmount => Mathf.Max(1, rewardAmount);
    public MissionRewardType RewardType => rewardType;
    public string PowerUpId => !string.IsNullOrWhiteSpace(powerUpId) ? powerUpId : PowerUpIds.Breaker;
    public string CosmeticId => cosmeticId ?? string.Empty;
    public Sprite IncompleteSprite => incompleteSprite;
    public Sprite CompleteSprite => completeSprite != null ? completeSprite : incompleteSprite;
    public Sprite ClaimedSprite => claimedSprite != null ? claimedSprite : CompleteSprite;
    public Sprite RewardIcon => rewardIcon;

    public void Configure(
        string id,
        string displayTitle,
        int requiredTasks,
        MissionRewardType rewardKind,
        int rewardValue,
        string rewardSkillId = "",
        string rewardSkinId = "",
        Sprite incomplete = null,
        Sprite complete = null,
        Sprite claimed = null,
        Sprite icon = null)
    {
        milestoneId = id;
        title = displayTitle;
        requiredCompletedMissions = Mathf.Max(1, requiredTasks);
        rewardType = rewardKind;
        rewardAmount = Mathf.Max(1, rewardValue);
        if (!string.IsNullOrWhiteSpace(rewardSkillId))
            powerUpId = rewardSkillId;
        cosmeticId = rewardSkinId ?? string.Empty;
        if (incomplete != null)
            incompleteSprite = incomplete;
        if (complete != null)
            completeSprite = complete;
        if (claimed != null)
            claimedSprite = claimed;
        if (icon != null)
            rewardIcon = icon;
    }

    public string GetFormattedRewardText()
    {
        switch (rewardType)
        {
            case MissionRewardType.Gems:
                return RewardAmount == 1 ? "1 Gem" : $"{RewardAmount} Gems";
            case MissionRewardType.PowerUp:
                return RewardAmount == 1 ? $"1 {PowerUpId}" : $"{RewardAmount}x {PowerUpId}";
            case MissionRewardType.Cosmetic:
                return "1 Skin";
            default:
                return RewardAmount == 1 ? "1 Coin" : $"{RewardAmount} Coins";
        }
    }

    private void OnValidate()
    {
        if (string.IsNullOrWhiteSpace(milestoneId))
            milestoneId = name;

        requiredCompletedMissions = Mathf.Max(1, requiredCompletedMissions);
        rewardAmount = Mathf.Max(1, rewardAmount);
    }
}
