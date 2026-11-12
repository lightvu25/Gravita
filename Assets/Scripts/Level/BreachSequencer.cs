using System;
using UnityEngine;

public enum BreachActionType
{
    PowerOn  = 0,
    PowerOff = 1,
    Toggle   = 2,
}

[Serializable]
public class BreachTimelineEvent
{
    [Tooltip("Designer note describing what happens during this station breach wave.")]
    public string label = "Sector Breach";

    [Tooltip("LevelClock timestamp (seconds) when this breach triggers.")]
    [Min(0f)] public float triggerTime = 8f;

    [Tooltip("Action applied to every wired ISwitchable target (EnergyGate, HazardActivation, FailingTile).")]
    public BreachActionType action = BreachActionType.PowerOn;

    [Tooltip("Targets driven when this breach fires.")]
    public MonoBehaviour[] targets;

    [Tooltip("Optional station-tremor camera shake impulse.")]
    [Min(0f)] public float cameraShakeForce = 0.14f;
}

/// <summary>
/// S4 Breach Sequencer: drives scripted station-collapse events along the authoritative
/// <see cref="LevelClock"/> so levels evolve dynamically without a generic rising tide.
/// Automatically resets all fired states when the level restarts.
/// </summary>
public class BreachSequencer : MonoBehaviour, ILevelClockListener
{
    public event Action<int, BreachTimelineEvent> OnBreachFired;

    [SerializeField] private BreachTimelineEvent[] timelineEvents;

    private bool[] firedFlags;

    public int EventCount => timelineEvents != null ? timelineEvents.Length : 0;

    private void Awake()
    {
        EnsureFlagsAllocated();
    }

    private void EnsureFlagsAllocated()
    {
        int len = timelineEvents != null ? timelineEvents.Length : 0;
        if (firedFlags == null || firedFlags.Length != len)
            firedFlags = new bool[len];
    }

    public bool HasEventFired(int index)
    {
        EnsureFlagsAllocated();
        return index >= 0 && index < firedFlags.Length && firedFlags[index];
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        if (timelineEvents == null || timelineEvents.Length == 0)
            return;

        EnsureFlagsAllocated();

        for (int i = 0; i < timelineEvents.Length; i++)
        {
            if (firedFlags[i]) continue;
            BreachTimelineEvent ev = timelineEvents[i];
            if (ev == null || levelTime < ev.triggerTime) continue;

            firedFlags[i] = true;
            ExecuteBreachEvent(i, ev);
        }
    }

    public void OnLevelClockReset()
    {
        if (firedFlags == null) return;
        for (int i = 0; i < firedFlags.Length; i++)
            firedFlags[i] = false;
    }

    private void ExecuteBreachEvent(int index, BreachTimelineEvent ev)
    {
        if (ev.targets != null)
        {
            for (int t = 0; t < ev.targets.Length; t++)
            {
                if (ev.targets[t] is ISwitchable switchable)
                {
                    switch (ev.action)
                    {
                        case BreachActionType.PowerOn:
                            switchable.SetPowered(true);
                            break;
                        case BreachActionType.PowerOff:
                            switchable.SetPowered(false);
                            break;
                        case BreachActionType.Toggle:
                            switchable.TogglePowered();
                            break;
                    }
                }
            }
        }

        if (ev.cameraShakeForce > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(ev.cameraShakeForce);

        OnBreachFired?.Invoke(index, ev);
    }
}

