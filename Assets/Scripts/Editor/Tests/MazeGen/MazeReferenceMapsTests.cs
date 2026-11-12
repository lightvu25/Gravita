using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 6 final acceptance tests for the 3 frozen reference <see cref="MazeMapData"/> fixtures
/// (<c>RefMap_Learn</c>, <c>RefMap_Navigate</c>, <c>RefMap_Master</c>):
/// verifies provenance, hard stop-graph validation (<c>0</c> soft-locks, stop-cell <c>Exit</c>),
/// <c>QualityScore &gt;= 85</c>, <c>DifficultyFit &gt;= 95</c>, bit-exact seed reproducibility,
/// monotonic tier progression (<c>Learn &lt; Navigate &lt; Master</c>), and 1:1 live
/// <see cref="PlayerMovement"/> + <see cref="CompositeCollider2D"/> physics execution across every move.
/// </summary>
[TestFixture]
public class MazeReferenceMapsTests
{
    private const float Dt = 0.02f;
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
            {
                Undo.ClearUndo(spawned[i]);
                Object.DestroyImmediate(spawned[i]);
            }
        }
        spawned.Clear();
        Physics2D.simulationMode = previousSimMode;
        Physics2D.SyncTransforms();
    }

    [Test]
    public void ReferenceMaps_AllThreeTiersExist_PassHardValidationZeroSoftLocks_MeetQualityAndFit_AndReproduceDeterministically()
    {
        var specs = new (string Path, string Tier, int ExpectedW, int ExpectedH, MazeSizePreset ExpectedPreset)[]
        {
            (MazePatternBootstrap.RefMapLearnPath,    "Learn",     9, 13, MazeSizePreset.Small),
            (MazePatternBootstrap.RefMapNavigatePath, "Navigate", 13, 21, MazeSizePreset.Medium),
            (MazePatternBootstrap.RefMapMasterPath,   "Master",   17, 29, MazeSizePreset.Large),
        };

        MazePatternLibrary library = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(library);

        var loadedMaps = new List<MazeMapData>(3);

        for (int i = 0; i < specs.Length; i++)
        {
            var spec = specs[i];
            MazeMapData mapData = AssetDatabase.LoadAssetAtPath<MazeMapData>(spec.Path);
            Assert.IsNotNull(mapData, $"Missing reference map at '{spec.Path}'.");
            loadedMaps.Add(mapData);

            MazeDifficultyProfile profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
                $"{MazePatternBootstrap.ProfilesFolder}/Profile_{spec.Tier}.asset");
            Assert.IsNotNull(profile);

            // 1. Metadata & footprint checks
            Assert.AreEqual(spec.Tier, mapData.profileId);
            Assert.AreEqual(spec.ExpectedW, mapData.width);
            Assert.AreEqual(spec.ExpectedH, mapData.height);
            Assert.AreEqual(spec.ExpectedPreset, mapData.sizePreset);
            Assert.AreEqual(library.libraryVersion, mapData.patternLibraryVersion);
            Assert.AreEqual(MazeMapData.CurrentGeneratorVersion, mapData.generatorVersion);
            Assert.GreaterOrEqual(mapData.qualityScore, 85f, $"{spec.Tier} reference map should exceed target quality.");
            Assert.GreaterOrEqual(mapData.difficultyFit, 95f, $"{spec.Tier} reference map should sit squarely in tier band.");

            // 2. Independent stop-state graph validation & scorer parity
            MazeGrid grid = mapData.ToMazeGrid();
            MazeValidationReport report = MazeValidator.Validate(grid, requireExitOnStopCell: true);
            Assert.IsTrue(report.IsValid, $"{spec.Tier} reference map failed MazeValidator.");
            Assert.AreEqual(0, report.ErrorCount);
            Assert.IsEmpty(report.SlideGraph.SoftLockedStops, $"{spec.Tier} reference map must have 0 soft-locks.");
            Assert.IsTrue(report.SlideGraph.IsExitStopReached, $"{spec.Tier} reference map Exit must be a stop cell.");

            CollectionAssert.AreEqual(report.SlideGraph.SolutionStops, mapData.solutionStops);
            CollectionAssert.AreEqual(report.SlideGraph.SolutionDirections, mapData.solutionDirections);
            Assert.AreEqual(report.SlideGraph.SolutionMoves, mapData.solutionMoves);
            Assert.AreEqual(report.SlideGraph.DecisionStopsOnSolution, mapData.decisionStops);
            Assert.AreEqual(report.SlideGraph.StopGraphLoops, mapData.stopGraphLoops);

            MazeScoreBreakdown gridOnlyScore = MazeScorer.Evaluate(grid, report, profile);
            Assert.IsTrue(gridOnlyScore.IsValid);
            Assert.GreaterOrEqual(gridOnlyScore.QualityScore, 85f);

            // 3. Deterministic seed reproducibility (including exact telemetry-backed QualityScore & DifficultyFit)
            MazeGenerationResult regen = MazeGenerator.Generate(new MazeGenerationRequest
            {
                Seed = mapData.seed,
                Profile = profile,
                Library = library,
            });
            Assert.IsTrue(regen.Success, regen.FailureReason);
            CollectionAssert.AreEqual(mapData.rows, regen.Grid.ToAsciiLines(includeMarkers: true));
            Assert.AreEqual(regen.Score.QualityScore, mapData.qualityScore, 0.01f);
            Assert.AreEqual(regen.Score.DifficultyFit, mapData.difficultyFit, 0.01f);
        }

        // 4. Strictly increasing progression across Learn -> Navigate -> Master
        Assert.Less(loadedMaps[0].solutionMoves, loadedMaps[1].solutionMoves);
        Assert.Less(loadedMaps[1].solutionMoves, loadedMaps[2].solutionMoves);
        Assert.Less(loadedMaps[0].decisionStops, loadedMaps[1].decisionStops);
        Assert.Less(loadedMaps[1].decisionStops, loadedMaps[2].decisionStops);
        Assert.Less(loadedMaps[0].stopGraphLoops, loadedMaps[1].stopGraphLoops);
        Assert.Less(loadedMaps[1].stopGraphLoops, loadedMaps[2].stopGraphLoops);
    }

    [Test]
    public void ReferenceMaps_AllThreeTiers_ExecuteStepByStepOnLiveTilemapAndPlayerMovementPhysics()
    {
        string[] paths =
        {
            MazePatternBootstrap.RefMapLearnPath,
            MazePatternBootstrap.RefMapNavigatePath,
            MazePatternBootstrap.RefMapMasterPath,
        };

        TileBase wallTile = MazeApplyService.LoadDefaultWallTile();
        Assert.IsNotNull(wallTile);

        for (int m = 0; m < paths.Length; m++)
        {
            MazeMapData mapData = AssetDatabase.LoadAssetAtPath<MazeMapData>(paths[m]);
            Assert.IsNotNull(mapData);

            MazeDifficultyProfile profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
                $"{MazePatternBootstrap.ProfilesFolder}/Profile_{mapData.profileId}.asset");

            var session = new MazePreviewSession();
            session.LoadFromMapData(mapData, profile);
            Assert.IsTrue(session.CanApply, $"Session loaded from '{paths[m]}' must be ready to Apply.");

            // Build isolated physics arena far away from origin for each tier
            var rootGo = new GameObject($"RefArena_{mapData.profileId}");
            spawned.Add(rootGo);
            rootGo.transform.position = new Vector3(2000f + m * 200f, 500f, 0f);
            var levelRoot = rootGo.AddComponent<LevelRoot>();

            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(rootGo.transform, false);
            gridGo.AddComponent<Grid>();

            var wallGo = new GameObject("WallTilemap");
            wallGo.layer = LayerMask.NameToLayer("Wall");
            wallGo.transform.SetParent(gridGo.transform, false);
            var wallTm = wallGo.AddComponent<Tilemap>();
            wallGo.AddComponent<TilemapRenderer>();
            var wallRb = wallGo.AddComponent<Rigidbody2D>();
            wallRb.bodyType = RigidbodyType2D.Static;
            var tmCol = wallGo.AddComponent<TilemapCollider2D>();
            tmCol.compositeOperation = Collider2D.CompositeOperation.Merge;
            var comp = wallGo.AddComponent<CompositeCollider2D>();
            comp.geometryType = CompositeCollider2D.GeometryType.Polygons;

            var spawnGo = new GameObject("PlayerSpawn");
            spawnGo.transform.SetParent(rootGo.transform, false);
            spawnGo.AddComponent<PlayerSpawnPoint>();

            var exitGo = new GameObject("Exit");
            exitGo.transform.SetParent(rootGo.transform, false);
            var exitBox = exitGo.AddComponent<BoxCollider2D>();
            exitBox.isTrigger = true;
            exitGo.AddComponent<LevelExit>();

            MazeApplyTargets targets = MazeApplyTargets.CreateDefault();
            Assert.IsTrue(MazeApplyService.TryBindTargetsFromLevelRoot(levelRoot, ref targets, out string bindErr), bindErr);
            targets.CenterAroundOrigin = false;
            targets.CellOriginOffset = Vector2Int.zero;

            MazeApplyResult applyRes = MazeApplyService.ApplyToTargets(session, in targets, $"RefApply_{mapData.profileId}");
            Assert.IsTrue(applyRes.Success, applyRes.ErrorMessage);
            Physics2D.SyncTransforms();

            var playerGo = new GameObject($"RefPlayer_{mapData.profileId}");
            spawned.Add(playerGo);
            playerGo.transform.position = spawnGo.transform.position;

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
            pm.TeleportTo(spawnGo.transform.position);
            Physics2D.SyncTransforms();

            Grid layoutGrid = wallTm.layoutGrid;
            for (int step = 0; step < mapData.solutionDirections.Count; step++)
            {
                MazeDir dir = mapData.solutionDirections[step];
                pm.QueueDirection(MazeDirectionUtil.ToWorldVector(dir));

                bool stopped = false;
                for (int tick = 0; tick < 400; tick++)
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

                Assert.IsTrue(stopped, $"[{mapData.profileId}] Player failed to stop on move #{step + 1} ({dir}).");
                Vector3Int landedCell = layoutGrid.WorldToCell(pm.transform.position);
                Assert.AreEqual(
                    mapData.solutionStops[step + 1],
                    new Vector2Int(landedCell.x, landedCell.y),
                    $"[{mapData.profileId}] Move #{step + 1} ({dir}) landed on ({landedCell.x},{landedCell.y}) instead of {mapData.solutionStops[step + 1]}.");
            }

            Assert.AreEqual(
                layoutGrid.WorldToCell(exitGo.transform.position),
                layoutGrid.WorldToCell(pm.transform.position),
                $"[{mapData.profileId}] Final physical resting cell must equal Exit cell.");
            Assert.Less(
                Vector2.Distance(pm.transform.position, exitGo.transform.position),
                0.08f,
                $"[{mapData.profileId}] Final physical resting position (0.90x0.90 collider against 1x1 wall) must sit within 0.08u of Exit cell center.");
        }
    }
}