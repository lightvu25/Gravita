using UnityEngine;

/// <summary>
/// ScriptableObject defining the player's Level and EXP progression curve.
/// Configures how much EXP is required to advance through player levels.
/// Create via: Assets -> Create -> Game -> Progression -> Player EXP Config
/// </summary>
[CreateAssetMenu(fileName = "PlayerExpConfig", menuName = "Game/Progression/Player EXP Config")]
public class PlayerExpConfig : ScriptableObject
{
    public const int DefaultBaseExpPerLevel = 100;
    public const int DefaultExpIncreasePerLevel = 50;
    public const int DefaultMaxPlayerLevel = 99;

    [Header("Level Cap")]
    [Tooltip("Maximum level the player can reach.")]
    [Min(1)]
    public int maxPlayerLevel = DefaultMaxPlayerLevel;

    [Header("Formula Progression")]
    [Tooltip("EXP required to advance from Level 1 to Level 2.")]
    [Min(1)]
    public int baseExpPerLevel = DefaultBaseExpPerLevel;

    [Tooltip("Additional EXP added to the requirement for each subsequent level.")]
    [Min(0)]
    public int expIncreasePerLevel = DefaultExpIncreasePerLevel;

    [Tooltip("Optional multiplier applied per level (1.0 = purely linear growth).")]
    [Min(1f)]
    public float expGrowthMultiplier = 1f;

    [Header("Custom Per-Level Overrides (Optional)")]
    [Tooltip("Optional explicit EXP required for each level (index 0 = Level 1 -> 2). If empty or out of range, the formula above is used.")]
    public int[] customExpPerLevel = new int[0];

    /// <summary>
    /// Returns the EXP required to advance from <paramref name="level"/> to <c>level + 1</c>.
    /// </summary>
    public int GetExpRequiredForLevel(int level)
    {
        return GetExpRequiredForLevel(
            level,
            maxPlayerLevel,
            baseExpPerLevel,
            expIncreasePerLevel,
            expGrowthMultiplier,
            customExpPerLevel);
    }

    /// <summary>
    /// Evaluates the player's 1-based level, current progress within that level,
    /// EXP needed for the next level, and normalized progress (0..1) from total cumulative EXP.
    /// </summary>
    public void EvaluateTotalExp(
        int totalExp,
        out int playerLevel,
        out int currentLevelExp,
        out int expForNextLevel,
        out float normalizedProgress)
    {
        EvaluateTotalExp(
            totalExp,
            maxPlayerLevel,
            baseExpPerLevel,
            expIncreasePerLevel,
            expGrowthMultiplier,
            customExpPerLevel,
            out playerLevel,
            out currentLevelExp,
            out expForNextLevel,
            out normalizedProgress);
    }

    /// <summary>
    /// Static evaluation helper so ProgressionManager can evaluate EXP even if no ScriptableObject asset is assigned.
    /// </summary>
    public static int GetExpRequiredForLevel(
        int level,
        int maxLevel,
        int baseExp,
        int expIncrease,
        float growthMultiplier = 1f,
        int[] customThresholds = null)
    {
        int clampedLevel = Mathf.Max(1, level);
        int clampedMax = Mathf.Max(1, maxLevel);
        if (clampedLevel >= clampedMax)
            return 0;

        int index = clampedLevel - 1;
        if (customThresholds != null && index >= 0 && index < customThresholds.Length && customThresholds[index] > 0)
            return customThresholds[index];

        float raw = (Mathf.Max(1, baseExp) + (index * Mathf.Max(0, expIncrease)))
                    * Mathf.Pow(Mathf.Max(1f, growthMultiplier), index);
        return Mathf.Max(1, Mathf.RoundToInt(raw));
    }

    /// <summary>
    /// Static evaluation helper that computes level and bar progress from cumulative EXP.
    /// </summary>
    public static void EvaluateTotalExp(
        int totalExp,
        int maxLevel,
        int baseExp,
        int expIncrease,
        float growthMultiplier,
        int[] customThresholds,
        out int playerLevel,
        out int currentLevelExp,
        out int expForNextLevel,
        out float normalizedProgress)
    {
        int remaining = Mathf.Max(0, totalExp);
        int clampedMax = Mathf.Max(1, maxLevel);
        int level = 1;

        while (level < clampedMax)
        {
            int required = GetExpRequiredForLevel(level, clampedMax, baseExp, expIncrease, growthMultiplier, customThresholds);
            if (required <= 0 || remaining < required)
            {
                playerLevel = level;
                currentLevelExp = remaining;
                expForNextLevel = Mathf.Max(1, required);
                normalizedProgress = Mathf.Clamp01((float)currentLevelExp / expForNextLevel);
                return;
            }

            remaining -= required;
            level++;
        }

        // Max level reached
        playerLevel = clampedMax;
        int lastReq = GetExpRequiredForLevel(
            Mathf.Max(1, clampedMax - 1),
            clampedMax,
            baseExp,
            expIncrease,
            growthMultiplier,
            customThresholds);
        expForNextLevel = Mathf.Max(1, lastReq);
        currentLevelExp = expForNextLevel;
        normalizedProgress = 1f;
    }

    /// <summary>
    /// Returns the minimum total cumulative EXP required to reach <paramref name="targetLevel"/>.
    /// </summary>
    public int GetTotalExpForLevel(int targetLevel)
    {
        int clampedTarget = Mathf.Clamp(targetLevel, 1, Mathf.Max(1, maxPlayerLevel));
        int total = 0;
        for (int lvl = 1; lvl < clampedTarget; lvl++)
        {
            total += GetExpRequiredForLevel(lvl);
        }
        return total;
    }
}
