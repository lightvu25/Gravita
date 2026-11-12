// By SOLO :)
// Check MOST IN ONE package https://assetstore.unity.com/packages/slug/295013

using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

#if UNITY_IOS && !UNITY_EDITOR
using System.Runtime.InteropServices;
#endif

namespace Solo.MOST_IN_ONE
{
    /// <summary>
    /// Mobile haptics for Unity.
    /// iOS uses UIFeedbackGenerator for presets and Core Haptics for curves.
    /// Android uses Vibrator/VibrationEffect for presets, patterns, and curves.
    /// </summary>
    public static class MOST_HapticFeedback
    {
        public const int DefaultCurveSamples = 16;
        public const int MaxCurveSamples = 64;
        public const int DefaultAndroidMaxAmplitude = 255;
        public const float DefaultCurveDurationMs = 160f;
        public const float DefaultCurveSharpness = 0.6f;
        public const float MinCurveDurationMs = 10f;

        const string TogglePrefsKey = "MOST Haptic Toggle";
        const float DefaultCooldownSeconds = 0.1f;
        const float IOSPresetDurationPaddingMs = 350f;

        public enum HapticTypes
        {
            Selection,
            Success,
            Warning,
            Failure,
            LightImpact,
            MediumImpact,
            HeavyImpact,
            RigidImpact,
            SoftImpact
        }

        [Serializable]
        public struct IOS_Haptic
        {
            [Tooltip("Delay before this iOS preset pulse, in milliseconds.")]
            [Min(0f)]
            public float Delay;

            [Tooltip("Preset haptic type for this iOS pulse.")]
            public HapticTypes PulseType;

            public IOS_Haptic(HapticTypes type, float delay = 0f)
            {
                Delay = delay;
                PulseType = type;
            }
        }

        [Serializable]
        public struct Android_Haptic
        {
            [Tooltip("Delay before this Android pulse, in milliseconds.")]
            [Min(0f)]
            public long Delay;

            [Tooltip("Pulse duration, in milliseconds.")]
            [Min(1f)]
            public long PulseTime;

            [Tooltip("Pulse amplitude. 0 = silent, 255 = maximum.")]
            [Range(0, 255)]
            public int PulseStrength;

            public Android_Haptic(long delay, long pulseTime, int pulseStrength)
            {
                Delay = delay;
                PulseTime = pulseTime;
                PulseStrength = pulseStrength;
            }
        }

        [Serializable]
        public struct CustomHapticPattern
        {
            [Tooltip("iOS preset pulse sequence.")]
            public IOS_Haptic[] IOS_HapticPattern;

            [Tooltip("Android waveform pulse sequence.")]
            public Android_Haptic[] Android_HapticPattern;

            public CustomHapticPattern(IOS_Haptic[] iosHapticPattern, Android_Haptic[] androidHapticPattern)
            {
                IOS_HapticPattern = iosHapticPattern;
                Android_HapticPattern = androidHapticPattern;
            }

            public float GetDuration()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return AndroidDuration();
#elif UNITY_IOS && !UNITY_EDITOR
                return IOSDuration();
#else
                return Mathf.Max(IOSDuration(), AndroidDuration());
#endif
            }

            public float IOSDuration()
            {
                if (IOS_HapticPattern == null || IOS_HapticPattern.Length == 0)
                    return 0f;

                float totalMs = IOSPresetDurationPaddingMs;

                for (int i = 0; i < IOS_HapticPattern.Length; i++)
                    totalMs += Mathf.Max(0f, IOS_HapticPattern[i].Delay);

                return totalMs / 1000f;
            }

            public float AndroidDuration()
            {
                if (Android_HapticPattern == null || Android_HapticPattern.Length == 0)
                    return 0f;

                long totalMs = 0L;

                for (int i = 0; i < Android_HapticPattern.Length; i++)
                {
                    Android_Haptic pulse = Android_HapticPattern[i];
                    totalMs += Math.Max(0L, pulse.Delay);
                    totalMs += Math.Max(1L, pulse.PulseTime);
                }

                return totalMs / 1000f;
            }
        }

        [Serializable]
        public struct iOSHapticCurve
        {
            [Tooltip("Delay before the iOS curve starts, in milliseconds.")]
            [Min(0f)]
            public float Delay;

