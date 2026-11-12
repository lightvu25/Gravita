using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 1a independent regression suite for strict stop-to-stop movement (<see cref="PlayerMovement.BufferTurnsUntilStop"/>).
/// Uses the project's real Grid + WallTilemap + CompositeCollider2D setup to verify buffered perpendicular turns,
/// ghost-input clearing, instant mid-slide reversal, GravityBlock fresh-input safety, and legacy toggle compatibility
/// BEFORE building the Maze Validator.
/// </summary>
[TestFixture]
public class MovementStopToStopTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(500f, 500f, 0f);

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

    private static Vector2 CellCenter(int cx, int cy) =>
        new Vector2(ArenaOrigin.x + cx + 0.5f, ArenaOrigin.y + cy + 0.5f);

    /// <summary>
    /// Builds an authentic Wall-layer Tilemap + CompositeCollider2D from an ASCII grid (row 0 = top row, '#' = wall).
    /// </summary>
    private void BuildAsciiWallTilemap(string[] rowsTopToBottom)
    {
        int wallLayer = LayerMask.NameToLayer("Wall");
        Assert.GreaterOrEqual(wallLayer, 0, "Project must define a 'Wall' layer.");

        TileBase wallTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Ground Palette.asset");
        bool createdTempTile = false;
        if (wallTile == null)
        {
            var temp = ScriptableObject.CreateInstance<Tile>();
            temp.colliderType = Tile.ColliderType.Grid;
            wallTile = temp;
            createdTempTile = true;
        }

        var gridGo = Track(new GameObject("TestGrid"));
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

        int height = rowsTopToBottom.Length;
        for (int r = 0; r < height; r++)
        {
            string row = rowsTopToBottom[r];
            int cy = height - 1 - r;
            for (int cx = 0; cx < row.Length; cx++)
            {
                if (row[cx] == '#')
                    tm.SetTile(new Vector3Int(cx, cy, 0), wallTile);
            }
        }

        tm.RefreshAllTiles();
        tmCol.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();

        if (createdTempTile)
            Object.DestroyImmediate(wallTile);
    }

    private PlayerMovement CreateTestPlayer(Vector2 startWorldPos, float speed = 30f, bool bufferTurnsUntilStop = true)
    {
        var go = Track(new GameObject("TestPlayer"));
        go.transform.position = startWorldPos;

        var rb = go.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;

        var col = go.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.90f, 0.90f);
        col.isTrigger = false;

        var pm = go.AddComponent<PlayerMovement>();
        var so = new SerializedObject(pm);
        so.FindProperty("moveSpeed").floatValue = speed;
        so.FindProperty("wallLayers").intValue = LayerMask.GetMask("Wall");
        so.FindProperty("turnClearance").floatValue = 0.35f;
        so.FindProperty("moveWithHeldDirection").boolValue = false;
        so.FindProperty("bufferTurnsUntilStop").boolValue = bufferTurnsUntilStop;
        so.ApplyModifiedPropertiesWithoutUndo();

        pm.EnsureInitialized();
        pm.TeleportTo(startWorldPos);
        Physics2D.SyncTransforms();
        return pm;
    }

    private static void Tick(PlayerMovement pm, int steps = 1)
    {
        for (int i = 0; i < steps; i++)
        {
            pm.SimulateStep(Dt);
            Physics2D.Simulate(Dt);
            Physics2D.SyncTransforms();
        }
    }

    [Test]
    public void StopToStop_MidSlidePerpendicularSwipe_SkipsSideBranchAndExecutesAtWallStop()
    {
        string[] map =
        {
            "#######", // cy=5
            "###.#.#", // cy=4
            "###.#.#", // cy=3
            "###.#.#", // cy=2
            "#.....#", // cy=1
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        // Use speed=4f (0.08 u/step) so player steps right into the cx=3.50 window — proving
        // bufferTurnsUntilStop=true intentionally ignores the mid-slide side branch!
        var player = CreateTestPlayer(CellCenter(1, 1), speed: 4f, bufferTurnsUntilStop: true);

        player.QueueDirection(Vector2.right);
        Tick(player, 5);
        Assert.IsTrue(player.IsMoving);

        // Queue perpendicular Up BEFORE reaching the mid-corridor branch at cx=3!
        player.QueueDirection(Vector2.up);

        // Run enough ticks to reach the East wall at (5,1) AND execute the buffered Up slide to (5,4)
        Tick(player, 140);

        Assert.IsFalse(player.IsMoving, "Player should have come to rest at the top of the end lane.");
        Vector2 expectedEnd = CellCenter(5, 4);
        Assert.AreEqual(expectedEnd.x, player.transform.position.x, 0.08f, "Should NOT have turned early at cx=3; must turn at end wall cx=5.");
        Assert.AreEqual(expectedEnd.y, player.transform.position.y, 0.08f, "Buffered Up swipe should carry player to top of cx=5 lane.");
    }

    [Test]
    public void StopToStop_BlockedBufferedTurnAtWallStop_ClearsWithoutGhostTurnLater()
    {
        string[] map =
        {
            "#######", // cy=5
            "###.###", // cy=4
            "###.###", // cy=3
            "###.###", // cy=2
            "#.....#", // cy=1
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        var player = CreateTestPlayer(CellCenter(1, 1), speed: 12f, bufferTurnsUntilStop: true);

        player.QueueDirection(Vector2.right);
        Tick(player, 2);
        player.QueueDirection(Vector2.up); // Blocked at (5,1)!

        Tick(player, 40);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(5, 1).x, player.transform.position.x, 0.08f);
        Assert.AreEqual(CellCenter(5, 1).y, player.transform.position.y, 0.08f);

        // Slide Left back past cx=3 — discarded Up buffer must NOT ghost-fire!
        player.QueueDirection(Vector2.left);
        Tick(player, 40);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(1, 1).x, player.transform.position.x, 0.08f);
        Assert.AreEqual(CellCenter(1, 1).y, player.transform.position.y, 0.08f);
    }

    [Test]
    public void StopToStop_MidSlideReversal_AppliesImmediatelyWithoutReachingFarWall()
    {
        string[] map =
        {
            "#########", // cy=2
            "#.......#", // cy=1 (cx=1..7)
            "#########", // cy=0
        };
        BuildAsciiWallTilemap(map);

        var player = CreateTestPlayer(CellCenter(1, 1), speed: 15f, bufferTurnsUntilStop: true);
        float maxObservedX = player.transform.position.x;

        player.QueueDirection(Vector2.right);
        for (int i = 0; i < 6; i++)
        {
            Tick(player, 1);
            maxObservedX = Mathf.Max(maxObservedX, player.transform.position.x);
        }

        player.QueueDirection(Vector2.left);
        for (int i = 0; i < 40; i++)
        {
            Tick(player, 1);
            maxObservedX = Mathf.Max(maxObservedX, player.transform.position.x);
        }

        Assert.Less(maxObservedX, CellCenter(5, 1).x, "Reversal must take effect immediately mid-corridor.");
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(1, 1).x, player.transform.position.x, 0.08f);
    }

    [Test]
    public void StopToStop_GravityBlockAnchor_ClearsBufferedPerpendicularInput()
    {
        string[] map =
        {
            "#######", // cy=5
            "###.###", // cy=4
            "###.###", // cy=3
            "###.###", // cy=2
            "#.....#", // cy=1
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        var gbGo = Track(new GameObject("GravityBlock_Test"));
        gbGo.transform.position = CellCenter(3, 1);
        var gbCol = gbGo.AddComponent<BoxCollider2D>();
        gbCol.size = new Vector2(0.90f, 0.90f);
        gbCol.isTrigger = true;
        gbGo.AddComponent<GravityBlock>();
        Physics2D.SyncTransforms();

        var player = CreateTestPlayer(CellCenter(1, 1), speed: 12f, bufferTurnsUntilStop: true);
        player.QueueDirection(Vector2.right);
        Tick(player, 2);

        player.QueueDirection(Vector2.up);
        Tick(player, 30);

        Assert.IsFalse(player.IsMoving);
        Assert.IsTrue(player.IsAwaitingFreshInput);
        Assert.AreEqual(CellCenter(3, 1).x, player.transform.position.x, 0.05f);
        Assert.AreEqual(CellCenter(3, 1).y, player.transform.position.y, 0.05f);

        player.QueueDirection(Vector2.up);
        Tick(player, 35);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(3, 4).y, player.transform.position.y, 0.08f);
    }

    [Test]
    public void LegacyToggleFalse_PreservesMidCorridorTurnWhenAlignedInWindow()
    {
        string[] map =
        {
            "#######", // cy=5
            "###.###", // cy=4
            "###.###", // cy=3
            "###.###", // cy=2
            "#.....#", // cy=1
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        // Place player near cx=3.48 (inside the 0.90 collider clearance window [3.46..3.54]) with legacy toggle false
        var player = CreateTestPlayer(CellCenter(1, 1), speed: 4f, bufferTurnsUntilStop: false);
        player.QueueDirection(Vector2.right);
        Tick(player, 25); // 1.50 + 25 * 0.08 = 3.50 exact junction centre
        player.QueueDirection(Vector2.up);
        Tick(player, 60);

        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(3, 4).x, player.transform.position.x, 0.08f);
        Assert.AreEqual(CellCenter(3, 4).y, player.transform.position.y, 0.08f);
    }

    [Test]
    public void StopToStop_ChainedPerpendicularWallTurns_NeverTunnelOnCompositeCollider()
    {
        // C03_Chicane + corners inside a single merged CompositeCollider2D:
        // tests 4 consecutive flush-along-wall stops and perpendicular launches.
        string[] map =
        {
            "#######", // cy=4
            "##...##", // cy=3 (cx=2..4)
            "#..#..#", // cy=2 (cx=1,2 and cx=4,5)
            "#####.#", // cy=1 (cx=5)
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        var player = CreateTestPlayer(CellCenter(1, 2), speed: 20f, bufferTurnsUntilStop: true);

        // 1: Right -> stops at (2,2)
        player.QueueDirection(Vector2.right);
        Tick(player, 20);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(2, 2).x, player.transform.position.x, 0.08f);

        // 2: Up -> stops at (2,3)
        player.QueueDirection(Vector2.up);
        Tick(player, 20);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(2, 3).y, player.transform.position.y, 0.08f);

        // 3: Right -> stops at (4,3)
        player.QueueDirection(Vector2.right);
        Tick(player, 20);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(4, 3).x, player.transform.position.x, 0.08f);

        // 4: Down -> stops at (4,2)
        player.QueueDirection(Vector2.down);
        Tick(player, 20);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(4, 2).y, player.transform.position.y, 0.08f);

        // 5: Right -> stops at (5,2), then Down -> stops at (5,1)
        player.QueueDirection(Vector2.right);
        Tick(player, 20);
        player.QueueDirection(Vector2.down);
        Tick(player, 20);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(5, 1).x, player.transform.position.x, 0.08f);
        Assert.AreEqual(CellCenter(5, 1).y, player.transform.position.y, 0.08f);
    }

    [Test]
    public void StopToStop_SlidePastTJunctionAfterFlushWallStop_NeverSnagsCornerSkinBevel()
    {
        // Reproduces Box2D defaultContactOffset (0.01) corner-bevel graze across a 1-cell T-junction opening:
        // 1) Slide West from (4,1) -> stops flush against wall (2,1) at x ~ 3.456.
        // 2) Slide Up along cx=3 past open West branch (1..2, 3) and resumed West wall (2,4) -> must reach (3,5) without snagging at (3,3).
        string[] map =
        {
            "#######", // cy=6
            "###.###", // cy=5 (expected top stop at (3,5))
            "###.###", // cy=4 (wall resumes at (2,4) -> corner vertex at x=3.0, y=4.0)
            "#...###", // cy=3 (1-cell T-junction opening on West side)
            "###.###", // cy=2
            "###..##", // cy=1 (cx=3,4)
            "#######", // cy=0
        };
        BuildAsciiWallTilemap(map);

        var player = CreateTestPlayer(CellCenter(4, 1), speed: 30f, bufferTurnsUntilStop: true);

        player.QueueDirection(Vector2.left);
        Tick(player, 25);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(3, 1).x, player.transform.position.x, 0.08f);

        player.QueueDirection(Vector2.up);
        Tick(player, 40);
        Assert.IsFalse(player.IsMoving);
        Assert.AreEqual(CellCenter(3, 5).x, player.transform.position.x, 0.08f);
        Assert.AreEqual(CellCenter(3, 5).y, player.transform.position.y, 0.08f,
            "Player must glide cleanly past T-junction corner bevel at y=4.0 and stop at head-on wall (3,5).");
    }
}
