using UnityEngine;

/// <summary>
/// Result returned when an <see cref="ICellInteraction"/> resolves at a cell centre.
/// Applied exclusively by <see cref="PlayerMovement"/> so there is one sole movement owner.
/// </summary>
public enum CellOutcomeType
{
    /// <summary>No movement change (e.g. a switch or failing-tile arming).</summary>
    Continue = 0,

    /// <summary>Halts the player at the cell centre.</summary>
    Stop = 1,

    /// <summary>Redirects the active slide along a new cardinal direction while preserving momentum tier.</summary>
    Redirect = 2,

    /// <summary>Launches the player (optionally phasing through walls/hazards to an editable landing position).</summary>
    Launch = 3,
}

public readonly struct CellOutcome
{
    public readonly CellOutcomeType Type;
    public readonly Vector2 Direction;
    public readonly VelocityTier MinTier;
    public readonly bool RequireFreshInput;
    public readonly bool IsPhaseLaunch;
    public readonly Vector2 PhaseLandingPosition;
    public readonly float PhaseSpeedMultiplier;

    private CellOutcome(
        CellOutcomeType type,
        Vector2 direction,
        VelocityTier minTier,
        bool requireFreshInput,
        bool isPhaseLaunch,
        Vector2 phaseLandingPosition,
        float phaseSpeedMultiplier)
    {
        Type = type;
        Direction = direction;
        MinTier = minTier;
        RequireFreshInput = requireFreshInput;
        IsPhaseLaunch = isPhaseLaunch;
        PhaseLandingPosition = phaseLandingPosition;
        PhaseSpeedMultiplier = phaseSpeedMultiplier;
    }

    public static CellOutcome Continue() =>
        new CellOutcome(CellOutcomeType.Continue, Vector2.zero, VelocityTier.None, false, false, Vector2.zero, 1f);

    public static CellOutcome Stop(bool requireFreshInput) =>
        new CellOutcome(CellOutcomeType.Stop, Vector2.zero, VelocityTier.None, requireFreshInput, false, Vector2.zero, 1f);

    public static CellOutcome Redirect(Vector2 cardinalDirection) =>
        new CellOutcome(CellOutcomeType.Redirect, CardinalDirectionUtil.SnapToCardinal(cardinalDirection), VelocityTier.None, false, false, Vector2.zero, 1f);

    public static CellOutcome PhaseLaunch(
        Vector2 postLandingDirection,
        Vector2 landingPosition,
        VelocityTier minTier = VelocityTier.KineticRam,
        float speedMultiplier = 1.6f) =>
        new CellOutcome(
            CellOutcomeType.Launch,
            CardinalDirectionUtil.SnapToCardinal(postLandingDirection),
            minTier,
            false,
            true,
            landingPosition,
            Mathf.Max(0.5f, speedMultiplier));
}

