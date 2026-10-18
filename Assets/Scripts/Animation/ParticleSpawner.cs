using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns one-shot particle effect prefabs (root or child ParticleSystems)
/// and returns each instance to the pool once every particle has died.
///
/// Reused instances get Clear()+Play() so no leftover particles carry over
/// from the previous life, and a maxLifetime cap prevents looping or
/// misconfigured systems from leaking pooled objects.
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
            ReleaseInstance(go);
        tracked.Clear();
    }

    /// <summary>Spawns a configured effect by id.</summary>
    public ParticleSystem Spawn(string effectId, Vector3 position, Quaternion rotation)
    {
        EffectEntry entry = effects.Find(e => e != null && e.id == effectId);
        if (entry == null || entry.prefab == null)
        {
            Debug.LogWarning($"[ParticleSpawner] No configured effect with id '{effectId}'.", this);
            return null;
        }
        return Spawn(entry.prefab, position, rotation);
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
            ? ObjectPoolManager.SpawnObject(prefab, position, rotation, poolType)
            : Instantiate(prefab, position, rotation);
        if (go == null) return null;   // pool rejected the prefab (already logged)

        // The pool already positioned + activated the instance; Clear+Play now
        // so emission starts at the NEW spot instead of leaving a streak from
        // where the object was parked in the pool.
        ParticleSystem[] systems = go.GetComponentsInChildren<ParticleSystem>(true);
        foreach (ParticleSystem ps in systems)
        {
            ps.Clear(true);   // wipe particles left over from the previous life
            ps.Play(true);    // restart this system and its child emitters
        }

        if (go.GetComponent<ReturnToPool>() == null)
        {
            tracked.Add(go);
            StartCoroutine(ReturnWhenFinished(go, systems));
        }

        return systems[0];
    }

    /// <summary>
    /// Polls until every ParticleSystem reports dead (IsAlive includes child
    /// emitters), then releases the instance. A timed-out looping system is
    /// force-stopped so its leftovers can't flash on the next reuse.
    /// </summary>
    private IEnumerator ReturnWhenFinished(GameObject go, ParticleSystem[] systems)
    {
        float startTime = Time.time;
        bool timedOut = false;

        while (go != null)
        {
            if (maxLifetime > 0f && Time.time - startTime >= maxLifetime)
            {
                timedOut = true;
                break;
            }

            bool anyAlive = false;
            foreach (ParticleSystem ps in systems)
            {
                if (ps != null && ps.IsAlive(true))
                {
                    anyAlive = true;
                    break;
                }
            }
            if (!anyAlive) break;

            yield return null;
        }

        if (timedOut && go != null)
        {
            foreach (ParticleSystem ps in systems)
                if (ps != null)
                    ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
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