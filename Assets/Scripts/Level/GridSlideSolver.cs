using System;
using UnityEngine;

/// <summary>
/// Pure, deterministic discrete grid slide solver.
/// Computes the exact stopping cell and tiles traveled along a cardinal direction
/// without allocating or touching scene transforms — used by editor validation
/// and available for future outbreak creatures (Mimic / Stalker).
/// </summary>
public static class GridSlideSolver
{
    public readonly struct SlideResult
    {
        public readonly Vector2Int StopCell;
        public readonly int TilesTraveled;
        public readonly VelocityTier ImpactTier;
        public readonly bool ReachedStepCap;

        public SlideResult(Vector2Int stopCell, int tilesTraveled, bool reachedStepCap)
        {
            StopCell = stopCell;
            TilesTraveled = tilesTraveled;
            ImpactTier = VelocityTierUtil.FromTiles(tilesTraveled);
            ReachedStepCap = reachedStepCap;
        }
    }

    public static SlideResult SolveSlide(
        Vector2Int startCell,
        Vector2Int cardinalDirection,
        Func<Vector2Int, bool> isCellBlocked,
        int maxSteps = 64)
    {
        if (cardinalDirection == Vector2Int.zero || isCellBlocked == null || maxSteps <= 0)
            return new SlideResult(startCell, 0, false);

        Vector2Int dir = Mathf.Abs(cardinalDirection.x) >= Mathf.Abs(cardinalDirection.y)
            ? new Vector2Int(cardinalDirection.x > 0 ? 1 : -1, 0)
            : new Vector2Int(0, cardinalDirection.y > 0 ? 1 : -1);

        Vector2Int current = startCell;
        int steps = 0;

        for (int i = 0; i < maxSteps; i++)
        {
            Vector2Int next = current + dir;
            if (isCellBlocked(next))
                return new SlideResult(current, steps, false);

            current = next;
            steps++;
        }

        return new SlideResult(current, steps, true);
    }
}

