using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 3 unit and integration tests for <see cref="MazeGenerator"/>:
/// verifies seed determinism, seed diversity, 100% hard-validation pass rate across
/// <c>Learn</c>, <c>Navigate</c>, and <c>Master</c> profiles, custom slot overrides,
/// invalid configuration handling, <see cref="MazeMapData"/> round-trip serialization,
/// and 1:1 runtime <see cref="PlayerMovement"/> physics parity on a generated maze.
/// </summary>
[TestFixture]
public class MazeGeneratorTests
{
    private const float Dt = 0.02f;
    private static readonly Vector3 ArenaOrigin = new Vector3(1200f, 500f, 0f);

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

    private static MazePatternLibrary LoadLibrary()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(lib);
        return lib;
    }

    private static MazeDifficultyProfile LoadProfile(string name)
    {
        var p = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"Assets/Data/MazeGen/Profiles/Profile_{name}.asset");
        Assert.IsNotNull(p);
        return p;
    }

    [Test]
    public void Generator_SameSeedAndProfile_ProducesBitIdenticalMazeAndMetrics()
    {
        MazePatternLibrary lib = LoadLibrary();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");

        var req = new MazeGenerationRequest
        {
            Seed = 20261029,
            Profile = navigate,
            Library = lib,
        };

        MazeGenerationResult r1 = MazeGenerator.Generate(in req);
        MazeGenerationResult r2 = MazeGenerator.Generate(in req);

        Assert.IsTrue(r1.Success, r1.FailureReason);
        Assert.IsTrue(r2.Success, r2.FailureReason);
        Assert.AreEqual(r1.AttemptsUsed, r2.AttemptsUsed);
        Assert.AreEqual(r1.Grid.Spawn, r2.Grid.Spawn);
        Assert.AreEqual(r1.Grid.Exit, r2.Grid.Exit);
        Assert.AreEqual(r1.Score.QualityScore, r2.Score.QualityScore, 0.0001f);
        Assert.AreEqual(r1.Score.DifficultyFit, r2.Score.DifficultyFit, 0.0001f);
        CollectionAssert.AreEqual(r1.Grid.ToAsciiLines(true), r2.Grid.ToAsciiLines(true));
        CollectionAssert.AreEqual(r1.Validation.SlideGraph.SolutionStops, r2.Validation.SlideGraph.SolutionStops);
        CollectionAssert.AreEqual(r1.Validation.SlideGraph.SolutionDirections, r2.Validation.SlideGraph.SolutionDirections);
    }

    [Test]
    public void Generator_DifferentSeeds_ProduceDistinctLayouts()
    {
        MazePatternLibrary lib = LoadLibrary();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");

        var reqA = new MazeGenerationRequest { Seed = 101, Profile = navigate, Library = lib };
        var reqB = new MazeGenerationRequest { Seed = 202, Profile = navigate, Library = lib };

        MazeGenerationResult a = MazeGenerator.Generate(in reqA);
        MazeGenerationResult b = MazeGenerator.Generate(in reqB);

        Assert.IsTrue(a.Success);
        Assert.IsTrue(b.Success);

        string asciiA = string.Join("\n", a.Grid.ToAsciiLines(true));
        string asciiB = string.Join("\n", b.Grid.ToAsciiLines(true));
        Assert.AreNotEqual(asciiA, asciiB, "Distinct seeds must generate distinct maze layouts.");
    }

    [Test]
    public void Generator_AllThreeDifficultyProfiles_PassHardValidationZeroSoftLocksAcrossMultipleSeeds()
    {
        MazePatternLibrary lib = LoadLibrary();
        string[] tiers = { "Learn", "Navigate", "Master" };
        int[] seeds = { 11, 42, 77, 1337, 9001 };

        foreach (string tier in tiers)
        {
            MazeDifficultyProfile profile = LoadProfile(tier);
            int targetScoreMetCount = 0;

            foreach (int seed in seeds)
            {
                var req = new MazeGenerationRequest
                {
                    Seed = seed,
                    Profile = profile,
                    Library = lib,
                };

                MazeGenerationResult res = MazeGenerator.Generate(in req);
                Assert.IsTrue(res.Success, $"Tier '{tier}' seed={seed} failed: {res.FailureReason}");
                Assert.IsNotNull(res.Grid);
                Assert.IsNotNull(res.Validation);
                Assert.IsTrue(res.Validation.IsValid, $"Tier '{tier}' seed={seed} validation failed.");
                Assert.IsTrue(res.Validation.IsFreshFor(res.Grid), "Returned validation report must match Grid.Revision.");

                // Re-run independent MazeValidator check to confirm zero soft-locks & stop-cell Exit
                MazeValidationReport independent = MazeValidator.Validate(res.Grid, requireExitOnStopCell: true);
                Assert.IsTrue(independent.IsValid);
                Assert.IsEmpty(independent.SlideGraph.SoftLockedStops, $"Tier '{tier}' seed={seed} had soft-locks.");
                Assert.IsTrue(independent.SlideGraph.IsExitStopReached, $"Tier '{tier}' seed={seed} Exit must be a stop cell.");

                // Verify Chamber cap is strictly honored
                Assert.LessOrEqual(res.Telemetry.ChamberSlotCount, profile.maxChambers,
                    $"Tier '{tier}' seed={seed} exceeded maxChambers ({profile.maxChambers}).");

                if (res.MetTargetScore)
                    targetScoreMetCount++;

                // Verify MazeMapData round-trip serialization
                MazeMapData mapData = res.CreateMapDataAsset(profile.profileId, lib.libraryVersion);
                try
                {
                    Assert.IsNotNull(mapData);
                    MazeGrid restored = mapData.ToMazeGrid();
                    CollectionAssert.AreEqual(res.Grid.ToAsciiLines(true), restored.ToAsciiLines(true));
                    Assert.AreEqual(res.Validation.SlideGraph.SolutionMoves, mapData.solutionMoves);
                }
                finally
                {
                    if (mapData != null)
                        Object.DestroyImmediate(mapData);
                }
            }

            Assert.GreaterOrEqual(targetScoreMetCount, 4,
                $"Tier '{tier}' should meet target QualityScore >= {profile.targetScore} on at least 4/5 seeds (met {targetScoreMetCount}/5).");
        }
    }

    [Test]
    public void Generator_CustomPresetOverrideAndInvalidConfig_BehaveCorrectly()
    {
        MazePatternLibrary lib = LoadLibrary();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");

        var customReq = new MazeGenerationRequest
        {
            Seed = 555,
            Profile = navigate,
            Library = lib,
            OverridePreset = MazeSizePreset.Custom,
            OverrideSlotCols = 4,
            OverrideSlotRows = 4,
        };

        MazeGenerationResult customRes = MazeGenerator.Generate(in customReq);
        Assert.IsTrue(customRes.Success, customRes.FailureReason);
        Assert.AreEqual(17, customRes.Grid.Width, "4 cols * 4 + 1 = 17 cells wide.");
        Assert.AreEqual(17, customRes.Grid.Height, "4 rows * 4 + 1 = 17 cells tall.");

        // Invalid weights must fail cleanly with Success == false
        var badProfile = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
        try
        {
            badProfile.playabilityWeight = 0f;
            badProfile.diversityWeight = 0f;
            badProfile.layoutWeight = 0f;

            var badReq = new MazeGenerationRequest { Seed = 1, Profile = badProfile, Library = lib };
            MazeGenerationResult badRes = MazeGenerator.Generate(in badReq);
            Assert.IsFalse(badRes.Success);
            Assert.IsNotEmpty(badRes.FailureReason);
            Assert.IsNull(badRes.Grid);
        }
        finally
        {
            Object.DestroyImmediate(badProfile);
        }
    }

    [Test]
    public void Generator_GeneratedNavigateMaze_MatchesRuntimePlayerMovementPhysics1To1()
    {
        MazePatternLibrary lib = LoadLibrary();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");

        var req = new MazeGenerationRequest
        {
            Seed = 777,
            Profile = navigate,
            Library = lib,
        };

        MazeGenerationResult res = MazeGenerator.Generate(in req);
        Assert.IsTrue(res.Success, res.FailureReason);
        Assert.GreaterOrEqual(res.Validation.SlideGraph.SolutionMoves, 6);

        // Build authentic WallTilemap + CompositeCollider2D physics arena from res.Grid
        int wallLayer = LayerMask.NameToLayer("Wall");
        TileBase wallTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Ground Palette.asset");
        bool createdTempTile = false;
        if (wallTile == null)
        {
            var t = ScriptableObject.CreateInstance<Tile>();
            t.colliderType = Tile.ColliderType.Grid;
            wallTile = t;
            createdTempTile = true;
        }

        var gridGo = new GameObject("GenParityGrid");
        spawned.Add(gridGo);
        gridGo.transform.position = ArenaOrigin;
        var unityGrid = gridGo.AddComponent<Grid>();
        unityGrid.cellSize = new Vector3(1f, 1f, 0f);

        var wallGo = new GameObject("WallTilemap");
        wallGo.layer = wallLayer;
        wallGo.transform.SetParent(gridGo.transform, false);

        var tm = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();
        var tmCol = wallGo.AddComponent<TilemapCollider2D>();
        tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        var wallRb = wallGo.AddComponent<Rigidbody2D>();
        wallRb.bodyType = RigidbodyType2D.Static;
        var composite = wallGo.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;

        for (int gx = 0; gx < res.Grid.Width; gx++)
            for (int gy = 0; gy < res.Grid.Height; gy++)
                if (res.Grid.GetCell(gx, gy) == MazeCell.Wall)
                    tm.SetTile(new Vector3Int(gx, gy, 0), wallTile);

        tm.RefreshAllTiles();
        tmCol.ProcessTilemapChanges();
        composite.GenerateGeometry();
        Physics2D.SyncTransforms();

        if (createdTempTile)
            Object.DestroyImmediate(wallTile);

        Vector3 spawnWorld = unityGrid.GetCellCenterWorld(new Vector3Int(res.Grid.Spawn.Value.x, res.Grid.Spawn.Value.y, 0));
        var playerGo = new GameObject("GenParityPlayer");
        spawned.Add(playerGo);
        playerGo.transform.position = spawnWorld;

        var rb = playerGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        var col = playerGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.90f, 0.90f);

        var pm = playerGo.AddComponent<PlayerMovement>();
        var so = new SerializedObject(pm);
        so.FindProperty("moveSpeed").floatValue = 30f;
        so.FindProperty("wallLayers").intValue = LayerMask.GetMask("Wall");
        so.FindProperty("turnClearance").floatValue = 0.35f;
        so.FindProperty("moveWithHeldDirection").boolValue = false;
        so.FindProperty("bufferTurnsUntilStop").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();

        pm.EnsureInitialized();
        pm.TeleportTo(spawnWorld);
        Physics2D.SyncTransforms();

        IReadOnlyList<MazeDir> dirs = res.Validation.SlideGraph.SolutionDirections;
        IReadOnlyList<Vector2Int> stops = res.Validation.SlideGraph.SolutionStops;

        for (int i = 0; i < dirs.Count; i++)
        {
            pm.QueueDirection(MazeDirectionUtil.ToWorldVector(dirs[i]));
            bool stopped = false;
            for (int t = 0; t < 350; t++)
            {
                pm.SimulateStep(Dt);
                Physics2D.Simulate(Dt);
                Physics2D.SyncTransforms();
                if (!pm.IsMoving && !pm.HasPendingDirection)
                {
                    stopped = true;
                    break;
                }
            }

            Assert.IsTrue(stopped, $"Player did not stop on move #{i + 1} ({dirs[i]}).");
            Vector3Int c3 = unityGrid.WorldToCell(pm.transform.position);
            Assert.AreEqual(stops[i + 1], new Vector2Int(c3.x, c3.y),
                $"Move #{i + 1} ({dirs[i]}) landed at ({c3.x},{c3.y}) instead of expected stop {stops[i + 1]}.");
        }

        Assert.AreEqual(res.Grid.Exit.Value, stops[stops.Count - 1]);
    }
}

