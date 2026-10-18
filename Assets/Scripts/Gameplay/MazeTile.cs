using UnityEngine;

/// <summary>
/// Base class for 32x32-cell floor/wall interactables the player slides
/// over or into (brake tiles, wedges, brittle floors…).
///
/// A MazeTile owns a trigger Collider2D covering its cell. When the dashing
/// player enters the cell, <see cref="OnPlayerEnter"/> is invoked once per
/// entry. Tiles are discovered and reset by the parent <see cref="LevelRoot"/>,
/// so they must live inside the layout prefab hierarchy.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public abstract class MazeTile : MonoBehaviour
{
    [Tooltip("Grid cell size the tile covers (1 unit = 32 px at 32 PPU). Used to snap effects to the tile centre.")]
    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    /// <summary>World-space centre of this tile's cell.</summary>
    public Vector2 CellCenter => transform.position;

    /// <summary>Configured cell size (1 unit at 32 PPU).</summary>
    public float CellSize => cellSize;

    protected virtual void Awake()
    {
        Collider2D col = GetComponent<Collider2D>();
        if (!col.isTrigger)
        {
            Debug.LogWarning($"[MazeTile] '{name}': Collider2D must be a trigger. Setting isTrigger = true.", this);
            col.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null || !player.IsMoving) return;
        OnPlayerEnter(player);
    }

    /// <summary>Called once when the sliding player enters this tile.</summary>
    protected abstract void OnPlayerEnter(PlayerMovement player);

    /// <summary>Restores the tile to its initial state (called by LevelRoot.ResetAll).</summary>
    public virtual void ResetTile() { }
}
