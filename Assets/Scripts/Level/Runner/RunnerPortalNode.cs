using System;
using UnityEngine;

public enum RunnerPortalRole
{
    Start = 0,
    Goal  = 1,
}

/// <summary>
/// Authoring &amp; runtime portal marker (<c>StartNode</c> or <c>GoalNode</c>) on a Maze Runner room.
/// Triggers continuous room-to-room traversal when slid into by <see cref="PlayerMovement"/>.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class RunnerPortalNode : MonoBehaviour
{
    [SerializeField] private RunnerPortalRole role = RunnerPortalRole.Start;
    [SerializeField] private CardinalDirection portalDirection = CardinalDirection.Up;

    public RunnerPortalRole Role => role;
    public CardinalDirection PortalDirection => portalDirection;
    public Vector3Int GridCell => new Vector3Int(
        Mathf.FloorToInt(transform.position.x),
        Mathf.FloorToInt(transform.position.y),
        0);

    public event Action<RunnerPortalNode, PlayerMovement> OnPlayerEnteredPortal;

    private Collider2D portalCollider;
    private bool consumed;

    private void Awake()
    {
        EnsureTriggerCollider();
    }

    public void Configure(RunnerPortalRole portalRole, CardinalDirection direction)
    {
        role = portalRole;
        portalDirection = direction;
        consumed = false;
        EnsureTriggerCollider();
    }

    public void ResetPortal()
    {
        consumed = false;
        EnsureTriggerCollider();
        if (portalCollider != null)
            portalCollider.enabled = true;
    }

    public void MarkConsumed()
    {
        consumed = true;
    }

    public void NotifyPlayerEntered(PlayerMovement player)
    {
        if (consumed || player == null)
            return;

        consumed = true;
        OnPlayerEnteredPortal?.Invoke(this, player);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (consumed || other == null)
            return;

        PlayerMovement player = other.GetComponentInParent<PlayerMovement>();
        if (player == null)
            return;

        NotifyPlayerEntered(player);
    }

    private void EnsureTriggerCollider()
    {
        if (portalCollider == null)
            portalCollider = GetComponent<Collider2D>();

        if (portalCollider != null)
            portalCollider.isTrigger = true;
    }
}
