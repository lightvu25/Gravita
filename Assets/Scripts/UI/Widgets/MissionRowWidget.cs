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
    private enum RowBindingMode
    {
        Mission = 0,
        Achievement = 1,
        EventTask = 2,
    }

    [Header("Row Elements")]
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text titleText;
    [Tooltip("Optional description text used when bound to Achievements or Event tasks.")]
    [SerializeField] private TMP_Text descriptionText;
    [Tooltip("Read-only Slider displaying current mission progress (0 .. Target).")]
    [SerializeField] private Slider progressSlider;
    [Tooltip("Legacy fallback fill Image when progressSlider is unassigned.")]
    [SerializeField] private Image progressFill;
    [Tooltip("Numeric progress text (e.g. '1/3').")]
    [SerializeField] private TMP_Text progressText;
    [Tooltip("Reward amount text.")]
    [SerializeField] private TMP_Text rewardText;
    [Tooltip("Optional icon displaying the reward type sprite.")]
    [SerializeField] private Image rewardIconImage;

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

    private RowBindingMode bindingMode = RowBindingMode.Mission;
    private MissionDefinition missionDefinition;
    private Action<MissionDefinition> onClaimDefinition;
    private AchievementDefinition achievementDefinition;
    private Action<AchievementDefinition> onClaimAchievement;
    private GameEventDefinition eventDefinition;
    private GameEventTaskEntry eventTaskEntry;
    private Action<GameEventDefinition, GameEventTaskEntry> onClaimEventTask;
    private Sequence claimSequence;
    private bool buttonBound;

    private bool initialLayoutCaptured;
    private Vector2 initialRowSizeDelta;
    private float initialMinHeight = -1f;
    private float initialPreferredHeight = -1f;
    private Vector2 initialSliderSizeDelta;
    private Vector3 initialTickScale = Vector3.one;

    public MissionDefinition MissionDefinition => missionDefinition;
    public AchievementDefinition AchievementDefinition => achievementDefinition;
    public string EventTaskId => eventTaskEntry.taskId;
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
        bindingMode = RowBindingMode.Mission;
        missionDefinition = definition;
        onClaimDefinition = claimCallback;
        achievementDefinition = null;
        onClaimAchievement = null;
        eventDefinition = null;
        eventTaskEntry = default;
        onClaimEventTask = null;

        PrepareRowBinding();
        Refresh();
    }

    public void SetupAchievement(AchievementDefinition definition, Action<AchievementDefinition> claimCallback)
    {
        bindingMode = RowBindingMode.Achievement;
        achievementDefinition = definition;
        onClaimAchievement = claimCallback;
        missionDefinition = null;
        onClaimDefinition = null;
        eventDefinition = null;
        eventTaskEntry = default;
        onClaimEventTask = null;

        PrepareRowBinding();
        Refresh();
    }

    public void SetupEventTask(
        GameEventDefinition parentEvent,
        GameEventTaskEntry task,
        Action<GameEventDefinition, GameEventTaskEntry> claimCallback)
    {
        bindingMode = RowBindingMode.EventTask;
        eventDefinition = parentEvent;
        eventTaskEntry = task;
        onClaimEventTask = claimCallback;
        missionDefinition = null;
        onClaimDefinition = null;
        achievementDefinition = null;
        onClaimAchievement = null;

        PrepareRowBinding();
        Refresh();
    }

    private void PrepareRowBinding()
    {
        CaptureInitialLayoutIfNeeded();
        KillTweensAndRestoreTransform();
        ConfigureNonBlockingSlider();

        if (claimButton != null && !buttonBound)
        {
            MissionMilestoneWidget.EnsureOpaqueButtonColors(claimButton);
            claimButton.onClick.AddListener(HandleClaimClicked);
            buttonBound = true;
        }
    }

    private void HandleClaimClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);

        switch (bindingMode)
        {
            case RowBindingMode.Achievement:
                if (achievementDefinition != null)
                    onClaimAchievement?.Invoke(achievementDefinition);
                break;

            case RowBindingMode.EventTask:
                if (eventDefinition != null && !string.IsNullOrEmpty(eventTaskEntry.taskId))
                    onClaimEventTask?.Invoke(eventDefinition, eventTaskEntry);
                break;

            default:
                if (missionDefinition != null)
                    onClaimDefinition?.Invoke(missionDefinition);
                break;
        }
    }

    public void Refresh()
    {
        RefreshInternal(applyClaimedSizeImmediate: true);
    }

    private void RefreshInternal(bool applyClaimedSizeImmediate)
    {
        switch (bindingMode)
        {
            case RowBindingMode.Achievement:
                if (achievementDefinition == null)
                    return;

                ApplyPresentation(
                    icon: achievementDefinition.Icon,
                    title: achievementDefinition.Title,
                    description: achievementDefinition.Description,
                    rawProgress: AchievementService.GetProgress(achievementDefinition),
                    rawTarget: achievementDefinition.Target,
                    rewardAmount: achievementDefinition.RewardAmount,
                    rewardSprite: achievementDefinition.RewardIcon,
                    complete: AchievementService.IsComplete(achievementDefinition),
                    claimed: AchievementService.IsClaimed(achievementDefinition),
                    allowClaimWhenComplete: true,
                    applyClaimedSizeImmediate: applyClaimedSizeImmediate);
                break;

            case RowBindingMode.EventTask:
                if (eventDefinition == null || string.IsNullOrEmpty(eventTaskEntry.taskId))
                    return;

                ApplyPresentation(
                    icon: eventTaskEntry.rewardIcon != null ? eventTaskEntry.rewardIcon : eventDefinition.BannerSprite,
                    title: eventTaskEntry.title,
                    description: string.Empty,
                    rawProgress: EventService.GetTaskProgress(eventDefinition, eventTaskEntry),
                    rawTarget: eventTaskEntry.target,
                    rewardAmount: eventTaskEntry.rewardAmount,
                    rewardSprite: eventTaskEntry.rewardIcon,
                    complete: EventService.IsTaskComplete(eventDefinition, eventTaskEntry),
                    claimed: EventService.IsTaskClaimed(eventDefinition, eventTaskEntry),
                    allowClaimWhenComplete: EventService.IsEventActive(eventDefinition),
                    applyClaimedSizeImmediate: applyClaimedSizeImmediate);
                break;

            default:
                if (missionDefinition == null)
                    return;

                ApplyPresentation(
                    icon: missionDefinition.Icon,
                    title: missionDefinition.Title,
                    description: missionDefinition.Description,
                    rawProgress: MissionService.GetProgress(missionDefinition),
                    rawTarget: missionDefinition.Target,
                    rewardAmount: missionDefinition.RewardAmount,
                    rewardSprite: missionDefinition.RewardIcon,
                    complete: MissionService.IsComplete(missionDefinition),
                    claimed: MissionService.IsClaimed(missionDefinition),
                    allowClaimWhenComplete: true,
                    applyClaimedSizeImmediate: applyClaimedSizeImmediate);
                break;
        }
    }

    private void ApplyPresentation(
        Sprite icon,
        string title,
        string description,
        int rawProgress,
        int rawTarget,
        int rewardAmount,
        Sprite rewardSprite,
        bool complete,
        bool claimed,
        bool allowClaimWhenComplete,
        bool applyClaimedSizeImmediate)
    {
        CaptureInitialLayoutIfNeeded();

        int safeTarget = Mathf.Max(1, rawTarget);
        int clampedProgress = Mathf.Clamp(rawProgress, 0, safeTarget);

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (titleText != null)
            titleText.text = title ?? string.Empty;

        if (descriptionText != null)
        {
            descriptionText.text = description ?? string.Empty;
            descriptionText.gameObject.SetActive(!string.IsNullOrEmpty(description));
        }

        if (rewardIconImage != null && rewardSprite != null)
        {
            rewardIconImage.sprite = rewardSprite;
            rewardIconImage.enabled = true;
        }

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
            rewardText.text = rewardAmount.ToString();

        ApplyClaimButtonAndTickVisuals(complete && allowClaimWhenComplete, claimed, clampedProgress, safeTarget);

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
                Color c = claimedTickImage.color;
                claimedTickImage.color = new Color(c.r, c.g, c.b, 1f);
            }
        }

        if (claimButton == null)
            return;

        TMP_Text label = (claimButtonLabel != null && claimButtonLabel != rewardText) ? claimButtonLabel : null;

        if (claimed)
        {
            if (label != null)
            {
                label.gameObject.SetActive(false);
            }

            claimButton.interactable = false;
            claimButton.gameObject.SetActive(false);
            return;
        }

        claimButton.gameObject.SetActive(true);

        bool canClaim = complete && !claimed;
        MissionMilestoneWidget.EnsureOpaqueButtonColors(claimButton);
        claimButton.interactable = canClaim;

        if (label != null)
        {
            label.gameObject.SetActive(true);
            if (string.IsNullOrEmpty(label.text))
                label.text = "CLAIM";
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
            float targetWidth = Mathf.Abs(claimedRowWidthOffset) > 0.01f
                ? initialRowSizeDelta.x + widthDelta
                : rowRect.sizeDelta.x;
            rowRect.sizeDelta = new Vector2(targetWidth, targetHeight);
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
