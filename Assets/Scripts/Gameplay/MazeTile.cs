using System;
using UnityEngine;

/// <summary>
/// Base class for 32x32-cell floor/wall interactables the player slides
/// over or into (gravity blocks, brake tiles, wedges, vector pads, launchers,
/// switches, brittle/failing floors…).
///
/// Subclasses implementing <see cref="ICellInteraction"/> are resolved
/// deterministically by <see cref="PlayerMovement"/> at the exact cell centre.
/// Subclasses that do not implement <see cref="ICellInteraction"/> (e.g. <see cref="BrittleFloor"/>)
/// receive <see cref="OnPlayerEnter"/> on trigger entry as before.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class MazeTile : MonoBehaviour, IPlayerInteractable
{
    [Tooltip("Grid cell size the tile covers (1 unit = 32 px at 32 PPU). Used to snap effects to the tile centre.")]
    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    /// <summary>World-space centre of this tile's cell.</summary>
    public Vector2 CellCenter => transform.position;

    /// <summary>Configured cell size (1 unit at 32 PPU).</summary>
    public float CellSize => cellSize;

    /// <summary>Convenience bridge for <see cref="ICellInteraction"/> subclasses.</summary>
    public Vector2 InteractionCellCenter => CellCenter;

    /// <summary>Convenience bridge for <see cref="ICellInteraction"/> subclasses.</summary>
    public float InteractionCellSize => cellSize;

    /// <summary>Fired when the player interacts with this tile (<see cref="IPlayerInteractable"/>).</summary>
    public event Action<InteractionContext> OnPlayerInteracted;

    /// <summary>
    /// Global presentation notification fired alongside <see cref="OnPlayerInteracted"/>.
    /// Used by centralized feedback listeners (such as <see cref="GameplayHapticsFeedback"/>)
    /// without coupling core tile logic to presentation services.
    /// </summary>
    public static event Action<MazeTile, InteractionContext> OnAnyPlayerInteracted;

    private Renderer cachedRenderer;
    private Collider2D cachedCollider;

    protected virtual void Awake()
    {
        cachedCollider = GetComponent<Collider2D>();
        cachedRenderer = GetComponentInChildren<Renderer>();

        if (cachedCollider != null && !cachedCollider.isTrigger)
        {
            Debug.LogWarning($"[MazeTile] '{name}': Collider2D must be a trigger. Setting isTrigger = true.", this);
            cachedCollider.isTrigger = true;
        }
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

    protected void RaisePlayerInteracted(InteractionKind kind, Vector2 direction = default)
    {
        InteractionContext context = new InteractionContext(kind, ResolveVisualWorldCenter(), direction);
        OnPlayerInteracted?.Invoke(context);
        OnAnyPlayerInteracted?.Invoke(this, context);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null || player.IsPhasing) return;

        // Deterministic centre-crossing tiles are driven exclusively by PlayerMovement.
        if (this is ICellInteraction) return;

        if (!player.IsMoving) return;
        OnPlayerEnter(player);
    }

    private void OnTriggerExit2D(Collider2D other)
    {
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null) return;
        OnPlayerExit(player);
    }

    /// <summary>Called once when the sliding player enters a non-ICellInteraction tile.</summary>
    protected virtual void OnPlayerEnter(PlayerMovement player) { }

    /// <summary>Called when the player collider exits this tile's trigger volume.</summary>
    protected virtual void OnPlayerExit(PlayerMovement player) { }

    /// <summary>Restores the tile to its initial state (called by LevelRoot.ResetAll).</summary>
    public virtual void ResetTile() { }
}
