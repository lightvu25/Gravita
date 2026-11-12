using UnityEngine;

/// <summary>
/// Vector Launcher (Phase Launch): fires Subject G-0 from this tile directly to an
/// Inspector-editable <see cref="landingTarget"/> position — phasing cleanly through
/// walls, gates, floor tiles, and hazards during transit — then resumes sliding along
/// the post-landing cardinal direction at <see cref="VelocityTier.KineticRam"/> speed.
///
/// Also triggers if the player starts or comes to rest on this tile, and disarms until
/// the player exits the cell so it can never re-trigger in an infinite same-tile loop.
/// </summary>
public class VectorLauncher : MazeTile, ICellInteraction, ILevelClockListener
{
    [Header("Editable Landing Position")]
    [Tooltip("Drag this child/scene Transform in the Scene View to place the exact landing cell after launching.")]
    [SerializeField] private Transform landingTarget;

    [Tooltip("Fallback world offset from this launcher when Landing Target is unassigned.")]
    [SerializeField] private Vector2 fallbackLandingOffset = new Vector2(3f, 0f);

    [Tooltip("When enabled and a LevelRoot Grid exists, snaps the landing position to the nearest grid cell centre.")]
    [SerializeField] private bool snapLandingToGridCell = false;

    [Header("Post-Landing Slide")]
    [Tooltip("Automatically derive the post-landing cardinal slide direction from (Landing - Launcher).")]
    [SerializeField] private bool autoDirectionFromLanding = true;

    [Tooltip("Explicit post-landing slide direction used when Auto Direction From Landing is unchecked.")]
    [SerializeField] private CardinalDirection postLandingDirection = CardinalDirection.Right;

    [Tooltip("Minimum velocity tier granted on launch (default KineticRam = smashes CrackedBlocks on landing).")]
    [SerializeField] private VelocityTier launchTier = VelocityTier.KineticRam;

    [Tooltip("Speed multiplier applied during the Phase Launch transit.")]
    [SerializeField, Min(0.5f)] private float phaseSpeedMultiplier = 1.6f;

    [Header("Visuals (Optional)")]
    [SerializeField] private Transform directionalVisual;
    [SerializeField] private bool autoRotateVisual = true;

    public Transform LandingTarget => landingTarget;
    public int Priority => CellInteractionPriority.Launcher;

    private static readonly Collider2D[] wallProbeBuffer = new Collider2D[12];
    private LevelRoot levelRoot;
    private bool disarmedWhileOccupying;

    protected override void Awake()
    {
        base.Awake();
        levelRoot = GetComponentInParent<LevelRoot>();
        ApplyVisualRotation();
    }

    private void OnValidate()
    {
        phaseSpeedMultiplier = Mathf.Max(0.5f, phaseSpeedMultiplier);
        ApplyVisualRotation();
    }

    public Vector2 GetResolvedLandingPosition()
    {
        Vector2 raw = landingTarget != null
            ? (Vector2)landingTarget.position
            : CellCenter + fallbackLandingOffset;

        if (snapLandingToGridCell)
        {
            if (levelRoot == null)
                levelRoot = GetComponentInParent<LevelRoot>();

            Grid grid = levelRoot != null ? levelRoot.LayoutGrid : null;
            if (grid != null)
            {
                Vector3Int cell = grid.WorldToCell(raw);
                Vector3 snapped = grid.GetCellCenterWorld(cell);
                return new Vector2(snapped.x, snapped.y);
            }
        }

        return raw;
    }

    public Vector2 GetResolvedPostDirection()
    {
        return GetResolvedPostDirection(GetResolvedLandingPosition());
    }

    private Vector2 GetResolvedPostDirection(Vector2 landingPos)
    {
        if (autoDirectionFromLanding)
        {
            Vector2 delta = landingPos - CellCenter;
            if (delta.sqrMagnitude > 0.0001f)
                return CardinalDirectionUtil.SnapToCardinal(delta);
        }

        return CardinalDirectionUtil.ToVector2(postLandingDirection);
    }

