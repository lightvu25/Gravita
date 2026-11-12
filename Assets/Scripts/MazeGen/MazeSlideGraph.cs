using System;
using System.Collections.Generic;
using UnityEngine;

public readonly struct MazeSlideEdge
{
    public readonly Vector2Int From;
    public readonly Vector2Int To;
    public readonly MazeDir Direction;
    public readonly int Distance;
    public readonly Vector2Int[] TraversedCells;
    public readonly bool CrossesExit;

    public MazeSlideEdge(
        Vector2Int from,
        Vector2Int to,
        MazeDir direction,
        int distance,
        Vector2Int[] traversedCells,
        bool crossesExit)
    {
        From = from;
        To = to;
        Direction = direction;
        Distance = distance;
        TraversedCells = traversedCells;
        CrossesExit = crossesExit;
    }
}

/// <summary>
/// Pure stop-state directed graph built by simulating Gravita's cardinal sliding rules
/// (<see cref="GridSlideSolver.SolveSlide"/>) over a <see cref="MazeGrid"/>.
/// Computes reachability, shortest solution path, reverse-BFS soft-lock states,
/// decision stops, navigable stop-graph loops, and runway metrics.
/// </summary>
public sealed class MazeSlideGraph
{
    public Vector2Int StartCell { get; }
    public Vector2Int? ExitCell { get; }
    public bool RequireExitOnStopCell { get; }

    public IReadOnlyList<Vector2Int> ReachableStops => reachableStops;
    public IReadOnlyList<MazeSlideEdge> Edges => edges;
    public IReadOnlyDictionary<Vector2Int, List<MazeSlideEdge>> OutgoingByStop => outgoingByStop;
    public IReadOnlyCollection<Vector2Int> TraversedFloorCells => traversedFloorCells;

    public bool IsExitStopReached { get; private set; }
    public bool IsExitPassThroughReached { get; private set; }
    public bool IsSolvable => RequireExitOnStopCell ? IsExitStopReached : IsExitPassThroughReached;

    public IReadOnlyList<Vector2Int> SolutionStops => solutionStops;
    public IReadOnlyList<MazeDir> SolutionDirections => solutionDirections;
    public int SolutionMoves => solutionDirections.Count;

    public IReadOnlyList<Vector2Int> SoftLockedStops => softLockedStops;

    public int DecisionStopsOnSolution { get; private set; }
    public int StopGraphLoops { get; private set; }
    public int MaxSlideLength { get; private set; }
    public float OneCellSlideRatio { get; private set; }
    public int TotalFloorCells { get; private set; }
    public float FloorCoverage { get; private set; }

    private readonly List<Vector2Int> reachableStops = new List<Vector2Int>();
    private readonly HashSet<Vector2Int> reachableStopSet = new HashSet<Vector2Int>();
    private readonly List<MazeSlideEdge> edges = new List<MazeSlideEdge>();
    private readonly Dictionary<Vector2Int, List<MazeSlideEdge>> outgoingByStop = new Dictionary<Vector2Int, List<MazeSlideEdge>>();
    private readonly Dictionary<Vector2Int, List<Vector2Int>> incomingByStop = new Dictionary<Vector2Int, List<Vector2Int>>();
    private readonly HashSet<Vector2Int> traversedFloorCells = new HashSet<Vector2Int>();
    private readonly List<Vector2Int> solutionStops = new List<Vector2Int>();
    private readonly List<MazeDir> solutionDirections = new List<MazeDir>();
    private readonly List<Vector2Int> softLockedStops = new List<Vector2Int>();

    private MazeSlideGraph(Vector2Int startCell, Vector2Int? exitCell, bool requireExitOnStopCell)
    {
        StartCell = startCell;
        ExitCell = exitCell;
        RequireExitOnStopCell = requireExitOnStopCell;
    }

    public bool IsStopReachable(Vector2Int cell) => reachableStopSet.Contains(cell);
    public bool IsCellTraversed(Vector2Int cell) => traversedFloorCells.Contains(cell);

