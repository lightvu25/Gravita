using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WorldData
{
    public int worldNumber = 1;
    public string worldName = "Sector 1";
    [TextArea(2, 3)]
    public string description;

    [Header("Unlock Requirements")]
    [Tooltip("Minimum number of completed levels required to unlock this world. 0 = unlocked by default (or sequential after previous Sector).")]
    [Min(0)]
    public int requiredCompletedLevelsToUnlock = 0;

    [Tooltip("Legacy Player Level lock (bypassed in standard Campaign per Economy GDD §2.1).")]
    [Min(1)]
    public int requiredPlayerLevelToUnlock = 1;

    [Tooltip("Legacy EXP lock (bypassed in standard Campaign per Economy GDD §2.1).")]
    [Min(0)]
    public int requiredExpToUnlock = 0;

    [Header("Levels")]
    [Tooltip("Ordered list of levels in this Sector (GDD baseline: 10 levels per Sector across 3 Sectors).")]
    public LevelData[] levels = new LevelData[0];
}

/// <summary>
/// ScriptableObject defining the data-driven campaign structure across Sectors (Worlds), levels, and player EXP progression.
/// </summary>
[CreateAssetMenu(menuName = "Game/Campaign/Campaign Database", fileName = "CampaignDatabase")]
public class CampaignDatabase : ScriptableObject
{
    [Header("Player Level & EXP Progression")]
    [Tooltip("ScriptableObject defining EXP thresholds per player level. If unassigned, uses the inline progression settings below.")]
    [SerializeField] private PlayerExpConfig expConfig;

    [Tooltip("Fallback EXP required for Level 1 -> 2 when no PlayerExpConfig asset is assigned.")]
    [Min(1)]
    [SerializeField] private int baseExpPerLevel = PlayerExpConfig.DefaultBaseExpPerLevel;

    [Tooltip("Fallback additional EXP required per subsequent level when no PlayerExpConfig asset is assigned.")]
    [Min(0)]
    [SerializeField] private int expIncreasePerLevel = PlayerExpConfig.DefaultExpIncreasePerLevel;

    [Tooltip("Fallback maximum player level when no PlayerExpConfig asset is assigned.")]
    [Min(1)]
    [SerializeField] private int maxPlayerLevel = PlayerExpConfig.DefaultMaxPlayerLevel;

    [Header("Campaign Sectors (Worlds)")]
    [Tooltip("Configured Sectors in this campaign (Sector 1..3).")]
    [SerializeField] private WorldData[] worlds = new WorldData[0];

    public PlayerExpConfig ExpConfig => expConfig;
    public IReadOnlyList<WorldData> Worlds => worlds;

    public void SetWorlds(WorldData[] configuredWorlds)
    {
        worlds = configuredWorlds ?? Array.Empty<WorldData>();
    }

    /// <summary>
    /// Returns the EXP required to advance from <paramref name="level"/> to <c>level + 1</c>.
    /// </summary>
    public int GetExpRequiredForLevel(int level)
    {
        if (expConfig != null)
            return expConfig.GetExpRequiredForLevel(level);

        return PlayerExpConfig.GetExpRequiredForLevel(
            level,
            maxPlayerLevel,
            baseExpPerLevel,
            expIncreasePerLevel);
    }

    /// <summary>
    /// Evaluates player level and EXP bar progress from total cumulative EXP.
    /// </summary>
    public void EvaluatePlayerExp(
        int totalExp,
        out int playerLevel,
        out int currentLevelExp,
        out int expForNextLevel,
        out float normalizedProgress)
    {
        if (expConfig != null)
        {
            expConfig.EvaluateTotalExp(
                totalExp,
                out playerLevel,
                out currentLevelExp,
                out expForNextLevel,
                out normalizedProgress);
            return;
        }

        PlayerExpConfig.EvaluateTotalExp(
            totalExp,
            maxPlayerLevel,
            baseExpPerLevel,
            expIncreasePerLevel,
            1f,
            null,
            out playerLevel,
            out currentLevelExp,
            out expForNextLevel,
            out normalizedProgress);
    }

    public LevelData GetLevelById(string levelId)
    {
        if (string.IsNullOrEmpty(levelId) || worlds == null) return null;

        foreach (var world in worlds)
        {
            if (world?.levels == null) continue;
            foreach (var lvl in world.levels)
            {
                if (lvl != null && lvl.LevelId == levelId)
                    return lvl;
            }
        }
        return null;
    }

