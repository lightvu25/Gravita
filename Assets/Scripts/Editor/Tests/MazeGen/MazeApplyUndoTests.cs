using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

[TestFixture]
public class MazeApplyUndoTests
{
    private readonly List<GameObject> spawned = new List<GameObject>();

    [TearDown]
    public void TearDown()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
            {
                Undo.ClearUndo(spawned[i]);
                Object.DestroyImmediate(spawned[i]);
            }
        }
        spawned.Clear();
        Physics2D.SyncTransforms();
    }

    private static MazePatternLibrary LoadLibrary() =>
        AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);

    private static MazeDifficultyProfile LoadProfile(string name) =>
        AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"{MazePatternBootstrap.ProfilesFolder}/Profile_{name}.asset");

    private GameObject BuildTestLevelRig(
        out LevelRoot levelRoot,
        out Tilemap wallTilemap,
        out Tilemap bgTilemap,
        out Transform spawnTransform,
        out Transform exitTransform)
    {
        var rootGo = new GameObject("TempLevelRoot");
        spawned.Add(rootGo);
        levelRoot = rootGo.AddComponent<LevelRoot>();

        var gridGo = new GameObject("Grid");
        gridGo.transform.SetParent(rootGo.transform, false);
        gridGo.AddComponent<Grid>();

        var wallGo = new GameObject("WallTilemap");
        wallGo.layer = LayerMask.NameToLayer("Wall");
        wallGo.transform.SetParent(gridGo.transform, false);
        wallTilemap = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();
        var rb = wallGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var tmCol = wallGo.AddComponent<TilemapCollider2D>();
        tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        var comp = wallGo.AddComponent<CompositeCollider2D>();
        comp.geometryType = CompositeCollider2D.GeometryType.Polygons;

        var bgGo = new GameObject("BackgroundTilemap");
        int bgLayer = LayerMask.NameToLayer("Background");
        if (bgLayer >= 0) bgGo.layer = bgLayer;
        bgGo.transform.SetParent(gridGo.transform, false);
        bgTilemap = bgGo.AddComponent<Tilemap>();
        bgGo.AddComponent<TilemapRenderer>();

        var spawnGo = new GameObject("PlayerSpawn");
        spawnGo.transform.SetParent(rootGo.transform, false);
        spawnGo.AddComponent<PlayerSpawnPoint>();
        spawnTransform = spawnGo.transform;

        var exitGo = new GameObject("Exit");
        exitGo.transform.SetParent(rootGo.transform, false);
        var exitBox = exitGo.AddComponent<BoxCollider2D>();
        exitBox.isTrigger = true;
        exitGo.AddComponent<LevelExit>();
        exitTransform = exitGo.transform;

        return rootGo;
    }

    [Test]
    public void ApplyToTargets_RejectsEmptyStaleInvalidAndPersistentAssets_WithoutMutatingTargets()
    {
        BuildTestLevelRig(
            out LevelRoot levelRoot,
            out Tilemap wallTilemap,
            out _,
            out Transform spawnTr,
            out Transform exitTr);

        TileBase wallTile = MazeApplyService.LoadDefaultWallTile();
        Assert.IsNotNull(wallTile);

        Vector3Int sentinelCell = new Vector3Int(42, 42, 0);
        wallTilemap.SetTile(sentinelCell, wallTile);
        Vector3 initialSpawn = new Vector3(1.25f, 2.75f, 0f);
        Vector3 initialExit = new Vector3(4.10f, 6.90f, 0f);
        spawnTr.position = initialSpawn;
        exitTr.position = initialExit;

        MazeApplyTargets targets = MazeApplyTargets.CreateDefault();
        Assert.IsTrue(MazeApplyService.TryBindTargetsFromLevelRoot(levelRoot, ref targets, out string bindErr), bindErr);

        var session = new MazePreviewSession();

        // 1. Empty session -> rejected
        MazeApplyResult emptyRes = MazeApplyService.ApplyToTargets(session, in targets);
        Assert.IsFalse(emptyRes.Success);
        Assert.IsTrue(wallTilemap.HasTile(sentinelCell));
        Assert.AreEqual(initialSpawn, spawnTr.position);

        // 2. Stale session -> rejected
        MazeDifficultyProfile learn = LoadProfile("Learn");
        session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 111222,
            Profile = learn,
            Library = LoadLibrary(),
        });
        Assert.IsTrue(session.CanApply);

        Vector2Int sp = session.PreviewGrid.Spawn.Value;
        session.BeginPaintStroke();
        session.PaintCell(sp.x, sp.y, MazePaintBrush.Wall);
        session.EndPaintStroke();
        Assert.IsTrue(session.IsValidationStale);

        MazeApplyResult staleRes = MazeApplyService.ApplyToTargets(session, in targets);
        Assert.IsFalse(staleRes.Success);
        Assert.IsTrue(wallTilemap.HasTile(sentinelCell));
        Assert.AreEqual(initialSpawn, spawnTr.position);
        Assert.AreEqual(initialExit, exitTr.position);

        // 3. Invalid revalidated session -> rejected
        session.Revalidate(learn);
        Assert.AreEqual(MazePreviewStatus.Invalid, session.Status);
        MazeApplyResult invalidRes = MazeApplyService.ApplyToTargets(session, in targets);
        Assert.IsFalse(invalidRes.Success);
        Assert.IsTrue(wallTilemap.HasTile(sentinelCell));

        // 4. Persistent Prefab asset on disk -> rejected even when session is Valid!
        session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 333444,
            Profile = learn,
            Library = LoadLibrary(),
        });
        Assert.IsTrue(session.CanApply);

        GameObject level1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Levels/Level 1.prefab");
        if (level1Prefab != null)
        {
            LevelRoot prefabRoot = level1Prefab.GetComponent<LevelRoot>();
            MazeApplyTargets badTargets = MazeApplyTargets.CreateDefault();
            Assert.IsFalse(MazeApplyService.TryBindTargetsFromLevelRoot(prefabRoot, ref badTargets, out _));

            badTargets.WallTilemap = level1Prefab.GetComponentInChildren<Tilemap>(true);
            badTargets.WallTile = wallTile;
            MazeApplyResult prefabRes = MazeApplyService.ApplyToTargets(session, in badTargets);
            Assert.IsFalse(prefabRes.Success, "Must never mutate a persistent Prefab asset directly via ApplyToTargets.");
        }
    }

    [Test]
    public void ApplyToTargets_WritesTilemapsAndHalfCellCenters_AndSingleUndoRestoresEverything()
    {
        GameObject rootGo = BuildTestLevelRig(
            out LevelRoot levelRoot,
            out Tilemap wallTilemap,
            out Tilemap bgTilemap,
            out Transform spawnTr,
            out Transform exitTr);

        TileBase wallTile = MazeApplyService.LoadDefaultWallTile();
        TileBase bgTile = MazeApplyService.LoadDefaultBackgroundTile();
        Assert.IsNotNull(wallTile);
        Assert.IsNotNull(bgTile);

        // Pre-populate sentinel tiles and off-grid Spawn/Exit so we can prove single-step Undo restores all 4 objects
        var sentinelWall = new Vector3Int(50, 50, 0);
        var sentinelBg = new Vector3Int(-25, -25, 0);
        wallTilemap.SetTile(sentinelWall, wallTile);
        bgTilemap.SetTile(sentinelBg, bgTile);

        Vector3 preUndoSpawn = new Vector3(1.25f, 2.75f, 0f);
        Vector3 preUndoExit = new Vector3(4.10f, 6.90f, 0f);
        spawnTr.position = preUndoSpawn;
        exitTr.position = preUndoExit;

        MazeApplyTargets targets = MazeApplyTargets.CreateDefault();
        Assert.IsTrue(MazeApplyService.TryBindTargetsFromLevelRoot(levelRoot, ref targets, out string bindErr), bindErr);
        targets.CenterAroundOrigin = true;

        var session = new MazePreviewSession();
        MazeDifficultyProfile learn = LoadProfile("Learn");
        session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 246810,
            Profile = learn,
            Library = LoadLibrary(),
        });
        Assert.IsTrue(session.CanApply);

        MazeApplyResult applyRes = MazeApplyService.ApplyToTargets(session, in targets, "Test Apply Maze");
        Assert.IsTrue(applyRes.Success, applyRes.ErrorMessage);

        MazeGrid grid = session.PreviewGrid;
        Vector2Int origin = applyRes.AppliedOriginOffset;
        Assert.AreEqual(new Vector2Int(-(grid.Width / 2), -(grid.Height / 2)), origin);

        // Sentinel tiles must have been cleared
        Assert.IsFalse(wallTilemap.HasTile(sentinelWall));
        Assert.IsFalse(bgTilemap.HasTile(sentinelBg));

        // Every cell in WallTilemap & BackgroundTilemap must match PreviewGrid
        Assert.AreEqual(grid.Width * grid.Height, applyRes.BackgroundTilesPlaced);
        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                var cell3 = new Vector3Int(origin.x + gx, origin.y + gy, 0);
                bool expectedWall = grid.GetCell(gx, gy) == MazeCell.Wall;
                Assert.AreEqual(expectedWall, wallTilemap.HasTile(cell3), $"Wall mismatch at ({gx},{gy})");
                Assert.IsTrue(bgTilemap.HasTile(cell3), $"Background tile missing at ({gx},{gy})");
            }
        }

        // Spawn & Exit must sit on exact .5 cell centers and pass LevelDataValidator with 0 errors & 0 alignment warnings
        Vector2Int sp = grid.Spawn.Value;
        Vector2Int ex = grid.Exit.Value;
        Assert.AreEqual(new Vector3(origin.x + sp.x + 0.5f, origin.y + sp.y + 0.5f, 0f), spawnTr.position);
        Assert.AreEqual(new Vector3(origin.x + ex.x + 0.5f, origin.y + ex.y + 0.5f, 0f), exitTr.position);

        var tempLevelData = ScriptableObject.CreateInstance<LevelData>();
        try
        {
            tempLevelData.levelId = "Temp_Phase5a_Check";
            tempLevelData.layoutPrefab = rootGo;
            tempLevelData.baseExpReward = 25;
            tempLevelData.primaryObjective = new ObjectiveDefinition { objectiveType = ObjectiveType.ReachExit };

            var checks = LevelDataValidator.Validate(tempLevelData);
            foreach (var c in checks)
            {
                Assert.AreNotEqual(LevelDataValidator.CheckResult.Error, c.Result, $"[{c.Label}] {c.Message}");
                if (c.Label == "Grid Center Alignment" || c.Label == "Maze Slide Topology")
                    Assert.AreEqual(LevelDataValidator.CheckResult.Pass, c.Result, $"[{c.Label}] {c.Message}");
            }
        }
        finally
        {
            Object.DestroyImmediate(tempLevelData);
        }

        // Single-step Undo.PerformUndo() must restore WallTilemap, BackgroundTilemap, Spawn, and Exit atomically
        Undo.PerformUndo();

        Assert.IsTrue(wallTilemap.HasTile(sentinelWall), "Single Undo must restore pre-existing WallTilemap tiles.");
        Assert.IsTrue(bgTilemap.HasTile(sentinelBg), "Single Undo must restore pre-existing BackgroundTilemap tiles.");
        Assert.IsFalse(wallTilemap.HasTile(new Vector3Int(origin.x, origin.y, 0)), "Single Undo must remove applied maze border tile.");
        Assert.AreEqual(preUndoSpawn, spawnTr.position, "Single Undo must restore SpawnTransform position.");
        Assert.AreEqual(preUndoExit, exitTr.position, "Single Undo must restore ExitTransform position.");
    }

    [Test]
    public void SaveMapDataAsset_PersistsAndUpdatesInPlacePreservingGuid_AndRefusesStaleSession()
    {
        const string tempAssetPath = "Assets/Data/MazeGen/Maps/__TempTest_Phase5a.asset";
        AssetDatabase.DeleteAsset(tempAssetPath);

        try
        {
            var session = new MazePreviewSession();
            MazeDifficultyProfile navigate = LoadProfile("Navigate");
            session.GeneratePreview(new MazeGenerationRequest
            {
                Seed = 135791,
                Profile = navigate,
                Library = LoadLibrary(),
            });
            Assert.IsTrue(session.CanApply);

            bool ok1 = MazeApplyService.SaveMapDataAsset(
                session,
                tempAssetPath,
                navigate.profileId,
                1,
                out MazeMapData saved1,
                out string err1);
            Assert.IsTrue(ok1, err1);
            Assert.IsNotNull(saved1);

            string guidBefore = AssetDatabase.AssetPathToGUID(tempAssetPath);
            Assert.IsNotEmpty(guidBefore);
            Assert.AreEqual(135791, saved1.seed);

            // Generate a second seed and overwrite in-place -> GUID must stay identical
            session.GeneratePreview(new MazeGenerationRequest
            {
                Seed = 975319,
                Profile = navigate,
                Library = LoadLibrary(),
            });
            bool ok2 = MazeApplyService.SaveMapDataAsset(
                session,
                tempAssetPath,
                navigate.profileId,
                1,
                out MazeMapData saved2,
                out string err2);
            Assert.IsTrue(ok2, err2);
            Assert.AreSame(saved1, saved2);
            Assert.AreEqual(guidBefore, AssetDatabase.AssetPathToGUID(tempAssetPath));
            Assert.AreEqual(975319, saved2.seed);

            // Stale edit must be refused without overwriting saved2
            Vector2Int sp = session.PreviewGrid.Spawn.Value;
            session.BeginPaintStroke();
            session.PaintCell(sp.x, sp.y, MazePaintBrush.Wall);
            session.EndPaintStroke();

            bool okStale = MazeApplyService.SaveMapDataAsset(
                session,
                tempAssetPath,
                navigate.profileId,
                1,
                out _,
                out string staleErr);
            Assert.IsFalse(okStale);
            Assert.IsNotEmpty(staleErr);
            Assert.AreEqual(975319, AssetDatabase.LoadAssetAtPath<MazeMapData>(tempAssetPath).seed);
        }
        finally
        {
            AssetDatabase.DeleteAsset(tempAssetPath);
        }
    }
}