using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure, deterministic resolver for all <see cref="ICellInteraction"/> components occupying a single grid cell.
/// Ensures world-state switches always execute before movement tiles, the highest-priority movement outcome wins,
/// and passive collapse tiles still arm on entry regardless of physics trigger callback order.
/// </summary>
public static class CellResolver
{
    public static void SortByPriority(List<ICellInteraction> interactions)
    {
        if (interactions == null || interactions.Count <= 1) return;

        interactions.Sort((a, b) =>
        {
            if (ReferenceEquals(a, b)) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            int cmp = a.Priority.CompareTo(b.Priority);
            if (cmp != 0) return cmp;

            EntityId idA = a is Object objA && objA != null ? objA.GetEntityId() : EntityId.None;
            EntityId idB = b is Object objB && objB != null ? objB.GetEntityId() : EntityId.None;
            return idA.CompareTo(idB);
        });
    }

    public static CellOutcome ResolveSorted(
        IReadOnlyList<ICellInteraction> sortedInteractions,
        PlayerMovement player,
        Vector2 incomingDirection)
    {
        if (sortedInteractions == null || sortedInteractions.Count == 0)
            return CellOutcome.Continue();

        CellOutcome winningOutcome = CellOutcome.Continue();
        bool hasMovementOutcome = false;

        for (int i = 0; i < sortedInteractions.Count; i++)
        {
            ICellInteraction interaction = sortedInteractions[i];
            if (interaction == null) continue;

            if (interaction is Behaviour behaviour && !behaviour.isActiveAndEnabled)
                continue;

            // Once a higher-priority movement tile has claimed the outcome (e.g. GravityBlock > VectorPad),
            // skip lower-priority movement tiles, while still allowing passive state tiles (FailingTile >= 500) to arm.
            if (hasMovementOutcome && interaction.Priority < CellInteractionPriority.FailingTile)
                continue;

            CellOutcome outcome = interaction.ResolveAtCenter(player, incomingDirection);
            if (!hasMovementOutcome && outcome.Type != CellOutcomeType.Continue)
            {
                winningOutcome = outcome;
                hasMovementOutcome = true;
            }
        }

        return winningOutcome;
    }
}

