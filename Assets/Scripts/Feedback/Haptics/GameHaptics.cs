using System;
using System.Runtime.CompilerServices;
using UnityEngine;

[assembly: InternalsVisibleTo("Assembly-CSharp-Editor")]

/// <summary>
/// Centralized, scene-independent mobile haptics service for Gravita.
///
/// Design guarantees:
///   - Static service (no scene GameObject or DontDestroyOnLoad singleton to duplicate across scenes).
///   - Persists <see cref="Enabled"/> through <see cref="PlayerPrefs"/> (<c>"HapticsEnabled"</c>, default <c>true</c>),
///     matching existing project settings conventions (<see cref="SettingUI"/>, <see cref="SoundManager"/>).
///   - Evaluates per-event cooldowns and global minimum spacing against <see cref="Time.unscaledTime"/>
///     so hit-stop (<c>Time.timeScale = 0</c>) and pause menus never block or distort cooldowns.
///   - Zero heap allocations on playback calls and graceful no-op degradation in the Unity Editor.
/// </summary>
public static class GameHaptics
{
    public const string PrefKey = "HapticsEnabled";

    /// <summary>Minimum unscaled seconds between any two non-Death haptic pulses.</summary>
    public const float GlobalMinGapSeconds = 0.03f;

    private const int EventCount = 7;

    private static readonly float[] EventCooldownSeconds = new float[EventCount]
    {
        0.05f, // UISelect
        0.08f, // WallImpact
        0.08f, // GravityAnchor
        0.06f, // PadRedirect
        0.10f, // LaunchStart
        0.08f, // SwitchToggle
        0.00f, // Death (bypasses cooldowns)
    };

    private static readonly float[] lastEventPlayTimes = new float[EventCount];

    private static IHapticsBackend backend = CreateDefaultBackend();
    private static IHapticsSettingsStore settingsStore = PlayerPrefsSettingsStore.Instance;
    private static Func<float> unscaledClock = DefaultUnscaledClock;

    private static bool enabledLoaded;
    private static bool cachedEnabled = true;

    private static bool supportChecked;
    private static bool cachedSupported;

    private static float lastAnyPlayTime = float.NegativeInfinity;
    private static bool loggedFailureOnce;

    /// <summary>Fired whenever <see cref="Enabled"/> changes value.</summary>
    public static event Action<bool> OnEnabledChanged;

    /// <summary>
    /// Global user setting controlling all haptic feedback (default <c>true</c>).
    /// Persisted under <see cref="PrefKey"/>.
    /// </summary>
    public static bool Enabled
    {
        get
        {
            EnsureEnabledLoaded();
            return cachedEnabled;
        }
        set
        {
            EnsureEnabledLoaded();
            if (cachedEnabled == value)
                return;

            cachedEnabled = value;
            settingsStore.SetBool(PrefKey, value);
            OnEnabledChanged?.Invoke(value);
        }
    }

    /// <summary>
    /// True if the active platform backend reports hardware haptics support (cached after first query).
    /// Returns <c>false</c> in the Unity Editor.
    /// </summary>
    public static bool IsSupported
    {
        get
        {
            if (!supportChecked)
            {
                supportChecked = true;
                try
                {
                    cachedSupported = backend != null && backend.IsSupported;
                }
                catch (Exception ex)
                {
                    cachedSupported = false;
                    LogFailureOnce("Support check threw an exception", ex);
                }
            }

            return cachedSupported;
        }
    }

