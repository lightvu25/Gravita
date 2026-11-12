using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;

[RequireComponent(typeof(RectTransform))]
public class UIPanelAnimator : MonoBehaviour
{
    public enum AnimationMode { Fade, Zoom, FadeAndZoom, Slide, FadeAndSlide, Glitch }

    [Header("Mode")]
    [SerializeField] private AnimationMode mode = AnimationMode.FadeAndZoom;
    [Tooltip("Play the show animation automatically when the object becomes active. Keep false when driven explicitly by presenters.")]
    [SerializeField] private bool playOnEnable = false;

    [Header("Glitch (AnimationMode.Glitch)")]
    [Tooltip("Pooled one-shot glitch frame animation prefab spawned on PlayIn / PlayOut when mode is Glitch.")]
    [SerializeField] private PooledGlitchTransitionEffect glitchEffectPrefab;
    [Tooltip("Optional parent Transform where the pooled glitch effect is spawned. Defaults to this panel's parent (or this panel itself).")]
    [SerializeField] private Transform glitchSpawnParent;

    [Header("Timing")]
    [SerializeField, Min(0.01f)] private float showDuration = 0.3f;
    [SerializeField, Min(0.01f)] private float hideDuration = 0.2f;
    [SerializeField, Min(0f)] private float showDelay;
    [SerializeField] private Ease showEase = Ease.OutBack;
    [SerializeField] private Ease hideEase = Ease.InQuad;

    [Header("Fade")]
    [Tooltip("CanvasGroup used for fading and input locking — auto-added when missing.")]
    [SerializeField] private CanvasGroup canvasGroup;

    [Header("Zoom")]
    [Tooltip("Scale the panel starts from (fraction of its designed scale).")]
    [SerializeField, Range(0f, 1.5f)] private float zoomFromScale = 0.7f;

    [Header("Slide")]
    [Tooltip("Anchored-position offset the panel slides in from and out to. (0,-800) enters from below the screen.")]
    [SerializeField] private Vector2 slideOffset = new Vector2(0f, -800f);

    [Header("Options")]
    [Tooltip("Deactivate the GameObject when PlayOut finishes (classic panel hide).")]
    [SerializeField] private bool deactivateOnHide = true;
    [Tooltip("Block raycasts/interaction while the panel is animating.")]
    [SerializeField] private bool lockInteractionWhileAnimating = true;

    [Header("Events")]
    [SerializeField] private UnityEvent onShowComplete;
    [SerializeField] private UnityEvent onHideComplete;

    private RectTransform rectTransform;
    private Vector3 baseScale;
    private Vector2 baseAnchoredPos;
    private Sequence activeSequence;

    private bool UsesFade => mode != AnimationMode.Zoom && mode != AnimationMode.Slide && mode != AnimationMode.Glitch;
    private bool UsesZoom => mode == AnimationMode.Zoom || mode == AnimationMode.FadeAndZoom;
    private bool UsesSlide => mode == AnimationMode.Slide || mode == AnimationMode.FadeAndSlide;
    private bool UsesGlitch => mode == AnimationMode.Glitch;

    private Transform ResolveGlitchSpawnParent()
    {
        if (glitchSpawnParent != null)
            return glitchSpawnParent;

        return transform.parent != null ? transform.parent : transform;
    }

    private void Awake()
    {
        rectTransform = (RectTransform)transform;
        baseScale = rectTransform.localScale;
        baseAnchoredPos = rectTransform.anchoredPosition;
        EnsureCanvasGroup();
    }

    private void OnEnable()
    {
        if (playOnEnable) PlayIn();
    }

    private void OnDisable() => KillActive();
    private void OnDestroy() => KillActive();

