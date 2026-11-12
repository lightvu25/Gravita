using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Optional slot-level telemetry recorded during module stamping so <see cref="MazeScorer"/>
/// can evaluate pattern diversity, adjacent repetition, dead-end caps, and chamber caps directly.
/// When absent (<c>HasSlotData == false</c>, e.g. after manual cell painting or loading raw ASCII),
/// <see cref="MazeScorer"/> automatically falls back to pure grid + stop-graph heuristics.
/// </summary>
public readonly struct MazePlacementTelemetry
{
    public readonly bool HasSlotData;
    public readonly int SlotCols;
    public readonly int SlotRows;
    public readonly int TotalSlots;
    public readonly int DistinctPatternCount;
    public readonly int DistinctGroupCount;
    public readonly int AdjacentDuplicateCount;
    public readonly int ConnectedSlotEdges;
    public readonly int DeadEndSlotCount;
    public readonly int ChamberSlotCount;

    public MazePlacementTelemetry(
        int slotCols,
        int slotRows,
        int distinctPatternCount,
        int distinctGroupCount,
        int adjacentDuplicateCount,
        int connectedSlotEdges,
        int deadEndSlotCount,
        int chamberSlotCount)
    {
        HasSlotData = slotCols > 0 && slotRows > 0;
        SlotCols = slotCols;
        SlotRows = slotRows;
        TotalSlots = Mathf.Max(0, slotCols * slotRows);
        DistinctPatternCount = distinctPatternCount;
        DistinctGroupCount = distinctGroupCount;
        AdjacentDuplicateCount = adjacentDuplicateCount;
        ConnectedSlotEdges = connectedSlotEdges;
        DeadEndSlotCount = deadEndSlotCount;
        ChamberSlotCount = chamberSlotCount;
    }
}

/// <summary>
/// Immutable scoring breakdown separating intrinsic <see cref="QualityScore"/> (0..100)
/// from tier-specific <see cref="DifficultyFit"/> (0..100) per GDD §6.3.
/// </summary>
public sealed class MazeScoreBreakdown
{
    public bool IsValid { get; }
    public bool HasConfigError { get; }
    public string ConfigError { get; }

    public float QualityScore { get; }
    public float PlayabilitySubscore { get; }
    public float DiversitySubscore { get; }
    public float LayoutSubscore { get; }

    public float NormalizedPlayabilityWeight { get; }
    public float NormalizedDiversityWeight { get; }
    public float NormalizedLayoutWeight { get; }

    public float DifficultyFit { get; }
    public float SolutionMovesFit { get; }
    public float DecisionStopsFit { get; }
    public float LoopsFit { get; }
    public float StructureCapsFit { get; }

    public MazeScoreBreakdown(
        bool isValid,
        bool hasConfigError,
        string configError,
        float qualityScore,
        float playabilitySubscore,
        float diversitySubscore,
        float layoutSubscore,
        float normPlayability,
        float normDiversity,
        float normLayout,
        float difficultyFit,
        float solutionMovesFit,
        float decisionStopsFit,
        float loopsFit,
        float structureCapsFit)
    {
        IsValid = isValid;
        HasConfigError = hasConfigError;
        ConfigError = configError;
        QualityScore = qualityScore;
        PlayabilitySubscore = playabilitySubscore;
        DiversitySubscore = diversitySubscore;
        LayoutSubscore = layoutSubscore;
        NormalizedPlayabilityWeight = normPlayability;
        NormalizedDiversityWeight = normDiversity;
        NormalizedLayoutWeight = normLayout;
        DifficultyFit = difficultyFit;
        SolutionMovesFit = solutionMovesFit;
        DecisionStopsFit = decisionStopsFit;
        LoopsFit = loopsFit;
        StructureCapsFit = structureCapsFit;
    }

