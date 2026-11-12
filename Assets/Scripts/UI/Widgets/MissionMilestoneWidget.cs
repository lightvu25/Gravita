using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dedicated UI Widget for a Weekly Milestone slot along the progress Slider in <see cref="MissionPanelUI"/>.
/// Binds directly to <see cref="MissionMilestoneDefinition"/> ScriptableObject assets and keeps 100% icon opacity.
/// </summary>
public class MissionMilestoneWidget : MonoBehaviour
{
    [Header("ScriptableObject Milestone Data")]
    [SerializeField] private MissionMilestoneDefinition milestoneDefinition;

    [Header("Milestone Elements")]
    [SerializeField] private Button button;
    [SerializeField] private Image milestoneIconImage;
    [Tooltip("Optional label displaying the required completed mission count (e.g. '3', '7', '10').")]
    [SerializeField] private TMP_Text thresholdText;

    [Header("DOTween Feedback")]
    [SerializeField] private float claimableScale = 1.12f;
    [SerializeField] private float tweenDuration = 0.2f;
    [Tooltip("Opacity applied to the milestone icon after it has been claimed.")]
    [SerializeField, Range(0.1f, 1f)] private float claimedIconAlpha = 0.55f;

    private MissionMilestoneDefinition boundMilestone;
    private Action<MissionMilestoneDefinition> onClaimMilestoneClicked;
    private WeeklyGoalReward boundLegacyGoal;
    private Action<WeeklyGoalReward> onClaimLegacyClicked;
    private bool buttonBound;

    public MissionMilestoneDefinition MilestoneDefinition =>
        boundMilestone != null ? boundMilestone : milestoneDefinition;

    public string GoalId
    {
        get
        {
            if (MilestoneDefinition != null) return MilestoneDefinition.MilestoneId;
            return boundLegacyGoal != null ? boundLegacyGoal.goalId : string.Empty;
        }
    }

    private void Awake()
    {
        EnsureOpaqueButtonColors(button);
        EnsureOpaqueIcon();
    }

    private void OnDisable()
    {
        if (milestoneIconImage != null)
        {
            milestoneIconImage.transform.DOKill();
            milestoneIconImage.transform.localScale = Vector3.one;
        }
    }

    private void OnDestroy()
    {
        if (buttonBound && button != null)
        {
            button.onClick.RemoveListener(HandleClicked);
            buttonBound = false;
        }
    }

    public static void EnsureOpaqueButtonColors(Button targetButton)
    {
        if (targetButton == null)
            return;

        ColorBlock cb = targetButton.colors;
        cb.normalColor = Color.white;
        cb.highlightedColor = Color.white;
        cb.selectedColor = Color.white;
        cb.pressedColor = new Color(0.9f, 0.9f, 0.9f, 1f);
        cb.disabledColor = Color.white;
        cb.colorMultiplier = 1f;
        targetButton.colors = cb;
    }

    private void EnsureOpaqueIcon()
    {
        if (milestoneIconImage != null)
        {
            Color c = milestoneIconImage.color;
            milestoneIconImage.color = new Color(c.r, c.g, c.b, 1f);
        }
    }

    public void Bind(
        MissionMilestoneDefinition definition,
        bool isComplete,
        bool isClaimed,
        Action<MissionMilestoneDefinition> claimCallback)
    {
        boundMilestone = definition != null ? definition : milestoneDefinition;
        boundLegacyGoal = null;
        onClaimMilestoneClicked = claimCallback;
        onClaimLegacyClicked = null;

        if (boundMilestone == null)
            return;

        bool canClaim = isComplete && !isClaimed;
        Sprite activeSprite = (isComplete || isClaimed)
            ? boundMilestone.CompleteSprite
            : boundMilestone.IncompleteSprite;

        ApplyVisualState(activeSprite, boundMilestone.RequiredCompletedMissions, canClaim, isClaimed);
    }

    public void Bind(
        WeeklyGoalReward goal,
        bool isComplete,
        bool isClaimed,
        Action<WeeklyGoalReward> claimCallback)
    {
        boundLegacyGoal = goal;
        boundMilestone = null;
        onClaimLegacyClicked = claimCallback;
        onClaimMilestoneClicked = null;

        if (goal == null)
            return;

        bool canClaim = isComplete && !isClaimed;
        Sprite activeSprite = (isComplete || isClaimed) ? goal.completeSprite : goal.incompleteSprite;
        ApplyVisualState(activeSprite, goal.requiredPoints, canClaim, isClaimed);
    }

    private void ApplyVisualState(Sprite activeSprite, int thresholdValue, bool canClaim, bool isClaimed = false)
    {
        if (thresholdText != null)
            thresholdText.text = thresholdValue.ToString();

        if (milestoneIconImage != null)
        {
            if (activeSprite != null)
                milestoneIconImage.SetSpriteNative(activeSprite);

            float targetAlpha = isClaimed ? claimedIconAlpha : 1f;
            milestoneIconImage.color = new Color(1f, 1f, 1f, targetAlpha);

            milestoneIconImage.transform.DOKill();
            float targetScale = canClaim ? claimableScale : 1f;
            milestoneIconImage.transform
                .DOScale(targetScale, tweenDuration)
                .SetEase(canClaim ? Ease.OutBack : Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(milestoneIconImage.gameObject);
        }

        if (button != null)
        {
            EnsureOpaqueButtonColors(button);
            button.interactable = canClaim;
            if (!buttonBound)
            {
                button.onClick.AddListener(HandleClicked);
                buttonBound = true;
            }
        }
    }

    private void HandleClicked()
    {
        if (boundMilestone == null && boundLegacyGoal == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);

        if (milestoneIconImage != null)
        {
            milestoneIconImage.transform.DOKill();
            milestoneIconImage.transform.localScale = Vector3.one;
            milestoneIconImage.transform
                .DOPunchScale(new Vector3(0.12f, 0.12f, 0f), 0.2f, 6, 0.5f)
                .SetUpdate(true)
                .SetLink(milestoneIconImage.gameObject);
        }

        if (boundMilestone != null)
            onClaimMilestoneClicked?.Invoke(boundMilestone);
        else if (boundLegacyGoal != null)
            onClaimLegacyClicked?.Invoke(boundLegacyGoal);
    }
}
