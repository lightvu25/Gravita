using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dedicated UI Widget for a single Mission row in <see cref="MissionPanelUI"/>.
/// Uses <see cref="Slider"/> for progression display, colors each character of <see cref="progressText"/>
/// as the fill bar passes over it, and when claimed hides <see cref="claimButton"/>, shows
/// <see cref="claimedTickImage"/> to the right of the slider, shrinks the widget height, and moves it to the bottom.
/// </summary>
public class MissionRowWidget : MonoBehaviour
{
    [Header("Row Elements")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text titleText;
    [Tooltip("Read-only Slider displaying current mission progress (0 .. Target).")]
    [SerializeField] private Slider progressSlider;
    [Tooltip("Legacy fallback fill Image when progressSlider is unassigned.")]
    [SerializeField] private Image progressFill;
    [Tooltip("Numeric progress text (e.g. '1/3').")]
    [SerializeField] private TMP_Text progressText;
    [Tooltip("Reward amount text.")]
    [SerializeField] private TMP_Text rewardText;

    [Header("Progress Text Character Color")]
    [Tooltip("Color applied to each character of progressText as the Slider fill passes over it.")]
    [SerializeField] private Color progressCoveredTextColor = new Color(0.078f, 0.094f, 0.141f, 1f);

    [Header("Claim Button & Tick Image")]
    [SerializeField] private Button claimButton;
    [SerializeField] private TMP_Text claimButtonLabel;
    [Tooltip("Tick Image shown to the right of the Slider (positioned in Inspector) once the mission is claimed.")]
    [SerializeField] private Image claimedTickImage;

    [Header("Claim Animation & Size Adjustment")]
    [SerializeField] private CanvasGroup rowCanvasGroup;
    [SerializeField, Min(0.05f)] private float claimHideDuration = 0.22f;
    [Tooltip("Height (px) reduced from the row widget when claimButton is hidden. If <= 0, uses claimButton height.")]
    [SerializeField] private float claimedHeightReduction = 76f;
    [Tooltip("Optional width delta (px) applied to the row widget when claimButton is hidden.")]
    [SerializeField] private float claimedRowWidthOffset = 0f;
    [Tooltip("Optional width delta (px) applied to progressSlider when claimButton is hidden.")]
    [SerializeField] private float claimedSliderWidthOffset = 0f;
    [SerializeField] private bool hideRowCompletelyAfterPushToBottom = false;

    [Header("Button State Colors (100% Opacity)")]
    [SerializeField] private Color claimableButtonColor = new Color(0.18f, 0.78f, 0.38f, 1f);
    [SerializeField] private Color incompleteButtonColor = new Color(0.24f, 0.29f, 0.40f, 1f);
    [SerializeField] private Color claimedButtonColor = new Color(0.18f, 0.42f, 0.30f, 1f);

    private MissionDefinition missionDefinition;
    private Action<MissionDefinition> onClaimDefinition;
    private Sequence claimSequence;
    private bool buttonBound;

    private bool initialLayoutCaptured;
    private Vector2 initialRowSizeDelta;
    private float initialMinHeight = -1f;
    private float initialPreferredHeight = -1f;
    private Vector2 initialSliderSizeDelta;
    private Vector3 initialTickScale = Vector3.one;

    public MissionDefinition MissionDefinition => missionDefinition;
    public Button ClaimButton => claimButton;
    public Slider ProgressSlider => progressSlider;
    public Image ClaimedTickImage => claimedTickImage;

    private void Awake()
    {
        CaptureInitialLayoutIfNeeded();
        ConfigureNonBlockingSlider();
        if (claimButton != null)
            MissionMilestoneWidget.EnsureOpaqueButtonColors(claimButton);
    }

    private void OnDisable()
    {
        KillTweensAndRestoreTransform();
    }

    private void OnDestroy()
    {
        KillTweens();
        if (buttonBound && claimButton != null)
        {
            claimButton.onClick.RemoveListener(HandleClaimClicked);
            buttonBound = false;
        }
    }

    private void CaptureInitialLayoutIfNeeded()
    {
        if (initialLayoutCaptured)
            return;

        if (transform is RectTransform rowRect)
            initialRowSizeDelta = rowRect.sizeDelta;

        if (TryGetComponent(out LayoutElement layoutElement))
        {
            initialMinHeight = layoutElement.minHeight;
            initialPreferredHeight = layoutElement.preferredHeight;
        }

        if (progressSlider != null && progressSlider.transform is RectTransform sliderRect)
            initialSliderSizeDelta = sliderRect.sizeDelta;

        if (claimedTickImage != null)
        {
            Vector3 tickScale = claimedTickImage.transform.localScale;
            if (tickScale.sqrMagnitude > 0.001f)
                initialTickScale = tickScale;
        }

        initialLayoutCaptured = true;
    }

    private void KillTweens()
    {
        if (claimSequence != null)
        {
            claimSequence.Kill();
            claimSequence = null;
        }

        transform.DOKill();
        if (rowCanvasGroup != null)
            rowCanvasGroup.DOKill();
        if (claimButton != null)
            claimButton.transform.DOKill();
        if (claimedTickImage != null)
            claimedTickImage.transform.DOKill();
    }

    private void KillTweensAndRestoreTransform()
    {
        KillTweens();
        transform.localScale = Vector3.one;
        if (rowCanvasGroup != null)
            rowCanvasGroup.alpha = 1f;
        if (claimButton != null)
            claimButton.transform.localScale = Vector3.one;
        if (claimedTickImage != null)
            claimedTickImage.transform.localScale = initialTickScale;
    }

    private void ConfigureNonBlockingSlider()
    {
        if (progressSlider == null)
            return;

        progressSlider.interactable = false;
        progressSlider.transition = Selectable.Transition.None;

        Graphic[] sliderGraphics = progressSlider.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < sliderGraphics.Length; i++)
        {
            if (sliderGraphics[i] != null)
                sliderGraphics[i].raycastTarget = false;
        }
    }

