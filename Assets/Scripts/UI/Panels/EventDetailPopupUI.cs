using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Event Detail Popup (<see cref="UIPanelType.EventDetail"/>) opened when clicking a Banner card.
/// Shows expanded artwork, event rules, live countdown, total progress slider, claimable tasks,
/// and a primary call-to-action button jumping to gameplay or another panel.
/// </summary>
public class EventDetailPopupUI : MonoBehaviour, IUIPanel
{
    [Header("Header & Rules")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Image heroBannerImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text rulesDescriptionText;
    [SerializeField] private TMP_Text daysLeftText;

    [Header("Summary Progress")]
    [SerializeField] private Slider eventProgressSlider;
    [SerializeField] private Image eventProgressFill;
    [SerializeField] private TMP_Text eventProgressText;
    [SerializeField] private Color progressCoveredTextColor = new Color(0.078f, 0.094f, 0.141f, 1f);

    [Header("Event Task List (Reuses MissionRowWidget)")]
    [SerializeField] private Transform taskRowContainer;
    [SerializeField] private GameObject taskRowPrefab;
    [SerializeField] private GameObject emptyTasksStateRoot;

    [Header("Primary Call-To-Action")]
    [SerializeField] private Button ctaActionButton;
    [SerializeField] private TMP_Text ctaActionLabel;

    [Header("Optional Animation (Leave Empty for No Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private readonly List<MissionRowWidget> spawnedRows = new List<MissionRowWidget>();
    private GameEventDefinition boundEvent;
    private bool buttonsBound;
    private bool subscribed;
    private bool isAnimatingRowClaim;

    public GameEventDefinition BoundEvent => boundEvent;
    public IReadOnlyList<MissionRowWidget> SpawnedRows => spawnedRows;

    private void Awake()
    {
        ConfigureNonBlockingSlider();
        BindButtons();
    }

    private void OnEnable()
    {
        ConfigureNonBlockingSlider();
        BindButtons();
        Subscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        isAnimatingRowClaim = false;
        Unsubscribe();
    }

    private void OnDestroy()
    {
        isAnimatingRowClaim = false;
        Unsubscribe();
        UnbindButtons();
    }

    public void BindEvent(GameEventDefinition definition)
    {
        boundEvent = definition;
        if (gameObject.activeInHierarchy)
            RefreshAll();
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

    public void RefreshAll()
    {
        if (isAnimatingRowClaim || boundEvent == null)
            return;

        EventService.EnsureInitialized();
        RefreshHeaderAndSummary();
        RefreshTasksList();
    }

    private void RefreshHeaderAndSummary()
    {
        if (heroBannerImage != null && boundEvent.DetailBannerSprite != null)
        {
            heroBannerImage.sprite = boundEvent.DetailBannerSprite;
            heroBannerImage.color = Color.white;
        }

        if (titleText != null)
            titleText.text = boundEvent.Title;

        if (rulesDescriptionText != null)
            rulesDescriptionText.text = boundEvent.DetailRulesText;

        if (daysLeftText != null)
            daysLeftText.text = EventService.FormatRemainingTime(boundEvent);

        int totalCount = boundEvent.Tasks != null ? boundEvent.Tasks.Count : 0;
        int claimedCount = EventService.GetClaimedTaskCount(boundEvent);
        int safeMax = Mathf.Max(1, totalCount);
        int clamped = Mathf.Clamp(claimedCount, 0, safeMax);

        if (eventProgressSlider != null)
        {
            eventProgressSlider.minValue = 0f;
            eventProgressSlider.maxValue = safeMax;
            eventProgressSlider.wholeNumbers = false;
            eventProgressSlider.value = clamped;
        }
        else if (eventProgressFill != null)
        {
            eventProgressFill.fillAmount = Mathf.Clamp01((float)clamped / safeMax);
        }

        if (eventProgressText != null)
        {
            eventProgressText.text = $"{claimedCount}/{totalCount}";
            UIProgressTextCharacterColorizer.EnsureAndRefresh(
                eventProgressText,
                eventProgressSlider,
                eventProgressFill,
                progressCoveredTextColor);
        }

        if (ctaActionButton != null)
        {
            bool showCta = boundEvent.CtaActionType != EventBannerActionType.OpenEventDetailPopup;
            ctaActionButton.gameObject.SetActive(showCta);
            if (showCta && ctaActionLabel != null)
                ctaActionLabel.text = boundEvent.CtaButtonLabel;
        }
    }

    private void RefreshTasksList()
    {
        if (taskRowContainer == null || taskRowPrefab == null)
            return;

        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null)
            {
                if (Application.isPlaying)
                    Destroy(spawnedRows[i].gameObject);
                else
                    DestroyImmediate(spawnedRows[i].gameObject);
            }
        }
        spawnedRows.Clear();

        IReadOnlyList<GameEventTaskEntry> allTasks = boundEvent.Tasks;
        if (allTasks != null)
        {
            var unclaimed = new List<GameEventTaskEntry>();
            var claimed = new List<GameEventTaskEntry>();

            for (int i = 0; i < allTasks.Count; i++)
            {
                GameEventTaskEntry t = allTasks[i];
                if (string.IsNullOrEmpty(t.taskId)) continue;

                if (EventService.IsTaskClaimed(boundEvent, t))
                    claimed.Add(t);
                else
                    unclaimed.Add(t);
            }

            SpawnTaskRows(unclaimed);
            SpawnTaskRows(claimed);
        }

        if (taskRowContainer is RectTransform containerRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            ScrollRect scroll = containerRect.GetComponentInParent<ScrollRect>();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f;
        }

        if (emptyTasksStateRoot != null && emptyTasksStateRoot != taskRowPrefab)
            emptyTasksStateRoot.SetActive(spawnedRows.Count == 0);
    }

    private void SpawnTaskRows(List<GameEventTaskEntry> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            GameObject rowObj = Instantiate(taskRowPrefab, taskRowContainer, false);
            if (!rowObj.TryGetComponent(out MissionRowWidget row))
            {
                Debug.LogError("[EventDetailPopupUI] Assigned taskRowPrefab is missing MissionRowWidget component.", this);
                if (Application.isPlaying)
                    Destroy(rowObj);
                else
                    DestroyImmediate(rowObj);
                return;
            }

            rowObj.transform.localScale = Vector3.one;
            rowObj.SetActive(true);
            row.SetupEventTask(boundEvent, list[i], OnClaimTaskPressed);
            spawnedRows.Add(row);
        }
    }

    private MissionRowWidget FindSpawnedTaskRow(string taskId)
    {
        if (string.IsNullOrEmpty(taskId))
            return null;

        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null && spawnedRows[i].EventTaskId == taskId)
                return spawnedRows[i];
        }
        return null;
    }

    private void OnClaimTaskPressed(GameEventDefinition def, GameEventTaskEntry task)
    {
        MissionRowWidget claimedRow = FindSpawnedTaskRow(task.taskId);
        isAnimatingRowClaim = true;
        bool claimed;
        try
        {
            claimed = EventService.TryClaimTask(def, task);
        }
        finally
        {
            isAnimatingRowClaim = false;
        }

        if (claimed)
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshHeaderAndSummary();

            if (claimedRow != null)
            {
                spawnedRows.Remove(claimedRow);
                spawnedRows.Add(claimedRow);
                claimedRow.PlayClaimAndMoveToBottomAnimation();
            }
            else
            {
                RefreshTasksList();
            }
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
        }
    }

    private void OnCtaClicked()
    {
        if (boundEvent == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        EventBannerActionType action = boundEvent.CtaActionType;
        UIPanelType targetPanel = boundEvent.CtaTargetPanel;
        RunnerModeKind runnerMode = boundEvent.CtaRunnerMode;

        CloseSelf();
        ExecuteEventAction(action, targetPanel, runnerMode);
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        CloseSelf();
    }

    private void CloseSelf()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.EventDetail))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.EventDetail);
        else
            Hide();
    }

    public static void ExecuteEventAction(EventBannerActionType actionType, UIPanelType targetPanel, RunnerModeKind runnerMode)
    {
        switch (actionType)
        {
            case EventBannerActionType.StartRunnerMode:
            {
                MazeRunnerService runner = MazeRunnerService.Instance != null
                    ? MazeRunnerService.Instance
                    : FindAnyObjectByType<MazeRunnerService>();

                if (runner != null)
                {
                    UIManager.Instance?.CloseAllPopups();
                    if (runner.StartRun(runnerMode))
                        return;
                }

                if (UIManager.Instance != null && UIManager.Instance.IsPanelRegistered(UIPanelType.Runner))
                    UIManager.Instance.OpenPanel(UIPanelType.Runner);
                break;
            }

            case EventBannerActionType.JumpToPanel:
                if (targetPanel != UIPanelType.None && UIManager.Instance != null && UIManager.Instance.IsPanelRegistered(targetPanel))
                    UIManager.Instance.OpenPanel(targetPanel);
                break;
        }
    }

    private void ConfigureNonBlockingSlider()
    {
        if (eventProgressSlider == null)
            return;

        eventProgressSlider.interactable = false;
        eventProgressSlider.transition = Selectable.Transition.None;

        Graphic[] graphics = eventProgressSlider.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].raycastTarget = false;
        }
    }

    private void BindButtons()
    {
        if (buttonsBound)
            return;
        buttonsBound = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
        if (ctaActionButton != null)
            ctaActionButton.onClick.AddListener(OnCtaClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound)
            return;
        buttonsBound = false;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);
        if (ctaActionButton != null)
            ctaActionButton.onClick.RemoveListener(OnCtaClicked);
    }

    private void Subscribe()
    {
        if (subscribed)
            return;
        subscribed = true;

        EventService.EnsureInitialized();
        EventService.OnEventStateChanged += RefreshAll;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;
        subscribed = false;

        EventService.OnEventStateChanged -= RefreshAll;
    }
}
