using UnityEngine;

/// <summary>
/// Brittle Flagstone (World 1 signature): safely walkable at Tier 1 (Drift),
/// but shatters permanently into an impassable pit when crossed at Tier 2+.
/// The player that broke it still finishes the current dash — the pit blocks
/// any FUTURE dash through this cell.
/// </summary>
public class BrittleFloor : MazeTile
{
    [Tooltip("Crossing at or above this tier shatters the floor.")]
    [SerializeField] private VelocityTier shatterTier = VelocityTier.Cruise;

    [Tooltip("Optional floor visual disabled when the floor shatters.")]
    [SerializeField] private GameObject intactVisual;

    [Tooltip("Solid collider enabled after shattering so the pit blocks later dashes. Auto-created if left empty.")]
    [SerializeField] private Collider2D pitCollider;

    public bool IsShattered { get; private set; }

    public event System.Action<BrittleFloor> OnShattered;

    protected override void Awake()
    {
        base.Awake();
        if (pitCollider == null)
        {
            // Second collider on this GameObject: solid (non-trigger) so it
            // blocks dashes once enabled, unlike the trigger used for entry.
            var pit = gameObject.AddComponent<BoxCollider2D>();
            pit.isTrigger = false;
            pitCollider = pit;
        }
        pitCollider.enabled = false;
    }

    protected override void OnPlayerEnter(PlayerMovement player)
    {
        if (IsShattered || player.CurrentTier < shatterTier) return;
        Shatter();
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        // The pit becomes solid only once the breaker has left the cell —
        // enabling it while the player is still inside would stop the dash.
        if (!IsShattered || pitCollider == null || pitCollider.enabled) return;
        if (other.GetComponentInParent<PlayerMovement>() == null) return;
        pitCollider.enabled = true;
    }

    private void Shatter()
    {
        IsShattered = true;
        if (intactVisual != null) intactVisual.SetActive(false);
        OnShattered?.Invoke(this);
    }

    public override void ResetTile()
    {
        IsShattered = false;
        if (intactVisual != null) intactVisual.SetActive(true);
        if (pitCollider != null) pitCollider.enabled = false;
    }
}
