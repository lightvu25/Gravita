using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reusable per-prefab presentation component (Layer 4) that spawns pooled effect prefabs
/// (ParticleSystem or Animator/Animation prefabs via <see cref="VfxSpawner"/>) whenever the
/// player interacts with the attached <see cref="IPlayerInteractable"/> object
/// (Star, Coin, CrackedBlock, BrittleFloor, BrakeTile, DeflectionWedge, Hazard, LevelExit).
/// </summary>
[DisallowMultipleComponent]
public class InteractionEffectPlayer : MonoBehaviour
{
    public enum SpawnAnchor
    {
        /// <summary>Uses the visual center reported by the interactable (Renderer.bounds.center).</summary>
        VisualCenter = 0,
        /// <summary>Uses this GameObject's transform.position.</summary>
        TransformOrigin = 1,
        /// <summary>Uses a custom Transform assigned in the Inspector.</summary>
        CustomTransform = 2,
    }

    [Serializable]
    public class InteractionEffectEntry
    {
        [Tooltip("Which interaction kind triggers this entry (Any = triggers on every interaction).")]
        public InteractionKind triggerOn = InteractionKind.Any;

        [Tooltip("Particle or Animation prefab to spawn from the object pool.")]
        public GameObject effectPrefab;

        [Tooltip("Optional Animator state name when the prefab uses an Animator.")]
        public string animationStateName;

        [Tooltip("Where to spawn the effect prefab.")]
        public SpawnAnchor anchor = SpawnAnchor.VisualCenter;

        [Tooltip("Optional custom spawn transform when anchor is CustomTransform.")]
        public Transform customAnchor;

        [Tooltip("World-space offset added to the resolved spawn position.")]
        public Vector3 positionOffset;

        [Tooltip("Rotate the spawned effect to face the interaction direction.")]
        public bool alignToDirection;

        [Tooltip("Optional camera shake impulse (0 = no shake).")]
        [Min(0f)]
        public float cameraShake;
    }

    [Header("Effect Prefabs (Particle or Animation)")]
    [Tooltip("Drag one or more VFX prefabs here (ParticleSystem or Animator prefabs). All assigned prefabs spawn at the object's visual center when the player interacts with it.")]
    [SerializeField] private List<GameObject> effectPrefabs = new List<GameObject>();

    [Header("Default Spawn Settings")]
    [SerializeField] private SpawnAnchor defaultAnchor = SpawnAnchor.VisualCenter;
    [SerializeField] private Transform customDefaultAnchor;
    [SerializeField] private Vector3 defaultOffset = Vector3.zero;
    [SerializeField] private bool alignToInteractionDirection = false;
    [Tooltip("Optional Animator state name for prefabs in Effect Prefabs.")]
    [SerializeField] private string defaultAnimationStateName;
    [Tooltip("Optional camera shake impulse triggered on interaction (0 = none).")]
    [SerializeField, Min(0f)] private float defaultCameraShake = 0f;

    [Header("Per-Interaction Rules (Optional)")]
    [Tooltip("Optional fine-grained rules filtered by InteractionKind (e.g. Deflect vs Hit, Crack vs Break).")]
    [SerializeField] private List<InteractionEffectEntry> rules = new List<InteractionEffectEntry>();

    private IPlayerInteractable interactable;
    private Renderer cachedRenderer;
    private Collider2D cachedCollider;

    /// <summary>
    /// True if at least one non-null effect prefab is configured on this component.
    /// </summary>
    public bool HasConfiguredEffects
    {
        get
        {
            if (effectPrefabs != null)
            {
                for (int i = 0; i < effectPrefabs.Count; i++)
                {
                    if (effectPrefabs[i] != null) return true;
                }
            }

            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i] != null && rules[i].effectPrefab != null) return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// True if this component is configured to apply a camera shake on interaction.
    /// </summary>
    public bool HasConfiguredShake
    {
        get
        {
            if (defaultCameraShake > 0f) return true;
            if (rules != null)
            {
                for (int i = 0; i < rules.Count; i++)
                {
                    if (rules[i] != null && rules[i].cameraShake > 0f) return true;
                }
            }
            return false;
        }
    }

