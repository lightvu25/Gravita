using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays a sequence of glitch Sprite frames on a UI Image using unscaled time
/// so it continues animating even when UIManager freezes Time.timeScale (e.g. Pause/Result).
/// </summary>
[RequireComponent(typeof(Image))]
public class UIGlitchSpriteAnimator : MonoBehaviour
{
    [Header("Sprite Frames")]
    [Tooltip("Assign your glitch sprite frames here in playback order.")]
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(1f)] private float framesPerSecond = 15f;
    [SerializeField] private bool loop = true;
    [Tooltip("Keep false for full-screen stretched backdrops; enable only if each frame should resize the Image to its native sprite dimensions.")]
    [SerializeField] private bool setNativeSizePerFrame = false;

    private Image targetImage;
    private int currentFrame;
    private float timer;

    public Sprite[] Frames
    {
        get => frames;
        set
        {
            frames = value;
            if (isActiveAndEnabled)
            {
                Restart();
            }
        }
    }

    public float FramesPerSecond
    {
        get => framesPerSecond;
        set => framesPerSecond = Mathf.Max(1f, value);
    }

    public Image TargetImage => targetImage != null ? targetImage : (targetImage = GetComponent<Image>());

    private void Awake()
    {
        targetImage = GetComponent<Image>();
    }

    private void OnEnable()
    {
        Restart();
    }

    public void Restart()
    {
        currentFrame = 0;
        timer = 0f;
        ApplyFrame(0);
    }

    private void Update()
    {
        if (frames == null || frames.Length == 0 || TargetImage == null)
            return;

        timer += Time.unscaledDeltaTime;
        float frameDuration = 1f / Mathf.Max(1f, framesPerSecond);

        if (timer >= frameDuration)
        {
            timer -= frameDuration;
            currentFrame++;

            if (currentFrame >= frames.Length)
            {
                if (!loop)
                {
                    currentFrame = frames.Length - 1;
                    return;
                }

                currentFrame = 0;
            }

            ApplyFrame(currentFrame);
        }
    }

    private void ApplyFrame(int index)
    {
        if (frames == null || frames.Length == 0 || TargetImage == null)
            return;

        int safeIndex = Mathf.Clamp(index, 0, frames.Length - 1);
        Sprite sprite = frames[safeIndex];
        if (sprite == null)
            return;

        if (setNativeSizePerFrame)
        {
            TargetImage.SetSpriteNative(sprite);
        }
        else
        {
            TargetImage.sprite = sprite;
        }
    }
}
