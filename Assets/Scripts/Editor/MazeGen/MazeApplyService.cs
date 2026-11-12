using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Explicit scene/prefab-stage targets for Phase 5a <see cref="MazeApplyService.ApplyToTargets"/>.
/// </summary>
[Serializable]
public struct MazeApplyTargets
{
    public Tilemap WallTilemap;
    public TileBase WallTile;

    public Tilemap BackgroundTilemap;
    public TileBase BackgroundTile;

    public Transform SpawnTransform;
    public Transform ExitTransform;

    public bool CenterAroundOrigin;
    public Vector2Int CellOriginOffset;
    public bool ClearExistingTilesFirst;
    public bool FillBackgroundUnderWalls;

    public static MazeApplyTargets CreateDefault()
    {
        return new MazeApplyTargets
        {
            CenterAroundOrigin = false,
            CellOriginOffset = Vector2Int.zero,
            ClearExistingTilesFirst = true,
            FillBackgroundUnderWalls = true,
        };
    }
}

/// <summary>
/// Outcome of <see cref="MazeApplyService.ApplyToTargets"/>.
/// </summary>
public readonly struct MazeApplyResult
{
    public readonly bool Success;
    public readonly string ErrorMessage;
    public readonly int WallTilesPlaced;
    public readonly int BackgroundTilesPlaced;
    public readonly Vector2Int AppliedOriginOffset;
    public readonly Vector3? AppliedSpawnWorldPos;
    public readonly Vector3? AppliedExitWorldPos;
    public readonly int UndoGroupIndex;

    public MazeApplyResult(
        bool success,
        string errorMessage,
        int wallTilesPlaced = 0,
        int backgroundTilesPlaced = 0,
        Vector2Int appliedOriginOffset = default,
        Vector3? appliedSpawnWorldPos = null,
        Vector3? appliedExitWorldPos = null,
        int undoGroupIndex = -1)
    {
        Success = success;
        ErrorMessage = errorMessage;
        WallTilesPlaced = wallTilesPlaced;
        BackgroundTilesPlaced = backgroundTilesPlaced;
        AppliedOriginOffset = appliedOriginOffset;
        AppliedSpawnWorldPos = appliedSpawnWorldPos;
        AppliedExitWorldPos = appliedExitWorldPos;
        UndoGroupIndex = undoGroupIndex;
    }

    public static MazeApplyResult Failed(string error) => new MazeApplyResult(false, error);
}

/// <summary>
/// Parameters for Phase 5b <see cref="MazeApplyService.CreateLevelFromTemplate"/>.
/// </summary>
[Serializable]
public struct MazeTemplateLevelRequest
{
    public GameObject TemplatePrefab;
    public string LevelAssetName;
    public string DisplayName;
    public int WorldIndex;
    public MazeDifficultyProfile Profile;
    public int PatternLibraryVersion;

    public TileBase OverrideWallTile;
    public TileBase OverrideBackgroundTile;

    public bool CenterAroundOrigin;
    public bool StripTemplateCollectiblesAndMechanics;
    public bool SaveCompanionMapData;
    public bool AllowOverwrite;

    public string CustomPrefabFolder;
    public string CustomLevelDataFolder;
    public string CustomMapDataFolder;

    public static MazeTemplateLevelRequest CreateDefault()
    {
        return new MazeTemplateLevelRequest
        {
            LevelAssetName = "Level_Maze_01",
            DisplayName = "Maze Sector 01",
            WorldIndex = 1,
            PatternLibraryVersion = 1,
            CenterAroundOrigin = true,
            StripTemplateCollectiblesAndMechanics = true,
            SaveCompanionMapData = true,
            AllowOverwrite = false,
        };
    }
}

