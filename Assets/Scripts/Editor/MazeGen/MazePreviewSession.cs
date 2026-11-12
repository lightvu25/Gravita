using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Interactive cell-editing brushes available in the Maze Generator preview canvas.
/// </summary>
public enum MazePaintBrush
{
    Wall  = 0,
    Floor = 1,
    Spawn = 2,
    Exit  = 3,
}

/// <summary>
/// High-level readiness state of the preview buffer in <see cref="MazePreviewSession"/>.
/// </summary>
public enum MazePreviewStatus
{
    Empty   = 0,
    Valid   = 1,
    Stale   = 2,
    Invalid = 3,
}

/// <summary>
/// Pure Editor preview state controller backing <see cref="MazeGeneratorWindow"/>.
/// Owns the temporary <see cref="MazeGrid"/> buffer, brush stroke undo/redo stack,
/// <see cref="MazeValidationReport"/> freshness tracking, and <see cref="MazeScoreBreakdown"/>.
/// Never touches scene GameObjects, Tilemaps, or project assets.
/// </summary>
public sealed class MazePreviewSession
{
    private const int MaxUndoSnapshots = 32;

    public MazeGrid PreviewGrid { get; private set; }
    public MazeValidationReport Validation { get; private set; }
    public MazeScoreBreakdown Score { get; private set; }
    public MazePlacementTelemetry Telemetry { get; private set; }
    public IReadOnlyList<MazePlacedModule> PlacedModules { get; private set; } = Array.Empty<MazePlacedModule>();

    public int LastSeedUsed { get; private set; }
    public int LastAttemptsUsed { get; private set; }
    public MazeSizePreset LastPresetUsed { get; private set; } = MazeSizePreset.Medium;
    public bool HasManualEdits { get; private set; }
    public bool LockOuterBorder { get; set; } = true;

    public string StatusBannerMessage { get; private set; } = "No preview generated yet.";
    public string GenerationWarning { get; private set; }
    public string GenerationFailure { get; private set; }

    private readonly struct PreviewHistoryEntry
    {
        public readonly MazeGrid Grid;
        public readonly bool HadManualEdits;
        public readonly MazeValidationReport Validation;
        public readonly MazeScoreBreakdown Score;

        public PreviewHistoryEntry(
            MazeGrid grid,
            bool hadManualEdits,
            MazeValidationReport validation,
            MazeScoreBreakdown score)
        {
            Grid = grid;
            HadManualEdits = hadManualEdits;
            Validation = validation;
            Score = score;
        }
    }

    private readonly List<PreviewHistoryEntry> undoStack = new List<PreviewHistoryEntry>(MaxUndoSnapshots);
    private readonly List<PreviewHistoryEntry> redoStack = new List<PreviewHistoryEntry>(MaxUndoSnapshots);
    private bool strokeSnapshotRecorded;

    public bool HasPreview => PreviewGrid != null;

    /// <summary>
    /// True whenever <see cref="PreviewGrid"/> exists and either has not been validated yet
    /// or was mutated after <see cref="Validation"/> was computed.
    /// </summary>
    public bool IsValidationStale =>
        PreviewGrid != null && (Validation == null || !Validation.IsFreshFor(PreviewGrid));

    /// <summary>
    /// Gate condition for Phase 5 Apply: true ONLY when a preview exists, validation is fresh
    /// (<c>Validation.GridRevision == PreviewGrid.Revision</c>), zero hard errors exist,
    /// and no profile configuration error is active.
    /// </summary>
    public bool CanApply =>
        PreviewGrid != null
        && Validation != null
        && Validation.IsFreshFor(PreviewGrid)
        && Validation.IsValid
        && (Score == null || !Score.HasConfigError);

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public MazePreviewStatus Status
    {
        get
        {
            if (PreviewGrid == null) return MazePreviewStatus.Empty;
            if (IsValidationStale) return MazePreviewStatus.Stale;
            if (!Validation.IsValid || (Score != null && Score.HasConfigError)) return MazePreviewStatus.Invalid;
            return MazePreviewStatus.Valid;
        }
    }

