using System;
using UnityEngine;

/// <summary>
/// Owns two-stage grid-slide sprite orientation (in-flight directional rotation and
/// 180-degree wall-landing flip around the tile center) plus the 5-state player
/// Animator synchronization (<c>Idle</c>, <c>Movement</c>, <c>Start</c>, <c>End</c>, <c>Death</c>).
/// Coordinates strictly on the visual child Transform without affecting physics colliders.
/// </summary>
[DefaultExecutionOrder(-10)]
[DisallowMultipleComponent]
public class PlayerVisual : MonoBehaviour
{
    public static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    public static readonly int StartHash = Animator.StringToHash("Start");
    public static readonly int EndHash = Animator.StringToHash("End");
    public static readonly int DeathHash = Animator.StringToHash("Death");
    public static readonly int ResetHash = Animator.StringToHash("Reset");

    private static readonly int[] TriggerHashes =
    {
        StartHash,
        EndHash,
        DeathHash,
        ResetHash,
    };

    private const float AngleSnapEpsilon = 0.0001f;

    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;

    [Tooltip("The visual child Transform that gets rotated and pivot-compensated — NOT the Player physics root.")]
    [SerializeField] private Transform visualTransform;

    [SerializeField] private Animator targetAnimator;
    [SerializeField] private LevelFlowController flowController;

    private bool isInitialized;
    private bool isSubscribed;
    private bool hasActiveInFlightAngle;
    private float inFlightAngleDegrees;
    private int lastTriggerHash;
    private int lastTriggerFrame = -1;

    /// <summary>Original resting localPosition of <see cref="visualTransform"/> at 0 degrees.</summary>
    public Vector3 InitialBaseLocalPosition { get; private set; }

    /// <summary>Current logical base Z-angle in signed degrees within (-180, 180].</summary>
    public float BaseAngleDegrees { get; private set; }

    /// <summary>Current base localRotation matching <see cref="BaseAngleDegrees"/>.</summary>
    public Quaternion BaseRotation => Quaternion.Euler(0f, 0f, BaseAngleDegrees);

    /// <summary>Pivot-compensated base localPosition keeping sprite bounds centered on the cell.</summary>
    public Vector3 CurrentBaseLocalPosition => ComputeRotatedBaseLocalPosition(InitialBaseLocalPosition, BaseAngleDegrees);

    /// <summary>True while the movement animation state (<c>IsMoving</c>) is active.</summary>
    public bool IsMovingAnimationActive { get; private set; }

    /// <summary>Fired whenever the base orientation angle changes.</summary>
    public event Action OnBasePoseChanged;

    // ─────────────────────────────────────────────────────────────
    //  Pure Orientation & Pivot Math
    // ─────────────────────────────────────────────────────────────

    /// <summary>Normalizes any degree angle into the canonical range (-180, 180].</summary>
    public static float NormalizeSignedAngle(float degrees)
    {
        float wrapped = Mathf.Repeat(degrees + 180f, 360f) - 180f;
        if (wrapped <= -180f + AngleSnapEpsilon || wrapped >= 180f - AngleSnapEpsilon)
            return 180f;
        if (Mathf.Abs(wrapped) <= AngleSnapEpsilon)
            return 0f;
        return wrapped;
    }

    /// <summary>
    /// Stage 1 angle while sliding toward a wall (upright 0° sprite has head at +Y, feet at -Y):
    /// Up = 0°, Right = -90°, Down = 180°, Left = +90°.
    /// </summary>
    public static float ComputeInFlightAngle(Vector2 direction)
    {
        if (direction.sqrMagnitude <= AngleSnapEpsilon)
            return 0f;

        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
            return direction.x >= 0f ? -90f : 90f;

        return direction.y >= 0f ? 0f : 180f;
    }

    /// <summary>
    /// Stage 2 angle upon hard wall impact: flips the in-flight sprite 180 degrees opposite
    /// so the feet plant onto the hit wall and the head faces back into the room.
    /// </summary>
    public static float ComputeWallStopAngle(float inFlightAngleDegrees)
    {
        return NormalizeSignedAngle(inFlightAngleDegrees + 180f);
    }

    /// <summary>
    /// Rotates <paramref name="initialBaseLocalPos"/> around the cell origin (0, 0, 0) so
    /// sprites with non-center pivots (e.g. Bottom-Center (0, -0.5, 0)) remain centered on the cell.
    /// </summary>
    public static Vector3 ComputeRotatedBaseLocalPosition(Vector3 initialBaseLocalPos, float angleDegrees)
    {
        if (initialBaseLocalPos.sqrMagnitude <= AngleSnapEpsilon * AngleSnapEpsilon)
            return Vector3.zero;

        Vector3 rotated = Quaternion.Euler(0f, 0f, NormalizeSignedAngle(angleDegrees)) * initialBaseLocalPos;
        rotated.x = CleanEpsilon(rotated.x);
        rotated.y = CleanEpsilon(rotated.y);
        rotated.z = CleanEpsilon(rotated.z);
        return rotated;
    }