            [Tooltip("Total iOS curve duration, in milliseconds.")]
            [Min(MinCurveDurationMs)]
            public float DurationMs;

            [Tooltip("Core Haptics sharpness. 0 = soft, 1 = sharp.")]
            [Range(0f, 1f)]
            public float Sharpness;

            [Tooltip("Number of Core Haptics control points sampled from Intensity.")]
            [Range(2, MaxCurveSamples)]
            public int Samples;

            [Tooltip("Normalized intensity curve. X = 0..1 over DurationMs. Y = intensity 0..1.")]
            public AnimationCurve Intensity;

            [Tooltip("Preset fallback used when Core Haptics curves are unavailable.")]
            public HapticTypes FallbackType;

            public iOSHapticCurve(
                float durationMs,
                float sharpness,
                AnimationCurve intensity,
                int samples = DefaultCurveSamples,
                HapticTypes fallbackType = HapticTypes.MediumImpact,
                float delay = 0f)
            {
                Delay = delay;
                DurationMs = durationMs;
                Sharpness = sharpness;
                Samples = samples;
                Intensity = intensity;
                FallbackType = fallbackType;
            }

            public float GetDuration()
            {
                float duration = DurationMs <= 0f ? DefaultCurveDurationMs : Mathf.Max(MinCurveDurationMs, DurationMs);
                return (Mathf.Max(0f, Delay) + duration) / 1000f;
            }
        }

        [Serializable]
        public struct AndroidHapticCurve
        {
            [Tooltip("Delay before the Android curve starts, in milliseconds.")]
            [Min(0f)]
            public long Delay;

            [Tooltip("Total Android curve duration, in milliseconds.")]
            [Min(MinCurveDurationMs)]
            public long DurationMs;

            [Tooltip("Number of waveform segments sampled from Intensity.")]
            [Range(2, MaxCurveSamples)]
            public int Samples;

            [Tooltip("Maximum Android amplitude. 1 = weakest, 255 = strongest.")]
            [Range(1, 255)]
            public int MaxAmplitude;

            [Tooltip("Normalized intensity curve. X = 0..1 over DurationMs. Y = intensity 0..1.")]
            public AnimationCurve Intensity;

            [Tooltip("Preset fallback used when Android amplitude curves are unavailable.")]
            public HapticTypes FallbackType;

            public AndroidHapticCurve(
                long durationMs,
                AnimationCurve intensity,
                int samples = DefaultCurveSamples,
                int maxAmplitude = DefaultAndroidMaxAmplitude,
                HapticTypes fallbackType = HapticTypes.MediumImpact,
                long delay = 0L)
            {
                Delay = delay;
                DurationMs = durationMs;
                Samples = samples;
                MaxAmplitude = maxAmplitude;
                Intensity = intensity;
                FallbackType = fallbackType;
            }

            public float GetDuration()
            {
                long duration = DurationMs <= 0L
                    ? Mathf.RoundToInt(DefaultCurveDurationMs)
                    : Math.Max((long)MinCurveDurationMs, DurationMs);

                return (Math.Max(0L, Delay) + duration) / 1000f;
            }
        }

        [Serializable]
        public struct HapticCurve
        {
            [Tooltip("iOS Core Haptics curve data.")]
            public iOSHapticCurve IOS_HapticCurve;

            [Tooltip("Android waveform curve data.")]
            public AndroidHapticCurve Android_HapticCurve;

            public HapticCurve(iOSHapticCurve iosHapticCurve, AndroidHapticCurve androidHapticCurve)
            {
                IOS_HapticCurve = iosHapticCurve;
                Android_HapticCurve = androidHapticCurve;
            }

            public HapticCurve(
                AnimationCurve intensity,
                float durationMs = DefaultCurveDurationMs,
                float sharpness = DefaultCurveSharpness,
                int samples = DefaultCurveSamples,
                HapticTypes fallbackType = HapticTypes.MediumImpact,
                float delay = 0f,
                int androidMaxAmplitude = DefaultAndroidMaxAmplitude)
            {
                IOS_HapticCurve = new iOSHapticCurve(
                    durationMs,
                    sharpness,
                    intensity,
                    samples,
                    fallbackType,
                    delay
                );

                Android_HapticCurve = new AndroidHapticCurve(
                    Mathf.RoundToInt(durationMs),
                    intensity,
                    samples,
                    androidMaxAmplitude,
                    fallbackType,
                    Mathf.Max(0, Mathf.RoundToInt(delay))
                );
            }

