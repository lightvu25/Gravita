using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Reusable full-screen fade curtain. Created lazily (no scene wiring needed),
/// persists across scene loads, and renders above all other UI.
///
/// FadeOut covers the screen and blocks raycasts; FadeIn reveals it and
/// restores interaction. Timing runs on unscaled time so transitions work
/// while <see cref="Time.timeScale"/> is zero (pause, hit-stop).
///
/// A pending completion callback is dropped when a newer fade supersedes it —
/// callers own their sequencing. As a safety net the curtain auto-reveals on
/// scene load so a cover can never outlive the sequence that created it.
/// </summary>
public class ScreenTransition : MonoBehaviour
{
    public static ScreenTransition Instance { get; private set; }

    [Header("Visuals")]
    [SerializeField] private Color fadeColor = Color.black;
    [SerializeField] private int sortingOrder = 30000;

    [Header("Timing")]
    [SerializeField, Min(0f)] private float defaultFadeDuration = 0.4f;
    [Tooltip("Automatically reveal after every scene load so a cover can " +
             "never survive the sequence that created it.")]
    [SerializeField] private bool autoRevealOnSceneLoad = true;

    private CanvasGroup canvasGroup;
    private Tween activeFade;
    private Action onFadeComplete;

    /// <summary>True while the screen is fully covered.</summary>
    public bool IsCovering => canvasGroup != null && canvasGroup.alpha >= 0.999f;

    /// <summary>True while a fade is animating.</summary>
    public bool IsFading => activeFade != null && activeFade.IsActive() && activeFade.IsPlaying();

    /// <summary>Existing instance, or lazily creates the overlay. Never null.</summary>
    public static ScreenTransition GetOrCreate()
    {
        if (Instance != null) return Instance;
        return new GameObject(nameof(ScreenTransition)).AddComponent<ScreenTransition>();
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject); // never allow a second curtain
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
        BuildOverlay();
        SceneManager.sceneLoaded += HandleSceneLoaded;
    }

    private void OnDestroy()
    {
        SceneManager.sceneLoaded -= HandleSceneLoaded;
        activeFade?.Kill();
        onFadeComplete = null;
        if (Instance == this) Instance = null;
    }

    private void BuildOverlay()
    {
        var canvas = gameObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = sortingOrder;

        canvasGroup = gameObject.AddComponent<CanvasGroup>();
        canvasGroup.alpha = 0f;
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var image = gameObject.AddComponent<Image>();
        image.color = fadeColor;
        image.raycastTarget = true; // blocks UI input while covered

        var rect = (RectTransform)transform;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (autoRevealOnSceneLoad && canvasGroup.alpha > 0f)
            FadeIn(defaultFadeDuration);
    }

    /// <summary>Animate to fully covered. Blocks raycasts for the duration.</summary>
    public void FadeOut(float duration = -1f, Action onComplete = null)
    {
        FadeTo(1f, duration < 0f ? defaultFadeDuration : duration, onComplete);
    }

    /// <summary>Animate back to transparent. Restores input when done.</summary>
    public void FadeIn(float duration = -1f, Action onComplete = null)
    {
        FadeTo(0f, duration < 0f ? defaultFadeDuration : duration, onComplete);
    }

    /// <summary>Snap to fully covered without animating.</summary>
    public void InstantCover()
    {
        CompleteFade(1f);
    }

    /// <summary>Snap to fully transparent without animating.</summary>
    public void InstantReveal()
    {
        CompleteFade(0f);
    }

    private void FadeTo(float target, float duration, Action onComplete)
    {
        // A superseding fade drops the stale callback — sequences are serial.
        activeFade?.Kill();
        onFadeComplete = onComplete;

        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        if (duration <= 0f)
        {
            CompleteFade(target);
            return;
        }

        activeFade = canvasGroup.DOFade(target, duration)
            .SetUpdate(true)
            .SetEase(Ease.InOutQuad)
            .OnComplete(() => CompleteFade(target));
    }

    private void CompleteFade(float target)
    {
        activeFade?.Kill();
        canvasGroup.alpha = target;

        bool covered = target >= 0.999f;
        canvasGroup.blocksRaycasts = covered;
        canvasGroup.interactable = covered;

        Action callback = onFadeComplete;
        onFadeComplete = null;
        callback?.Invoke();
    }
}
