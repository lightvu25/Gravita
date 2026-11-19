using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

[Serializable]
public class MissionCategoryTabUI
{
    public MissionCategory category = MissionCategory.Weekly;
    public Button button;
    public Image backgroundImage;
    public TMP_Text categoryLabel;
}

/// <summary>
/// Mission Popup presenter (<see cref="UIPanelType.Mission"/>).
/// Binds strictly to <see cref="MissionDatabase"/>, <see cref="MissionMilestoneDefinition"/> ScriptableObjects,
/// and dedicated UI widgets (<see cref="MissionRowWidget"/>, <see cref="MissionDayTabWidget"/>, <see cref="MissionMilestoneWidget"/>).
/// </summary>
public class MissionPanelUI : MonoBehaviour, IUIPanel
{
    [Header("Data")]
    [SerializeField] private MissionDatabase missionDatabase;
    [Tooltip("Milestone ScriptableObject assigned to the final Chest button at the end of the weekly Slider.")]
    [SerializeField] private MissionMilestoneDefinition chestMilestoneDefinition;

    [Header("Header & Navigation")]
    [SerializeField] private Button closeButton;
    [SerializeField] private MissionCategory defaultCategory = MissionCategory.Weekly;
    [SerializeField] private List<MissionCategoryTabUI> categoryTabs = new List<MissionCategoryTabUI>();

    [Header("7-Day Selector Tabs (Optional)")]
    [SerializeField] private List<MissionDayTabWidget> dayTabWidgets = new List<MissionDayTabWidget>();
    [SerializeField] private Sprite lockedDaySprite;
    [SerializeField] private Color selectedDayColor = new Color(0.22f, 0.68f, 0.98f, 1f);
    [SerializeField] private Color unlockedDayColor = new Color(0.18f, 0.24f, 0.36f, 1f);
    [SerializeField] private Color lockedDayColor = new Color(0.10f, 0.12f, 0.18f, 0.85f);

    [Header("Top Weekly Reward Progress (Slider + ScriptableObject Milestones)")]
    [SerializeField] private Slider weeklyProgressSlider;
    [Tooltip("Legacy fallback fill Image when weeklyProgressSlider is unassigned.")]
    [SerializeField] private Image weeklyProgressFill;
    [FormerlySerializedAs("weeklyPointsText")]
    [SerializeField] private TMP_Text weeklyProgressText;
    [Tooltip("Color applied to each character of weeklyProgressText as the Slider fill passes over it.")]
    [SerializeField] private Color weeklyProgressCoveredTextColor = new Color(0.078f, 0.094f, 0.141f, 1f);
    [SerializeField] private TMP_Text weeklyResetTimeText;
    [Tooltip("Chest button at the end of MissionTopReward that claims the final milestone or next completed milestone.")]
    [SerializeField] private Button topRewardChestButton;
    [SerializeField] private Image topRewardChestImage;
    [Tooltip("Opacity applied to the top reward chest image after the final milestone has been claimed.")]
    [SerializeField, Range(0.1f, 1f)] private float claimedChestAlpha = 0.55f;
    [Tooltip("Milestone widgets along the progress Slider, each bound to a MissionMilestoneDefinition ScriptableObject.")]
    [SerializeField] private List<MissionMilestoneWidget> milestoneWidgets = new List<MissionMilestoneWidget>();
    [Tooltip("Automatically position milestone widgets along the weeklyProgressSlider width by RequiredCompletedMissions / MaxTasks.")]
    [SerializeField] private bool autoPositionMilestonesOnSlider = true;

    [Header("Mission List")]
    [SerializeField] private Transform rowContainer;
    [SerializeField] private GameObject rowPrefab;
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    [Header("Debug")]
    [Tooltip("0 = use real calendar weekday (Mon=1 .. Sun=7).")]
    [Range(0, 7)]
    [SerializeField] private int debugDayOfWeekOverride = 0;