    public static MazeScoreBreakdown Invalid(string configError = null)
    {
        return new MazeScoreBreakdown(
            isValid: false,
            hasConfigError: !string.IsNullOrEmpty(configError),
            configError: configError,
            qualityScore: 0f,
            playabilitySubscore: 0f,
            diversitySubscore: 0f,
            layoutSubscore: 0f,
            normPlayability: 0f,
            normDiversity: 0f,
            normLayout: 0f,
            difficultyFit: 0f,
            solutionMovesFit: 0f,
            decisionStopsFit: 0f,
            loopsFit: 0f,
            structureCapsFit: 0f);
    }
}

/// <summary>
/// Evaluates validated Gravita mazes along two orthogonal axes (GDD §6.3):
/// <list type="bullet">
/// <item><description><c>QualityScore</c> (0..100): normalized weighted sum of Playability (default 40%), Diversity (35%), and Layout (25%).</description></item>
/// <item><description><c>DifficultyFit</c> (0..100): closeness to the active <see cref="MazeDifficultyProfile"/>'s target bands.</description></item>
/// </list>
/// </summary>
public static class MazeScorer
{
    public static MazeScoreBreakdown Evaluate(
        MazeGrid grid,
        MazeValidationReport validation,
        MazeDifficultyProfile profile,
        MazePlacementTelemetry telemetry = default)
    {
        if (profile == null)
            return MazeScoreBreakdown.Invalid("MazeDifficultyProfile is null.");

        if (!profile.TryGetNormalizedQualityWeights(
                out float wPlayability,
                out float wDiversity,
                out float wLayout,
                out string weightError))
        {
            return MazeScoreBreakdown.Invalid(weightError);
        }

        if (grid == null || validation == null || !validation.IsValid || validation.SlideGraph == null)
        {
            return new MazeScoreBreakdown(
                isValid: false,
                hasConfigError: false,
                configError: null,
                qualityScore: 0f,
                playabilitySubscore: 0f,
                diversitySubscore: 0f,
                layoutSubscore: 0f,
                normPlayability: wPlayability,
                normDiversity: wDiversity,
                normLayout: wLayout,
                difficultyFit: 0f,
                solutionMovesFit: 0f,
                decisionStopsFit: 0f,
                loopsFit: 0f,
                structureCapsFit: 0f);
        }

        MazeSlideGraph graph = validation.SlideGraph;

        float playability = ComputePlayabilitySubscore(graph, profile.minLongRunway);
        float diversity = ComputeDiversitySubscore(graph, telemetry);
        float layout = ComputeLayoutSubscore(grid, graph);

        float qualityScore = Mathf.Clamp(
            wPlayability * playability + wDiversity * diversity + wLayout * layout,
            0f,
            100f);

        float movesFit = ComputeBandFit(graph.SolutionMoves, profile.minSolutionMoves, profile.maxSolutionMoves, falloffRange: 5f);
        float decisionsFit = ComputeBandFit(graph.DecisionStopsOnSolution, profile.minDecisionStops, profile.maxDecisionStops, falloffRange: 3f);
        float loopsFit = ComputeBandFit(graph.StopGraphLoops, profile.minStopGraphLoops, profile.maxStopGraphLoops, falloffRange: 3f);
        float capsFit = ComputeStructureCapsFit(profile, telemetry);

        float difficultyFit = Mathf.Clamp(
            100f * (0.40f * movesFit + 0.25f * decisionsFit + 0.20f * loopsFit + 0.15f * capsFit),
            0f,
            100f);

        return new MazeScoreBreakdown(
            isValid: true,
            hasConfigError: false,
            configError: null,
            qualityScore: qualityScore,
            playabilitySubscore: playability,
            diversitySubscore: diversity,
            layoutSubscore: layout,
            normPlayability: wPlayability,
            normDiversity: wDiversity,
            normLayout: wLayout,
            difficultyFit: difficultyFit,
            solutionMovesFit: movesFit * 100f,
            decisionStopsFit: decisionsFit * 100f,
            loopsFit: loopsFit * 100f,
            structureCapsFit: capsFit * 100f);
    }

