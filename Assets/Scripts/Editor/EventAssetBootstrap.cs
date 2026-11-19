using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor bootstrapper creating the 3 approved ScriptableObject assets for Live-Ops Events:
///   1. Assets/Data/Events/Event_NeonMazeRush.asset
///   2. Assets/Data/Events/Event_SectorStarHunt.asset
///   3. Assets/Data/Events/EventDatabase_Main.asset
/// Does NOT modify any Scene or UI Prefab.
/// </summary>
public static class EventAssetBootstrap
{
    public const string NeonMazeRushAssetPath = "Assets/Data/Events/Event_NeonMazeRush.asset";
    public const string SectorStarHuntAssetPath = "Assets/Data/Events/Event_SectorStarHunt.asset";
    public const string EventDatabaseAssetPath = "Assets/Data/Events/EventDatabase_Main.asset";

    [MenuItem("Tools/Gravita/Bootstrap Live Event Assets")]
    public static void BootstrapMenuCommand()
    {
        EnsureEventAssetsExist(forceRebuild: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EventAssetBootstrap] Created/updated Live Event ScriptableObject assets cleanly.");
    }

    public static void EnsureEventAssetsExist(bool forceRebuild = false)
    {
        GameEventDefinition neonRush = EnsureNeonMazeRushAsset(forceRebuild);
        GameEventDefinition starHunt = EnsureSectorStarHuntAsset(forceRebuild);
        EnsureDatabaseAsset(neonRush, starHunt, forceRebuild);
    }

    private static GameEventDefinition EnsureNeonMazeRushAsset(bool forceRebuild)
    {
        GameEventDefinition asset = AssetDatabase.LoadAssetAtPath<GameEventDefinition>(NeonMazeRushAssetPath);
        if (asset != null && !forceRebuild)
            return asset;

        EnsureParentDirectoryExists(NeonMazeRushAssetPath);
        bool isNew = asset == null;
        if (isNew)
            asset = ScriptableObject.CreateInstance<GameEventDefinition>();

        asset.Configure(
            id: "event_neon_maze_rush",
            displayTitle: "NEON MAZE RUSH",
            shortDescription: "Conquer stitched Maze Runner rooms for bonus coins & power charges.",
            rulesText: "Dive into Maze Runner mode and clear stitched rooms continuously without hitting traps. Reach each room milestone before the timer runs out to claim event rewards!",
            fixedDateSchedule: false,
            isoStartDate: string.Empty,
            daysDuration: 7,
            hideExpired: false,
            bannerClickAction: EventBannerActionType.OpenEventDetailPopup,
            detailCtaLabel: "PLAY MAZE RUNNER",
            detailCtaAction: EventBannerActionType.StartRunnerMode,
            detailCtaPanel: UIPanelType.Runner,
            detailCtaRunnerMode: RunnerModeKind.Endless,
            eventTasks: new[]
            {
                new GameEventTaskEntry
                {
                    taskId = "rush_rooms_03",
                    title = "Clear 3 Total Runner Rooms",
                    objectiveKind = EventObjectiveKind.RunnerLifetimeRooms,
                    missionType = MissionService.MissionType.CompleteLevels,
                    target = 3,
                    rewardType = MissionRewardType.Coins,
                    rewardAmount = 100,
                },
                new GameEventTaskEntry
                {
                    taskId = "rush_best_05",
                    title = "Reach Room 5 in a Runner Run",
                    objectiveKind = EventObjectiveKind.RunnerBestRooms,
                    missionType = MissionService.MissionType.CompleteLevels,
                    target = 5,
                    rewardType = MissionRewardType.Gems,
                    rewardAmount = 10,
                },
                new GameEventTaskEntry
                {
                    taskId = "rush_rooms_12",
                    title = "Clear 12 Total Runner Rooms",
                    objectiveKind = EventObjectiveKind.RunnerLifetimeRooms,
                    missionType = MissionService.MissionType.CompleteLevels,
                    target = 12,
                    rewardType = MissionRewardType.PowerUp,
                    rewardAmount = 2,
                    rewardPowerUpId = PowerUpIds.Breaker,
                },
            });

        if (isNew)
            AssetDatabase.CreateAsset(asset, NeonMazeRushAssetPath);
        else
            EditorUtility.SetDirty(asset);

        return asset;
    }

