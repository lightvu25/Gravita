using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Manages the playback and pool-return lifecycle of a pooled animation instance
/// (Animator or legacy Animation component).
///
/// Features:
/// - Resets Animator/Animation state cleanly on spawn so reused instances always start at frame 0.
/// - Supports playing the default entry state or a configured/overridden Animator state name.
/// - Supports explicit Animation Event completion via <see cref="OnAnimationComplete"/>.
/// - Detects genuine animation completion without premature fallback truncation.
/// - Explicitly handles looping animations (never auto-recycles while looping unless stopped via <see cref="StopAndReturn"/>).
/// - Guarantees a single return to <see cref="ObjectPoolManager"/> per spawn cycle.
/// </summary>
public class PooledAnimationPlayer : MonoBehaviour
{
    [Header("Animation References")]
    [Tooltip("Animator driving this effect. Auto-resolved from this GameObject or children if left empty.")]
    [SerializeField] private Animator animator;

    [Tooltip("Optional legacy Animation component if no Animator is used.")]
    [SerializeField] private Animation legacyAnimation;

    [Header("Playback Settings")]
    [Tooltip("Automatically reset and play when the pooled object is enabled.")]
    [SerializeField] private bool playOnSpawn = true;

    [Tooltip("Optional Animator state name to play on spawn. Leave empty to play the default entry state.")]
    [SerializeField] private string defaultStateName;

    [Tooltip("Animator layer index to monitor.")]
    [SerializeField, Min(0)] private int animatorLayer = 0;

    [Tooltip("If true, treats the animation as looping and will not auto-return it to the pool until StopAndReturn() is called.")]
    [SerializeField] private bool isLooping = false;

    [Header("Completion & Fallback")]
    [Tooltip("If true, waits for an Animation Event to call OnAnimationComplete() before returning (with a safe non-premature fallback).")]
    [SerializeField] private bool waitForAnimationEvent = false;

    [Tooltip("Fallback duration (seconds) used when no Animator/Animation clip is present.")]
    [SerializeField, Min(0.05f)] private float fallbackDuration = 1f;

    [Tooltip("Additional safety buffer (seconds) added after clip length when waiting for a missing Animation Event (0 = no timeout).")]
    [SerializeField, Min(0f)] private float eventSafetyBuffer = 5f;

    /// <summary>Fired when a non-looping animation finishes and is about to return to the pool.</summary>
    public event Action<PooledAnimationPlayer> OnCompleted;

    private Action<PooledAnimationPlayer> oneShotCallback;
    private Coroutine monitorRoutine;
    private string activeStateName;
    private bool isReturned;
    private bool animationEventReceived;
    private bool initialized;
    private float defaultAnimatorSpeed = 1f;

    public Animator AnimatorComponent => animator;
    public bool IsLooping
    {
        get => isLooping || IsUnderlyingClipLooping();
        set => isLooping = value;
    }

    private void Awake()
    {
        EnsureComponents();
    }

    private void EnsureComponents()
    {
        if (initialized) return;
        initialized = true;

        if (animator == null)
            animator = GetComponentInChildren<Animator>(true);

        if (legacyAnimation == null && animator == null)
            legacyAnimation = GetComponentInChildren<Animation>(true);

        if (animator != null)
            defaultAnimatorSpeed = animator.speed > 0f ? animator.speed : 1f;
    }

    private void OnEnable()
    {
        isReturned = false;
        animationEventReceived = false;

        if (playOnSpawn)
        {
            Play(defaultStateName, null);
        }
    }

