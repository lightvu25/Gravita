using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns animated effect prefabs — a SpriteRenderer driven by an
/// Animator (or a legacy Animation component) — and delegates per-instance
/// playback, reset, looping handling, and pool return to <see cref="PooledAnimationPlayer"/>.
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
    [Tooltip("Hard cap so broken clips can't keep an object alive forever. 0 = no cap.")]
    [SerializeField, Min(0f)] private float maxLifetime = 10f;

    private readonly HashSet<GameObject> tracked = new HashSet<GameObject>();
    private static readonly Dictionary<EntityId, bool> ValidAnimationPrefabCache = new Dictionary<EntityId, bool>(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticCache()
    {
        ValidAnimationPrefabCache.Clear();
    }

    private static bool HasAnimatorOrAnimationCached(GameObject prefab)
    {
        EntityId id = prefab.GetEntityId();
        if (ValidAnimationPrefabCache.TryGetValue(id, out bool hasAnim))
            return hasAnim;

        hasAnim = prefab.GetComponentInChildren<Animator>(true) != null
            || prefab.GetComponentInChildren<Animation>(true) != null;
        ValidAnimationPrefabCache[id] = hasAnim;
        return hasAnim;
    }

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
        foreach (GameObject go in tracked)
        {
            if (go == null) continue;
            if (go.TryGetComponent(out PooledAnimationPlayer player))
                player.StopAndReturn();
            else
                ReleaseInstance(go);
        }
        tracked.Clear();
    }

    /// <summary>
    /// Static convenience API: spawns an animation prefab at a position, rotation, and optional parent
    /// via <see cref="Instance"/> if present, or directly via <see cref="PooledAnimationPlayer.Spawn"/>.
    /// </summary>
    public static GameObject SpawnEffect(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation = default,
        Transform parent = null,
        string stateName = null)
    {
        if (prefab == null) return null;
        if (rotation == default) rotation = Quaternion.identity;

        if (Instance != null)
            return Instance.Spawn(prefab, position, rotation, parent, stateName);

        PooledAnimationPlayer player = PooledAnimationPlayer.Spawn(prefab, position, rotation, parent, stateName);
        return player != null ? player.gameObject : null;
    }

    /// <summary>Spawns a configured effect by id at a world position.</summary>
    public GameObject Spawn(string effectId, Vector3 position)
    {
        return Spawn(effectId, position, Quaternion.identity, null);
    }

    /// <summary>Spawns a configured effect by id at a world position, rotation, and optional parent.</summary>
    public GameObject Spawn(string effectId, Vector3 position, Quaternion rotation, Transform parent = null)
    {
        EffectEntry entry = effects.Find(e => e != null && e.id == effectId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] No configured effect with id '{effectId}'.", this);
            return null;
        }
        return Spawn(entry.prefab, position, rotation, parent);
    }

    /// <summary>Spawns a configured effect at an Inspector-defined spawn point.</summary>
    public GameObject SpawnAtPoint(string effectId, int pointIndex)
    {
        if (spawnPoints == null || pointIndex < 0 || pointIndex >= spawnPoints.Length || spawnPoints[pointIndex] == null)
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] Spawn point index {pointIndex} is not configured.", this);
            return null;
        }
        Transform point = spawnPoints[pointIndex];
        return Spawn(effectId, point.position, point.rotation);
    }

    /// <summary>
    /// Spawns any animated prefab directly — registration in <see cref="effects"/>
    /// is not required for this overload.
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        return Spawn(prefab, position, rotation, null, null);
    }

    /// <summary>
    /// Spawns any animated prefab directly at the given position, rotation, and optional parent.
    /// </summary>
    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent, string stateName = null)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[PrefabAnimationSpawner] Spawn called with a null prefab.", this);
            return null;
        }

        if (!HasAnimatorOrAnimationCached(prefab))
        {
            Debug.LogWarning($"[PrefabAnimationSpawner] '{prefab.name}' has no Animator or Animation — it will be returned after the fallback duration.", prefab);
        }

        GameObject go = usePooling
            ? ObjectPoolManager.SpawnObject(prefab, position, rotation, parent, poolType)
            : (parent != null ? Instantiate(prefab, position, rotation, parent) : Instantiate(prefab, position, rotation));
        if (go == null) return null;

        if (usePooling && go.GetComponent<ReturnToPool>() == null)
        {
            if (!go.TryGetComponent(out PooledAnimationPlayer player))
            {
                player = go.AddComponent<PooledAnimationPlayer>();
                player.ConfigureFallbackDuration(fallbackDuration);
            }

            tracked.Add(go);
            player.Play(stateName, p =>
            {
                if (p != null)
                    tracked.Remove(p.gameObject);
            });
        }
        else
        {
            Animator animator = go.GetComponentInChildren<Animator>(true);
            Animation legacyAnim = go.GetComponentInChildren<Animation>(true);

            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
            else if (legacyAnim != null)
            {
                legacyAnim.Stop();
                if (legacyAnim.clip != null)
                    legacyAnim.Play();
            }

            if (!usePooling)
            {
                Destroy(go, Mathf.Max(fallbackDuration, maxLifetime > 0f ? maxLifetime : fallbackDuration));
            }
        }

        return go;
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
