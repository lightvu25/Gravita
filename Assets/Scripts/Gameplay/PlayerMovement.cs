using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Explicit state of the sole player movement owner (<see cref="PlayerMovement"/>).
/// </summary>
public enum MovementState
{
    Idle               = 0,
    Sliding            = 1,
    AwaitingFreshInput = 2,
    Phasing            = 3,
    Locked             = 4,
}

/// <summary>
/// Tomb-of-the-Mask / Gravita grid-aligned sliding controller: the sole owner of
/// player movement. Slides continuously in cardinal directions, resolves
/// <see cref="ICellInteraction"/> mechanics deterministically at cell centres,
/// tracks velocity tiers, enforces fresh-input stops (Gravity Block), and executes
/// Phase Launches across terrain.
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(Collider2D))]
public class PlayerMovement : MonoBehaviour
{
    public static PlayerMovement Instance { get; private set; }

    [Header("Movement")]
    [Tooltip("Slide speed in units per second.")]
    [SerializeField, Min(0.1f)] private float moveSpeed = 10f;

    [Tooltip("Layers treated as walls that stop the player.")]
    [SerializeField] private LayerMask wallLayers = ~0;

    [Tooltip("Free distance required before a queued (non-reverse) direction is taken. Should roughly match the corridor width minus the collider size.")]
    [SerializeField, Min(0.01f)] private float turnClearance = 0.35f;

    [Header("Input")]
    [Tooltip("If true, a held direction key/stick also starts movement when idle (ignored while AwaitingFreshInput on a Gravity Block).")]
    [SerializeField] private bool moveWithHeldDirection = true;

    [Tooltip("Strict stop-to-stop mode: perpendicular inputs queued mid-slide wait until the player hits a wall stop instead of turning mid-corridor. Reversal remains instant.")]
    [SerializeField] private bool bufferTurnsUntilStop = true;

    [Header("Grid / Velocity Tiers")]
    [Tooltip("Grid cell size in world units (32 px cell at 32 PPU = 1).")]
    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    [Tooltip("Anti-softlock: redirects without fresh input above this count force an emergency rest.")]
    [SerializeField, Min(1)] private int maxConsecutiveRedirects = 8;

    public MovementState State
    {
        get
        {
            if (inputLocked) return MovementState.Locked;
            if (isPhasing) return MovementState.Phasing;
            if (moveDirection != Vector2.zero) return MovementState.Sliding;
            if (awaitingFreshInput) return MovementState.AwaitingFreshInput;
            return MovementState.Idle;
        }
    }

    public bool IsMoving => isPhasing || moveDirection != Vector2.zero;
    public bool HasPendingDirection => pendingDirection != Vector2.zero;
    public bool IsPhasing => isPhasing;
    public bool IsAwaitingFreshInput => awaitingFreshInput;
    public Vector2 CurrentDirection => isPhasing ? phasePostDirection : moveDirection;
    /// <summary>Most recently established cardinal movement direction (persists after stopping against a wall).</summary>
    public Vector2 LastMoveDirection => CurrentDirection != Vector2.zero ? CurrentDirection : lastMoveDirection;
    public bool InputLocked => inputLocked;
    public bool BufferTurnsUntilStop
    {
        get => bufferTurnsUntilStop;
        set => bufferTurnsUntilStop = value;
    }
    public float CellSize => cellSize;
    public LayerMask WallLayers => wallLayers;
    public Collider2D BodyCollider => bodyCollider;

    /// <summary>Tiles crossed since the last stop. Wedges and Vector Pads do NOT reset this.</summary>
    public int TilesTraveled => tilesTraveled;

    /// <summary>Current velocity tier derived from TilesTraveled (Drift/Cruise/KineticRam).</summary>
    public VelocityTier CurrentTier => VelocityTierUtil.FromTiles(tilesTraveled);

    /// <summary>Tier held at the moment the last stop occurred (survives the reset).</summary>
    public VelocityTier LastImpactTier { get; private set; } = VelocityTier.None;

    /// <summary>True if the most recent stop was against a solid wall/blunt face rather than a floor stop tile.</summary>
    public bool WasLastStopWallImpact { get; private set; }

    /// <summary>Fired when the player starts sliding. Argument is the slide direction.</summary>
    public event Action<Vector2> OnMoveStarted;
    /// <summary>Fired when the player stops against a wall, Gravity Block, BrakeTile, or is frozen.</summary>
    public event Action OnMoveStopped;
    /// <summary>Fired each time the dash crosses a grid-cell boundary. Argument is the new TilesTraveled.</summary>
    public event Action<int> OnTileCrossed;
    /// <summary>Fired whenever <see cref="CurrentTier"/> changes (including reset to None on stop).</summary>
    public event Action<VelocityTier> OnVelocityTierChanged;
    /// <summary>Fired when Phase Launch begins (true) or ends (false).</summary>
    public event Action<bool> OnPhasingChanged;
    /// <summary>Fired when a deflection wedge or vector pad redirects the dash. Argument is the new direction.</summary>
    public event Action<Vector2> OnRedirected;
    /// <summary>Fired on a hard stop against a solid surface. Argument is the impact normal.</summary>
    public event Action<Vector2> OnWallImpact;

