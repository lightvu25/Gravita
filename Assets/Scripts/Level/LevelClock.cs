/// <summary>
/// Implemented by level mechanics that advance deterministically from <see cref="LevelClock"/>
/// instead of using independent coroutines, Invoke, or uncancelled callbacks.
/// </summary>
public interface ILevelClockListener
{
    /// <summary>Called during FixedUpdate while the level is in <see cref="LevelState.Playing"/>.</summary>
    void OnLevelClockTick(float levelTime, float deltaTime);

    /// <summary>Called when the level starts or resets so all timers return to initial state.</summary>
    void OnLevelClockReset();
}

/// <summary>
/// Authoritative, deterministic level timer owned by <see cref="LevelRoot"/> and ticked
/// exclusively while <see cref="LevelFlowController.CurrentState"/> is <see cref="LevelState.Playing"/>.
/// Automatically halts during death, revive popups, completion, and scene teardown.
/// </summary>
public sealed class LevelClock
{
    public float Time { get; private set; }
    public int TickCount { get; private set; }

    public void Tick(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        Time += deltaTime;
        TickCount++;
    }

    public void Reset()
    {
        Time = 0f;
        TickCount = 0;
    }
}

