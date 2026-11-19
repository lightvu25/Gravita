using System;
using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Self-contained camera shake and directional kick helper. Owns a <see cref="CinemachineImpulseSource"/>
/// and optionally drives a <see cref="CinemachineCameraOffset"/> for directional recoil/nudges.
/// </summary>
[RequireComponent(typeof(CinemachineImpulseSource))]
public class CinemachineCameraShake2D : MonoBehaviour
{
    public static CinemachineCameraShake2D Instance { get; private set; }
    public static Func<int> FrameCountProvider = () => Time.frameCount;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticHooks()
    {
        Instance = null;
        FrameCountProvider = () => Time.frameCount;
    }

    [Tooltip("Default shake intensity used by ShakeCamera().")]
    [SerializeField] private float defaultShakeForce = 0.2f;

    [Header("Directional Camera Kick (Optional CinemachineCameraOffset)")]
    [Tooltip("Optional offset extension used to nudge/kick the camera along the slide/impact direction.")]
    [SerializeField] private CinemachineCameraOffset cameraOffset;

    [SerializeField, Min(0.05f)] private float maxKickDistance = 0.5f;

    private CinemachineImpulseSource impulseSource;
    private int lastImpulseFrame = -1;
    private float lastImpulseForceInFrame;

    private int lastKickFrame = -1;
    private float lastKickDistanceInFrame;
    private Sequence kickSequence;

    public Vector3 LastEmittedVelocity { get; private set; }
    public int EmittedImpulseCount { get; private set; }
    public float MaxForceInCurrentFrame => lastImpulseFrame == CurrentFrame ? lastImpulseForceInFrame : 0f;

    public Vector3 CurrentKickOffset { get; private set; }
    public Vector2 LastKickDirection { get; private set; }
    public float LastKickDistance { get; private set; }
    public int EmittedKickCount { get; private set; }

    private static int CurrentFrame => FrameCountProvider != null ? FrameCountProvider() : Time.frameCount;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        impulseSource = GetComponent<CinemachineImpulseSource>();
        CinemachineImpulseManager.Instance.IgnoreTimeScale = true;
        TryResolveCameraOffset();
    }

    private void OnDestroy()
    {
        KillKickTween(resetOffset: true);
        if (Instance == this)
            Instance = null;
    }

    public void ResetCoalescingState()
    {
        lastImpulseFrame = -1;
        lastImpulseForceInFrame = 0f;
        LastEmittedVelocity = Vector3.zero;
        EmittedImpulseCount = 0;

        lastKickFrame = -1;
        lastKickDistanceInFrame = 0f;
        LastKickDirection = Vector2.zero;
        LastKickDistance = 0f;
        EmittedKickCount = 0;
        KillKickTween(resetOffset: true);
    }

    /// <summary>
    /// Nudges or kicks the camera along <paramref name="direction"/> then smoothly returns to center.
    /// Safe to call with or without a CinemachineCameraOffset present (EditMode tests track offsets directly).
    /// </summary>
    public void KickCamera(
        Vector2 direction,
        float distance,
        float kickOutDuration = 0.055f,
        float returnDuration = 0.18f)
    {
        float clampedDistance = Mathf.Clamp(distance, 0f, maxKickDistance);
        if (clampedDistance <= 0f || direction.sqrMagnitude <= 0.0001f)
            return;

        int frame = CurrentFrame;
        if (lastKickFrame == frame)
        {
            if (clampedDistance <= lastKickDistanceInFrame)
                return;

            lastKickDistanceInFrame = clampedDistance;
        }
        else
        {
            lastKickFrame = frame;
            lastKickDistanceInFrame = clampedDistance;
        }

        Vector2 dir = direction.normalized;
        LastKickDirection = dir;
        LastKickDistance = clampedDistance;
        EmittedKickCount++;

        Vector3 targetOffset = new Vector3(dir.x, dir.y, 0f) * clampedDistance;
        TryResolveCameraOffset();

        KillKickTween(resetOffset: false);

        if (!Application.isPlaying)
        {
            ApplyKickOffset(targetOffset);
            return;
        }

        float outDur = Mathf.Max(0.01f, kickOutDuration);
        float inDur = Mathf.Max(0.02f, returnDuration);

        kickSequence = DOTween.Sequence()
            .Append(DOTween.To(() => CurrentKickOffset, ApplyKickOffset, targetOffset, outDur).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => CurrentKickOffset, ApplyKickOffset, Vector3.zero, inDur).SetEase(Ease.OutCubic))
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnKill(() => kickSequence = null);
    }

    private void KillKickTween(bool resetOffset)
    {
        if (kickSequence != null && kickSequence.IsActive())
            kickSequence.Kill();
        kickSequence = null;

        if (resetOffset)
            ApplyKickOffset(Vector3.zero);
    }

    private void ApplyKickOffset(Vector3 offset)
    {
        CurrentKickOffset = offset;
        if (cameraOffset != null)
            cameraOffset.Offset = offset;
    }

    private void TryResolveCameraOffset()
    {
        if (!Application.isPlaying || cameraOffset != null)
            return;

        CinemachineCamera cam = FindAnyObjectByType<CinemachineCamera>();
        if (cam == null)
            return;

        cameraOffset = cam.GetComponent<CinemachineCameraOffset>();
        if (cameraOffset == null)
            cameraOffset = cam.gameObject.AddComponent<CinemachineCameraOffset>();

        cameraOffset.ApplyAfter = CinemachineCore.Stage.Aim;
    }

    /// <summary>Shakes the camera with the configured default force.</summary>
    public void ShakeCamera() => ShakeCamera(Vector2.down, defaultShakeForce);

    /// <summary>Shakes the camera downward with the given impulse force.</summary>
    public void ShakeCamera(float force) => ShakeCamera(Vector2.down, force);

    /// <summary>
    /// Shakes the camera along <paramref name="direction"/> with per-frame delta-max coalescing
    /// so same-frame events never double-stack impulses beyond <c>max(forces)</c>.
    /// </summary>
    public void ShakeCamera(Vector2 direction, float force)
    {
        if (force <= 0f)
            return;

        if (impulseSource == null)
            impulseSource = GetComponent<CinemachineImpulseSource>();

        int frame = CurrentFrame;
        float emitForce = force;
        if (lastImpulseFrame == frame)
        {
            if (force <= lastImpulseForceInFrame)
                return;

            emitForce = force - lastImpulseForceInFrame;
            lastImpulseForceInFrame = force;
        }
        else
        {
            lastImpulseFrame = frame;
            lastImpulseForceInFrame = force;
        }

        Vector3 velocity = direction.sqrMagnitude > 0.001f
            ? new Vector3(direction.x, direction.y, 0f).normalized * emitForce
            : Vector3.down * emitForce;

        LastEmittedVelocity = velocity;
        EmittedImpulseCount++;
        impulseSource?.GenerateImpulseWithVelocity(velocity);
    }
}
