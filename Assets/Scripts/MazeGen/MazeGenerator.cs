using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Metadata for a single 5x5 module stamped at slot (<see cref="SlotX"/>, <see cref="SlotY"/>).
/// </summary>
public readonly struct MazePlacedModule
{
    public readonly int SlotX;
    public readonly int SlotY;
    public readonly string PatternId;
    public readonly MazePatternGroup Group;
    public readonly int QuarterTurnsCW;
    public readonly MazeSideMask PortMask;

    public MazePlacedModule(
        int slotX,
        int slotY,
        string patternId,
        MazePatternGroup group,
        int quarterTurnsCW,
        MazeSideMask portMask)
    {
        SlotX = slotX;
        SlotY = slotY;
        PatternId = patternId;
        Group = group;
        QuarterTurnsCW = quarterTurnsCW;
        PortMask = portMask;
    }
}

/// <summary>
/// Input parameters for deterministic <see cref="MazeGenerator.Generate"/> execution.
/// </summary>
public struct MazeGenerationRequest
{
    public int Seed;
    public MazeDifficultyProfile Profile;
    public MazePatternLibrary Library;
    public MazeSizePreset? OverridePreset;
    public int? OverrideSlotCols;
    public int? OverrideSlotRows;
}

/// <summary>
/// Complete deterministic result returned by <see cref="MazeGenerator.Generate"/>.
/// </summary>
public sealed class MazeGenerationResult
{
    public bool Success { get; }
    public bool MetTargetScore { get; }
    public int SeedUsed { get; }
    public int AttemptsUsed { get; }
    public int MaxAttempts { get; }
    public MazeSizePreset ResolvedPreset { get; }
    public int SlotCols { get; }
    public int SlotRows { get; }

    public MazeGrid Grid { get; }
    public MazeValidationReport Validation { get; }
    public MazeScoreBreakdown Score { get; }
    public MazePlacementTelemetry Telemetry { get; }
    public IReadOnlyList<MazePlacedModule> PlacedModules { get; }

    public string WarningMessage { get; }
    public string FailureReason { get; }

    public MazeGenerationResult(
        bool success,
        bool metTargetScore,
        int seedUsed,
        int attemptsUsed,
        int maxAttempts,
        MazeSizePreset resolvedPreset,
        int slotCols,
        int slotRows,
        MazeGrid grid,
        MazeValidationReport validation,
        MazeScoreBreakdown score,
        MazePlacementTelemetry telemetry,
        IReadOnlyList<MazePlacedModule> placedModules,
        string warningMessage,
        string failureReason)
    {
        Success = success;
        MetTargetScore = metTargetScore;
        SeedUsed = seedUsed;
        AttemptsUsed = attemptsUsed;
        MaxAttempts = maxAttempts;
        ResolvedPreset = resolvedPreset;
        SlotCols = slotCols;
        SlotRows = slotRows;
        Grid = grid;
        Validation = validation;
        Score = score;
        Telemetry = telemetry;
        PlacedModules = placedModules ?? Array.Empty<MazePlacedModule>();
        WarningMessage = warningMessage;
        FailureReason = failureReason;
    }

    public static MazeGenerationResult Failed(
        int seedUsed,
        int attemptsUsed,
        int maxAttempts,
        MazeSizePreset preset,
        int slotCols,
        int slotRows,
        string failureReason)
    {
        return new MazeGenerationResult(
            success: false,
            metTargetScore: false,
            seedUsed: seedUsed,
            attemptsUsed: attemptsUsed,
            maxAttempts: maxAttempts,
            resolvedPreset: preset,
            slotCols: slotCols,
            slotRows: slotRows,
            grid: null,
            validation: null,
            score: MazeScoreBreakdown.Invalid(failureReason),
            telemetry: default,
            placedModules: Array.Empty<MazePlacedModule>(),
            warningMessage: null,
            failureReason: failureReason);
    }

    /// <summary>
    /// Creates an in-memory <see cref="MazeMapData"/> ScriptableObject populated from this generation result.
    /// </summary>
    public MazeMapData CreateMapDataAsset(string profileId, int libraryVersion)
    {
        if (!Success || Grid == null || Validation == null || Validation.SlideGraph == null)
            return null;

        var data = ScriptableObject.CreateInstance<MazeMapData>();
        MazeSlideGraph g = Validation.SlideGraph;
        data.PopulateFromGrid(
            grid: Grid,
            generationSeed: SeedUsed,
            sourceProfileId: profileId,
            preset: ResolvedPreset,
            libVersion: libraryVersion,
            score: Score != null ? Score.QualityScore : 0f,
            fit: Score != null ? Score.DifficultyFit : 0f,
            stops: g.SolutionStops,
            directions: g.SolutionDirections,
            decisionCount: g.DecisionStopsOnSolution,
            loopCount: g.StopGraphLoops);
        return data;
    }
}

