using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one-shot animated effect prefabs — a SpriteRenderer driven by an
/// Animator (or a legacy Animation component) — and returns each instance to
/// the pool once its animation finishes.
///
/// Lifecycle is owned HERE, not on the prefab: the spawner polls each spawned
/// instance and calls ObjectPoolManager.ReturnObjectToPool (or Destroy when
/// usePooling is off). Prefab authors therefore don't need a ReturnToPool
/// component — if the prefab has one anyway, this spawner detects it and lets
/// it own the return instead.
/// </summary>
public class PrefabAnimationSpawner : MonoBehaviour
{
    public static PrefabAnimationSpawner Instance { get; private set; }

    [System.Serializable]
    private class EffectEntry
    {
        public string id;
        public GameObject prefab;
    }

    [Header("Configured Effects")]
    [SerializeField] private List<EffectEntry> effects = new List<EffectEntry>();

    [Header("Spawn Points (optional)")]
    [Tooltip("Inspector-defined positions SpawnAtPoint() can target.")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Pooling")]
    [Tooltip("Off = Instantiate/Destroy per spawn. Fine for rare one-shots; pooling pays off for frequent effects.")]
    [SerializeField] private bool usePooling = true;
    [SerializeField] private ObjectPoolManager.PoolType poolType = ObjectPoolManager.PoolType.GameObject;

    [Header("Safety")]
    [Tooltip("Return delay used when a prefab has no Animator or Animation to observe.")]
    [SerializeField, Min(0.05f)] private float fallbackDuration = 1f;
    [Tooltip("Hard cap so looping or broken clips can't keep an object alive forever. 0 = no cap.")]
    [SerializeField, Min(0f)] private float maxLifetime = 10f;

    // Instances this spawner is currently responsible for returning.
    private readonly HashSet<GameObject> tracked = new HashSet<GameObject>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnDisable()
    {
        // Coroutines die with this component. If the spawner is disabled
        // mid-effect, hand every live instance back now so pooled objects
        // can't be left running forever.
        foreach (GameObject go in tracked)
            ReleaseInstance(go);
        tracked.Clear();
    }

    /// <summary>Spawns a configured effect by id at a world position.</summary>
    public GameObject Spawn(string effectId, Vector3 position)
    {
        EffectEntry entry = effects.Find(e => e != null && e.id == effectId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] No configured effect with id '{effectId}'.", this);
            return null;
        }
        return Spawn(entry.prefab, position, Quaternion.identity);
    }

    /// <summary>Spawns a configured effect at an Inspector-defined spawn point.</summary>
    public GameObject SpawnAtPoint(string effectId, int pointIndex)
    {
        if (spawnPoints == null || pointIndex < 0 || pointIndex >= spawnPoints.Length || spawnPoints[pointIndex] == null)
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] Spawn point index {pointIndex} is not configured.", this);
            return null;
        }
        return Spawn(effectId, spawnPoints[pointIndex].position);
    }

    /// <summary>
    /// Spawns any animated prefab directly — registration in <see cref="effects"/>
    /// is not required for this overload.
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[PrefabAnimationSpawner] Spawn called with a null prefab.", this);
            return null;
        }

        if (prefab.GetComponentInChildren<Animator>(true) == null &&
            prefab.GetComponentInChildren<Animation>(true) == null)
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] '{prefab.name}' has no Animator or Animation — it will be returned after the fallback duration.", prefab);
        }

        GameObject go = usePooling
            ? ObjectPoolManager.SpawnObject(prefab, position, rotation, poolType)
            : Instantiate(prefab, position, rotation);
        if (go == null) return null;   // pool rejected the prefab (already logged)

        // Components must be read from the instance, never the prefab.
        Animator animator = go.GetComponentInChildren<Animator>(true);
        Animation legacyAnim = go.GetComponentInChildren<Animation>(true);

        if (animator != null)
        {
            // Pooled instances may be parked mid-play; Rebind resets the state
            // machine to its entry state and clears parameters so every spawn
            // starts at frame zero regardless of keepAnimatorControllerStateOnDisable.
            animator.Rebind();
            animator.Update(0f);
        }
        else if (legacyAnim != null)
        {
            legacyAnim.Stop();
            if (legacyAnim.clip != null)
                legacyAnim.Play();
        }

        // If the prefab carries its own ReturnToPool, it owns the lifecycle —
        // tracking here too would only produce redundant double-returns.
        if (go.GetComponent<ReturnToPool>() == null)
        {
            tracked.Add(go);
            StartCoroutine(ReturnWhenFinished(go, animator, legacyAnim));
        }
        else if (animator == null && legacyAnim != null)
        {
            // ReturnToPool can't observe legacy Animation components — it would
            // fall back to its fixed delay and cut the effect short.
            Debug.LogWarning($"[PrefabAnimationSpawner] '{prefab.name}' uses a legacy Animation with ReturnToPool — remove ReturnToPool and let this spawner track it.", go);
        }

        return go;
    }

    /// <summary>
    /// Polls the instance until its animation reports finished, then releases
    /// it. Polling (rather than assuming the clip length) stays correct under
    /// scaled time and variable clip durations.
    /// </summary>
    private IEnumerator ReturnWhenFinished(GameObject go, Animator animator, Animation legacyAnim)
    {
        float startTime = Time.time;

        while (go != null)
        {
            if (maxLifetime > 0f && Time.time - startTime >= maxLifetime)
                break;

            bool finished;
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                finished = state.normalizedTime >= 1f && !animator.IsInTransition(0);
            }
            else if (legacyAnim != null)
            {
                finished = !legacyAnim.isPlaying;
            }
            else
            {
                finished = Time.time - startTime >= fallbackDuration;
            }

            if (finished) break;
            yield return null;
        }

        tracked.Remove(go);
        ReleaseInstance(go);
    }

    private void ReleaseInstance(GameObject go)
    {
        if (go == null) return;

        if (usePooling)
            ObjectPoolManager.ReturnObjectToPool(go);
        else
            Destroy(go);
    }
}