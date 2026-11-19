using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Shared axis-aware VFX rotation resolver separating 3D +Z ParticleSystem cones
/// from 2D +X sprite Animator prefabs.
/// </summary>
public static class VfxRotationUtility
{
    private static readonly Dictionary<EntityId, bool> Is3DZParticleCache = new Dictionary<EntityId, bool>(32);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetCache()
    {
        Is3DZParticleCache.Clear();
    }

    public static Quaternion ResolveRotationForPrefab(GameObject prefab, bool align, Vector2 direction)
    {
        if (!align || direction.sqrMagnitude <= 0.0001f)
            return Quaternion.identity;

        Vector2 norm = direction.normalized;
        bool is3DZParticle = Is3DZParticlePrefab(prefab);

        return is3DZParticle
            ? Quaternion.LookRotation(new Vector3(norm.x, norm.y, 0f), Vector3.back)
            : Quaternion.Euler(0f, 0f, Mathf.Atan2(norm.y, norm.x) * Mathf.Rad2Deg);
    }

    private static bool Is3DZParticlePrefab(GameObject prefab)
    {
        if (prefab == null)
            return false;

        EntityId id = prefab.GetEntityId();
        if (Is3DZParticleCache.TryGetValue(id, out bool cached))
            return cached;

        bool resolved = prefab.GetComponentInChildren<ParticleSystem>(true) != null
            && prefab.GetComponentInChildren<Animator>(true) == null
            && prefab.GetComponentInChildren<Animation>(true) == null;

        Is3DZParticleCache[id] = resolved;
        return resolved;
    }
}
