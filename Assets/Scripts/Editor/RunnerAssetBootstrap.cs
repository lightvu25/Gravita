using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor bootstrapper for the three Maze Runner asset templates approved in the plan:
///   1. Assets/Prefabs/Runner/RunnerRoomShell_Template.prefab
///   2. Assets/Data/Runner/RunnerPopulationProfile_Default.asset
///   3. Assets/Data/Runner/RunnerModeConfig_Main.asset
/// Does NOT touch any scene or UI prefab.
/// </summary>
public static class RunnerAssetBootstrap
{
    public const string RoomShellPrefabPath = "Assets/Prefabs/Runner/RunnerRoomShell_Template.prefab";
    public const string PopulationProfileAssetPath = "Assets/Data/Runner/RunnerPopulationProfile_Default.asset";
    public const string ModeConfigAssetPath = "Assets/Data/Runner/RunnerModeConfig_Main.asset";

    private const string ReferenceLevelPrefabPath = "Assets/Prefabs/Levels/Level 1.prefab";
    private const string ReferenceExitPrefabPath = "Assets/Prefabs/Gameplay/Exit.prefab";
    private const string PatternLibraryPath = "Assets/Data/MazeGen/DefaultMazePatternLibrary.asset";
    private const string ProfileLearnPath = "Assets/Data/MazeGen/Profiles/Profile_Learn.asset";
    private const string ProfileNavigatePath = "Assets/Data/MazeGen/Profiles/Profile_Navigate.asset";
    private const string ProfileMasterPath = "Assets/Data/MazeGen/Profiles/Profile_Master.asset";
    private const string WallRuleTilePath = "Assets/Tilemap/Ground Palette.asset";
    private const string BackgroundRuleTilePath = "Assets/Tilemap/Background Palette.asset";

    private const string DefaultWallMaterialGuid = "a97c105638bdf8b4a8650670310a4cd3";
    private const string DefaultBackgroundMaterialGuid = "9dfc825aed78fcd4ba02077103263b40";

