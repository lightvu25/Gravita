/// <summary>
/// Objective: reach the level exit.
/// Completes the first time the player slides into any LevelExit in the layout.
/// </summary>
public class ReachExitObjective : ObjectiveBase
{
    private LevelRoot layout;

    public override float Progress => IsComplete ? 1f : 0f;

    public override void Initialize(ObjectiveDefinition definition, LevelRoot layout)
    {
        this.layout = layout;
        if (layout != null)
            layout.OnExitReached += HandleExitReached;
    }

    public override void Reset()
    {
        ClearComplete();
    }

    private void HandleExitReached(LevelExit exit)
    {
        MarkComplete();
    }
}