    /// <summary>
    /// Runs <see cref="MazeGenerator.Generate"/> into the temporary preview buffer without mutating any scene objects.
    /// </summary>
    public MazeGenerationResult GeneratePreview(in MazeGenerationRequest request)
    {
        EndPaintStroke();
        undoStack.Clear();
        redoStack.Clear();
        HasManualEdits = false;

        MazeGenerationResult result = MazeGenerator.Generate(in request);
        LastSeedUsed = request.Seed;
        LastAttemptsUsed = result.AttemptsUsed;
        LastPresetUsed = result.ResolvedPreset;
        GenerationWarning = result.WarningMessage;
        GenerationFailure = result.FailureReason;

        if (!result.Success || result.Grid == null)
        {
            PreviewGrid = null;
            Validation = null;
            Score = result.Score;
            Telemetry = default;
            PlacedModules = Array.Empty<MazePlacedModule>();
            StatusBannerMessage = result.FailureReason ?? "Maze generation failed.";
            return result;
        }

        PreviewGrid = result.Grid;
        Validation = result.Validation;
        Score = result.Score;
        Telemetry = result.Telemetry;
        PlacedModules = result.PlacedModules ?? Array.Empty<MazePlacedModule>();
        StatusBannerMessage = BuildStatusSummary();
        return result;
    }

    /// <summary>
    /// Loads a saved <see cref="MazeMapData"/> asset into the temporary preview buffer and validates it.
    /// </summary>
    public bool LoadFromMapData(MazeMapData mapData, MazeDifficultyProfile activeProfile)
    {
        EndPaintStroke();
        if (mapData == null || mapData.rows == null || mapData.rows.Length == 0)
        {
            GenerationFailure = "Cannot load null or empty MazeMapData.";
            return false;
        }

        undoStack.Clear();
        redoStack.Clear();

        PreviewGrid = mapData.ToMazeGrid();
        LastSeedUsed = mapData.seed;
        LastAttemptsUsed = 1;
        LastPresetUsed = mapData.sizePreset;
        HasManualEdits = false;
        Telemetry = default;
        PlacedModules = Array.Empty<MazePlacedModule>();
        GenerationWarning = null;
        GenerationFailure = null;

        Revalidate(activeProfile);
        return true;
    }

    /// <summary>
    /// Marks the beginning of a mouse-down/drag brush stroke so continuous dragging creates a single undo snapshot.
    /// </summary>
    public void BeginPaintStroke()
    {
        strokeSnapshotRecorded = false;
    }

    /// <summary>
    /// Ends the current mouse-drag brush stroke.
    /// </summary>
    public void EndPaintStroke()
    {
        strokeSnapshotRecorded = false;
    }

    /// <summary>
    /// Applies <paramref name="brush"/> to cell (<paramref name="gx"/>, <paramref name="gy"/>) in <see cref="PreviewGrid"/>.
    /// Returns true if the preview grid changed (which automatically marks <see cref="IsValidationStale"/> true
    /// and locks <see cref="CanApply"/> until <see cref="Revalidate"/> is executed).
    /// </summary>
    public bool PaintCell(int gx, int gy, MazePaintBrush brush)
    {
        if (PreviewGrid == null || !PreviewGrid.InBounds(gx, gy))
            return false;

        bool isPerimeter = gx == 0 || gx == PreviewGrid.Width - 1 || gy == 0 || gy == PreviewGrid.Height - 1;
        if (LockOuterBorder && isPerimeter && brush != MazePaintBrush.Wall)
            return false;

        var coord = new Vector2Int(gx, gy);
        if (!WillBrushMutateCell(coord, brush))
            return false;

        if (!strokeSnapshotRecorded)
        {
            PushUndoSnapshot(new PreviewHistoryEntry(PreviewGrid.Clone(), HasManualEdits, Validation, Score));
            redoStack.Clear();
            strokeSnapshotRecorded = true;
        }

        bool changed = false;
        switch (brush)
        {
            case MazePaintBrush.Wall:
                changed = PreviewGrid.SetCell(gx, gy, MazeCell.Wall);
                break;

            case MazePaintBrush.Floor:
                changed = PreviewGrid.SetCell(gx, gy, MazeCell.Floor);
                break;

            case MazePaintBrush.Spawn:
                if (PreviewGrid.GetCell(gx, gy) != MazeCell.Floor)
                    changed |= PreviewGrid.SetCell(gx, gy, MazeCell.Floor);
                if (PreviewGrid.Exit == coord)
                    changed |= PreviewGrid.SetExit(null);
                changed |= PreviewGrid.SetSpawn(coord);
                break;

            case MazePaintBrush.Exit:
                if (PreviewGrid.GetCell(gx, gy) != MazeCell.Floor)
                    changed |= PreviewGrid.SetCell(gx, gy, MazeCell.Floor);
                if (PreviewGrid.Spawn == coord)
                    changed |= PreviewGrid.SetSpawn(null);
                changed |= PreviewGrid.SetExit(coord);
                break;
        }

        if (changed)
        {
            HasManualEdits = true;
            StatusBannerMessage = "Preview edited — validation is STALE. Click Validate before Apply.";
        }

        return changed;
    }