    public void Setup(MissionDefinition definition, Action<MissionDefinition> claimCallback)
    {
        missionDefinition = definition;
        onClaimDefinition = claimCallback;

        CaptureInitialLayoutIfNeeded();
        KillTweensAndRestoreTransform();
        ConfigureNonBlockingSlider();

        if (claimButton != null && !buttonBound)
        {
            MissionMilestoneWidget.EnsureOpaqueButtonColors(claimButton);
            claimButton.onClick.AddListener(HandleClaimClicked);
            buttonBound = true;
        }

        Refresh();
    }

    private void HandleClaimClicked()
    {
        if (missionDefinition == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);
        onClaimDefinition?.Invoke(missionDefinition);
    }

    public void Refresh()
    {
        RefreshInternal(applyClaimedSizeImmediate: true);
    }

    private void RefreshInternal(bool applyClaimedSizeImmediate)
    {
        if (missionDefinition == null)
            return;

        CaptureInitialLayoutIfNeeded();

        int progress = MissionService.GetProgress(missionDefinition);
        bool claimed = MissionService.IsClaimed(missionDefinition);
        bool complete = MissionService.IsComplete(missionDefinition);

        int safeTarget = Mathf.Max(1, missionDefinition.Target);
        int clampedProgress = Mathf.Clamp(progress, 0, safeTarget);

        if (iconImage != null)
        {
            iconImage.SetSpriteNative(missionDefinition.Icon);
            iconImage.enabled = missionDefinition.Icon != null;
            iconImage.color = Color.white;
        }

        if (titleText != null)
            titleText.text = missionDefinition.Title ?? string.Empty;

        if (progressSlider != null)
        {
            progressSlider.minValue = 0f;
            progressSlider.maxValue = safeTarget;
            progressSlider.wholeNumbers = false;
            progressSlider.value = clampedProgress;
        }
        else if (progressFill != null)
        {
            progressFill.fillAmount = Mathf.Clamp01((float)clampedProgress / safeTarget);
        }

        if (progressText != null)
        {
            progressText.text = $"{clampedProgress}/{safeTarget}";
            UIProgressTextCharacterColorizer.EnsureAndRefresh(
                progressText,
                progressSlider,
                progressFill,
                progressCoveredTextColor);
        }

        if (rewardText != null)
            rewardText.text = missionDefinition.GetFormattedRewardText() ?? string.Empty;

        ApplyClaimButtonAndTickVisuals(complete, claimed, clampedProgress, safeTarget);

        if (applyClaimedSizeImmediate)
            ApplyRowDimensions(claimed ? 1f : 0f);
    }

    private void EnsureTickNotHiddenByClaimButton()
    {
        if (claimedTickImage != null && claimButton != null && claimedTickImage.transform.IsChildOf(claimButton.transform))
        {
            claimedTickImage.transform.SetParent(transform, true);
        }
    }

    private void ApplyClaimButtonAndTickVisuals(bool complete, bool claimed, int clampedProgress, int safeTarget)
    {
        if (claimed)
            EnsureTickNotHiddenByClaimButton();

        if (claimedTickImage != null)
        {
            claimedTickImage.gameObject.SetActive(claimed);
            claimedTickImage.enabled = claimed;
            if (claimed)
            {
                if (claimedTickImage.sprite != null)
                    claimedTickImage.SetNativeSize();
                claimedTickImage.color = Color.white;
            }
        }

        if (claimButton == null)
            return;

        if (claimed)
        {
            if (claimButtonLabel == null)
                claimButtonLabel = claimButton.GetComponentInChildren<TMP_Text>(true);

            if (claimButtonLabel != null && claimButtonLabel != rewardText)
            {
                claimButtonLabel.text = string.Empty;
                claimButtonLabel.gameObject.SetActive(false);
            }

            claimButton.interactable = false;
            claimButton.gameObject.SetActive(false);
            return;
        }

        claimButton.gameObject.SetActive(true);

        bool canClaim = complete && !claimed;
        MissionMilestoneWidget.EnsureOpaqueButtonColors(claimButton);
        claimButton.interactable = canClaim;

        if (claimButtonLabel == null)
            claimButtonLabel = claimButton.GetComponentInChildren<TMP_Text>(true);

        if (claimButtonLabel != null && claimButtonLabel != rewardText)
        {
            claimButtonLabel.gameObject.SetActive(true);
            claimButtonLabel.text = canClaim ? "CLAIM" : $"{clampedProgress}/{safeTarget}";
        }

        Image buttonImg = claimButton.targetGraphic as Image ?? claimButton.image;
        if (buttonImg != null)
        {
            buttonImg.color = complete ? claimableButtonColor : incompleteButtonColor;
        }
    }

