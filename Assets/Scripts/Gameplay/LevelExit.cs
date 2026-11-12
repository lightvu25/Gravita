using System;
using UnityEngine;

/// <summary>
/// Trigger volume marking the exit of a maze level. When the player slides
/// into it, the parent <see cref="LevelRoot"/> is notified and the
/// ReachExit objective completes.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class LevelExit : MonoBehaviour, IPlayerInteractable
{
    [Tooltip("Optional id if a layout ever needs multiple exits.")]
    [SerializeField] private string exitId;

    public string ExitId => exitId;

    /// <summary>Fired when the player reaches this exit (<see cref="IPlayerInteractable"/>).</summary>
    public event Action<InteractionContext> OnPlayerInteracted;

    private LevelRoot levelRoot;
    private Renderer cachedRenderer;
    private Collider2D cachedCollider;
    private bool triggered;

    private void Awake()
    {
        levelRoot = GetComponentInParent<LevelRoot>();
        cachedRenderer = GetComponentInChildren<Renderer>();
        cachedCollider = GetComponent<Collider2D>();

        if (cachedCollider != null && !cachedCollider.isTrigger)
        {
            Debug.LogWarning($"[LevelExit] '{name}': Collider2D must be a trigger. Setting isTrigger = true.", this);
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

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (triggered) return;
        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null) return;

        triggered = true;
        OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Exit, ResolveVisualWorldCenter(), player.CurrentDirection));
        levelRoot?.NotifyExitReached(this);
    }

    /// <summary>Re-arms the exit for a level reset.</summary>
    public void ResetExit()
    {
        triggered = false;
    }
}