    private static GameEventDefinition EnsureSectorStarHuntAsset(bool forceRebuild)
    {
        GameEventDefinition asset = AssetDatabase.LoadAssetAtPath<GameEventDefinition>(SectorStarHuntAssetPath);
        if (asset != null && !forceRebuild)
            return asset;

        EnsureParentDirectoryExists(SectorStarHuntAssetPath);
        bool isNew = asset == null;
        if (isNew)
            asset = ScriptableObject.CreateInstance<GameEventDefinition>();

        asset.Configure(
            id: "event_sector_star_hunt",
            displayTitle: "SECTOR STAR HUNT",
            shortDescription: "Collect stars and shatter cracked bulkheads across Story stages.",
            rulesText: "Replay or advance through Campaign sectors to gather Stars, shatter Cracked Blocks, and beat levels cleanly without dying.",
            fixedDateSchedule: false,
            isoStartDate: string.Empty,
            daysDuration: 10,
            hideExpired: false,
            bannerClickAction: EventBannerActionType.OpenEventDetailPopup,
            detailCtaLabel: "GO TO CAMPAIGN MAP",
            detailCtaAction: EventBannerActionType.JumpToPanel,
            detailCtaPanel: UIPanelType.Map,
            detailCtaRunnerMode: RunnerModeKind.Endless,
            eventTasks: new[]
            {
                new GameEventTaskEntry
                {
                    taskId = "hunt_stars_10",
                    title = "Earn 10 Stars Across Stages",
                    objectiveKind = EventObjectiveKind.MissionTypeStat,
                    missionType = MissionService.MissionType.EarnStars,
                    target = 10,
                    rewardType = MissionRewardType.Coins,
                    rewardAmount = 120,
                },
                new GameEventTaskEntry
                {
                    taskId = "hunt_walls_15",
                    title = "Break 15 Cracked Bulkheads",
                    objectiveKind = EventObjectiveKind.MissionTypeStat,
                    missionType = MissionService.MissionType.BreakWalls,
                    target = 15,
                    rewardType = MissionRewardType.Coins,
                    rewardAmount = 180,
                },
                new GameEventTaskEntry
                {
                    taskId = "hunt_nodeath_05",
                    title = "Clear 5 Stages Without Dying",
                    objectiveKind = EventObjectiveKind.MissionTypeStat,
                    missionType = MissionService.MissionType.CompleteLevelsWithoutDying,
                    target = 5,
                    rewardType = MissionRewardType.Gems,
                    rewardAmount = 15,
                },
            });

        if (isNew)
            AssetDatabase.CreateAsset(asset, SectorStarHuntAssetPath);
        else
            EditorUtility.SetDirty(asset);

        return asset;
    }

    private static void EnsureDatabaseAsset(
        GameEventDefinition neonRush,
        GameEventDefinition starHunt,
        bool forceRebuild)
    {
        EventDatabase db = AssetDatabase.LoadAssetAtPath<EventDatabase>(EventDatabaseAssetPath);
        if (db != null && !forceRebuild)
            return;

        EnsureParentDirectoryExists(EventDatabaseAssetPath);
        bool isNew = db == null;
        if (isNew)
            db = ScriptableObject.CreateInstance<EventDatabase>();

        db.SetEvents(new[] { neonRush, starHunt });

        if (isNew)
            AssetDatabase.CreateAsset(db, EventDatabaseAssetPath);
        else
            EditorUtility.SetDirty(db);
    }

    private static void EnsureParentDirectoryExists(string assetPath)
    {
        string dir = Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
        if (string.IsNullOrEmpty(dir) || AssetDatabase.IsValidFolder(dir))
            return;

        string[] parts = dir.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
