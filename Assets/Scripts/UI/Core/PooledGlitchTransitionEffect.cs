using System;
using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Image))]
public class PooledGlitchTransitionEffect : MonoBehaviour
{
    [Header("Glitch Sprite Frames")]
    [SerializeField] private Image targetImage;
    [Tooltip("Sprite frames played sequentially once before returning to the Object Pool.")]
    [SerializeField] private Sprite[] frames;
    [SerializeField, Min(1f)] private float framesPerSecond = 24f;

    [Header("Layout Options")]
    [Tooltip("Keep true for full-screen or full-panel stretched overlays; set false if using native sprite size.")]
    [SerializeField] private bool stretchFullScreen = true;
    [Tooltip("When true (and stretchFullScreen is false), calls SetNativeSize() on each frame.")]
    [SerializeField] private bool setNativeSizePerFrame = false;

    private int currentFrame;
    private float timer;
    private bool isPlaying;
    private Action onCompleteCallback;

    public Image TargetImage => targetImage != null ? targetImage : (targetImage = GetComponent<Image>());
    public Sprite[] Frames
    {
        get => frames;
        set => frames = value;
    }

    public float FramesPerSecond
    {
        get => framesPerSecond;
        set => framesPerSecond = Mathf.Max(1f, value);
    }

    /// <summary>
    /// Spawns a <see cref="PooledGlitchTransitionEffect"/> from <see cref="ObjectPoolManager"/> (<see cref="ObjectPoolManager.PoolType.UI"/>)
    /// and plays its frame animation once before automatically returning it to the pool.
    /// </summary>
    public static PooledGlitchTransitionEffect SpawnAndPlay(
        PooledGlitchTransitionEffect prefab,
        Transform parent,
        Action onComplete = null)
    {
        if (prefab == null)
        {
            onComplete?.Invoke();
            return null;
        }

        PooledGlitchTransitionEffect instance = ObjectPoolManager.SpawnObject(
            prefab,
            Vector3.zero,
            Quaternion.identity,
            parent,
            ObjectPoolManager.PoolType.UI);

        if (instance == null)
        {
            onComplete?.Invoke();
            return null;
        }

        instance.transform.SetAsLastSibling();
        instance.Play(onComplete);
        return instance;
    }

    private void Awake()
    {
        if (targetImage == null)
            targetImage = GetComponent<Image>();

        if (targetImage != null)
            targetImage.raycastTarget = false;
    }

    private void OnEnable()
    {
        if (stretchFullScreen && transform is RectTransform rt)
        {
            StretchFullScreen(rt);
        }

        if (!isPlaying && frames != null && frames.Length > 0)
        {
            Play(null);
        }
    }

    private void OnDisable()
    {
        isPlaying = false;
        onCompleteCallback = null;
    }

    public void Play(Action onComplete = null)
    {
        onCompleteCallback = onComplete;
        currentFrame = 0;
        timer = 0f;

        if (stretchFullScreen && transform is RectTransform rt)
        {
            StretchFullScreen(rt);
        }

        if (frames == null || frames.Length == 0 || TargetImage == null)
        {
            CompleteAndReturnToPool();
            return;
        }

        isPlaying = true;
        TargetImage.enabled = true;
        ApplyFrame(0);

        if (!Application.isPlaying)
        {
            CompleteAndReturnToPool();
        }
    }

    private void Update()
    {
        if (!isPlaying)
            return;

        if (frames == null || frames.Length == 0 || TargetImage == null)
        {
            CompleteAndReturnToPool();
            return;
        }

        timer += Time.unscaledDeltaTime;
        float frameDuration = 1f / Mathf.Max(1f, framesPerSecond);

        while (timer >= frameDuration)
        {
            timer -= frameDuration;
            currentFrame++;

            if (currentFrame >= frames.Length)
            {
                CompleteAndReturnToPool();
                return;
            }

            ApplyFrame(currentFrame);
        }
    }

    private void ApplyFrame(int index)
    {
        if (frames == null || index < 0 || index >= frames.Length || TargetImage == null)
            return;

        Sprite frameSprite = frames[index];
        if (frameSprite == null)
            return;

        if (setNativeSizePerFrame && !stretchFullScreen)
        {
            TargetImage.SetSpriteNative(frameSprite);
        }
        else
        {
            TargetImage.sprite = frameSprite;
        }
    }

    private void CompleteAndReturnToPool()
    {
        isPlaying = false;
        Action callback = onCompleteCallback;
        onCompleteCallback = null;

        callback?.Invoke();

        if (gameObject.activeSelf)
        {
            ObjectPoolManager.ReturnObjectToPool(gameObject);
        }
    }

    private static void StretchFullScreen(RectTransform rect)
    {
        if (rect == null)
            return;

        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