            public float GetDuration()
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return AndroidDuration();
#elif UNITY_IOS && !UNITY_EDITOR
                return IOSDuration();
#else
                return Mathf.Max(IOSDuration(), AndroidDuration());
#endif
            }

            public float IOSDuration()
            {
                return IOS_HapticCurve.GetDuration();
            }

            public float AndroidDuration()
            {
                return Android_HapticCurve.GetDuration();
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [DllImport("__Internal", EntryPoint = "MOST_HapticFeedback")]
        static extern void IOS_PlayPreset(int type);

        [DllImport("__Internal", EntryPoint = "MOST_HapticPrewarm")]
        static extern void IOS_Prewarm();

        [DllImport("__Internal", EntryPoint = "MOST_HapticPulseCurve")]
        static extern void IOS_PlayCurve(
            float durationSeconds,
            float sharpness,
            [In] float[] timesSeconds,
            [In] float[] intensities,
            int count
        );

        [DllImport("__Internal", EntryPoint = "MOST_HapticStopCurve")]
        static extern void IOS_StopCurve();

        [DllImport("__Internal", EntryPoint = "MOST_HapticSupportsCore")]
        [return: MarshalAs(UnmanagedType.I1)]
        static extern bool IOS_SupportsCoreHaptics();
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _androidVibrator;
        static AndroidJavaClass _vibrationEffectClass;
        static int _androidApiLevel;
        static bool _androidHasAmplitudeControl;
#endif

        static bool _initialized;
        static float _lastHapticTime = -999f;
        static Task _activePlayback;
        static CancellationTokenSource _playbackCts;
        static readonly object _sync = new ();

        public static bool IsPlaying
        {
            get
            {
                lock (_sync)
                    return _activePlayback != null && !_activePlayback.IsCompleted;
            }
        }

        public static bool HapticsEnabled
        {
            get { return !PlayerPrefs.HasKey(TogglePrefsKey) || PlayerPrefs.GetInt(TogglePrefsKey) == 1; }
            set
            {
                PlayerPrefs.SetInt(TogglePrefsKey, value ? 1 : 0);
                PlayerPrefs.Save();
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeOnLoad()
        {
            EnsureInitialized();
        }

        public static void Prewarm()
        {
            EnsureInitialized();

#if UNITY_IOS && !UNITY_EDITOR
            TryIOSPrewarm();
#endif
        }

        public static void Generate(HapticTypes type)
        {
            if (!CanPlay())
                return;

            PlayPreset(type);
        }

        public static void GenerateWithCooldown(HapticTypes type, float cooldown = DefaultCooldownSeconds)
        {
            if (!CanPlay() || !TryConsumeCooldown(cooldown))
                return;

            PlayPreset(type);
        }

        public static void Generate(CustomHapticPattern pattern)
        {
            if (!CanPlay() || !HasPatternDataForCurrentPlatform(pattern))
                return;

            StartPlayback(token => RunPatternAsync(pattern, token));
        }

        public static void GeneratePattern(CustomHapticPattern pattern)
        {
            Generate(pattern);
        }

        public static void Generate(HapticCurve curve)
        {
            if (!CanPlay())
                return;

#if UNITY_IOS && !UNITY_EDITOR
            StartPlayback(token => RunIOSCurveAsync(curve.IOS_HapticCurve, token));
#elif UNITY_ANDROID && !UNITY_EDITOR
            StartPlayback(token => RunAndroidCurveAsync(curve.Android_HapticCurve, token));
#endif
        }

        public static void GenerateCurve(HapticCurve curve)
        {
            Generate(curve);
        }

        public static void GenerateCurve(
            AnimationCurve intensity,
            float durationMs = DefaultCurveDurationMs,
            float sharpness = DefaultCurveSharpness,
            int samples = DefaultCurveSamples,
            HapticTypes fallbackType = HapticTypes.MediumImpact,
            float delayMs = 0f,
            int androidMaxAmplitude = DefaultAndroidMaxAmplitude)
        {
            Generate(new HapticCurve(intensity, durationMs, sharpness, samples, fallbackType, delayMs, androidMaxAmplitude));
        }

        public static void GeneratePulseCurve(
            float durationMs = DefaultCurveDurationMs,
            float sharpness = DefaultCurveSharpness,
            HapticTypes fallbackType = HapticTypes.MediumImpact,
            float delayMs = 0f,
            int samples = DefaultCurveSamples,
            int androidMaxAmplitude = DefaultAndroidMaxAmplitude)
        {
            Generate(CreatePulseCurve(durationMs, sharpness, fallbackType, delayMs, samples, androidMaxAmplitude));
        }

        public static void GenerateCurveWithCooldown(HapticCurve curve, float cooldown = DefaultCooldownSeconds)
        {
            if (!CanPlay() || !TryConsumeCooldown(cooldown))
                return;

            Generate(curve);
        }

        public static HapticCurve CreatePulseCurve(
            float durationMs = DefaultCurveDurationMs,
            float sharpness = DefaultCurveSharpness,
            HapticTypes fallbackType = HapticTypes.MediumImpact,
            float delayMs = 0f,
            int samples = DefaultCurveSamples,
            int androidMaxAmplitude = DefaultAndroidMaxAmplitude)
        {
            return new HapticCurve(
                DefaultPulseCurve(),
                durationMs,
                sharpness,
                samples,
                fallbackType,
                delayMs,
                androidMaxAmplitude
            );
        }

        public static AnimationCurve DefaultPulseCurve()
        {
            return new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.12f, 1f),
                new Keyframe(0.55f, 0.65f),
                new Keyframe(1f, 0f)
            );
        }

        public static void Stop()
        {
            CancelManagedPlayback();
            StopNativePlayback();
        }

        public static bool IsSupported()
        {
            EnsureInitialized();

#if UNITY_IOS && !UNITY_EDITOR
            return true;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return HasAndroidVibrator();
#else
            return false;
#endif
        }

        public static bool IsCoreHapticsSupported()
        {
#if UNITY_IOS && !UNITY_EDITOR
            EnsureInitialized();

            try { return IOS_SupportsCoreHaptics(); }
            catch { return false; }
#else
            return false;
#endif
        }

        static bool CanPlay()
        {
            if (!HapticsEnabled)
                return false;

            EnsureInitialized();
            return _initialized;
        }

        static void EnsureInitialized()
        {
            if (_initialized)
                return;

#if UNITY_ANDROID && !UNITY_EDITOR
            InitializeAndroid();
#endif

#if UNITY_IOS && !UNITY_EDITOR
            TryIOSPrewarm();
#endif

            _initialized = true;
        }

        static bool TryConsumeCooldown(float cooldown)
        {
            float requiredCooldown = Mathf.Max(0f, cooldown);
            float now = Time.unscaledTime;

            if (now - _lastHapticTime < requiredCooldown)
                return false;

            _lastHapticTime = now;
            return true;
        }

        static bool HasPatternDataForCurrentPlatform(CustomHapticPattern pattern)
        {
#if UNITY_IOS && !UNITY_EDITOR
            return pattern.IOS_HapticPattern != null && pattern.IOS_HapticPattern.Length > 0;
#elif UNITY_ANDROID && !UNITY_EDITOR
            return pattern.Android_HapticPattern != null && pattern.Android_HapticPattern.Length > 0;
#else
            return false;
#endif
        }

        static void StartPlayback(Func<CancellationToken, Task> runner)
        {
            CancellationTokenSource cts;

            lock (_sync)
            {
                if (_playbackCts != null && !_playbackCts.IsCancellationRequested)
                    _playbackCts.Cancel();

                cts = new CancellationTokenSource();
                _playbackCts = cts;
                _activePlayback = RunPlaybackSafeAsync(runner, cts);
            }
        }

        static async Task RunPlaybackSafeAsync(Func<CancellationToken, Task> runner, CancellationTokenSource cts)
        {
            try
            {
                await runner(cts.Token);
            }
            catch (OperationCanceledException)
            {
                // Expected when another haptic starts or Stop() is called.
            }
            catch (Exception e)
            {
                Debug.LogError("MOST haptic playback failed: " + e.Message);
            }
            finally
            {
                lock (_sync)
                {
                    if (ReferenceEquals(_playbackCts, cts))
                    {
                        _playbackCts = null;
                        _activePlayback = null;
                    }
                }

                cts.Dispose();
            }
        }

        static void CancelManagedPlayback()
        {
            CancellationTokenSource cts;

            lock (_sync)
            {
                cts = _playbackCts;
                _playbackCts = null;
                _activePlayback = null;
            }

            if (cts != null && !cts.IsCancellationRequested)
                cts.Cancel();
        }

        static async Task RunPatternAsync(CustomHapticPattern pattern, CancellationToken token)
        {
#if UNITY_IOS && !UNITY_EDITOR
            await RunIOSPatternAsync(pattern, token);
#elif UNITY_ANDROID && !UNITY_EDITOR
            await RunAndroidPatternAsync(pattern, token);
#else
            await Task.CompletedTask;
#endif
        }

        static void StopNativePlayback()
        {
#if UNITY_IOS && !UNITY_EDITOR
            try { IOS_StopCurve(); } catch { }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                if (_androidVibrator != null)
                    _androidVibrator.Call("cancel");
            }
            catch { }
#endif
        }

