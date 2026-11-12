using UnityEngine;

/// <summary>
/// ScriptableObject that defines a single level.
///
/// Create via: Assets → Create → Game → Level Data
/// </summary>
[CreateAssetMenu(fileName = "New LevelData", menuName = "Game/Level Data")]
public class LevelData : ScriptableObject
{
    // ─────────────────────────────────────────────────────────────
    //  Identity
    // ─────────────────────────────────────────────────────────────

    [Header("Identity")]
    [Tooltip("Unique string identifier for this level (e.g. '001_Tutorial'). Used for save-data and analytics.")]
    public string levelId;

    [Tooltip("Display name shown in the level-select UI and debug logs.")]
    public string displayName;

    [Tooltip("Designer-facing difficulty rating.")]
    public LevelDifficulty difficulty = LevelDifficulty.Tutorial;

    [Tooltip("World index this level belongs to (displayed as 'World X' in the HUD).")]
    [Min(1)]
    public int world = 1;

    // ─────────────────────────────────────────────────────────────
    //  Layout
    // ─────────────────────────────────────────────────────────────

    [Header("Layout")]
    [Tooltip("The prefab containing the maze layout. Must have a LevelRoot component on its root GameObject.")]
    public GameObject layoutPrefab;

    // ─────────────────────────────────────────────────────────────
    //  Objectives
    // ─────────────────────────────────────────────────────────────

    [Header("Objectives")]
    [Tooltip("The primary win condition for this level.")]
    public ObjectiveDefinition primaryObjective;

    [Tooltip("Optional bonus objectives. Completing them grants extra stars/rewards.")]
    public ObjectiveDefinition[] optionalObjectives;

    // ─────────────────────────────────────────────────────────────
    //  Rewards
    // ─────────────────────────────────────────────────────────────

    [Header("Rewards")]
    [Tooltip("Base coin amount granted on level completion (before star bonus).")]
    [Min(0)]
    public int baseCoinReward = 10;

    [Tooltip("Coins added per optional objective completed.")]
    [Min(0)]
    public int optionalCoinBonus = 5;

    [Header("EXP Rewards")]
    [Tooltip("Base player EXP granted when completing this level for the first time.")]
    [Min(0)]
    public int baseExpReward = 50;

    [Tooltip("Bonus player EXP added per star earned on first completion.")]
    [Min(0)]
    public int expBonusPerStar = 15;

    [Tooltip("Player EXP granted when replaying and completing this level.")]
    [Min(0)]
    public int replayExpReward = 20;

    // ─────────────────────────────────────────────────────────────
    //  Convenience Accessors
    // ─────────────────────────────────────────────────────────────

    /// <summary>Returns the level's unique ID, falling back to the asset name if unset.</summary>
    public string LevelId => string.IsNullOrEmpty(levelId) ? name : levelId;

    /// <summary>Returns the level's display name, falling back to the asset name if unset.</summary>
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;

    /// <summary>Returns the layout prefab assigned to this level.</summary>
    public GameObject LayoutPrefab => layoutPrefab;

    /// <summary>Returns the world index this level belongs to.</summary>
    public int World => world;

    /// <summary>Returns the primary objective, or null if none is defined.</summary>
    public ObjectiveDefinition PrimaryObjective => primaryObjective;

    /// <summary>Returns the optional objectives array (may be null).</summary>
    public ObjectiveDefinition[] OptionalObjectives => optionalObjectives;

    /// <summary>Human-readable summary for editor debugging.</summary>
    public override string ToString() => $"[LevelData] {DisplayName} ({LevelId})";
}