    [MenuItem("Tools/Gravita/Bootstrap Maze Runner Assets")]
    public static void BootstrapMenuCommand()
    {
        EnsureRunnerAssetsExist(forceRebuild: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[RunnerAssetBootstrap] Maze Runner template prefab and ScriptableObject configs built cleanly.");
    }

    public static void EnsureRunnerAssetsExist(bool forceRebuild = false)
    {
        GameObject shellPrefab = EnsureRoomShellPrefab(forceRebuild);
        RunnerPopulationProfile popProfile = EnsurePopulationProfileAsset(forceRebuild);
        EnsureModeConfigAsset(shellPrefab, popProfile, forceRebuild);
    }

    private static GameObject EnsureRoomShellPrefab(bool forceRebuild)
    {
        GameObject existing = AssetDatabase.LoadAssetAtPath<GameObject>(RoomShellPrefabPath);
        if (existing != null && !forceRebuild)
            return existing;

        EnsureParentDirectoryExists(RoomShellPrefabPath);

        Material wallMat = null;
        Material bgMat = null;
        int bgSortingLayerId = 0;
        int bgSortingOrder = -2;

        GameObject refLevel = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceLevelPrefabPath);
        if (refLevel != null)
        {
            foreach (TilemapRenderer tr in refLevel.GetComponentsInChildren<TilemapRenderer>(true))
            {
                if (tr == null) continue;
                if (wallMat == null && tr.gameObject.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    wallMat = tr.sharedMaterial;
                }
                else if (bgMat == null && tr.gameObject.name.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    bgMat = tr.sharedMaterial;
                    bgSortingLayerId = tr.sortingLayerID;
                    bgSortingOrder = tr.sortingOrder;
                }
            }
        }

        if (wallMat == null)
        {
            string p = AssetDatabase.GUIDToAssetPath(DefaultWallMaterialGuid);
            if (!string.IsNullOrEmpty(p)) wallMat = AssetDatabase.LoadAssetAtPath<Material>(p);
        }

        if (bgMat == null)
        {
            string p = AssetDatabase.GUIDToAssetPath(DefaultBackgroundMaterialGuid);
            if (!string.IsNullOrEmpty(p)) bgMat = AssetDatabase.LoadAssetAtPath<Material>(p);
        }

        var root = new GameObject("RunnerRoomShell_Template");
        try
        {
            LevelRoot levelRoot = root.AddComponent<LevelRoot>();
            RunnerRoomController roomController = root.AddComponent<RunnerRoomController>();

            var gridObj = new GameObject("Grid");
            gridObj.transform.SetParent(root.transform, false);
            Grid grid = gridObj.AddComponent<Grid>();
            grid.cellSize = Vector3.one;

            int wallLayer = LayerMask.NameToLayer("Wall");
            int bgLayer = LayerMask.NameToLayer("Background");

            // WallTilemap
            var wallObj = new GameObject("WallTilemap");
            wallObj.transform.SetParent(gridObj.transform, false);
            if (wallLayer >= 0) wallObj.layer = wallLayer;
            Tilemap wallTm = wallObj.AddComponent<Tilemap>();
            TilemapRenderer wallTr = wallObj.AddComponent<TilemapRenderer>();
            if (wallMat != null) wallTr.sharedMaterial = wallMat;

            Rigidbody2D wallRb = wallObj.AddComponent<Rigidbody2D>();
            wallRb.bodyType = RigidbodyType2D.Static;

            TilemapCollider2D wallTmCol = wallObj.AddComponent<TilemapCollider2D>();
            wallTmCol.compositeOperation = Collider2D.CompositeOperation.Merge;

            CompositeCollider2D wallComposite = wallObj.AddComponent<CompositeCollider2D>();
            wallComposite.geometryType = CompositeCollider2D.GeometryType.Polygons;

            // BackgroundTilemap
            var bgObj = new GameObject("BackgroundTilemap");
            bgObj.transform.SetParent(gridObj.transform, false);
            if (bgLayer >= 0) bgObj.layer = bgLayer;
            Tilemap bgTm = bgObj.AddComponent<Tilemap>();
            TilemapRenderer bgTr = bgObj.AddComponent<TilemapRenderer>();
            if (bgMat != null) bgTr.sharedMaterial = bgMat;
            if (bgSortingLayerId != 0) bgTr.sortingLayerID = bgSortingLayerId;
            else bgTr.sortingLayerName = "Background";
            bgTr.sortingOrder = bgSortingOrder;

            // StartNode
            var startObj = new GameObject("StartNode");
            startObj.transform.SetParent(root.transform, false);
            BoxCollider2D startCol = startObj.AddComponent<BoxCollider2D>();
            startCol.isTrigger = true;
            startCol.size = new Vector2(0.75f, 0.75f);
            startObj.AddComponent<PlayerSpawnPoint>();
            RunnerPortalNode startPortal = startObj.AddComponent<RunnerPortalNode>();
            startPortal.Configure(RunnerPortalRole.Start, CardinalDirection.Up);

            // GoalNode (visual copied from Exit prefab without Story LevelExit trigger)
            var goalObj = new GameObject("GoalNode");
            goalObj.transform.SetParent(root.transform, false);
            BoxCollider2D goalCol = goalObj.AddComponent<BoxCollider2D>();
            goalCol.isTrigger = true;
            goalCol.size = new Vector2(0.75f, 0.75f);
            RunnerPortalNode goalPortal = goalObj.AddComponent<RunnerPortalNode>();
            goalPortal.Configure(RunnerPortalRole.Goal, CardinalDirection.Up);

            GameObject exitRef = AssetDatabase.LoadAssetAtPath<GameObject>(ReferenceExitPrefabPath);
            if (exitRef != null)
            {
                SpriteRenderer refSr = exitRef.GetComponentInChildren<SpriteRenderer>(true);
                if (refSr != null)
                {
                    SpriteRenderer goalSr = goalObj.AddComponent<SpriteRenderer>();
                    goalSr.sprite = refSr.sprite;
                    goalSr.sharedMaterial = refSr.sharedMaterial;
                    goalSr.color = refSr.color;
                    goalSr.sortingLayerID = refSr.sortingLayerID;
                    goalSr.sortingOrder = refSr.sortingOrder;
                }
            }

            roomController.BindInspectorReferences(levelRoot, grid, wallTm, bgTm, startPortal, goalPortal);

            GameObject savedPrefab = PrefabUtility.SaveAsPrefabAsset(root, RoomShellPrefabPath);
            AssetDatabase.SaveAssets();
            return savedPrefab;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    private static RunnerPopulationProfile EnsurePopulationProfileAsset(bool forceRebuild)
    {
        RunnerPopulationProfile existing = AssetDatabase.LoadAssetAtPath<RunnerPopulationProfile>(PopulationProfileAssetPath);
        if (existing != null && !forceRebuild)
            return existing;

        EnsureParentDirectoryExists(PopulationProfileAssetPath);

        RunnerPopulationProfile profile = existing != null
            ? existing
            : ScriptableObject.CreateInstance<RunnerPopulationProfile>();

        profile.enableBlocks = true;
        profile.roomBlockTriggerChance = 0.85f;
        profile.roomBlocksRange = new Vector2Int(2, 5);

        profile.enableEnemies = true;
        profile.roomEnemyTriggerChance = 0.80f;
        profile.enemyChanceBonusPerRoom = 0.03f;
        profile.roomEnemiesRange = new Vector2Int(1, 3);
        profile.extraEnemyEveryNRooms = 4;
        profile.maxExtraEnemies = 3;

        profile.enableEvents = true;
        profile.roomEventTriggerChance = 0.55f;
        profile.roomEventsRange = new Vector2Int(1, 3);

        profile.enablePickups = true;
        profile.roomPickupsRange = new Vector2Int(5, 10);

        profile.safeRadiusAroundPortals = 2;
        profile.enforceSlideSolvability = true;
        profile.maxDronePatrolSpan = 4;

        profile.blockEntries.Clear();
        AddEntryIfPrefabExists(profile.blockEntries, "block_cracked", RunnerSpawnCategory.Block,
            "Assets/Prefabs/Gameplay/Cracked.prefab", 0.90f, 3.0f, new Vector2Int(1, 3), 1, 0, true);
        AddEntryIfPrefabExists(profile.blockEntries, "block_brittle", RunnerSpawnCategory.Block,
            "Assets/Prefabs/Gameplay/Brittle.prefab", 0.70f, 2.0f, new Vector2Int(1, 2), 1, 0, false);
        AddEntryIfPrefabExists(profile.blockEntries, "block_brake", RunnerSpawnCategory.Block,
            "Assets/Prefabs/Gameplay/Brake.prefab", 0.55f, 1.5f, new Vector2Int(1, 2), 2, 0, false);
        AddEntryIfPrefabExists(profile.blockEntries, "block_solid", RunnerSpawnCategory.Block,
            "Assets/Prefabs/Gameplay/SolidBlock.prefab", 0.45f, 1.2f, new Vector2Int(0, 2), 2, 0, true);

        profile.enemyEntries.Clear();
        AddEntryIfPrefabExists(profile.enemyEntries, "enemy_drone", RunnerSpawnCategory.Enemy,
            "Assets/Prefabs/Gameplay/SentinelDrone.prefab", 0.85f, 3.0f, new Vector2Int(1, 2), 1, 0, false);
        AddEntryIfPrefabExists(profile.enemyEntries, "enemy_hazard", RunnerSpawnCategory.Enemy,
            "Assets/Prefabs/Gameplay/Hazard.prefab", 0.70f, 2.0f, new Vector2Int(0, 2), 1, 0, false);

        profile.eventEntries.Clear();
        AddEntryIfPrefabExists(profile.eventEntries, "event_vector_pad", RunnerSpawnCategory.Event,
            "Assets/Prefabs/Gameplay/VectorPad.prefab", 0.75f, 2.5f, new Vector2Int(1, 2), 1, 0, false);
        AddEntryIfPrefabExists(profile.eventEntries, "event_wedge", RunnerSpawnCategory.Event,
            "Assets/Prefabs/Gameplay/Wedge.prefab", 0.65f, 2.0f, new Vector2Int(1, 2), 1, 0, false);
        AddEntryIfPrefabExists(profile.eventEntries, "event_pulse_laser", RunnerSpawnCategory.Event,
            "Assets/Prefabs/Gameplay/PulseLaser.prefab", 0.55f, 1.5f, new Vector2Int(0, 2), 2, 0, false);
        AddEntryIfPrefabExists(profile.eventEntries, "event_failing_tile", RunnerSpawnCategory.Event,
            "Assets/Prefabs/Gameplay/FailingTile.prefab", 0.50f, 1.5f, new Vector2Int(0, 2), 3, 0, false);

        profile.pickupEntries.Clear();
        AddEntryIfPrefabExists(profile.pickupEntries, "pickup_coin", RunnerSpawnCategory.Pickup,
            "Assets/Prefabs/Gameplay/Coin.prefab", 1.0f, 1.0f, new Vector2Int(4, 10), 1, 0, false);

        profile.Sanitize();

        if (existing == null)
            AssetDatabase.CreateAsset(profile, PopulationProfileAssetPath);
        else
            EditorUtility.SetDirty(profile);

        return profile;
    }

    private static RunnerModeConfig EnsureModeConfigAsset(
        GameObject shellPrefab,
        RunnerPopulationProfile popProfile,
        bool forceRebuild)
    {
        RunnerModeConfig existing = AssetDatabase.LoadAssetAtPath<RunnerModeConfig>(ModeConfigAssetPath);
        if (existing != null && !forceRebuild)
            return existing;

        EnsureParentDirectoryExists(ModeConfigAssetPath);

        RunnerModeConfig config = existing != null
            ? existing
            : ScriptableObject.CreateInstance<RunnerModeConfig>();

        config.defaultMode = RunnerModeKind.Endless;
        config.roomSourcePolicy = RunnerRoomSourcePolicy.HybridPreferPoolThenProcedural;
        config.proceduralMixChanceWhenHybrid = 0.5f;
        config.proceduralRoomShellPrefab = shellPrefab;
        config.patternLibrary = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(PatternLibraryPath);
        config.earlyDifficultyProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(ProfileLearnPath);
        config.midDifficultyProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(ProfileNavigatePath);
        config.lateDifficultyProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(ProfileMasterPath);
        config.proceduralSizePreset = MazeSizePreset.Large;
        config.overrideSlotCols = 4;
        config.overrideSlotRows = 7;
        config.throatLengthCells = 2;
        config.lookaheadRooms = 1;
        config.retainedPreviousRooms = 1;
        config.wallRuleTile = AssetDatabase.LoadAssetAtPath<TileBase>(WallRuleTilePath);
        config.backgroundRuleTile = AssetDatabase.LoadAssetAtPath<TileBase>(BackgroundRuleTilePath);
        config.defaultPopulationProfile = popProfile;
        config.coinsPerClearedRoom = 15;
        config.expPerClearedRoom = 10;
        config.bonusCoins10Rooms = 100;
        config.bonusCoins20Rooms = 250;

        EditorUtility.SetDirty(config);
        if (existing == null)
            AssetDatabase.CreateAsset(config, ModeConfigAssetPath);

        return config;
    }

    private static void AddEntryIfPrefabExists(
        System.Collections.Generic.List<RunnerSpawnEntry> list,
        string entryId,
        RunnerSpawnCategory category,
        string prefabPath,
        float spawnChance,
        float weight,
        Vector2Int countRange,
        int minRoomIndex,
        int maxRoomIndex,
        bool blocksSlideTraversal)
    {
        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (prefab == null)
        {
            Debug.LogWarning($"[RunnerAssetBootstrap] Optional gameplay prefab not found at '{prefabPath}'.");
            return;
        }

        list.Add(new RunnerSpawnEntry
        {
            entryId = entryId,
            category = category,
            prefab = prefab,
            spawnChance = spawnChance,
            weight = weight,
            countRange = countRange,
            minRoomIndex = minRoomIndex,
            maxRoomIndex = maxRoomIndex,
            blocksSlideTraversal = blocksSlideTraversal,
        });
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
