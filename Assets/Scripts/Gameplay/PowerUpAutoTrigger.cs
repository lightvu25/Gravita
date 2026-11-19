using UnityEngine;

/// <summary>
/// Evaluates automatic trigger conditions for modular Auto/Hybrid Power Cores
/// (Gravity Lens, Kinetic Surge, Time Dilation) while the player is actively sliding (L11).
/// </summary>
public sealed class PowerUpAutoTrigger
{
    private const float ScanIntervalSeconds = 0.08f;
    private float scanTimer;

    public void ResetTimer() => scanTimer = 0f;

    public void Tick(PowerUpController controller, PlayerMovement player, LevelRoot root, float deltaTime)
    {
        if (controller == null || player == null || !player.gameObject.activeInHierarchy || player.InputLocked)
            return;

        scanTimer -= deltaTime;
        if (scanTimer > 0f)
            return;

        scanTimer = ScanIntervalSeconds;
        EvaluateNow(controller, player, root);
    }

    /// <summary>Evaluates all auto-trigger sensors immediately (also callable directly by unit tests).</summary>
    public bool EvaluateNow(PowerUpController controller, PlayerMovement player, LevelRoot root)
    {
        if (controller == null || player == null || !player.gameObject.activeInHierarchy || player.InputLocked)
            return false;

        bool triggeredAny = false;

        if (player.IsMoving && controller.CanAutoTriggerSkill(PowerUpType.Magnet))
        {
            if (HasCollectibleInRadius(controller, player, root))
                triggeredAny |= controller.TryAutoActivate(PowerUpType.Magnet);
        }

        if (player.IsMoving && controller.CanAutoTriggerSkill(PowerUpType.Breaker))
        {
            if (HasBreakableAhead(controller, player, root))
                triggeredAny |= controller.TryAutoActivate(PowerUpType.Breaker);
        }

        if (player.IsMoving && controller.CanAutoTriggerSkill(PowerUpType.Freeze))
        {
            if (HasActiveHazardNearby(player, root))
                triggeredAny |= controller.TryAutoActivate(PowerUpType.Freeze);
        }

        return triggeredAny;
    }

    private static bool HasCollectibleInRadius(PowerUpController controller, PlayerMovement player, LevelRoot root)
    {
        float radius = controller.GetMagnetAttractionRadius();
        float radiusSqr = radius * radius;
        Vector3 playerPos = player.transform.position;

        Collectible[] collectibles = root != null && root.Collectibles != null
            ? root.Collectibles
            : Object.FindObjectsByType<Collectible>();

        if (collectibles == null)
            return false;

        for (int i = 0; i < collectibles.Length; i++)
        {
            Collectible c = collectibles[i];
            if (c == null || (!c.IsCoin && !c.IsStar) || c.IsCollected || !c.gameObject.activeInHierarchy)
                continue;

            if ((c.transform.position - playerPos).sqrMagnitude <= radiusSqr)
                return true;
        }

        return false;
    }

    private static bool HasBreakableAhead(PowerUpController controller, PlayerMovement player, LevelRoot root)
    {
        Vector2 dir = player.LastMoveDirection;
        if (dir == Vector2.zero)
            return false;

        float cellSize = Mathf.Max(0.1f, player.CellSize);
        Vector2 origin = player.transform.position;
        float maxRange = cellSize * 2.25f;
        float corridorTolerance = cellSize * 0.45f;

        if (root != null && root.CrackedBlocks != null && player.CurrentTier < VelocityTier.KineticRam)
        {
            for (int i = 0; i < root.CrackedBlocks.Length; i++)
            {
                CrackedBlock block = root.CrackedBlocks[i];
                if (block == null || block.IsBroken || !block.gameObject.activeInHierarchy)
                    continue;

                if (IsPointAheadAlongDirection(origin, block.transform.position, dir, maxRange, corridorTolerance))
                    return true;
            }
        }

        Hazard[] hazards = root != null && root.Hazards != null
            ? root.Hazards
            : null;
        if (hazards != null)
        {
            for (int i = 0; i < hazards.Length; i++)
            {
                Hazard h = hazards[i];
                if (h == null || !h.CanBeBrokenByBreaker || !h.IsActiveHazard || !h.gameObject.activeInHierarchy)
                    continue;

                if (IsPointAheadAlongDirection(origin, h.transform.position, dir, maxRange, corridorTolerance))
                    return true;
            }
        }

        return false;
    }

    private static bool HasActiveHazardNearby(PlayerMovement player, LevelRoot root)
    {
        float cellSize = Mathf.Max(0.1f, player.CellSize);
        float dangerRange = cellSize * 2.4f;
        float dangerRangeSqr = dangerRange * dangerRange;
        Vector3 playerPos = player.transform.position;

        if (root != null && root.Hazards != null)
        {
            for (int i = 0; i < root.Hazards.Length; i++)
            {
                Hazard h = root.Hazards[i];
                if (h == null || !h.IsActiveHazard || !h.SupportsFreeze || !h.gameObject.activeInHierarchy)
                    continue;

                if ((h.transform.position - playerPos).sqrMagnitude <= dangerRangeSqr)
                    return true;
            }
        }

        if (root != null && root.MazeTiles != null)
        {
            for (int i = 0; i < root.MazeTiles.Length; i++)
            {
                if (root.MazeTiles[i] is FailingTile ft
                    && ft.gameObject.activeInHierarchy
                    && (ft.State == FailingTileState.Warning || ft.State == FailingTileState.Collapsing)
                    && (ft.transform.position - playerPos).sqrMagnitude <= dangerRangeSqr)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsPointAheadAlongDirection(
        Vector2 origin,
        Vector2 point,
        Vector2 dir,
        float maxForwardDist,
        float maxLateralDist)
    {
        Vector2 delta = point - origin;
        float forward = Vector2.Dot(delta, dir);
        if (forward <= 0.05f || forward > maxForwardDist)
            return false;

        Vector2 perp = new Vector2(-dir.y, dir.x);
        return Mathf.Abs(Vector2.Dot(delta, perp)) <= maxLateralDist;
    }
}
