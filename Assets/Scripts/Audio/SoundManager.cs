using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

public class SoundManager : MonoBehaviour
{
    private const int SOUND_VOLUME_MAX = 10;

    public static SoundManager Instance { get; private set; }

    private static int soundVolume = 6;
    private const string PREF_KEY = "SoundVolume";

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            if (GetComponent<MusicManager>() != null)
                Destroy(gameObject);
            else
                Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        soundVolume = PlayerPrefs.GetInt(PREF_KEY, 6);

        InitializePool();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        ApplyMixerVolume();
    }

    private void InitializePool()
    {
        if (fallbackAudioSource == null)
            fallbackAudioSource = GetComponent<AudioSource>();

        Transform container = poolContainer != null ? poolContainer : transform;
        AudioSource[] existingSources = container.GetComponentsInChildren<AudioSource>(true);
        for (int i = 0; i < existingSources.Length; i++)
        {
            AudioSource existing = existingSources[i];
            if (existing != null && !sfxPool.Contains(existing))
            {
                ConfigureAudioSource(existing);
                sfxPool.Add(existing);
            }
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

    private AudioSource GetAvailableAudioSource()
    {
        for (int i = sfxPool.Count - 1; i >= 0; i--)
        {
            AudioSource source = sfxPool[i];
            if (source == null)
            {
                sfxPool.RemoveAt(i);
                continue;
            }

            if (!source.isPlaying)
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

        float minInterval = Mathf.Max(0.01f, entry.minInterval);
        float now = Time.unscaledTime;
        if (lastPlayedTimeByClip.TryGetValue(clip, out float lastTime) && now - lastTime < minInterval)
            return;

        lastPlayedTimeByClip[clip] = now;
        PlayResolvedClip(clip, entry.GetResolvedVolume(), entry.GetResolvedPitch());
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

        float now = Time.unscaledTime;
        if (lastPlayedTimeByClip.TryGetValue(clip, out float lastTime) && now - lastTime < 0.015f)
            return;

        lastPlayedTimeByClip[clip] = now;
        float pitch = randomPitch ? AudioPitchUtility.GetRandomPitch() : 1f;
        PlayResolvedClip(clip, Mathf.Clamp01(volume), pitch);
    }

    private void PlayResolvedClip(AudioClip clip, float volume, float pitch)
    {
        if (clip == null || this == null || !gameObject.activeInHierarchy)
            return;

        AudioSource source = GetAvailableAudioSource();
        if (source == null)
            return;

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
    }

    public void SetSoundVolume(int newVolume)
    {
        soundVolume = Mathf.Clamp(newVolume, 0, SOUND_VOLUME_MAX);
        ApplyMixerVolume();
        OnSoundVolumeChanged?.Invoke(this, EventArgs.Empty);
    }

    public void SaveVolume()
    {
        PlayerPrefs.SetInt(PREF_KEY, soundVolume);
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
