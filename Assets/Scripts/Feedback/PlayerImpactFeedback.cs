using System.Collections;
using UnityEngine;

/// <summary>
/// Kinetic "juice" for the player: camera trauma and hit-stop scaled by the
/// velocity tier at the moment of impact.
///
/// Event-driven — subscribes to PlayerMovement's OnMoveStopped / OnRedirected
/// and delegates camera shake to the existing CinemachineCameraShake2D
/// singleton. Hit-stop is implemented as a brief unscaled-time pulse of
/// Time.timeScale (restored afterwards, and skipped while the game is
/// already paused).
///
/// GDD hit-stop targets: Tier 1 stop = 0 frames, Tier 2 ≈ 2 frames,
/// Tier 3 ≈ 4–6 frames + stronger shake.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class PlayerImpactFeedback : MonoBehaviour
{
    [Header("Camera Shake")]
    [Tooltip("Impulse force for a Tier 1 wall stop.")]
    [SerializeField, Min(0f)] private float tier1Shake = 0.10f;
    [Tooltip("Impulse force for a Tier 2 wall stop.")]
    [SerializeField, Min(0f)] private float tier2Shake = 0.18f;
    [Tooltip("Impulse force for a Tier 3 Kinetic Ram impact.")]
    [SerializeField, Min(0f)] private float tier3Shake = 0.32f;
    [Tooltip("Small impulse on each wedge deflection.")]
    [SerializeField, Min(0f)] private float redirectShake = 0.06f;

    [Header("Hit-Stop (seconds, unscaled)")]
    [SerializeField, Min(0f)] private float tier1HitStop = 0f;      // 0 frames
    [SerializeField, Min(0f)] private float tier2HitStop = 0.033f;  // ~2 frames
    [SerializeField, Min(0f)] private float tier3HitStop = 0.083f;  // ~5 frames

    [Tooltip("If false, hit-stop is disabled (GDD 'Speedrun Minimal' setting).")]
    [SerializeField] private bool hitStopEnabled = true;

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
    private float preHitStopTimeScale = 1f;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void OnEnable()
    {
        if (movement == null) return;
        movement.OnMoveStopped += HandleStopped;
        movement.OnRedirected += HandleRedirected;
        movement.OnWallImpact += HandleWallImpact;
        CrackedBlock.OnAnyBroken += HandleBlockBroken;
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.OnMoveStopped -= HandleStopped;
            movement.OnRedirected -= HandleRedirected;
            movement.OnWallImpact -= HandleWallImpact;
        }
        CrackedBlock.OnAnyBroken -= HandleBlockBroken;
        if (hitStopRoutine != null) { StopCoroutine(hitStopRoutine); hitStopRoutine = null; }
    }

    private void HandleStopped()
    {
        // The tier is reset when the stop fires, so read the recorded impact tier.
        VelocityTier tier = movement.LastImpactTier;
        float shake = tier switch
        {
            VelocityTier.KineticRam => tier3Shake,
            VelocityTier.Cruise     => tier2Shake,
            _                       => tier1Shake,
        };

        if (shake > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(shake);

        float stop = tier switch
        {
            VelocityTier.KineticRam => tier3HitStop,
            VelocityTier.Cruise     => tier2HitStop,
            _                       => tier1HitStop,
        };
        if (stop > 0f) PulseHitStop(stop);
    }

    private void HandleRedirected(Vector2 _)
    {
        if (redirectShake > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(redirectShake);
    }

    /// <summary>
    /// Spawns the impact burst at the wall contact point, cone spraying back
    /// along the surface normal (away from the wall) via the reusable spawner API.
    /// </summary>
    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero) return;

        Vector3 contact = movement.LastImpactPoint;
        Quaternion rotation = Quaternion.LookRotation((Vector3)normal);

        if (impactBurstPrefab != null)
            ParticleSpawner.SpawnEffect(impactBurstPrefab, contact, rotation);

        if (impactAnimationPrefab != null)
            PrefabAnimationSpawner.SpawnEffect(impactAnimationPrefab, contact, rotation);
    }

    private void HandleBlockBroken(CrackedBlock block)
    {
        if (block == null || block.HasCustomBreakEffect) return;
        Vector3 pos = block.LastBreakWorldPosition != Vector3.zero
            ? block.LastBreakWorldPosition
            : block.transform.position;

        if (blockBreakParticlePrefab != null)
            ParticleSpawner.SpawnEffect(blockBreakParticlePrefab, pos, Quaternion.identity);

        if (blockBreakAnimationPrefab != null)
            PrefabAnimationSpawner.SpawnEffect(blockBreakAnimationPrefab, pos, Quaternion.identity);
    }

    /// <summary>
    /// Freezes gameplay for <paramref name="duration"/> real seconds by
    /// dropping Time.timeScale. Skipped while paused (a pause menu owns
    /// timeScale then) or when hit-stop is disabled in settings.
    /// </summary>
    private void PulseHitStop(float duration)
    {
        if (!hitStopEnabled || Time.timeScale < 0.5f) return;

        if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
        hitStopRoutine = StartCoroutine(HitStopRoutine(duration));
    }

    private IEnumerator HitStopRoutine(float duration)
    {
        preHitStopTimeScale = Time.timeScale;
        Time.timeScale = 0f;
        yield return new WaitForSecondsRealtime(duration);
        Time.timeScale = preHitStopTimeScale;
        hitStopRoutine = null;
    }
}