    public static MazeSlideGraph Build(MazeGrid grid, Vector2Int? overrideStart = null, bool requireExitOnStopCell = true)
    {
        if (grid == null) throw new ArgumentNullException(nameof(grid));

        Vector2Int start = overrideStart ?? (grid.Spawn ?? Vector2Int.zero);
        var graph = new MazeSlideGraph(start, grid.Exit, requireExitOnStopCell);
        graph.TotalFloorCells = grid.CountFloorCells();

        if (!grid.IsFloor(start))
            return graph;

        int maxSteps = Mathf.Max(grid.Width, grid.Height) + 2;
        var bfsQueue = new Queue<Vector2Int>();

        // Parent pointers & stop depths for shortest solution path from StartCell
        var parentStop = new Dictionary<Vector2Int, Vector2Int>();
        var parentDir = new Dictionary<Vector2Int, MazeDir>();
        var stopDepth = new Dictionary<Vector2Int, int> { [start] = 0 };

        // Track shortest pass-through edge reaching Exit when requireExitOnStopCell == false
        bool foundPassThroughSolution = false;
        int bestPassThroughMoves = int.MaxValue;
        MazeSlideEdge firstPassThroughEdge = default;

        graph.reachableStopSet.Add(start);
        graph.reachableStops.Add(start);
        graph.outgoingByStop[start] = new List<MazeSlideEdge>(4);
        graph.incomingByStop[start] = new List<Vector2Int>(4);
        graph.traversedFloorCells.Add(start);
        bfsQueue.Enqueue(start);

        if (grid.Exit.HasValue && start == grid.Exit.Value)
        {
            graph.IsExitStopReached = true;
            graph.IsExitPassThroughReached = true;
        }

        int oneCellEdges = 0;
        int maxSlide = 0;

        while (bfsQueue.Count > 0)
        {
            Vector2Int u = bfsQueue.Dequeue();
            int uDepth = stopDepth[u];
            List<MazeSlideEdge> outList = graph.outgoingByStop[u];

            for (int d = 0; d < MazeDirectionUtil.AllDirections.Length; d++)
            {
                MazeDir dir = MazeDirectionUtil.AllDirections[d];
                Vector2Int step = MazeDirectionUtil.ToStep(dir);

                GridSlideSolver.SlideResult slide = GridSlideSolver.SolveSlide(u, step, grid.IsWallPredicate, maxSteps);
                if (slide.TilesTraveled <= 0 || slide.StopCell == u)
                    continue;

                var traversed = new Vector2Int[slide.TilesTraveled];
                bool crossesExit = false;
                Vector2Int cursor = u;
                for (int s = 0; s < slide.TilesTraveled; s++)
                {
                    cursor += step;
                    traversed[s] = cursor;
                    graph.traversedFloorCells.Add(cursor);
                    if (grid.Exit.HasValue && cursor == grid.Exit.Value)
                        crossesExit = true;
                }

                Vector2Int v = slide.StopCell;
                var edge = new MazeSlideEdge(u, v, dir, slide.TilesTraveled, traversed, crossesExit);
                outList.Add(edge);
                graph.edges.Add(edge);

                if (slide.TilesTraveled == 1)
                    oneCellEdges++;
                if (slide.TilesTraveled > maxSlide)
                    maxSlide = slide.TilesTraveled;

                if (crossesExit)
                {
                    graph.IsExitPassThroughReached = true;
                    int passMoves = uDepth + 1;
                    if (!foundPassThroughSolution || passMoves < bestPassThroughMoves)
                    {
                        foundPassThroughSolution = true;
                        bestPassThroughMoves = passMoves;
                        firstPassThroughEdge = edge;
                    }
                }

                if (graph.reachableStopSet.Add(v))
                {
                    graph.reachableStops.Add(v);
                    graph.outgoingByStop[v] = new List<MazeSlideEdge>(4);
                    graph.incomingByStop[v] = new List<Vector2Int>(4);
                    parentStop[v] = u;
                    parentDir[v] = dir;
                    stopDepth[v] = uDepth + 1;
                    bfsQueue.Enqueue(v);

                    if (grid.Exit.HasValue && v == grid.Exit.Value)
                    {
                        graph.IsExitStopReached = true;
                        graph.IsExitPassThroughReached = true;
                    }
                }

                graph.incomingByStop[v].Add(u);
            }
        }

        graph.MaxSlideLength = maxSlide;
        graph.OneCellSlideRatio = graph.edges.Count > 0 ? (float)oneCellEdges / graph.edges.Count : 0f;
        graph.FloorCoverage = graph.TotalFloorCells > 0
            ? (float)graph.traversedFloorCells.Count / graph.TotalFloorCells
            : 0f;

        // Reconstruct true shortest solution path:
        // - When requireExitOnStopCell == true: Exit must be reached as a stop cell.
        // - When requireExitOnStopCell == false: pick whichever reaches Exit in fewer moves (stop vs pass-through).
        if (grid.Exit.HasValue)
        {
            Vector2Int exitVal = grid.Exit.Value;
            int stopMoves = graph.IsExitStopReached && stopDepth.TryGetValue(exitVal, out int sm) ? sm : int.MaxValue;

            if (requireExitOnStopCell)
            {
                if (graph.IsExitStopReached)
                    ReconstructPath(start, exitVal, parentStop, parentDir, graph.solutionStops, graph.solutionDirections);
            }
            else if (foundPassThroughSolution && bestPassThroughMoves < stopMoves)
            {
                ReconstructPath(start, firstPassThroughEdge.From, parentStop, parentDir, graph.solutionStops, graph.solutionDirections);
                graph.solutionStops.Add(exitVal);
                graph.solutionDirections.Add(firstPassThroughEdge.Direction);
            }
            else if (graph.IsExitStopReached)
            {
                ReconstructPath(start, exitVal, parentStop, parentDir, graph.solutionStops, graph.solutionDirections);
            }
        }

        // Compute DecisionStopsOnSolution (stops along solution before Exit with >= 2 genuine forward/branch options)
        graph.DecisionStopsOnSolution = graph.ComputeDecisionStopsOnSolution();

        // Compute cyclomatic loop count on the undirected projection of reachable stops
        graph.StopGraphLoops = graph.ComputeStopGraphLoops();

        // Reverse-BFS Soft-Lock Detection (Q6)
        graph.ComputeSoftLocks(requireExitOnStopCell);

        return graph;
    }

