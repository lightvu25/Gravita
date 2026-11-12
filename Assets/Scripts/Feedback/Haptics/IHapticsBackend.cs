/// <summary>
/// Platform-agnostic adapter contract for issuing low-level haptic feedback.
/// Gameplay and UI scripts must call <see cref="GameHaptics"/> rather than referencing a backend directly.
/// </summary>
public interface IHapticsBackend
{
    /// <summary>True when the current runtime platform and hardware support haptic playback.</summary>
    bool IsSupported { get; }

    /// <summary>Triggers the platform haptic preset mapped to <paramref name="hapticEvent"/>.</summary>
    void Play(HapticEvent hapticEvent);
}

/// <summary>
/// Key-value persistence seam for the global haptics toggle.
/// Backed by <see cref="UnityEngine.PlayerPrefs"/> in production and an in-memory store in EditMode tests.
/// </summary>
internal interface IHapticsSettingsStore
{
    bool GetBool(string key, bool defaultValue);
    void SetBool(string key, bool value);
}