    private static float CleanEpsilon(float value)
    {
        return Mathf.Abs(value) < 0.0001f ? 0f : value;
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Reset()
    {
        ResolveReferences();
    }

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
        SubscribeEvents();
        SyncMovementStateFromController();
    }

    private void OnDisable()
    {
        UnsubscribeEvents();
    }

    /// <summary>
    /// Resolves required visual references and captures the initial upright pivot offset once.
    /// Safe to invoke directly in EditMode tests.
    /// </summary>
    public void EnsureInitialized()
    {
        ResolveReferences();

        if (!isInitialized && visualTransform != null)
        {
            InitialBaseLocalPosition = visualTransform.localPosition;
            BaseAngleDegrees = NormalizeSignedAngle(visualTransform.localEulerAngles.z);
            isInitialized = true;
        }
    }

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();

        if (visualTransform == null
            || visualTransform == transform
            || visualTransform.GetComponent<Collider2D>() != null)
        {
            SpriteRenderer sr = GetComponentInChildren<SpriteRenderer>();
            visualTransform = (sr != null && sr.transform != transform && sr.GetComponent<Collider2D>() == null)
                ? sr.transform
                : null;
        }

        if (targetAnimator == null)
        {
            targetAnimator = visualTransform != null
                ? visualTransform.GetComponent<Animator>()
                : null;
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>();
        }

        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
    }

    // ─────────────────────────────────────────────────────────────
    //  Event Subscriptions
    // ─────────────────────────────────────────────────────────────

    private void SubscribeEvents()
    {
        if (isSubscribed)
            return;

        if (playerMovement != null)
        {
            playerMovement.OnMoveStarted += HandleMoveStarted;
            playerMovement.OnRedirected += HandleRedirected;
            playerMovement.OnPhasingChanged += HandlePhasingChanged;
            playerMovement.OnMoveStopped += HandleMoveStopped;
        }

        if (flowController != null)
        {
            flowController.OnLevelStarted += PlayStartAnimation;
            flowController.OnLevelRestarted += ResetToIdle;
            flowController.OnPlayerRespawned += HandlePlayerRespawned;
            flowController.OnPlayerRevived += ResetToIdle;
            flowController.OnLevelFailed += HandleLevelFailed;
        }

        isSubscribed = true;
    }

    private void UnsubscribeEvents()
    {
        if (!isSubscribed)
            return;

        if (playerMovement != null)
        {
            playerMovement.OnMoveStarted -= HandleMoveStarted;
            playerMovement.OnRedirected -= HandleRedirected;
            playerMovement.OnPhasingChanged -= HandlePhasingChanged;
            playerMovement.OnMoveStopped -= HandleMoveStopped;
        }

        if (flowController != null)
        {
            flowController.OnLevelStarted -= PlayStartAnimation;
            flowController.OnLevelRestarted -= ResetToIdle;
            flowController.OnPlayerRespawned -= HandlePlayerRespawned;
            flowController.OnPlayerRevived -= ResetToIdle;
            flowController.OnLevelFailed -= HandleLevelFailed;
        }

        isSubscribed = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Movement & Lifecycle Handlers
    // ─────────────────────────────────────────────────────────────

    private void HandleMoveStarted(Vector2 direction)
    {
        ApplyInFlightOrientation(direction);
        SetMovingState(true);
    }

    private void HandleRedirected(Vector2 newDirection)
    {
        ApplyInFlightOrientation(newDirection);
        SetMovingState(true);
    }

    private void HandlePhasingChanged(bool isPhasing)
    {
        if (isPhasing)
        {
            Vector2 dir = playerMovement != null
                ? (playerMovement.CurrentDirection != Vector2.zero ? playerMovement.CurrentDirection : playerMovement.LastMoveDirection)
                : Vector2.zero;

            if (dir != Vector2.zero)
                ApplyInFlightOrientation(dir);

            SetMovingState(true);
            return;
        }

        if (playerMovement == null)
            SetMovingState(false);
    }

    private void HandleMoveStopped()
    {
        SetMovingState(false);

        if (playerMovement != null && playerMovement.WasLastStopWallImpact)
        {
            float sourceAngle = hasActiveInFlightAngle
                ? inFlightAngleDegrees
                : ComputeInFlightAngle(playerMovement.LastMoveDirection);
            hasActiveInFlightAngle = false;
            SetBaseAngle(ComputeWallStopAngle(sourceAngle), applyTransformImmediately: true);
            return;
        }

        hasActiveInFlightAngle = false;

        // Preserve current pose while frozen for Death/Goal sequences;
        // floor stops (BrakeTile, GravityBlock, Runner StartNode, Teleport) return upright.
        if (playerMovement == null || !playerMovement.InputLocked)
            SetBaseAngle(0f, applyTransformImmediately: true);
    }

    private void HandlePlayerRespawned()
    {
        hasActiveInFlightAngle = false;
        SetMovingState(false);
        SetBaseAngle(0f, applyTransformImmediately: true);
    }

    private void HandleLevelFailed(string _)
    {
        PlayDeathAnimation();
    }

    private void SyncMovementStateFromController()
    {
        if (playerMovement == null)
            return;

        if (playerMovement.IsMoving)
        {
            Vector2 dir = playerMovement.CurrentDirection != Vector2.zero
                ? playerMovement.CurrentDirection
                : playerMovement.LastMoveDirection;
            if (dir != Vector2.zero)
                ApplyInFlightOrientation(dir);
            SetMovingState(true);
        }
        else
        {
            SetMovingState(false);
        }
    }

    private void ApplyInFlightOrientation(Vector2 direction)
    {
        inFlightAngleDegrees = ComputeInFlightAngle(direction);
        hasActiveInFlightAngle = true;
        SetBaseAngle(inFlightAngleDegrees, applyTransformImmediately: true);
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Animation API
    // ─────────────────────────────────────────────────────────────

    /// <summary>Resets orientation upright (0°) and triggers the <c>Start</c> state.</summary>
    public void PlayStartAnimation()
    {
        EnsureInitialized();
        hasActiveInFlightAngle = false;
        SetMovingState(false);
        SetBaseAngle(0f, applyTransformImmediately: true);
        TrySetTrigger(StartHash);
    }

    /// <summary>Resets orientation upright (0°) and triggers the <c>End</c> goal state.</summary>
    public void PlayEndAnimation()
    {
        EnsureInitialized();
        hasActiveInFlightAngle = false;
        SetMovingState(false);
        SetBaseAngle(0f, applyTransformImmediately: true);
        TrySetTrigger(EndHash);
    }

    /// <summary>Halts movement animation and triggers the <c>Death</c> state.</summary>
    public void PlayDeathAnimation()
    {
        EnsureInitialized();
        hasActiveInFlightAngle = false;
        SetMovingState(false);
        TrySetTrigger(DeathHash);
    }

    /// <summary>Restores upright 0° pose, clears queued triggers, and forces transition back to <c>Idle</c>.</summary>
    public void ResetToIdle()
    {
        EnsureInitialized();
        hasActiveInFlightAngle = false;
        SetMovingState(false);
        SetBaseAngle(0f, applyTransformImmediately: true);
        ResetAllTriggers();
        lastTriggerHash = 0;
        lastTriggerFrame = -1;
        TrySetTrigger(ResetHash);
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal Pose & Animator Helpers
    // ─────────────────────────────────────────────────────────────

    private void SetBaseAngle(float angleDegrees, bool applyTransformImmediately)
    {
        float normalized = NormalizeSignedAngle(angleDegrees);
        BaseAngleDegrees = normalized;

        if (applyTransformImmediately && visualTransform != null)
        {
            visualTransform.localRotation = BaseRotation;
            visualTransform.localPosition = CurrentBaseLocalPosition;
        }

        OnBasePoseChanged?.Invoke();
    }

    private void SetMovingState(bool isMoving)
    {
        IsMovingAnimationActive = isMoving;
        if (HasAnimatorParameter(IsMovingHash, AnimatorControllerParameterType.Bool))
            targetAnimator.SetBool(IsMovingHash, isMoving);
    }

    private void TrySetTrigger(int triggerHash)
    {
        int currentFrame = Time.frameCount;
        if (lastTriggerHash == triggerHash && lastTriggerFrame == currentFrame)
            return;

        if (!HasAnimatorParameter(triggerHash, AnimatorControllerParameterType.Trigger))
            return;

        lastTriggerHash = triggerHash;
        lastTriggerFrame = currentFrame;
        targetAnimator.SetTrigger(triggerHash);
    }

    private void ResetAllTriggers()
    {
        if (targetAnimator == null || !targetAnimator.isActiveAndEnabled || targetAnimator.runtimeAnimatorController == null)
            return;

        for (int i = 0; i < TriggerHashes.Length; i++)
        {
            if (HasAnimatorParameter(TriggerHashes[i], AnimatorControllerParameterType.Trigger))
                targetAnimator.ResetTrigger(TriggerHashes[i]);
        }
    }

    private bool HasAnimatorParameter(int nameHash, AnimatorControllerParameterType expectedType)
    {
        if (targetAnimator == null || !targetAnimator.isActiveAndEnabled || targetAnimator.runtimeAnimatorController == null)
            return false;

        AnimatorControllerParameter[] parameters = targetAnimator.parameters;
        if (parameters == null)
            return false;

        for (int i = 0; i < parameters.Length; i++)
        {
            AnimatorControllerParameter param = parameters[i];
            if (param.nameHash == nameHash && param.type == expectedType)
                return true;
        }

        return false;
    }
}
