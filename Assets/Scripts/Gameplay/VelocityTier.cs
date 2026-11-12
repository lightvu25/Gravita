using UnityEngine;

/// <summary>
/// Velocity tiers from the Kinetic Maze GDD ("The Runway System").
/// The tier is derived from the number of grid tiles travelled in one
/// uninterrupted dash:
///   1–2 tiles → Drift, 3–4 tiles → Cruise, 5+ tiles → Kinetic Ram.
/// </summary>
public enum VelocityTier
{
    /// <summary>Not moving.</summary>
    None       = 0,

    /// <summary>1–2 tiles travelled. Cannot break barriers; safely crosses brittle floors.</summary>
    Drift      = 1,

    /// <summary>3–4 tiles travelled. Standard interactions; activates heavy switches.</summary>
    Cruise     = 2,

    /// <summary>5+ tiles travelled. Shatters Cracked Blocks and brittle floors, crushes enemies.</summary>
    KineticRam = 3,
}

public static class VelocityTierUtil
{
    /// <summary>Maps a tile count to a velocity tier (0 tiles → None).</summary>
    public static VelocityTier FromTiles(int tilesTraveled)
    {
        if (tilesTraveled <= 0) return VelocityTier.None;
        if (tilesTraveled <= 2) return VelocityTier.Drift;
        if (tilesTraveled <= 4) return VelocityTier.Cruise;
        return VelocityTier.KineticRam;
    }

    /// <summary>Minimum tile count required to sustain the specified <paramref name="tier"/>.</summary>
    public static int MinTilesForTier(VelocityTier tier)
    {
        return tier switch
        {
            VelocityTier.Drift      => 1,
            VelocityTier.Cruise     => 3,
            VelocityTier.KineticRam => 5,
            _                       => 0,
        };
    }
}
