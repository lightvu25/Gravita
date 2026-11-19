using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Kinetic "juice" for the player: directional coalesced camera trauma and chained
/// hit-stop scaled by velocity tier, coordinated safely with modal Time.timeScale ownership.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerImpactFeedback : MonoBehaviour
{
    public static bool IsHitStopActive { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        IsHitStopActive = false;
    }

    [Header("Camera Shake")]
    [Tooltip("Impulse force for a Tier 1 Drift wall stop.")]
    [SerializeField, Min(0f)] private float tier1Shake = 0.045f;
    [Tooltip("Impulse force for a Tier 2 Cruise wall stop.")]
    [SerializeField, Min(0f)] private float tier2Shake = 0.13f;
    [Tooltip("Impulse force for a Tier 3 Kinetic Ram impact.")]
    [SerializeField, Min(0f)] private float tier3Shake = 0.28f;
    [Tooltip("Impulse force on each non-phasing wedge/pad deflection.")]
    [SerializeField, Min(0f)] private float redirectShake = 0.08f;
    [Tooltip("Subtle downward settle impulse on BrakeTile / GravityBlock stops.")]
    [SerializeField, Min(0f)] private float brakeSettleShake = 0.035f;
    [Tooltip("Directional impulse when shattering a CrackedBlock.")]
    [SerializeField, Min(0f)] private float blockBreakShake = 0.22f;

    [Header("Directional Camera Kick")]
    [Tooltip("Subtle camera nudge distance (world units) when the player launches in a slide direction.")]
    [SerializeField, Min(0f)] private float launchKickDistance = 0.08f;
    [Tooltip("Directional kick distance for a Tier 1 Drift wall stop.")]
    [SerializeField, Min(0f)] private float tier1KickDistance = 0.14f;
    [Tooltip("Directional kick distance for a Tier 2 Cruise wall stop.")]
    [SerializeField, Min(0f)] private float tier2KickDistance = 0.24f;
    [Tooltip("Directional kick distance for a Tier 3 Kinetic Ram wall impact.")]
    [SerializeField, Min(0f)] private float tier3KickDistance = 0.36f;
    [Tooltip("Directional kick distance on each non-phasing redirect.")]
    [SerializeField, Min(0f)] private float redirectKickDistance = 0.14f;
    [Tooltip("Directional kick distance when shattering a CrackedBlock.")]
    [SerializeField, Min(0f)] private float blockBreakKickDistance = 0.28f;
    [Tooltip("Seconds for camera offset to reach peak kick distance.")]
    [SerializeField, Min(0.01f)] private float kickOutDuration = 0.055f;
    [Tooltip("Seconds for camera offset to smoothly settle back to center.")]
    [SerializeField, Min(0.02f)] private float kickReturnDuration = 0.18f;

    [Header("Hit-Stop (seconds, unscaled)")]
    [SerializeField, Min(0f)] private float tier1HitStop = 0f;
    [SerializeField, Min(0f)] private float tier2HitStop = 0f;
    [SerializeField, Min(0f)] private float tier3HitStop = 0f;
    [SerializeField, Min(0f)] private float blockBreakHitStop = 0f;

    [Tooltip("If true, wall impacts and CrackedBlock breaks briefly freeze Time.timeScale. Off by default for uninterrupted corridor flow.")]
    [SerializeField] private bool wallImpactHitStopEnabled = false;

    [Tooltip("Legacy Hit-Stop master toggle preserved for TriggerHitStop callers.")]
    [SerializeField] private bool hitStopEnabled = true;

    [Header("Camera Brain Reference (Optional)")]
    [Tooltip("Optional CinemachineBrain switched to LateUpdate + IgnoreTimeScale solely during hit-stop.")]
    [SerializeField] private CinemachineBrain cinemachineBrain;

    [Header("Effects")]
    [Tooltip("Pooled particle burst spawned at the wall contact point (WallImpactBurst prefab).")]
    [SerializeField] private GameObject impactBurstPrefab;

    [Tooltip("Optional pooled animation prefab spawned at the wall contact point.")]
    [SerializeField] private GameObject impactAnimationPrefab;

    [Tooltip("Optional pooled particle or animation effect spawned when smashing a CrackedBlock.")]
    [SerializeField] private GameObject blockBreakParticlePrefab;
    [SerializeField] private GameObject blockBreakAnimationPrefab;

    private PlayerMovement movement;
    private Coroutine hitStopRoutine;
    private bool hasActiveHitStop;
    private float hitStopEndRealtime;
    private float preHitStopTimeScale = 1f;
    private CinemachineBrain.UpdateMethods preHitStopBrainUpdateMethod;
    private bool preHitStopBrainIgnoreTimeScale;
    private int lastBlockBreakHitStopFrame = -1;

    public float ActiveHitStopEndRealtime => hasActiveHitStop ? hitStopEndRealtime : 0f;
    public bool WallImpactHitStopEnabled
    {
        get => wallImpactHitStopEnabled;
        set => wallImpactHitStopEnabled = value;
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        if (cinemachineBrain == null && Camera.main != null)
            cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();
    }

    private void Start()
    {
        if (!Application.isPlaying) return;

        VfxSpawner.Prewarm(impactBurstPrefab, 2);
        VfxSpawner.Prewarm(impactAnimationPrefab, 2);
        VfxSpawner.Prewarm(blockBreakParticlePrefab, 2);
        VfxSpawner.Prewarm(blockBreakAnimationPrefab, 2);
    }

    private void OnEnable()
    {
        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        if (movement != null)
        {
            movement.OnMoveStarted += HandleMoveStarted;
            movement.OnMoveStopped += HandleStopped;
            movement.OnRedirected += HandleRedirected;
            movement.OnWallImpact += HandleWallImpact;
        }

        MazeTile.OnAnyPlayerInteracted += HandleTileInteracted;
        CrackedBlock.OnAnyBroken += HandleBlockBroken;
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.OnMoveStarted -= HandleMoveStarted;
            movement.OnMoveStopped -= HandleStopped;
            movement.OnRedirected -= HandleRedirected;
            movement.OnWallImpact -= HandleWallImpact;
        }

        MazeTile.OnAnyPlayerInteracted -= HandleTileInteracted;
        CrackedBlock.OnAnyBroken -= HandleBlockBroken;
        RestoreHitStopState();
    }

    public void TriggerHitStop(float duration) => PulseHitStop(duration);

    public void CancelHitStop() => RestoreHitStopState();

    private void HandleMoveStarted(Vector2 direction)
    {
        if (launchKickDistance <= 0f || CinemachineCameraShake2D.Instance == null || direction == Vector2.zero)
            return;

        CinemachineCameraShake2D.Instance.KickCamera(
            direction,
            launchKickDistance,
            kickOutDuration * 0.9f,
            kickReturnDuration * 0.85f);
    }

    private void HandleStopped()
    {
        if (movement == null || !movement.WasLastStopWallImpact)
            return;

        VelocityTier tier = movement.LastImpactTier;
        float shake = tier switch
        {
            VelocityTier.KineticRam => tier3Shake,
            VelocityTier.Cruise     => tier2Shake,
            _                       => tier1Shake,
        };

        Vector2 dir = movement.LastMoveDirection != Vector2.zero ? movement.LastMoveDirection : Vector2.down;
        if (shake > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(dir, shake);

        float kickDist = tier switch
        {
            VelocityTier.KineticRam => tier3KickDistance,
            VelocityTier.Cruise     => tier2KickDistance,
            _                       => tier1KickDistance,
        };
        if (kickDist > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.KickCamera(dir, kickDist, kickOutDuration, kickReturnDuration);

        if (wallImpactHitStopEnabled)
        {
            float stop = tier switch
            {
                VelocityTier.KineticRam => tier3HitStop,
                VelocityTier.Cruise     => tier2HitStop,
                _                       => tier1HitStop,
            };
            if (stop > 0f)
                PulseHitStop(stop);
        }
    }

    private void HandleTileInteracted(MazeTile tile, InteractionContext context)
    {
        if (brakeSettleShake <= 0f || CinemachineCameraShake2D.Instance == null)
            return;

        if ((tile is BrakeTile || tile is GravityBlock) && context.Kind == InteractionKind.Brake)
            CinemachineCameraShake2D.Instance.ShakeCamera(Vector2.down, brakeSettleShake);
    }

    private void HandleRedirected(Vector2 newDirection)
    {
        if (movement != null && movement.IsPhasing)
            return;

        if (CinemachineCameraShake2D.Instance != null)
        {
            Vector2 dir = newDirection != Vector2.zero ? newDirection : Vector2.down;
            if (redirectShake > 0f)
                CinemachineCameraShake2D.Instance.ShakeCamera(dir, redirectShake);
            if (redirectKickDistance > 0f)
                CinemachineCameraShake2D.Instance.KickCamera(dir, redirectKickDistance, kickOutDuration, kickReturnDuration);
        }
    }

    /// <summary>
    /// Spawns wall impact effects using axis-aware rotation (+Z LookRotation for 3D particles,
    /// +X Atan2 for 2D sprite animators).
    /// </summary>
    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero) return;

        Vector3 contact = movement != null ? movement.LastImpactPoint : transform.position;

        if (impactBurstPrefab != null)
        {
            Quaternion burstRot = VfxRotationUtility.ResolveRotationForPrefab(impactBurstPrefab, true, normal);
            ParticleSpawner.SpawnEffect(impactBurstPrefab, contact, burstRot);
        }

        if (impactAnimationPrefab != null)
        {
            Quaternion animRot = VfxRotationUtility.ResolveRotationForPrefab(impactAnimationPrefab, true, normal);
            PrefabAnimationSpawner.SpawnEffect(impactAnimationPrefab, contact, animRot);
        }
    }

    private void HandleBlockBroken(CrackedBlock block)
    {
        if (block == null) return;

        Vector2 dir = block.LastBreakDirection != Vector2.zero
            ? block.LastBreakDirection
            : (movement != null && movement.LastMoveDirection != Vector2.zero
                ? movement.LastMoveDirection
                : Vector2.down);

        if (CinemachineCameraShake2D.Instance != null)
        {
            if (blockBreakShake > 0f)
                CinemachineCameraShake2D.Instance.ShakeCamera(dir, blockBreakShake);
            if (blockBreakKickDistance > 0f)
                CinemachineCameraShake2D.Instance.KickCamera(dir, blockBreakKickDistance, kickOutDuration, kickReturnDuration);
        }

        if (wallImpactHitStopEnabled && blockBreakHitStop > 0f)
        {
            int frame = Time.frameCount;
            if (lastBlockBreakHitStopFrame != frame)
            {
                lastBlockBreakHitStopFrame = frame;
                PulseHitStop(blockBreakHitStop);
            }
        }

        if (block.HasCustomBreakEffect) return;
        Vector3 pos = block.LastBreakWorldPosition != Vector3.zero
            ? block.LastBreakWorldPosition
            : block.transform.position;

        if (blockBreakParticlePrefab != null)
        {
            Quaternion particleRot = VfxRotationUtility.ResolveRotationForPrefab(blockBreakParticlePrefab, true, dir);
            ParticleSpawner.SpawnEffect(blockBreakParticlePrefab, pos, particleRot);
        }

        if (blockBreakAnimationPrefab != null)
        {
            Quaternion animRot = VfxRotationUtility.ResolveRotationForPrefab(blockBreakAnimationPrefab, true, dir);
            PrefabAnimationSpawner.SpawnEffect(blockBreakAnimationPrefab, pos, animRot);
        }
    }

    /// <summary>
    /// Freezes gameplay until <see cref="hitStopEndRealtime"/> while respecting open modals
    /// via <see cref="UIManager.HasTimeFreezingPopupOpen"/> and extending across chained impacts.
    /// </summary>
    private void PulseHitStop(float duration)
    {
        if (!hitStopEnabled || duration <= 0f) return;
        if (UIManager.Instance != null && UIManager.Instance.HasTimeFreezingPopupOpen) return;
        if (!hasActiveHitStop && Time.timeScale < 0.5f) return;

        float candidateEnd = Time.realtimeSinceStartup + duration;
        hitStopEndRealtime = hasActiveHitStop ? Mathf.Max(hitStopEndRealtime, candidateEnd) : candidateEnd;

        if (!hasActiveHitStop)
        {
            ActivateHitStopState();
            if (Application.isPlaying)
                hitStopRoutine = StartCoroutine(HitStopRoutine());
        }
    }

    private void ActivateHitStopState()
    {
        hasActiveHitStop = true;
        IsHitStopActive = true;
        preHitStopTimeScale = Time.timeScale;

        if (cinemachineBrain == null && Camera.main != null)
            cinemachineBrain = Camera.main.GetComponent<CinemachineBrain>();

        if (cinemachineBrain != null)
        {
            preHitStopBrainUpdateMethod = cinemachineBrain.UpdateMethod;
            preHitStopBrainIgnoreTimeScale = cinemachineBrain.IgnoreTimeScale;
            cinemachineBrain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
            cinemachineBrain.IgnoreTimeScale = true;
        }

        Time.timeScale = 0f;
    }

    private IEnumerator HitStopRoutine()
    {
        while (Time.realtimeSinceStartup < hitStopEndRealtime)
        {
            if (UIManager.Instance != null && UIManager.Instance.HasTimeFreezingPopupOpen)
                break;

            yield return null;
        }

        hitStopRoutine = null;
        RestoreHitStopState();
    }

    private void RestoreHitStopState()
    {
        if (!hasActiveHitStop) return;

        IsHitStopActive = false;
        hasActiveHitStop = false;
        hitStopEndRealtime = 0f;

        if (hitStopRoutine != null)
        {
            StopCoroutine(hitStopRoutine);
            hitStopRoutine = null;
        }

        if (cinemachineBrain != null)
        {
            cinemachineBrain.UpdateMethod = preHitStopBrainUpdateMethod;
            cinemachineBrain.IgnoreTimeScale = preHitStopBrainIgnoreTimeScale;
        }

        if (UIManager.Instance != null)
            UIManager.Instance.RefreshTimeScale();
        else
            Time.timeScale = preHitStopTimeScale;
    }
}
