using System;
using UnityEngine;

public enum SwitchMode
{
    /// <summary>Flips each target's powered state on every valid tile entry.</summary>
    Toggle = 0,

    /// <summary>Flips each target's powered state once per level run, then stays locked.</summary>
    OneShotToggle = 1,

    /// <summary>Powers targets ON once per level run.</summary>
    OneShotPowerOn = 2,

    /// <summary>Powers targets OFF once per level run.</summary>
    OneShotPowerOff = 3,

    /// <summary>Powers targets ON while the player occupies the switch cell, OFF upon exit.</summary>
    Momentary = 4,
}

/// <summary>
/// Circuit Switch: floor control tile that drives one or more Inspector-wired
/// <see cref="ISwitchable"/> targets (<see cref="EnergyGate"/>, <see cref="HazardActivation"/>).
///
/// Resolves at <see cref="CellInteractionPriority.Switch"/> (100) — BEFORE movement tiles —
/// and guards against duplicate callbacks within a single occupancy.
/// </summary>
public class CircuitSwitch : MazeTile, ICellInteraction
{
    public event Action<bool> OnSwitchStateChanged;

    [Header("Switch Behaviour")]
    [SerializeField] private SwitchMode mode = SwitchMode.Toggle;

    [Tooltip("Explicitly wired ISwitchable targets (EnergyGate, HazardActivation, etc.).")]
    [SerializeField] private MonoBehaviour[] targets;

    [Header("Visuals (Optional)")]
    [SerializeField] private SpriteRenderer fallbackSpriteRenderer;
    [SerializeField] private Color unpressedTint = new Color(1f, 0.85f, 0.2f, 1f);
    [SerializeField] private Color activeTint = new Color(0.25f, 1f, 0.65f, 1f);

    public int Priority => CellInteractionPriority.Switch;
    public SwitchMode Mode => mode;
    public bool IsActivatedVisualState { get; private set; }

    private bool disarmedWhileOccupying;
    private bool oneShotConsumed;

    protected override void Awake()
    {
        base.Awake();
        if (fallbackSpriteRenderer == null)
            fallbackSpriteRenderer = GetComponentInChildren<SpriteRenderer>();
        ValidateTargets();
        RefreshVisual();
    }

    private void OnValidate()
    {
        ValidateTargets();
    }

    private void ValidateTargets()
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            MonoBehaviour mb = targets[i];
            if (mb != null && !(mb is ISwitchable))
            {
                Debug.LogWarning($"[CircuitSwitch] '{name}': Target '{mb.name}' at index {i} does not implement ISwitchable.", this);
            }
        }
    }

    public MonoBehaviour[] RawTargets => targets;

    public bool BlocksEntryFrom(Vector2 incomingDirection) => false;

    public CellOutcome ResolveAtCenter(PlayerMovement player, Vector2 incomingDirection)
    {
        if (disarmedWhileOccupying)
            return CellOutcome.Continue();

        if (oneShotConsumed && (mode == SwitchMode.OneShotToggle || mode == SwitchMode.OneShotPowerOn || mode == SwitchMode.OneShotPowerOff))
            return CellOutcome.Continue();

        disarmedWhileOccupying = true;
        ExecuteSwitchActivation(incomingDirection);
        return CellOutcome.Continue();
    }

    /// <summary>Toggles or pulses this switch remotely (e.g. from Kinetic Surge shockwave).</summary>
    public bool TriggerRemotePulse(Vector2 impulseDirection)
    {
        if (oneShotConsumed && (mode == SwitchMode.OneShotToggle || mode == SwitchMode.OneShotPowerOn || mode == SwitchMode.OneShotPowerOff))
            return false;

        ExecuteSwitchActivation(impulseDirection);
        return true;
    }

    private void ExecuteSwitchActivation(Vector2 incomingDirection)
    {
        switch (mode)
        {
            case SwitchMode.Toggle:
                ApplyToTargets(toggle: true, powerValue: false);
                IsActivatedVisualState = !IsActivatedVisualState;
                break;

            case SwitchMode.OneShotToggle:
                oneShotConsumed = true;
                ApplyToTargets(toggle: true, powerValue: false);
                IsActivatedVisualState = true;
                break;

            case SwitchMode.OneShotPowerOn:
                oneShotConsumed = true;
                ApplyToTargets(toggle: false, powerValue: true);
                IsActivatedVisualState = true;
                break;

            case SwitchMode.OneShotPowerOff:
                oneShotConsumed = true;
                ApplyToTargets(toggle: false, powerValue: false);
                IsActivatedVisualState = true;
                break;

            case SwitchMode.Momentary:
                ApplyToTargets(toggle: false, powerValue: true);
                IsActivatedVisualState = true;
                break;
        }

        RefreshVisual();
        RaisePlayerInteracted(InteractionKind.Switch, incomingDirection);
        OnSwitchStateChanged?.Invoke(IsActivatedVisualState);
    }

    protected override void OnPlayerExit(PlayerMovement player)
    {
        disarmedWhileOccupying = false;

        if (mode == SwitchMode.Momentary && IsActivatedVisualState)
        {
            ApplyToTargets(toggle: false, powerValue: false);
            IsActivatedVisualState = false;
            RefreshVisual();
            OnSwitchStateChanged?.Invoke(false);
        }
    }

    public override void ResetTile()
    {
        disarmedWhileOccupying = false;
        oneShotConsumed = false;
        IsActivatedVisualState = false;
        RefreshVisual();
    }

    private void ApplyToTargets(bool toggle, bool powerValue)
    {
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] is ISwitchable switchable)
            {
                if (toggle)
                    switchable.TogglePowered();
                else
                    switchable.SetPowered(powerValue);
            }
        }
    }

    private void RefreshVisual()
    {
        if (fallbackSpriteRenderer != null)
            fallbackSpriteRenderer.color = IsActivatedVisualState ? activeTint : unpressedTint;
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, 0.9f, 0.2f, 0.75f);
        if (targets == null) return;
        for (int i = 0; i < targets.Length; i++)
        {
            if (targets[i] != null)
                Gizmos.DrawLine(transform.position, targets[i].transform.position);
        }
    }
#endif
}

