using System.Collections.Generic;
using UnityEngine;
using System.Linq;

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

    public static List<PooledObjectInfo> ObjectPools = new List<PooledObjectInfo>();
    
    private static GameObject objectPoolEmptyHolder;

    private static GameObject particlesEmpty;
    private static GameObject gameObjectsEmpty;
    private static GameObject lootEmpty;
    private static GameObject enemiesEmpty;
    private static GameObject uiEmpty;
    private static GameObject projectilesEmpty;

    public static ObjectPoolManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            EnsureEmpties();
        }
        else
        {
            Destroy(gameObject);
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

    public static GameObject SpawnObject(GameObject objectToSpawn, Vector3 spawnPosition, Quaternion spawnRotation, PoolType poolType = PoolType.None)
    {
        EntityId prefabInstanceId = objectToSpawn.GetEntityId();
        PooledObjectInfo pool = ObjectPools.Find(x => x.prefabInstanceId == prefabInstanceId);
        
        if (pool == null)
        {
            pool = new PooledObjectInfo
            {
                lookupString = objectToSpawn.name,
                prefabInstanceId = prefabInstanceId
            };
            ObjectPools.Add(pool);
        }

        for (int i = pool.pooledObjects.Count - 1; i >= 0; i--)
        {
            if (pool.pooledObjects[i] == null)
            {
                pool.pooledObjects.RemoveAt(i);
            }
        }

        GameObject objectToUse = pool.pooledObjects.FirstOrDefault(x => x != null && !x.activeSelf);
        Transform parent = null;

        if (poolType != PoolType.None)
        {
            EnsureEmpties();
            parent = GetParentTransform(poolType);
        }

        if (objectToUse == null)
        {
            objectToUse = parent != null
                ? Instantiate(objectToSpawn, parent)
                : Instantiate(objectToSpawn);
            objectToUse.name = objectToSpawn.name;
            pool.pooledObjects.Add(objectToUse);
        }
        else if (parent != null && objectToUse.transform.parent != parent)
        {
            objectToUse.transform.SetParent(parent);
        }

        objectToUse.transform.position = spawnPosition;
        objectToUse.transform.rotation = spawnRotation;
        objectToUse.SetActive(true);
        return objectToUse;
    }

    public static void ReturnObjectToPool(GameObject objectToReturn)
    {
        if (objectToReturn == null) return;

        PooledObjectInfo pool = ObjectPools.Find(x => x.pooledObjects.Contains(objectToReturn));

        if (pool == null)
        {
            objectToReturn.SetActive(false);
            Destroy(objectToReturn);
        }
        else
        {
            if (objectToReturn.activeSelf)
                objectToReturn.SetActive(false);
        }
    }

    private static Transform GetParentTransform(PoolType poolType)
    {
        switch (poolType)
        {
            case PoolType.ParticleSystem: return particlesEmpty.transform;
            case PoolType.GameObject: return gameObjectsEmpty.transform;
            case PoolType.Loot: return lootEmpty.transform;
            case PoolType.Enemy: return enemiesEmpty.transform;
            case PoolType.UI: return uiEmpty.transform;
            case PoolType.Projectile: return projectilesEmpty.transform;
            default: return null;
        }
    }
}

public class PooledObjectInfo
{
    public string lookupString;
    public EntityId prefabInstanceId;
    public List<GameObject> pooledObjects = new List<GameObject>();
}
