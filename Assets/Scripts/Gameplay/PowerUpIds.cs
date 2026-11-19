/// <summary>
/// Well-known power-up identifiers stored in the profile power-up inventory
/// (ProfileData.powerUpIds / powerUpCounts, queried via ProgressionManager)
/// and referenced by PowerUpData ScriptableObject assets.
/// </summary>
public static class PowerUpIds
{
    /// <summary>Legacy identifier for the kinetic shield item.</summary>
    public const string KineticShield = "kinetic_shield";

    public const string Shield   = "shield";
    public const string Magnet   = "magnet";
    public const string Breaker  = "breaker";
    public const string Echo     = "echo";
    public const string Teleport = "teleport";
    public const string Freeze   = "freeze";

    public static string FormatDisplayName(string powerUpId)
    {
        if (string.IsNullOrWhiteSpace(powerUpId))
            return "Core";

        switch (powerUpId.Trim().ToLowerInvariant())
        {
            case Breaker:  return "Kinetic Surge";
            case Freeze:   return "Time Dilation";
            case Teleport: return "Phase Shift";
            case Shield:   return "Inertia Core";
            case Magnet:   return "Gravity Lens";
            case Echo:     return "Temporal Anchor";
            default:       return powerUpId;
        }
    }
}