    /// <summary>World-space contact point of the most recent wall impact — valid during OnWallImpact and until the next dash.</summary>
    public Vector2 LastImpactPoint { get; private set; }

    /// <summary>Most recent resting cell position before the current dash began (used for revive respawn).</summary>
    public Vector3 LastSafePosition { get; private set; }

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private ContactFilter2D wallFilter;
    private bool isInitialized;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private readonly RaycastHit2D[] probeHits = new RaycastHit2D[8];
    private readonly Collider2D[] overlapResults = new Collider2D[16];
    private readonly Collider2D[] interactionColliderBuffer = new Collider2D[32];
    private readonly List<ICellInteraction> cellInteractionsBuffer = new List<ICellInteraction>(8);

    private Vector2 moveDirection;
    private Vector2 pendingDirection;
    private Vector2 lastMoveDirection;
    private bool inputLocked;
    private bool awaitingFreshInput;

    // Phase Launch runtime state
    private bool isPhasing;
    private Vector2 phaseTargetPosition;
    private Vector2 phasePostDirection;
    private VelocityTier phaseMinTier = VelocityTier.KineticRam;
    private float phaseSpeedMultiplier = 1.6f;

    // Velocity-tier tracking: distance accumulated during an uninterrupted dash.
    private float dashDistance;
    private int tilesTraveled;
    private VelocityTier lastNotifiedTier = VelocityTier.None;
    private int consecutiveRedirects;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        EnsureInitialized();
        LastSafePosition = transform.position;
    }

    /// <summary>
    /// Ensures physics references and wall contact filter are initialized (used at Awake and in EditMode tests).
    /// </summary>
    public void EnsureInitialized()
    {
        if (rb == null)
        {
            rb = GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.gravityScale = 0f;
                rb.freezeRotation = true;
                rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            }
        }

        if (bodyCollider == null)
            bodyCollider = GetComponent<Collider2D>();

        wallFilter.SetLayerMask(wallLayers);
        wallFilter.useTriggers = false;
        isInitialized = rb != null && bodyCollider != null;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        if (GameInput.Instance != null)
            GameInput.Instance.OnDirectionPressed += HandleDirectionPressed;
    }

    private void OnDisable()
    {
        if (GameInput.Instance != null)
            GameInput.Instance.OnDirectionPressed -= HandleDirectionPressed;
    }

    private void OnValidate()
    {
        moveSpeed = Mathf.Max(0.1f, moveSpeed);
        turnClearance = Mathf.Max(0.01f, turnClearance);
        if (isInitialized)
        {
            wallFilter.SetLayerMask(wallLayers);
            wallFilter.useTriggers = false;
        }
    }

    private void HandleDirectionPressed(Vector2 direction)
    {
        QueueDirection(direction);
    }

    /// <summary>
    /// Queues a fresh swipe/direction press. Clears any Gravity Block fresh-input lock.
    /// Reversal is applied instantly; perpendicular turns wait for a wall stop when
    /// <see cref="bufferTurnsUntilStop"/> is enabled.
    /// </summary>
    public void QueueDirection(Vector2 direction)
    {
        if (inputLocked || direction == Vector2.zero) return;

        awaitingFreshInput = false;

        // Normalize to a cardinal direction — the player only slides orthogonally.
        Vector2 cardinal = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? new Vector2(Mathf.Sign(direction.x), 0f)
            : new Vector2(0f, Mathf.Sign(direction.y));

        // Ignore redundant same-direction swipes while already sliding along that axis.
        if (moveDirection != Vector2.zero && cardinal == moveDirection)
            return;

        pendingDirection = cardinal;

        if (moveDirection == Vector2.zero && !isPhasing)
            lastMoveDirection = pendingDirection;
    }

    private void FixedUpdate()
    {
        SimulateStep(Time.fixedDeltaTime);
    }

    /// <summary>
    /// Advances one deterministic physics step of player movement. Invoked by <see cref="FixedUpdate"/>
    /// at runtime and directly by automated parity/regression tests.
    /// </summary>
    public void SimulateStep(float deltaTime)
    {
        if (!isInitialized || rb == null || bodyCollider == null)
            EnsureInitialized();
        if (inputLocked || rb == null || bodyCollider == null || deltaTime <= 0f) return;

        if (isPhasing)
        {
            StepPhaseLaunch(deltaTime);
            return;
        }

        // Push out if a solid collider materialized around the player while not phasing.
        DepenetrateFromWalls();

        bool wasStationary = moveDirection == Vector2.zero;

        // Consume a queued direction:
        // - When stationary: launch if free (or clear blocked buffer in strict stop-to-stop mode).
        // - When sliding: reversal is always instant; perpendicular turns wait for wall stop when bufferTurnsUntilStop is true.
        if (pendingDirection != Vector2.zero)
        {
            bool canConsumeNow = wasStationary
                ? IsDirectionFree(pendingDirection)
                : (pendingDirection == -moveDirection || (!bufferTurnsUntilStop && IsDirectionFree(pendingDirection)));

            if (canConsumeNow)
            {
                if (wasStationary)
                    LastSafePosition = new Vector3(rb.position.x, rb.position.y, transform.position.z);

                awaitingFreshInput = false;
                moveDirection = pendingDirection;
                lastMoveDirection = moveDirection;
                pendingDirection = Vector2.zero;
                consecutiveRedirects = 0;
                NotifyTierIfChanged();
                OnMoveStarted?.Invoke(moveDirection);
            }
            else if (wasStationary && bufferTurnsUntilStop)
            {
                // Buffered turn is blocked by a wall at this stop cell — discard so no ghost input persists.
                pendingDirection = Vector2.zero;
            }
        }

        if (moveDirection == Vector2.zero)
        {
            // Check if the player is resting on an armed VectorLauncher (auto-fires even from Idle/AwaitingFreshInput).
            if (TryTriggerRestingLauncher())
                return;

            // Held input starts movement ONLY when not locked by a Gravity Block / BrakeTile (awaitingFreshInput).
            if (moveWithHeldDirection && !awaitingFreshInput)
            {
                Vector2 held = DominantHeldDirection();
                if (held != Vector2.zero)
                {
                    lastMoveDirection = held;
                    if (IsDirectionFree(held))
                    {
                        LastSafePosition = new Vector3(rb.position.x, rb.position.y, transform.position.z);
                        moveDirection = held;
                        NotifyTierIfChanged();
                        OnMoveStarted?.Invoke(moveDirection);
                    }
                }
            }

            if (moveDirection == Vector2.zero) return;
        }

        float remainingStep = moveSpeed * deltaTime;
        AdvanceSlideStep(remainingStep);
    }

    private void AdvanceSlideStep(float stepBudget)
    {
        int safetySubSteps = 0;
        while (stepBudget > 0.0001f && !inputLocked && !isPhasing && moveDirection != Vector2.zero && safetySubSteps++ < 4)
        {
            Vector2 startPos = rb.position;
            float wallDistance = CastDistance(moveDirection, stepBudget, out RaycastHit2D stopHit);
            bool hasBluntBlock = FindNearestBlockingCellBoundary(startPos, moveDirection, stepBudget, out float bluntDistance, out ICellInteraction bluntInteraction);

            bool bluntWins = hasBluntBlock && (wallDistance < 0f || bluntDistance < wallDistance);
            float maxTravelBeforeObstacle = bluntWins
                ? Mathf.Max(0f, bluntDistance)
                : (wallDistance >= 0f ? Mathf.Max(0f, wallDistance - 0.001f) : stepBudget);

            // Check whether a cell centre with ICellInteraction(s) is crossed within maxTravelBeforeObstacle.
            if (TryFindNearestCellCenterAhead(startPos, moveDirection, maxTravelBeforeObstacle, out Vector2 targetCenter, out float centerDist))
            {
                MoveBodyImmediate(targetCenter);
                TrackDistance(centerDist);
                stepBudget -= centerDist;

                // 1. Lethal hazards take priority over switches & movement tiles at the cell centre.
                if (TriggerOverlappingLethalHazardAt(targetCenter))
                    return;

                // 2. Gather and deterministically sort all ICellInteractions at targetCenter.
                GatherCellInteractionsAt(targetCenter, cellInteractionsBuffer);
                CellResolver.SortByPriority(cellInteractionsBuffer);
                CellOutcome outcome = CellResolver.ResolveSorted(cellInteractionsBuffer, this, moveDirection);

                if (inputLocked) return;

                switch (outcome.Type)
                {
                    case CellOutcomeType.Continue:
                        // A switch may have opened/closed a gate ahead — sync transforms and continue remaining stepBudget!
                        Physics2D.SyncTransforms();
                        continue;

                    case CellOutcomeType.Stop:
                        HaltAt(targetCenter, outcome.RequireFreshInput);
                        return;

                    case CellOutcomeType.Redirect:
                        if (outcome.Direction == Vector2.zero || outcome.Direction == moveDirection)
                            continue;
                        Redirect(outcome.Direction, targetCenter);
                        return;

                    case CellOutcomeType.Launch:
                        if (outcome.IsPhaseLaunch)
                        {
                            StartPhaseLaunch(targetCenter, outcome.PhaseLandingPosition, outcome.Direction, outcome.MinTier, outcome.PhaseSpeedMultiplier);
                        }
                        else
                        {
                            EnforceMinimumTier(outcome.MinTier);
                            Redirect(outcome.Direction, targetCenter);
                        }
                        return;
                }
            }

            // No cell centre intervened before the obstacle or end of step.
            if (bluntWins)
            {
                Vector2 stopPos = startPos + moveDirection * Mathf.Max(0f, bluntDistance);
                MoveBodyImmediate(stopPos);
                TrackDistance(Mathf.Max(0f, bluntDistance));
                LastSafePosition = new Vector3(stopPos.x, stopPos.y, transform.position.z);
                LastImpactPoint = stopPos + moveDirection * (cellSize * 0.5f);
                if (bluntInteraction is DeflectionWedge wedge)
                    wedge.NotifyBlockedImpact(moveDirection);
                OnWallImpact?.Invoke(-moveDirection);
                StopMoving(wallImpact: true);
                return;
            }

            if (wallDistance >= 0f)
            {
                ICrushable crushable = stopHit.collider != null
                    ? stopHit.collider.GetComponentInParent<ICrushable>()
                    : null;

                if (crushable != null && crushable.TryCrush(this))
                {
                    Physics2D.SyncTransforms();
                    float crushAdvance = Mathf.Min(stepBudget, Mathf.Max(0.01f, wallDistance + 0.01f));
                    MoveBodyImmediate(startPos + moveDirection * crushAdvance);
                    TrackDistance(crushAdvance);
                    stepBudget -= crushAdvance;
                    continue;
                }

                if (wallDistance <= 0.001f && DepenetrateFromWalls())
                    return;

                Vector2 stopPosition = startPos + moveDirection * maxTravelBeforeObstacle;
                MoveBodyImmediate(stopPosition);
                TrackDistance(maxTravelBeforeObstacle);
                LastSafePosition = new Vector3(stopPosition.x, stopPosition.y, transform.position.z);
                LastImpactPoint = stopHit.point;
                OnWallImpact?.Invoke(stopHit.normal);
                StopMoving(wallImpact: true);
                return;
            }

            rb.MovePosition(startPos + moveDirection * stepBudget);
            TrackDistance(stepBudget);
            stepBudget = 0f;
        }
    }

    private void StepPhaseLaunch(float deltaTime)
    {
        Vector2 current = rb.position;
        float speed = moveSpeed * phaseSpeedMultiplier;
        float step = speed * deltaTime;
        Vector2 next = Vector2.MoveTowards(current, phaseTargetPosition, step);
        float moved = Vector2.Distance(current, next);

        rb.MovePosition(next);
        transform.position = new Vector3(next.x, next.y, transform.position.z);
        TrackDistance(moved);

        if ((next - phaseTargetPosition).sqrMagnitude <= 0.0001f)
        {
            MoveBodyImmediate(phaseTargetPosition);
            SetPhasing(false);
            EnforceMinimumTier(phaseMinTier);

            if (phasePostDirection != Vector2.zero)
            {
                moveDirection = phasePostDirection;
                lastMoveDirection = phasePostDirection;
                pendingDirection = Vector2.zero;
                OnMoveStarted?.Invoke(moveDirection);
            }
            else
            {
                LastSafePosition = new Vector3(phaseTargetPosition.x, phaseTargetPosition.y, transform.position.z);
                StopMoving(wallImpact: false);
            }
        }
    }

    /// <summary>
    /// Initiates a Phase Launch from <paramref name="origin"/> directly to <paramref name="landingPosition"/>,
    /// ignoring walls, gates, floor tiles, and hazards until arrival, then resuming cardinal sliding at <paramref name="minTier"/>.
    /// </summary>
    public void StartPhaseLaunch(
        Vector2 origin,
        Vector2 landingPosition,
        Vector2 postDirection,
        VelocityTier minTier = VelocityTier.KineticRam,
        float speedMultiplier = 1.6f)
    {
        if (inputLocked) return;

        consecutiveRedirects++;
        if (consecutiveRedirects > maxConsecutiveRedirects)
        {
            HaltAt(origin, requireFreshInput: false);
            return;
        }

        awaitingFreshInput = false;
        pendingDirection = Vector2.zero;
        moveDirection = Vector2.zero;

        MoveBodyImmediate(origin);
        EnforceMinimumTier(minTier);

        Vector2 resolvedDir = postDirection != Vector2.zero
            ? CardinalDirectionUtil.SnapToCardinal(postDirection)
            : CardinalDirectionUtil.SnapToCardinal(landingPosition - origin);

        phaseTargetPosition = landingPosition;
        phasePostDirection = resolvedDir;
        phaseMinTier = minTier;
        phaseSpeedMultiplier = Mathf.Max(0.5f, speedMultiplier);

        if ((landingPosition - origin).sqrMagnitude <= 0.0001f)
        {
            if (resolvedDir != Vector2.zero)
            {
                moveDirection = resolvedDir;
                lastMoveDirection = resolvedDir;
                OnRedirected?.Invoke(resolvedDir);
                OnMoveStarted?.Invoke(resolvedDir);
            }
            return;
        }

        lastMoveDirection = resolvedDir != Vector2.zero ? resolvedDir : lastMoveDirection;
        SetPhasing(true);
        OnRedirected?.Invoke(lastMoveDirection);
    }

    private void SetPhasing(bool value)
    {
        if (isPhasing == value) return;
        isPhasing = value;
        OnPhasingChanged?.Invoke(isPhasing);
    }

    public void EnforceMinimumTier(VelocityTier minTier)
    {
        int minTiles = VelocityTierUtil.MinTilesForTier(minTier);
        if (tilesTraveled < minTiles)
        {
            tilesTraveled = minTiles;
            dashDistance = Mathf.Max(dashDistance, minTiles * cellSize);
            OnTileCrossed?.Invoke(tilesTraveled);
            NotifyTierIfChanged();
        }
    }

    private void MoveBodyImmediate(Vector2 pos)
    {
        rb.position = pos;
        transform.position = new Vector3(pos.x, pos.y, transform.position.z);
    }

    private bool TryTriggerRestingLauncher()
    {
        Vector2 pos = rb.position;
        Vector2 probeSize = Vector2.one * (cellSize * 0.6f);
        int count = Physics2D.OverlapBox(pos, probeSize, 0f, ContactFilter2D.noFilter, interactionColliderBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = interactionColliderBuffer[i];
            if (col == null || col == bodyCollider) continue;

            ICellInteraction[] interactions = col.GetComponentsInParent<ICellInteraction>();
            for (int k = 0; k < interactions.Length; k++)
            {
                ICellInteraction interaction = interactions[k];
                if (interaction == null || interaction.Priority != CellInteractionPriority.Launcher)
                    continue;
                if (interaction is Behaviour b && !b.isActiveAndEnabled)
                    continue;
                if ((interaction.InteractionCellCenter - pos).sqrMagnitude > (cellSize * 0.45f) * (cellSize * 0.45f))
                    continue;

                CellOutcome outcome = interaction.ResolveAtCenter(this, Vector2.zero);
                if (outcome.Type == CellOutcomeType.Launch)
                {
                    if (outcome.IsPhaseLaunch)
                        StartPhaseLaunch(interaction.InteractionCellCenter, outcome.PhaseLandingPosition, outcome.Direction, outcome.MinTier, outcome.PhaseSpeedMultiplier);
                    else
                    {
                        EnforceMinimumTier(outcome.MinTier);
                        Redirect(outcome.Direction, interaction.InteractionCellCenter);
                    }
                    return true;
                }
            }
        }
        return false;
    }

    private bool FindNearestBlockingCellBoundary(
        Vector2 startPos,
        Vector2 dir,
        float stepDistance,
        out float nearestBoundaryDist,
        out ICellInteraction blockingInteraction)
    {
        nearestBoundaryDist = float.MaxValue;
        blockingInteraction = null;

        Vector2 boxCenter = startPos + dir * (stepDistance * 0.5f + cellSize * 0.5f);
        Vector2 boxSize = new Vector2(
            Mathf.Abs(dir.x) * (stepDistance + cellSize) + (1f - Mathf.Abs(dir.x)) * (cellSize * 0.8f),
            Mathf.Abs(dir.y) * (stepDistance + cellSize) + (1f - Mathf.Abs(dir.y)) * (cellSize * 0.8f));

        int count = Physics2D.OverlapBox(boxCenter, boxSize, 0f, ContactFilter2D.noFilter, interactionColliderBuffer);
        Vector2 perp = new Vector2(-dir.y, dir.x);
        float playerHalf = bodyCollider != null
            ? (Mathf.Abs(dir.x) > 0.5f ? bodyCollider.bounds.extents.x : bodyCollider.bounds.extents.y)
            : cellSize * 0.375f;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = interactionColliderBuffer[i];
            if (col == null || col == bodyCollider) continue;

            ICellInteraction interaction = col.GetComponentInParent<ICellInteraction>();
            if (interaction == null) continue;
            if (interaction is Behaviour b && !b.isActiveAndEnabled) continue;
            if (!interaction.BlocksEntryFrom(dir)) continue;

            Vector2 delta = interaction.InteractionCellCenter - startPos;
            float alongCenter = Vector2.Dot(delta, dir);
            float lateral = Mathf.Abs(Vector2.Dot(delta, perp));
            float halfTile = interaction.InteractionCellSize * 0.5f;

            if (lateral > halfTile * 0.9f) continue;
            if (alongCenter <= 0.01f) continue;

            // Stop flush with the cell center minus one full cell spacing (matching wall stops on grid centres).
            float stopCenterAlong = alongCenter - (halfTile + playerHalf + 0.001f);
            if (stopCenterAlong <= stepDistance + 0.001f && stopCenterAlong >= -0.05f && stopCenterAlong < nearestBoundaryDist)
            {
                nearestBoundaryDist = Mathf.Max(0f, stopCenterAlong);
                blockingInteraction = interaction;
            }
        }

        return blockingInteraction != null;
    }

    private bool TryFindNearestCellCenterAhead(
        Vector2 startPos,
        Vector2 dir,
        float maxTravel,
        out Vector2 nearestCenter,
        out float nearestAlong)
    {
        nearestCenter = Vector2.zero;
        nearestAlong = float.MaxValue;
        if (maxTravel <= 0.0005f) return false;

        Vector2 boxCenter = startPos + dir * (maxTravel * 0.5f);
        Vector2 boxSize = new Vector2(
            Mathf.Abs(dir.x) * (maxTravel + cellSize * 0.5f) + (1f - Mathf.Abs(dir.x)) * (cellSize * 0.8f),
            Mathf.Abs(dir.y) * (maxTravel + cellSize * 0.5f) + (1f - Mathf.Abs(dir.y)) * (cellSize * 0.8f));

        int count = Physics2D.OverlapBox(boxCenter, boxSize, 0f, ContactFilter2D.noFilter, interactionColliderBuffer);
        Vector2 perp = new Vector2(-dir.y, dir.x);
        bool found = false;

        for (int i = 0; i < count; i++)
        {
            Collider2D col = interactionColliderBuffer[i];
            if (col == null || col == bodyCollider) continue;

            ICellInteraction interaction = col.GetComponentInParent<ICellInteraction>();
            if (interaction == null) continue;
            if (interaction is Behaviour b && !b.isActiveAndEnabled) continue;
            if (interaction.BlocksEntryFrom(dir)) continue;

            Vector2 center = interaction.InteractionCellCenter;
            Vector2 delta = center - startPos;
            float along = Vector2.Dot(delta, dir);
            float lateral = Mathf.Abs(Vector2.Dot(delta, perp));

            if (lateral > interaction.InteractionCellSize * 0.45f) continue;
            if (along <= 0.0005f || along > maxTravel + 0.0005f) continue;

            if (along < nearestAlong)
            {
                nearestAlong = Mathf.Min(along, maxTravel);
                nearestCenter = center;
                found = true;
            }
        }

        return found;
    }

    private void GatherCellInteractionsAt(Vector2 cellCenter, List<ICellInteraction> results)
    {
        results.Clear();
        Vector2 probeSize = Vector2.one * (cellSize * 0.5f);
        int count = Physics2D.OverlapBox(cellCenter, probeSize, 0f, ContactFilter2D.noFilter, interactionColliderBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = interactionColliderBuffer[i];
            if (col == null || col == bodyCollider) continue;

            ICellInteraction[] list = col.GetComponentsInParent<ICellInteraction>();
            for (int k = 0; k < list.Length; k++)
            {
                ICellInteraction interaction = list[k];
                if (interaction == null) continue;
                if ((interaction.InteractionCellCenter - cellCenter).sqrMagnitude > 0.04f) continue;
                if (!results.Contains(interaction))
                    results.Add(interaction);
            }
        }
    }

    private bool TriggerOverlappingLethalHazardAt(Vector2 position)
    {
        if (isPhasing) return false;

        Vector2 probeSize = bodyCollider != null ? (Vector2)bodyCollider.bounds.size * 0.9f : Vector2.one * (cellSize * 0.7f);
        int count = Physics2D.OverlapBox(position, probeSize, 0f, ContactFilter2D.noFilter, overlapResults);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapResults[i];
            if (col == null || !col.enabled || col == bodyCollider) continue;
            Hazard hazard = col.GetComponentInParent<Hazard>();
            if (hazard != null && hazard.IsLethalToPlayer(this))
            {
                hazard.CheckOverlappingPlayer();
                if (inputLocked)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Accumulates dash distance and fires OnTileCrossed / OnVelocityTierChanged at each cell boundary.
    /// </summary>
    private void TrackDistance(float step)
    {
        if (step <= 0f) return;
        dashDistance += step;
        int tiles = Mathf.FloorToInt(dashDistance / cellSize);
        if (tiles <= tilesTraveled) return;

        tilesTraveled = tiles;
        OnTileCrossed?.Invoke(tilesTraveled);
        NotifyTierIfChanged();
    }

    private void NotifyTierIfChanged()
    {
        VelocityTier current = CurrentTier;
        if (current == lastNotifiedTier) return;
        lastNotifiedTier = current;
        OnVelocityTierChanged?.Invoke(current);
    }

    private void ResetDashTracking()
    {
        dashDistance = 0f;
        tilesTraveled = 0;
        NotifyTierIfChanged();
    }

    private void StopMoving(bool wallImpact = false)
    {
        bool keepBufferedTurn = wallImpact
            && bufferTurnsUntilStop
            && pendingDirection != Vector2.zero
            && pendingDirection != moveDirection;

        LastImpactTier = CurrentTier;
        WasLastStopWallImpact = wallImpact;
        moveDirection = Vector2.zero;
        if (!keepBufferedTurn)
            pendingDirection = Vector2.zero;
        SetPhasing(false);
        ResetDashTracking();
        OnMoveStopped?.Invoke();
    }

    /// <summary>
    /// Returns the remaining distance the collider can travel in the given
    /// direction before touching a wall, or -1 when the path is clear for the
    /// full distance. <paramref name="stopHit"/> receives the blocking hit.
    /// </summary>
    private float CastDistance(Vector2 direction, float distance, out RaycastHit2D stopHit)
    {
        int count = bodyCollider.Cast(direction, wallFilter, castHits, distance);
        float nearest = -1f;
        stopHit = default;
        bool skippedParallelHit = false;

        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (hit.collider == null || hit.collider == bodyCollider) continue;
            // Ignore parallel wall-grazing hits and T-junction corner skin bevels (normal > 60 deg off travel axis).
            // Because Collider2D.Cast returns at most 1 hit per CompositeCollider2D, any skipped grazing hit
            // triggers the laterally-inset forward cast below so real head-on walls ahead are never masked.
            if (Vector2.Dot(hit.normal, direction) >= -0.5f)
            {
                skippedParallelHit = true;
                continue;
            }
            if (nearest < 0f || hit.distance < nearest)
            {
                nearest = hit.distance;
                stopHit = hit;
            }
        }

        // Collider2D.Cast returns at most ONE hit per target Collider2D (such as a merged WallTilemap
        // CompositeCollider2D). When sliding flush along a parallel wall of that CompositeCollider2D,
        // the zero-distance grazing hit masks head-on walls ahead on the same CompositeCollider2D.
        // Perform a laterally-inset forward shape cast matching bodyCollider's type so head-on walls are never missed.
        if (skippedParallelHit)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector2 perpAbs = new Vector2(Mathf.Abs(direction.y), Mathf.Abs(direction.x));
            int fallbackCount;

            switch (bodyCollider)
            {
                case CircleCollider2D:
                    float radius = Mathf.Max(0.025f, Mathf.Min(bounds.extents.x, bounds.extents.y) - 0.02f);
                    fallbackCount = Physics2D.CircleCast(bounds.center, radius, direction, wallFilter, castHits, distance);
                    break;

                case CapsuleCollider2D capsule:
                    Vector2 capSize = (Vector2)bounds.size - perpAbs * 0.04f;
                    capSize.x = Mathf.Max(0.05f, capSize.x);
                    capSize.y = Mathf.Max(0.05f, capSize.y);
                    fallbackCount = Physics2D.CapsuleCast(bounds.center, capSize, capsule.direction,
                        transform.eulerAngles.z, direction, wallFilter, castHits, distance);
                    break;

                default:
                    Vector2 insetSize = (Vector2)bounds.size - perpAbs * 0.04f;
                    insetSize.x = Mathf.Max(0.05f, insetSize.x);
                    insetSize.y = Mathf.Max(0.05f, insetSize.y);
                    fallbackCount = Physics2D.BoxCast(bounds.center, insetSize, transform.eulerAngles.z,
                        direction, wallFilter, castHits, distance);
                    break;
            }

            for (int i = 0; i < fallbackCount; i++)
            {
                RaycastHit2D hit = castHits[i];
                if (hit.collider == null || hit.collider == bodyCollider) continue;
                if (Vector2.Dot(hit.normal, direction) >= -0.5f)
                    continue;
                if (nearest < 0f || hit.distance < nearest)
                {
                    nearest = hit.distance;
                    stopHit = hit;
                }
            }
        }

        return nearest;
    }

    /// <summary>
    /// If the body is already overlapped with a wall collider, pushes it out
    /// along the separation normal and returns true.
    /// </summary>
    private bool DepenetrateFromWalls()
    {
        if (isPhasing || bodyCollider == null) return false;

        int count = Physics2D.OverlapBox(bodyCollider.bounds.center, bodyCollider.bounds.size, 0f, wallFilter, overlapResults);
        bool moved = false;
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlapResults[i];
            if (other == null || other == bodyCollider) continue;

            ColliderDistance2D d = bodyCollider.Distance(other);
            if (!d.isOverlapped) continue;

            rb.MovePosition(rb.position + d.normal * d.distance);
            moved = true;
        }
        return moved;
    }

    /// <summary>True when the player can start moving in the given direction.</summary>
    private bool IsDirectionFree(Vector2 direction)
    {
        float clearance = ProbeClearance(direction);
        return clearance < 0f || clearance >= turnClearance;
    }

    private float ProbeClearance(Vector2 direction)
    {
        const float shrink = 0.9f;
        Bounds bounds = bodyCollider.bounds;
        Vector2 center = bounds.center;
        int count;

        switch (bodyCollider)
        {
            case CircleCollider2D:
                float radius = Mathf.Min(bounds.extents.x, bounds.extents.y) * shrink;
                count = Physics2D.CircleCast(center, radius, direction, wallFilter, probeHits, turnClearance);
                break;

            case CapsuleCollider2D capsule:
                count = Physics2D.CapsuleCast(center, bounds.size * shrink, capsule.direction,
                    transform.eulerAngles.z, direction, wallFilter, probeHits, turnClearance);
                break;

            default:
                count = Physics2D.BoxCast(center, bounds.size * shrink, transform.eulerAngles.z,
                    direction, wallFilter, probeHits, turnClearance);
                break;
        }

        float nearest = -1f;
        for (int i = 0; i < count; i++)
        {
            if (probeHits[i].collider == null || probeHits[i].collider == bodyCollider) continue;
            if (nearest < 0f || probeHits[i].distance < nearest)
                nearest = probeHits[i].distance;
        }
        return nearest;
    }

    private static Vector2 DominantHeldDirection()
    {
        if (GameInput.Instance == null) return Vector2.zero;
        Vector2 input = GameInput.Instance.MovementInput;
        if (input.sqrMagnitude < 0.25f) return Vector2.zero;
        return Mathf.Abs(input.x) >= Mathf.Abs(input.y)
            ? new Vector2(Mathf.Sign(input.x), 0f)
            : new Vector2(0f, Mathf.Sign(input.y));
    }

    // ------------------------------------------------------------------ //
    //  Public API                                                          //
    // ------------------------------------------------------------------ //

    /// <summary>Blocks input and halts the player immediately (level end, hazards).</summary>
    public void Freeze()
    {
        inputLocked = true;
        awaitingFreshInput = false;
        StopMoving(wallImpact: false);
    }

    /// <summary>Re-enables input.</summary>
    public void Unfreeze()
    {
        inputLocked = false;
    }

    /// <summary>Teleports the player to a new position and stops all motion.</summary>
    public void TeleportTo(Vector3 position)
    {
        consecutiveRedirects = 0;
        awaitingFreshInput = false;
        StopMoving(wallImpact: false);
        rb.position = position;
        transform.position = position;
        LastSafePosition = position;
    }

    /// <summary>Clears the cached movement direction history on level start/reset.</summary>
    public void ResetDirectionHistory()
    {
        lastMoveDirection = Vector2.zero;
        pendingDirection = Vector2.zero;
        awaitingFreshInput = false;
        consecutiveRedirects = 0;
        SetPhasing(false);
    }

    /// <summary>
    /// Re-aims the dash along a new cardinal direction without stopping —
    /// used by deflection wedges and vector pads. TilesTraveled keeps accumulating.
    /// <paramref name="snapPosition"/> re-centres the player on the tile cell.
    /// </summary>
    public void Redirect(Vector2 newDirection, Vector2 snapPosition)
    {
        if (inputLocked || newDirection == Vector2.zero) return;

        consecutiveRedirects++;
        if (consecutiveRedirects > maxConsecutiveRedirects)
        {
            HaltAt(snapPosition, requireFreshInput: true);
            return;
        }

        awaitingFreshInput = false;
        MoveBodyImmediate(snapPosition);
        moveDirection = CardinalDirectionUtil.SnapToCardinal(newDirection);
        lastMoveDirection = moveDirection;
        pendingDirection = Vector2.zero;
        OnRedirected?.Invoke(moveDirection);
        OnMoveStarted?.Invoke(moveDirection);
    }

    /// <summary>
    /// Dead-stop at an exact position — used by Gravity Blocks, BrakeTiles (tile centre),
    /// and wedge blunt surfaces (tile boundary).
    /// </summary>
    public void HaltAt(Vector2 position, bool requireFreshInput = false)
    {
        MoveBodyImmediate(position);
        LastSafePosition = new Vector3(position.x, position.y, transform.position.z);
        consecutiveRedirects = 0;
        awaitingFreshInput = requireFreshInput;
        StopMoving(wallImpact: false);
    }
}
