/// <summary>
/// Interface for gameplay objects (enemies and traps) that can be temporarily
/// frozen by the Freeze power-up without altering global <c>Time.timeScale</c>.
/// </summary>
public interface IFreezable
{
    /// <summary>Whether this instance supports freezing.</summary>
    bool SupportsFreeze { get; }

    /// <summary>True while this object is currently frozen.</summary>
    bool IsFrozen { get; }

    /// <summary>Freezes active movement, attack, and trigger behavior while preserving previous state.</summary>
    void FreezeTarget();

    /// <summary>Restores the object's previous pre-freeze state.</summary>
    void UnfreezeTarget();
}
