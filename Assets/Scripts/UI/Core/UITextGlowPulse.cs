using DG.Tweening;
using TMPro;
using UnityEngine;

/// <summary>
/// Pulsing glow/flicker for a TMP text. Drives a breathing alpha pulse plus
/// occasional random flicker dips, and optionally modulates the glow
/// intensity of an SDF font material that has GLOW_ON enabled (e.g.
/// "BoldPixels SDF Glow"). Runs on unscaled time so it works in Bootstrap
/// and pause screens.
/// </summary>
public class UITextGlowPulse : MonoBehaviour
{
    [Header("Target")]
    [Tooltip("Defaults to the TextMeshProUGUI on this GameObject.")]
    [SerializeField] private TextMeshProUGUI targetText;

    [Header("Pulse")]
    [SerializeField, Range(0f, 1f)] private float minAlpha = 0.4f;
    [SerializeField, Range(0f, 1f)] private float maxAlpha = 1f;
    [Tooltip("Seconds for one full fade-out (the fade-in mirrors it).")]
    [SerializeField, Min(0.05f)] private float pulseHalfDuration = 0.9f;
    [SerializeField] private Ease pulseEase = Ease.InOutSine;

    [Header("Flicker")]
    [Tooltip("Mean seconds between random flicker dips. 0 disables flicker.")]
    [SerializeField, Min(0f)] private float flickerInterval = 4f;
    [Tooltip("How much extra the alpha drops during a flicker dip.")]
    [SerializeField, Range(0f, 1f)] private float flickerDepth = 0.55f;
    [Tooltip("Duration of a single flicker dip-and-recover.")]
    [SerializeField, Min(0.02f)] private float flickerDipTime = 0.08f;
    [Tooltip("Random dips per flicker burst (rapid double-blink feels electric).")]
    [SerializeField, Range(1, 4)] private int flickerDipsPerBurst = 2;

    [Header("Glow (optional)")]
    [Tooltip("Scale the material's HDR _GlowColor with the pulse. Only works " +
             "with an SDF font material that has GLOW_ON enabled.")]
    [SerializeField] private bool modulateGlow = true;
    [SerializeField, Min(0f)] private float glowScaleMin = 0.4f;
    [SerializeField, Min(0f)] private float glowScaleMax = 1.2f;

    private static readonly int GlowColorId = Shader.PropertyToID("_GlowColor");

    private Color baseColor;
    private Color baseGlowColor;
    private Material fontMaterialInstance;
    private bool hasGlow;

    private float pulseT = 1f;      // 0..1 breathing value
    private float flickerT;         // 0..1 dip amount
    private Tween pulseTween;
    private Tween flickerTween;
    private float nextFlickerAt = float.PositiveInfinity;

    private void Awake()
    {
        if (targetText == null)
            targetText = GetComponent<TextMeshProUGUI>();
        if (targetText == null) { enabled = false; return; }

        baseColor = targetText.color;

        // fontMaterial returns a per-renderer instance copy — safe to animate.
        if (modulateGlow)
        {
            fontMaterialInstance = targetText.fontMaterial;
            hasGlow = fontMaterialInstance != null && fontMaterialInstance.HasProperty(GlowColorId);
            if (hasGlow) baseGlowColor = fontMaterialInstance.GetColor(GlowColorId);
        }
    }

    private void OnEnable()
    {
        pulseT = 1f;
        flickerT = 0f;
        ScheduleNextFlicker();

        pulseTween?.Kill();
        pulseTween = DOVirtual.Float(1f, 0f, pulseHalfDuration, v => pulseT = v)
            .SetEase(pulseEase)
            .SetLoops(-1, LoopType.Yoyo)
            .SetUpdate(true);
    }

    private void OnDisable()
    {
        pulseTween?.Kill();
        flickerTween?.Kill();
        targetText.color = baseColor;
        if (hasGlow) fontMaterialInstance.SetColor(GlowColorId, baseGlowColor);
    }

    private void Update()
    {
        if (flickerInterval > 0f && Time.unscaledTime >= nextFlickerAt)
        {
            ScheduleNextFlicker();
            StartFlickerBurst();
        }

        float t = Mathf.Clamp01(pulseT * (1f - flickerT * flickerDepth));
        targetText.color = new Color(baseColor.r, baseColor.g, baseColor.b,
            baseColor.a * Mathf.Lerp(minAlpha, maxAlpha, t));

        if (hasGlow)
        {
            float s = Mathf.Lerp(glowScaleMin, glowScaleMax, t);
            fontMaterialInstance.SetColor(GlowColorId, baseGlowColor * s);
        }
    }

    private void ScheduleNextFlicker()
    {
        // Randomized interval around the mean so flickers feel organic.
        nextFlickerAt = Time.unscaledTime + flickerInterval * Random.Range(0.4f, 1.6f);
    }

    private void StartFlickerBurst()
    {
        flickerTween?.Kill();
        var seq = DOTween.Sequence().SetUpdate(true);
        for (int i = 0; i < flickerDipsPerBurst; i++)
        {
            seq.Append(DOVirtual.Float(0f, 1f, flickerDipTime, v => flickerT = v));
            seq.Append(DOVirtual.Float(1f, 0f, flickerDipTime * 1.6f, v => flickerT = v));
        }
        flickerTween = seq;
    }

    private void OnDestroy()
    {
        pulseTween?.Kill();
        flickerTween?.Kill();
        if (fontMaterialInstance != null) Destroy(fontMaterialInstance);
    }
}