        static void PlayPreset(HapticTypes type)
        {
#if UNITY_IOS && !UNITY_EDITOR
            PlayIOSPreset(type);
#elif UNITY_ANDROID && !UNITY_EDITOR
            GetAndroidPreset(type, out long[] timings, out int[] amplitudes);
            PlayAndroidWaveform(timings, amplitudes);
#endif
        }

        static iOSHapticCurve Sanitize(iOSHapticCurve curve)
        {
            curve.Delay = Mathf.Max(0f, curve.Delay);
            curve.DurationMs = curve.DurationMs <= 0f ? DefaultCurveDurationMs : Mathf.Max(MinCurveDurationMs, curve.DurationMs);
            curve.Sharpness = Mathf.Clamp01(curve.Sharpness);
            curve.Samples = Mathf.Clamp(curve.Samples <= 0 ? DefaultCurveSamples : curve.Samples, 2, MaxCurveSamples);
            curve.Intensity = curve.Intensity == null || curve.Intensity.length == 0 ? DefaultPulseCurve() : curve.Intensity;
            return curve;
        }

        static AndroidHapticCurve Sanitize(AndroidHapticCurve curve)
        {
            curve.Delay = Math.Max(0L, curve.Delay);
            curve.DurationMs = curve.DurationMs <= 0L ? Mathf.RoundToInt(DefaultCurveDurationMs) : Math.Max((long)MinCurveDurationMs, curve.DurationMs);
            curve.Samples = Mathf.Clamp(curve.Samples <= 0 ? DefaultCurveSamples : curve.Samples, 2, MaxCurveSamples);
            curve.MaxAmplitude = Mathf.Clamp(curve.MaxAmplitude <= 0 ? DefaultAndroidMaxAmplitude : curve.MaxAmplitude, 1, 255);
            curve.Intensity = curve.Intensity == null || curve.Intensity.length == 0 ? DefaultPulseCurve() : curve.Intensity;
            return curve;
        }

