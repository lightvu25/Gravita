using System.Collections.Generic;
using UnityEngine;

public enum MazeValidationSeverity
{
    Info    = 0,
    Warning = 1,
    Error   = 2,
}

public enum MazeValidationCode
{
    InvalidDimensions      = 0,
    OpenBoundary           = 1,
    MissingSpawn           = 2,
    MissingExit            = 3,
    SpawnOutOfBounds       = 4,
    ExitOutOfBounds        = 5,
    SpawnOnWall            = 6,
    ExitOnWall             = 7,
    SpawnEqualsExit        = 8,
    Unsolvable             = 9,
    ExitNotStopCell        = 10,
    SoftLockDetected       = 11,
    UnreachableCollectible = 12,
    UnreachableFloorCells  = 13,
    HighOneCellSlideRatio  = 14,
}

public readonly struct MazeValidationIssue
{
    public readonly MazeValidationCode Code;
    public readonly MazeValidationSeverity Severity;
    public readonly string Message;
    public readonly Vector2Int? Cell;

    public MazeValidationIssue(
        MazeValidationCode code,
        MazeValidationSeverity severity,
        string message,
        Vector2Int? cell = null)
    {
        Code = code;
        Severity = severity;
        Message = message;
        Cell = cell;
    }
}

/// <summary>
/// Immutable validation snapshot stamped with <see cref="GridRevision"/> so any subsequent
/// cell, Spawn, or Exit edit immediately marks the report stale before Apply.
/// </summary>
public sealed class MazeValidationReport
{
    public int GridRevision { get; }
    public MazeSlideGraph SlideGraph { get; }
    public IReadOnlyList<MazeValidationIssue> Issues => issues;
    public int ErrorCount { get; private set; }
    public int WarningCount { get; private set; }

    public bool IsValid => ErrorCount == 0;

    private readonly List<MazeValidationIssue> issues = new List<MazeValidationIssue>();

    public MazeValidationReport(int gridRevision, MazeSlideGraph slideGraph)
    {
        GridRevision = gridRevision;
        SlideGraph = slideGraph;
    }

    public bool IsFreshFor(MazeGrid grid) => grid != null && GridRevision == grid.Revision;

    public bool HasIssue(MazeValidationCode code)
    {
        for (int i = 0; i < issues.Count; i++)
            if (issues[i].Code == code)
                return true;
        return false;
    }

    internal void AddIssue(MazeValidationCode code, MazeValidationSeverity severity, string message, Vector2Int? cell = null)
    {
        issues.Add(new MazeValidationIssue(code, severity, message, cell));
        if (severity == MazeValidationSeverity.Error)
            ErrorCount++;
        else if (severity == MazeValidationSeverity.Warning)
            WarningCount++;
    }
}

