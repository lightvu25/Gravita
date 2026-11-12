/// <summary>
/// Optional companion contract attached alongside a <see cref="Hazard"/> component
/// to control timed cycles (Pulse Laser, Timed Overloaded Floor), switch-powered states,
/// or velocity-gated lethality (Skate Overloaded Floor).
/// </summary>
public interface IHazardActivation
{
    /// <summary>True when the hazard is currently armed/active in the world.</summary>
    bool IsHazardGenerallyActive { get; }

    /// <summary>Evaluates whether the active hazard is lethal to <paramref name="player"/> right now.</summary>
    bool IsLethalTo(PlayerMovement player);
}

