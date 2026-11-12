using System;
using UnityEngine;

/// <summary>
/// Gravity Block: an intentional anomaly anchor tile that halts Subject G-0's
/// slide at the exact cell centre — even mid-corridor with no wall ahead — and
/// waits for a fresh directional input before resuming movement.
///
/// Rules:
///   - Stops sliding immediately on reaching the cell centre.
///   - Held directional keys/sticks are ignored until released and re-pressed.
///   - Triggers consistently on every entry without trapping or damaging the player.
/// </summary>
public class GravityBlock : MazeTile, ICellInteraction
{
    public event Action OnPlayerAnchored;

    public int Priority => CellInteractionPriority.GravityBlock;

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        RaisePlayerInteracted(InteractionKind.Brake, incomingDirection);
        OnPlayerAnchored?.Invoke();
        return CellOutcome.Stop(requireFreshInput: true);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.68f, 0.38f, 1f, 0.65f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * (CellSize * 0.82f));
    }
#endif
}

