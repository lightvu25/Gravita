using System.Collections.Generic;
using UnityEngine;

public enum MazeDifficultyTier
{
    Learn    = 0,
    Navigate = 1,
    Master   = 2,
}

public enum MazeSizePreset
{
    Small  = 0, // 2x3 slots = 9x13 cells
    Medium = 1, // 3x5 slots = 13x21 cells
    Large  = 2, // 4x7 slots = 17x29 cells
    Custom = 3,
}

/// <summary>
/// Initial tunable difficulty profile and quality-scoring configuration for the Gravita Maze Generator.
/// </summary>
[CreateAssetMenu(menuName = "Gravita/Maze/Maze Difficulty Profile", fileName = "Profile_Navigate")]
public class MazeDifficultyProfile : ScriptableObject
{
    [Header("Identity & Default Size")]
    public string profileId = "Navigate";
    public MazeDifficultyTier tier = MazeDifficultyTier.Navigate;
    public MazeSizePreset defaultPreset = MazeSizePreset.Medium;
    [Range(2, 8)] public int customSlotCols = 3;
    [Range(2, 10)] public int customSlotRows = 5;

    [Header("Stop-Graph Target Bands (Initial Tunable Values)")]
    [Min(1)] public int minSolutionMoves = 8;
    [Min(1)] public int maxSolutionMoves = 14;
    [Min(0)] public int minDecisionStops = 3;
    [Min(0)] public int maxDecisionStops = 6;
    [Min(0)] public int minStopGraphLoops = 1;
    [Min(0)] public int maxStopGraphLoops = 4;

    [Header("Topology & Pattern Controls")]
    [Range(0f, 1f)] public float braidRatio = 0.30f;
    [Min(0)] public int maxDeadEnds = 3;
    [Min(0)] public int maxChambers = 1;
    [Min(3)] public int minLongRunway = 5;
    public bool requireExitOnStopCell = true;

    public List<MazePatternGroup> allowedGroups = new List<MazePatternGroup>
    {
        MazePatternGroup.Corridor,
        MazePatternGroup.Intersection,
        MazePatternGroup.Loop,
        MazePatternGroup.DeadEnd,
        MazePatternGroup.Chamber,
    };

    [Header("Search Budget")]
    [Min(1)] public int maxAttempts = 50;
    [Range(0f, 100f)] public float targetScore = 80f;

    [Header("Quality Score Weights (GDD §9.3 — Normalized Before Scoring)")]
    public float playabilityWeight = 40f;
    public float diversityWeight = 35f;
    public float layoutWeight = 25f;

    public static Vector2Int ResolveSlotDimensions(MazeSizePreset preset, int customCols = 3, int customRows = 5)
    {
        return preset switch
        {
            MazeSizePreset.Small  => new Vector2Int(2, 3), // 9 x 13
            MazeSizePreset.Medium => new Vector2Int(3, 5), // 13 x 21
            MazeSizePreset.Large  => new Vector2Int(4, 7), // 17 x 29
            _                     => new Vector2Int(Mathf.Clamp(customCols, 2, 8), Mathf.Clamp(customRows, 2, 10)),
        };
    }

    public Vector2Int GetSlotDimensions(MazeSizePreset? overridePreset = null, int? overrideCols = null, int? overrideRows = null)
    {
        MazeSizePreset p = overridePreset ?? defaultPreset;
        return ResolveSlotDimensions(p, overrideCols ?? customSlotCols, overrideRows ?? customSlotRows);
    }

    public HashSet<MazePatternGroup> BuildAllowedGroupSet()
    {
        var set = new HashSet<MazePatternGroup>();
        if (allowedGroups != null)
        {
            for (int i = 0; i < allowedGroups.Count; i++)
                set.Add(allowedGroups[i]);
        }
        return set;
    }

    /// <summary>
    /// Enforces GDD §9.3 weight rules: rejects negative, NaN, Infinity, or zero-sum weights;
    /// otherwise normalizes weights so wPlayability + wDiversity + wLayout == 1.0f.
    /// </summary>
    public bool TryGetNormalizedQualityWeights(
        out float normPlayability,
        out float normDiversity,
        out float normLayout,
        out string error)
    {
        return TryNormalizeWeights(
            playabilityWeight,
            diversityWeight,
            layoutWeight,
            out normPlayability,
            out normDiversity,
            out normLayout,
            out error);
    }

    public static bool TryNormalizeWeights(
        float playability,
        float diversity,
        float layout,
        out float normPlayability,
        out float normDiversity,
        out float normLayout,
        out string error)
    {
        normPlayability = 0f;
        normDiversity = 0f;
        normLayout = 0f;
        error = null;

        if (float.IsNaN(playability) || float.IsInfinity(playability)
            || float.IsNaN(diversity) || float.IsInfinity(diversity)
            || float.IsNaN(layout) || float.IsInfinity(layout))
        {
            error = "Quality weights cannot be NaN or Infinity.";
            return false;
        }

        if (playability < 0f || diversity < 0f || layout < 0f)
        {
            error = "Quality weights cannot be negative.";
            return false;
        }

        float sum = playability + diversity + layout;
        if (sum <= 0.00001f)
        {
            error = "Quality weight sum must be strictly greater than zero.";
            return false;
        }

        normPlayability = playability / sum;
        normDiversity = diversity / sum;
        normLayout = layout / sum;
        return true;
    }

    private void OnValidate()
    {
        customSlotCols = Mathf.Clamp(customSlotCols, 2, 8);
        customSlotRows = Mathf.Clamp(customSlotRows, 2, 10);

        minSolutionMoves = Mathf.Max(1, minSolutionMoves);
        maxSolutionMoves = Mathf.Max(minSolutionMoves, maxSolutionMoves);

        minDecisionStops = Mathf.Max(0, minDecisionStops);
        maxDecisionStops = Mathf.Max(minDecisionStops, maxDecisionStops);

        minStopGraphLoops = Mathf.Max(0, minStopGraphLoops);
        maxStopGraphLoops = Mathf.Max(minStopGraphLoops, maxStopGraphLoops);

        maxDeadEnds = Mathf.Max(0, maxDeadEnds);
        maxChambers = Mathf.Max(0, maxChambers);
        minLongRunway = Mathf.Max(3, minLongRunway);
        maxAttempts = Mathf.Max(1, maxAttempts);

        playabilityWeight = Mathf.Max(0f, playabilityWeight);
        diversityWeight = Mathf.Max(0f, diversityWeight);
        layoutWeight = Mathf.Max(0f, layoutWeight);
    }
}

