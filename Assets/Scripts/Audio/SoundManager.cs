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

    [Header("Mixer Routing")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup sfxMixerGroup;

    public AudioMixerGroup SfxMixerGroup => sfxMixerGroup;
    [SerializeField] private string sfxVolumeParam = "SFXVolume";

    [Header("SFX Pooling")]
    [SerializeField] private int initialPoolSize = 10;
    private List<AudioSource> sfxPool = new List<AudioSource>();
    private Transform poolContainer;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        
        // Load volume from PlayerPrefs
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
        poolContainer = new GameObject("SFX_Pool").transform;
        poolContainer.SetParent(transform);

        for (int i = 0; i < initialPoolSize; i++)
        {
            CreateNewAudioSource();
        }
    }

    private AudioSource CreateNewAudioSource()
    {
        if (poolContainer == null) return null;
        
        GameObject go = new GameObject("PooledAudioSource");
        go.transform.SetParent(poolContainer);
        AudioSource source = go.AddComponent<AudioSource>();
        
        // Setup for 2D generic sounds
        source.spatialBlend = 0f; 
        source.playOnAwake = false;
        
        if (sfxMixerGroup != null)
            source.outputAudioMixerGroup = sfxMixerGroup;

        sfxPool.Add(source);
        return source;
    }

    private AudioSource GetAvailableAudioSource()
    {
        for (int i = sfxPool.Count - 1; i >= 0; i--)
        {
            var source = sfxPool[i];
            if (source == null)
            {
                sfxPool.RemoveAt(i);
                continue;
            }
            if (!source.isPlaying)
            {
                return source;
            }
        }
        
        // If all are playing, expand the pool
        return CreateNewAudioSource();
    }

    /// <summary>
    /// Plays a global 2D SFX (e.g., UI, Game Over, Item Pickups).
    /// </summary>
    public void PlaySFX(AudioClip clip, float volume = 1f, bool randomPitch = true)
    {
        if (clip == null) return;
        if (this == null || !gameObject.activeInHierarchy) return;

        AudioSource source = GetAvailableAudioSource();
        if (source == null) return;
        
        source.clip = clip;
        source.volume = volume;

        if (randomPitch)
            source.pitch = AudioPitchUtility.GetRandomPitch();
        else
            source.pitch = 1f;

        source.Play();
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
