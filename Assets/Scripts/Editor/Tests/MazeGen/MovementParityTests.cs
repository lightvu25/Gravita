using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 2 physics-to-validator parity suite: stitches 5x5 <see cref="MazePattern"/> modules
/// onto an authentic <see cref="Tilemap"/> + <see cref="CompositeCollider2D"/> arena and verifies
/// that <see cref="PlayerMovement"/> stops at the exact sequence of cells predicted by
/// <see cref="MazeSlideGraph"/>.
/// </summary>
[TestFixture]
public class MovementParityTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(-500f, 500f, 0f);

    private readonly List<GameObject> spawned = new List<GameObject>();
    private SimulationMode2D previousSimMode;

    [SetUp]
    public void SetUp()
    {
        previousSimMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
    }

    [TearDown]
    public void TearDown()
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
                Object.DestroyImmediate(spawned[i]);
        }
        spawned.Clear();
        Physics2D.simulationMode = previousSimMode;
        Physics2D.SyncTransforms();
    }

    private GameObject Track(GameObject go)
    {
        spawned.Add(go);
        return go;
    }

    private Grid BuildPhysicsMazeArena(MazeGrid mazeGrid)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        TileBase wallTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Ground Palette.asset");
        bool tempTile = false;
        if (wallTile == null)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.colliderType = Tile.ColliderType.Grid;
            wallTile = t;
            tempTile = true;
        }

        var gridGo = Track(new GameObject("ParityGrid"));
        gridGo.transform.position = ArenaOrigin;
        var grid = gridGo.AddComponent<Grid>();
        grid.cellSize = new Vector3(1f, 1f, 0f);

        var wallGo = new GameObject("WallTilemap");
        wallGo.layer = wallLayer;
        wallGo.transform.SetParent(gridGo.transform, false);

        var tm = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();
        var tmCol = wallGo.AddComponent<TilemapCollider2D>();
        tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;

        var rb = wallGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        var composite = wallGo.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

        for (int gx = 0; gx < mazeGrid.Width; gx++)
        {
            for (int gy = 0; gy < mazeGrid.Height; gy++)
            {
                if (mazeGrid.GetCell(gx, gy) == MazeCell.Wall)
                    tm.SetTile(new Vector3Int(gx, gy, 0), wallTile);
            }
        }

        tm.RefreshAllTiles();
        tmCol.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();

        if (tempTile)
            Object.DestroyImmediate(wallTile);

        return grid;
    }

    private PlayerMovement CreateAuthenticPlayer(Vector3 startWorldPos)
    {
        var go = Track(new GameObject("ParityPlayer"));
        go.transform.position = startWorldPos;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.90f, 0.90f);

        var pm = go.AddComponent<PlayerMovement>();
        var so = new SerializedObject(pm);
        so.FindProperty("moveSpeed").floatValue = 30f;
        so.FindProperty("wallLayers").intValue = LayerMask.GetMask("Wall");
        so.FindProperty("turnClearance").floatValue = 0.35f;
        so.FindProperty("moveWithHeldDirection").boolValue = false;
        so.FindProperty("bufferTurnsUntilStop").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        pm.EnsureInitialized();
        pm.TeleportTo(startWorldPos);
        Physics2D.SyncTransforms();
        return pm;
    }

    private static void RunUntilStopped(PlayerMovement pm, int maxTicks = 250)
    {
        for (int t = 0; t < maxTicks; t++)
        {
            pm.SimulateStep(Dt);
            Physics2D.Simulate(Dt);
            Physics2D.SyncTransforms();
            if (!pm.IsMoving && !pm.HasPendingDirection)
                return;
        }
        Assert.Fail($"PlayerMovement failed to reach a stop within {maxTicks} physics ticks (pos={pm.transform.position}, dir={pm.CurrentDirection}).");
    }

    [Test]
    public void Parity_MultiModuleStitchedMaze_PlayerPhysicsMatchesValidatorStopSequence1To1()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(lib);

        MazePattern Find(string id) => lib.patterns.Find(p => p.id == id);

        // 3x2 stitched slot maze (13x9 cells) exercising both Chamber modules, baffled 3-port junctions, corners, and dead ends:
        //   slot(0,0): R01_PillarChamber rot=2 (N|E)  | slot(1,0): I02_TBaffled     rot=0 (N|E|W) | slot(2,0): D01_DeadEnd     rot=0 (W)
        //   slot(0,1): C02_Corner        rot=3 (E|S)  | slot(1,1): R02_Chamber3Port rot=2 (E|S|W) | slot(2,1): D02_DeadEndHook rot=0 (W)
        var maze = new MazeGrid(13, 9);
        Assert.IsTrue(maze.TryStampPattern(Find("R01_PillarChamber"), 2, 0, 0, out string e00), e00);
        Assert.IsTrue(maze.TryStampPattern(Find("I02_TBaffled"),      0, 1, 0, out string e10), e10);
        Assert.IsTrue(maze.TryStampPattern(Find("D01_DeadEnd"),       0, 2, 0, out string e20), e20);
        Assert.IsTrue(maze.TryStampPattern(Find("C02_Corner"),        3, 0, 1, out string e01), e01);
        Assert.IsTrue(maze.TryStampPattern(Find("R02_Chamber3Port"),  2, 1, 1, out string e11), e11);
        Assert.IsTrue(maze.TryStampPattern(Find("D02_DeadEndHook"),   0, 2, 1, out string e21), e21);

        maze.SetSpawn(new Vector2Int(1, 1));
        maze.SetExit(new Vector2Int(10, 7));

        MazeValidationReport report = MazeValidator.Validate(maze, requireExitOnStopCell: true);
        Assert.IsTrue(report.IsValid, "Stitched 3x2 maze must pass strict validation: " +
            (report.Issues.Count > 0 ? report.Issues[0].Message : ""));
        Assert.GreaterOrEqual(report.SlideGraph.SolutionMoves, 6);

        Grid unityGrid = BuildPhysicsMazeArena(maze);
        Vector3 spawnWorld = unityGrid.GetCellCenterWorld(new Vector3Int(maze.Spawn.Value.x, maze.Spawn.Value.y, 0));
        PlayerMovement player = CreateAuthenticPlayer(spawnWorld);

        for (int step = 0; step < report.SlideGraph.SolutionDirections.Count; step++)
        {
            MazeDir dir = report.SlideGraph.SolutionDirections[step];
            Vector2Int expectedStop = report.SlideGraph.SolutionStops[step + 1];

            player.QueueDirection(MazeDirectionUtil.ToWorldVector(dir));
            RunUntilStopped(player);

            Vector3Int actualCell3 = unityGrid.WorldToCell(player.transform.position);
            var actualCell = new Vector2Int(actualCell3.x, actualCell3.y);
            Assert.AreEqual(expectedStop, actualCell,
                $"Physics parity mismatch at move #{step + 1} ({dir}): expected cell {expectedStop}, actual {actualCell} (world={player.transform.position}).");
        }
    }

    [Test]
    public void Parity_BufferedPerpendicularTurnDuringChicaneSlide_MatchesTwoConsecutiveGraphMoves()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(lib);
        MazePattern Find(string id) => lib.patterns.Find(p => p.id == id);

        // 3x1 stitched corridor: D01_DeadEnd(E) -> C03_Chicane(W|E) -> D01_DeadEnd(W)
        var maze = new MazeGrid(13, 5);
        Assert.IsTrue(maze.TryStampPattern(Find("D01_DeadEnd"), 2, 0, 0, out string err0), err0);
        Assert.IsTrue(maze.TryStampPattern(Find("C03_Chicane"), 0, 1, 0, out string err1), err1);
        Assert.IsTrue(maze.TryStampPattern(Find("D01_DeadEnd"), 0, 2, 0, out string err2), err2);

        maze.SetSpawn(new Vector2Int(2, 2));
        maze.SetExit(new Vector2Int(10, 2));

        MazeValidationReport report = MazeValidator.Validate(maze, requireExitOnStopCell: true);
        Assert.IsTrue(report.IsValid);
        Assert.AreEqual(5, report.SlideGraph.SolutionMoves);

        Grid unityGrid = BuildPhysicsMazeArena(maze);
        PlayerMovement player = CreateAuthenticPlayer(unityGrid.GetCellCenterWorld(new Vector3Int(2, 2, 0)));

        // Start move 0 (East) and queue move 1 (North) mid-slide on tick 1.
        player.QueueDirection(MazeDirectionUtil.ToWorldVector(report.SlideGraph.SolutionDirections[0]));
        player.SimulateStep(Dt);
        Physics2D.Simulate(Dt);
        Physics2D.SyncTransforms();
        Assert.IsTrue(player.IsMoving);

        player.QueueDirection(MazeDirectionUtil.ToWorldVector(report.SlideGraph.SolutionDirections[1]));
        RunUntilStopped(player);

        Vector3Int cellAfterBufferedPair = unityGrid.WorldToCell(player.transform.position);
        Assert.AreEqual(report.SlideGraph.SolutionStops[2], new Vector2Int(cellAfterBufferedPair.x, cellAfterBufferedPair.y));
    }
}