/// <summary>
/// Deterministic 3-stage Hybrid Maze generator for Gravita (GDD §6):
/// <list type="number">
/// <item><description>Seeded Growing-Tree slot topology + dead-end pruning + macro-cycle braiding.</description></item>
/// <item><description>Catcher-aware 5x5 shared-edge module placement (<c>stride = 4</c>) preventing unstoppable pass-through chains.</description></item>
/// <item><description>Tarjan terminal-SCC <c>(Spawn, Exit)</c> selection guaranteeing zero reachable soft-locks and <c>Exit</c> on a stop cell.</description></item>
/// </list>
/// </summary>
public static class MazeGenerator
{
    private const float EarlyStopDifficultyFitThreshold = 75f;
    private const int MaxSpawnCandidatesPerAttempt = 28;
    private const int TopPairsToFullScorePerAttempt = 4;

    private readonly struct SlotEdge
    {
        public readonly Vector2Int A;
        public readonly MazeDir DirFromA;
        public readonly Vector2Int B;

        public SlotEdge(Vector2Int a, MazeDir dirFromA, Vector2Int b)
        {
            A = a;
            DirFromA = dirFromA;
            B = b;
        }
    }

    private readonly struct SpawnExitCandidate
    {
        public readonly Vector2Int Spawn;
        public readonly Vector2Int Exit;
        public readonly float HeuristicRank;

        public SpawnExitCandidate(Vector2Int spawn, Vector2Int exit, float heuristicRank)
        {
            Spawn = spawn;
            Exit = exit;
            HeuristicRank = heuristicRank;
        }
    }

    public static MazeGenerationResult Generate(in MazeGenerationRequest request)
    {
        MazeDifficultyProfile profile = request.Profile;
        MazePatternLibrary library = request.Library;

        if (profile == null)
        {
            return MazeGenerationResult.Failed(
                request.Seed, 0, 0, MazeSizePreset.Medium, 0, 0,
                "MazeDifficultyProfile is null.");
        }

        MazeSizePreset resolvedPreset = request.OverridePreset ?? profile.defaultPreset;
        Vector2Int slotDims = profile.GetSlotDimensions(
            request.OverridePreset,
            request.OverrideSlotCols,
            request.OverrideSlotRows);

        int slotCols = slotDims.x;
        int slotRows = slotDims.y;
        int maxAttempts = Mathf.Max(1, profile.maxAttempts);

        if (library == null || library.patterns == null || library.patterns.Count == 0)
        {
            return MazeGenerationResult.Failed(
                request.Seed, 0, maxAttempts, resolvedPreset, slotCols, slotRows,
                "MazePatternLibrary is null or contains zero patterns.");
        }

        if (!profile.TryGetNormalizedQualityWeights(out _, out _, out _, out string weightError))
        {
            return MazeGenerationResult.Failed(
                request.Seed, 0, maxAttempts, resolvedPreset, slotCols, slotRows,
                $"Invalid profile quality weights: {weightError}");
        }

        if (!library.ValidateLibrary(out List<string> libErrors))
        {
            return MazeGenerationResult.Failed(
                request.Seed, 0, maxAttempts, resolvedPreset, slotCols, slotRows,
                $"Pattern library validation failed: {string.Join("; ", libErrors)}");
        }

        HashSet<MazePatternGroup> allowedGroups = profile.BuildAllowedGroupSet();
        var masterRng = new System.Random(request.Seed);

        MazeGrid bestGrid = null;
        MazeValidationReport bestValidation = null;
        MazeScoreBreakdown bestScore = null;
        MazePlacementTelemetry bestTelemetry = default;
        List<MazePlacedModule> bestModules = null;
        float bestCombinedRank = float.NegativeInfinity;
        int attemptsUsed = 0;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            attemptsUsed = attempt;
            int attemptSeed = MixAttemptSeed(request.Seed, attempt, masterRng.Next());
            var rng = new System.Random(attemptSeed);

            // Stage 1: Slot spanning tree + dead-end cap + braid loops
            MazeSideMask[,] slotMasks = BuildSlotTopology(slotCols, slotRows, profile, rng, out int connectedEdges);

            // Stage 2: Catcher-aware 5x5 shared-edge module stamping
            if (!TryStampCatcherAwareModules(
                    slotCols,
                    slotRows,
                    slotMasks,
                    connectedEdges,
                    profile,
                    library,
                    allowedGroups,
                    rng,
                    out MazeGrid candidateGrid,
                    out List<MazePlacedModule> placedModules,
                    out MazePlacementTelemetry telemetry))
            {
                continue;
            }

            // Stage 3: Terminal-SCC soft-lock-free (Spawn, Exit) selection & scoring
            if (!TrySelectBestSpawnAndExit(
                    candidateGrid,
                    profile,
                    telemetry,
                    rng,
                    out MazeValidationReport candidateValidation,
                    out MazeScoreBreakdown candidateScore))
            {
                continue;
            }

            float combinedRank = ComputeCombinedRank(candidateScore, profile.targetScore);
            if (bestGrid == null || combinedRank > bestCombinedRank)
            {
                bestCombinedRank = combinedRank;
                bestGrid = candidateGrid;
                bestValidation = candidateValidation;
                bestScore = candidateScore;
                bestTelemetry = telemetry;
                bestModules = placedModules;
            }

            // Early stopping rule (GDD §6.2): valid map meeting target QualityScore AND strong DifficultyFit
            if (candidateScore.QualityScore >= profile.targetScore
                && candidateScore.DifficultyFit >= EarlyStopDifficultyFitThreshold)
            {
                return new MazeGenerationResult(
                    success: true,
                    metTargetScore: true,
                    seedUsed: request.Seed,
                    attemptsUsed: attemptsUsed,
                    maxAttempts: maxAttempts,
                    resolvedPreset: resolvedPreset,
                    slotCols: slotCols,
                    slotRows: slotRows,
                    grid: candidateGrid,
                    validation: candidateValidation,
                    score: candidateScore,
                    telemetry: telemetry,
                    placedModules: placedModules,
                    warningMessage: null,
                    failureReason: null);
            }
        }

