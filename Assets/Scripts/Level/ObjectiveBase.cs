using System;

/// <summary>
/// Abstract base class for all runtime level objectives.
///
/// Subclass this to add new objective types (TimeLimit, NoDeaths, etc.).
/// Each objective is initialised with an ObjectiveDefinition (from LevelData)
/// and a LevelRoot reference (for finding gameplay elements in the layout).
/// </summary>
public abstract class ObjectiveBase
{
    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    /// <summary>Fired exactly once when the objective transitions to the completed state.</summary>
    public event Action OnCompleted;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private bool isComplete;

    public bool  IsComplete => isComplete;

    /// <summary>Normalised progress from 0 (start) to 1 (complete).</summary>
    public abstract float Progress { get; }

    // ─────────────────────────────────────────────────────────────
    //  Abstract API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Sets up subscriptions to layout objects. Called once after the layout is loaded.</summary>
    public abstract void Initialize(ObjectiveDefinition definition, LevelRoot layout);

    /// <summary>Cleans up subscriptions and resets progress.</summary>
    public abstract void Reset();

    // ─────────────────────────────────────────────────────────────
    //  Protected Helpers
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Call from a subclass when the completion condition is first met.
    /// Idempotent — fires OnCompleted only once.
    /// </summary>
    protected void MarkComplete()
    {
        if (isComplete) return;
        isComplete = true;
        OnCompleted?.Invoke();
    }

    /// <summary>Resets the completion flag. Called from Reset().</summary>
    protected void ClearComplete()
    {
        isComplete = false;
    }
}
