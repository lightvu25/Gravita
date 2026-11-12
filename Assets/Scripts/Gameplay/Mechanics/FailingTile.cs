using System;
using UnityEngine;

public enum FailingTileState
{
    Intact     = 0,
    Warning    = 1,
    Collapsing = 2,
    Collapsed  = 3,
}

/// <summary>
/// Failing Tile: structural station floor that destabilizes when Subject G-0 enters it
/// (or when triggered by a <see cref="BreachSequencer"/> event via <see cref="ISwitchable"/>).
///
/// Approved rules (D3):
///   - Arms on enter -> Warning (0.6 s) -> Collapsing (0.3 s) -> Collapsed (lethal pit).
///   - Driven strictly by <see cref="LevelClock"/> so delayed collapse can never fire
///     after death, level restart, or scene teardown.
/// </summary>
public class FailingTile : MazeTile, ICellInteraction, ILevelClockListener, ISwitchable
{
    public event Action<FailingTileState> OnStateChanged;

    [Header("Collapse Timing")]
    [SerializeField, Min(0.05f)] private float warningDuration = 0.6f;
    [SerializeField, Min(0.05f)] private float collapseDuration = 0.3f;

    [Header("Lethal Pit & Visuals (Pre-wired on Prefab)")]
    [Tooltip("Child Hazard component enabled when the tile reaches Collapsed state.")]
    [SerializeField] private Hazard collapsedPitHazard;

    [SerializeField] private GameObject intactVisual;
    [SerializeField] private GameObject warningVisual;
    [SerializeField] private GameObject collapsingVisual;
    [SerializeField] private GameObject collapsedVisual;

    [SerializeField] private SpriteRenderer fallbackSpriteRenderer;
    [SerializeField] private Color intactTint = new Color(0.55f, 0.72f, 0.85f, 0.9f);
    [SerializeField] private Color warningTint = new Color(1f, 0.72f, 0.18f, 1f);
    [SerializeField] private Color collapsingTint = new Color(1f, 0.38f, 0.15f, 0.95f);
    [SerializeField] private Color collapsedTint = new Color(0.85f, 0.12f, 0.22f, 1f);

    public int Priority => CellInteractionPriority.FailingTile;
    public FailingTileState State { get; private set; } = FailingTileState.Intact;
    public bool IsArmed => isArmed;
    public bool IsPowered => isArmed;

    private LevelRoot levelRoot;
    private bool isArmed;
    private float armedAtClockTime;
    private float latestClockTime;

    protected override void Awake()
    {
        base.Awake();
        levelRoot = GetComponentInParent<LevelRoot>();
        if (fallbackSpriteRenderer == null)
            fallbackSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        ApplyState(FailingTileState.Intact, force: true);
    }

    public static FailingTileState EvaluateState(
        bool armed,
        float elapsedSeconds,
        float warningSeconds,
        float collapseSeconds)
    {
        if (!armed)
            return FailingTileState.Intact;

        const float BoundaryEpsilon = 0.0001f;
        float safeWarn = Mathf.Max(0f, warningSeconds);
        float safeCollapse = Mathf.Max(0f, collapseSeconds);

        if (elapsedSeconds + BoundaryEpsilon < safeWarn)
            return FailingTileState.Warning;

        if (elapsedSeconds + BoundaryEpsilon < safeWarn + safeCollapse)
            return FailingTileState.Collapsing;

        return FailingTileState.Collapsed;
    }

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        if (!isArmed && State == FailingTileState.Intact)
        {
            ArmNow();
        }
        return CellOutcome.Continue();
    }

    /// <summary>Arms the collapse countdown starting at the current <see cref="LevelClock"/> time.</summary>
    public void ArmNow()
    {
        if (isArmed) return;

        if (levelRoot == null)
            levelRoot = GetComponentInParent<LevelRoot>();

        float now = levelRoot != null ? levelRoot.Clock.Time : latestClockTime;
        isArmed = true;
        armedAtClockTime = now;
        RaisePlayerInteracted(InteractionKind.Crack, Vector2.zero);
        ApplyState(EvaluateState(true, 0f, warningDuration, collapseDuration), force: false);
    }

    public void SetPowered(bool powered)
    {
        if (powered)
            ArmNow();
        else
            ResetTile();
    }

    public void TogglePowered()
    {
        SetPowered(!isArmed);
    }

    public void ResetSwitchState()
    {
        ResetTile();
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        latestClockTime = levelTime;
        if (!isArmed || State == FailingTileState.Collapsed)
            return;

        float elapsed = Mathf.Max(0f, levelTime - armedAtClockTime);
        FailingTileState next = EvaluateState(true, elapsed, warningDuration, collapseDuration);
        ApplyState(next, force: false);
    }

    public void OnLevelClockReset()
    {
        ResetTile();
    }

    public override void ResetTile()
    {
        isArmed = false;
        armedAtClockTime = 0f;
        latestClockTime = 0f;
        ApplyState(FailingTileState.Intact, force: true);
    }

    private void ApplyState(FailingTileState next, bool force)
    {
        if (!force && State == next)
            return;

        State = next;

        bool collapsed = next == FailingTileState.Collapsed;
        if (collapsedPitHazard != null)
        {
            collapsedPitHazard.gameObject.SetActive(collapsed);
            if (collapsed)
            {
                collapsedPitHazard.ResetHazard();
                RaisePlayerInteracted(InteractionKind.Break, Vector2.zero);
                collapsedPitHazard.CheckOverlappingPlayer();
            }
        }

        if (intactVisual != null) intactVisual.SetActive(next == FailingTileState.Intact);
        if (warningVisual != null) warningVisual.SetActive(next == FailingTileState.Warning);
        if (collapsingVisual != null) collapsingVisual.SetActive(next == FailingTileState.Collapsing);
        if (collapsedVisual != null) collapsedVisual.SetActive(collapsed);

        if (fallbackSpriteRenderer != null)
        {
            fallbackSpriteRenderer.color = next switch
            {
                FailingTileState.Warning    => warningTint,
                FailingTileState.Collapsing => collapsingTint,
                FailingTileState.Collapsed  => collapsedTint,
                _                           => intactTint,
            };
        }

        OnStateChanged?.Invoke(next);
    }
}