        if (bestGrid == null || bestValidation == null || !bestValidation.IsValid)
        {
            return MazeGenerationResult.Failed(
                request.Seed,
                attemptsUsed,
                maxAttempts,
                resolvedPreset,
                slotCols,
                slotRows,
                $"0/{maxAttempts} generation attempts produced a hard-valid soft-lock-free maze for profile '{profile.profileId}'.");
        }

        bool metTarget = bestScore.QualityScore >= profile.targetScore;
        string warning = metTarget
            ? null
            : $"Best valid map after {maxAttempts} attempts scored Quality {bestScore.QualityScore:F1} (target {profile.targetScore:F1}) with DifficultyFit {bestScore.DifficultyFit:F1}.";

        return new MazeGenerationResult(
            success: true,
            metTargetScore: metTarget,
            seedUsed: request.Seed,
            attemptsUsed: attemptsUsed,
            maxAttempts: maxAttempts,
            resolvedPreset: resolvedPreset,
            slotCols: slotCols,
            slotRows: slotRows,
            grid: bestGrid,
            validation: bestValidation,
            score: bestScore,
            telemetry: bestTelemetry,
            placedModules: bestModules,
            warningMessage: warning,
            failureReason: null);
    }

    // ─────────────────────────────────────────────────────────────
    //  Stage 1 — Slot Topology (Growing Tree + Dead-End Cap + Braiding)
    // ─────────────────────────────────────────────────────────────

    private static MazeSideMask[,] BuildSlotTopology(
        int cols,
        int rows,
        MazeDifficultyProfile profile,
        System.Random rng,
        out int connectedEdgeCount)
    {
        var masks = new MazeSideMask[cols, rows];
        var visited = new bool[cols, rows];
        var frontier = new List<Vector2Int>(cols * rows);

        var start = new Vector2Int(rng.Next(cols), rng.Next(rows));
        visited[start.x, start.y] = true;
        frontier.Add(start);
        connectedEdgeCount = 0;

        var unvisitedDirs = new List<MazeDir>(4);
        while (frontier.Count > 0)
        {
            // 65% newest slot (winding corridors), 35% random active slot (branching junctions)
            int pickIdx = rng.NextDouble() < 0.65 ? frontier.Count - 1 : rng.Next(frontier.Count);
            Vector2Int cur = frontier[pickIdx];

            unvisitedDirs.Clear();
            for (int d = 0; d < MazeDirectionUtil.AllDirections.Length; d++)
            {
                MazeDir dir = MazeDirectionUtil.AllDirections[d];
                Vector2Int nxt = cur + MazeDirectionUtil.ToStep(dir);
                if (nxt.x >= 0 && nxt.x < cols && nxt.y >= 0 && nxt.y < rows && !visited[nxt.x, nxt.y])
                    unvisitedDirs.Add(dir);
            }

            if (unvisitedDirs.Count == 0)
            {
                frontier.RemoveAt(pickIdx);
                continue;
            }

            MazeDir chosenDir = unvisitedDirs[rng.Next(unvisitedDirs.Count)];
            Vector2Int target = cur + MazeDirectionUtil.ToStep(chosenDir);

            masks[cur.x, cur.y] |= MazeDirectionUtil.ToMask(chosenDir);
            masks[target.x, target.y] |= MazeDirectionUtil.ToMask(MazeDirectionUtil.Opposite(chosenDir));
            visited[target.x, target.y] = true;
            frontier.Add(target);
            connectedEdgeCount++;
        }

        // Prune excess degree-1 slots down to profile.maxDeadEnds
        PruneExcessDeadEnds(cols, rows, masks, profile.maxDeadEnds, rng, ref connectedEdgeCount);

        // Open additional interior braid edges to form macro-cycles
        var closedEdges = CollectClosedInteriorEdges(cols, rows, masks);
        Shuffle(closedEdges, rng);

        int desiredExtra = Mathf.RoundToInt(closedEdges.Count * Mathf.Clamp01(profile.braidRatio));
        if (profile.minStopGraphLoops > 0)
            desiredExtra = Mathf.Max(desiredExtra, Mathf.Min(closedEdges.Count, profile.minStopGraphLoops));
        if (profile.maxStopGraphLoops == 0)
            desiredExtra = 0;

        int addedBraid = 0;
        // Pass 1: open braid edges while keeping at least 1 dead-end pocket if profile allows dead-ends
        int currentDeadEnds = CountDeadEndSlots(cols, rows, masks);
        for (int pass = 0; pass < 2 && addedBraid < desiredExtra; pass++)
        {
            for (int i = 0; i < closedEdges.Count && addedBraid < desiredExtra; i++)
            {
                SlotEdge e = closedEdges[i];
                MazeSideMask dirMask = MazeDirectionUtil.ToMask(e.DirFromA);
                if ((masks[e.A.x, e.A.y] & dirMask) != 0)
                    continue;

                if (pass == 0 && profile.maxDeadEnds >= 1)
                {
                    int degA = CountMaskBits(masks[e.A.x, e.A.y]);
                    int degB = CountMaskBits(masks[e.B.x, e.B.y]);
                    int deadLoss = (degA == 1 ? 1 : 0) + (degB == 1 ? 1 : 0);
                    if (deadLoss > 0 && currentDeadEnds - deadLoss < 1)
                        continue;
                }

                if (CountMaskBits(masks[e.A.x, e.A.y]) == 1) currentDeadEnds--;
                if (CountMaskBits(masks[e.B.x, e.B.y]) == 1) currentDeadEnds--;

                masks[e.A.x, e.A.y] |= dirMask;
                masks[e.B.x, e.B.y] |= MazeDirectionUtil.ToMask(MazeDirectionUtil.Opposite(e.DirFromA));
                connectedEdgeCount++;
                addedBraid++;
            }
        }

        return masks;
    }

    private static void PruneExcessDeadEnds(
        int cols,
        int rows,
        MazeSideMask[,] masks,
        int maxDeadEnds,
        System.Random rng,
        ref int connectedEdgeCount)
    {
        int safety = cols * rows * 2;
        while (CountDeadEndSlots(cols, rows, masks) > maxDeadEnds && safety-- > 0)
        {
            var leaves = new List<Vector2Int>();
            for (int x = 0; x < cols; x++)
                for (int y = 0; y < rows; y++)
                    if (CountMaskBits(masks[x, y]) == 1)
                        leaves.Add(new Vector2Int(x, y));

            Shuffle(leaves, rng);
            bool openedAny = false;

            for (int i = 0; i < leaves.Count; i++)
            {
                Vector2Int leaf = leaves[i];
                if (CountMaskBits(masks[leaf.x, leaf.y]) != 1)
                    continue;

                MazeDir? bestDir = null;
                int bestNeighborDeg = int.MaxValue;

                // Evaluate directions in randomized order, preferring low-degree neighbors
                int dirOffset = rng.Next(4);
                for (int k = 0; k < 4; k++)
                {
                    MazeDir dir = MazeDirectionUtil.AllDirections[(dirOffset + k) % 4];
                    MazeSideMask m = MazeDirectionUtil.ToMask(dir);
                    if ((masks[leaf.x, leaf.y] & m) != 0)
                        continue;

                    Vector2Int nxt = leaf + MazeDirectionUtil.ToStep(dir);
                    if (nxt.x < 0 || nxt.x >= cols || nxt.y < 0 || nxt.y >= rows)
                        continue;

                    int deg = CountMaskBits(masks[nxt.x, nxt.y]);
                    if (deg < bestNeighborDeg)
                    {
                        bestNeighborDeg = deg;
                        bestDir = dir;
                    }
                }

                if (bestDir.HasValue)
                {
                    Vector2Int nxt = leaf + MazeDirectionUtil.ToStep(bestDir.Value);
                    masks[leaf.x, leaf.y] |= MazeDirectionUtil.ToMask(bestDir.Value);
                    masks[nxt.x, nxt.y] |= MazeDirectionUtil.ToMask(MazeDirectionUtil.Opposite(bestDir.Value));
                    connectedEdgeCount++;
                    openedAny = true;
                    break;
                }
            }

            if (!openedAny)
                break;
        }
    }

    private static List<SlotEdge> CollectClosedInteriorEdges(int cols, int rows, MazeSideMask[,] masks)
    {
        var list = new List<SlotEdge>();
        for (int x = 0; x < cols; x++)
        {
            for (int y = 0; y < rows; y++)
            {
                var a = new Vector2Int(x, y);
                if (x + 1 < cols && (masks[x, y] & MazeSideMask.East) == 0)
                    list.Add(new SlotEdge(a, MazeDir.East, new Vector2Int(x + 1, y)));
                if (y + 1 < rows && (masks[x, y] & MazeSideMask.North) == 0)
                    list.Add(new SlotEdge(a, MazeDir.North, new Vector2Int(x, y + 1)));
            }
        }
        return list;
    }

    // ─────────────────────────────────────────────────────────────
    //  Stage 2 — Catcher-Aware Pattern Selection & Stamping
    // ─────────────────────────────────────────────────────────────

    private static bool TryStampCatcherAwareModules(
        int cols,
        int rows,
        MazeSideMask[,] slotMasks,
        int connectedEdges,
        MazeDifficultyProfile profile,
        MazePatternLibrary library,
        HashSet<MazePatternGroup> allowedGroups,
        System.Random rng,
        out MazeGrid grid,
        out List<MazePlacedModule> placedModules,
        out MazePlacementTelemetry telemetry)
    {
        grid = new MazeGrid(cols * MazePattern.Stride + 1, rows * MazePattern.Stride + 1);
        placedModules = new List<MazePlacedModule>(cols * rows);
        telemetry = default;

        var chosenGrid = new MazePatternPlacement[cols, rows];
        var hasPlaced = new bool[cols, rows];
        var usedPatternIds = new HashSet<string>();
        var usedGroups = new HashSet<MazePatternGroup>();

        // Visit slots in randomized order so caps & novelty bonuses spread evenly across the map
        var slotOrder = new List<Vector2Int>(cols * rows);
        for (int y = 0; y < rows; y++)
            for (int x = 0; x < cols; x++)
                slotOrder.Add(new Vector2Int(x, y));
        Shuffle(slotOrder, rng);

        bool chambersAllowedByGroup = allowedGroups == null
                                   || allowedGroups.Count == 0
                                   || allowedGroups.Contains(MazePatternGroup.Chamber);
        int chambersPlaced = 0;
        int deadEndsPlaced = 0;
        int straightRunwaysFormed = 0;

        for (int i = 0; i < slotOrder.Count; i++)
        {
            Vector2Int slot = slotOrder[i];
            MazeSideMask reqMask = slotMasks[slot.x, slot.y];

            List<MazePatternPlacement> rawCandidates = library.GetCandidates(
                reqMask,
                allowedGroups != null && allowedGroups.Count > 0 ? allowedGroups : null);

            if (rawCandidates.Count == 0)
                rawCandidates = library.GetCandidates(reqMask, null);
            if (rawCandidates.Count == 0)
                return false;

            // Filter chambers once maxChambers is reached (or when Chamber group is disallowed)
            var filtered = new List<MazePatternPlacement>(rawCandidates.Count);
            bool canPlaceChamber = chambersAllowedByGroup && chambersPlaced < profile.maxChambers;
            for (int c = 0; c < rawCandidates.Count; c++)
            {
                if (rawCandidates[c].Pattern.group == MazePatternGroup.Chamber && !canPlaceChamber)
                    continue;
                filtered.Add(rawCandidates[c]);
            }
            if (filtered.Count == 0)
                return false;

            // Enforce 4-port I03_Cross adjacency ban if an alternative (e.g. L01_RingHub) exists
            if (reqMask == MazeSideMask.All && filtered.Count > 1 && HasAdjacentPassThroughNeighbor(slot.x, slot.y, cols, rows, reqMask, chosenGrid, hasPlaced))
            {
                var nonCross = new List<MazePatternPlacement>(filtered.Count);
                for (int c = 0; c < filtered.Count; c++)
                {
                    if (filtered[c].Pattern.id != "I03_Cross")
                        nonCross.Add(filtered[c]);
                }
                if (nonCross.Count > 0)
                    filtered = nonCross;
            }

            // Compute dynamic catcher-aware weights
            var weights = new float[filtered.Count];
            float weightSum = 0f;

            int consecutiveStraights = CountConsecutiveAxisStraights(slot.x, slot.y, cols, rows, reqMask, chosenGrid, hasPlaced);
            bool neighborIsPassThrough = HasAdjacentPassThroughNeighbor(slot.x, slot.y, cols, rows, reqMask, chosenGrid, hasPlaced);

            for (int c = 0; c < filtered.Count; c++)
            {
                MazePattern p = filtered[c].Pattern;
                float w = Mathf.Max(0.01f, p.baseWeight);

                // 4-port hub rule: strongly favor L01_RingHub over I03_Cross
                if (reqMask == MazeSideMask.All && p.id == "L01_RingHub")
                    w *= 2.6f;

                // 2-port straight axis rule: cap uninterrupted C01_Straight runs with C03_Chicane
                if (reqMask == (MazeSideMask.East | MazeSideMask.West) || reqMask == (MazeSideMask.North | MazeSideMask.South))
                {
                    if (consecutiveStraights >= 2)
                    {
                        if (p.id == "C03_Chicane") w *= 3.5f;
                        else if (p.id == "C01_Straight") w *= 0.20f;
                    }
                    else if (consecutiveStraights == 1 && straightRunwaysFormed == 0 && p.id == "C01_Straight")
                    {
                        w *= 1.35f;
                    }
                }

                // 3-port junction rule: boost all-3-port catchers next to pass-through corridors
                if (CountMaskBits(reqMask) == 3 && neighborIsPassThrough && (p.id == "I02_TBaffled" || p.id == "R02_Chamber3Port"))
                {
                    w *= 1.35f;
                }

                // Novelty & anti-adjacent-duplicate weighting
                if (!usedPatternIds.Contains(p.id))
                    w *= 1.35f;
                if (HasConnectedNeighborWithPatternId(slot.x, slot.y, cols, rows, reqMask, chosenGrid, hasPlaced, p.id))
                    w *= 0.45f;

                weights[c] = w;
                weightSum += w;
            }

            MazePatternPlacement chosen = PickWeighted(filtered, weights, weightSum, rng);
            chosenGrid[slot.x, slot.y] = chosen;
            hasPlaced[slot.x, slot.y] = true;

            usedPatternIds.Add(chosen.Pattern.id);
            usedGroups.Add(chosen.Pattern.group);
            if (chosen.Pattern.group == MazePatternGroup.Chamber)
                chambersPlaced++;
            if (CountMaskBits(reqMask) == 1)
                deadEndsPlaced++;
            if (chosen.Pattern.id == "C01_Straight" && consecutiveStraights >= 1)
                straightRunwaysFormed++;
        }

        // Stamp slots in canonical (x, y) order and compute exact adjacent duplicate telemetry
        int adjacentDuplicates = 0;
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < cols; x++)
            {
                MazePatternPlacement pl = chosenGrid[x, y];
                if (!grid.TryStampPattern(pl.Pattern, pl.QuarterTurnsCW, x, y, out _))
                    return false;

                placedModules.Add(new MazePlacedModule(x, y, pl.Pattern.id, pl.Pattern.group, pl.QuarterTurnsCW, pl.PortMask));

                if (x + 1 < cols && (slotMasks[x, y] & MazeSideMask.East) != 0 && chosenGrid[x + 1, y].Pattern.id == pl.Pattern.id)
                    adjacentDuplicates++;
                if (y + 1 < rows && (slotMasks[x, y] & MazeSideMask.North) != 0 && chosenGrid[x, y + 1].Pattern.id == pl.Pattern.id)
                    adjacentDuplicates++;
            }
        }

        telemetry = new MazePlacementTelemetry(
            slotCols: cols,
            slotRows: rows,
            distinctPatternCount: usedPatternIds.Count,
            distinctGroupCount: usedGroups.Count,
            adjacentDuplicateCount: adjacentDuplicates,
            connectedSlotEdges: connectedEdges,
            deadEndSlotCount: deadEndsPlaced,
            chamberSlotCount: chambersPlaced);

        return true;
    }

    private static bool HasAdjacentPassThroughNeighbor(
        int sx,
        int sy,
        int cols,
        int rows,
        MazeSideMask mask,
        MazePatternPlacement[,] chosenGrid,
        bool[,] hasPlaced)
    {
        for (int d = 0; d < MazeDirectionUtil.AllDirections.Length; d++)
        {
            MazeDir dir = MazeDirectionUtil.AllDirections[d];
            if ((mask & MazeDirectionUtil.ToMask(dir)) == 0)
                continue;

            Vector2Int nxt = new Vector2Int(sx, sy) + MazeDirectionUtil.ToStep(dir);
            if (nxt.x < 0 || nxt.x >= cols || nxt.y < 0 || nxt.y >= rows || !hasPlaced[nxt.x, nxt.y])
                continue;

            string nId = chosenGrid[nxt.x, nxt.y].Pattern.id;
            if (nId == "C01_Straight" || nId == "I03_Cross" || nId == "I01_TJunction")
                return true;
        }
        return false;
    }

    private static int CountConsecutiveAxisStraights(
        int sx,
        int sy,
        int cols,
        int rows,
        MazeSideMask mask,
        MazePatternPlacement[,] chosenGrid,
        bool[,] hasPlaced)
    {
        bool horiz = mask == (MazeSideMask.East | MazeSideMask.West);
        bool vert = mask == (MazeSideMask.North | MazeSideMask.South);
        if (!horiz && !vert)
            return 0;

        int count = 0;
        Vector2Int stepPos = horiz ? Vector2Int.right : Vector2Int.up;
        Vector2Int stepNeg = -stepPos;

        Vector2Int cur = new Vector2Int(sx, sy) + stepPos;
        while (cur.x >= 0 && cur.x < cols && cur.y >= 0 && cur.y < rows && hasPlaced[cur.x, cur.y]
               && chosenGrid[cur.x, cur.y].Pattern.id == "C01_Straight")
        {
            count++;
            cur += stepPos;
        }

        cur = new Vector2Int(sx, sy) + stepNeg;
        while (cur.x >= 0 && cur.x < cols && cur.y >= 0 && cur.y < rows && hasPlaced[cur.x, cur.y]
               && chosenGrid[cur.x, cur.y].Pattern.id == "C01_Straight")
        {
            count++;
            cur += stepNeg;
        }

        return count;
    }

    private static bool HasConnectedNeighborWithPatternId(
        int sx,
        int sy,
        int cols,
        int rows,
        MazeSideMask mask,
        MazePatternPlacement[,] chosenGrid,
        bool[,] hasPlaced,
        string patternId)
    {
        for (int d = 0; d < MazeDirectionUtil.AllDirections.Length; d++)
        {
            MazeDir dir = MazeDirectionUtil.AllDirections[d];
            if ((mask & MazeDirectionUtil.ToMask(dir)) == 0)
                continue;

            Vector2Int nxt = new Vector2Int(sx, sy) + MazeDirectionUtil.ToStep(dir);
            if (nxt.x >= 0 && nxt.x < cols && nxt.y >= 0 && nxt.y < rows
                && hasPlaced[nxt.x, nxt.y]
                && chosenGrid[nxt.x, nxt.y].Pattern.id == patternId)
            {
                return true;
            }
        }
        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Stage 3 — Soft-Lock-Free (Spawn, Exit) Placement & Scoring
    // ─────────────────────────────────────────────────────────────

    private static bool TrySelectBestSpawnAndExit(
        MazeGrid grid,
        MazeDifficultyProfile profile,
        MazePlacementTelemetry telemetry,
        System.Random rng,
        out MazeValidationReport bestValidation,
        out MazeScoreBreakdown bestScore)
    {
        bestValidation = null;
        bestScore = null;

        List<Vector2Int> wallAdj = MazeSlideGraph.FindWallAdjacentFloorCells(grid);
        if (wallAdj.Count < 2)
            return false;

        Shuffle(wallAdj, rng);

        // Prioritize corner/pocket cells (>= 2 adjacent walls) as natural Spawn anchors
        var orderedSpawns = new List<Vector2Int>(wallAdj.Count);
        for (int i = 0; i < wallAdj.Count; i++)
            if (CountAdjacentWalls(grid, wallAdj[i]) >= 2)
                orderedSpawns.Add(wallAdj[i]);
        for (int i = 0; i < wallAdj.Count; i++)
            if (CountAdjacentWalls(grid, wallAdj[i]) < 2)
                orderedSpawns.Add(wallAdj[i]);

        int spawnLimit = Mathf.Min(MaxSpawnCandidatesPerAttempt, orderedSpawns.Count);
        var topPairs = new List<SpawnExitCandidate>(TopPairsToFullScorePerAttempt + 1);

        var depthMap = new Dictionary<Vector2Int, int>(64);
        var parentMap = new Dictionary<Vector2Int, Vector2Int>(64);
        var bfs = new Queue<Vector2Int>(64);
        var pathScratch = new List<Vector2Int>(32);

        int maxSpan = Mathf.Max(2, (grid.Width - 3) + (grid.Height - 3));

        for (int s = 0; s < spawnLimit; s++)
        {
            Vector2Int spawn = orderedSpawns[s];
            List<Vector2Int> safeExits = MazeSlideGraph.GetZeroSoftLockExitCandidates(grid, spawn, out MazeSlideGraph spawnGraph);
            if (safeExits.Count == 0 || spawnGraph.FloorCoverage < 0.45f)
                continue;

            // Compute shortest stop-depths and parent pointers across spawnGraph once for all safeExits
            ComputeStopTreeFromGraph(spawn, spawnGraph, depthMap, parentMap, bfs);

            float loopsFit = EstimateBandFit(spawnGraph.StopGraphLoops, profile.minStopGraphLoops, profile.maxStopGraphLoops, 3f);

            for (int e = 0; e < safeExits.Count; e++)
            {
                Vector2Int exit = safeExits[e];
                if (!depthMap.TryGetValue(exit, out int moves) || moves <= 0)
                    continue;

                int decisions = CountDecisionsAlongParentChain(spawn, exit, parentMap, spawnGraph, pathScratch);
                float movesFit = EstimateBandFit(moves, profile.minSolutionMoves, profile.maxSolutionMoves, 5f);
                float decFit = EstimateBandFit(decisions, profile.minDecisionStops, profile.maxDecisionStops, 3f);

                int manhattan = Mathf.Abs(exit.x - spawn.x) + Mathf.Abs(exit.y - spawn.y);
                float sepNorm = Mathf.Clamp01((float)manhattan / maxSpan / 0.45f);
                int exitWalls = CountAdjacentWalls(grid, exit);
                float pocketBonus = exitWalls >= 3 ? 1.5f : (exitWalls == 2 ? 0.6f : 0f);

                float heuristic = 45f * movesFit
                                + 25f * decFit
                                + 15f * loopsFit
                                + 15f * sepNorm
                                + 12f * spawnGraph.FloorCoverage
                                + pocketBonus;

                InsertTopCandidate(topPairs, new SpawnExitCandidate(spawn, exit, heuristic), TopPairsToFullScorePerAttempt);
            }
        }

        if (topPairs.Count == 0)
            return false;

        Vector2Int chosenSpawn = default;
        Vector2Int chosenExit = default;
        float bestRank = float.NegativeInfinity;

        for (int i = 0; i < topPairs.Count; i++)
        {
            SpawnExitCandidate pair = topPairs[i];
            grid.SetSpawn(pair.Spawn);
            grid.SetExit(pair.Exit);

            MazeValidationReport report = MazeValidator.Validate(grid, profile.requireExitOnStopCell);
            if (!report.IsValid)
                continue;

            MazeScoreBreakdown score = MazeScorer.Evaluate(grid, report, profile, telemetry);
            if (!score.IsValid)
                continue;

            float rank = ComputeCombinedRank(score, profile.targetScore)
                       + (CountAdjacentWalls(grid, pair.Exit) >= 3 ? 0.35f : 0f);

            if (bestValidation == null || rank > bestRank)
            {
                bestRank = rank;
                chosenSpawn = pair.Spawn;
                chosenExit = pair.Exit;
                bestValidation = report;
                bestScore = score;
            }
        }

        if (bestValidation == null)
            return false;

        // Ensure grid markers and Validation.GridRevision match the winning pair!
        if (grid.Spawn != chosenSpawn || grid.Exit != chosenExit || !bestValidation.IsFreshFor(grid))
        {
            grid.SetSpawn(chosenSpawn);
            grid.SetExit(chosenExit);
            bestValidation = MazeValidator.Validate(grid, profile.requireExitOnStopCell);
            bestScore = MazeScorer.Evaluate(grid, bestValidation, profile, telemetry);
        }

        return bestValidation.IsValid && bestScore.IsValid;
    }

    private static void ComputeStopTreeFromGraph(
        Vector2Int spawn,
        MazeSlideGraph graph,
        Dictionary<Vector2Int, int> depthMap,
        Dictionary<Vector2Int, Vector2Int> parentMap,
        Queue<Vector2Int> bfs)
    {
        depthMap.Clear();
        parentMap.Clear();
        bfs.Clear();

        depthMap[spawn] = 0;
        bfs.Enqueue(spawn);

        while (bfs.Count > 0)
        {
            Vector2Int u = bfs.Dequeue();
            int nextDepth = depthMap[u] + 1;
            if (!graph.OutgoingByStop.TryGetValue(u, out List<MazeSlideEdge> outEdges))
                continue;

            for (int i = 0; i < outEdges.Count; i++)
            {
                Vector2Int v = outEdges[i].To;
                if (!depthMap.ContainsKey(v))
                {
                    depthMap[v] = nextDepth;
                    parentMap[v] = u;
                    bfs.Enqueue(v);
                }
            }
        }
    }

    private static int CountDecisionsAlongParentChain(
        Vector2Int spawn,
        Vector2Int exit,
        Dictionary<Vector2Int, Vector2Int> parentMap,
        MazeSlideGraph graph,
        List<Vector2Int> scratch)
    {
        scratch.Clear();
        Vector2Int cur = exit;
        scratch.Add(cur);
        while (cur != spawn && parentMap.TryGetValue(cur, out Vector2Int prev))
        {
            cur = prev;
            scratch.Add(cur);
        }
        scratch.Reverse();

        int decisions = 0;
        for (int i = 0; i < scratch.Count - 1; i++)
        {
            Vector2Int stop = scratch[i];
            if (!graph.OutgoingByStop.TryGetValue(stop, out List<MazeSlideEdge> outEdges))
                continue;

            if (i == 0)
            {
                if (outEdges.Count >= 2)
                    decisions++;
            }
            else
            {
                Vector2Int prevStop = scratch[i - 1];
                int choices = 0;
                for (int e = 0; e < outEdges.Count; e++)
                    if (outEdges[e].To != prevStop)
                        choices++;
                if (choices >= 2)
                    decisions++;
            }
        }

        return decisions;
    }

    private static void InsertTopCandidate(List<SpawnExitCandidate> list, SpawnExitCandidate item, int maxCount)
    {
        int insertAt = list.Count;
        for (int i = 0; i < list.Count; i++)
        {
            if (item.HeuristicRank > list[i].HeuristicRank)
            {
                insertAt = i;
                break;
            }
        }

        if (insertAt < maxCount)
        {
            list.Insert(insertAt, item);
            if (list.Count > maxCount)
                list.RemoveAt(maxCount);
        }
    }

    private static float ComputeCombinedRank(MazeScoreBreakdown score, float targetScore)
    {
        float targetBonus = score.QualityScore >= targetScore ? 6f : 0f;
        return 0.52f * score.QualityScore + 0.48f * score.DifficultyFit + targetBonus;
    }

    // ─────────────────────────────────────────────────────────────
    //  Deterministic Math & Sampling Utilities
    // ─────────────────────────────────────────────────────────────

    private static int MixAttemptSeed(int baseSeed, int attempt, int streamSample)
    {
        unchecked
        {
            int h = baseSeed * 397 ^ (attempt * 7919) ^ streamSample;
            h ^= (int)((uint)h >> 16);
            h *= -2048144789;
            h ^= (int)((uint)h >> 13);
            return h;
        }
    }

    private static MazePatternPlacement PickWeighted(
        List<MazePatternPlacement> items,
        float[] weights,
        float weightSum,
        System.Random rng)
    {
        if (items.Count == 1 || weightSum <= 0f)
            return items[0];

        double roll = rng.NextDouble() * weightSum;
        float acc = 0f;
        for (int i = 0; i < items.Count - 1; i++)
        {
            acc += weights[i];
            if (roll <= acc)
                return items[i];
        }
        return items[items.Count - 1];
    }

    private static void Shuffle<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static int CountDeadEndSlots(int cols, int rows, MazeSideMask[,] masks)
    {
        int count = 0;
        for (int x = 0; x < cols; x++)
            for (int y = 0; y < rows; y++)
                if (CountMaskBits(masks[x, y]) == 1)
                    count++;
        return count;
    }

    private static int CountMaskBits(MazeSideMask mask)
    {
        int v = (int)mask & 0xF;
        return (v & 1) + ((v >> 1) & 1) + ((v >> 2) & 1) + ((v >> 3) & 1);
    }

    private static int CountAdjacentWalls(MazeGrid grid, Vector2Int cell)
    {
        int walls = 0;
        if (grid.IsWall(cell + Vector2Int.up)) walls++;
        if (grid.IsWall(cell + Vector2Int.down)) walls++;
        if (grid.IsWall(cell + Vector2Int.left)) walls++;
        if (grid.IsWall(cell + Vector2Int.right)) walls++;
        return walls;
    }

    private static float EstimateBandFit(int value, int min, int max, float falloff)
    {
        if (value >= min && value <= max) return 1f;
        int d = value < min ? (min - value) : (value - max);
        return Mathf.Clamp01(1f - d / falloff);
    }
}