    private static void ReconstructPath(
        Vector2Int start,
        Vector2Int target,
        Dictionary<Vector2Int, Vector2Int> parentStop,
        Dictionary<Vector2Int, MazeDir> parentDir,
        List<Vector2Int> outStops,
        List<MazeDir> outDirs)
    {
        outStops.Clear();
        outDirs.Clear();

        Vector2Int cur = target;
        outStops.Add(cur);
        while (cur != start && parentStop.TryGetValue(cur, out Vector2Int prev))
        {
            outDirs.Add(parentDir[cur]);
            cur = prev;
            outStops.Add(cur);
        }

        outStops.Reverse();
        outDirs.Reverse();
    }

    private int ComputeDecisionStopsOnSolution()
    {
        if (solutionStops.Count <= 1)
            return 0;

        int decisions = 0;
        // Evaluate every resting stop prior to the final Exit stop
        for (int i = 0; i < solutionStops.Count - 1; i++)
        {
            Vector2Int stop = solutionStops[i];
            if (!outgoingByStop.TryGetValue(stop, out List<MazeSlideEdge> outEdges))
                continue;

            if (i == 0)
            {
                if (outEdges.Count >= 2)
                    decisions++;
            }
            else
            {
                Vector2Int prevStop = solutionStops[i - 1];
                int nonTrivialChoices = 0;
                for (int e = 0; e < outEdges.Count; e++)
                {
                    if (outEdges[e].To != prevStop)
                        nonTrivialChoices++;
                }

                if (nonTrivialChoices >= 2)
                    decisions++;
            }
        }

        return decisions;
    }

