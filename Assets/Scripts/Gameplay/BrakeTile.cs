using UnityEngine;

/// <summary>
/// Mud/Tar brake tile: absorbs all momentum and forces a dead stop at the
/// tile centre, requiring fresh directional input to move again.
/// Resolved deterministically at the cell centre by <see cref="PlayerMovement"/>.
/// </summary>
public class BrakeTile : MazeTile, ICellInteraction
{
    public virtual int Priority => CellInteractionPriority.Brake;

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public virtual CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        RaisePlayerInteracted(InteractionKind.Brake, incomingDirection);
        return CellOutcome.Stop(requireFreshInput: true);
    }
}
