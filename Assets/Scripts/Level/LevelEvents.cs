using System;
using UnityEngine;

/// <summary>
/// Centralized static lifecycle event bus for level runs so services and UI
/// can subscribe cleanly without repeated scene-wide FindAnyObjectByType lookups.
/// </summary>
public static class LevelEvents
{
    public static event Action<LevelFlowController, LevelState> OnStateChanged;
    public static event Action<LevelFlowController> OnLevelStarted;
    public static event Action<LevelFlowController> OnLevelRestarted;
    public static event Action<LevelFlowController> OnLevelCompleted;
    public static event Action<LevelFlowController, string> OnLevelFailed;
    public static event Action<LevelFlowController, string> OnPlayerDiedAwaitingRevive;
    public static event Action<LevelFlowController> OnPlayerRevived;
    public static event Action<LevelFlowController> OnReturnToMapRequested;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        OnStateChanged = null;
        OnLevelStarted = null;
        OnLevelRestarted = null;
        OnLevelCompleted = null;
        OnLevelFailed = null;
        OnPlayerDiedAwaitingRevive = null;
        OnPlayerRevived = null;
        OnReturnToMapRequested = null;
    }

    internal static void RaiseStateChanged(LevelFlowController controller, LevelState state)
        => OnStateChanged?.Invoke(controller, state);

    internal static void RaiseLevelStarted(LevelFlowController controller)
        => OnLevelStarted?.Invoke(controller);

    internal static void RaiseLevelRestarted(LevelFlowController controller)
        => OnLevelRestarted?.Invoke(controller);

    internal static void RaiseLevelCompleted(LevelFlowController controller)
        => OnLevelCompleted?.Invoke(controller);

    internal static void RaiseLevelFailed(LevelFlowController controller, string reason)
        => OnLevelFailed?.Invoke(controller, reason);

    internal static void RaisePlayerDiedAwaitingRevive(LevelFlowController controller, string reason)
        => OnPlayerDiedAwaitingRevive?.Invoke(controller, reason);

    internal static void RaisePlayerRevived(LevelFlowController controller)
        => OnPlayerRevived?.Invoke(controller);

    internal static void RaiseReturnToMapRequested(LevelFlowController controller)
        => OnReturnToMapRequested?.Invoke(controller);
}
