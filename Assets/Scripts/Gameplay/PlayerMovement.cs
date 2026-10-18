using System;
using UnityEngine;

/// <summary>
/// Tomb-of-the-Mask style movement: the player slides in a straight line
/// until a wall stops it. Input (arrow keys, WASD, or touch swipe) sets a
/// pending direction; the player turns as soon as that direction is free,
/// and can always reverse.
///
/// Uses a kinematic Rigidbody2D + Collider2D.Cast so no grid data structure
/// is required — walls just need colliders on the configured layer mask.
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
    [Tooltip("If true, a held direction key/stick also starts movement when idle (keyboard-friendly).")]
    [SerializeField] private bool moveWithHeldDirection = true;

    [Header("Grid / Velocity Tiers")]
    [Tooltip("Grid cell size in world units (32 px cell at 32 PPU = 1).")]
    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    [Tooltip("Anti-softlock: redirects without fresh input above this count force an emergency rest.")]
    [SerializeField, Min(1)] private int maxConsecutiveRedirects = 8;

    public bool IsMoving => moveDirection != Vector2.zero;
    public Vector2 CurrentDirection => moveDirection;
    public bool InputLocked => inputLocked;

    /// <summary>Tiles crossed since the last stop. Wedges do NOT reset this (dash continues).</summary>
    public int TilesTraveled => tilesTraveled;

    /// <summary>Current velocity tier derived from TilesTraveled (Drift/Cruise/KineticRam).</summary>
    public VelocityTier CurrentTier => VelocityTierUtil.FromTiles(tilesTraveled);

    /// <summary>Tier held at the moment the last stop occurred (survives the reset).</summary>
    public VelocityTier LastImpactTier { get; private set; } = VelocityTier.None;

    /// <summary>Fired when the player starts sliding. Argument is the slide direction.</summary>
    public event Action<Vector2> OnMoveStarted;
    /// <summary>Fired when the player stops against a wall, brake tile or is frozen.</summary>
    public event Action OnMoveStopped;
    /// <summary>Fired each time the dash crosses a grid-cell boundary. Argument is the new TilesTraveled.</summary>
    public event Action<int> OnTileCrossed;
    /// <summary>Fired when a deflection wedge redirects the dash. Argument is the new direction.</summary>
    public event Action<Vector2> OnRedirected;
    /// <summary>Fired on a hard stop against a solid surface. Argument is the impact normal.</summary>
    public event Action<Vector2> OnWallImpact;

    /// <summary>World-space contact point of the most recent wall impact — valid during OnWallImpact and until the next dash.</summary>
    public Vector2 LastImpactPoint { get; private set; }

    private Rigidbody2D rb;
    private Collider2D bodyCollider;
    private ContactFilter2D wallFilter;
    private readonly RaycastHit2D[] castHits = new RaycastHit2D[8];
    private readonly RaycastHit2D[] probeHits = new RaycastHit2D[8];
    private readonly Collider2D[] overlapResults = new Collider2D[8];

    private Vector2 moveDirection;
    private Vector2 pendingDirection;
    private bool inputLocked;

    // Velocity-tier tracking: distance accumulated during an uninterrupted dash.
    private float dashDistance;
    private int tilesTraveled;
    private int consecutiveRedirects;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        rb = GetComponent<Rigidbody2D>();
        bodyCollider = GetComponent<Collider2D>();

        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(wallLayers);
        wallFilter.useTriggers = false;
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
    }

    private void HandleDirectionPressed(Vector2 direction)
    {
        QueueDirection(direction);
    }

    /// <summary>
    /// Queues a swipe/direction. Reversal is applied instantly; any other
    /// direction is taken as soon as the path ahead is clear.
    /// </summary>
    public void QueueDirection(Vector2 direction)
    {
        if (inputLocked || direction == Vector2.zero) return;

        // Normalize to a cardinal direction — the player only slides orthogonally.
        pendingDirection = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y)
            ? new Vector2(Mathf.Sign(direction.x), 0f)
            : new Vector2(0f, Mathf.Sign(direction.y));
    }

    private void FixedUpdate()
    {
        if (inputLocked || rb == null) return;

        // If a wall collider materialized around us (async composite
        // generation, bad snap), push out — otherwise every direction probe
        // sees zero clearance and the player soft-locks forever.
        DepenetrateFromWalls();

        // Consume a queued direction: reversal is always allowed, any other
        // direction is taken as soon as enough clearance exists ahead.
        if (pendingDirection != Vector2.zero)
        {
            if (pendingDirection == -moveDirection || IsDirectionFree(pendingDirection))
            {
                moveDirection = pendingDirection;
                pendingDirection = Vector2.zero;
                consecutiveRedirects = 0; // fresh player intent clears the anti-softlock counter
                OnMoveStarted?.Invoke(moveDirection);
            }
        }

        if (moveDirection == Vector2.zero)
        {
            if (moveWithHeldDirection)
            {
                Vector2 held = DominantHeldDirection();
                if (held != Vector2.zero && IsDirectionFree(held))
                {
                    moveDirection = held;
                    OnMoveStarted?.Invoke(moveDirection);
                }
            }

            if (moveDirection == Vector2.zero) return;
        }

        float step = moveSpeed * Time.fixedDeltaTime;
        float wallDistance = CastDistance(moveDirection, step, out RaycastHit2D stopHit);

        if (wallDistance >= 0f)
        {
            // Cracked blocks (and future ICrushable obstacles) absorb a
            // high-tier dash instead of stopping it — the player keeps moving.
            ICrushable crushable = stopHit.collider != null
                ? stopHit.collider.GetComponentInParent<ICrushable>()
                : null;

            if (crushable != null && crushable.TryCrush(this))
            {
                rb.MovePosition(stopHit.point + moveDirection * 0.01f);
                return;
            }

            // A zero-distance hit means we're already inside a wall (e.g. a
            // composite collider's shape generated a few frames late, or a
            // bad teleport). Push out instead of freezing at d=0 forever.
            if (wallDistance <= 0.001f && DepenetrateFromWalls())
                return;

            // Wall reached within this step: travel up to it and stop.
            rb.MovePosition(rb.position + moveDirection * Mathf.Max(0f, wallDistance - 0.001f));
            LastImpactPoint = stopHit.point;
            OnWallImpact?.Invoke(stopHit.normal);
            StopMoving();
        }
        else
        {
            rb.MovePosition(rb.position + moveDirection * step);
            TrackDistance(step);
        }
    }

    /// <summary>
    /// Accumulates dash distance and fires OnTileCrossed at each cell
    /// boundary. Only called while actually moving.
    /// </summary>
    private void TrackDistance(float step)
    {
        dashDistance += step;
        int tiles = Mathf.FloorToInt(dashDistance / cellSize);
        if (tiles <= tilesTraveled) return;

        tilesTraveled = tiles;
        OnTileCrossed?.Invoke(tilesTraveled);
    }

    private void ResetDashTracking()
    {
        dashDistance = 0f;
        tilesTraveled = 0;
    }

    private void StopMoving()
    {
        LastImpactTier = CurrentTier;
        moveDirection = Vector2.zero;
        pendingDirection = Vector2.zero;
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
        for (int i = 0; i < count; i++)
        {
            RaycastHit2D hit = castHits[i];
            if (hit.collider == null || hit.collider == bodyCollider) continue;
            // A wall merely touching our side (distance ~0 with a normal that
            // does not oppose the motion) is not ahead of us — ignore it so
            // the player can slide parallel to a wall it already rests on.
            if (hit.distance <= 0.001f && Vector2.Dot(hit.normal, direction) >= -0.5f)
                continue;
            if (nearest < 0f || hit.distance < nearest)
            {
                nearest = hit.distance;
                stopHit = hit;
            }
        }
        return nearest;
    }

    /// <summary>
    /// If the body is already overlapped with a wall collider, pushes it out
    /// along the separation normal and returns true. Prevents a permanent
    /// soft-lock when a wall's collider materializes after the player has
    /// already entered its cells.
    /// </summary>
    private bool DepenetrateFromWalls()
    {
        int count = Physics2D.OverlapBox(bodyCollider.bounds.center, bodyCollider.bounds.size, 0f, wallFilter, overlapResults);
        bool moved = false;
        for (int i = 0; i < count; i++)
        {
            Collider2D other = overlapResults[i];
            if (other == null || other == bodyCollider) continue;

            ColliderDistance2D d = bodyCollider.Distance(other);
            if (!d.isOverlapped) continue;

            // distance is negative while overlapped; normal points from us
            // toward the wall, so normal * distance moves us away from it.
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

    /// <summary>
    /// Casts a slightly shrunken copy of the player's collider in the given
    /// direction and returns the distance to the nearest wall hit
    /// (-1 = clear). The shrink prevents lateral walls the player is already
    /// tangent to (e.g. after sliding into a corner) from blocking turns.
    /// </summary>
    private float ProbeClearance(Vector2 direction)
    {
        const float shrink = 0.9f;
        Bounds bounds = bodyCollider.bounds;
        Vector2 center = bounds.center;
        int count;

        // The contact filter carries the wall layer mask AND excludes trigger
        // colliders (collectibles, exits, hazards) so they never block turns.
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

            default: // BoxCollider2D and everything else approximated by the bounds box
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
        StopMoving();
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
        StopMoving();
        rb.position = position;
        transform.position = position;
    }

    /// <summary>
    /// Re-aims the dash along a new cardinal direction without stopping —
    /// used by deflection wedges. TilesTraveled keeps accumulating.
    /// <paramref name="snapPosition"/> re-centres the player on the wedge cell.
    /// </summary>
    public void Redirect(Vector2 newDirection, Vector2 snapPosition)
    {
        if (inputLocked || newDirection == Vector2.zero) return;

        // Anti-softlock: too many redirects without player input → emergency rest.
        consecutiveRedirects++;
        if (consecutiveRedirects > maxConsecutiveRedirects)
        {
            HaltAt(snapPosition);
            return;
        }

        rb.position = snapPosition;
        transform.position = snapPosition;
        moveDirection = newDirection;
        pendingDirection = Vector2.zero;
        OnRedirected?.Invoke(newDirection);
        OnMoveStarted?.Invoke(newDirection);
    }

    /// <summary>
    /// Dead-stop at an exact position — used by brake tiles (tile centre) and
    /// wedge blunt surfaces (tile boundary).
    /// </summary>
    public void HaltAt(Vector2 position)
    {
        rb.position = position;
        transform.position = position;
        consecutiveRedirects = 0;
        StopMoving();
    }
}
