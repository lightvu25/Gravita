using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase 2 unit tests for <see cref="MazeSlideGraph"/> stop-state graph construction,
/// shortest slide path solver (both strict stop-cell and pass-through modes),
/// cyclomatic loop counting, decision-stop counting, and Tarjan terminal-SCC zero-soft-lock candidate selection.
/// </summary>
[TestFixture]
public class MazeSlideGraphTests
{
    [Test]
    public void SlideGraph_FloodFillConnectedCrossWithSideExit_IsUnsolvableUnderSlideRules()
    {
        // Spawn (1,3) slides East/West between (1,3) and (5,3) without stopping at (3,3),
        // so Exit (3,5) in the perpendicular branch is unreachable despite 4-connected floor tiles.
        string[] crossRows =
        {
            "#######", // gy=6
            "###E###", // gy=5
            "###.###", // gy=4
            "#S....#", // gy=3
            "###.###", // gy=2
            "###.###", // gy=1
            "#######", // gy=0
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(crossRows);
        MazeSlideGraph graph = MazeSlideGraph.Build(grid, requireExitOnStopCell: false);

        Assert.IsFalse(graph.IsExitStopReached, "Exit in perpendicular branch should not be a stop cell.");
        Assert.IsFalse(graph.IsExitPassThroughReached, "Exit in perpendicular branch is never crossed.");
        Assert.IsFalse(graph.IsSolvable, "4-connected layout with no stop at junction must be unsolvable.");
    }

    [Test]
    public void SlideGraph_ShortestPathLoopCountAndDecisionCount_AreAccurate()
    {
        // Reversible 4-corner rectangle: (1,4) <-> (5,4) <-> (5,1) <-> (1,1) <-> (1,4).
        string[] loopRows =
        {
            "#######", // gy=5
            "#S...E#", // gy=4
            "#.###.#", // gy=3
            "#.###.#", // gy=2
            "#.....#", // gy=1
            "#######", // gy=0
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(loopRows);
        MazeSlideGraph graph = MazeSlideGraph.Build(grid, requireExitOnStopCell: true);

        Assert.IsTrue(graph.IsSolvable);
        Assert.AreEqual(1, graph.SolutionMoves, "Shortest path from (1,4) to (5,4) is 1 East slide.");
        Assert.AreEqual(4, graph.ReachableStops.Count, "All 4 corners of the rectangle are reachable stops.");
        Assert.AreEqual(1, graph.StopGraphLoops, "4 corners with 4 undirected corridor segments = 1 navigable loop.");
        Assert.AreEqual(1, graph.DecisionStopsOnSolution, "Spawn (1,4) offers 2 outgoing moves (East and South).");
        Assert.IsEmpty(graph.SoftLockedStops, "Reversible rectangle has zero soft-locks.");
    }

    [Test]
    public void SlideGraph_PassThroughMode_ChoosesShorterPassThroughOverLongerStopPath()
    {
        // Spawn (1,4) slides East across Exit (3,4) in 1 move to stop at (5,4),
        // OR takes a 3-move detour (1,4) -> South (1,1) -> East (3,1) -> North (3,4) to come to rest on Exit.
        string[] detourRows =
        {
            "#######", // gy=5
            "#S.E..#", // gy=4
            "#.#.#.#", // gy=3
            "#.#.#.#", // gy=2
            "#...#.#", // gy=1
            "#######", // gy=0
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(detourRows);

        MazeSlideGraph strictStopGraph = MazeSlideGraph.Build(grid, requireExitOnStopCell: true);
        Assert.IsTrue(strictStopGraph.IsExitStopReached);
        Assert.AreEqual(3, strictStopGraph.SolutionMoves, "Strict stop mode requires 3 moves to stop on Exit (3,4).");

        MazeSlideGraph passThroughGraph = MazeSlideGraph.Build(grid, requireExitOnStopCell: false);
        Assert.IsTrue(passThroughGraph.IsSolvable);
        Assert.AreEqual(1, passThroughGraph.SolutionMoves, "Pass-through mode must pick the 1-move direct East slide across Exit.");
    }

    [Test]
    public void SlideGraph_UniqueTerminalSccSelector_FindsOnlySoftLockFreeExits()
    {
        // Sliding North from Spawn (2,1) stops at (2,4); sliding East or West from (2,4) enters {(1,4), (5,4)},
        // which oscillates between cx=1 and cx=5 forever (overshooting cx=2) and can never return to row gy=1.
        string[] trapRows =
        {
            "#######", // gy=5
            "#.....#", // gy=4: one-way oscillation basin {(1,4), (5,4)}
            "##.####", // gy=3
            "##.####", // gy=2
            "##S..E#", // gy=1: Spawn=(2,1), Exit=(5,1)
            "#######", // gy=0
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(trapRows);
        MazeSlideGraph graph = MazeSlideGraph.Build(grid, requireExitOnStopCell: true);

        Assert.IsTrue(graph.IsSolvable, "Spawn (2,1) -> East -> Exit (5,1) solves in 1 move.");
        Assert.IsNotEmpty(graph.SoftLockedStops, "Basin {(1,4), (5,4)} has no path back to Exit (5,1).");
        CollectionAssert.Contains(graph.SoftLockedStops, new Vector2Int(1, 4));
        CollectionAssert.Contains(graph.SoftLockedStops, new Vector2Int(5, 4));

        // {(1,4), (5,4)} is the unique sink SCC from Spawn (2,1), so only (1,4) and (5,4) yield zero soft-locks.
        List<Vector2Int> safeExits = MazeSlideGraph.GetZeroSoftLockExitCandidates(grid, new Vector2Int(2, 1), out _);
        CollectionAssert.AreEquivalent(new[] { new Vector2Int(1, 4), new Vector2Int(5, 4) }, safeExits);
    }
}
