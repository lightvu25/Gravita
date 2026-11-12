using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

public class ObjectPoolManager : MonoBehaviour
{
    public enum PoolType
    {
        ParticleSystem,
        GameObject,
        Loot,
        Enemy,
        UI,
        Projectile,
        None
    }

    [Serializable]
    public class PoolConfig
    {
        public GameObject prefab;
        [Min(0)] public int initialSize = 4;
        public PoolType poolType = PoolType.None;
    }

    [Header("Prewarm Configuration")]
    [Tooltip("Default initial size created when a new prefab pool is initialized on demand (0 = create on demand only).")]
    [SerializeField, Min(0)] private int defaultInitialPoolSize = 0;

    [Tooltip("Optional list of prefab pools to prewarm on Awake.")]
    [SerializeField] private List<PoolConfig> initialPools = new List<PoolConfig>();

    public static List<PooledObjectInfo> ObjectPools = new List<PooledObjectInfo>();

    private static readonly Dictionary<EntityId, PooledObjectInfo> poolsByPrefabId = new Dictionary<EntityId, PooledObjectInfo>();
    private static readonly Dictionary<GameObject, PooledObjectInfo> instanceToPool = new Dictionary<GameObject, PooledObjectInfo>();

    private static GameObject objectPoolEmptyHolder;
    private static GameObject particlesEmpty;
    private static GameObject gameObjectsEmpty;
    private static GameObject lootEmpty;
    private static GameObject enemiesEmpty;
    private static GameObject uiEmpty;
    private static GameObject projectilesEmpty;

    private static bool sceneHookRegistered;

