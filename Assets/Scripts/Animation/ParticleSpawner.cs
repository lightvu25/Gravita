using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns particle effect prefabs (root or child ParticleSystems)
/// and delegates per-instance reset, child particle lifetime checks, looping handling,
/// and pool return to <see cref="PooledParticlePlayer"/>.
/// </summary>
public class ParticleSpawner : MonoBehaviour
{
    public static ParticleSpawner Instance { get; private set; }

    [System.Serializable]
    private class EffectEntry
    {
        public string id;
        public GameObject prefab;
    }

    [Header("Configured Effects")]
    [SerializeField] private List<EffectEntry> effects = new List<EffectEntry>();

    [Header("Spawn Points (optional)")]
    [SerializeField] private Transform[] spawnPoints;

    [Header("Pooling")]
    [Tooltip("Off = Instantiate/Destroy per spawn. Fine for rare one-shots; pooling pays off for frequent effects.")]
    [SerializeField] private bool usePooling = true;
    [SerializeField] private ObjectPoolManager.PoolType poolType = ObjectPoolManager.PoolType.ParticleSystem;

    [Header("Safety")]
    [Tooltip("Hard cap so looping systems can't keep an object alive forever. 0 = no cap.")]
    [SerializeField, Min(0f)] private float maxLifetime = 15f;

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
        foreach (GameObject go in tracked)
        {
            if (go == null) continue;
            if (go.TryGetComponent(out PooledParticlePlayer player))
                player.StopAndReturn();
            else
                ReleaseInstance(go);
        }
        tracked.Clear();
    }

    /// <summary>
    /// Static convenience API: spawns a particle prefab at a position, rotation, and optional parent
    /// via <see cref="Instance"/> if present, or directly via <see cref="PooledParticlePlayer.Spawn"/>.
    /// </summary>
    public static ParticleSystem SpawnEffect(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation = default,
        Transform parent = null)
    {
        if (prefab == null) return null;
        if (rotation == default) rotation = Quaternion.identity;

        if (Instance != null)
            return Instance.Spawn(prefab, position, rotation, parent);

        PooledParticlePlayer player = PooledParticlePlayer.Spawn(prefab, position, rotation, parent);
        return player != null ? player.PrimaryParticleSystem : null;
    }

    /// <summary>Spawns a configured effect by id.</summary>
    public ParticleSystem Spawn(string effectId, Vector3 position, Quaternion rotation)
    {
        return Spawn(effectId, position, rotation, null);
    }

    /// <summary>Spawns a configured effect by id at a position, rotation, and optional parent.</summary>
    public ParticleSystem Spawn(string effectId, Vector3 position, Quaternion rotation, Transform parent)
    {
        EffectEntry entry = effects.Find(e => e != null && e.id == effectId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[ParticleSpawner] No configured effect with id '{effectId}'.", this);
            return null;
        }
        return Spawn(entry.prefab, position, rotation, parent);
    }

    /// <summary>Spawns a configured effect at an Inspector-defined spawn point.</summary>
    public ParticleSystem SpawnAtPoint(string effectId, int pointIndex)
    {
        if (spawnPoints == null || pointIndex < 0 || pointIndex >= spawnPoints.Length || spawnPoints[pointIndex] == null)
        {
            Debug.LogWarning($"[ParticleSpawner] Spawn point index {pointIndex} is not configured.", this);
            return null;
        }
        Transform point = spawnPoints[pointIndex];
        return Spawn(effectId, point.position, point.rotation);
    }

    /// <summary>
    /// Spawns any particle prefab directly. Returns the first ParticleSystem
    /// found (root first) so callers can tweak modules like startColor.
    /// </summary>
    public ParticleSystem Spawn(GameObject prefab, Vector3 position, Quaternion rotation)
    {
        return Spawn(prefab, position, rotation, null);
    }

    /// <summary>
    /// Spawns any particle prefab directly at the given position, rotation, and optional parent.
    /// </summary>
    public ParticleSystem Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[ParticleSpawner] Spawn called with a null prefab.", this);
            return null;
        }

        if (prefab.GetComponentInChildren<ParticleSystem>(true) == null)
        {
            Debug.LogWarning($"[ParticleSpawner] '{prefab.name}' contains no ParticleSystem — nothing to spawn.", prefab);
            return null;
        }

        GameObject go = usePooling
            ? ObjectPoolManager.SpawnObject(prefab, position, rotation, parent, poolType)
            : (parent != null ? Instantiate(prefab, position, rotation, parent) : Instantiate(prefab, position, rotation));
        if (go == null) return null;

        if (usePooling && go.GetComponent<ReturnToPool>() == null)
        {
            if (!go.TryGetComponent(out PooledParticlePlayer player))
            {
                player = go.AddComponent<PooledParticlePlayer>();
                player.ConfigureLoopMaxLifetime(maxLifetime);
            }

            tracked.Add(go);
            player.Play(p =>
            {
                if (p != null)
                    tracked.Remove(p.gameObject);
            });
            return player.PrimaryParticleSystem;
        }

        ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ps.Clear(true);
            ps.Play(true);
        }

        return systems.Length > 0 ? systems[0] : null;
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
