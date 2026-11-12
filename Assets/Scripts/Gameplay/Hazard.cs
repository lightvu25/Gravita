using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Trigger volume that kills the player on contact (spikes, lasers, electrified floors, pits, drones).
/// Reports to the parent <see cref="LevelRoot"/> which runs the death/revive/failure flow.
/// Supports <see cref="IHazardActivation"/> (timed/switch/skate lethality), the Kinetic Burst (Breaker)
/// power-up for eligible traps, and EMP Pulse (Freeze) for freezable traps and enemies.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class Hazard : MonoBehaviour, IFreezable, IPlayerInteractable
{
    public event Action<InteractionContext> OnPlayerInteracted;

    [Tooltip("Reason shown on the fail screen.")]
    [SerializeField] private string failReason = "You hit a hazard!";

    [Header("Power-up Interaction")]
    [Tooltip("True if this hazard represents an enemy rather than a stationary/mechanical trap. Breaker only breaks traps, not enemies.")]
    [SerializeField] private bool isEnemy = false;

    [Tooltip("Whether the Breaker power-up can break this trap (ignored if isEnemy is true).")]
    [SerializeField] private bool breakableByBreaker = true;

    [Tooltip("Whether the Freeze power-up can temporarily freeze this trap or enemy.")]
    [SerializeField] private bool supportsFreeze = true;

    [Tooltip("Optional visual root hidden when this trap is broken by Breaker.")]
    [SerializeField] private GameObject intactVisual;

    public string FailReason => failReason;
    public bool IsEnemy => isEnemy;
    public bool IsTrap => !isEnemy;
    public bool IsBroken { get; private set; }
    public bool IsFrozen { get; private set; }

    public bool IsActiveHazard =>
        !IsBroken
        && !IsFrozen
        && isActiveAndEnabled
        && (hazardActivation == null || hazardActivation.IsHazardGenerallyActive);

    public bool CanBeBrokenByBreaker => !isEnemy && breakableByBreaker && !IsBroken;
    public bool SupportsFreeze => supportsFreeze && !IsBroken;

    private LevelRoot levelRoot;
    private IHazardActivation hazardActivation;
    private Collider2D[] hazardColliders;
    private Renderer[] hazardRenderers;
    private SpriteRenderer[] spriteRenderers;
    private Color[] originalSpriteColors;
    private Animator hazardAnimator;
    private Rigidbody2D hazardBody;
    private Vector3 initialLocalPosition;
    private bool hasInitialPosition;

    private static readonly Collider2D[] overlapBuffer = new Collider2D[8];

    // Saved pre-freeze state so UnfreezeTarget restores exact previous behavior
    private bool[] preFreezeColliderStates;
    private bool preFreezeAnimatorEnabled;
    private Vector2 preFreezeLinearVelocity;
    private float preFreezeAngularVelocity;
    private readonly List<MonoBehaviour> pausedBehaviours = new List<MonoBehaviour>();

    private static readonly Color FrozenTintColor = new Color(0.55f, 0.88f, 1f, 1f);

    private void Awake()
    {
        levelRoot = GetComponentInParent<LevelRoot>();
        hazardActivation = GetComponent<IHazardActivation>();
        initialLocalPosition = transform.localPosition;
        hasInitialPosition = true;

        hazardColliders = GetComponentsInChildren<Collider2D>(includeInactive: true);
        hazardRenderers = GetComponentsInChildren<Renderer>(includeInactive: true);
        spriteRenderers = GetComponentsInChildren<SpriteRenderer>(includeInactive: true);
        if (spriteRenderers != null && spriteRenderers.Length > 0)
        {
            originalSpriteColors = new Color[spriteRenderers.Length];
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    originalSpriteColors[i] = spriteRenderers[i].color;
            }
        }

        hazardAnimator = GetComponentInChildren<Animator>();
        hazardBody = GetComponent<Rigidbody2D>();

        Collider2D col = GetComponent<Collider2D>();
        if (col != null && !col.isTrigger)
        {
            Debug.LogWarning($"[Hazard] '{name}': Collider2D must be a trigger. Setting isTrigger = true.", this);
            col.isTrigger = true;
        }
    }

    private void OnEnable()
    {
        if (hazardActivation == null)
            hazardActivation = GetComponent<IHazardActivation>();

        if (PowerUpController.Instance != null && PowerUpController.Instance.IsFreezeActive)
        {
            PowerUpController.Instance.RegisterAndFreezeIfActive(this);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        TryTriggerHazard(other);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        TryTriggerHazard(other);
    }

    public bool IsLethalToPlayer(PlayerMovement player)
    {
        if (player == null || player.IsPhasing || !IsActiveHazard)
            return false;

        return hazardActivation == null || hazardActivation.IsLethalTo(player);
    }

    /// <summary>
    /// Explicitly checks whether the player currently overlaps this hazard's colliders.
    /// Called when a timed/switched hazard transitions into its active state, or on Skate ticks,
    /// because Unity may not fire OnTriggerStay2D on a stationary kinematic Rigidbody2D.
    /// </summary>
    public void CheckOverlappingPlayer()
    {
        if (!IsActiveHazard || hazardColliders == null) return;

        for (int i = 0; i < hazardColliders.Length; i++)
        {
            Collider2D col = hazardColliders[i];
            if (col == null || !col.enabled || !col.gameObject.activeInHierarchy) continue;

            Bounds b = col.bounds;
            if (b.size.sqrMagnitude <= 0.0001f) continue;

            int count = Physics2D.OverlapBox(b.center, b.size * 0.92f, 0f, ContactFilter2D.noFilter, overlapBuffer);
            for (int k = 0; k < count; k++)
            {
                Collider2D hit = overlapBuffer[k];
                if (hit == null || hit == col) continue;
                PlayerMovement player = hit.GetComponentInParent<PlayerMovement>();
                if (player != null)
                {
                    TryTriggerForPlayer(player);
                    return;
                }
            }
        }
    }

    private void TryTriggerHazard(Collider2D other)
    {
        if (!IsActiveHazard || other == null) return;
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null) return;
        TryTriggerForPlayer(player);
    }

    private void TryTriggerForPlayer(PlayerMovement player)
    {
        if (!IsLethalToPlayer(player)) return;

        OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Hit, ResolveVisualWorldCenter(), player.CurrentDirection));

        if (levelRoot == null)
            levelRoot = GetComponentInParent<LevelRoot>();

        levelRoot?.NotifyHazardTriggered(this);
    }

    public Vector3 ResolveVisualWorldCenter()
    {
        if (hazardRenderers != null)
        {
            for (int i = 0; i < hazardRenderers.Length; i++)
            {
                Renderer r = hazardRenderers[i];
                if (r != null && r.enabled && r.gameObject.activeInHierarchy)
                    return r.bounds.center;
            }
        }

        if (hazardColliders != null)
        {
            for (int i = 0; i < hazardColliders.Length; i++)
            {
                Collider2D c = hazardColliders[i];
                if (c != null && c.enabled && c.gameObject.activeInHierarchy)
                    return c.bounds.center;
            }
        }

        return transform.position;
    }

    /// <summary>
    /// Breaks this trap when hit by the Breaker power-up.
    /// Does not affect enemies or non-breakable hazards.
    /// </summary>
    public bool BreakTrap()
    {
        if (!CanBeBrokenByBreaker) return false;

        if (IsFrozen)
            UnfreezeTarget();

        Vector3 breakCenter = ResolveVisualWorldCenter();
        IsBroken = true;
        OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Break, breakCenter, Vector2.zero));

        if (hazardColliders != null)
        {
            for (int i = 0; i < hazardColliders.Length; i++)
            {
                if (hazardColliders[i] != null)
                    hazardColliders[i].enabled = false;
            }
        }

        if (intactVisual != null)
        {
            intactVisual.SetActive(false);
        }
        else if (hazardRenderers != null)
        {
            for (int i = 0; i < hazardRenderers.Length; i++)
            {
                if (hazardRenderers[i] != null)
                    hazardRenderers[i].enabled = false;
            }
        }

        return true;
    }

    /// <summary>
    /// Freezes this trap or enemy without changing Time.timeScale.
    /// Preserves previous state so it can be restored accurately when Freeze ends.
    /// </summary>
    public void FreezeTarget()
    {
        if (!SupportsFreeze || IsFrozen) return;
        IsFrozen = true;

        if (hazardColliders != null)
        {
            if (preFreezeColliderStates == null || preFreezeColliderStates.Length != hazardColliders.Length)
                preFreezeColliderStates = new bool[hazardColliders.Length];

            for (int i = 0; i < hazardColliders.Length; i++)
            {
                if (hazardColliders[i] != null)
                {
                    preFreezeColliderStates[i] = hazardColliders[i].enabled;
                    hazardColliders[i].enabled = false;
                }
            }
        }

        if (hazardAnimator != null)
        {
            preFreezeAnimatorEnabled = hazardAnimator.enabled;
            hazardAnimator.enabled = false;
        }

        if (hazardBody != null)
        {
            preFreezeLinearVelocity = hazardBody.linearVelocity;
            preFreezeAngularVelocity = hazardBody.angularVelocity;
            hazardBody.linearVelocity = Vector2.zero;
            hazardBody.angularVelocity = 0f;
        }

        pausedBehaviours.Clear();
        MonoBehaviour[] behaviours = GetComponents<MonoBehaviour>();
        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour b = behaviours[i];
            if (b == null || b == this || !b.enabled) continue;
            b.enabled = false;
            pausedBehaviours.Add(b);
        }

        if (spriteRenderers != null && originalSpriteColors != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                {
                    Color baseCol = originalSpriteColors[i];
                    spriteRenderers[i].color = new Color(
                        baseCol.r * FrozenTintColor.r,
                        baseCol.g * FrozenTintColor.g,
                        baseCol.b * FrozenTintColor.b,
                        baseCol.a);
                }
            }
        }
    }

    /// <summary>
    /// Restores the trap or enemy to its previous state prior to being frozen.
    /// </summary>
    public void UnfreezeTarget()
    {
        if (!IsFrozen) return;
        IsFrozen = false;

        for (int i = 0; i < pausedBehaviours.Count; i++)
        {
            if (pausedBehaviours[i] != null)
                pausedBehaviours[i].enabled = true;
        }
        pausedBehaviours.Clear();

        if (hazardAnimator != null)
        {
            hazardAnimator.enabled = preFreezeAnimatorEnabled;
        }

        if (hazardBody != null)
        {
            hazardBody.linearVelocity = preFreezeLinearVelocity;
            hazardBody.angularVelocity = preFreezeAngularVelocity;
        }

        if (spriteRenderers != null && originalSpriteColors != null)
        {
            for (int i = 0; i < spriteRenderers.Length; i++)
            {
                if (spriteRenderers[i] != null)
                    spriteRenderers[i].color = originalSpriteColors[i];
            }
        }

        if (!IsBroken && hazardColliders != null && preFreezeColliderStates != null)
        {
            int count = Mathf.Min(hazardColliders.Length, preFreezeColliderStates.Length);
            for (int i = 0; i < count; i++)
            {
                if (hazardColliders[i] != null)
                    hazardColliders[i].enabled = preFreezeColliderStates[i];
            }
        }

        CheckOverlappingPlayer();
    }

    /// <summary>
    /// Restores the hazard for a level reset.
    /// </summary>
    public void ResetHazard()
    {
        if (IsFrozen)
            UnfreezeTarget();

        IsBroken = false;

        if (hasInitialPosition)
        {
            transform.localPosition = initialLocalPosition;
            if (hazardBody != null)
                hazardBody.position = transform.position;
        }

        if (hazardColliders != null)
        {
            for (int i = 0; i < hazardColliders.Length; i++)
            {
                if (hazardColliders[i] != null)
                    hazardColliders[i].enabled = true;
            }
        }

        if (intactVisual != null)
        {
            intactVisual.SetActive(true);
        }
        else if (hazardRenderers != null)
        {
            for (int i = 0; i < hazardRenderers.Length; i++)
            {
                if (hazardRenderers[i] != null)
                    hazardRenderers[i].enabled = true;
            }
        }
    }
}