    private readonly List<MissionRowWidget> spawnedRows = new List<MissionRowWidget>();
    private readonly List<MissionRowWidget> rowPool = new List<MissionRowWidget>();
    private readonly List<UnityEngine.Events.UnityAction> categoryTabActions = new List<UnityEngine.Events.UnityAction>();
    private MissionCategory selectedCategory = MissionCategory.Weekly;
    private int selectedDayIndex = 1;
    private bool buttonsBound;
    private bool subscribed;
    private bool isAnimatingRowClaim;

    public MissionCategory SelectedCategory => selectedCategory;
    public int SelectedDayIndex => selectedDayIndex;
    public Slider WeeklyProgressSlider => weeklyProgressSlider;

    private void Awake()
    {
        ConfigureNonBlockingWeeklySlider();
        EnsureChestOpaque();

        selectedCategory = defaultCategory;
        ApplyDebugDayOverride();
        MissionService.EnsureProfileMigratedAndUpdated();
        selectedDayIndex = MissionService.GetCurrentDayOfWeekIndex();
        BindButtons();
        BindDayTabWidgets();
    }

    private void OnEnable()
    {
        ApplyDebugDayOverride();
        MissionService.EnsureProfileMigratedAndUpdated();
        ConfigureNonBlockingWeeklySlider();
        EnsureChestOpaque();
        BindButtons();
        BindDayTabWidgets();

        if (selectedDayIndex < 1 || selectedDayIndex > 7)
            selectedDayIndex = MissionService.GetCurrentDayOfWeekIndex();

        Subscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
    }

    private void OnValidate()
    {
        if (Application.isPlaying)
            ApplyDebugDayOverride();
    }

