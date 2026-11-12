using UnityEngine;

/// <summary>
/// Deterministic resolution priority constants when multiple mechanics share the same grid cell.
/// Lower value = resolved earlier.
/// Switches run first (world-state changes), followed by movement tiles (first non-Continue wins),
/// followed by passive state-only tiles (FailingTile arming).
/// </summary>
public static class CellInteractionPriority
{
    public const int Switch       = 100;
    public const int GravityBlock = 200;
    public const int Launcher     = 300;
    public const int VectorPad    = 400;
    public const int Wedge        = 450;
    public const int Brake        = 460;
    public const int FailingTile  = 500;
}

/// <summary>
/// Implemented by grid-cell mechanics whose entry/centre-crossing is resolved
/// deterministically by <see cref="PlayerMovement"/>.
/// </summary>
public interface ICellInteraction
{
    /// <summary>Deterministic priority within the same cell (lower runs first).</summary>
    int Priority { get; }

    /// <summary>World-space centre of the grid cell this interaction occupies.</summary>
    Vector2 InteractionCellCenter { get; }

    /// <summary>Grid cell width/height in world units (1 unit at 32 PPU).</summary>
    float InteractionCellSize { get; }

    /// <summary>
    /// True if approaching this cell from <paramref name="incomingDirection"/> hits a solid face
    /// at the cell boundary (e.g. blunt side of a <see cref="DeflectionWedge"/>).
    /// </summary>
    bool BlocksEntryFrom(Vector2 incomingDirection);

    /// <summary>
    /// Invoked once when the player reaches the cell centre.
    /// Returns a <see cref="CellOutcome"/> applied by <see cref="PlayerMovement"/>.
    /// </summary>
    CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection);
}