    private void OnDisable()
    {
        StopMonitorCoroutine();
        oneShotCallback = null;
        animationEventReceived = false;
        activeStateName = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Static Spawner Helper
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Spawns a pooled animation prefab via <see cref="ObjectPoolManager"/> and starts its <see cref="PooledAnimationPlayer"/>.
    /// </summary>
    public static PooledAnimationPlayer Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation = default,
        Transform parent = null,
        string stateName = null,
        Action<PooledAnimationPlayer> onComplete = null)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[PooledAnimationPlayer] Spawn called with a null prefab.");
            return null;
        }

        if (rotation == default)
            rotation = Quaternion.identity;

        GameObject instance = ObjectPoolManager.SpawnObject(
            prefab,
            position,
            rotation,
            parent,
            ObjectPoolManager.PoolType.GameObject);

        if (instance == null) return null;

        if (!instance.TryGetComponent(out PooledAnimationPlayer player))
        {
            player = instance.AddComponent<PooledAnimationPlayer>();
        }

        if (!string.IsNullOrEmpty(stateName) || onComplete != null || !player.playOnSpawn)
        {
            player.Play(stateName, onComplete);
        }

        return player;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Control API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets the animation to frame 0 and begins playback.
    /// </summary>
    public void Play(string stateOverride = null, Action<PooledAnimationPlayer> onComplete = null)
    {
        EnsureComponents();
        isReturned = false;
        animationEventReceived = false;
        activeStateName = !string.IsNullOrEmpty(stateOverride) ? stateOverride : defaultStateName;

        if (onComplete != null)
            oneShotCallback = onComplete;

        ResetAndStartAnimation();

        StopMonitorCoroutine();
        if (isActiveAndEnabled)
        {
            monitorRoutine = StartCoroutine(MonitorLifecycleRoutine());
        }
    }

    /// <summary>
    /// Animation Event hook: wire an Animation Event at the end of a clip to `OnAnimationComplete`
    /// (or call from code) to signal completion and recycle the instance.
    /// </summary>
    public void OnAnimationComplete()
    {
        animationEventReceived = true;
        if (!isLooping)
        {
            CompleteAndReturn();
        }
    }

    /// <summary>Alias for <see cref="OnAnimationComplete"/>.</summary>
    public void NotifyAnimationComplete() => OnAnimationComplete();

    /// <summary>
    /// Configures the fallback duration (ensuring a safe minimum of 0.05s).
    /// </summary>
    public void ConfigureFallbackDuration(float seconds)
    {
        fallbackDuration = Mathf.Max(0.05f, seconds);
    }

    /// <summary>
    /// Explicitly stops playback (including looping animations) and returns this instance to <see cref="ObjectPoolManager"/>.
    /// </summary>
    public void StopAndReturn()
    {
        if (isReturned) return;
        isReturned = true;

        StopMonitorCoroutine();
        ResetAnimationStateForPool();

        Action<PooledAnimationPlayer> callback = oneShotCallback;
        oneShotCallback = null;
        callback?.Invoke(this);
        OnCompleted?.Invoke(this);

        ObjectPoolManager.ReturnObjectToPool(gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void ResetAndStartAnimation()
    {
        if (animator != null && animator.runtimeAnimatorController != null)
        {
            animator.speed = defaultAnimatorSpeed;
            animator.Rebind();
            animator.Update(0f);

            if (!string.IsNullOrEmpty(activeStateName))
            {
                int stateHash = Animator.StringToHash(activeStateName);
                if (animator.HasState(animatorLayer, stateHash))
                {
                    animator.Play(stateHash, animatorLayer, 0f);
                    animator.Update(0f);
                }
            }
        }
        else if (legacyAnimation != null)
        {
            legacyAnimation.Stop();
            legacyAnimation.Rewind();
            if (!string.IsNullOrEmpty(activeStateName) && legacyAnimation.GetClip(activeStateName) != null)
            {
                legacyAnimation.Play(activeStateName);
            }
            else if (legacyAnimation.clip != null)
            {
                legacyAnimation.Play();
            }
        }
    }

    private void ResetAnimationStateForPool()
    {
        if (animator != null && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled)
        {
            animator.Rebind();
            animator.Update(0f);
        }
        else if (legacyAnimation != null)
        {
            legacyAnimation.Stop();
        }
    }

    private IEnumerator MonitorLifecycleRoutine()
    {
        // Wait one frame so Animator state transitions and normalizedTime initialize accurately.
        yield return null;

        float elapsed = 0f;

        while (!isReturned && this != null && gameObject.activeInHierarchy)
        {
            if (animationEventReceived && !IsLooping)
            {
                break;
            }

            // Explicitly do not auto-recycle while the animation is configured or authored to loop.
            if (IsLooping)
            {
                yield return null;
                continue;
            }

            elapsed += Time.deltaTime;

            if (animator != null && animator.runtimeAnimatorController != null)
            {
                if (animator.IsInTransition(animatorLayer))
                {
                    yield return null;
                    continue;
                }

                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animatorLayer);

                if (stateInfo.loop)
                {
                    yield return null;
                    continue;
                }

                float clipDuration = stateInfo.length > 0f
                    ? stateInfo.length / Mathf.Max(0.0001f, Mathf.Abs(animator.speed * stateInfo.speed * stateInfo.speedMultiplier))
                    : fallbackDuration;

                if (!waitForAnimationEvent)
                {
                    // Genuine Animator state completion: normalizedTime >= 1f and not transitioning.
                    if (stateInfo.normalizedTime >= 1f)
                    {
                        break;
                    }
                }
                else if (eventSafetyBuffer > 0f)
                {
                    // When waiting for an Animation Event, never return prematurely before the full clip duration + safety buffer.
                    float safeTimeout = Mathf.Max(fallbackDuration, clipDuration) + eventSafetyBuffer;
                    if (elapsed >= safeTimeout)
                    {
                        break;
                    }
                }
            }
            else if (legacyAnimation != null && legacyAnimation.clip != null)
            {
                if (!legacyAnimation.isPlaying)
                {
                    break;
                }
            }
            else
            {
                // No valid Animator controller or legacy Animation clip: use configurable fallbackDuration.
                if (elapsed >= fallbackDuration)
                {
                    break;
                }
            }

            yield return null;
        }

        if (!isReturned && this != null && !IsLooping)
        {
            CompleteAndReturn();
        }
    }

    private bool IsUnderlyingClipLooping()
    {
        if (animator != null && animator.runtimeAnimatorController != null && animator.isActiveAndEnabled)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(animatorLayer);
            return stateInfo.loop;
        }

        if (legacyAnimation != null && legacyAnimation.clip != null)
        {
            return legacyAnimation.clip.isLooping ||
                   legacyAnimation.wrapMode == WrapMode.Loop ||
                   legacyAnimation.clip.wrapMode == WrapMode.Loop;
        }

        return false;
    }

    private void CompleteAndReturn()
    {
        if (isReturned) return;
        isReturned = true;

        StopMonitorCoroutine();
        ResetAnimationStateForPool();

        Action<PooledAnimationPlayer> callback = oneShotCallback;
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
