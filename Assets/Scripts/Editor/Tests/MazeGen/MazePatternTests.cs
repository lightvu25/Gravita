using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 1 unit tests for 5x5 <see cref="MazePattern"/> modules, <see cref="MazePatternLibrary"/>,
/// <see cref="MazeGrid"/> shared-edge stamping (stride 4), <see cref="MazeDifficultyProfile"/> weight/band rules,
/// and stop-state behavior across all 11 base patterns under Gravita's sliding rules.
/// </summary>
[TestFixture]
public class MazePatternTests
{
    private static MazePatternLibrary LoadLibrary()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(
            lib,
            $"Missing '{MazePatternBootstrap.LibraryPath}'. Run 'Tools/Gravita/Maze/Create Or Refresh Base Assets' before executing tests.");
        return lib;
    }

    [Test]
    public void BaseLibrary_AllElevenPatternsExistAreValidAndCoverAllFifteenMasks()
    {
        MazePatternLibrary lib = LoadLibrary();
        Assert.AreEqual(11, lib.patterns.Count, "Base library must contain all 11 audited 5x5 patterns.");

        bool valid = lib.ValidateLibrary(out List<string> errors);
        Assert.IsTrue(valid, "Library validation failed: " + string.Join("; ", errors));

        for (int maskBits = 1; maskBits <= 15; maskBits++)
        {
            var candidates = lib.GetCandidates((MazeSideMask)maskBits);
            Assert.IsNotEmpty(candidates, $"Port mask {(MazeSideMask)maskBits} ({maskBits}) must have at least one valid candidate.");
        }
    }

    [Test]
    public void PatternValidation_RejectsInvalidSizeCharsBorderPortsAndDiagonalDisconnects()
    {
        var bad = ScriptableObject.CreateInstance<MazePattern>();
        try
        {
            bad.id = "Bad_Border";
            bad.rows = new[]
            {
                "#...#",
                "#####",
                ".....",
                "#####",
                "#####",
            };
            Assert.IsFalse(bad.ValidateDefinition(out List<string> borderErrs));
            Assert.IsNotEmpty(borderErrs);

            bad.id = "Bad_Diagonal";
            bad.rows = new[]
            {
                "#####",
                "##..#",
                "..#..",
                "#####",
                "#####",
            };
            Assert.IsFalse(bad.ValidateDefinition(out List<string> diagErrs));
            StringAssert.Contains("not 4-connected", string.Join("; ", diagErrs));

            bad.id = "Bad_Char";
            bad.rows = new[]
            {
                "#####",
                "#####",
                "..X..",
                "#####",
                "#####",
            };
            Assert.IsFalse(bad.ValidateDefinition(out List<string> charErrs));
            StringAssert.Contains("illegal character", string.Join("; ", charErrs));
        }
        finally
        {
            Object.DestroyImmediate(bad);
        }
    }

    [Test]
    public void PatternRotation_ClockwiseQuarterTurnsMatchDerivedPortMasksExactly()
    {
        MazePatternLibrary lib = LoadLibrary();
        foreach (MazePattern pattern in lib.patterns)
        {
            MazeSideMask baseMask = pattern.GetPortMask(0);
            for (int rot = 0; rot < 4; rot++)
            {
                MazeSideMask expected = MazeDirectionUtil.RotateMaskClockwise(baseMask, rot);
                MazeSideMask actual = pattern.GetPortMask(rot);
                Assert.AreEqual(expected, actual, $"Pattern '{pattern.id}' rot={rot} derived port mask mismatch.");
            }

            for (int r = 0; r < MazePattern.ModuleSize; r++)
            {
                for (int c = 0; c < MazePattern.ModuleSize; c++)
                {
                    Assert.AreEqual(pattern.GetCell(c, r, 0), pattern.GetCell(c, r, 4));
                }
            }
        }
    }

    [Test]
    public void SharedEdgeStamping_AcceptsMatchingPortsAndRejectsConflictingBordersAtomically()
    {
        MazePatternLibrary lib = LoadLibrary();
        MazePattern straight = lib.patterns.Find(p => p.id == "C01_Straight");
        MazePattern deadEnd = lib.patterns.Find(p => p.id == "D01_DeadEnd");

        var grid = new MazeGrid(9, 5);
        Assert.IsTrue(grid.TryStampPattern(straight, 0, 0, 0, out string err0), err0);

        int revBeforeConflict = grid.Revision;
        // D01_DeadEnd at rot=2 exposes only East port (West border at col=0 is Wall), conflicting with slot(0,0)'s open East port.
        Assert.IsFalse(grid.TryStampPattern(deadEnd, 2, 1, 0, out string conflictErr));
        StringAssert.Contains("Shared border conflict", conflictErr);
        Assert.AreEqual(revBeforeConflict, grid.Revision, "Failed stamp must not mutate grid or increment Revision.");

        Assert.IsTrue(grid.TryStampPattern(deadEnd, 0, 1, 0, out string err1), err1);
        Assert.AreEqual(MazeCell.Floor, grid.GetCell(4, 2), "Shared port cell (4,2) must be open Floor.");
    }

    [Test]
    public void Profiles_AllThreeTiersCoverRequiredMasksAndEnforceQualityWeightAndBandRules()
    {
        MazePatternLibrary lib = LoadLibrary();
        string[] paths =
        {
            "Assets/Data/MazeGen/Profiles/Profile_Learn.asset",
            "Assets/Data/MazeGen/Profiles/Profile_Navigate.asset",
            "Assets/Data/MazeGen/Profiles/Profile_Master.asset",
        };

        foreach (string path in paths)
        {
            var profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(path);
            Assert.IsNotNull(profile, $"Missing profile at {path}");
            Assert.LessOrEqual(profile.minSolutionMoves, profile.maxSolutionMoves);
            Assert.LessOrEqual(profile.minDecisionStops, profile.maxDecisionStops);
            Assert.LessOrEqual(profile.minStopGraphLoops, profile.maxStopGraphLoops);
            Assert.IsTrue(profile.TryGetNormalizedQualityWeights(out float wP, out float wD, out float wL, out string wErr), wErr);
            Assert.AreEqual(1f, wP + wD + wL, 0.0001f);

            var allowed = profile.BuildAllowedGroupSet();
            for (int maskBits = 1; maskBits <= 15; maskBits++)
            {
                var cands = lib.GetCandidates((MazeSideMask)maskBits, allowed);
                Assert.IsNotEmpty(cands, $"Profile '{profile.profileId}' lacks candidates for port mask {(MazeSideMask)maskBits}.");
            }
        }

        // GDD §9.3 weight normalization & rejection checks
        Assert.IsTrue(MazeDifficultyProfile.TryNormalizeWeights(4f, 3.5f, 2.5f, out float np, out float nd, out float nl, out _));
        Assert.AreEqual(0.40f, np, 0.0001f);
        Assert.AreEqual(0.35f, nd, 0.0001f);
        Assert.AreEqual(0.25f, nl, 0.0001f);

        Assert.IsFalse(MazeDifficultyProfile.TryNormalizeWeights(-1f, 50f, 50f, out _, out _, out _, out _));
        Assert.IsFalse(MazeDifficultyProfile.TryNormalizeWeights(0f, 0f, 0f, out _, out _, out _, out _));
        Assert.IsFalse(MazeDifficultyProfile.TryNormalizeWeights(float.NaN, 1f, 1f, out _, out _, out _, out _));
        Assert.IsFalse(MazeDifficultyProfile.TryNormalizeWeights(float.PositiveInfinity, 1f, 1f, out _, out _, out _, out _));

        // OnValidate clamps inverted min/max bands
        var tempProfile = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
        try
        {
            tempProfile.minSolutionMoves = 15;
            tempProfile.maxSolutionMoves = 5;
            tempProfile.minDecisionStops = 6;
            tempProfile.maxDecisionStops = 2;
            tempProfile.minStopGraphLoops = 4;
            tempProfile.maxStopGraphLoops = 1;
            MethodInfo onValidate = typeof(MazeDifficultyProfile).GetMethod("OnValidate", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(onValidate);
            onValidate.Invoke(tempProfile, null);

            Assert.AreEqual(15, tempProfile.maxSolutionMoves);
            Assert.AreEqual(6, tempProfile.maxDecisionStops);
            Assert.AreEqual(4, tempProfile.maxStopGraphLoops);
        }
        finally
        {
            Object.DestroyImmediate(tempProfile);
        }
    }

    [Test]
    public void PatternStopStates_AllElevenModulesBehaveAsSpecifiedUnderSlideSolver()
    {
        MazePatternLibrary lib = LoadLibrary();

        // Stamp a 5x5 module at gx=0..4, gy=0..4 (note: bottom-up gy = 4 - topDownRow).
        MazeGrid StampSingle(string id)
        {
            MazePattern p = lib.patterns.Find(x => x.id == id);
            Assert.IsNotNull(p, id);
            var g = new MazeGrid(5, 5);
            Assert.IsTrue(g.TryStampPattern(p, 0, 0, 0, out string err), err);
            return g;
        }

        // 1. C01_Straight: pure runway between West (0,2) and East (4,2)
        MazeGrid straight = StampSingle("C01_Straight");
        Assert.AreEqual(new Vector2Int(4, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, straight.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(0, 2), GridSlideSolver.SolveSlide(new Vector2Int(4, 2), Vector2Int.left, straight.IsWallPredicate).StopCell);

        // 2. C02_Corner: West (0,2) and South (2,0) both stop at centre (2,2)
        MazeGrid corner = StampSingle("C02_Corner");
        Assert.AreEqual(new Vector2Int(2, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, corner.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 2), GridSlideSolver.SolveSlide(new Vector2Int(2, 0), Vector2Int.up, corner.IsWallPredicate).StopCell);

        // 3. C03_Chicane: 4-stop bidirectional jog (0,2)->(1,2)->(1,3)->(3,3)->(3,2)->(4,2)
        MazeGrid chicane = StampSingle("C03_Chicane");
        var s1 = GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, chicane.IsWallPredicate);
        Assert.AreEqual(new Vector2Int(1, 2), s1.StopCell);
        var s2 = GridSlideSolver.SolveSlide(s1.StopCell, Vector2Int.up, chicane.IsWallPredicate);
        Assert.AreEqual(new Vector2Int(1, 3), s2.StopCell);
        var s3 = GridSlideSolver.SolveSlide(s2.StopCell, Vector2Int.right, chicane.IsWallPredicate);
        Assert.AreEqual(new Vector2Int(3, 3), s3.StopCell);
        var s4 = GridSlideSolver.SolveSlide(s3.StopCell, Vector2Int.down, chicane.IsWallPredicate);
        Assert.AreEqual(new Vector2Int(3, 2), s4.StopCell);
        var s5 = GridSlideSolver.SolveSlide(s4.StopCell, Vector2Int.right, chicane.IsWallPredicate);
        Assert.AreEqual(new Vector2Int(4, 2), s5.StopCell);

        // 4. I01_TJunction: North stem (2,4) stops at (2,2); West<->East is a fast main line
        MazeGrid tJunc = StampSingle("I01_TJunction");
        Assert.AreEqual(new Vector2Int(2, 2), GridSlideSolver.SolveSlide(new Vector2Int(2, 4), Vector2Int.down, tJunc.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(4, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, tJunc.IsWallPredicate).StopCell);

        // 5. I02_TBaffled: all 3 ports (W, N, E) stop inside the module (right-hand stem chirality)
        MazeGrid tBaffled = StampSingle("I02_TBaffled");
        Assert.AreEqual(new Vector2Int(1, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, tBaffled.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 3), GridSlideSolver.SolveSlide(new Vector2Int(2, 4), Vector2Int.down, tBaffled.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(3, 2), GridSlideSolver.SolveSlide(new Vector2Int(4, 2), Vector2Int.left, tBaffled.IsWallPredicate).StopCell);

        // 6. I03_Cross: 4-way pass-through crossover along both axes
        MazeGrid cross = StampSingle("I03_Cross");
        Assert.AreEqual(new Vector2Int(4, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, cross.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 0), GridSlideSolver.SolveSlide(new Vector2Int(2, 4), Vector2Int.down, cross.IsWallPredicate).StopCell);

        // 7. L01_RingHub: all 4 ports stop inside around centre baffle (2,2)
        MazeGrid ring = StampSingle("L01_RingHub");
        Assert.AreEqual(new Vector2Int(1, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, ring.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(3, 2), GridSlideSolver.SolveSlide(new Vector2Int(4, 2), Vector2Int.left, ring.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 3), GridSlideSolver.SolveSlide(new Vector2Int(2, 4), Vector2Int.down, ring.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 1), GridSlideSolver.SolveSlide(new Vector2Int(2, 0), Vector2Int.up, ring.IsWallPredicate).StopCell);

        // 8. D01_DeadEnd: single pocket stop at (2,2) and reversible back to (0,2)
        MazeGrid deadEnd = StampSingle("D01_DeadEnd");
        Assert.AreEqual(new Vector2Int(2, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, deadEnd.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(0, 2), GridSlideSolver.SolveSlide(new Vector2Int(2, 2), Vector2Int.left, deadEnd.IsWallPredicate).StopCell);

        // 9. D02_DeadEndHook: reversible 3-move alcove (0,2)->(1,2)->(1,3)->(2,3) and back to (0,2)
        MazeGrid hook = StampSingle("D02_DeadEndHook");
        var h1 = GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, hook.IsWallPredicate).StopCell;
        var h2 = GridSlideSolver.SolveSlide(h1, Vector2Int.up, hook.IsWallPredicate).StopCell;
        var h3 = GridSlideSolver.SolveSlide(h2, Vector2Int.right, hook.IsWallPredicate).StopCell;
        Assert.AreEqual(new Vector2Int(2, 3), h3);
        var back1 = GridSlideSolver.SolveSlide(h3, Vector2Int.left, hook.IsWallPredicate).StopCell;
        var back2 = GridSlideSolver.SolveSlide(back1, Vector2Int.down, hook.IsWallPredicate).StopCell;
        var back3 = GridSlideSolver.SolveSlide(back2, Vector2Int.left, hook.IsWallPredicate).StopCell;
        Assert.AreEqual(new Vector2Int(0, 2), back3);

        // 10. R01_PillarChamber: both W (0,2) and S (2,0) ports stop inside the 3x3 corner chamber
        MazeGrid chamber2 = StampSingle("R01_PillarChamber");
        Assert.AreEqual(new Vector2Int(3, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, chamber2.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 3), GridSlideSolver.SolveSlide(new Vector2Int(2, 0), Vector2Int.up, chamber2.IsWallPredicate).StopCell);

        // 11. R02_Chamber3Port: all 3 ports (W, N, E) stop inside the chamber (left-hand stem chirality mirror of I02)
        MazeGrid chamber3 = StampSingle("R02_Chamber3Port");
        Assert.AreEqual(new Vector2Int(1, 2), GridSlideSolver.SolveSlide(new Vector2Int(0, 2), Vector2Int.right, chamber3.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(2, 3), GridSlideSolver.SolveSlide(new Vector2Int(2, 4), Vector2Int.down, chamber3.IsWallPredicate).StopCell);
        Assert.AreEqual(new Vector2Int(3, 2), GridSlideSolver.SolveSlide(new Vector2Int(4, 2), Vector2Int.left, chamber3.IsWallPredicate).StopCell);
    }
}
