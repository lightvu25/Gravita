using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

public enum RunnerModeKind
{
    Endless = 0,
    Rooms10 = 1,
    Rooms20 = 2,
}

public enum RunnerRoomSourcePolicy
{
    HybridPreferPoolThenProcedural = 0,
    PrefabPoolOnly                 = 1,
    ProceduralOnly                 = 2,
}

[Serializable]
public struct RunnerRoomTemplateEntry
{
    [Tooltip("Logical room identifier (must contain RunnerRoomController, StartNode, GoalNode).")]
    public string roomId;

    [Tooltip("Authored large-map prefab to pull from when rolling room templates.")]
    public GameObject roomPrefab;

    [Min(0.01f)]
    [Tooltip("Weighted probability relative to other room templates.")]
    public float weight;

    [Min(1)]
    [Tooltip("First room index where this template becomes eligible.")]
    public int minRoomIndex;

    [Tooltip("Optional population override specific to this room prefab.")]
    public RunnerPopulationProfile populationOverride;

    public bool IsEligible(int roomNumber)
    {
        return roomPrefab != null && weight > 0f && roomNumber >= Mathf.Max(1, minRoomIndex);
    }
}

/// <summary>
/// Master configuration asset for the continuous Tomb-of-the-Mask-style Maze Runner mode.
/// Holds room pool, procedural MazeGenerator fallback settings, population profile,
/// active stitching window parameters, and end-of-run economy rewards.
/// </summary>
[CreateAssetMenu(fileName = "RunnerModeConfig", menuName = "Gravita/Runner/Mode Config")]
public class RunnerModeConfig : ScriptableObject
{
    [Header("Mode & Room Source")]
    public RunnerModeKind defaultMode = RunnerModeKind.Endless;
    public RunnerRoomSourcePolicy roomSourcePolicy = RunnerRoomSourcePolicy.HybridPreferPoolThenProcedural;

    [Range(0f, 1f)]
    [Tooltip("In Hybrid mode when authored templates exist, chance to still inject a fresh MazeGenerator room.")]
    public float proceduralMixChanceWhenHybrid = 0.35f;

    [Tooltip("Optional authored large room prefabs. When empty in Hybrid mode, 100% procedural rooms are used.")]
    public List<RunnerRoomTemplateEntry> roomTemplates = new List<RunnerRoomTemplateEntry>();

    [Header("Procedural Large Room Generation")]
    [Tooltip("Shell prefab containing LevelRoot, RunnerRoomController, WallTilemap, BackgroundTilemap, StartNode, GoalNode.")]
    public GameObject proceduralRoomShellPrefab;
    public MazePatternLibrary patternLibrary;
    public MazeDifficultyProfile earlyDifficultyProfile;
    public MazeDifficultyProfile midDifficultyProfile;
    public MazeDifficultyProfile lateDifficultyProfile;
    public MazeSizePreset proceduralSizePreset = MazeSizePreset.Large;
    [Range(2, 8)] public int overrideSlotCols = 4;
    [Range(2, 8)] public int overrideSlotRows = 7;

    [Header("Continuous World Stitching & Window")]
    [Min(2)]
    [Tooltip("Number of connecting corridor cells between previous Goal perimeter and next Start perimeter.")]
    public int throatLengthCells = 2;
    [Range(1, 2)]
    [Tooltip("How many rooms ahead of the current room are pre-stitched.")]
    public int lookaheadRooms = 1;
    [Range(0, 2)]
    [Tooltip("How many previous rooms behind the current room stay alive briefly.")]
    public int retainedPreviousRooms = 1;

    [Header("Tilemap Appearance")]
    public TileBase wallRuleTile;
    public TileBase backgroundRuleTile;

    [Header("Default Population Profile")]
    public RunnerPopulationProfile defaultPopulationProfile;

    [Header("Run Rewards (Settled Atomically on Run End)")]
    [Min(0)] public int coinsPerClearedRoom = 15;
    [Min(0)] public int expPerClearedRoom = 10;
    [Min(0)] public int bonusCoins10Rooms = 100;
    [Min(0)] public int bonusCoins20Rooms = 250;

    public static int GetTargetRoomCount(RunnerModeKind mode)
    {
        return mode switch
        {
            RunnerModeKind.Rooms10 => 10,
            RunnerModeKind.Rooms20 => 20,
            _                      => 0,
        };
    }

    public MazeDifficultyProfile ResolveDifficultyProfile(int roomNumber)
    {
        if (roomNumber <= 3)
            return earlyDifficultyProfile != null ? earlyDifficultyProfile : (midDifficultyProfile ?? lateDifficultyProfile);

        if (roomNumber <= 8)
            return midDifficultyProfile != null ? midDifficultyProfile : (lateDifficultyProfile ?? earlyDifficultyProfile);

        return lateDifficultyProfile != null ? lateDifficultyProfile : (midDifficultyProfile ?? earlyDifficultyProfile);
    }

    public void CalculateCompletionRewards(
        RunnerModeKind mode,
        int clearedRooms,
        int pickedUpCoins,
        bool reachedTarget,
        out int totalCoins,
        out int totalExp)
    {
        int safeRooms = Mathf.Max(0, clearedRooms);
        int safePickups = Mathf.Max(0, pickedUpCoins);

        int modeClearBonus = 0;
        if (reachedTarget)
        {
            if (mode == RunnerModeKind.Rooms10) modeClearBonus = bonusCoins10Rooms;
            else if (mode == RunnerModeKind.Rooms20) modeClearBonus = bonusCoins20Rooms;
        }

        totalCoins = safePickups + safeRooms * Mathf.Max(0, coinsPerClearedRoom) + Mathf.Max(0, modeClearBonus);
        totalExp = safeRooms * Mathf.Max(0, expPerClearedRoom);
    }

    private void OnValidate()
    {
        proceduralMixChanceWhenHybrid = Mathf.Clamp01(proceduralMixChanceWhenHybrid);
        overrideSlotCols = Mathf.Clamp(overrideSlotCols, 2, 8);
        overrideSlotRows = Mathf.Clamp(overrideSlotRows, 2, 8);
        throatLengthCells = Mathf.Max(2, throatLengthCells);
        lookaheadRooms = Mathf.Clamp(lookaheadRooms, 1, 2);
        retainedPreviousRooms = Mathf.Clamp(retainedPreviousRooms, 0, 2);
        coinsPerClearedRoom = Mathf.Max(0, coinsPerClearedRoom);
        expPerClearedRoom = Mathf.Max(0, expPerClearedRoom);
        bonusCoins10Rooms = Mathf.Max(0, bonusCoins10Rooms);
        bonusCoins20Rooms = Mathf.Max(0, bonusCoins20Rooms);

        if (roomTemplates != null)
        {
            for (int i = 0; i < roomTemplates.Count; i++)
            {
                RunnerRoomTemplateEntry entry = roomTemplates[i];
                entry.weight = Mathf.Max(0.01f, entry.weight);
                entry.minRoomIndex = Mathf.Max(1, entry.minRoomIndex);
                roomTemplates[i] = entry;
            }
        }
    }
}
