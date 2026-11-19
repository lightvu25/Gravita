using System;
using UnityEngine;

public enum HazardActivationMode
{
    /// <summary>Alternates deterministically: Inactive -> Warning -> Active -> repeat.</summary>
    TimedCycle = 0,

    /// <summary>Steps through a boolean array pattern (e.g. rolling electrical floor waves).</summary>
    StepPattern = 1,

    /// <summary>Directly controlled by a CircuitSwitch or BreachSequencer (Powered = Active).</summary>
    SwitchControlled = 2,

    /// <summary>Overloaded Floor "Skate" mode: safe when crossed at >= minimumSafeTier, lethal if slower or stopped.</summary>
    SkateVelocityGate = 3,
}

public enum HazardCyclePhase
{
    Inactive = 0,
    Warning  = 1,
    Active   = 2,
}

/// <summary>
/// Deterministic state controller for Pulse Lasers and Overloaded Floors.
/// Separates logical lethality from visual effects: animations/sprites observe <see cref="CurrentPhase"/>,
/// never the other way around. Driven strictly by <see cref="LevelClock"/>.
/// </summary>
[RequireComponent(typeof(Hazard))]
public class HazardActivation : MonoBehaviour, IHazardActivation, ISwitchable, ILevelClockListener
{
    public event Action<HazardCyclePhase> OnPhaseChanged;

    [Header("Mode")]
    [SerializeField] private HazardActivationMode mode = HazardActivationMode.TimedCycle;

    [Header("Power / Switch Integration")]
    [Tooltip("Whether this hazard starts powered when the level begins.")]
    [SerializeField] private bool poweredByDefault = true;

    [Tooltip("When true in SwitchControlled mode, Powered = Inactive instead of Active.")]
    [SerializeField] private bool invertSwitchPower = false;

    [Header("Timed Cycle Settings (Pulse Laser / Timed Floor)")]
    [SerializeField, Min(0.05f)] private float inactiveDuration = 1.4f;
    [SerializeField, Min(0f)] private float warningDuration = 0.5f;
    [SerializeField, Min(0.05f)] private float activeDuration = 1.1f;
    [SerializeField] private float phaseOffset = 0f;

    [Header("Step Pattern Settings (Floor Waves)")]
    [SerializeField] private bool[] patternSteps = { false, true };
    [SerializeField, Min(0.1f)] private float stepDuration = 0.8f;
    [SerializeField, Min(0f)] private float patternWarningLead = 0.3f;

    [Header("Skate Mode Settings (Overloaded Floor Skate)")]
    [Tooltip("Minimum velocity tier required to skate safely across this electrified floor.")]
    [SerializeField] private VelocityTier minimumSafeTier = VelocityTier.Cruise;

    [Header("Visuals (Optional — Purely Driven by Logical State)")]
    [SerializeField] private GameObject inactiveVisual;
    [SerializeField] private GameObject warningVisual;
    [SerializeField] private GameObject activeVisual;
    [SerializeField] private SpriteRenderer fallbackSpriteRenderer;
    [SerializeField] private Color inactiveTint = new Color(0.35f, 0.45f, 0.55f, 0.3f);
    [SerializeField] private Color warningTint = new Color(1f, 0.68f, 0.15f, 0.78f);
    [SerializeField] private Color activeTint = new Color(1f, 0.18f, 0.28f, 1f);
    [SerializeField] private Color skateTint = new Color(1f, 0.42f, 0.12f, 0.92f);

    public HazardActivationMode Mode => mode;
    public HazardCyclePhase CurrentPhase { get; private set; } = HazardCyclePhase.Inactive;
    public bool IsPowered { get; private set; }
    public VelocityTier MinimumSafeTier => minimumSafeTier;

    /// <summary>Seconds remaining until this hazard enters the Active phase (float.PositiveInfinity if not applicable).</summary>
    public float TimeUntilActive { get; private set; } = float.PositiveInfinity;

    public bool IsHazardGenerallyActive =>
        !isHaltedForDeath
        && IsPowered
        && (mode == HazardActivationMode.SkateVelocityGate || CurrentPhase == HazardCyclePhase.Active);