    private int ComputeStopGraphLoops()
    {
        int vCount = reachableStops.Count;
        if (vCount <= 2) return 0;

        var undirectedPairs = new HashSet<long>();
        for (int i = 0; i < edges.Count; i++)
        {
            Vector2Int a = edges[i].From;
            Vector2Int b = edges[i].To;
            if (a == b) continue;

            int keyA = (a.x << 16) ^ (a.y & 0xFFFF);
            int keyB = (b.x << 16) ^ (b.y & 0xFFFF);
            int minK = Mathf.Min(keyA, keyB);
            int maxK = Mathf.Max(keyA, keyB);
            long pairKey = ((long)minK << 32) | (uint)maxK;
            undirectedPairs.Add(pairKey);
        }

        // ReachableStops is connected from StartCell -> 1 connected component
        return Mathf.Max(0, undirectedPairs.Count - vCount + 1);
    }

    /// <summary>
    /// Computes reachable stop states that cannot reach Exit via reverse BFS.
    /// Note: when <paramref name="requireExitOnStop"/> is true (generated maps), reverse BFS intentionally seeds
    /// ONLY from <c>Exit</c> as a resting stop cell (conservative invariant: every reachable stop must be able
    /// to come to rest on <c>Exit</c>, never relying on a mid-slide trigger pass-through).
    /// </summary>
    private void ComputeSoftLocks(bool requireExitOnStop)
    {
        softLockedStops.Clear();
        if (!ExitCell.HasValue || !IsSolvable)
            return;

        Vector2Int exitVal = ExitCell.Value;
        var canReachExit = new HashSet<Vector2Int>();
        var revQueue = new Queue<Vector2Int>();

        if (IsExitStopReached)
        {
            canReachExit.Add(exitVal);
            revQueue.Enqueue(exitVal);
        }

        if (!requireExitOnStop)
        {
            for (int i = 0; i < edges.Count; i++)
            {
                if (edges[i].CrossesExit && canReachExit.Add(edges[i].From))
                    revQueue.Enqueue(edges[i].From);
            }
        }

        while (revQueue.Count > 0)
        {
            Vector2Int cur = revQueue.Dequeue();
            if (!incomingByStop.TryGetValue(cur, out List<Vector2Int> preds))
                continue;

            for (int i = 0; i < preds.Count; i++)
            {
                Vector2Int pred = preds[i];
                if (canReachExit.Add(pred))
                    revQueue.Enqueue(pred);
            }
        }

        for (int i = 0; i < reachableStops.Count; i++)
        {
            Vector2Int s = reachableStops[i];
            if (!canReachExit.Contains(s))
                softLockedStops.Add(s);
        }
    }

    /// <summary>
    /// Returns all floor cells in <paramref name="grid"/> that sit adjacent to at least one cardinal Wall
    /// (necessary condition to be a resting stop cell in a pure wall/floor maze).
    /// </summary>
    public static List<Vector2Int> FindWallAdjacentFloorCells(MazeGrid grid)
    {
        var list = new List<Vector2Int>();
        if (grid == null) return list;

        for (int y = 1; y < grid.Height - 1; y++)
        {
            for (int x = 1; x < grid.Width - 1; x++)
            {
                var cell = new Vector2Int(x, y);
                if (!grid.IsFloor(cell)) continue;

                if (grid.IsWall(cell + Vector2Int.up)
                    || grid.IsWall(cell + Vector2Int.down)
                    || grid.IsWall(cell + Vector2Int.left)
                    || grid.IsWall(cell + Vector2Int.right))
                {
                    list.Add(cell);
                }
            }
        }

        return list;
    }

