/// <summary>
/// Implemented by level elements whose powered/active state can be controlled
/// by a <see cref="CircuitSwitch"/> or a <see cref="BreachSequencer"/> event
/// (e.g. <see cref="EnergyGate"/>, <see cref="HazardActivation"/>).
/// </summary>
public interface ISwitchable
{
    /// <summary>Current logical powered state.</summary>
    bool IsPowered { get; }

    /// <summary>Sets the powered state explicitly.</summary>
    void SetPowered(bool powered);

    /// <summary>Toggles the current powered state.</summary>
    void TogglePowered();

    /// <summary>Restores the Inspector-configured initial state on level reset.</summary>
    void ResetSwitchState();
}