    static GameHaptics()
    {
        ResetCooldownTimestamps();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetRuntimeSessionState()
    {
        ResetCooldownTimestamps();
        supportChecked = false;
        enabledLoaded = false;
        loggedFailureOnce = false;
    }

    /// <summary>
    /// Requests tactile feedback for <paramref name="hapticEvent"/> if <see cref="Enabled"/> is true,
    /// the device supports haptics, and cooldown guards pass.
    /// </summary>
    public static void Play(HapticEvent hapticEvent)
    {
        int index = (int)hapticEvent;
        if (index < 0 || index >= EventCount)
            return;

        if (!Enabled)
            return;

        if (!IsSupported || backend == null)
            return;

        float now = unscaledClock != null ? unscaledClock() : 0f;

        // Guard against clock rewind (e.g. Enter Play Mode without domain reload).
        if (!float.IsNegativeInfinity(lastAnyPlayTime) && now < lastAnyPlayTime)
        {
            ResetCooldownTimestamps();
        }

        if (hapticEvent != HapticEvent.Death)
        {
            if (now - lastAnyPlayTime < GlobalMinGapSeconds)
                return;

            float eventCooldown = EventCooldownSeconds[index];
            if (eventCooldown > 0f && now - lastEventPlayTimes[index] < eventCooldown)
                return;
        }

        lastEventPlayTimes[index] = now;
        lastAnyPlayTime = now;

        try
        {
            backend.Play(hapticEvent);
        }
        catch (Exception ex)
        {
            LogFailureOnce($"Backend playback threw for {hapticEvent}", ex);
        }
    }

    /// <summary>Returns the configured unscaled cooldown in seconds for <paramref name="hapticEvent"/>.</summary>
    public static float GetCooldownSeconds(HapticEvent hapticEvent)
    {
        int index = (int)hapticEvent;
        return (index >= 0 && index < EventCount) ? EventCooldownSeconds[index] : 0f;
    }

    internal static void ConfigureForTests(
        IHapticsBackend testBackend,
        Func<float> testClock,
        IHapticsSettingsStore testStore)
    {
        backend = testBackend ?? NullHapticsBackend.Instance;
        unscaledClock = testClock ?? DefaultUnscaledClock;
        settingsStore = testStore ?? PlayerPrefsSettingsStore.Instance;

        enabledLoaded = false;
        cachedEnabled = true;
        supportChecked = false;
        cachedSupported = false;
        loggedFailureOnce = false;
        OnEnabledChanged = null;
        ResetCooldownTimestamps();
    }

    internal static void ResetForTests()
    {
        backend = CreateDefaultBackend();
        unscaledClock = DefaultUnscaledClock;
        settingsStore = PlayerPrefsSettingsStore.Instance;

        enabledLoaded = false;
        cachedEnabled = true;
        supportChecked = false;
        cachedSupported = false;
        loggedFailureOnce = false;
        OnEnabledChanged = null;
        ResetCooldownTimestamps();
    }

    private static void EnsureEnabledLoaded()
    {
        if (enabledLoaded)
            return;

        enabledLoaded = true;
        cachedEnabled = settingsStore != null ? settingsStore.GetBool(PrefKey, true) : true;
    }

    private static void ResetCooldownTimestamps()
    {
        for (int i = 0; i < EventCount; i++)
        {
            lastEventPlayTimes[i] = float.NegativeInfinity;
        }

        lastAnyPlayTime = float.NegativeInfinity;
    }

    private static IHapticsBackend CreateDefaultBackend()
    {
#if (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
        return new MostHapticsBackend();
#else
        return NullHapticsBackend.Instance;
#endif
    }

    private static float DefaultUnscaledClock() => Time.unscaledTime;

    private static void LogFailureOnce(string message, Exception ex)
    {
        if (loggedFailureOnce)
            return;

        loggedFailureOnce = true;
        Debug.LogWarning($"[GameHaptics] {message}: {ex.Message}");
    }

    private sealed class PlayerPrefsSettingsStore : IHapticsSettingsStore
    {
        public static readonly PlayerPrefsSettingsStore Instance = new PlayerPrefsSettingsStore();

        public bool GetBool(string key, bool defaultValue)
        {
            return PlayerPrefs.GetInt(key, defaultValue ? 1 : 0) != 0;
        }

        public void SetBool(string key, bool value)
        {
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}

