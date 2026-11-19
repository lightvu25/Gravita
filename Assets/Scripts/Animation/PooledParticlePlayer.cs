using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages the playback and pool-return lifecycle of a pooled particle effect instance
/// (root and all child <see cref="ParticleSystem"/> components).
///
/// Features:
/// - Resets (Stop + Clear) and plays all root and child ParticleSystems cleanly on spawn.
/// - Checks actual particle system completion across the entire hierarchy (<see cref="ParticleSystem.IsAlive(bool)"/>
///   plus child <c>startDelay</c> awareness) rather than relying on root <c>main.duration</c>.
/// - Explicitly handles looping particle systems: never auto-recycles while any emitter is actively looping
///   unless <see cref="StopEmitting"/> or <see cref="StopAndReturn"/> is called (or an optional <see cref="loopMaxLifetime"/> is set).
/// - Guarantees a single return to <see cref="ObjectPoolManager"/> per spawn cycle.
/// </summary>
public class PooledParticlePlayer : MonoBehaviour
{
    [Header("Playback Settings")]
    [Tooltip("Automatically clear and play all particle systems when the pooled object is enabled.")]
    [SerializeField] private bool playOnSpawn = true;

    [Tooltip("If true, explicitly treats this effect as looping (will not auto-return until StopEmitting() or StopAndReturn() is called).")]
    [SerializeField] private bool isLooping = false;

    [Tooltip("Optional safety timeout (seconds) for looping particle systems. 0 = loop indefinitely until StopEmitting() or StopAndReturn() is called.")]
    [SerializeField, Min(0f)] private float loopMaxLifetime = 0f;

    [Tooltip("Fallback duration (seconds) if no ParticleSystem components exist on this hierarchy.")]
    [SerializeField, Min(0.05f)] private float emptyFallbackDuration = 0.5f;

    /// <summary>Fired when all particle systems have finished and the instance is about to return to the pool.</summary>
    public event Action<PooledParticlePlayer> OnCompleted;

    private ParticleSystem[] particleSystems;
    private Action<PooledParticlePlayer> oneShotCallback;
    private Coroutine monitorRoutine;
    private bool isReturned;
    private bool stopEmittingRequested;
    private float maxConfiguredStartDelay;

    public ParticleSystem PrimaryParticleSystem =>
        particleSystems != null && particleSystems.Length > 0 ? particleSystems[0] : null;

    public IReadOnlyList<ParticleSystem> ParticleSystems
    {
        get
        {
            EnsureParticleSystemsCached();
            return particleSystems;
        }
    }

    private void Awake()
    {
        EnsureParticleSystemsCached();
    }

    private void EnsureParticleSystemsCached()
    {
        if (particleSystems != null && particleSystems.Length > 0)
            return;

        RefreshParticleSystems();
    }

