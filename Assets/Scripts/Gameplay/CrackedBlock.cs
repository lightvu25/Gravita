using System;
using UnityEngine;

/// <summary>
/// Cracked Masonry block: a solid obstacle that behaves as a normal wall
/// for Tier 1–2 impacts, but is destroyed instantly by a Tier 3 Kinetic Ram
/// dash (the player keeps moving through the rubble).
///
/// The collider sits on the wall layer so PlayerMovement's cast detects it
/// as a stopper; the tier check happens at the moment of impact.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class CrackedBlock : MonoBehaviour, ICrushable, IPlayerInteractable
{
    /// <summary>Fired whenever any CrackedBlock is smashed across the active level.</summary>
    public static event Action<CrackedBlock> OnAnyBroken;

    [Tooltip("Minimum velocity tier needed to smash through this block.")]
    [SerializeField] private VelocityTier requiredTier = VelocityTier.KineticRam;

    [Tooltip("Optional visual swapped off when the block breaks.")]
    [SerializeField] private GameObject intactVisual;

    public bool IsBroken { get; private set; }
    public Vector3 LastBreakWorldPosition { get; private set; }
    public bool HasCustomBreakEffect => interactionEffectPlayer != null && interactionEffectPlayer.HasConfiguredEffects;

    /// <summary>Fired when the block is smashed by a Kinetic Ram dash.</summary>
    public event Action<CrackedBlock> OnBroken;

    /// <summary>Fired when the player interacts with (breaks) this block (<see cref="IPlayerInteractable"/>).</summary>
    public event Action<InteractionContext> OnPlayerInteracted;

    private Renderer cachedRenderer;
    private Collider2D cachedCollider;
    private InteractionEffectPlayer interactionEffectPlayer;
    private Vector2 lastImpactDirection;

    private void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
        cachedRenderer = GetComponentInChildren<Renderer>();
        interactionEffectPlayer = GetComponent<InteractionEffectPlayer>();

        if (cachedCollider != null && cachedCollider.isTrigger)
            Debug.LogWarning($"[CrackedBlock] '{name}': Collider2D must be solid (isTrigger = false).", this);

        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0 && gameObject.layer == 0)
        {
            Debug.LogWarning($"[CrackedBlock] '{name}': GameObject was on Default layer; moving to 'Wall' layer ({wallLayer}) so PlayerMovement detects it.", this);
            gameObject.layer = wallLayer;
        }
    }

    public bool TryCrush(PlayerMovement player)
    {
        if (IsBroken) return true;
        if (player == null || player.CurrentTier < requiredTier) return false;

        lastImpactDirection = player.CurrentDirection;
        Break();
        return true;
    }

    public Vector3 ResolveVisualWorldCenter()
    {
        if (cachedRenderer == null)
            cachedRenderer = GetComponentInChildren<Renderer>();

        if (cachedRenderer != null && cachedRenderer.enabled && cachedRenderer.gameObject.activeInHierarchy)
            return cachedRenderer.bounds.center;

        if (cachedCollider != null && cachedCollider.enabled && cachedCollider.gameObject.activeInHierarchy)
            return cachedCollider.bounds.center;

        return transform.position;
    }

    /// <summary>Smashes the block: colliders disabled, visual removed.</summary>
    public void Break()
    {
        if (IsBroken) return;
        IsBroken = true;

        Vector3 breakCenter = ResolveVisualWorldCenter();
        LastBreakWorldPosition = breakCenter;

        OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Break, breakCenter, lastImpactDirection));

        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        if (intactVisual != null) intactVisual.SetActive(false);
        else if (TryGetComponent(out Renderer r)) r.enabled = false;

        OnBroken?.Invoke(this);
        OnAnyBroken?.Invoke(this);
    }

    /// <summary>Restores the block for a level reset.</summary>
    public void ResetBlock()
    {
        IsBroken = false;
        lastImpactDirection = Vector2.zero;
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = true;
        if (intactVisual != null) intactVisual.SetActive(true);
        else if (TryGetComponent(out Renderer r)) r.enabled = true;
    }
}