        static int ToIntMs(long milliseconds)
        {
            if (milliseconds <= 0L)
                return 0;

            return milliseconds > int.MaxValue ? int.MaxValue : (int)milliseconds;
        }

#if UNITY_IOS && !UNITY_EDITOR
        static void TryIOSPrewarm()
        {
            try { IOS_Prewarm(); }
            catch (Exception e) { Debug.LogError("iOS haptics prewarm failed: " + e.Message); }
        }

        static void PlayIOSPreset(HapticTypes type)
        {
            try { IOS_PlayPreset((int)type); }
            catch (Exception e) { Debug.LogError("Failed to generate iOS haptic " + type + ": " + e.Message); }
        }

        static async Task RunIOSPatternAsync(CustomHapticPattern pattern, CancellationToken token)
        {
            IOS_Haptic[] sequence = pattern.IOS_HapticPattern;

            for (int i = 0; i < sequence.Length; i++)
            {
                int delayMs = Mathf.Max(0, Mathf.RoundToInt(sequence[i].Delay));

                if (delayMs > 0)
                    await Task.Delay(delayMs, token);

                token.ThrowIfCancellationRequested();
                PlayIOSPreset(sequence[i].PulseType);
            }
        }

        static async Task RunIOSCurveAsync(iOSHapticCurve curve, CancellationToken token)
        {
            curve = Sanitize(curve);

            int delayMs = Mathf.RoundToInt(curve.Delay);
            if (delayMs > 0)
                await Task.Delay(delayMs, token);

            token.ThrowIfCancellationRequested();
            PlayIOSCurve(curve);

            int waitMs = Mathf.RoundToInt(curve.DurationMs);
            if (waitMs > 0)
                await Task.Delay(waitMs, token);
        }

