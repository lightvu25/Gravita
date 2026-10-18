using System;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;

[System.Serializable]
public struct SceneMusicEntry
{
    [Tooltip("The exact name of the scene (as it appears in Build Settings).")]
    public string sceneName;

    [Tooltip("The music track to play when this scene loads. Leave empty to stop music.")]
    public AudioClip musicClip;

    [Tooltip("The ambient track to play when this scene loads. Leave empty to stop ambient.")]
    public AudioClip ambientClip;

    [Tooltip("Crossfade duration in seconds.")]
    public float fadeDuration;
}

public class MusicManager : MonoBehaviour
{
    public static MusicManager Instance { get; private set; }

    [Header("Audio Sources")]
    [SerializeField] private AudioSource musicSource;
    [SerializeField] private AudioSource ambientSource;
    
    private const int MUSIC_VOLUME_MAX = 10;

    private static float musicTime;
    private static int musicVolume = 4;
    private const string PREF_KEY = "MusicVolume";

    public event EventHandler OnMusicVolumeChanged;

    [Header("Mixer Routing")]
    [SerializeField] private AudioMixer audioMixer;
    [SerializeField] private AudioMixerGroup musicMixerGroup;
    [SerializeField] private string musicVolumeParam = "MusicVolume";

    [Header("Scene Music (assign clips per scene — no code needed!)")]
    [SerializeField] private SceneMusicEntry[] sceneMusicEntries;

    private Coroutine musicCrossfadeCoroutine;
    private Coroutine ambientCrossfadeCoroutine;

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
        musicVolume = PlayerPrefs.GetInt(PREF_KEY, 4);

        if (musicMixerGroup != null)
        {
            if (musicSource != null) musicSource.outputAudioMixerGroup = musicMixerGroup;
            if (ambientSource != null) ambientSource.outputAudioMixerGroup = musicMixerGroup;
        }

        if (musicSource != null) musicSource.time = musicTime;

        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (Instance == this)
            Instance = null;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        foreach (var entry in sceneMusicEntries)
        {
            if (entry.sceneName != scene.name) continue;

            float fade = entry.fadeDuration > 0f ? entry.fadeDuration : 0.5f;

            if (entry.musicClip != null)
                PlayMusicClip(entry.musicClip, fade);
            else
                StopMusic(fade);

            if (entry.ambientClip != null)
                PlayAmbientClip(entry.ambientClip, fade);
            else
                StopAmbient(fade);

            return;
        }
    }

    private void Start()
    {
        ApplyMixerVolume();
        
        if (musicCrossfadeCoroutine == null && musicSource != null)
            musicSource.volume = 1f;
            
        if (ambientCrossfadeCoroutine == null && ambientSource != null)
            ambientSource.volume = 1f;

        // Trigger for the first scene since Awake is too early for sceneLoaded
        OnSceneLoaded(SceneManager.GetActiveScene(), LoadSceneMode.Single);
    }

    private void Update()
    {
        if (musicSource != null && musicSource.isPlaying)
            musicTime = musicSource.time;
    }

    // ------------------------------------------------------------------ //
    //  Public API — clip-based (used by scene auto-play and external code) //
    // ------------------------------------------------------------------ //

    public void PlayMusicClip(AudioClip clip, float fadeDuration = 0.5f)
    {
        // Only skip if the SAME clip is already actively playing — not just assigned.
        if (clip == null || (musicSource.clip == clip && musicSource.isPlaying)) return;

        if (musicCrossfadeCoroutine != null)
            StopCoroutine(musicCrossfadeCoroutine);

        musicCrossfadeCoroutine = StartCoroutine(AnimateCrossfade(musicSource, clip, fadeDuration, isMusic: true));
    }

    public void PlayAmbientClip(AudioClip clip, float fadeDuration = 0.5f)
    {
        // Only skip if the SAME clip is already actively playing — not just assigned.
        if (clip == null || (ambientSource.clip == clip && ambientSource.isPlaying)) return;

        if (ambientCrossfadeCoroutine != null)
            StopCoroutine(ambientCrossfadeCoroutine);

        ambientCrossfadeCoroutine = StartCoroutine(AnimateCrossfade(ambientSource, clip, fadeDuration, isMusic: false));
    }

    public void StopMusic(float fadeDuration = 0.5f)
    {
        if (musicCrossfadeCoroutine != null) StopCoroutine(musicCrossfadeCoroutine);
        musicCrossfadeCoroutine = StartCoroutine(FadeOutAndStop(musicSource, fadeDuration));
    }

    public void StopAmbient(float fadeDuration = 0.5f)
    {
        if (ambientCrossfadeCoroutine != null) StopCoroutine(ambientCrossfadeCoroutine);
        ambientCrossfadeCoroutine = StartCoroutine(FadeOutAndStop(ambientSource, fadeDuration));
    }

    // ------------------------------------------------------------------ //
    //  Coroutines                                                         //
    // ------------------------------------------------------------------ //

    private IEnumerator AnimateCrossfade(AudioSource source, AudioClip nextTrack, float fadeDuration, bool isMusic)
    {
        float halfDuration = fadeDuration / 2f;
        float startVolume = source.volume;
        float elapsed = 0f;

        // Fade out
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / halfDuration);
            yield return null;
        }
        source.volume = 0f;

        // Switch track
        source.clip = nextTrack;
        if (isMusic) musicTime = 0f;
        source.Play();

        // Fade in
        elapsed = 0f;
        while (elapsed < halfDuration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(0f, 1f, elapsed / halfDuration);
            yield return null;
        }
        source.volume = 1f;

        if (isMusic) musicCrossfadeCoroutine = null;
        else ambientCrossfadeCoroutine = null;
    }

    private IEnumerator FadeOutAndStop(AudioSource source, float fadeDuration)
    {
        float startVolume = source.volume;
        float elapsed = 0f;
        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            source.volume = Mathf.Lerp(startVolume, 0f, elapsed / fadeDuration);
            yield return null;
        }
        source.volume = 0f;
        source.Stop();
        source.clip = null;
    }

    // ------------------------------------------------------------------ //
    //  Volume control                                                      //
    // ------------------------------------------------------------------ //

    public void SetMusicVolume(int newVolume)
    {
        musicVolume = Mathf.Clamp(newVolume, 0, MUSIC_VOLUME_MAX);
        ApplyMixerVolume();
        OnMusicVolumeChanged?.Invoke(this, EventArgs.Empty);
    }
    public void SaveVolume() { PlayerPrefs.SetInt(PREF_KEY, musicVolume); PlayerPrefs.Save(); }
    private void ApplyMixerVolume()
    {
        if (audioMixer != null)
        {
            float normalizedVolume = GetMusicVolumeNormalized();
            float dbValue = Mathf.Log10(Mathf.Max(0.0001f, normalizedVolume)) * 20f;
            audioMixer.SetFloat(musicVolumeParam, dbValue);
        }
    }
    public int GetMusicVolume() => musicVolume;
    public float GetMusicVolumeNormalized() => (float)musicVolume / MUSIC_VOLUME_MAX;

    public void DuckMusic(bool isDucking)
    {
        float targetVolume = isDucking ? 0.3f : 1f;
        musicSource.DOFade(targetVolume, 0.5f).SetUpdate(true);
    }

}