    /// <summary>
    /// Re-runs <see cref="MazeValidator.Validate"/> and <see cref="MazeScorer.Evaluate"/> on the current <see cref="PreviewGrid"/>.
    /// </summary>
    public MazeValidationReport Revalidate(MazeDifficultyProfile profile)
    {
        if (PreviewGrid == null)
        {
            Validation = null;
            Score = null;
            StatusBannerMessage = "No preview grid loaded.";
            return null;
        }

        bool requireExitOnStop = profile == null || profile.requireExitOnStopCell;
        Validation = MazeValidator.Validate(PreviewGrid, requireExitOnStopCell: requireExitOnStop);

        MazePlacementTelemetry activeTelemetry = HasManualEdits ? default : Telemetry;
        Score = profile != null
            ? MazeScorer.Evaluate(PreviewGrid, Validation, profile, activeTelemetry)
            : null;

        StatusBannerMessage = BuildStatusSummary();
        return Validation;
    }

    public bool UndoPreviewEdit(MazeDifficultyProfile activeProfile = null)
    {
        if (undoStack.Count == 0 || PreviewGrid == null)
            return false;

        EndPaintStroke();
        redoStack.Add(new PreviewHistoryEntry(PreviewGrid.Clone(), HasManualEdits, Validation, Score));
        int last = undoStack.Count - 1;
        PreviewHistoryEntry restored = undoStack[last];
        undoStack.RemoveAt(last);

        PreviewGrid = restored.Grid;
        HasManualEdits = restored.HadManualEdits;
        Validation = restored.Validation;
        Score = restored.Score;

        if (Validation != null && Validation.IsFreshFor(PreviewGrid) && activeProfile != null)
            Score = MazeScorer.Evaluate(PreviewGrid, Validation, activeProfile, HasManualEdits ? default : Telemetry);

        StatusBannerMessage = BuildStatusSummary();
        return true;
    }

    public bool RedoPreviewEdit(MazeDifficultyProfile activeProfile = null)
    {
        if (redoStack.Count == 0 || PreviewGrid == null)
            return false;

        EndPaintStroke();
        undoStack.Add(new PreviewHistoryEntry(PreviewGrid.Clone(), HasManualEdits, Validation, Score));
        int last = redoStack.Count - 1;
        PreviewHistoryEntry restored = redoStack[last];
        redoStack.RemoveAt(last);

        PreviewGrid = restored.Grid;
        HasManualEdits = restored.HadManualEdits;
        Validation = restored.Validation;
        Score = restored.Score;

        if (Validation != null && Validation.IsFreshFor(PreviewGrid) && activeProfile != null)
            Score = MazeScorer.Evaluate(PreviewGrid, Validation, activeProfile, HasManualEdits ? default : Telemetry);

        StatusBannerMessage = BuildStatusSummary();
        return true;
    }

