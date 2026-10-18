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
public class CrackedBlock : MonoBehaviour, ICrushable
{
    [Tooltip("Minimum velocity tier needed to smash through this block.")]
    [SerializeField] private VelocityTier requiredTier = VelocityTier.KineticRam;

    [Tooltip("Optional visual swapped off when the block breaks.")]
    [SerializeField] private GameObject intactVisual;

    public bool IsBroken { get; private set; }

    /// <summary>Fired when the block is smashed by a Kinetic Ram dash.</summary>
    public event System.Action<CrackedBlock> OnBroken;

    private void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (col.isTrigger)
            Debug.LogWarning($"[CrackedBlock] '{name}': Collider2D must be solid (isTrigger = false).", this);
    }

    public bool TryCrush(PlayerMovement player)
    {
        if (IsBroken) return true;
        if (player == null || player.CurrentTier < requiredTier) return false;

        Break();
        return true;
    }

    /// <summary>Smashes the block: colliders disabled, visual removed.</summary>
    public void Break()
    {
        if (IsBroken) return;
        IsBroken = true;

        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = false;

        if (intactVisual != null) intactVisual.SetActive(false);
        else if (TryGetComponent(out Renderer r)) r.enabled = false;

        OnBroken?.Invoke(this);
    }

    /// <summary>Restores the block for a level reset.</summary>
    public void ResetBlock()
    {
        IsBroken = false;
        foreach (Collider2D col in GetComponentsInChildren<Collider2D>())
            col.enabled = true;
        if (intactVisual != null) intactVisual.SetActive(true);
        else if (TryGetComponent(out Renderer r)) r.enabled = true;
    }
}