/// <summary>
/// Stop-state graph validator for generated and preview-edited Gravita mazes.
/// Enforces closed boundaries, valid Spawn/Exit placement, slide solvability,
/// Exit-on-stop-cell requirement (Q3), and zero reachable soft-locks (Q6).
/// </summary>
public static class MazeValidator
{
    public static MazeValidationReport Validate(
        MazeGrid grid,
        bool requireExitOnStopCell = true,
        IReadOnlyList<Vector2Int> requiredCollectibleCells = null)
    {
        if (grid == null)
        {
            var nullReport = new MazeValidationReport(-1, null);
            nullReport.AddIssue(MazeValidationCode.InvalidDimensions, MazeValidationSeverity.Error, "MazeGrid is null.");
            return nullReport;
        }

        if (grid.Width < 3 || grid.Height < 3)
        {
            var dimReport = new MazeValidationReport(grid.Revision, null);
            dimReport.AddIssue(
                MazeValidationCode.InvalidDimensions,
                MazeValidationSeverity.Error,
                $"Grid dimensions ({grid.Width}x{grid.Height}) must be at least 3x3.");
            return dimReport;
        }

        // Check outer perimeter closure
        Vector2Int? firstOpenBorder = FindFirstOpenBoundaryCell(grid);

        // Validate Spawn & Exit markers before graph construction
        bool spawnOk = true;
        bool exitOk = true;
        var preIssues = new List<MazeValidationIssue>();

        if (firstOpenBorder.HasValue)
        {
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.OpenBoundary,
                MazeValidationSeverity.Error,
                $"Outer perimeter must be closed Wall; found open cell at {firstOpenBorder.Value}.",
                firstOpenBorder.Value));
        }

        if (!grid.Spawn.HasValue)
        {
            spawnOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.MissingSpawn,
                MazeValidationSeverity.Error,
                "Spawn cell is not placed."));
        }
        else if (!grid.InBounds(grid.Spawn.Value))
        {
            spawnOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.SpawnOutOfBounds,
                MazeValidationSeverity.Error,
                $"Spawn {grid.Spawn.Value} is outside grid bounds ({grid.Width}x{grid.Height}).",
                grid.Spawn.Value));
        }
        else if (!grid.IsFloor(grid.Spawn.Value))
        {
            spawnOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.SpawnOnWall,
                MazeValidationSeverity.Error,
                $"Spawn {grid.Spawn.Value} sits on a Wall cell.",
                grid.Spawn.Value));
        }

        if (!grid.Exit.HasValue)
        {
            exitOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.MissingExit,
                MazeValidationSeverity.Error,
                "Exit cell is not placed."));
        }
        else if (!grid.InBounds(grid.Exit.Value))
        {
            exitOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.ExitOutOfBounds,
                MazeValidationSeverity.Error,
                $"Exit {grid.Exit.Value} is outside grid bounds ({grid.Width}x{grid.Height}).",
                grid.Exit.Value));
        }
        else if (!grid.IsFloor(grid.Exit.Value))
        {
            exitOk = false;
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.ExitOnWall,
                MazeValidationSeverity.Error,
                $"Exit {grid.Exit.Value} sits on a Wall cell.",
                grid.Exit.Value));
        }

        if (spawnOk && exitOk && grid.Spawn.Value == grid.Exit.Value)
        {
            preIssues.Add(new MazeValidationIssue(
                MazeValidationCode.SpawnEqualsExit,
                MazeValidationSeverity.Error,
                $"Spawn and Exit cannot share cell {grid.Spawn.Value}.",
                grid.Spawn.Value));
            exitOk = false;
        }

        MazeSlideGraph slideGraph = spawnOk
            ? MazeSlideGraph.Build(grid, grid.Spawn.Value, requireExitOnStopCell)
            : null;

        var report = new MazeValidationReport(grid.Revision, slideGraph);
        for (int i = 0; i < preIssues.Count; i++)
        {
            MazeValidationIssue issue = preIssues[i];
            report.AddIssue(issue.Code, issue.Severity, issue.Message, issue.Cell);
        }

        if (slideGraph == null || !exitOk)
            return report;

        // 1. Solvability & Q3 Exit-on-stop-cell rule
        if (!slideGraph.IsExitPassThroughReached)
        {
            report.AddIssue(
                MazeValidationCode.Unsolvable,
                MazeValidationSeverity.Error,
                $"No valid slide sequence from Spawn {grid.Spawn.Value} reaches Exit {grid.Exit.Value}.",
                grid.Exit.Value);
        }
        else if (requireExitOnStopCell && !slideGraph.IsExitStopReached)
        {
            report.AddIssue(
                MazeValidationCode.ExitNotStopCell,
                MazeValidationSeverity.Error,
                $"Exit {grid.Exit.Value} is only passed through mid-slide and is never a reachable resting stop cell.",
                grid.Exit.Value);
        }

        // 2. Q6 Zero Reachable Soft-Locks (Hard Error)
        if (slideGraph.SoftLockedStops.Count > 0)
        {
            int previewCount = Mathf.Min(5, slideGraph.SoftLockedStops.Count);
            var coords = new string[previewCount];
            for (int i = 0; i < previewCount; i++)
                coords[i] = slideGraph.SoftLockedStops[i].ToString();

            string suffix = slideGraph.SoftLockedStops.Count > previewCount
                ? $" (+{slideGraph.SoftLockedStops.Count - previewCount} more)"
                : string.Empty;

            report.AddIssue(
                MazeValidationCode.SoftLockDetected,
                MazeValidationSeverity.Error,
                $"Found {slideGraph.SoftLockedStops.Count} reachable stop state(s) with no slide path to Exit: {string.Join(", ", coords)}{suffix}.",
                slideGraph.SoftLockedStops[0]);
        }

        // 3. Optional required collectibles reachability
        if (requiredCollectibleCells != null)
        {
            for (int i = 0; i < requiredCollectibleCells.Count; i++)
            {
                Vector2Int c = requiredCollectibleCells[i];
                if (!slideGraph.IsCellTraversed(c))
                {
                    report.AddIssue(
                        MazeValidationCode.UnreachableCollectible,
                        MazeValidationSeverity.Error,
                        $"Required collectible at {c} is never traversed by any reachable slide.",
                        c);
                }
            }
        }

        // 4. Non-blocking structural warnings
        if (slideGraph.TotalFloorCells > 0 && slideGraph.FloorCoverage < 0.65f)
        {
            report.AddIssue(
                MazeValidationCode.UnreachableFloorCells,
                MazeValidationSeverity.Warning,
                $"Only {slideGraph.FloorCoverage:P0} of floor cells are reachable from Spawn (disconnected or dead floor pockets).");
        }

        if (slideGraph.Edges.Count >= 4 && slideGraph.OneCellSlideRatio > 0.65f)
        {
            report.AddIssue(
                MazeValidationCode.HighOneCellSlideRatio,
                MazeValidationSeverity.Warning,
                $"High 1-cell stutter ratio ({slideGraph.OneCellSlideRatio:P0} of slide transitions move only 1 cell).");
        }

        return report;
    }

    private static Vector2Int? FindFirstOpenBoundaryCell(MazeGrid grid)
    {
        for (int x = 0; x < grid.Width; x++)
        {
            if (grid.GetCell(x, 0) != MazeCell.Wall)
                return new Vector2Int(x, 0);
            if (grid.GetCell(x, grid.Height - 1) != MazeCell.Wall)
                return new Vector2Int(x, grid.Height - 1);
        }

        for (int y = 1; y < grid.Height - 1; y++)
        {
            if (grid.GetCell(0, y) != MazeCell.Wall)
                return new Vector2Int(0, y);
            if (grid.GetCell(grid.Width - 1, y) != MazeCell.Wall)
                return new Vector2Int(grid.Width - 1, y);
        }

        return null;
    }
}
