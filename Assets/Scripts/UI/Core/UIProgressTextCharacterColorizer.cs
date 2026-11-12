using TMPro;
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public class UIProgressTextCharacterColorizer : MonoBehaviour
{
    private static readonly Color DefaultCoveredColor = new Color(0.078f, 0.094f, 0.141f, 1f); // #141824

    [Header("Targets")]
    [SerializeField] private TMP_Text targetText;
    [SerializeField] private Slider targetSlider;
    [SerializeField] private Image targetFillImage;

    [Header("Character Colors")]
    [Tooltip("Color applied to each character once the progress fill bar passes over it.")]
    [SerializeField] private Color coveredTextColor = DefaultCoveredColor;
    [Tooltip("If true, uses the TMP_Text's initial Inspector color for characters not yet reached by the fill bar.")]
    [SerializeField] private bool useInitialTextColorAsUncovered = true;
    [Tooltip("Fallback color for characters not yet reached by the fill bar when useInitialTextColorAsUncovered is false.")]
    [SerializeField] private Color uncoveredTextColor = Color.white;
    [Tooltip("If true, splits color between the left and right vertices of a single character when the fill edge cuts through the middle of that character.")]
    [SerializeField] private bool splitCharacterVertices = false;

    private readonly Vector3[] worldCorners = new Vector3[4];
    private bool initialColorCaptured;
    private bool sliderSubscribed;
    private bool textSubscribed;
    private float lastManualNormalizedFill = -1f;

    public TMP_Text TargetText => targetText;
    public Slider TargetSlider => targetSlider;
    public Image TargetFillImage => targetFillImage;

    public Color CoveredTextColor
    {
        get => coveredTextColor;
        set
        {
            coveredTextColor = value;
            Refresh();
        }
    }

    public Color UncoveredTextColor
    {
        get => uncoveredTextColor;
        set
        {
            uncoveredTextColor = value;
            useInitialTextColorAsUncovered = false;
            Refresh();
        }
    }

    /// <summary>
    /// Attaches or updates a <see cref="UIProgressTextCharacterColorizer"/> on <paramref name="text"/>
    /// and immediately refreshes per-character vertex colors for the current progress value.
    /// </summary>
    public static UIProgressTextCharacterColorizer EnsureAndRefresh(
        TMP_Text text,
        Slider slider,
        Image fillImage = null,
        Color? coveredColorOverride = null)
    {
        if (text == null)
            return null;

        if (!text.gameObject.TryGetComponent(out UIProgressTextCharacterColorizer colorizer))
            colorizer = text.gameObject.AddComponent<UIProgressTextCharacterColorizer>();

        if (coveredColorOverride.HasValue)
            colorizer.coveredTextColor = coveredColorOverride.Value;

        colorizer.Bind(text, slider, fillImage);
        colorizer.Refresh();
        return colorizer;
    }

    private void Awake()
    {
        if (targetText == null)
            targetText = GetComponent<TMP_Text>();
        if (targetSlider == null)
            targetSlider = GetComponentInParent<Slider>();

        CaptureInitialTextColorIfNeeded();
        SubscribeCallbacks();
    }

    private void OnEnable()
    {
        CaptureInitialTextColorIfNeeded();
        SubscribeCallbacks();
        Refresh();
    }

    private void OnDisable()
    {
        UnsubscribeCallbacks();
    }

    private void OnDestroy()
    {
        UnsubscribeCallbacks();
    }

    public void Bind(TMP_Text text, Slider slider, Image fillImage = null)
    {
        if (targetText != text || targetSlider != slider)
            UnsubscribeCallbacks();

        targetText = text != null ? text : GetComponent<TMP_Text>();
        targetSlider = slider;
        targetFillImage = fillImage;

        CaptureInitialTextColorIfNeeded();
        SubscribeCallbacks();
    }

    private void CaptureInitialTextColorIfNeeded()
    {
        if (initialColorCaptured || targetText == null)
            return;

        if (useInitialTextColorAsUncovered)
            uncoveredTextColor = targetText.color;

        initialColorCaptured = true;
    }

    private void SubscribeCallbacks()
    {
        if (!textSubscribed && targetText != null)
        {
            targetText.OnPreRenderText += HandlePreRenderText;
            textSubscribed = true;
        }

        if (!sliderSubscribed && targetSlider != null)
        {
            targetSlider.onValueChanged.AddListener(HandleSliderValueChanged);
            sliderSubscribed = true;
        }
    }

    private void UnsubscribeCallbacks()
    {
        if (textSubscribed && targetText != null)
        {
            targetText.OnPreRenderText -= HandlePreRenderText;
            textSubscribed = false;
        }

        if (sliderSubscribed && targetSlider != null)
        {
            targetSlider.onValueChanged.RemoveListener(HandleSliderValueChanged);
            sliderSubscribed = false;
        }
    }

    private void HandleSliderValueChanged(float _)
    {
        Refresh();
    }

    private void HandlePreRenderText(TMP_TextInfo textInfo)
    {
        ApplyCharacterColorsToTextInfo(textInfo);
    }

    public void SetNormalizedFill(float normalizedFill)
    {
        lastManualNormalizedFill = Mathf.Clamp01(normalizedFill);
        Refresh();
    }

    public void Refresh()
    {
        if (targetText == null)
            return;

        CaptureInitialTextColorIfNeeded();

        targetText.ForceMeshUpdate();
        TMP_TextInfo textInfo = targetText.textInfo;
        if (textInfo == null || textInfo.characterCount <= 0)
            return;

        if (ApplyCharacterColorsToTextInfo(textInfo))
            targetText.UpdateVertexData(TMP_VertexDataUpdateFlags.Colors32);
    }

    private float ResolveNormalizedFill()
    {
        if (targetSlider != null)
        {
            float span = targetSlider.maxValue - targetSlider.minValue;
            if (span > 0.0001f)
                return Mathf.Clamp01((targetSlider.value - targetSlider.minValue) / span);
            return 0f;
        }

        if (targetFillImage != null)
            return Mathf.Clamp01(targetFillImage.fillAmount);

        if (lastManualNormalizedFill >= 0f)
            return lastManualNormalizedFill;

        return 0f;
    }

    private RectTransform ResolveBarReferenceRect()
    {
        if (targetSlider != null)
        {
            if (targetSlider.fillRect != null && targetSlider.fillRect.parent is RectTransform fillAreaRect && fillAreaRect.rect.width > 1f)
                return fillAreaRect;

            if (targetSlider.transform is RectTransform sliderRect)
                return sliderRect;
        }

        if (targetFillImage != null)
            return targetFillImage.rectTransform;

        return targetText != null ? targetText.rectTransform : null;
    }

    private bool ApplyCharacterColorsToTextInfo(TMP_TextInfo textInfo)
    {
        if (textInfo == null || targetText == null)
            return false;

        int charCount = textInfo.characterCount;
        if (charCount <= 0)
            return false;

        float normalizedFill = ResolveNormalizedFill();
        Color32 covered32 = coveredTextColor;
        Color32 uncovered32 = uncoveredTextColor;

        float fillEdgeLocalX;
        if (normalizedFill <= 0.0001f)
        {
            fillEdgeLocalX = float.NegativeInfinity;
        }
        else if (normalizedFill >= 0.9999f)
        {
            fillEdgeLocalX = float.PositiveInfinity;
        }
        else
        {
            fillEdgeLocalX = ResolveFillEdgeLocalX(textInfo, normalizedFill);
        }

        bool anyModified = false;
        for (int i = 0; i < charCount; i++)
        {
            TMP_CharacterInfo charInfo = textInfo.characterInfo[i];
            if (!charInfo.isVisible)
                continue;

            int materialIndex = charInfo.materialReferenceIndex;
            int vertexIndex = charInfo.vertexIndex;

            if (materialIndex < 0 || materialIndex >= textInfo.meshInfo.Length)
                continue;

            Color32[] vertexColors = textInfo.meshInfo[materialIndex].colors32;
            if (vertexColors == null || vertexIndex + 3 >= vertexColors.Length)
                continue;

            float charLeftX = Mathf.Min(charInfo.bottomLeft.x, charInfo.topLeft.x);
            float charRightX = Mathf.Max(charInfo.bottomRight.x, charInfo.topRight.x);
            float charCenterX = (charLeftX + charRightX) * 0.5f;

            if (splitCharacterVertices)
            {
                Color32 leftColor = fillEdgeLocalX >= charLeftX ? covered32 : uncovered32;
                Color32 rightColor = fillEdgeLocalX >= charRightX ? covered32 : uncovered32;

                vertexColors[vertexIndex + 0] = leftColor;
                vertexColors[vertexIndex + 1] = leftColor;
                vertexColors[vertexIndex + 2] = rightColor;
                vertexColors[vertexIndex + 3] = rightColor;
            }
            else
            {
                Color32 charColor = fillEdgeLocalX >= charCenterX ? covered32 : uncovered32;
                vertexColors[vertexIndex + 0] = charColor;
                vertexColors[vertexIndex + 1] = charColor;
                vertexColors[vertexIndex + 2] = charColor;
                vertexColors[vertexIndex + 3] = charColor;
            }

            anyModified = true;
        }

        return anyModified;
    }

    private float ResolveFillEdgeLocalX(TMP_TextInfo textInfo, float normalizedFill)
    {
        RectTransform textRect = targetText.rectTransform;
        RectTransform barRect = ResolveBarReferenceRect();

        if (barRect != null && textRect != null)
        {
            barRect.GetWorldCorners(worldCorners);
            float leftLocalX = textRect.InverseTransformPoint(worldCorners[0]).x;
            float rightLocalX = textRect.InverseTransformPoint(worldCorners[2]).x;

            if (rightLocalX - leftLocalX > 1f)
                return Mathf.Lerp(leftLocalX, rightLocalX, normalizedFill);

            float fallbackWidth = barRect.rect.width > 1f ? barRect.rect.width : Mathf.Abs(barRect.sizeDelta.x);
            if (fallbackWidth > 1f)
            {
                float half = fallbackWidth * 0.5f;
                return Mathf.Lerp(-half, half, normalizedFill);
            }
        }

        if (textRect != null && textRect.rect.width > 1f)
            return Mathf.Lerp(textRect.rect.xMin, textRect.rect.xMax, normalizedFill);

        // Final fallback when RectTransforms have not been laid out yet (e.g. unit tests with 0 width):
        // interpolate across the rendered character bounds.
        float minCharX = float.MaxValue;
        float maxCharX = float.MinValue;
        for (int i = 0; i < textInfo.characterCount; i++)
        {
            TMP_CharacterInfo c = textInfo.characterInfo[i];
            if (!c.isVisible)
                continue;

            float lx = Mathf.Min(c.bottomLeft.x, c.topLeft.x);
            float rx = Mathf.Max(c.bottomRight.x, c.topRight.x);
            if (lx < minCharX) minCharX = lx;
            if (rx > maxCharX) maxCharX = rx;
        }

        if (maxCharX > minCharX)
            return Mathf.Lerp(minCharX, maxCharX, normalizedFill);

        return 0f;
    }
}