    public static ObjectPoolManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            RegisterSceneHooks();
            EnsureEmpties();
            InitializeConfiguredPools();
        }
        else if (Instance != this)
        {
            Destroy(gameObject);
        }
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private static void RegisterSceneHooks()
    {
        if (sceneHookRegistered) return;
        SceneManager.sceneUnloaded += HandleSceneUnloaded;
        sceneHookRegistered = true;
    }

    private static void HandleSceneUnloaded(Scene _)
    {
        CleanupDestroyedReferences();
    }

    private void InitializeConfiguredPools()
    {
        if (initialPools == null) return;

        for (int i = 0; i < initialPools.Count; i++)
        {
            PoolConfig config = initialPools[i];
            if (config == null || config.prefab == null || config.initialSize <= 0)
                continue;

            PrewarmPool(config.prefab, config.initialSize, config.poolType);
        }
    }

    private static void EnsureEmpties()
    {
        if (objectPoolEmptyHolder == null)
            objectPoolEmptyHolder = new GameObject("Pooled Objects");

        if (particlesEmpty == null)
        {
            particlesEmpty = new GameObject("Particles");
            particlesEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }

        if (gameObjectsEmpty == null)
        {
            gameObjectsEmpty = new GameObject("GameObjects");
            gameObjectsEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }

        if (lootEmpty == null)
        {
            lootEmpty = new GameObject("Loot");
            lootEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }

        if (enemiesEmpty == null)
        {
            enemiesEmpty = new GameObject("Enemies");
            enemiesEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }

        if (uiEmpty == null)
        {
            uiEmpty = new GameObject("UI");
            uiEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }

        if (projectilesEmpty == null)
        {
            projectilesEmpty = new GameObject("Projectiles");
            projectilesEmpty.transform.SetParent(objectPoolEmptyHolder.transform);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Static Pool API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-creates inactive instances for the given prefab up to <paramref name="initialSize"/>.
    /// </summary>
    public static void PrewarmPool(GameObject prefab, int initialSize, PoolType poolType = PoolType.None)
    {
        if (prefab == null)
        {
            Debug.LogWarning("[ObjectPoolManager] Cannot prewarm a null prefab.");
            return;
        }

        if (initialSize <= 0) return;

        RegisterSceneHooks();
        PooledObjectInfo pool = GetOrCreatePool(prefab, poolType);
        pool.PurgeDestroyed(instanceToPool);

        Transform holder = ResolvePoolHolder(pool.defaultPoolType);
        int needed = initialSize - pool.pooledObjects.Count;
        for (int i = 0; i < needed; i++)
        {
            GameObject instance = CreateNewInstance(prefab, pool, holder, activate: false);
            pool.StoreInactive(instance);
        }
    }

    public static GameObject SpawnObject(
        GameObject objectToSpawn,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        PoolType poolType = PoolType.None)
    {
        return SpawnInternal(objectToSpawn, spawnPosition, spawnRotation, null, poolType);
    }

    public static GameObject SpawnObject(
        GameObject objectToSpawn,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Transform parent,
        PoolType poolType = PoolType.None)
    {
        return SpawnInternal(objectToSpawn, spawnPosition, spawnRotation, parent, poolType);
    }

    public static T SpawnObject<T>(
        T componentPrefab,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        PoolType poolType = PoolType.None) where T : Component
    {
        if (componentPrefab == null)
        {
            Debug.LogWarning("[ObjectPoolManager] Cannot spawn a null component prefab.");
            return null;
        }

        GameObject go = SpawnInternal(componentPrefab.gameObject, spawnPosition, spawnRotation, null, poolType);
        return go != null ? go.GetComponent<T>() : null;
    }

    public static T SpawnObject<T>(
        T componentPrefab,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Transform parent,
        PoolType poolType = PoolType.None) where T : Component
    {
        if (componentPrefab == null)
        {
            Debug.LogWarning("[ObjectPoolManager] Cannot spawn a null component prefab.");
            return null;
        }

        GameObject go = SpawnInternal(componentPrefab.gameObject, spawnPosition, spawnRotation, parent, poolType);
        return go != null ? go.GetComponent<T>() : null;
    }

    public static void ReturnObjectToPool(Component componentToReturn)
    {
        if (componentToReturn == null) return;
        ReturnObjectToPool(componentToReturn.gameObject);
    }

    public static void ReturnObjectToPool(GameObject objectToReturn)
    {
        if (objectToReturn == null) return;

        PooledObjectInfo pool = FindOwningPool(objectToReturn);

        if (pool == null)
        {
            Debug.LogWarning($"[ObjectPoolManager] '{objectToReturn.name}' does not belong to any registered pool — deactivating and destroying.", objectToReturn);
            if (objectToReturn.activeSelf)
                objectToReturn.SetActive(false);
            Destroy(objectToReturn);
            return;
        }

        // Prevent duplicate returns of the same instance while it is already stored in the pool.
        if (pool.IsStoredInactive(objectToReturn) && !objectToReturn.activeSelf)
        {
            return;
        }

        // Always deactivate before storing in the pool.
        if (objectToReturn.activeSelf)
        {
            objectToReturn.SetActive(false);
        }

        // Re-parent back to the pool container if the instance was attached to an external parent.
        Transform poolHolder = ResolvePoolHolder(pool.defaultPoolType);
        if (poolHolder != null && objectToReturn.transform.parent != poolHolder)
        {
            objectToReturn.transform.SetParent(poolHolder, false);
        }

        pool.StoreInactive(objectToReturn);
    }

    /// <summary>
    /// Returns all currently active pooled instances across all pools back to their respective pools.
    /// </summary>
    public static void ReturnAllActiveObjects()
    {
        for (int p = 0; p < ObjectPools.Count; p++)
        {
            PooledObjectInfo pool = ObjectPools[p];
            if (pool == null) continue;

            for (int i = pool.pooledObjects.Count - 1; i >= 0; i--)
            {
                GameObject instance = pool.pooledObjects[i];
                if (instance == null)
                {
                    pool.pooledObjects.RemoveAt(i);
                    continue;
                }

                if (instance.activeSelf || !pool.IsStoredInactive(instance))
                {
                    ReturnObjectToPool(instance);
                }
            }
        }
    }

    /// <summary>
    /// Removes destroyed references across all pools (e.g., after a scene unload).
    /// </summary>
    public static void CleanupDestroyedReferences()
    {
        for (int p = ObjectPools.Count - 1; p >= 0; p--)
        {
            PooledObjectInfo pool = ObjectPools[p];
            if (pool == null)
            {
                ObjectPools.RemoveAt(p);
                continue;
            }

            pool.PurgeDestroyed(instanceToPool);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Instance Convenience Wrappers
    // ─────────────────────────────────────────────────────────────

    public void Prewarm(GameObject prefab, int initialSize, PoolType poolType = PoolType.None) =>
        PrewarmPool(prefab, initialSize, poolType);

    public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent = null, PoolType poolType = PoolType.None) =>
        SpawnObject(prefab, position, rotation, parent, poolType);

    public void Return(GameObject instance) =>
        ReturnObjectToPool(instance);

    // ─────────────────────────────────────────────────────────────
    //  Internal Implementation
    // ─────────────────────────────────────────────────────────────

    private static GameObject SpawnInternal(
        GameObject objectToSpawn,
        Vector3 spawnPosition,
        Quaternion spawnRotation,
        Transform customParent,
        PoolType poolType)
    {
        if (objectToSpawn == null)
        {
            Debug.LogWarning("[ObjectPoolManager] SpawnObject called with a null prefab reference.");
            return null;
        }

        RegisterSceneHooks();
        PooledObjectInfo pool = GetOrCreatePool(objectToSpawn, poolType);
        Transform targetParent = customParent != null
            ? customParent
            : ResolvePoolHolder(poolType != PoolType.None ? poolType : pool.defaultPoolType);

        GameObject objectToUse = pool.AcquireInactive(instanceToPool);

        if (objectToUse == null)
        {
            objectToUse = CreateNewInstance(objectToSpawn, pool, targetParent, activate: false);
        }
        else if (objectToUse.transform.parent != targetParent)
        {
            objectToUse.transform.SetParent(targetParent, false);
        }

        Transform tr = objectToUse.transform;
        tr.SetPositionAndRotation(spawnPosition, spawnRotation);
        if (customParent == null && objectToSpawn != null)
        {
            tr.localScale = objectToSpawn.transform.localScale;
        }

        objectToUse.SetActive(true);
        return objectToUse;
    }

    private static PooledObjectInfo GetOrCreatePool(GameObject prefab, PoolType poolType)
    {
        EntityId prefabId = prefab.GetEntityId();

        if (!poolsByPrefabId.TryGetValue(prefabId, out PooledObjectInfo pool) || pool == null)
        {
            // Check ObjectPools list in case it was added externally
            for (int i = 0; i < ObjectPools.Count; i++)
            {
                if (ObjectPools[i] != null && ObjectPools[i].prefabInstanceId == prefabId)
                {
                    pool = ObjectPools[i];
                    break;
                }
            }

            if (pool == null)
            {
                PoolType resolvedType = poolType;
                if (resolvedType == PoolType.None && prefab.GetComponentInChildren<ParticleSystem>(true) != null)
                {
                    resolvedType = PoolType.ParticleSystem;
                }

                pool = new PooledObjectInfo
                {
                    lookupString = prefab.name,
                    prefabInstanceId = prefabId,
                    prefab = prefab,
                    defaultPoolType = resolvedType
                };
                ObjectPools.Add(pool);
                poolsByPrefabId[prefabId] = pool;

                int defaultCount = Instance != null ? Instance.defaultInitialPoolSize : 0;
                if (defaultCount > 1)
                {
                    Transform holder = ResolvePoolHolder(pool.defaultPoolType);
                    for (int i = 0; i < defaultCount - 1; i++)
                    {
                        GameObject prewarmed = CreateNewInstance(prefab, pool, holder, activate: false);
                        pool.StoreInactive(prewarmed);
                    }
                }
            }
            else
            {
                poolsByPrefabId[prefabId] = pool;
            }
        }
        else if (pool.defaultPoolType == PoolType.None && poolType != PoolType.None)
        {
            pool.defaultPoolType = poolType;
        }

        return pool;
    }

    private static GameObject CreateNewInstance(GameObject prefab, PooledObjectInfo pool, Transform parent, bool activate)
    {
        GameObject instance = parent != null
            ? Instantiate(prefab, parent)
            : Instantiate(prefab);

        instance.name = prefab.name;
        if (instance.activeSelf != activate)
            instance.SetActive(activate);

        pool.pooledObjects.Add(instance);
        instanceToPool[instance] = pool;
        return instance;
    }

    private static PooledObjectInfo FindOwningPool(GameObject instance)
    {
        if (instanceToPool.TryGetValue(instance, out PooledObjectInfo mappedPool) && mappedPool != null)
            return mappedPool;

        for (int i = 0; i < ObjectPools.Count; i++)
        {
            PooledObjectInfo candidate = ObjectPools[i];
            if (candidate != null && candidate.pooledObjects.Contains(instance))
            {
                instanceToPool[instance] = candidate;
                return candidate;
            }
        }

        return null;
    }

    private static Transform ResolvePoolHolder(PoolType poolType)
    {
        if (poolType == PoolType.None) return null;
        EnsureEmpties();
        return GetParentTransform(poolType);
    }

    private static Transform GetParentTransform(PoolType poolType)
    {
        switch (poolType)
        {
            case PoolType.ParticleSystem: return particlesEmpty != null ? particlesEmpty.transform : null;
            case PoolType.GameObject: return gameObjectsEmpty != null ? gameObjectsEmpty.transform : null;
            case PoolType.Loot: return lootEmpty != null ? lootEmpty.transform : null;
            case PoolType.Enemy: return enemiesEmpty != null ? enemiesEmpty.transform : null;
            case PoolType.UI: return uiEmpty != null ? uiEmpty.transform : null;
            case PoolType.Projectile: return projectilesEmpty != null ? projectilesEmpty.transform : null;
            default: return null;
        }
    }
}

public class PooledObjectInfo
{
    public string lookupString;
    public EntityId prefabInstanceId;
    public GameObject prefab;
    public ObjectPoolManager.PoolType defaultPoolType = ObjectPoolManager.PoolType.None;
    public List<GameObject> pooledObjects = new List<GameObject>();

    private readonly Queue<GameObject> inactiveQueue = new Queue<GameObject>();
    private readonly HashSet<GameObject> inactiveSet = new HashSet<GameObject>();

    public bool IsStoredInactive(GameObject instance) =>
        instance != null && inactiveSet.Contains(instance);

    public void StoreInactive(GameObject instance)
    {
        if (instance == null) return;
        if (inactiveSet.Add(instance))
        {
            inactiveQueue.Enqueue(instance);
        }
    }

    public GameObject AcquireInactive(Dictionary<GameObject, PooledObjectInfo> instanceMap)
    {
        while (inactiveQueue.Count > 0)
        {
            GameObject candidate = inactiveQueue.Dequeue();
            inactiveSet.Remove(candidate);

            if (candidate == null)
                continue;

            if (!candidate.activeSelf)
                return candidate;
        }

        // Fallback: check if any tracked object was deactivated directly without calling ReturnObjectToPool
        for (int i = pooledObjects.Count - 1; i >= 0; i--)
        {
            GameObject candidate = pooledObjects[i];
            if (candidate == null)
            {
                pooledObjects.RemoveAt(i);
                continue;
            }

            if (!candidate.activeSelf)
            {
                inactiveSet.Remove(candidate);
                return candidate;
            }
        }

        return null;
    }

    public void PurgeDestroyed(Dictionary<GameObject, PooledObjectInfo> instanceMap)
    {
        for (int i = pooledObjects.Count - 1; i >= 0; i--)
        {
            GameObject go = pooledObjects[i];
            if (go == null)
            {
                pooledObjects.RemoveAt(i);
            }
        }

        if (inactiveQueue.Count == 0) return;

        int count = inactiveQueue.Count;
        for (int i = 0; i < count; i++)
        {
            GameObject go = inactiveQueue.Dequeue();
            if (go != null)
            {
                inactiveQueue.Enqueue(go);
            }
            else
            {
                inactiveSet.Remove(go);
            }
        }
    }
}
