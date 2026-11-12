using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unified pooled VFX spawner facade.
/// Automatically inspects a prefab once (cached by instance ID) and routes spawning to
/// <see cref="ParticleSpawner"/> (for ParticleSystem prefabs), <see cref="PrefabAnimationSpawner"/>
/// (for Animator / Animation prefabs), or <see cref="ObjectPoolManager"/> so callers can assign
/// any effect prefab into a single Inspector slot.
/// </summary>
public static class VfxSpawner
{
    private enum EffectPrefabCategory
    {
        Unknown = 0,
        Particle = 1,
        Animation = 2,
        ParticleAndAnimation = 3,
        GenericGameObject = 4,
    }

    private static readonly Dictionary<EntityId, EffectPrefabCategory> CategoryCache =
        new Dictionary<EntityId, EffectPrefabCategory>(32);

    /// <summary>
    /// Spawns an effect prefab (ParticleSystem, Animator/Animation, or generic GameObject) at the given
    /// world position and rotation using the project's pooled spawners.
    /// </summary>
    public static GameObject Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation = default,
        Transform parent = null,
        string animationStateName = null)
    {
        if (prefab == null)
            return null;

        if (rotation == default)
            rotation = Quaternion.identity;

        EffectPrefabCategory category = ResolveCategory(prefab);

        switch (category)
        {
            case EffectPrefabCategory.Particle:
            {
                ParticleSystem ps = ParticleSpawner.SpawnEffect(prefab, position, rotation, parent);
                return ps != null ? ps.gameObject : null;
            }

            case EffectPrefabCategory.Animation:
            {
                return PrefabAnimationSpawner.SpawnEffect(prefab, position, rotation, parent, animationStateName);
            }

            case EffectPrefabCategory.ParticleAndAnimation:
            {
                // Spawn via PrefabAnimationSpawner for Animator playback, and also play any child ParticleSystems
                GameObject instance = PrefabAnimationSpawner.SpawnEffect(prefab, position, rotation, parent, animationStateName);
                if (instance != null)
                {
                    ParticleSystem[] systems = instance.GetComponentsInChildren<ParticleSystem>(true);
                    for (int i = 0; i < systems.Length; i++)
                    {
                        systems[i].Clear(true);
                        systems[i].Play(true);
                    }
                }
                return instance;
            }

            default:
            {
                return ObjectPoolManager.SpawnObject(
                    prefab,
                    position,
                    rotation,
                    parent,
                    ObjectPoolManager.PoolType.GameObject);
            }
        }
    }

    private static EffectPrefabCategory ResolveCategory(GameObject prefab)
    {
        EntityId id = prefab.GetEntityId();
        if (CategoryCache.TryGetValue(id, out EffectPrefabCategory cached) && cached != EffectPrefabCategory.Unknown)
        {
            return cached;
        }

        bool hasParticle = prefab.GetComponentInChildren<ParticleSystem>(true) != null;
        bool hasAnimation = prefab.GetComponentInChildren<Animator>(true) != null
                         || prefab.GetComponentInChildren<Animation>(true) != null;

        EffectPrefabCategory resolved;
        if (hasParticle && hasAnimation)
            resolved = EffectPrefabCategory.ParticleAndAnimation;
        else if (hasParticle)
            resolved = EffectPrefabCategory.Particle;
        else if (hasAnimation)
            resolved = EffectPrefabCategory.Animation;
        else
            resolved = EffectPrefabCategory.GenericGameObject;

        CategoryCache[id] = resolved;
        return resolved;
    }
}

