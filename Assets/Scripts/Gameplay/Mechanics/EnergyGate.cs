using System;
using UnityEngine;

/// <summary>
/// Energy Gate: a cyber security barrier with explicit Open and Closed states.
///
/// Rules:
///   - Closed gate enables a solid <c>Wall</c>-layer collider that stops sliding movement.
///   - Open gate disables the solid collider and allows traversal.
///   - Controlled via <see cref="ISwitchable"/> (by <see cref="CircuitSwitch"/> or <see cref="BreachSequencer"/>).
///   - Deferred Close (D4): if commanded to close while Subject G-0 overlaps the gate cell,
///     the solid collider remains disabled until the player leaves the cell.
/// </summary>
public class EnergyGate : MonoBehaviour, ISwitchable, ILevelClockListener, IPlayerInteractable
{
    public event Action<bool> OnGateOpenStateChanged;
    public event Action<InteractionContext> OnPlayerInteracted;

    [Header("Gate State")]
    [Tooltip("Initial state when the level starts or resets.")]
    [SerializeField] private bool defaultOpen = false;

    [Tooltip("If false (default), Powered = Open. If true, Powered = Closed.")]
    [SerializeField] private bool invertPower = false;

    [Header("Collider & Visuals (Pre-wired on Prefab)")]
    [Tooltip("Non-trigger Collider2D on the Wall layer enabled when Closed.")]
    [SerializeField] private Collider2D solidBarrierCollider;

    [SerializeField] private GameObject closedVisual;
    [SerializeField] private GameObject openVisual;

    [Tooltip("Optional SpriteRenderer tinted/faded to reflect Open vs Closed on single-sprite placeholder prefabs.")]
    [SerializeField] private SpriteRenderer fallbackSpriteRenderer;
    [SerializeField] private Color closedTint = new Color(1f, 0.78f, 0.18f, 1f);
    [SerializeField] private Color deferredCloseTint = new Color(1f, 0.5f, 0.15f, 0.55f);
    [SerializeField] private Color openTint = new Color(0.25f, 0.95f, 0.85f, 0.22f);

    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    public bool IsPowered { get; private set; }
    public bool IsLogicalOpen => invertPower ? !IsPowered : IsPowered;
    public bool IsPendingDeferredClose => pendingDeferredClose;
    public bool IsPassable => IsLogicalOpen || pendingDeferredClose;

    private static readonly Collider2D[] overlapBuffer = new Collider2D[8];
    private bool pendingDeferredClose;

    private void Awake()
    {
        if (solidBarrierCollider == null)
            solidBarrierCollider = GetComponent<Collider2D>();

        if (fallbackSpriteRenderer == null)
            fallbackSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0 && solidBarrierCollider != null && solidBarrierCollider.gameObject.layer != wallLayer)
        {
            solidBarrierCollider.gameObject.layer = wallLayer;
        }

        if (solidBarrierCollider != null)
            solidBarrierCollider.isTrigger = false;

        ResetSwitchState();
    }

    public void SetPowered(bool powered)
    {
        bool previousLogicalOpen = IsLogicalOpen;
        IsPowered = powered;
        bool targetOpen = IsLogicalOpen;

        if (targetOpen)
        {
            pendingDeferredClose = false;
            ApplyPhysicalState(open: true);
        }
        else
        {
            if (IsPlayerOverlappingGateCell())
            {
                pendingDeferredClose = true;
                ApplyPhysicalState(open: true);
            }
            else
            {
                pendingDeferredClose = false;
                ApplyPhysicalState(open: false);
            }
        }

        if (previousLogicalOpen != targetOpen)
        {
            OnGateOpenStateChanged?.Invoke(targetOpen);
            OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Switch, transform.position, Vector2.zero));
        }
    }

    public void TogglePowered()
    {
        SetPowered(!IsPowered);
    }

    public void ResetSwitchState()
    {
        pendingDeferredClose = false;
        IsPowered = invertPower ? !defaultOpen : defaultOpen;
        ApplyPhysicalState(open: defaultOpen);
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        if (!pendingDeferredClose) return;

        if (!IsPlayerOverlappingGateCell())
        {
            pendingDeferredClose = false;
            ApplyPhysicalState(open: false);
        }
    }

    public void OnLevelClockReset()
    {
        ResetSwitchState();
    }

    private void ApplyPhysicalState(bool open)
    {
        if (solidBarrierCollider != null)
            solidBarrierCollider.enabled = !open;

        if (closedVisual != null)
            closedVisual.SetActive(!open);

        if (openVisual != null)
            openVisual.SetActive(open);

        if (fallbackSpriteRenderer != null)
        {
            if (!open)
                fallbackSpriteRenderer.color = closedTint;
            else if (pendingDeferredClose)
                fallbackSpriteRenderer.color = deferredCloseTint;
            else
                fallbackSpriteRenderer.color = openTint;
        }

        Physics2D.SyncTransforms();
    }

    private bool IsPlayerOverlappingGateCell()
    {
        Vector2 center = transform.position;
        Vector2 probe = Vector2.one * (cellSize * 0.8f);
        int count = Physics2D.OverlapBox(center, probe, 0f, ContactFilter2D.noFilter, overlapBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = overlapBuffer[i];
            if (col == null || col == solidBarrierCollider) continue;
            if (col.GetComponentInParent<PlayerMovement>() != null)
                return true;
        }
        return false;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        bool open = Application.isPlaying ? IsPassable : defaultOpen;
        Gizmos.color = open ? new Color(0.2f, 1f, 0.6f, 0.5f) : new Color(1f, 0.75f, 0.15f, 0.85f);
        Gizmos.DrawWireCube(transform.position, Vector3.one * (cellSize * 0.9f));
    }
#endif
}