/// <summary>
/// Outcome of Phase 5b <see cref="MazeApplyService.CreateLevelFromTemplate"/>.
/// </summary>
public readonly struct MazeTemplateLevelResult
{
    public readonly bool Success;
    public readonly string ErrorMessage;
    public readonly string PrefabAssetPath;
    public readonly string LevelDataAssetPath;
    public readonly string MapDataAssetPath;
    public readonly GameObject CreatedPrefab;
    public readonly LevelData CreatedLevelData;
    public readonly MazeMapData CreatedMapData;
    public readonly string ManualCampaignReminder;

    public MazeTemplateLevelResult(
        bool success,
        string errorMessage,
        string prefabAssetPath = null,
        string levelDataAssetPath = null,
        string mapDataAssetPath = null,
        GameObject createdPrefab = null,
        LevelData createdLevelData = null,
        MazeMapData createdMapData = null,
        string manualCampaignReminder = null)
    {
        Success = success;
        ErrorMessage = errorMessage;
        PrefabAssetPath = prefabAssetPath;
        LevelDataAssetPath = levelDataAssetPath;
        MapDataAssetPath = mapDataAssetPath;
        CreatedPrefab = createdPrefab;
        CreatedLevelData = createdLevelData;
        CreatedMapData = createdMapData;
        ManualCampaignReminder = manualCampaignReminder;
    }

    public static MazeTemplateLevelResult Failed(string error) =>
        new MazeTemplateLevelResult(false, error);
}

/// <summary>
/// Phase 5a &amp; 5b Editor service:
/// <list type="bullet">
/// <item><description><b>Phase 5a</b>: Writes a freshly validated <see cref="MazePreviewSession"/> into an explicitly assigned <see cref="Tilemap"/> (+ optional <c>BackgroundTilemap</c>, <c>SpawnTransform</c>, <c>ExitTransform</c>) inside a single collapsed Unity <see cref="Undo"/> group, and saves <see cref="MazeMapData"/> assets.</description></item>
/// <item><description><b>Phase 5b</b>: Clones a template layout prefab in isolation, applies the validated <see cref="MazeGrid"/>, saves a new <c>.prefab</c> + <see cref="LevelData"/> (+ optional <see cref="MazeMapData"/>), and <b>never</b> modifies <c>CampaignDatabase</c> (Q4).</description></item>
/// </list>
/// </summary>
public static class MazeApplyService
{
    public const string DefaultWallTilePath            = "Assets/Tilemap/Ground Palette.asset";
    public const string DefaultBackgroundTilePath      = "Assets/Tilemap/Background Palette.asset";
    public const string DefaultMapsFolder              = "Assets/Data/MazeGen/Maps";
    public const string DefaultTemplatePrefabPath      = "Assets/Prefabs/Levels/Level 1.prefab";
    public const string DefaultLevelsPrefabFolder      = "Assets/Prefabs/Levels";
    public const string DefaultLevelDataFolder         = "Assets/Data/Levels";

    private const string ProtectedLegacyPrefabPath     = "Assets/Prefabs/Levels/Level 1.prefab";
    private const string ProtectedLegacyLevelDataPath  = "Assets/Data/Levels/Level_001.asset";

    public static TileBase LoadDefaultWallTile() =>
        AssetDatabase.LoadAssetAtPath<TileBase>(DefaultWallTilePath);

    public static TileBase LoadDefaultBackgroundTile() =>
        AssetDatabase.LoadAssetAtPath<TileBase>(DefaultBackgroundTilePath);

    public static GameObject LoadDefaultTemplatePrefab() =>
        AssetDatabase.LoadAssetAtPath<GameObject>(DefaultTemplatePrefabPath);

    /// <summary>
    /// Auto-discovers <c>WallTilemap</c>, <c>BackgroundTilemap</c>, <see cref="PlayerSpawnPoint"/>,
    /// and <see cref="LevelExit"/> beneath an existing scene <see cref="LevelRoot"/>.
    /// </summary>
    public static bool TryBindTargetsFromLevelRoot(LevelRoot levelRoot, ref MazeApplyTargets targets, out string error)
    {
        error = null;
        if (levelRoot == null)
        {
            error = "LevelRoot is null.";
            return false;
        }

        if (EditorUtility.IsPersistent(levelRoot))
        {
            error = "Cannot bind Apply targets directly to a Project prefab asset on disk. Instantiate it in a scene or open it in Prefab Mode.";
            return false;
        }

        int wallLayer = LayerMask.NameToLayer("Wall");
        int bgLayer = LayerMask.NameToLayer("Background");

        Tilemap foundWall = null;
        Tilemap foundBg = null;
        Tilemap[] tilemaps = levelRoot.GetComponentsInChildren<Tilemap>(includeInactive: true);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            Tilemap tm = tilemaps[i];
            if (tm == null) continue;

            if (foundWall == null && (tm.gameObject.layer == wallLayer || tm.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0))
                foundWall = tm;
            else if (foundBg == null && (tm.gameObject.layer == bgLayer || tm.name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0))
                foundBg = tm;
        }