    private static float ComputePlayabilitySubscore(MazeSlideGraph graph, int minLongRunway)
    {
        // 1. Decision richness along solution (0..35 pts)
        float decisionPts;
        if (graph.SolutionMoves <= 0)
        {
            decisionPts = 0f;
        }
        else if (graph.DecisionStopsOnSolution <= 0)
        {
            decisionPts = 8f;
        }
        else
        {
            float decisionRatio = (float)graph.DecisionStopsOnSolution / Mathf.Max(1, graph.SolutionMoves);
            // Sweet spot: ~20%..65% of stops along the solution present >= 2 forward choices
            if (decisionRatio >= 0.20f && decisionRatio <= 0.70f)
                decisionPts = 35f;
            else if (decisionRatio < 0.20f)
                decisionPts = Mathf.Lerp(15f, 35f, decisionRatio / 0.20f);
            else
                decisionPts = Mathf.Lerp(35f, 24f, Mathf.Clamp01((decisionRatio - 0.70f) / 0.30f));
        }

        // 2. KineticRam runway presence (0..30 pts)
        int targetRunway = Mathf.Max(3, minLongRunway);
        float runwayPts = graph.MaxSlideLength >= targetRunway
            ? 30f
            : 30f * Mathf.Clamp01((float)graph.MaxSlideLength / targetRunway);

        // 3. Slide smoothness / low 1-cell stutter (0..20 pts)
        float stutterPts;
        if (graph.OneCellSlideRatio <= 0.35f)
            stutterPts = 20f;
        else if (graph.OneCellSlideRatio >= 0.75f)
            stutterPts = 0f;
        else
            stutterPts = 20f * (1f - (graph.OneCellSlideRatio - 0.35f) / 0.40f);

        // 4. Non-linear exploration & navigable loop bonus (0..15 pts)
        int extraStops = Mathf.Max(0, graph.ReachableStops.Count - (graph.SolutionMoves + 1));
        float branchFactor = Mathf.Clamp01(extraStops / 4f);
        float loopBonus = graph.StopGraphLoops > 0 ? 0.45f : 0f;
        float explorationPts = 15f * Mathf.Clamp01(branchFactor * 0.70f + loopBonus);

        return Mathf.Clamp(decisionPts + runwayPts + stutterPts + explorationPts, 0f, 100f);
    }

    private static float ComputeDiversitySubscore(MazeSlideGraph graph, MazePlacementTelemetry telemetry)
    {
        // Bucket slide distances across edges: [1], [2..3], [4..5], [6+]
        int bucketMask = 0;
        var distinctDistances = new HashSet<int>();
        for (int i = 0; i < graph.Edges.Count; i++)
        {
            int d = graph.Edges[i].Distance;
            distinctDistances.Add(d);
            if (d <= 1) bucketMask |= 1;
            else if (d <= 3) bucketMask |= 2;
            else if (d <= 5) bucketMask |= 4;
            else bucketMask |= 8;
        }

        int bucketCount = ((bucketMask & 1) != 0 ? 1 : 0)
                        + ((bucketMask & 2) != 0 ? 1 : 0)
                        + ((bucketMask & 4) != 0 ? 1 : 0)
                        + ((bucketMask & 8) != 0 ? 1 : 0);

        float distanceSpreadPts = bucketCount switch
        {
            >= 3 => 25f,
            2    => 18f,
            1    => 7f,
            _    => 0f,
        };

        if (telemetry.HasSlotData)
        {
            // 1. Pattern variety relative to map slot count (0..45 pts)
            int targetDistinct = Mathf.Clamp(telemetry.TotalSlots / 2, 3, 7);
            float varietyPts = 45f * Mathf.Clamp01((float)telemetry.DistinctPatternCount / targetDistinct);

            // 2. Low adjacent duplicate ratio across connected slot edges (0..30 pts)
            float dupRatio = telemetry.ConnectedSlotEdges > 0
                ? (float)telemetry.AdjacentDuplicateCount / telemetry.ConnectedSlotEdges
                : 0f;
            float antiDupPts = 30f * (1f - Mathf.Clamp01(dupRatio / 0.50f));

            return Mathf.Clamp(varietyPts + antiDupPts + distanceSpreadPts, 0f, 100f);
        }

        // Pure stop-graph fallback when slot telemetry is unavailable (e.g. manual paint / raw ASCII map)
        float fallbackVarietyPts = 45f * Mathf.Clamp01(distinctDistances.Count / 4f);

        int degMask = 0;
        foreach (var kvp in graph.OutgoingByStop)
        {
            int deg = Mathf.Clamp(kvp.Value.Count, 1, 4);
            degMask |= 1 << (deg - 1);
        }
        int distinctDegrees = ((degMask & 1) != 0 ? 1 : 0)
                            + ((degMask & 2) != 0 ? 1 : 0)
                            + ((degMask & 4) != 0 ? 1 : 0)
                            + ((degMask & 8) != 0 ? 1 : 0);
        float fallbackDegreePts = 30f * Mathf.Clamp01(distinctDegrees / 3f);

        return Mathf.Clamp(fallbackVarietyPts + fallbackDegreePts + distanceSpreadPts, 0f, 100f);
    }

