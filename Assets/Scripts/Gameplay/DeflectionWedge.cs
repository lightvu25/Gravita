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
/// cardinal vector to an outgoing cardinal vector WITHOUT resetting
/// TilesTraveled or interrupting the dash state.
///
/// GDD mapping:
///   "/" (Slash):     incoming North → East, incoming West → South.
///   "\" (Backslash): incoming North → West, incoming East → South.
/// Any other incoming direction hits the blunt surface: the dash halts at
/// the tile boundary like a normal solid wall.
/// </summary>
public class DeflectionWedge : MazeTile
{
    [Tooltip("Diagonal orientation of the wedge face.")]
    [SerializeField] private WedgeFacing facing = WedgeFacing.Slash;

    /// <summary>
    /// Returns the outgoing direction for a given incoming direction,
    /// or Vector2.zero when the approach is blunt (acts as a wall).
    /// </summary>
    public Vector2 GetDeflection(Vector2 incoming)
    {
        if (facing == WedgeFacing.Slash)
        {
            if (incoming == Vector2.up)    return Vector2.right;
            if (incoming == Vector2.left)  return Vector2.down;
        }
        else
        {
            if (incoming == Vector2.up)    return Vector2.left;
            if (incoming == Vector2.right) return Vector2.down;
        }
        return Vector2.zero;
    }

    protected override void OnPlayerEnter(PlayerMovement player)
    {
        Vector2 incoming = player.CurrentDirection;
        Vector2 outgoing = GetDeflection(incoming);

        if (outgoing != Vector2.zero)
        {
            // Snap to the cell centre, then continue the dash along the new
            // axis — TilesTraveled keeps accumulating (no stop event).
            player.Redirect(outgoing, CellCenter);
        }
        else
        {
            // Blunt surface: stop flush at the tile boundary like a wall.
            float offset = CellSize * 0.5f + 0.001f;
            player.HaltAt(CellCenter - incoming * offset);
        }
    }
}
