using UnityEngine;

public class UIAudioManager : MonoBehaviour
{
    [SerializeField] private AudioClip openSound;
    [SerializeField] private AudioClip closeSound;
    [Range(0f, 1f)] [SerializeField] private float sfxVolume = 1f;

    [Header("Music Settings")]
    [SerializeField] private bool duckMusicOnOpen = true;

    private void OnEnable()
    {
        // Prevent all UI panels from playing their open sounds simultaneously when the scene first loads
        if (Time.timeSinceLevelLoad < 0.1f) return;

        if (openSound != null)
        {
            SoundManager.Instance.PlaySFX(openSound, sfxVolume);
        }

        if (duckMusicOnOpen)
        {
            MusicManager.Instance.DuckMusic(true);
        }
    }

    private void OnDisable()
    {
        // Prevent all UI panels from playing their close sounds simultaneously when the scene first loads
        if (Time.timeSinceLevelLoad < 0.1f) return;

        if (closeSound != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(closeSound, sfxVolume);
        }

        if (duckMusicOnOpen && MusicManager.Instance != null)
        {
            MusicManager.Instance.DuckMusic(false);
        }
    }
}