    private Hazard hazard;
    private float lastClockTime;
    private bool isHaltedForDeath;

    private void Awake()
    {
        hazard = GetComponent<Hazard>();
        if (fallbackSpriteRenderer == null)
            fallbackSpriteRenderer = GetComponentInChildren<SpriteRenderer>();

        IsPowered = poweredByDefault;
        EvaluateAndApply(0f, checkOverlapOnActivate: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Pure Deterministic Evaluators (Unit-Testable)
    // ─────────────────────────────────────────────────────────────

    public static HazardCyclePhase EvaluateTimedPhase(
        float time,
        float inactiveSeconds,
        float warningSeconds,
        float activeSeconds,
        float offsetSeconds,
        out float timeUntilActive)
    {
        float safeInactive = Mathf.Max(0f, inactiveSeconds);
        float safeWarning = Mathf.Max(0f, warningSeconds);
        float safeActive = Mathf.Max(0.001f, activeSeconds);
        float cycle = safeInactive + safeWarning + safeActive;

        float t = Mathf.Repeat(Mathf.Max(0f, time) + offsetSeconds, cycle);
        float activeStart = safeInactive + safeWarning;

        if (t < safeInactive)
        {
            timeUntilActive = activeStart - t;
            return HazardCyclePhase.Inactive;
        }

        if (t < activeStart)
        {
            timeUntilActive = activeStart - t;
            return HazardCyclePhase.Warning;
        }

        timeUntilActive = 0f;
        return HazardCyclePhase.Active;
    }

    public static HazardCyclePhase EvaluatePatternPhase(
        float time,
        bool[] steps,
        float stepSeconds,
        float warningLeadSeconds,
        float offsetSeconds,
        out float timeUntilActive)
    {
        timeUntilActive = float.PositiveInfinity;
        if (steps == null || steps.Length == 0)
            return HazardCyclePhase.Inactive;

        float safeStep = Mathf.Max(0.05f, stepSeconds);
        float total = steps.Length * safeStep;
        float t = Mathf.Repeat(Mathf.Max(0f, time) + offsetSeconds, total);

        int idx = Mathf.Clamp(Mathf.FloorToInt(t / safeStep), 0, steps.Length - 1);
        float timeInStep = t - idx * safeStep;
        float remainingInStep = safeStep - timeInStep;

        if (steps[idx])
        {
            timeUntilActive = 0f;
            return HazardCyclePhase.Active;
        }

        int nextIdx = (idx + 1) % steps.Length;
        if (steps[nextIdx])
        {
            timeUntilActive = remainingInStep;
            if (warningLeadSeconds > 0f && remainingInStep <= warningLeadSeconds)
                return HazardCyclePhase.Warning;
        }

        return HazardCyclePhase.Inactive;
    }

    public static bool EvaluateSkateLethality(bool isMoving, VelocityTier currentTier, VelocityTier minSafeTier)
    {
        if (!isMoving)
            return true;

        return currentTier < minSafeTier;
    }

    // ─────────────────────────────────────────────────────────────
    //  IHazardActivation / ISwitchable / ILevelClockListener
    // ─────────────────────────────────────────────────────────────

    public bool IsLethalTo(PlayerMovement player)
    {
        if (player == null || player.IsPhasing || !IsPowered)
            return false;

        if (mode == HazardActivationMode.SkateVelocityGate)
            return EvaluateSkateLethality(player.IsMoving, player.CurrentTier, minimumSafeTier);

        return CurrentPhase == HazardCyclePhase.Active;
    }

    public void SetPowered(bool powered)
    {
        IsPowered = powered;
        EvaluateAndApply(lastClockTime, checkOverlapOnActivate: true);
    }

    public void TogglePowered()
    {
        SetPowered(!IsPowered);
    }

    public void ResetSwitchState()
    {
        isHaltedForDeath = false;
        IsPowered = poweredByDefault;
        lastClockTime = 0f;
        EvaluateAndApply(0f, checkOverlapOnActivate: false);
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        if (isHaltedForDeath)
            return;

        lastClockTime = levelTime;
        EvaluateAndApply(levelTime, checkOverlapOnActivate: true);
    }

    public void OnLevelClockReset()
    {
        isHaltedForDeath = false;
        lastClockTime = 0f;
        IsPowered = poweredByDefault;
        EvaluateAndApply(0f, checkOverlapOnActivate: false);
    }

    public void EnterPlayerDeathIdle()
    {
        if (isHaltedForDeath)
            return;

        isHaltedForDeath = true;
        TimeUntilActive = float.PositiveInfinity;
        if (CurrentPhase != HazardCyclePhase.Inactive)
        {
            CurrentPhase = HazardCyclePhase.Inactive;
            OnPhaseChanged?.Invoke(HazardCyclePhase.Inactive);
        }
        ApplyVisualState(HazardCyclePhase.Inactive);
    }

    public void RestoreFromPlayerDeathIdle()
    {
        if (!isHaltedForDeath)
            return;

        isHaltedForDeath = false;
        EvaluateAndApply(lastClockTime, checkOverlapOnActivate: false);
    }

    private void EvaluateAndApply(float clockTime, bool checkOverlapOnActivate)
    {
        HazardCyclePhase previous = CurrentPhase;
        HazardCyclePhase next;
        float untilActive = float.PositiveInfinity;

        if (!IsPowered && mode != HazardActivationMode.SwitchControlled)
        {
            next = HazardCyclePhase.Inactive;
        }
        else
        {
            switch (mode)
            {
                case HazardActivationMode.TimedCycle:
                    next = EvaluateTimedPhase(clockTime, inactiveDuration, warningDuration, activeDuration, phaseOffset, out untilActive);
                    break;

                case HazardActivationMode.StepPattern:
                    next = EvaluatePatternPhase(clockTime, patternSteps, stepDuration, patternWarningLead, phaseOffset, out untilActive);
                    break;

                case HazardActivationMode.SwitchControlled:
                    bool active = invertSwitchPower ? !IsPowered : IsPowered;
                    next = active ? HazardCyclePhase.Active : HazardCyclePhase.Inactive;
                    untilActive = active ? 0f : float.PositiveInfinity;
                    break;

                case HazardActivationMode.SkateVelocityGate:
                    next = IsPowered ? HazardCyclePhase.Active : HazardCyclePhase.Inactive;
                    untilActive = IsPowered ? 0f : float.PositiveInfinity;
                    break;

                default:
                    next = HazardCyclePhase.Inactive;
                    break;
            }
        }

        TimeUntilActive = untilActive;
        bool phaseChanged = previous != next;
        CurrentPhase = next;

        if (phaseChanged || clockTime == 0f)
        {
            ApplyVisualState(next);
            if (phaseChanged)
                OnPhaseChanged?.Invoke(next);
        }

        if (checkOverlapOnActivate && hazard != null && !hazard.IsFrozen)
        {
            if (mode == HazardActivationMode.SkateVelocityGate && IsPowered)
            {
                hazard.CheckOverlappingPlayer();
            }
            else if (phaseChanged && next == HazardCyclePhase.Active)
            {
                hazard.CheckOverlappingPlayer();
            }
        }
    }

    private void ApplyVisualState(HazardCyclePhase phase)
    {
        if (inactiveVisual != null) inactiveVisual.SetActive(phase == HazardCyclePhase.Inactive);
        if (warningVisual != null) warningVisual.SetActive(phase == HazardCyclePhase.Warning);
        if (activeVisual != null) activeVisual.SetActive(phase == HazardCyclePhase.Active);

        if (fallbackSpriteRenderer != null && (hazard == null || !hazard.IsFrozen))
        {
            if (mode == HazardActivationMode.SkateVelocityGate && phase == HazardCyclePhase.Active)
            {
                fallbackSpriteRenderer.color = skateTint;
            }
            else
            {
                fallbackSpriteRenderer.color = phase switch
                {
                    HazardCyclePhase.Active   => activeTint,
                    HazardCyclePhase.Warning  => warningTint,
                    _                         => inactiveTint,
                };
            }
        }
    }
}