        if (foundWall == null)
        {
            error = $"No WallTilemap found under LevelRoot '{levelRoot.name}'.";
            return false;
        }

        targets.WallTilemap = foundWall;
        targets.BackgroundTilemap = foundBg;

        PlayerSpawnPoint spawn = levelRoot.GetComponentInChildren<PlayerSpawnPoint>(includeInactive: true);
        if (spawn != null)
            targets.SpawnTransform = spawn.transform;

        LevelExit exit = levelRoot.GetComponentInChildren<LevelExit>(includeInactive: true);
        if (exit != null)
            targets.ExitTransform = exit.transform;

        if (targets.WallTile == null)
            targets.WallTile = LoadDefaultWallTile();
        if (targets.BackgroundTile == null)
            targets.BackgroundTile = LoadDefaultBackgroundTile();

        return true;
    }

    /// <summary>
    /// Computes the cell origin offset applied to <paramref name="grid"/> when writing into a <see cref="Tilemap"/>.
    /// </summary>
    public static Vector2Int ResolveOriginOffset(MazeGrid grid, bool centerAroundOrigin, Vector2Int customOffset)
    {
        if (grid == null)
            return customOffset;
        if (centerAroundOrigin)
            return new Vector2Int(-(grid.Width / 2), -(grid.Height / 2));
        return customOffset;
    }

    /// <summary>
    /// Writes <paramref name="session"/>'s validated <see cref="MazeGrid"/> to <paramref name="targets"/>
    /// wrapped inside a single collapsed Unity <see cref="Undo"/> operation.
    /// Hard-fails without touching any scene object if <see cref="MazePreviewSession.CanApply"/> is false
    /// or if any target references a persistent asset on disk.
    /// </summary>
    public static MazeApplyResult ApplyToTargets(
        MazePreviewSession session,
        in MazeApplyTargets targets,
        string undoLabel = "Apply Generated Maze")
    {
        if (!ValidateSessionReadyForApply(session, out string sessionError))
            return MazeApplyResult.Failed(sessionError);

        if (targets.WallTilemap == null)
            return MazeApplyResult.Failed("WallTilemap is not assigned.");

        if (EditorUtility.IsPersistent(targets.WallTilemap))
            return MazeApplyResult.Failed("WallTilemap points to a persistent Prefab asset on disk. Assign a scene or Prefab Stage Tilemap instead.");

        if (targets.WallTile == null)
            return MazeApplyResult.Failed("WallTile (TileBase) is not assigned.");

        if (targets.BackgroundTilemap != null)
        {
            if (EditorUtility.IsPersistent(targets.BackgroundTilemap))
                return MazeApplyResult.Failed("BackgroundTilemap points to a persistent Prefab asset on disk.");
            if (targets.BackgroundTile == null)
                return MazeApplyResult.Failed("BackgroundTile is required when BackgroundTilemap is assigned.");
        }

        if (targets.SpawnTransform != null && EditorUtility.IsPersistent(targets.SpawnTransform))
            return MazeApplyResult.Failed("SpawnTransform points to a persistent Prefab asset on disk.");

        if (targets.ExitTransform != null && EditorUtility.IsPersistent(targets.ExitTransform))
            return MazeApplyResult.Failed("ExitTransform points to a persistent Prefab asset on disk.");

        MazeGrid grid = session.PreviewGrid;
        Vector2Int origin = ResolveOriginOffset(grid, targets.CenterAroundOrigin, targets.CellOriginOffset);

        Undo.IncrementCurrentGroup();
        Undo.SetCurrentGroupName(undoLabel);
        int undoGroup = Undo.GetCurrentGroup();

        // Record complete object snapshots before any mutation so a single Ctrl+Z reverts everything atomically
        Undo.RegisterCompleteObjectUndo(targets.WallTilemap, undoLabel);
        if (targets.BackgroundTilemap != null)
            Undo.RegisterCompleteObjectUndo(targets.BackgroundTilemap, undoLabel);
        if (targets.SpawnTransform != null)
            Undo.RecordObject(targets.SpawnTransform, undoLabel);
        if (targets.ExitTransform != null)
            Undo.RecordObject(targets.ExitTransform, undoLabel);

        int wallPlaced = WriteGridToTilemaps(grid, in targets, origin, out int bgPlaced);

        Grid layoutGrid = targets.WallTilemap.layoutGrid;
        Vector3? appliedSpawnPos = null;
        if (targets.SpawnTransform != null && grid.Spawn.HasValue)
        {
            Vector3Int spCell = new Vector3Int(origin.x + grid.Spawn.Value.x, origin.y + grid.Spawn.Value.y, 0);
            Vector3 worldPos = CellCenterToWorld(layoutGrid, targets.WallTilemap, spCell, targets.SpawnTransform.position.z);
            targets.SpawnTransform.position = worldPos;
            EditorUtility.SetDirty(targets.SpawnTransform);
            appliedSpawnPos = worldPos;
        }

        Vector3? appliedExitPos = null;
        if (targets.ExitTransform != null && grid.Exit.HasValue)
        {
            Vector3Int exCell = new Vector3Int(origin.x + grid.Exit.Value.x, origin.y + grid.Exit.Value.y, 0);
            Vector3 worldPos = CellCenterToWorld(layoutGrid, targets.WallTilemap, exCell, targets.ExitTransform.position.z);
            targets.ExitTransform.position = worldPos;
            EditorUtility.SetDirty(targets.ExitTransform);
            appliedExitPos = worldPos;
        }

        EditorUtility.SetDirty(targets.WallTilemap);
        if (targets.BackgroundTilemap != null)
            EditorUtility.SetDirty(targets.BackgroundTilemap);

        if (targets.WallTilemap.gameObject.scene.IsValid())
            EditorSceneManager.MarkSceneDirty(targets.WallTilemap.gameObject.scene);

        Undo.CollapseUndoOperations(undoGroup);
        Physics2D.SyncTransforms();

        return new MazeApplyResult(
            success: true,
            errorMessage: null,
            wallTilesPlaced: wallPlaced,
            backgroundTilesPlaced: bgPlaced,
            appliedOriginOffset: origin,
            appliedSpawnWorldPos: appliedSpawnPos,
            appliedExitWorldPos: appliedExitPos,
            undoGroupIndex: undoGroup);
    }

    /// <summary>
    /// Phase 5b: Clones <see cref="MazeTemplateLevelRequest.TemplatePrefab"/> in an isolated prefab preview context,
    /// applies the validated <see cref="MazeGrid"/>, saves a new <c>LevelRoot</c> prefab and <see cref="LevelData"/> asset
    /// (plus optional <see cref="MazeMapData"/>), and strictly leaves <c>CampaignDatabase</c> untouched (Q4).
    /// </summary>
    public static MazeTemplateLevelResult CreateLevelFromTemplate(
        MazePreviewSession session,
        in MazeTemplateLevelRequest request)
    {
        if (!ValidateSessionReadyForApply(session, out string sessionError))
            return MazeTemplateLevelResult.Failed(sessionError);

        if (request.TemplatePrefab == null)
            return MazeTemplateLevelResult.Failed("Template Layout Prefab is required.");

        string templatePath = AssetDatabase.GetAssetPath(request.TemplatePrefab);
        if (string.IsNullOrEmpty(templatePath) || !templatePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
            return MazeTemplateLevelResult.Failed("Template Layout Prefab must be a saved .prefab asset in the Project.");

        if (request.TemplatePrefab.GetComponent<LevelRoot>() == null)
            return MazeTemplateLevelResult.Failed($"Template prefab '{request.TemplatePrefab.name}' does not have a LevelRoot component on its root.");

        string rawTrimmed = request.LevelAssetName != null ? request.LevelAssetName.Trim() : string.Empty;
        string safeName = SanitizeFileName(rawTrimmed);
        if (string.IsNullOrWhiteSpace(rawTrimmed) || string.IsNullOrEmpty(safeName))
            return MazeTemplateLevelResult.Failed("Level Asset Name cannot be empty.");

        string prefabFolder = string.IsNullOrWhiteSpace(request.CustomPrefabFolder)
            ? DefaultLevelsPrefabFolder
            : request.CustomPrefabFolder.TrimEnd('/');
        string levelDataFolder = string.IsNullOrWhiteSpace(request.CustomLevelDataFolder)
            ? DefaultLevelDataFolder
            : request.CustomLevelDataFolder.TrimEnd('/');
        string mapDataFolder = string.IsNullOrWhiteSpace(request.CustomMapDataFolder)
            ? DefaultMapsFolder
            : request.CustomMapDataFolder.TrimEnd('/');

        string rawPrefabPath = $"{prefabFolder}/{rawTrimmed}.prefab";
        string rawLevelDataPath = $"{levelDataFolder}/{rawTrimmed}.asset";
        string newPrefabPath = $"{prefabFolder}/{safeName}.prefab";
        string newLevelDataPath = $"{levelDataFolder}/{safeName}.asset";
        string newMapDataPath = $"{mapDataFolder}/{safeName}_MapData.asset";

        // Hard safety guards: never overwrite the template itself or legacy Level_001 / Level 1 (Q7)
        if (string.Equals(templatePath, newPrefabPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(templatePath, rawPrefabPath, StringComparison.OrdinalIgnoreCase))
        {
            return MazeTemplateLevelResult.Failed($"Target prefab path '{rawPrefabPath}' cannot overwrite the template prefab itself.");
        }

        if (string.Equals(newPrefabPath, ProtectedLegacyPrefabPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawPrefabPath, ProtectedLegacyPrefabPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(newLevelDataPath, ProtectedLegacyLevelDataPath, StringComparison.OrdinalIgnoreCase)
            || string.Equals(rawLevelDataPath, ProtectedLegacyLevelDataPath, StringComparison.OrdinalIgnoreCase))
        {
            return MazeTemplateLevelResult.Failed("Overwriting legacy Level 1.prefab or Level_001.asset is prohibited (Q7).");
        }

        if (!request.AllowOverwrite)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(newPrefabPath) != null)
                return MazeTemplateLevelResult.Failed($"Prefab already exists at '{newPrefabPath}'. Choose a unique name or enable Allow Overwrite.");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(newLevelDataPath) != null)
                return MazeTemplateLevelResult.Failed($"LevelData already exists at '{newLevelDataPath}'. Choose a unique name or enable Allow Overwrite.");
        }

        TileBase wallTile = request.OverrideWallTile != null ? request.OverrideWallTile : LoadDefaultWallTile();
        if (wallTile == null)
            return MazeTemplateLevelResult.Failed("WallTile could not be resolved (assign OverrideWallTile or ensure Ground Palette.asset exists).");

        TileBase bgTile = request.OverrideBackgroundTile != null ? request.OverrideBackgroundTile : LoadDefaultBackgroundTile();

        EnsureFolderRecursive(prefabFolder);
        EnsureFolderRecursive(levelDataFolder);
        if (request.SaveCompanionMapData)
            EnsureFolderRecursive(mapDataFolder);

        GameObject contentsRoot = null;
        GameObject savedPrefab = null;
        try
        {
            contentsRoot = PrefabUtility.LoadPrefabContents(templatePath);
            if (contentsRoot == null)
                return MazeTemplateLevelResult.Failed($"Failed to load prefab contents from '{templatePath}'.");

            contentsRoot.name = safeName;
            contentsRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            contentsRoot.transform.localScale = Vector3.one;

            if (request.StripTemplateCollectiblesAndMechanics)
                StripTemplateGameplayExtras(contentsRoot);

            LevelRoot rootComp = contentsRoot.GetComponent<LevelRoot>();
            MazeApplyTargets cloneTargets = MazeApplyTargets.CreateDefault();
            cloneTargets.WallTile = wallTile;
            cloneTargets.BackgroundTile = bgTile;
            cloneTargets.CenterAroundOrigin = request.CenterAroundOrigin;
            cloneTargets.ClearExistingTilesFirst = true;
            cloneTargets.FillBackgroundUnderWalls = true;

            if (!TryBindTargetsFromLevelRoot(rootComp, ref cloneTargets, out string bindErr))
                return MazeTemplateLevelResult.Failed(bindErr);

            if (cloneTargets.BackgroundTilemap != null && cloneTargets.BackgroundTile == null)
                cloneTargets.BackgroundTilemap = null;

            MazeGrid grid = session.PreviewGrid;
            Vector2Int origin = ResolveOriginOffset(grid, cloneTargets.CenterAroundOrigin, Vector2Int.zero);
            WriteGridToTilemaps(grid, in cloneTargets, origin, out _);

            Grid layoutGrid = cloneTargets.WallTilemap.layoutGrid;
            if (cloneTargets.SpawnTransform != null && grid.Spawn.HasValue)
            {
                Vector3Int spCell = new Vector3Int(origin.x + grid.Spawn.Value.x, origin.y + grid.Spawn.Value.y, 0);
                cloneTargets.SpawnTransform.position = CellCenterToWorld(
                    layoutGrid, cloneTargets.WallTilemap, spCell, cloneTargets.SpawnTransform.position.z);
            }

            if (cloneTargets.ExitTransform != null && grid.Exit.HasValue)
            {
                Vector3Int exCell = new Vector3Int(origin.x + grid.Exit.Value.x, origin.y + grid.Exit.Value.y, 0);
                cloneTargets.ExitTransform.position = CellCenterToWorld(
                    layoutGrid, cloneTargets.WallTilemap, exCell, cloneTargets.ExitTransform.position.z);
            }

            savedPrefab = PrefabUtility.SaveAsPrefabAsset(contentsRoot, newPrefabPath, out bool prefabSavedOk);
            if (!prefabSavedOk || savedPrefab == null)
                return MazeTemplateLevelResult.Failed($"PrefabUtility.SaveAsPrefabAsset failed for '{newPrefabPath}'.");
        }
        finally
        {
            if (contentsRoot != null)
                PrefabUtility.UnloadPrefabContents(contentsRoot);
        }

        // Create or update LevelData asset (strictly NEVER touches CampaignDatabase.asset)
        LevelData existingLevelData = AssetDatabase.LoadAssetAtPath<LevelData>(newLevelDataPath);
        bool createNewLevelData = existingLevelData == null;
        LevelData levelData = createNewLevelData ? ScriptableObject.CreateInstance<LevelData>() : existingLevelData;

        levelData.levelId = safeName;
        levelData.displayName = string.IsNullOrWhiteSpace(request.DisplayName) ? safeName : request.DisplayName.Trim();
        levelData.world = Mathf.Max(1, request.WorldIndex);
        levelData.difficulty = MapProfileTierToLevelDifficulty(request.Profile);
        levelData.layoutPrefab = savedPrefab;
        levelData.primaryObjective = new ObjectiveDefinition
        {
            objectiveType = ObjectiveType.ReachExit,
            requiredCount = 0,
            description = "Reach the exit",
        };
        levelData.optionalObjectives = Array.Empty<ObjectiveDefinition>();
        levelData.baseCoinReward = 10;
        levelData.optionalCoinBonus = 5;
        levelData.baseExpReward = 50;
        levelData.expBonusPerStar = 15;
        levelData.replayExpReward = 20;

        if (createNewLevelData)
            AssetDatabase.CreateAsset(levelData, newLevelDataPath);
        else
            EditorUtility.SetDirty(levelData);

        MazeMapData companionMap = null;
        string savedMapPath = null;
        if (request.SaveCompanionMapData)
        {
            string profId = request.Profile != null ? request.Profile.profileId : "Custom";
            int libVer = Mathf.Max(1, request.PatternLibraryVersion);
            if (SaveMapDataAsset(session, newMapDataPath, profId, libVer, out companionMap, out string mapErr))
                savedMapPath = newMapDataPath;
            else
                return MazeTemplateLevelResult.Failed($"Prefab & LevelData created, but saving MazeMapData failed: {mapErr}");
        }

        AssetDatabase.SaveAssets();

        const string reminder =
            "Created Layout Prefab + LevelData. Per Q4, CampaignDatabase was NOT modified — add the new LevelData to CampaignDatabase manually when ready.";

        return new MazeTemplateLevelResult(
            success: true,
            errorMessage: null,
            prefabAssetPath: newPrefabPath,
            levelDataAssetPath: newLevelDataPath,
            mapDataAssetPath: savedMapPath,
            createdPrefab: savedPrefab,
            createdLevelData: levelData,
            createdMapData: companionMap,
            manualCampaignReminder: reminder);
    }

    private static LevelDifficulty MapProfileTierToLevelDifficulty(MazeDifficultyProfile profile)
    {
        if (profile == null)
            return LevelDifficulty.Medium;

        return profile.tier switch
        {
            MazeDifficultyTier.Learn    => LevelDifficulty.Easy,
            MazeDifficultyTier.Navigate => LevelDifficulty.Medium,
            MazeDifficultyTier.Master   => LevelDifficulty.Hard,
            _                           => LevelDifficulty.Medium,
        };
    }

    private static bool ContainsCoreStructuralComponent(GameObject go)
    {
        if (go == null) return false;
        return go.GetComponent<LevelRoot>() != null
            || go.GetComponentInChildren<Grid>(true) != null
            || go.GetComponentInChildren<Tilemap>(true) != null
            || go.GetComponentInChildren<PlayerSpawnPoint>(true) != null
            || go.GetComponentInChildren<LevelExit>(true) != null;
    }

    private static bool IsGameplayExtraObject(GameObject go, int wallLayer)
    {
        if (go == null) return false;
        if (go.GetComponent<Collectible>() != null
            || go.GetComponent<Hazard>() != null
            || go.GetComponent<ICellInteraction>() != null
            || go.GetComponent<CrackedBlock>() != null
            || go.GetComponent<EnergyGate>() != null
            || go.GetComponent<BreachSequencer>() != null)
        {
            return true;
        }

        if (go.layer == wallLayer
            && go.GetComponent<BoxCollider2D>() != null
            && go.GetComponent<TilemapCollider2D>() == null)
        {
            return true;
        }

        return false;
    }

    private static void StripTemplateGameplayExtras(GameObject contentsRoot)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        var toDestroy = new HashSet<GameObject>();

        Transform[] allTransforms = contentsRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < allTransforms.Length; i++)
        {
            Transform tr = allTransforms[i];
            if (tr == null || tr == contentsRoot.transform)
                continue;

            if (!IsGameplayExtraObject(tr.gameObject, wallLayer))
                continue;

            if (ContainsCoreStructuralComponent(tr.gameObject))
                continue;

            // Walk up to the highest pure-extra container beneath contentsRoot (if any)
            Transform target = tr;
            while (target.parent != null
                   && target.parent != contentsRoot.transform
                   && !ContainsCoreStructuralComponent(target.parent.gameObject))
            {
                // Only promote to parent if every child under parent is free of core components
                target = target.parent;
            }

            toDestroy.Add(target.gameObject);
        }

        foreach (GameObject go in toDestroy)
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }
    }

    /// <summary>
    /// Low-level shared helper that stamps <paramref name="grid"/> onto <paramref name="targets"/>'s
    /// <c>WallTilemap</c> and optional <c>BackgroundTilemap</c> and rebuilds 2D tilemap/composite collider shapes.
    /// </summary>
    internal static int WriteGridToTilemaps(
        MazeGrid grid,
        in MazeApplyTargets targets,
        Vector2Int origin,
        out int bgTilesPlaced)
    {
        if (targets.ClearExistingTilesFirst)
        {
            targets.WallTilemap.ClearAllTiles();
            if (targets.BackgroundTilemap != null)
                targets.BackgroundTilemap.ClearAllTiles();
        }

        int wallTilesPlaced = 0;
        bgTilesPlaced = 0;

        for (int gy = 0; gy < grid.Height; gy++)
        {
            for (int gx = 0; gx < grid.Width; gx++)
            {
                var cellPos = new Vector3Int(origin.x + gx, origin.y + gy, 0);
                bool isWall = grid.GetCell(gx, gy) == MazeCell.Wall;

                targets.WallTilemap.SetTile(cellPos, isWall ? targets.WallTile : null);
                if (isWall)
                    wallTilesPlaced++;

                if (targets.BackgroundTilemap != null)
                {
                    bool placeBg = targets.FillBackgroundUnderWalls || !isWall;
                    targets.BackgroundTilemap.SetTile(cellPos, placeBg ? targets.BackgroundTile : null);
                    if (placeBg)
                        bgTilesPlaced++;
                }
            }
        }

        targets.WallTilemap.CompressBounds();
        targets.WallTilemap.RefreshAllTiles();
        RefreshTilemapPhysics(targets.WallTilemap);

        if (targets.BackgroundTilemap != null)
        {
            targets.BackgroundTilemap.CompressBounds();
            targets.BackgroundTilemap.RefreshAllTiles();
        }

        return wallTilesPlaced;
    }

    /// <summary>
    /// Saves the validated preview in <paramref name="session"/> to a <see cref="MazeMapData"/> asset at <paramref name="assetPath"/>.
    /// Updates an existing <see cref="MazeMapData"/> in-place if present so Unity GUID references stay intact.
    /// </summary>
    public static bool SaveMapDataAsset(
        MazePreviewSession session,
        string assetPath,
        string profileId,
        int libraryVersion,
        out MazeMapData savedAsset,
        out string errorMessage)
    {
        savedAsset = null;
        if (!ValidateSessionReadyForApply(session, out errorMessage))
            return false;

        if (string.IsNullOrWhiteSpace(assetPath)
            || !assetPath.StartsWith("Assets/", StringComparison.Ordinal)
            || !assetPath.EndsWith(".asset", StringComparison.OrdinalIgnoreCase))
        {
            errorMessage = $"Invalid asset path '{assetPath}'. Must start with 'Assets/' and end with '.asset'.";
            return false;
        }

        int lastSlash = assetPath.LastIndexOf('/');
        if (lastSlash > 0)
            EnsureFolderRecursive(assetPath.Substring(0, lastSlash));

        MazeSlideGraph graph = session.Validation.SlideGraph;
        MazeMapData existing = AssetDatabase.LoadAssetAtPath<MazeMapData>(assetPath);
        bool createdNew = existing == null;
        MazeMapData target = createdNew ? ScriptableObject.CreateInstance<MazeMapData>() : existing;

        target.PopulateFromGrid(
            grid: session.PreviewGrid,
            generationSeed: session.LastSeedUsed,
            sourceProfileId: profileId,
            preset: session.LastPresetUsed,
            libVersion: libraryVersion,
            score: session.Score != null ? session.Score.QualityScore : 0f,
            fit: session.Score != null ? session.Score.DifficultyFit : 0f,
            stops: graph.SolutionStops,
            directions: graph.SolutionDirections,
            decisionCount: graph.DecisionStopsOnSolution,
            loopCount: graph.StopGraphLoops);

        if (createdNew)
            AssetDatabase.CreateAsset(target, assetPath);
        else
            EditorUtility.SetDirty(target);

        AssetDatabase.SaveAssets();
        savedAsset = target;
        errorMessage = null;
        return true;
    }

    public static string BuildDefaultMapAssetPath(string profileId, int seed)
    {
        string safeProfile = SanitizeFileName(string.IsNullOrWhiteSpace(profileId) ? "Custom" : profileId);
        return $"{DefaultMapsFolder}/Maze_{safeProfile}_{seed}.asset";
    }

    internal static bool ValidateSessionReadyForApply(MazePreviewSession session, out string error)
    {
        error = null;
        if (session == null || !session.HasPreview)
        {
            error = "Cannot apply: no maze preview is loaded.";
            return false;
        }

        if (session.IsValidationStale)
        {
            error = "Cannot apply: preview was edited after validation (STALE). Click Validate Preview first.";
            return false;
        }

        if (session.Score != null && session.Score.HasConfigError)
        {
            error = $"Cannot apply: profile configuration error ({session.Score.ConfigError}).";
            return false;
        }

        if (session.Validation == null || !session.Validation.IsValid || session.Validation.SlideGraph == null)
        {
            int count = session.Validation != null ? session.Validation.ErrorCount : 0;
            error = $"Cannot apply: preview failed hard validation ({count} error(s)).";
            return false;
        }

        return true;
    }

    internal static Vector3 CellCenterToWorld(Grid layoutGrid, Tilemap fallbackTilemap, Vector3Int cell, float preserveZ = 0f)
    {
        if (layoutGrid != null)
        {
            Vector3 c = layoutGrid.GetCellCenterWorld(cell);
            return new Vector3(c.x, c.y, preserveZ);
        }
        if (fallbackTilemap != null)
        {
            Vector3 c = fallbackTilemap.GetCellCenterWorld(cell);
            return new Vector3(c.x, c.y, preserveZ);
        }
        return new Vector3(cell.x + 0.5f, cell.y + 0.5f, preserveZ);
    }

    internal static void RefreshTilemapPhysics(Tilemap wallTilemap)
    {
        if (wallTilemap == null)
            return;

        TilemapCollider2D tmCollider = wallTilemap.GetComponent<TilemapCollider2D>();
        if (tmCollider != null)
            tmCollider.ProcessTilemapChanges();

        CompositeCollider2D composite = wallTilemap.GetComponent<CompositeCollider2D>();
        if (composite != null)
            composite.GenerateGeometry();
    }

    public static void EnsureFolderRecursive(string folderPath)
    {
        if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath))
            return;

        int slash = folderPath.LastIndexOf('/');
        if (slash <= 0)
            return;

        string parent = folderPath.Substring(0, slash);
        string leaf = folderPath.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolderRecursive(parent);

        AssetDatabase.CreateFolder(parent, leaf);
    }

    internal static string SanitizeFileName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Maze";

        char[] chars = raw.Trim().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            char c = chars[i];
            bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-';
            if (!ok)
                chars[i] = '_';
        }
        return new string(chars);
    }
}
