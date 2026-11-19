using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    private const int SOUND_VOLUME_MAX = 10;
    private const float MIN_PITCH = 0.5f;
    private const float MAX_PITCH = 2.0f;

    public static SoundManager Instance { get; private set; }
    public static Func<float> UnscaledTimeProvider = () => Time.unscaledTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticHooks()
    {
        UnscaledTimeProvider = () => Time.unscaledTime;
    }

    private static int soundVolume = 6;
    private static int previousSoundVolume = 6;
    private const string PREF_KEY = "SoundVolume";
    private const string PREV_PREF_KEY = "PreviousSoundVolume";

    public event EventHandler OnSoundVolumeChanged;

    [Header("Audio Catalog (ScriptableObject)")]
    [SerializeField] private SoundConfig soundConfig;
    public SoundConfig Config => soundConfig;

    [Header("Mixer Routing")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    public AudioMixerGroup SfxMixerGroup => sfxMixerGroup;
    [SerializeField] private string sfxVolumeParam = "SFXVolume";

    [Header("SFX Pooling (Inspector Wired)")]
    [Tooltip("Optional parent container for pooled AudioSources.")]
    [SerializeField] private Transform poolContainer;
    [Tooltip("Prefab with an AudioSource component used to populate/expand the SFX pool.")]
    [SerializeField] private AudioSource pooledAudioSourcePrefab;
    [Tooltip("Fallback or pre-wired AudioSource on this object.")]
    [SerializeField] private AudioSource fallbackAudioSource;
    [SerializeField] private int initialPoolSize = 10;
    [SerializeField] private List<AudioSource> sfxPool = new List<AudioSource>();

    private readonly Dictionary<AudioClip, float> lastPlayedTimeByClip = new Dictionary<AudioClip, float>();
    private readonly Dictionary<AudioSource, double> busyUntilDspBySource = new Dictionary<AudioSource, double>();

    private static float CurrentUnscaledTime => UnscaledTimeProvider != null ? UnscaledTimeProvider() : Time.unscaledTime;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Instance.AdoptMissingReferencesFrom(this);
            if (GetComponent<MusicManager>() != null)
                Destroy(gameObject);
            else
                Destroy(this);
            return;
        }

        Instance = this;
        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        soundVolume = Mathf.Clamp(PlayerPrefs.GetInt(PREF_KEY, 6), 0, SOUND_VOLUME_MAX);
        int defaultPrev = soundVolume > 0 ? soundVolume : 6;
        previousSoundVolume = Mathf.Clamp(PlayerPrefs.GetInt(PREV_PREF_KEY, defaultPrev), 1, SOUND_VOLUME_MAX);

        ResetPlaybackTracking();
        InitializePool();
    }

    private void OnDestroy()
    {
        busyUntilDspBySource.Clear();
        lastPlayedTimeByClip.Clear();
        if (Instance == this)
            Instance = null;
    }

    private void AdoptMissingReferencesFrom(SoundManager other)
    {
        if (other == null)
            return;

        if (soundConfig == null && other.soundConfig != null)
            soundConfig = other.soundConfig;

        if (audioMixer == null && other.audioMixer != null)
            audioMixer = other.audioMixer;

        if (sfxMixerGroup == null && other.sfxMixerGroup != null)
        {
            sfxMixerGroup = other.sfxMixerGroup;
            for (int i = 0; i < sfxPool.Count; i++)
                ConfigureAudioSource(sfxPool[i]);
        }

        if (poolContainer == null && other.poolContainer != null && sfxPool.Count == 0)
        {
            poolContainer = other.poolContainer;
            InitializePool();
        }
    }

    public void ResetPlaybackTracking()
    {
        lastPlayedTimeByClip.Clear();
        busyUntilDspBySource.Clear();
    }

    private void Start()
    {
        ApplyMixerVolume();
    }

    private void InitializePool()
    {
        if (fallbackAudioSource == null)
        {
            AudioSource rootSource = GetComponent<AudioSource>();
            if (rootSource != null && rootSource.GetComponent<RandomPitchAudio>() == null)
                fallbackAudioSource = rootSource;
        }

        if (Application.isPlaying && poolContainer == null && pooledAudioSourcePrefab == null)
        {
            Debug.LogWarning("[SoundManager] poolContainer is unassigned; scanning children while excluding MusicManager and RandomPitchAudio sources.", this);
        }

        Transform container = poolContainer != null ? poolContainer : transform;
        MusicManager siblingMusic = GetComponent<MusicManager>();
        if (siblingMusic == null)
            siblingMusic = GetComponentInParent<MusicManager>();

        AudioSource[] existingSources = container.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < existingSources.Length; i++)
        {
            AudioSource existing = existingSources[i];
            if (existing == null || sfxPool.Contains(existing))
                continue;

            if (siblingMusic != null && siblingMusic.OwnsAudioSource(existing))
                continue;

            if (existing.GetComponent<RandomPitchAudio>() != null)
                continue;

            ConfigureAudioSource(existing);
            sfxPool.Add(existing);
        }

        if (pooledAudioSourcePrefab != null)
        {
            while (sfxPool.Count < initialPoolSize)
            {
                SpawnPooledAudioSource();
            }
        }
    }

    private AudioSource SpawnPooledAudioSource()
    {
        if (pooledAudioSourcePrefab == null)
            return null;

        Transform parent = poolContainer != null ? poolContainer : transform;
        AudioSource source = Instantiate(pooledAudioSourcePrefab, parent);
        ConfigureAudioSource(source);
        sfxPool.Add(source);
        return source;
    }

    private void ConfigureAudioSource(AudioSource source)
    {
        if (source == null)
            return;

        source.spatialBlend = 0f;
        source.playOnAwake = false;

        if (sfxMixerGroup != null)
            source.outputAudioMixerGroup = sfxMixerGroup;
    }

    private bool IsSourceBusy(AudioSource source)
    {
        if (source == null)
            return false;

        if (source.isPlaying)
            return true;

        return busyUntilDspBySource.TryGetValue(source, out double busyUntilDsp)
            && AudioSettings.dspTime < busyUntilDsp;
    }

    private AudioSource GetAvailableAudioSource()
    {
        if (sfxPool.Count == 0 && poolContainer != null)
            InitializePool();

        for (int i = sfxPool.Count - 1; i >= 0; i--)
        {
            AudioSource source = sfxPool[i];
            if (source == null)
            {
                sfxPool.RemoveAt(i);
                continue;
            }

            if (!IsSourceBusy(source))
                return source;
        }

        if (pooledAudioSourcePrefab != null)
            return SpawnPooledAudioSource();

        return fallbackAudioSource != null ? fallbackAudioSource : (sfxPool.Count > 0 ? sfxPool[0] : null);
    }

    /// <summary>
    /// Plays a configured <see cref="SoundEntry"/> with pitch variance and anti-spam cooldown.
    /// </summary>
    public void PlaySFX(SoundEntry entry)
    {
        if (!entry.HasClips)
            return;

        AudioClip clip = entry.GetRandomClip();
        if (clip == null)
            return;

        PlayClipWithPitch(clip, entry.GetResolvedPitch(), entry.GetResolvedVolume(), entry.minInterval, bypassCooldown: false, scheduledDspDelay: 0f);
    }

    /// <summary>
    /// Plays a configured <see cref="SoundEntry"/> with an explicit pitch multiplier, optional volume scale,
    /// cooldown bypass, and DSP scheduling delay.
    /// </summary>
    public bool PlaySFXWithPitch(
        SoundEntry entry,
        float pitch,
        float volumeScale = 1f,
        bool bypassCooldown = false,
        float scheduledDspDelay = 0f)
    {
        if (!entry.HasClips)
            return false;

        AudioClip clip = entry.GetRandomClip();
        if (clip == null)
            return false;

        float resolvedVolume = entry.GetResolvedVolume() * Mathf.Clamp01(volumeScale);
        return PlayClipWithPitch(clip, pitch, resolvedVolume, entry.minInterval, bypassCooldown, scheduledDspDelay);
    }

    /// <summary>
    /// Plays an explicit <see cref="AudioClip"/> with clamped pitch, optional cooldown bypass,
    /// and optional DSP stagger delay for multi-voice chords/arpeggios.
    /// </summary>
    public bool PlayClipWithPitch(
        AudioClip clip,
        float pitch,
        float volume = 1f,
        float minInterval = 0.015f,
        bool bypassCooldown = false,
        float scheduledDspDelay = 0f)
    {
        if (clip == null)
            return false;

        float resolvedMinInterval = Mathf.Max(0.01f, minInterval);
        float now = CurrentUnscaledTime;
        if (!bypassCooldown && lastPlayedTimeByClip.TryGetValue(clip, out float lastTime) && now - lastTime < resolvedMinInterval)
            return false;

        lastPlayedTimeByClip[clip] = now;
        float clampedPitch = Mathf.Clamp(pitch, MIN_PITCH, MAX_PITCH);
        return PlayResolvedClip(clip, Mathf.Clamp01(volume), clampedPitch, scheduledDspDelay);
    }

    /// <summary>
    /// Plays a semantic UI sound from <see cref="Config"/> (or an override <see cref="SoundConfig"/>).
    /// </summary>
    public void PlayUISound(UISoundType soundType, SoundConfig overrideConfig = null)
    {
        if (soundType == UISoundType.None)
            return;

        SoundConfig activeConfig = overrideConfig != null ? overrideConfig : soundConfig;
        if (activeConfig == null)
            return;

        if (activeConfig.TryGetUISound(soundType, out SoundEntry entry))
        {
            PlaySFX(entry);
        }
    }

    /// <summary>
    /// Plays a global 2D SFX clip directly (e.g., world items, hazards, or custom clip references).
    /// </summary>
    public void PlaySFX(AudioClip clip, float volume = 1f, bool randomPitch = true)
    {
        if (clip == null)
            return;

        float pitch = randomPitch ? AudioPitchUtility.GetRandomPitch() : 1f;
        PlayClipWithPitch(clip, pitch, volume, 0.015f, bypassCooldown: false, scheduledDspDelay: 0f);
    }

    private bool PlayResolvedClip(AudioClip clip, float volume, float pitch, float scheduledDspDelay = 0f)
    {
        if (clip == null || this == null || !gameObject.activeInHierarchy)
            return false;

        AudioSource source = GetAvailableAudioSource();
        if (source == null)
            return false;

        double nowDsp = AudioSettings.dspTime;
        double delay = Math.Max(0.0, scheduledDspDelay);
        double clipDuration = clip.length > 0f ? clip.length / Math.Max(0.1, pitch) : 0.08;

        if (delay > 0.0001)
        {
            if (!IsSourceBusy(source))
            {
                source.clip = clip;
                source.volume = volume;
                source.pitch = pitch;
                source.PlayScheduled(nowDsp + delay);
                busyUntilDspBySource[source] = nowDsp + delay + clipDuration;
                return true;
            }

            if (Application.isPlaying)
            {
                StartCoroutine(PlayDelayedOneShotRoutine(source, clip, volume, pitch, (float)delay));
                return true;
            }
        }

        source.pitch = pitch;

        if (source.isPlaying)
        {
            source.PlayOneShot(clip, volume);
        }
        else
        {
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        busyUntilDspBySource[source] = nowDsp + clipDuration;
        return true;
    }

    private static IEnumerator PlayDelayedOneShotRoutine(
        AudioSource source,
        AudioClip clip,
        float volume,
        float pitch,
        float delaySeconds)
    {
        yield return new WaitForSecondsRealtime(delaySeconds);
        if (source == null || clip == null)
            yield break;

        source.pitch = pitch;
        source.PlayOneShot(clip, volume);
    }

    public void SetSoundVolume(int newVolume)
    {
        soundVolume = Mathf.Clamp(newVolume, 0, SOUND_VOLUME_MAX);
        ApplyMixerVolume();
        OnSoundVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public int GetPreviousSoundVolume()
    {
        return previousSoundVolume > 0 ? previousSoundVolume : 6;
    }

    public void SetPreviousSoundVolume(int newVolume)
    {
        previousSoundVolume = Mathf.Clamp(newVolume, 1, SOUND_VOLUME_MAX);
    }

    public void SaveVolume()
    {
        PlayerPrefs.SetInt(PREF_KEY, soundVolume);
        PlayerPrefs.SetInt(PREV_PREF_KEY, GetPreviousSoundVolume());
        PlayerPrefs.Save();
    }

    private void ApplyMixerVolume()
    {
        if (audioMixer != null)
        {
            float normalizedVolume = GetSoundVolumeNormalized();
            float dbValue = Mathf.Log10(Mathf.Max(0.0001f, normalizedVolume)) * 20f;
            audioMixer.SetFloat(sfxVolumeParam, dbValue);
        }
    }

    public int GetSoundVolume()
    {
        return soundVolume;
    }

    public float GetSoundVolumeNormalized()
    {
        return ((float)soundVolume) / SOUND_VOLUME_MAX;
    }
}