    /// <summary>
    /// Creates an in-memory <see cref="MazeMapData"/> from the current validated preview (returns null if <see cref="CanApply"/> is false).
    /// </summary>
    public MazeMapData CreateValidatedMapData(string profileId, int libraryVersion)
    {
        if (!CanApply || Validation.SlideGraph == null)
            return null;

        var asset = ScriptableObject.CreateInstance<MazeMapData>();
        MazeSlideGraph g = Validation.SlideGraph;
        asset.PopulateFromGrid(
            grid: PreviewGrid,
            generationSeed: LastSeedUsed,
            sourceProfileId: profileId,
            preset: LastPresetUsed,
            libVersion: libraryVersion,
            score: Score != null ? Score.QualityScore : 0f,
            fit: Score != null ? Score.DifficultyFit : 0f,
            stops: g.SolutionStops,
            directions: g.SolutionDirections,
            decisionCount: g.DecisionStopsOnSolution,
            loopCount: g.StopGraphLoops);
        return asset;
    }

    /// <summary>
    /// Looks up the stamped 5x5 module owning cell (<paramref name="gx"/>, <paramref name="gy"/>)
    /// when slot placement metadata is present and the grid has not been manually edited.
    /// </summary>
    public bool TryGetPlacedModuleAtCell(int gx, int gy, out MazePlacedModule module)
    {
        module = default;
        if (PreviewGrid == null || HasManualEdits || PlacedModules == null || PlacedModules.Count == 0)
            return false;
        if (!PreviewGrid.InBounds(gx, gy))
            return false;

        int maxSlotX = Mathf.Max(0, (PreviewGrid.Width - 1) / MazePattern.Stride - 1);
        int maxSlotY = Mathf.Max(0, (PreviewGrid.Height - 1) / MazePattern.Stride - 1);
        int slotX = Mathf.Clamp(gx / MazePattern.Stride, 0, maxSlotX);
        int slotY = Mathf.Clamp(gy / MazePattern.Stride, 0, maxSlotY);

        for (int i = 0; i < PlacedModules.Count; i++)
        {
            if (PlacedModules[i].SlotX == slotX && PlacedModules[i].SlotY == slotY)
            {
                module = PlacedModules[i];
                return true;
            }
        }

        return false;
    }

    private bool WillBrushMutateCell(Vector2Int coord, MazePaintBrush brush) => brush switch
    {
        MazePaintBrush.Wall  => PreviewGrid.GetCell(coord) != MazeCell.Wall,
        MazePaintBrush.Floor => PreviewGrid.GetCell(coord) != MazeCell.Floor,
        MazePaintBrush.Spawn => PreviewGrid.Spawn != coord || PreviewGrid.GetCell(coord) != MazeCell.Floor || PreviewGrid.Exit == coord,
        MazePaintBrush.Exit  => PreviewGrid.Exit != coord || PreviewGrid.GetCell(coord) != MazeCell.Floor || PreviewGrid.Spawn == coord,
        _                    => false,
    };

    private void PushUndoSnapshot(PreviewHistoryEntry snapshot)
    {
        if (undoStack.Count >= MaxUndoSnapshots)
            undoStack.RemoveAt(0);
        undoStack.Add(snapshot);
    }

    private string BuildStatusSummary()
    {
        return Status switch
        {
            MazePreviewStatus.Empty   => "No preview generated yet.",
            MazePreviewStatus.Stale   => "Preview modified — validation is STALE (Apply locked until re-validated).",
            MazePreviewStatus.Invalid => Score != null && Score.HasConfigError
                ? $"INVALID CONFIG ({Score.ConfigError}) — Apply is locked."
                : $"INVALID ({Validation.ErrorCount} hard error(s)) — Apply is locked.",
            MazePreviewStatus.Valid   =>
                $"VALID — {Validation.SlideGraph.SolutionMoves} moves, 0 soft-locks"
                + (Score != null ? $", Quality {Score.QualityScore:F1}, Fit {Score.DifficultyFit:F1}" : string.Empty),
            _ => string.Empty,
        };
    }
}
