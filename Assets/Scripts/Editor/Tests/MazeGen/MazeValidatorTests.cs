using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 2 unit tests for <see cref="MazeValidator"/> hard/warning rules,
/// stale <see cref="MazeGrid.Revision"/> invalidation, and <see cref="LevelDataValidator"/>'s
/// non-destructive diagnostic checks (Q7).
/// </summary>
[TestFixture]
public class MazeValidatorTests
{
    [Test]
    public void Validator_RejectsSoftLocksWithHardError_PerQ6()
    {
        string[] trapMap =
        {
            "#######",
            "#.....#",
            "##.####",
            "##.####",
            "##S..E#",
            "#######",
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(trapMap);
        MazeValidationReport report = MazeValidator.Validate(grid, requireExitOnStopCell: true);

        Assert.IsFalse(report.IsValid, "Reachable soft-lock must fail hard validation.");
        Assert.IsTrue(report.HasIssue(MazeValidationCode.SoftLockDetected));
    }

    [Test]
    public void Validator_EnforcesExitOnStopCellForGeneratedMaps_AndAllowsPassThroughWhenDisabled_PerQ3()
    {
        string[] passThroughMap =
        {
            "#######",
            "#S.E..#",
            "#######",
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(passThroughMap);

        MazeValidationReport strictReport = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsFalse(strictReport.IsValid);
        Assert.IsTrue(strictReport.HasIssue(MazeValidationCode.ExitNotStopCell));

        MazeValidationReport legacyReport = MazeValidator.Validate(grid, requireExitOnStopCell: false);
        Assert.IsTrue(legacyReport.IsValid, "Hand-authored pass-through mode should accept mid-corridor Exit.");
        Assert.AreEqual(1, legacyReport.SlideGraph.SolutionMoves);
    }

    [Test]
    public void Validator_DetectsOpenBoundaryInvalidMarkersAndStaleGridRevision()
    {
        string[] validMap =
        {
            "######",
            "#S..E#",
            "######",
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(validMap);
        MazeValidationReport report = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsTrue(report.IsValid);
        Assert.IsTrue(report.IsFreshFor(grid));

        grid.SetCell(2, 1, MazeCell.Wall);
        Assert.IsFalse(report.IsFreshFor(grid), "Grid mutation must immediately mark previous validation report stale.");

        MazeValidationReport blockedReport = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsFalse(blockedReport.IsValid);
        Assert.IsTrue(blockedReport.HasIssue(MazeValidationCode.Unsolvable));

        grid.SetCell(2, 1, MazeCell.Floor);
        grid.SetCell(0, 1, MazeCell.Floor);
        MazeValidationReport openReport = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsFalse(openReport.IsValid);
        Assert.IsTrue(openReport.HasIssue(MazeValidationCode.OpenBoundary));
    }

    [Test]
    public void LevelDataValidator_ReportsLevel001OffGridSpawnAndExitAsWarningWithoutModifyingAssets_PerQ7()
    {
        var level001 = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Levels/Level_001.asset");
        var level1Prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Levels/Level 1.prefab");
        Assert.IsNotNull(level001);
        Assert.IsNotNull(level1Prefab);

        var checks = LevelDataValidator.Validate(level001);

        bool foundAlignmentWarning = false;
        foreach (var check in checks)
        {
            Assert.AreNotEqual(LevelDataValidator.CheckResult.Error, check.Result,
                $"Level_001 must not fail with Error [{check.Label}]: {check.Message}");

            if (check.Label == "Grid Center Alignment" && check.Result == LevelDataValidator.CheckResult.Warning)
            {
                foundAlignmentWarning = true;
                StringAssert.Contains("off-grid", check.Message);
            }
        }

        Assert.IsTrue(foundAlignmentWarning, "LevelDataValidator must report Level_001's off-grid Spawn/Exit as a Warning.");
        Assert.IsFalse(EditorUtility.IsDirty(level001), "Level_001.asset must not be modified.");
        Assert.IsFalse(EditorUtility.IsDirty(level1Prefab), "Level 1.prefab must not be modified.");
    }

    [Test]
    public void LevelDataValidator_GridAlignedLayoutWithOpenDoorway_EmitsWarningNotError()
    {
        // Hand-authored levels with grid-aligned Spawn/Exit and an open outer Tilemap doorway
        // must surface Maze Slide Topology as a diagnostic Warning, never breaking LevelData validation with Error.
        var rootGo = new GameObject("TempHandAuthoredLayout");
        var tempLevelData = ScriptableObject.CreateInstance<LevelData>();
        var tempTile = ScriptableObject.CreateInstance<Tile>();

        try
        {
            rootGo.AddComponent<LevelRoot>();
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(rootGo.transform, false);
            gridGo.AddComponent<Grid>();

            var wallGo = new GameObject("WallTilemap");
            wallGo.layer = LayerMask.NameToLayer("Wall");
            wallGo.transform.SetParent(gridGo.transform, false);
            var tm = wallGo.AddComponent<Tilemap>();

            // 5x5 ring with an intentional open doorway at (4, 2)
            for (int x = 0; x < 5; x++)
            {
                for (int y = 0; y < 5; y++)
                {
                    bool border = x == 0 || x == 4 || y == 0 || y == 4;
                    if (border && !(x == 4 && y == 2))
                        tm.SetTile(new Vector3Int(x, y, 0), tempTile);
                }
            }

            var spawnGo = new GameObject("PlayerSpawn");
            spawnGo.transform.SetParent(rootGo.transform, false);
            spawnGo.transform.position = new Vector3(1.5f, 2.5f, 0f);
            spawnGo.AddComponent<PlayerSpawnPoint>();

            var exitGo = new GameObject("LevelExit");
            exitGo.transform.SetParent(rootGo.transform, false);
            exitGo.transform.position = new Vector3(3.5f, 2.5f, 0f);
            var exitCol = exitGo.AddComponent<BoxCollider2D>();
            exitCol.isTrigger = true;
            exitGo.AddComponent<LevelExit>();

            tempLevelData.levelId = "Temp_Doorway";
            tempLevelData.layoutPrefab = rootGo;
            tempLevelData.baseExpReward = 10;
            tempLevelData.primaryObjective = new ObjectiveDefinition { objectiveType = ObjectiveType.ReachExit };

            var checks = LevelDataValidator.Validate(tempLevelData);
            bool sawTopologyWarning = false;
            foreach (var check in checks)
            {
                Assert.AreNotEqual(LevelDataValidator.CheckResult.Error, check.Result,
                    $"Unexpected Error on [{check.Label}]: {check.Message}");
                if (check.Label == "Maze Slide Topology" && check.Result == LevelDataValidator.CheckResult.Warning)
                    sawTopologyWarning = true;
            }

            Assert.IsTrue(sawTopologyWarning, "Open doorway on hand-authored LevelData should report Warning, not Error.");
        }
        finally
        {
            Object.DestroyImmediate(tempTile);
            Object.DestroyImmediate(tempLevelData);
            Object.DestroyImmediate(rootGo);
        }
    }
}