    private void Awake()
    {
        interactable = GetComponent<IPlayerInteractable>();
        cachedRenderer = GetComponentInChildren<Renderer>();
        cachedCollider = GetComponent<Collider2D>();

        if (interactable == null)
        {
            Debug.LogError($"[{nameof(InteractionEffectPlayer)}] Missing IPlayerInteractable component on '{name}'!", this);
        }
    }

    private void OnEnable()
    {
        if (interactable == null)
            interactable = GetComponent<IPlayerInteractable>();

        if (interactable != null)
            interactable.OnPlayerInteracted += HandlePlayerInteracted;
    }

    private void OnDisable()
    {
        if (interactable != null)
            interactable.OnPlayerInteracted -= HandlePlayerInteracted;
    }

    private void HandlePlayerInteracted(InteractionContext context)
    {
        TriggerEffects(context);
    }

    /// <summary>
    /// Spawns all configured effects matching the given <see cref="InteractionContext"/>.
    /// </summary>
    public void TriggerEffects(InteractionContext context)
    {
        float maxShake = defaultCameraShake;

        // 1. Spawn primary effectPrefabs list
        if (effectPrefabs != null && effectPrefabs.Count > 0)
        {
            Vector3 spawnPos = ResolveSpawnPosition(defaultAnchor, customDefaultAnchor, context.VisualCenter) + defaultOffset;

            for (int i = 0; i < effectPrefabs.Count; i++)
            {
                GameObject prefab = effectPrefabs[i];
                if (prefab == null) continue;
                Quaternion spawnRot = VfxRotationUtility.ResolveRotationForPrefab(prefab, alignToInteractionDirection, context.Direction);
                VfxSpawner.Spawn(prefab, spawnPos, spawnRot, null, defaultAnimationStateName);
            }
        }

        // 2. Spawn matching per-interaction rules
        if (rules != null && rules.Count > 0)
        {
            for (int i = 0; i < rules.Count; i++)
            {
                InteractionEffectEntry rule = rules[i];
                if (rule == null) continue;
                if (rule.triggerOn != InteractionKind.Any && rule.triggerOn != context.Kind) continue;

                if (rule.cameraShake > maxShake)
                    maxShake = rule.cameraShake;

                if (rule.effectPrefab == null) continue;

                Vector3 pos = ResolveSpawnPosition(rule.anchor, rule.customAnchor, context.VisualCenter) + rule.positionOffset;
                Quaternion rot = VfxRotationUtility.ResolveRotationForPrefab(rule.effectPrefab, rule.alignToDirection, context.Direction);
                VfxSpawner.Spawn(rule.effectPrefab, pos, rot, null, rule.animationStateName);
            }
        }

        if (maxShake > 0f && CinemachineCameraShake2D.Instance != null)
        {
            Vector2 shakeDir = context.Direction != Vector2.zero ? context.Direction : Vector2.down;
            CinemachineCameraShake2D.Instance.ShakeCamera(shakeDir, maxShake);
        }
    }

    /// <summary>
    /// Manual trigger helper (e.g. for UnityEvents or custom scripts).
    /// </summary>
    public void TriggerDefaultEffects()
    {
        TriggerEffects(new InteractionContext(InteractionKind.Any, ResolveFallbackVisualCenter(), Vector2.zero));
    }

    private Vector3 ResolveSpawnPosition(SpawnAnchor anchor, Transform customTransform, Vector3 contextVisualCenter)
    {
        switch (anchor)
        {
            case SpawnAnchor.CustomTransform:
                return customTransform != null ? customTransform.position : transform.position;
            case SpawnAnchor.TransformOrigin:
                return transform.position;
            default:
                return contextVisualCenter;
        }
    }

    private Vector3 ResolveFallbackVisualCenter()
    {
        if (cachedRenderer == null)
            cachedRenderer = GetComponentInChildren<Renderer>();

        if (cachedRenderer != null && cachedRenderer.enabled && cachedRenderer.gameObject.activeInHierarchy)
            return cachedRenderer.bounds.center;

        if (cachedCollider != null && cachedCollider.enabled && cachedCollider.gameObject.activeInHierarchy)
            return cachedCollider.bounds.center;

        return transform.position;
    }
}