    private void ConfigureNonBlockingWeeklySlider()
    {
        if (weeklyProgressSlider == null)
            return;

        weeklyProgressSlider.interactable = false;
        weeklyProgressSlider.transition = Selectable.Transition.None;

        Graphic[] graphics = weeklyProgressSlider.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].raycastTarget = false;
        }
    }

    private void EnsureChestOpaque()
    {
        if (topRewardChestButton != null)
        {
            MissionMilestoneWidget.EnsureOpaqueButtonColors(topRewardChestButton);
            if (topRewardChestImage == null)
                topRewardChestImage = topRewardChestButton.targetGraphic as Image ?? topRewardChestButton.image;
        }

        if (topRewardChestImage != null)
            topRewardChestImage.color = Color.white;
    }

    public void Show()
    {
        BindButtons();
        if (panelAnimator != null)
            panelAnimator.PlayIn();
        else
            gameObject.SetActive(true);

        RefreshAll();
    }

    public void Hide()
    {
        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(() => gameObject.SetActive(false));
        else
            gameObject.SetActive(false);
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (topRewardChestButton != null)
            topRewardChestButton.onClick.AddListener(OnTopRewardChestClicked);

        categoryTabActions.Clear();
        for (int i = 0; i < categoryTabs.Count; i++)
        {
            MissionCategoryTabUI tab = categoryTabs[i];
            if (tab == null || tab.button == null)
            {
                categoryTabActions.Add(null);
                continue;
            }

            MissionCategory captured = tab.category;
            UnityEngine.Events.UnityAction action = () =>
            {
                GameHaptics.Play(HapticEvent.UISelect);
                SelectCategory(captured);
            };
            categoryTabActions.Add(action);
            tab.button.onClick.AddListener(action);
        }
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);

        if (topRewardChestButton != null)
            topRewardChestButton.onClick.RemoveListener(OnTopRewardChestClicked);

        for (int i = 0; i < categoryTabs.Count && i < categoryTabActions.Count; i++)
        {
            MissionCategoryTabUI tab = categoryTabs[i];
            UnityEngine.Events.UnityAction action = categoryTabActions[i];
            if (tab != null && tab.button != null && action != null)
                tab.button.onClick.RemoveListener(action);
        }
        categoryTabActions.Clear();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged += RefreshAll;

        MissionService.OnWeeklyProgressChanged += RefreshAll;
        MissionService.OnMissionProgressChanged += RefreshAll;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged -= RefreshAll;

        MissionService.OnWeeklyProgressChanged -= RefreshAll;
        MissionService.OnMissionProgressChanged -= RefreshAll;
    }

    private void ApplyDebugDayOverride()
    {
        MissionService.DebugOverrideDayIndex = debugDayOfWeekOverride;
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Mission))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Mission);
        else
            Hide();
    }

    private void OnTopRewardChestClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);

        if (chestMilestoneDefinition != null && MissionService.TryClaimMilestone(chestMilestoneDefinition, missionDatabase))
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshAll();
            if (chestMilestoneDefinition.RewardType == MissionRewardType.Coins && chestMilestoneDefinition.RewardAmount > 0)
            {
                CoinUI.TryShowCoinRewardPopup(chestMilestoneDefinition.RewardAmount, alreadyGrantedBase: true);
            }
            return;
        }

        MissionMilestoneDefinition nextClaimable = GetFirstClaimableMilestone();
        if (nextClaimable != null && MissionService.TryClaimMilestone(nextClaimable, missionDatabase))
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshAll();
            if (nextClaimable.RewardType == MissionRewardType.Coins && nextClaimable.RewardAmount > 0)
            {
                CoinUI.TryShowCoinRewardPopup(nextClaimable.RewardAmount, alreadyGrantedBase: true);
            }
            return;
        }

        WeeklyGoalReward legacyClaimable = GetFirstClaimableLegacyGoal();
        if (legacyClaimable != null && MissionService.TryClaimGoal(legacyClaimable))
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshAll();
            if (legacyClaimable.rewardType == MissionRewardType.Coins && legacyClaimable.rewardAmount > 0)
            {
                CoinUI.TryShowCoinRewardPopup(legacyClaimable.rewardAmount, alreadyGrantedBase: true);
            }
            return;
        }

        UIAudio.Play(UISoundType.ErrorOrLocked);
    }

    private void BindDayTabWidgets()
    {
        for (int i = 0; i < dayTabWidgets.Count; i++)
        {
            MissionDayTabWidget widget = dayTabWidgets[i];
            if (widget == null) continue;

            int day = widget.DayIndex >= 1 && widget.DayIndex <= 7 ? widget.DayIndex : (i + 1);
            widget.Bind(day, SelectDay);
        }
    }

    public void SelectCategory(MissionCategory category, bool playSound = true)
    {
        selectedCategory = category;
        if (playSound)
            UIAudio.Play(UISoundType.TabSwitch);

        RefreshCategoryTabs();
        RefreshWeeklyProgressBar();
        RefreshDayTabs();
        RefreshList();
    }

    public void SelectDay(int dayIndex)
    {
        int clamped = Mathf.Clamp(dayIndex, 1, 7);
        if (!MissionService.IsDayUnlocked(clamped))
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
            return;
        }

        selectedDayIndex = clamped;
        UIAudio.Play(UISoundType.TabSwitch);
        RefreshDayTabs();
        RefreshList();
    }

    public void RefreshAll()
    {
        if (isAnimatingRowClaim)
            return;

        ApplyDebugDayOverride();
        MissionService.EnsureProfileMigratedAndUpdated();
        RefreshCategoryTabs();
        RefreshWeeklyProgressBar();
        RefreshDayTabs();
        RefreshList();
    }

    public void RefreshCategoryTabs()
    {
        for (int i = 0; i < categoryTabs.Count; i++)
        {
            MissionCategoryTabUI tab = categoryTabs[i];
            if (tab == null) continue;

            if (tab.button != null && !tab.button.gameObject.activeSelf)
                tab.button.gameObject.SetActive(true);

            bool isSelected = tab.category == selectedCategory;

            if (tab.categoryLabel != null)
            {
                tab.categoryLabel.text = GetCategoryDisplayName(tab.category);
                tab.categoryLabel.color = isSelected ? Color.white : new Color(0.82f, 0.86f, 0.94f, 1f);
            }

            if (tab.button != null)
            {
                tab.button.interactable = true;
                tab.button.transform.DOKill();
                float targetScale = isSelected ? 1.04f : 1f;
                tab.button.transform
                    .DOScale(targetScale, 0.18f)
                    .SetEase(isSelected ? Ease.OutBack : Ease.OutQuad)
                    .SetUpdate(true)
                    .SetLink(tab.button.gameObject);
            }

            if (tab.backgroundImage != null)
                tab.backgroundImage.color = isSelected ? selectedDayColor : unlockedDayColor;
        }
    }

    private static string GetCategoryDisplayName(MissionCategory category)
    {
        return category switch
        {
            MissionCategory.NewPlayer   => "NEW PLAYER",
            MissionCategory.Daily       => "DAILY",
            MissionCategory.Weekly      => "WEEKLY",
            MissionCategory.Achievement => "ACHIEVEMENT",
            MissionCategory.Sector      => "SECTOR",
            _                           => category.ToString().ToUpperInvariant()
        };
    }

    private bool HasMilestoneScriptableObjects()
    {
        if (chestMilestoneDefinition != null)
            return true;

        if (missionDatabase != null && missionDatabase.Milestones != null && missionDatabase.Milestones.Count > 0)
            return true;

        for (int i = 0; i < milestoneWidgets.Count; i++)
        {
            if (milestoneWidgets[i] != null && milestoneWidgets[i].MilestoneDefinition != null)
                return true;
        }

        return false;
    }

    private int ResolveMaxMilestoneTasks()
    {
        int max = missionDatabase != null ? missionDatabase.GetMaxMilestoneRequiredMissions() : 0;

        if (chestMilestoneDefinition != null && chestMilestoneDefinition.RequiredCompletedMissions > max)
            max = chestMilestoneDefinition.RequiredCompletedMissions;

        for (int i = 0; i < milestoneWidgets.Count; i++)
        {
            MissionMilestoneWidget w = milestoneWidgets[i];
            if (w != null && w.MilestoneDefinition != null && w.MilestoneDefinition.RequiredCompletedMissions > max)
                max = w.MilestoneDefinition.RequiredCompletedMissions;
        }

        return max > 0 ? max : 14;
    }

    public void RefreshWeeklyProgressBar()
    {
        SetWeeklyTopDecorationsVisible(true);

        if (weeklyResetTimeText != null)
        {
            bool hasCumulativeWeekly = missionDatabase != null && missionDatabase.HasMonthlyCumulativeWeeklyMissions();
            TimeSpan rem = (selectedCategory == MissionCategory.Weekly && hasCumulativeWeekly)
                ? MissionService.GetTimeRemainingUntilNextMonthMidnight()
                : MissionService.GetTimeRemainingUntilNextMondayMidnight();
            weeklyResetTimeText.text = $"End in: {(int)rem.TotalDays}d {rem.Hours}h";
        }

        if (HasMilestoneScriptableObjects())
        {
            int completedTasks = MissionService.GetWeeklyCompletedMissionCount(missionDatabase);
            int maxTasks = Mathf.Max(1, ResolveMaxMilestoneTasks());
            int clampedTasks = Mathf.Clamp(completedTasks, 0, maxTasks);

            if (weeklyProgressSlider != null)
            {
                weeklyProgressSlider.minValue = 0f;
                weeklyProgressSlider.maxValue = maxTasks;
                weeklyProgressSlider.wholeNumbers = false;
                weeklyProgressSlider.value = clampedTasks;
            }
            else if (weeklyProgressFill != null)
            {
                weeklyProgressFill.fillAmount = Mathf.Clamp01((float)clampedTasks / maxTasks);
            }

            if (weeklyProgressText != null)
            {
                weeklyProgressText.text = $"{completedTasks}/{maxTasks}";
                UIProgressTextCharacterColorizer.EnsureAndRefresh(
                    weeklyProgressText,
                    weeklyProgressSlider,
                    weeklyProgressFill,
                    weeklyProgressCoveredTextColor);
            }

            IReadOnlyList<MissionMilestoneDefinition> dbMilestones =
                missionDatabase != null ? missionDatabase.Milestones : null;

            RectTransform sliderRect = weeklyProgressSlider != null
                ? weeklyProgressSlider.transform as RectTransform
                : null;

            for (int i = 0; i < milestoneWidgets.Count; i++)
            {
                MissionMilestoneWidget widget = milestoneWidgets[i];
                if (widget == null) continue;

                MissionMilestoneDefinition def = widget.MilestoneDefinition;
                if (def == null && dbMilestones != null && i < dbMilestones.Count)
                    def = dbMilestones[i];

                if (def == null) continue;

                bool isComplete = MissionService.IsMilestoneComplete(def, missionDatabase);
                bool isClaimed = MissionService.IsMilestoneClaimed(def);
                widget.Bind(def, isComplete, isClaimed, OnMilestoneClicked);

                if (autoPositionMilestonesOnSlider && sliderRect != null && widget.transform.parent == sliderRect.parent)
                {
                    PositionMilestoneAlongSlider(widget.transform as RectTransform, sliderRect, def.RequiredCompletedMissions, maxTasks);
                }
            }

            RefreshChestButtonForMilestones();
            return;
        }

        // Legacy fallback when no MissionMilestoneDefinition assets are wired
        int currentPoints = MissionService.GetWeeklyPoints();
        int maxPoints = missionDatabase != null ? Mathf.Max(1, missionDatabase.GetMaxGoalPoints()) : 100;

        if (weeklyProgressSlider != null)
        {
            weeklyProgressSlider.minValue = 0f;
            weeklyProgressSlider.maxValue = maxPoints;
            weeklyProgressSlider.value = Mathf.Clamp(currentPoints, 0, maxPoints);
        }
        else if (weeklyProgressFill != null)
        {
            weeklyProgressFill.fillAmount = Mathf.Clamp01((float)currentPoints / maxPoints);
        }

        if (weeklyProgressText != null)
        {
            weeklyProgressText.text = $"{currentPoints}/{maxPoints}";
            UIProgressTextCharacterColorizer.EnsureAndRefresh(
                weeklyProgressText,
                weeklyProgressSlider,
                weeklyProgressFill,
                weeklyProgressCoveredTextColor);
        }

        if (topRewardChestButton != null)
        {
            EnsureChestOpaque();
            WeeklyGoalReward claimable = GetFirstClaimableLegacyGoal();
            topRewardChestButton.interactable = claimable != null;
        }

        if (missionDatabase != null && missionDatabase.GoalRewards != null && milestoneWidgets.Count > 0)
        {
            IReadOnlyList<WeeklyGoalReward> goals = missionDatabase.GoalRewards;
            for (int i = 0; i < goals.Count && i < milestoneWidgets.Count; i++)
            {
                WeeklyGoalReward goal = goals[i];
                MissionMilestoneWidget widget = milestoneWidgets[i];
                if (goal == null || widget == null) continue;

                widget.Bind(
                    goal,
                    MissionService.IsGoalComplete(goal),
                    MissionService.IsGoalClaimed(goal),
                    OnLegacyGoalMilestoneClicked);
            }
        }
    }

    private static void PositionMilestoneAlongSlider(
        RectTransform widgetRect,
        RectTransform sliderRect,
        int requiredTasks,
        int maxTasks)
    {
        if (widgetRect == null || sliderRect == null || maxTasks <= 0)
            return;

        float width = sliderRect.rect.width > 1f ? sliderRect.rect.width : Mathf.Abs(sliderRect.sizeDelta.x);
        if (width <= 1f)
            return;

        float ratio = Mathf.Clamp01((float)requiredTasks / maxTasks);
        float leftEdgeX = sliderRect.anchoredPosition.x - (width * sliderRect.pivot.x);
        float targetX = leftEdgeX + (width * ratio);

        Vector2 pos = widgetRect.anchoredPosition;
        pos.x = targetX;
        widgetRect.anchoredPosition = pos;
    }

    private void RefreshChestButtonForMilestones()
    {
        if (topRewardChestButton == null)
            return;

        EnsureChestOpaque();

        MissionMilestoneDefinition chestDef = chestMilestoneDefinition;
        if (chestDef == null && missionDatabase != null && missionDatabase.Milestones != null && missionDatabase.Milestones.Count > 0)
            chestDef = missionDatabase.Milestones[missionDatabase.Milestones.Count - 1];

        bool chestClaimable = chestDef != null && MissionService.CanClaimMilestone(chestDef, missionDatabase);
        bool anyClaimable = chestClaimable || GetFirstClaimableMilestone() != null;

        if (topRewardChestImage != null && chestDef != null)
        {
            bool claimed = MissionService.IsMilestoneClaimed(chestDef);
            bool complete = MissionService.IsMilestoneComplete(chestDef, missionDatabase);
            Sprite chestSprite = claimed
                ? chestDef.ClaimedSprite
                : (complete ? chestDef.CompleteSprite : chestDef.IncompleteSprite);
            if (chestSprite != null)
                topRewardChestImage.sprite = chestSprite;

            float targetAlpha = claimed ? claimedChestAlpha : 1f;
            topRewardChestImage.color = new Color(1f, 1f, 1f, targetAlpha);
        }

        topRewardChestButton.interactable = anyClaimable;
        topRewardChestButton.transform.DOKill();
        topRewardChestButton.transform
            .DOScale(anyClaimable ? 1.1f : 1f, 0.2f)
            .SetEase(anyClaimable ? Ease.OutBack : Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(topRewardChestButton.gameObject);
    }

    private MissionMilestoneDefinition GetFirstClaimableMilestone()
    {
        for (int i = 0; i < milestoneWidgets.Count; i++)
        {
            MissionMilestoneWidget w = milestoneWidgets[i];
            if (w != null && w.MilestoneDefinition != null && MissionService.CanClaimMilestone(w.MilestoneDefinition, missionDatabase))
                return w.MilestoneDefinition;
        }

        if (missionDatabase != null && missionDatabase.Milestones != null)
        {
            IReadOnlyList<MissionMilestoneDefinition> list = missionDatabase.Milestones;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null && MissionService.CanClaimMilestone(list[i], missionDatabase))
                    return list[i];
            }
        }

        if (chestMilestoneDefinition != null && MissionService.CanClaimMilestone(chestMilestoneDefinition, missionDatabase))
            return chestMilestoneDefinition;

        return null;
    }

    private WeeklyGoalReward GetFirstClaimableLegacyGoal()
    {
        if (missionDatabase == null || missionDatabase.GoalRewards == null)
            return null;

        IReadOnlyList<WeeklyGoalReward> goals = missionDatabase.GoalRewards;
        for (int i = 0; i < goals.Count; i++)
        {
            WeeklyGoalReward goal = goals[i];
            if (goal != null && MissionService.IsGoalComplete(goal) && !MissionService.IsGoalClaimed(goal))
                return goal;
        }
        return null;
    }

    private void OnMilestoneClicked(MissionMilestoneDefinition milestone)
    {
        if (MissionService.TryClaimMilestone(milestone, missionDatabase))
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshAll();
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
        }
    }

    private void OnLegacyGoalMilestoneClicked(WeeklyGoalReward goal)
    {
        if (MissionService.TryClaimGoal(goal))
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshAll();
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
        }
    }

    public void RefreshDayTabs()
    {
        bool hasCumulativeWeekly = missionDatabase != null && missionDatabase.HasMonthlyCumulativeWeeklyMissions();
        bool showDayTabs = hasCumulativeWeekly
            ? selectedCategory == MissionCategory.Daily
            : selectedCategory == MissionCategory.Weekly;
        selectedDayIndex = Mathf.Clamp(selectedDayIndex, 1, 7);

        if (dayTabWidgets.Count > 0 && dayTabWidgets[0] != null)
        {
            Transform parentContainer = dayTabWidgets[0].transform.parent;
            ScrollRect dayScroll = parentContainer != null ? parentContainer.GetComponentInParent<ScrollRect>(true) : null;
            if (dayScroll != null && (rowContainer == null || !rowContainer.IsChildOf(dayScroll.transform)))
                dayScroll.gameObject.SetActive(showDayTabs);
            else if (parentContainer != null && parentContainer != transform)
                parentContainer.gameObject.SetActive(showDayTabs);
        }

        for (int i = 0; i < dayTabWidgets.Count; i++)
        {
            MissionDayTabWidget widget = dayTabWidgets[i];
            if (widget == null) continue;

            widget.gameObject.SetActive(showDayTabs);
            if (!showDayTabs) continue;

            int day = Mathf.Clamp(widget.DayIndex, 1, 7);
            bool unlocked = MissionService.IsDayUnlocked(day);
            bool isSelected = unlocked && (day == selectedDayIndex);
            bool hasClaimable = unlocked && HasClaimableMissionForDay(day);

            widget.RefreshState(
                unlocked,
                isSelected,
                selectedDayColor,
                unlockedDayColor,
                lockedDayColor,
                lockedDaySprite,
                hasClaimable);
        }
    }

    private bool HasClaimableMissionForDay(int day)
    {
        if (missionDatabase == null) return false;
        List<MissionDefinition> dayMissions = missionDatabase.GetMissionsForDay(day);
        for (int i = 0; i < dayMissions.Count; i++)
        {
            MissionDefinition def = dayMissions[i];
            if (def != null
                && MissionService.IsMissionEligibleForCurrentProgression(def)
                && MissionService.IsComplete(def)
                && !MissionService.IsClaimed(def))
            {
                return true;
            }
        }
        return false;
    }

    private void SetWeeklyTopDecorationsVisible(bool visible)
    {
        if (weeklyResetTimeText != null)
            weeklyResetTimeText.gameObject.SetActive(visible);

        if (topRewardChestButton != null)
            topRewardChestButton.gameObject.SetActive(visible);

        for (int i = 0; i < milestoneWidgets.Count; i++)
        {
            if (milestoneWidgets[i] != null)
                milestoneWidgets[i].gameObject.SetActive(visible);
        }
    }

    private void UpdateTopBarSliderAndLabel(int value, int max, string label)
    {
        int safeMax = Mathf.Max(1, max);
        int clamped = Mathf.Clamp(value, 0, safeMax);

        if (weeklyProgressSlider != null)
        {
            weeklyProgressSlider.minValue = 0f;
            weeklyProgressSlider.maxValue = safeMax;
            weeklyProgressSlider.wholeNumbers = false;
            weeklyProgressSlider.value = clamped;
        }
        else if (weeklyProgressFill != null)
        {
            weeklyProgressFill.fillAmount = Mathf.Clamp01((float)clamped / safeMax);
        }

        if (weeklyProgressText != null)
        {
            weeklyProgressText.text = label;
            UIProgressTextCharacterColorizer.EnsureAndRefresh(
                weeklyProgressText,
                weeklyProgressSlider,
                weeklyProgressFill,
                weeklyProgressCoveredTextColor);
        }
    }

    public void RefreshList()
    {
        if (rowContainer == null || rowPrefab == null) return;

        spawnedRows.Clear();
        for (int i = rowPool.Count - 1; i >= 0; i--)
        {
            if (rowPool[i] == null)
                rowPool.RemoveAt(i);
            else
                rowPool[i].gameObject.SetActive(false);
        }

        IReadOnlyList<MissionDefinition> activeMissions = GetMissionsForCurrentSelection();
        if (activeMissions != null)
        {
            var unclaimedMissions = new List<MissionDefinition>();
            var claimedMissions = new List<MissionDefinition>();

            for (int i = 0; i < activeMissions.Count; i++)
            {
                MissionDefinition missionDef = activeMissions[i];
                if (missionDef == null || string.IsNullOrEmpty(missionDef.MissionId)) continue;
                if (missionDef.Category == MissionCategory.Achievement) continue;
                if (!MissionService.IsMissionEligibleForCurrentProgression(missionDef)) continue;

                if (MissionService.IsClaimed(missionDef))
                    claimedMissions.Add(missionDef);
                else
                    unclaimedMissions.Add(missionDef);
            }

            SpawnMissionRows(unclaimedMissions);
            SpawnMissionRows(claimedMissions);
        }

        if (rowContainer is RectTransform containerRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            ScrollRect parentScroll = containerRect.GetComponentInParent<ScrollRect>();
            if (parentScroll != null)
                parentScroll.verticalNormalizedPosition = 1f;
        }

        if (emptyStateRoot != null && emptyStateRoot != rowPrefab)
            emptyStateRoot.SetActive(spawnedRows.Count == 0);
    }

    private void SpawnMissionRows(List<MissionDefinition> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            MissionRowWidget row = InstantiateOrReuseRow(spawnedRows.Count);
            if (row == null)
                return;
            row.Setup(list[i], OnClaimDefinitionPressed);
        }
    }

    private MissionRowWidget InstantiateOrReuseRow(int poolIndex)
    {
        MissionRowWidget row;
        if (poolIndex < rowPool.Count && rowPool[poolIndex] != null)
        {
            row = rowPool[poolIndex];
        }
        else
        {
            GameObject rowObj = Instantiate(rowPrefab, rowContainer, false);
            if (!rowObj.TryGetComponent(out row))
            {
                Debug.LogError("[MissionPanelUI] Assigned rowPrefab is missing MissionRowWidget component.", this);
                if (Application.isPlaying)
                    Destroy(rowObj);
                else
                    DestroyImmediate(rowObj);
                return null;
            }

            if (poolIndex < rowPool.Count)
                rowPool[poolIndex] = row;
            else
                rowPool.Add(row);
        }

        row.transform.SetSiblingIndex(poolIndex);
        row.transform.localScale = Vector3.one;
        row.gameObject.SetActive(true);
        spawnedRows.Add(row);
        return row;
    }

    private IReadOnlyList<MissionDefinition> GetMissionsForCurrentSelection()
    {
        if (missionDatabase == null)
            return null;

        bool hasCumulativeWeekly = missionDatabase.HasMonthlyCumulativeWeeklyMissions();

        if (selectedCategory == MissionCategory.Weekly)
        {
            if (hasCumulativeWeekly)
                return missionDatabase.GetCumulativeWeeklyMissions(MissionService.GetCurrentWeekOfMonth());

            if (HasWiredDayTabs())
                return missionDatabase.GetMissionsForDay(selectedDayIndex);

            List<MissionDefinition> forToday = missionDatabase.GetMissionsForDay(selectedDayIndex);
            if (forToday != null && forToday.Count > 0)
                return forToday;

            return missionDatabase.GetMissionsByCategory(MissionCategory.Weekly);
        }

        if (selectedCategory == MissionCategory.Daily && hasCumulativeWeekly && HasWiredDayTabs())
        {
            List<MissionDefinition> dayMissions = missionDatabase.GetMissionsForDay(selectedDayIndex);
            if (dayMissions != null && dayMissions.Count > 0)
                return dayMissions;
        }

        return missionDatabase.GetMissionsByCategory(selectedCategory);
    }

    private bool HasWiredDayTabs()
    {
        for (int i = 0; i < dayTabWidgets.Count; i++)
        {
            if (dayTabWidgets[i] != null)
                return true;
        }
        return false;
    }

    private MissionRowWidget FindSpawnedRow(MissionDefinition mission)
    {
        if (mission == null) return null;
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            MissionRowWidget row = spawnedRows[i];
            if (row != null && row.MissionDefinition == mission)
                return row;
        }
        return null;
    }

    private void OnClaimDefinitionPressed(MissionDefinition mission)
    {
        ExecuteClaimWithAnimation(FindSpawnedRow(mission), () => MissionService.TryClaim(mission));
    }

    private void ExecuteClaimWithAnimation(MissionRowWidget claimedRow, Func<bool> tryClaimAction)
    {
        isAnimatingRowClaim = true;
        bool claimed;
        try
        {
            claimed = tryClaimAction != null && tryClaimAction();
        }
        finally
        {
            isAnimatingRowClaim = false;
        }

        if (claimed)
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshCategoryTabs();
            RefreshWeeklyProgressBar();
            RefreshDayTabs();

            if (claimedRow != null)
            {
                spawnedRows.Remove(claimedRow);
                spawnedRows.Add(claimedRow);
                claimedRow.PlayClaimAndMoveToBottomAnimation();
            }
            else
            {
                RefreshList();
            }
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
        }
    }
}