    private static float ComputeLayoutSubscore(MazeGrid grid, MazeSlideGraph graph)
    {
        // 1. Reachable floor coverage — rewards clean, fully traversable corridors without dead floor pockets (0..45 pts)
        float cov = graph.FloorCoverage;
        float coveragePts;
        if (cov >= 0.85f)
            coveragePts = 45f;
        else if (cov <= 0.40f)
            coveragePts = 0f;
        else
            coveragePts = 45f * ((cov - 0.40f) / 0.45f);

        // 2. Balanced interior floor density for 5x5 stride-4 hybrid mazes: sweet spot [0.22 .. 0.55] (0..25 pts)
        int interiorArea = Mathf.Max(1, (grid.Width - 2) * (grid.Height - 2));
        float interiorFloorRatio = (float)graph.TotalFloorCells / interiorArea;
        float densityPts;
        if (interiorFloorRatio >= 0.22f && interiorFloorRatio <= 0.55f)
            densityPts = 25f;
        else if (interiorFloorRatio < 0.22f)
            densityPts = 25f * Mathf.Clamp01(interiorFloorRatio / 0.22f);
        else
            densityPts = 25f * Mathf.Clamp01(1f - (interiorFloorRatio - 0.55f) / 0.30f);

        // 3. Spatial Spawn-to-Exit framing across the grid (0..30 pts)
        float separationPts = 0f;
        if (grid.Spawn.HasValue && grid.Exit.HasValue)
        {
            int manhattan = Mathf.Abs(grid.Exit.Value.x - grid.Spawn.Value.x)
                          + Mathf.Abs(grid.Exit.Value.y - grid.Spawn.Value.y);
            int maxSpan = Mathf.Max(2, (grid.Width - 3) + (grid.Height - 3));
            float sepRatio = (float)manhattan / maxSpan;
            separationPts = 30f * Mathf.Clamp01(sepRatio / 0.45f);
        }

        return Mathf.Clamp(coveragePts + densityPts + separationPts, 0f, 100f);
    }

    private static float ComputeBandFit(int value, int min, int max, float falloffRange)
    {
        if (value >= min && value <= max)
            return 1f;

        int delta = value < min ? (min - value) : (value - max);
        return Mathf.Clamp01(1f - delta / Mathf.Max(1f, falloffRange));
    }

    private static float ComputeStructureCapsFit(MazeDifficultyProfile profile, MazePlacementTelemetry telemetry)
    {
        if (!telemetry.HasSlotData)
            return 1f;

        float deadEndFit = telemetry.DeadEndSlotCount <= profile.maxDeadEnds
            ? 1f
            : Mathf.Clamp01(1f - (telemetry.DeadEndSlotCount - profile.maxDeadEnds) / 2f);

        float chamberFit = telemetry.ChamberSlotCount <= profile.maxChambers
            ? 1f
            : Mathf.Clamp01(1f - (telemetry.ChamberSlotCount - profile.maxChambers) / 2f);

        return 0.5f * deadEndFit + 0.5f * chamberFit;
    }
}