    /// <summary>Animate the panel in (fade/zoom/glitch to its designed state).</summary>
    public void PlayIn()
    {
        if (!gameObject.activeSelf) gameObject.SetActive(true);
        if (rectTransform == null) Awake();

        KillActive();
        SetInteractable(false);

        if (UsesGlitch)
        {
            rectTransform.localScale = baseScale;
            rectTransform.anchoredPosition = baseAnchoredPos;
            if (canvasGroup != null) canvasGroup.alpha = 1f;

            PooledGlitchTransitionEffect.SpawnAndPlay(
                glitchEffectPrefab,
                ResolveGlitchSpawnParent(),
                () =>
                {
                    SetInteractable(true);
                    onShowComplete?.Invoke();
                });
            return;
        }

        if (UsesZoom) rectTransform.localScale = baseScale * zoomFromScale;
        if (UsesSlide) rectTransform.anchoredPosition = baseAnchoredPos + slideOffset;
        if (UsesFade) canvasGroup.alpha = 0f;

        activeSequence = DOTween.Sequence().SetUpdate(true).SetDelay(showDelay).SetLink(gameObject);
        if (UsesZoom)
            activeSequence.Join(rectTransform.DOScale(baseScale, showDuration).SetEase(showEase).SetUpdate(true));
        if (UsesSlide)
            activeSequence.Join(rectTransform.DOAnchorPos(baseAnchoredPos, showDuration).SetEase(showEase).SetUpdate(true));
        if (UsesFade)
            activeSequence.Join(canvasGroup.DOFade(1f, showDuration).SetUpdate(true));

        activeSequence.OnComplete(() =>
        {
            SetInteractable(true);
            onShowComplete?.Invoke();
        });
    }

    /// <summary>Animate the panel out, then optionally deactivate it.</summary>
    public void PlayOut() => PlayOut(null);

    /// <summary>Animate the panel out and invoke <paramref name="onComplete"/> when done.</summary>
    public void PlayOut(System.Action onComplete)
    {
        // Already hidden (or mid-teardown) — run completion immediately rather
        // than starting tweens that would animate on an invisible object.
        if (!gameObject.activeInHierarchy)
        {
            KillActive();
            onHideComplete?.Invoke();
            onComplete?.Invoke();
            return;
        }

        if (rectTransform == null) Awake();

        KillActive();
        SetInteractable(false);

        if (UsesGlitch)
        {
            PooledGlitchTransitionEffect.SpawnAndPlay(
                glitchEffectPrefab,
                ResolveGlitchSpawnParent(),
                () =>
                {
                    onHideComplete?.Invoke();
                    onComplete?.Invoke();
                    if (deactivateOnHide) gameObject.SetActive(false);
                });
            return;
        }

        activeSequence = DOTween.Sequence().SetUpdate(true).SetLink(gameObject);
        if (UsesZoom)
            activeSequence.Join(rectTransform.DOScale(baseScale * zoomFromScale, hideDuration).SetEase(hideEase).SetUpdate(true));
        if (UsesSlide)
            activeSequence.Join(rectTransform.DOAnchorPos(baseAnchoredPos + slideOffset, hideDuration).SetEase(hideEase).SetUpdate(true));
        if (UsesFade)
            activeSequence.Join(canvasGroup.DOFade(0f, hideDuration).SetEase(hideEase).SetUpdate(true));

        activeSequence.OnComplete(() =>
        {
            onHideComplete?.Invoke();
            onComplete?.Invoke();
            if (deactivateOnHide) gameObject.SetActive(false);
        });
    }

    /// <summary>Jump straight to the fully-shown state, no animation.</summary>
    public void SnapShown()
    {
        KillActive();
        if (rectTransform == null) Awake();
        rectTransform.localScale = baseScale;
        if (UsesSlide) rectTransform.anchoredPosition = baseAnchoredPos;
        if (UsesFade) canvasGroup.alpha = 1f;
        SetInteractable(true);
    }

    /// <summary>Jump straight to the hidden state, no animation.</summary>
    public void SnapHidden()
    {
        KillActive();
        if (rectTransform == null) Awake();
        if (UsesZoom) rectTransform.localScale = baseScale * zoomFromScale;
        if (UsesSlide) rectTransform.anchoredPosition = baseAnchoredPos + slideOffset;
        if (UsesFade) canvasGroup.alpha = 0f;
        SetInteractable(false);
    }

    private void EnsureCanvasGroup()
    {
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
    }

    private void SetInteractable(bool value)
    {
        if (!lockInteractionWhileAnimating || canvasGroup == null) return;
        canvasGroup.interactable = value;
        canvasGroup.blocksRaycasts = value;
    }

    private void KillActive()
    {
        if (activeSequence != null)
        {
            activeSequence.Kill();
            activeSequence = null;
        }
    }
}