    /// <summary>
    /// Uses Tarjan's Strongly Connected Components algorithm on the stop-state graph rooted at
    /// <paramref name="spawn"/> to return the exact set of reachable stop cells (excluding <paramref name="spawn"/>)
    /// that yield ZERO reachable soft-locks if chosen as <c>Exit</c>.
    /// By SCC DAG theory, this set is non-empty iff the stop graph has a unique terminal (sink) SCC,
    /// in which case it equals <c>C_sink \ {spawn}</c>.
    /// </summary>
    public static List<Vector2Int> GetZeroSoftLockExitCandidates(MazeGrid grid, Vector2Int spawn, out MazeSlideGraph graph)
    {
        var result = new List<Vector2Int>();
        MazeSlideGraph builtGraph = Build(grid, spawn, requireExitOnStopCell: true);
        graph = builtGraph;
        int n = builtGraph.reachableStops.Count;
        if (n <= 1)
            return result;

        var nodeIndex = new Dictionary<Vector2Int, int>(n);
        for (int i = 0; i < n; i++)
            nodeIndex[builtGraph.reachableStops[i]] = i;

        int[] dfn = new int[n];
        int[] low = new int[n];
        int[] sccId = new int[n];
        bool[] onStack = new bool[n];
        for (int i = 0; i < n; i++)
        {
            dfn[i] = -1;
            sccId[i] = -1;
        }

        var stack = new Stack<int>(n);
        var sccMembers = new List<List<int>>();
        int timer = 0;

        void StrongConnect(int u)
        {
            dfn[u] = low[u] = timer++;
            stack.Push(u);
            onStack[u] = true;

            Vector2Int uCell = builtGraph.reachableStops[u];
            if (builtGraph.outgoingByStop.TryGetValue(uCell, out List<MazeSlideEdge> outEdges))
            {
                for (int i = 0; i < outEdges.Count; i++)
                {
                    int v = nodeIndex[outEdges[i].To];
                    if (dfn[v] == -1)
                    {
                        StrongConnect(v);
                        if (low[v] < low[u]) low[u] = low[v];
                    }
                    else if (onStack[v] && dfn[v] < low[u])
                    {
                        low[u] = dfn[v];
                    }
                }
            }

            if (low[u] == dfn[u])
            {
                int id = sccMembers.Count;
                var comp = new List<int>();
                while (true)
                {
                    int w = stack.Pop();
                    onStack[w] = false;
                    sccId[w] = id;
                    comp.Add(w);
                    if (w == u) break;
                }
                sccMembers.Add(comp);
            }
        }

        for (int i = 0; i < n; i++)
        {
            if (dfn[i] == -1)
                StrongConnect(i);
        }

        int sccCount = sccMembers.Count;
        bool[] hasOutgoingSccEdge = new bool[sccCount];
        for (int u = 0; u < n; u++)
        {
            int cu = sccId[u];
            Vector2Int uCell = graph.reachableStops[u];
            if (!graph.outgoingByStop.TryGetValue(uCell, out List<MazeSlideEdge> outEdges))
                continue;

            for (int i = 0; i < outEdges.Count; i++)
            {
                int cv = sccId[nodeIndex[outEdges[i].To]];
                if (cu != cv)
                {
                    hasOutgoingSccEdge[cu] = true;
                    break;
                }
            }
        }

        int terminalScc = -1;
        int terminalCount = 0;
        for (int c = 0; c < sccCount; c++)
        {
            if (!hasOutgoingSccEdge[c])
            {
                terminalScc = c;
                terminalCount++;
            }
        }

        // If > 1 terminal SCC exists, ANY choice of Exit leaves the other terminal SCC(s) soft-locked!
        if (terminalCount != 1 || terminalScc < 0)
            return result;

        List<int> members = sccMembers[terminalScc];
        for (int i = 0; i < members.Count; i++)
        {
            Vector2Int cell = graph.reachableStops[members[i]];
            if (cell != spawn)
                result.Add(cell);
        }

        return result;
    }
}
