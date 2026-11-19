using System;
using System.Collections.Generic;
using UnityEngine;

public enum RunnerSpawnCategory
{
    Block  = 0,
    Enemy  = 1,
    Event  = 2,
    Pickup = 3,
    Any    = 4,
}

[Serializable]
public struct RunnerSpawnEntry
{
    [Tooltip("Logical identifier for analytics/debugging.")]
    public string entryId;

    [Tooltip("Category this prefab belongs to.")]
    public RunnerSpawnCategory category;

    [Tooltip("Prefab to spawn (or pool) when this entry is rolled.")]
    public GameObject prefab;

    [Range(0f, 1f)]
    [Tooltip("Independent probability (0..1) that this prefab/event is eligible in the room.")]
    public float spawnChance;

    [Min(0f)]
    [Tooltip("Relative weight when selecting among eligible entries in this category.")]
    public float weight;

    [Tooltip("Inclusive min/max instances of THIS entry allowed per room.")]
    public Vector2Int countRange;

    [Min(1)]
    [Tooltip("Minimum 1-based room index before this entry can appear.")]
    public int minRoomIndex;

    [Min(0)]
    [Tooltip("Maximum 1-based room index (0 = no upper cap).")]
    public int maxRoomIndex;

    [Tooltip("True for solid/cracked obstacles that alter Gravita slide stopping cells (triggers solvability check).")]
    public bool blocksSlideTraversal;

    public bool IsEligibleForRoom(int roomNumber)
    {
        if (prefab == null)
            return false;

        int minRoom = Mathf.Max(1, minRoomIndex);
        if (roomNumber < minRoom)
            return false;

        if (maxRoomIndex > 0 && roomNumber > maxRoomIndex)
            return false;

        return weight > 0f && spawnChance > 0f && ClampedMaxCount > 0;
    }

    public int ClampedMinCount => Mathf.Clamp(Mathf.Min(countRange.x, countRange.y), 0, ClampedMaxCount);
    public int ClampedMaxCount => Mathf.Max(0, Mathf.Max(countRange.x, countRange.y));

    public void Sanitize()
    {
        spawnChance = Mathf.Clamp01(spawnChance);
        weight = Mathf.Max(0f, weight);
        minRoomIndex = Mathf.Max(1, minRoomIndex);
        maxRoomIndex = Mathf.Max(0, maxRoomIndex);

        int min = Mathf.Max(0, countRange.x);
        int max = Mathf.Max(min, countRange.y);
        countRange = new Vector2Int(min, max);
    }
}

/// <summary>
/// Two-tier data-driven configuration for <see cref="RuntimeLevelPopulator"/>:
///   Tier 1: Room-wide occurrence probabilities and total count ranges per category (Block / Enemy / Event / Pickup).
///   Tier 2: Per-prefab / per-event probability, weight, per-room count range, and room-depth unlock gating.
/// </summary>
[CreateAssetMenu(fileName = "RunnerPopulationProfile", menuName = "Gravita/Runner/Population Profile")]
public class RunnerPopulationProfile : ScriptableObject
{
    [Header("Tier 1 — Blocks (Obstacles & Interactive Tiles)")]
    public bool enableBlocks = true;
    [Range(0f, 1f)] public float roomBlockTriggerChance = 0.85f;
    public Vector2Int roomBlocksRange = new Vector2Int(1, 4);

    [Header("Tier 1 — Enemies & Hazards")]
    public bool enableEnemies = true;
    [Range(0f, 1f)] public float roomEnemyTriggerChance = 0.80f;
    [Range(0f, 0.25f)] public float enemyChanceBonusPerRoom = 0.03f;
    public Vector2Int roomEnemiesRange = new Vector2Int(1, 3);
    [Min(1)] public int extraEnemyEveryNRooms = 5;
    [Min(0)] public int maxExtraEnemies = 3;

    [Header("Tier 1 — Room Events")]
    public bool enableEvents = true;
    [Range(0f, 1f)] public float roomEventTriggerChance = 0.45f;
    public Vector2Int roomEventsRange = new Vector2Int(1, 2);

    [Header("Tier 1 — Pickups (Coins)")]
    public bool enablePickups = true;
    public Vector2Int roomPickupsRange = new Vector2Int(4, 8);

    [Header("Safety & Solvability")]
    [Min(1)] public int safeRadiusAroundPortals = 2;
    [Tooltip("Checks MazeSlideGraph reachability & zero soft-lock before keeping a blocking obstacle.")]
    public bool enforceSlideSolvability = true;
    [Range(1, 4)] public int maxDronePatrolSpan = 4;

    [Header("Tier 2 — Per-Prefab / Per-Event Pools")]
    public List<RunnerSpawnEntry> blockEntries = new List<RunnerSpawnEntry>();
    public List<RunnerSpawnEntry> enemyEntries = new List<RunnerSpawnEntry>();
    public List<RunnerSpawnEntry> eventEntries = new List<RunnerSpawnEntry>();
    public List<RunnerSpawnEntry> pickupEntries = new List<RunnerSpawnEntry>();

    private void OnValidate()
    {
        Sanitize();
    }

    public void Sanitize()
    {
        roomBlockTriggerChance = Mathf.Clamp01(roomBlockTriggerChance);
        roomEnemyTriggerChance = Mathf.Clamp01(roomEnemyTriggerChance);
        enemyChanceBonusPerRoom = Mathf.Clamp(enemyChanceBonusPerRoom, 0f, 0.25f);
        roomEventTriggerChance = Mathf.Clamp01(roomEventTriggerChance);

        roomBlocksRange = ClampRange(roomBlocksRange);
        roomEnemiesRange = ClampRange(roomEnemiesRange);
        roomEventsRange = ClampRange(roomEventsRange);
        roomPickupsRange = ClampRange(roomPickupsRange);

        extraEnemyEveryNRooms = Mathf.Max(1, extraEnemyEveryNRooms);
        maxExtraEnemies = Mathf.Max(0, maxExtraEnemies);
        safeRadiusAroundPortals = Mathf.Max(1, safeRadiusAroundPortals);
        maxDronePatrolSpan = Mathf.Clamp(maxDronePatrolSpan, 1, 8);

        SanitizePool(blockEntries, RunnerSpawnCategory.Block);
        SanitizePool(enemyEntries, RunnerSpawnCategory.Enemy);
        SanitizePool(eventEntries, RunnerSpawnCategory.Event);
        SanitizePool(pickupEntries, RunnerSpawnCategory.Pickup);
    }

    public List<RunnerSpawnEntry> GetEntriesForCategory(RunnerSpawnCategory category)
    {
        return category switch
        {
            RunnerSpawnCategory.Block  => blockEntries,
            RunnerSpawnCategory.Enemy  => enemyEntries,
            RunnerSpawnCategory.Event  => eventEntries,
            RunnerSpawnCategory.Pickup => pickupEntries,
            _                          => null,
        };
    }

    private static Vector2Int ClampRange(Vector2Int range)
    {
        int min = Mathf.Max(0, range.x);
        int max = Mathf.Max(min, range.y);
        return new Vector2Int(min, max);
    }

    private static void SanitizePool(List<RunnerSpawnEntry> pool, RunnerSpawnCategory expectedCategory)
    {
        if (pool == null)
            return;

        for (int i = 0; i < pool.Count; i++)
        {
            RunnerSpawnEntry entry = pool[i];
            if (entry.category == RunnerSpawnCategory.Any)
                entry.category = expectedCategory;
            entry.Sanitize();
            pool[i] = entry;
        }
    }
}