        static void PlayIOSCurve(iOSHapticCurve curve)
        {
            try
            {
                if (!IOS_SupportsCoreHaptics())
                {
                    PlayIOSPreset(curve.FallbackType);
                    return;
                }

                float durationSeconds = curve.DurationMs / 1000f;
                float[] times = new float[curve.Samples];
                float[] intensities = new float[curve.Samples];

                for (int i = 0; i < curve.Samples; i++)
                {
                    float normalized = i / (float)(curve.Samples - 1);
                    times[i] = normalized * durationSeconds;
                    intensities[i] = Mathf.Clamp01(curve.Intensity.Evaluate(normalized));
                }

                times[0] = 0f;
                times[curve.Samples - 1] = durationSeconds;

                IOS_PlayCurve(durationSeconds, curve.Sharpness, times, intensities, curve.Samples);
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to generate iOS curve haptic: " + e.Message);
                PlayIOSPreset(curve.FallbackType);
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        static void InitializeAndroid()
        {
            try
            {
                using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (AndroidJavaClass version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    _androidVibrator = currentActivity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                    _androidApiLevel = version.GetStatic<int>("SDK_INT");

                    if (_androidApiLevel >= 26)
                    {
                        _vibrationEffectClass = new AndroidJavaClass("android.os.VibrationEffect");
                        _androidHasAmplitudeControl = _androidVibrator != null && _androidVibrator.Call<bool>("hasAmplitudeControl");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Android haptics initialization failed: " + e.Message);
            }
        }

        static async Task RunAndroidPatternAsync(CustomHapticPattern pattern, CancellationToken token)
        {
            Android_Haptic[] sequence = pattern.Android_HapticPattern;
            long[] timings = new long[sequence.Length * 2];
            int[] amplitudes = new int[sequence.Length * 2];
            long totalMs = 0L;

            for (int i = 0; i < sequence.Length; i++)
            {
                Android_Haptic pulse = sequence[i];
                int index = i * 2;
                long delay = Math.Max(0L, pulse.Delay);
                long pulseTime = Math.Max(1L, pulse.PulseTime);

                timings[index] = delay;
                amplitudes[index] = 0;
                timings[index + 1] = pulseTime;
                amplitudes[index + 1] = Mathf.Clamp(pulse.PulseStrength, 0, 255);

                totalMs += delay + pulseTime;
            }

            PlayAndroidWaveform(timings, amplitudes);
            await DelayAndroidDuration(totalMs, token);
        }

        static async Task RunAndroidCurveAsync(AndroidHapticCurve curve, CancellationToken token)
        {
            curve = Sanitize(curve);

            int delayMs = ToIntMs(curve.Delay);
            if (delayMs > 0)
                await Task.Delay(delayMs, token);

            token.ThrowIfCancellationRequested();
            PlayAndroidCurve(curve);
            await DelayAndroidDuration(curve.DurationMs, token);
        }

        static async Task DelayAndroidDuration(long durationMs, CancellationToken token)
        {
            int waitMs = ToIntMs(durationMs);
            if (waitMs > 0)
                await Task.Delay(waitMs, token);
        }

        static void PlayAndroidCurve(AndroidHapticCurve curve)
        {
            if (!HasAndroidVibrator())
                return;

            if (_androidApiLevel < 26 || _vibrationEffectClass == null || !_androidHasAmplitudeControl)
            {
                GetAndroidPreset(curve.FallbackType, out long[] fallbackTimings, out int[] fallbackAmplitudes);
                PlayAndroidWaveform(fallbackTimings, fallbackAmplitudes);
                return;
            }

            int durationMs = Mathf.Max((int)MinCurveDurationMs, ToIntMs(curve.DurationMs));
            int sampleCount = Mathf.Clamp(curve.Samples, 2, Mathf.Min(MaxCurveSamples, durationMs));
            long[] timings = new long[sampleCount];
            int[] amplitudes = new int[sampleCount];
            long remainingMs = durationMs;

            for (int i = 0; i < sampleCount; i++)
            {
                int remainingSamples = sampleCount - i;
                long segmentMs = Math.Max(1L, remainingMs / remainingSamples);
                remainingMs -= segmentMs;

                float normalized = (i + 0.5f) / sampleCount;
                float intensity = Mathf.Clamp01(curve.Intensity.Evaluate(normalized));

                timings[i] = segmentMs;
                amplitudes[i] = Mathf.Clamp(Mathf.RoundToInt(intensity * curve.MaxAmplitude), 0, 255);
            }

            PlayAndroidWaveform(timings, amplitudes);
        }

        static bool HasAndroidVibrator()
        {
            return _androidVibrator != null && _androidVibrator.Call<bool>("hasVibrator");
        }

        static void PlayAndroidWaveform(long[] timings, int[] amplitudes)
        {
            if (!HasAndroidVibrator() || timings == null || timings.Length == 0)
                return;

            SanitizeAndroidWaveform(timings, amplitudes);

            try
            {
                if (_androidApiLevel >= 26 && _vibrationEffectClass != null && amplitudes != null && amplitudes.Length == timings.Length)
                {
                    using (AndroidJavaObject effect = _vibrationEffectClass.CallStatic<AndroidJavaObject>(
                        "createWaveform",
                        timings,
                        amplitudes,
                        -1))
                    {
                        _androidVibrator.Call("vibrate", effect);
                    }
                }
                else
                {
                    _androidVibrator.Call("vibrate", timings, -1);
                }
            }
            catch (Exception e)
            {
                Debug.LogError("Failed to generate Android haptic: " + e.Message);
            }
        }

        static void SanitizeAndroidWaveform(long[] timings, int[] amplitudes)
        {
            for (int i = 0; i < timings.Length; i++)
                timings[i] = Math.Max(0L, timings[i]);

            if (amplitudes == null)
                return;

            for (int i = 0; i < amplitudes.Length; i++)
                amplitudes[i] = Mathf.Clamp(amplitudes[i], 0, 255);
        }

        static void GetAndroidPreset(HapticTypes type, out long[] timings, out int[] amplitudes)
        {
            switch (type)
            {
                case HapticTypes.Selection:
                    timings = new long[] { 0, 20 };
                    amplitudes = new int[] { 0, 80 };
                    break;
                case HapticTypes.Success:
                    timings = new long[] { 0, 100, 50, 100 };
                    amplitudes = new int[] { 0, 150, 0, 150 };
                    break;
                case HapticTypes.Warning:
                    timings = new long[] { 0, 200 };
                    amplitudes = new int[] { 0, 200 };
                    break;
                case HapticTypes.Failure:
                    timings = new long[] { 0, 40, 40, 40 };
                    amplitudes = new int[] { 0, 255, 0, 255 };
                    break;
                case HapticTypes.LightImpact:
                    timings = new long[] { 0, 50 };
                    amplitudes = new int[] { 0, 100 };
                    break;
                case HapticTypes.HeavyImpact:
                    timings = new long[] { 0, 200 };
                    amplitudes = new int[] { 0, 255 };
                    break;
                case HapticTypes.RigidImpact:
                    timings = new long[] { 0, 25 };
                    amplitudes = new int[] { 0, 255 };
                    break;
                case HapticTypes.SoftImpact:
                    timings = new long[] { 0, 80 };
                    amplitudes = new int[] { 0, 80 };
                    break;
                case HapticTypes.MediumImpact:
                default:
                    timings = new long[] { 0, 100 };
                    amplitudes = new int[] { 0, 180 };
                    break;
            }
        }
#endif
    }
}
