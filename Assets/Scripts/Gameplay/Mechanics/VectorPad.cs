using UnityEngine;

/// <summary>
/// Vector Pad: a floor directional pad that redirects Subject G-0's active slide
/// along a configured cardinal direction upon reaching the cell centre, preserving
/// accumulated momentum (<see cref="PlayerMovement.TilesTraveled"/>).
///
/// Solid-bound: never phases through walls, only activates while sliding, and
/// participates in <see cref="PlayerMovement"/>'s infinite-loop guard.
/// </summary>
public class VectorPad : MazeTile, ICellInteraction
{
    [Header("Direction")]
    [SerializeField] private CardinalDirection direction = CardinalDirection.Right;

    [Header("Visuals (Optional)")]
    [SerializeField] private Transform directionalVisual;
    [SerializeField] private bool autoRotateVisual = true;

    public CardinalDirection Direction => direction;
    public Vector2 OutgoingVector => CardinalDirectionUtil.ToVector2(direction);
    public int Priority => CellInteractionPriority.VectorPad;

    protected override void Awake()
    {
        base.Awake();
        ApplyVisualRotation();
    }

    private void OnValidate()
    {
        ApplyVisualRotation();
    }

    public void SetDirection(CardinalDirection newDirection)
    {
        direction = newDirection;
        ApplyVisualRotation();
    }

    private void ApplyVisualRotation()
    {
        if (!autoRotateVisual || directionalVisual == null) return;
        directionalVisual.localRotation = Quaternion.Euler(0f, 0f, CardinalDirectionUtil.ToZAngleDegrees(direction));
    }

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        Vector2 outDir = OutgoingVector;
        Vector2 inDir = CardinalDirectionUtil.SnapToCardinal(incomingDirection);

        // Already moving along this pad's direction: keep sliding without burning a redirect count.
        if (inDir == outDir)
            return CellOutcome.Continue();

        RaisePlayerInteracted(InteractionKind.Deflect, outDir);
        return CellOutcome.Redirect(outDir);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector3 center = transform.position;
        Vector3 dir3 = (Vector3)OutgoingVector;
        Gizmos.color = new Color(0.2f, 0.92f, 1f, 0.85f);
        Gizmos.DrawLine(center - dir3 * 0.25f, center + dir3 * 0.35f);
        Vector3 tip = center + dir3 * 0.35f;
        Vector3 perp = new Vector3(-dir3.y, dir3.x, 0f) * 0.14f;
        Gizmos.DrawLine(tip, tip - dir3 * 0.16f + perp);
        Gizmos.DrawLine(tip, tip - dir3 * 0.16f - perp);
    }
#endif
}