    public LevelData GetNextLevel(LevelData currentLevel)
    {
        if (currentLevel == null || worlds == null) return null;

        bool foundCurrent = false;
        foreach (var world in worlds)
        {
            if (world?.levels == null) continue;
            for (int i = 0; i < world.levels.Length; i++)
            {
                var lvl = world.levels[i];
                if (foundCurrent && lvl != null)
                    return lvl;

                if (lvl != null && (lvl == currentLevel || lvl.LevelId == currentLevel.LevelId))
                    foundCurrent = true;
            }
        }
        return null;
    }

    public LevelData GetFirstLevel()
    {
        if (worlds != null)
        {
            for (int w = 0; w < worlds.Length; w++)
            {
                var world = worlds[w];
                if (world?.levels == null) continue;
                for (int i = 0; i < world.levels.Length; i++)
                {
                    if (world.levels[i] != null)
                        return world.levels[i];
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Finds the <see cref="WorldData"/> (Sector) containing <paramref name="level"/> and outputs its 1-based Sector number.
    /// Falls back to <see cref="LevelData.World"/> when not registered in <see cref="worlds"/>.
    /// </summary>
    public WorldData GetWorldForLevel(LevelData level, out int oneBasedSectorNumber)
    {
        oneBasedSectorNumber = level != null ? Mathf.Max(1, level.World) : 1;
        if (level == null || worlds == null)
            return null;

        for (int w = 0; w < worlds.Length; w++)
        {
            WorldData world = worlds[w];
            if (world?.levels == null) continue;

            for (int i = 0; i < world.levels.Length; i++)
            {
                LevelData candidate = world.levels[i];
                if (candidate != null && (candidate == level || candidate.LevelId == level.LevelId))
                {
                    oneBasedSectorNumber = w + 1;
                    return world;
                }
            }
        }

        return null;
    }

    public int GetSectorNumberForLevel(LevelData level)
    {
        GetWorldForLevel(level, out int sectorNumber);
        return sectorNumber;
    }

    public int GetOneBasedWorldIndex(WorldData targetWorld)
    {
        if (targetWorld == null || worlds == null)
            return 1;

        for (int i = 0; i < worlds.Length; i++)
        {
            if (worlds[i] == targetWorld)
                return i + 1;
        }

        return Mathf.Max(1, targetWorld.worldNumber);
    }

    public static bool AreAllLevelsInWorldCompleted(WorldData world, ProfileData profile)
    {
        if (world?.levels == null || profile?.completedLevelIDs == null)
            return false;

        int validLevelCount = 0;
        for (int i = 0; i < world.levels.Length; i++)
        {
            LevelData lvl = world.levels[i];
            if (lvl == null || string.IsNullOrEmpty(lvl.LevelId))
                continue;

            validLevelCount++;
            if (!profile.completedLevelIDs.Contains(lvl.LevelId))
                return false;
        }

        return validLevelCount > 0;
    }

    public bool AreAllCampaignLevelsCompleted(ProfileData profile)
    {
        if (worlds == null || worlds.Length == 0 || profile?.completedLevelIDs == null)
            return false;

        int totalValidLevels = 0;
        for (int w = 0; w < worlds.Length; w++)
        {
            WorldData world = worlds[w];
            if (world?.levels == null) continue;

            for (int i = 0; i < world.levels.Length; i++)
            {
                LevelData lvl = world.levels[i];
                if (lvl == null || string.IsNullOrEmpty(lvl.LevelId))
                    continue;

                totalValidLevels++;
                if (!profile.completedLevelIDs.Contains(lvl.LevelId))
                    return false;
            }
        }

        return totalValidLevels > 0;
    }

    /// <summary>
    /// Returns the 1-based sequential index of the level across the campaign, or 0 if not found.
    /// </summary>
    public int GetLevelNumber(LevelData targetLevel)
    {
        if (targetLevel == null || worlds == null) return 0;

        int number = 0;
        foreach (var world in worlds)
        {
            if (world?.levels == null) continue;
            foreach (var lvl in world.levels)
            {
                if (lvl == null) continue;
                number++;
                if (lvl == targetLevel || lvl.LevelId == targetLevel.LevelId) return number;
            }
        }
        return 0;
    }
}