    private float ResolveEffectiveHeightReduction()
    {
        if (claimedHeightReduction > 0.01f)
            return claimedHeightReduction;

        if (claimButton != null && claimButton.transform is RectTransform btnRect)
        {
            float btnH = btnRect.rect.height > 1f ? btnRect.rect.height : Mathf.Abs(btnRect.sizeDelta.y);
            if (btnH > 1f)
                return btnH;
        }

        return 0f;
    }

    private void ApplyRowDimensions(float claimedBlend)
    {
        claimedBlend = Mathf.Clamp01(claimedBlend);
        float heightReduction = ResolveEffectiveHeightReduction() * claimedBlend;
        float widthDelta = claimedRowWidthOffset * claimedBlend;

        if (transform is RectTransform rowRect)
        {
            float baseHeight = initialRowSizeDelta.y > 1f ? initialRowSizeDelta.y : rowRect.sizeDelta.y;
            float targetHeight = baseHeight > 1f ? Mathf.Max(40f, baseHeight - heightReduction) : rowRect.sizeDelta.y;
            rowRect.sizeDelta = new Vector2(initialRowSizeDelta.x + widthDelta, targetHeight);
        }

        if (TryGetComponent(out LayoutElement layoutElement))
        {
            if (initialPreferredHeight > 1f)
                layoutElement.preferredHeight = Mathf.Max(40f, initialPreferredHeight - heightReduction);
            if (initialMinHeight > 1f)
                layoutElement.minHeight = Mathf.Max(40f, initialMinHeight - heightReduction);
        }

        if (progressSlider != null && progressSlider.transform is RectTransform sliderRect && Mathf.Abs(claimedSliderWidthOffset) > 0.01f)
        {
            sliderRect.sizeDelta = new Vector2(
                initialSliderSizeDelta.x + (claimedSliderWidthOffset * claimedBlend),
                initialSliderSizeDelta.y);
        }
    }

    /// <summary>
    /// Hides <see cref="claimButton"/>, activates <see cref="claimedTickImage"/> to the right of the slider,
    /// shrinks the widget height smoothly, and pushes the mission row to the bottom of its container.
    /// </summary>
    public void PlayClaimAndMoveToBottomAnimation(Action onComplete = null)
    {
        KillTweensAndRestoreTransform();
        // Refresh visual state (hides claimButton, shows claimedTickImage) while starting height tween from 0 -> 1
        RefreshInternal(applyClaimedSizeImmediate: !Application.isPlaying);

        if (!Application.isPlaying)
        {
            MoveToBottomImmediate();
            onComplete?.Invoke();
            return;
        }

        ApplyRowDimensions(0f);

        float transitionDuration = Mathf.Max(0.05f, claimHideDuration);
        claimSequence = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(gameObject);

        if (claimedTickImage != null)
        {
            claimedTickImage.gameObject.SetActive(true);
            claimedTickImage.transform.localScale = initialTickScale * 0.35f;
            claimSequence.Append(
                claimedTickImage.transform
                    .DOScale(initialTickScale, 0.18f)
                    .SetEase(Ease.OutBack)
                    .SetUpdate(true));
        }

        float blend = 0f;
        Tween shrinkHeightTween = DOTween.To(
                () => blend,
                val =>
                {
                    blend = val;
                    ApplyRowDimensions(blend);
                    RebuildParentLayout();
                },
                1f,
                transitionDuration)
            .SetEase(Ease.OutQuad)
            .SetUpdate(true);

        claimSequence.Join(shrinkHeightTween);
        claimSequence.AppendCallback(MoveToBottomImmediate);

        claimSequence.OnComplete(() =>
        {
            claimSequence = null;
            onComplete?.Invoke();
        });
    }

    private void MoveToBottomImmediate()
    {
        ApplyRowDimensions(1f);
        transform.SetAsLastSibling();

        if (hideRowCompletelyAfterPushToBottom)
        {
            transform.localScale = Vector3.one;
            if (rowCanvasGroup != null)
                rowCanvasGroup.alpha = 1f;
            gameObject.SetActive(false);
        }
        else if (!Application.isPlaying)
        {
            transform.localScale = Vector3.one;
            if (rowCanvasGroup != null)
                rowCanvasGroup.alpha = 1f;
        }

        RebuildParentLayout();
    }

    private void RebuildParentLayout()
    {
        if (transform.parent is RectTransform parentRect)
            LayoutRebuilder.ForceRebuildLayoutImmediate(parentRect);
    }
}
