using UnityEngine;

/// <summary>
/// Which diagonal the wedge splits along. Mirrors the GDD names:
///   Slash     = "/" — solid bottom-right face (top-right wedge).
///   Backslash = "\" — solid bottom-left face (top-left wedge).
/// </summary>
public enum WedgeFacing
{
    Slash,
    Backslash,
}

/// <summary>
/// Prism deflection wedge. Occupies a full grid cell and remaps the incoming
/// cardinal vector to an outgoing cardinal vector at the exact cell centre
/// WITHOUT resetting TilesTraveled or interrupting the dash state.
///
/// GDD mapping:
///   "/" (Slash):     incoming North → East, incoming West → South.
///   "\" (Backslash): incoming North → West, incoming East → South.
/// Any other incoming direction hits the blunt surface: the dash halts flush at
/// the tile boundary like a normal solid wall.
/// </summary>
public class DeflectionWedge : MazeTile, ICellInteraction
{
    [Tooltip("Diagonal orientation of the wedge face.")]
    [SerializeField] private WedgeFacing facing = WedgeFacing.Slash;

    public int Priority => CellInteractionPriority.Wedge;

    /// <summary>
    /// Returns the outgoing direction for a given incoming direction,
    /// or Vector2.zero when the approach is blunt (acts as a wall).
    /// </summary>
    public Vector2 GetDeflection(Vector2 incoming)
    {
        Vector2 dir = CardinalDirectionUtil.SnapToCardinal(incoming);
        if (facing == WedgeFacing.Slash)
        {
            if (dir == Vector2.up)    return Vector2.right;
            if (dir == Vector2.left)  return Vector2.down;
        }
        else
        {
            if (dir == Vector2.up)    return Vector2.left;
            if (dir == Vector2.right) return Vector2.down;
        }
        return Vector2.zero;
    }

    public bool BlocksEntryFrom(Vector2 incomingDirection) =>
        GetDeflection(incomingDirection) == Vector2.zero;

    public void NotifyBlockedImpact(Vector2 incomingDirection) =>
        RaisePlayerInteracted(InteractionKind.Hit, -incomingDirection);

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        Vector2 outgoing = GetDeflection(incomingDirection);
        if (outgoing != Vector2.zero)
        {
            RaisePlayerInteracted(InteractionKind.Deflect, outgoing);
            return CellOutcome.Redirect(outgoing);
        }

        RaisePlayerInteracted(InteractionKind.Hit, -incomingDirection);
        return CellOutcome.Stop(requireFreshInput: false);
    }
}
