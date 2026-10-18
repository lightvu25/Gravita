using UnityEngine;

[RequireComponent(typeof(AudioSource))]
public sealed class RandomPitchAudio : MonoBehaviour
{
    [Header("Source")]
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip[] randomClips;
    [SerializeField] private bool playOnEnable;

    [Header("Pitch Variation")]
    [SerializeField, Range(0.1f, 3f)] private float minimumPitch = 0.9f;
    [SerializeField, Range(0.1f, 3f)] private float maximumPitch = 1.1f;

    private bool hasStarted;

    private void Awake()
    {
        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }

        TryApplyMixerRouting();
    }

    private void OnEnable()
    {
        TryApplyMixerRouting();

        if (playOnEnable && hasStarted)
        {
            PlayRandomClip();
        }
    }

    private void Start()
    {
        hasStarted = true;
        TryApplyMixerRouting();

        if (playOnEnable)
        {
            PlayRandomClip();
        }
    }

    public void Play()
    {
        if (audioSource == null || audioSource.clip == null) return;

        TryApplyMixerRouting();
        RandomizePitch();
        audioSource.Play();
    }

    public void PlayClip(AudioClip clip)
    {
        if (audioSource == null || clip == null) return;

        TryApplyMixerRouting();
        audioSource.clip = clip;
        RandomizePitch();
        audioSource.Play();
    }

    public void PlayRandomClip()
    {
        if (randomClips == null || randomClips.Length == 0) return;

        int startIndex = Random.Range(0, randomClips.Length);
        for (int i = 0; i < randomClips.Length; i++)
        {
            AudioClip clip = randomClips[(startIndex + i) % randomClips.Length];
            if (clip == null) continue;

            PlayClip(clip);
            return;
        }
    }

    public float RandomizePitch()
    {
        if (audioSource == null) return 1f;

        float pitch = AudioPitchUtility.GetRandomPitch(minimumPitch, maximumPitch);
        audioSource.pitch = pitch;
        return pitch;
    }

    private void TryApplyMixerRouting()
    {
        if (audioSource != null
            && audioSource.outputAudioMixerGroup == null
            && SoundManager.Instance != null)
        {
            audioSource.outputAudioMixerGroup = SoundManager.Instance.SfxMixerGroup;
        }
    }

    private void OnValidate()
    {
        minimumPitch = Mathf.Clamp(minimumPitch, 0.1f, 3f);
        maximumPitch = Mathf.Clamp(maximumPitch, minimumPitch, 3f);

        if (audioSource == null)
        {
            audioSource = GetComponent<AudioSource>();
        }
    }
}