    /// <summary>Re-scans this GameObject and its children for <see cref="ParticleSystem"/> components.</summary>
    public void RefreshParticleSystems()
    {
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
        maxConfiguredStartDelay = 0f;

        if (particleSystems == null) return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null) continue;
            float delay = ps.main.startDelay.constantMax;
            if (delay > maxConfiguredStartDelay)
                maxConfiguredStartDelay = delay;
        }
    }

    private void OnEnable()
    {
        isReturned = false;
        stopEmittingRequested = false;

        if (playOnSpawn)
        {
            Play(null);
        }
    }

    private void OnDisable()
    {
        StopMonitorCoroutine();
        ClearAllParticles();
        oneShotCallback = null;
        stopEmittingRequested = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Static Spawner Helper
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a pooled particle prefab via <see cref="ObjectPoolManager"/> and starts its <see cref="PooledParticlePlayer"/>.
    /// </summary>
    public static PooledParticlePlayer Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation = default,
        Transform parent = null,
        Action<PooledParticlePlayer> onComplete = null)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[PooledParticlePlayer] Spawn called with a null prefab.");
            return null;
        }

        if (rotation == default)
            rotation = Quaternion.identity;

        GameObject instance = ObjectPoolManager.SpawnObject(
            prefab,
            position,
            rotation,
            parent,
            ObjectPoolManager.PoolType.ParticleSystem);

        if (instance == null) return null;

        if (!instance.TryGetComponent(out PooledParticlePlayer player))
        {
            player = instance.AddComponent<PooledParticlePlayer>();
        }

        if (onComplete != null || !player.playOnSpawn)
        {
            player.Play(onComplete);
        }

        return player;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Control API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Clears any residual particles from previous spawns and restarts all root and child particle systems.
    /// </summary>
    public void Play(Action<PooledParticlePlayer> onComplete = null)
    {
        EnsureParticleSystemsCached();
        isReturned = false;
        stopEmittingRequested = false;

        if (onComplete != null)
            oneShotCallback = onComplete;

        ResetAndPlayParticles();

        StopMonitorCoroutine();
        if (isActiveAndEnabled)
        {
            monitorRoutine = StartCoroutine(MonitorParticlesRoutine());
        }
    }

    /// <summary>
    /// Configures the optional safety timeout for looping particle systems (0 = loop indefinitely).
    /// </summary>
    public void ConfigureLoopMaxLifetime(float seconds)
    {
        loopMaxLifetime = Mathf.Max(0f, seconds);
    }

    /// <summary>
    /// Stops emission on all particle systems (including looping systems) while allowing
    /// already-spawned particles to finish their natural lifetime before returning to the pool.
    /// </summary>
    public void StopEmitting()
    {
        EnsureParticleSystemsCached();
        stopEmittingRequested = true;

        if (particleSystems == null) return;
        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps != null)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
            }
        }
    }

    /// <summary>
    /// Immediately stops and clears all particle systems and returns this instance to <see cref="ObjectPoolManager"/>.
    /// </summary>
    public void StopAndReturn()
    {
        if (isReturned) return;
        isReturned = true;

        StopMonitorCoroutine();
        ClearAllParticles();

        Action<PooledParticlePlayer> callback = oneShotCallback;
        oneShotCallback = null;
        callback?.Invoke(this);
        OnCompleted?.Invoke(this);

        ObjectPoolManager.ReturnObjectToPool(gameObject);
    }

    /// <summary>
    /// Returns true if this player or any of its active particle systems is configured to loop
    /// and <see cref="StopEmitting"/> has not been called.
    /// </summary>
    public bool IsActivelyLooping()
    {
        if (stopEmittingRequested) return false;
        if (isLooping) return true;

        EnsureParticleSystemsCached();
        if (particleSystems == null) return false;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps != null && ps.gameObject.activeInHierarchy && ps.main.loop)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns true if any root or child <see cref="ParticleSystem"/> currently has live particles or is still emitting.
    /// </summary>
    public bool AreAnyParticlesAlive()
    {
        EnsureParticleSystemsCached();
        if (particleSystems == null || particleSystems.Length == 0)
            return false;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps != null && ps.IsAlive(false))
            {
                return true;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void ResetAndPlayParticles()
    {
        if (particleSystems == null || particleSystems.Length == 0)
            return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null) continue;

            if (!ps.main.loop)
            {
                ParticleSystem.MainModule main = ps.main;
                main.useUnscaledTime = true;
            }

            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            ps.Clear(false);
            ps.time = 0f;
        }

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps == null || !ps.gameObject.activeInHierarchy) continue;

            ps.Play(false);
        }
    }

    private void ClearAllParticles()
    {
        if (particleSystems == null) return;

        for (int i = 0; i < particleSystems.Length; i++)
        {
            ParticleSystem ps = particleSystems[i];
            if (ps != null)
            {
                ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                ps.Clear(false);
            }
        }
    }

    private IEnumerator MonitorParticlesRoutine()
    {
        // Wait at least one frame so all emitters have processed Play().
        yield return null;

        float elapsed = 0f;

        if (particleSystems == null || particleSystems.Length == 0)
        {
            while (!isReturned && elapsed < emptyFallbackDuration)
            {
                elapsed += PlayerImpactFeedback.IsHitStopActive ? Time.unscaledDeltaTime : Time.deltaTime;
                yield return null;
            }

            if (!isReturned && this != null)
                CompleteAndReturn();
            yield break;
        }

        while (!isReturned && this != null && gameObject.activeInHierarchy)
        {
            elapsed += PlayerImpactFeedback.IsHitStopActive ? Time.unscaledDeltaTime : Time.deltaTime;

            // Handle looping particle systems explicitly: do not recycle while looping unless loopMaxLifetime expires.
            if (IsActivelyLooping())
            {
                if (loopMaxLifetime > 0f && elapsed >= loopMaxLifetime)
                {
                    StopEmitting();
                }
                else
                {
                    yield return null;
                    continue;
                }
            }

            // Ensure child systems with a configured startDelay have had a chance to begin emitting.
            if (elapsed < maxConfiguredStartDelay)
            {
                yield return null;
                continue;
            }

            if (!AreAnyParticlesAlive())
            {
                break;
            }

            yield return null;
        }

        if (!isReturned && this != null)
        {
            CompleteAndReturn();
        }
    }

    private void CompleteAndReturn()
    {
        if (isReturned) return;
        isReturned = true;

        StopMonitorCoroutine();
        ClearAllParticles();

        Action<PooledParticlePlayer> callback = oneShotCallback;
        oneShotCallback = null;
        callback?.Invoke(this);
        OnCompleted?.Invoke(this);

        ObjectPoolManager.ReturnObjectToPool(gameObject);
    }

    private void StopMonitorCoroutine()
    {
        if (monitorRoutine != null)
        {
            StopCoroutine(monitorRoutine);
            monitorRoutine = null;
        }
    }
}