    private void ApplyVisualRotation()
    {
        if (!autoRotateVisual || directionalVisual == null) return;
        Vector2 dir = GetResolvedPostDirection();
        CardinalDirection card = CardinalDirectionUtil.FromVector2(dir);
        directionalVisual.localRotation = Quaternion.Euler(0f, 0f, CardinalDirectionUtil.ToZAngleDegrees(card));
    }

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        if (disarmedWhileOccupying)
            return CellOutcome.Continue();

        Vector2 landingPos = GetResolvedLandingPosition();
        Vector2 postDir = GetResolvedPostDirection(landingPos);

        if (IsLandingBlockedByUnbreakableWall(landingPos, player))
        {
            Debug.LogWarning($"[VectorLauncher] '{name}': Landing position {landingPos} is inside an unbreakable wall — launch aborted safely.", this);
            disarmedWhileOccupying = true;
            RaisePlayerInteracted(InteractionKind.Hit, -incomingDirection);
            return CellOutcome.Stop(requireFreshInput: false);
        }

        disarmedWhileOccupying = true;
        RaisePlayerInteracted(InteractionKind.Launch, postDir);
        return CellOutcome.PhaseLaunch(postDir, landingPos, launchTier, phaseSpeedMultiplier);
    }

    public bool IsLandingBlockedByUnbreakableWall(Vector2 landingPos, PlayerMovement player = null)
    {
        int mask = player != null ? player.WallLayers.value : LayerMask.GetMask("Wall");
        if (mask == 0) return false;

        Vector2 probeSize = Vector2.one * (CellSize * 0.65f);
        var filter = new ContactFilter2D();
        filter.SetLayerMask(mask);
        filter.useTriggers = false;

        int count = Physics2D.OverlapBox(landingPos, probeSize, 0f, filter, wallProbeBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = wallProbeBuffer[i];
            if (col == null || !col.enabled || col.isTrigger) continue;
            if (player != null && col == player.BodyCollider) continue;

            // Allow landing on a CrackedBlock if the launch tier is high enough to smash it immediately.
            CrackedBlock cracked = col.GetComponentInParent<CrackedBlock>();
            if (cracked != null && launchTier >= VelocityTier.KineticRam)
                continue;

            return true;
        }
        return false;
    }

    protected override void OnPlayerExit(PlayerMovement player)
    {
        disarmedWhileOccupying = false;
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        if (!disarmedWhileOccupying) return;

        PlayerMovement player = PlayerMovement.Instance;
        if (player == null) return;

        float rearmDist = CellSize * 0.6f;
        if (((Vector2)player.transform.position - CellCenter).sqrMagnitude > rearmDist * rearmDist)
        {
            disarmedWhileOccupying = false;
        }
    }

    public void OnLevelClockReset()
    {
        disarmedWhileOccupying = false;
    }

    public override void ResetTile()
    {
        disarmedWhileOccupying = false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 start = transform.position;
        Vector2 landing2D = GetResolvedLandingPosition();
        Vector3 end = new Vector3(landing2D.x, landing2D.y, start.z);
        bool blocked = IsLandingBlockedByUnbreakableWall(landing2D, null);

        Gizmos.color = blocked ? new Color(1f, 0.25f, 0.25f, 0.9f) : new Color(0.55f, 0.95f, 1f, 0.9f);
        Gizmos.DrawLine(start, end);
        Gizmos.DrawWireCube(end, Vector3.one * (CellSize * 0.85f));

        Vector3 postDir = (Vector3)GetResolvedPostDirection(landing2D);
        Vector3 arrowTip = end + postDir * 0.45f;
        Vector3 perp = new Vector3(-postDir.y, postDir.x, 0f) * 0.15f;
        Gizmos.DrawLine(end, arrowTip);
        Gizmos.DrawLine(arrowTip, arrowTip - postDir * 0.18f + perp);
        Gizmos.DrawLine(arrowTip, arrowTip - postDir * 0.18f - perp);
    }
#endif
}

