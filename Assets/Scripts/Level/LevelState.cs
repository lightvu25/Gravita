/// <summary>
/// Represents the current state of a maze level.
/// </summary>
public enum LevelState
{
    /// <summary>No level is loaded.</summary>
    Idle      = 0,

    /// <summary>A level is being loaded/initialised.</summary>
    Loading   = 1,

    /// <summary>The level is active and accepting player input.</summary>
    Playing   = 2,

    /// <summary>The primary objective has been satisfied.</summary>
    Completed = 3,

    /// <summary>The level has been failed (hazard, etc.).</summary>
    Failed         = 4,

    /// <summary>The player died and the death cutscene / revive popup is active.</summary>
    AwaitingRevive = 5,
}
