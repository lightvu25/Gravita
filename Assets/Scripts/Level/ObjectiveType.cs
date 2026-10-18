/// <summary>
/// Identifies the type of a level objective.
/// Extend with new values as future maze mechanics are added.
/// </summary>
public enum ObjectiveType
{
    /// <summary>Slide into the level's exit trigger.</summary>
    ReachExit = 0,

    /// <summary>Collect a number of pickups (0 = all pickups in the layout).</summary>
    CollectPickups = 1,
}
