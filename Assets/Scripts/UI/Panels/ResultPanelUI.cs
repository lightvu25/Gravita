using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultPanelUI : MonoBehaviour, IUIPanel
{
    private enum PendingResult { None, Completed, Failed }

    private PendingResult pendingResult = PendingResult.None;
    private string pendingFailReason;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Completion Panel")]
    [SerializeField] private GameObject completionPanel;
    [SerializeField] private TMP_Text completionTitleText;
    [SerializeField] private TMP_Text coinsEarnedText;
    [SerializeField] private Button nextLevelButton;
    [SerializeField] private Button completionRestartButton;
    [SerializeField] private Button completionMapButton;
    [SerializeField] private Image[] stars;
    [SerializeField] private Sprite starFilled;
    [SerializeField] private Sprite starEmpty;

    [Header("Failure Panel")]
    [SerializeField] private GameObject failurePanel;
    [SerializeField] private TMP_Text failTitleText;

    [SerializeField] private TMP_Text failureReasonText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button failureMapButton;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
    }

    private void Start()
    {
        flowController.OnLevelCompleted += HandleLevelCompleted;
        flowController.OnLevelFailed += HandleLevelFailed;
        flowController.OnLevelRestarted += HandleResultClosed;
        flowController.OnReturnToMapRequested += HandleResultClosed;

        BindButton(nextLevelButton, OnNextLevel);
        BindButton(completionRestartButton, OnRestart);
        BindButton(completionMapButton, OnReturnToMap);
        BindButton(retryButton, OnRestart);
        BindButton(failureMapButton, OnReturnToMap);
    }

    private void OnDestroy()
    {
        flowController.OnLevelCompleted -= HandleLevelCompleted;
        flowController.OnLevelFailed -= HandleLevelFailed;
        flowController.OnLevelRestarted -= HandleResultClosed;
        flowController.OnReturnToMapRequested -= HandleResultClosed;

    }

    private void BindButton(Button button, Action callback)
    {
        button.onClick.AddListener(() => callback());
    }

    // ─────────────────────────────────────────────────────────────
    //  IUIPanel + flow events
    // ─────────────────────────────────────────────────────────────

    public void Show()
    {
        // This GameObject is the mapped overlay root and is saved inactive —
        // activate it before driving the sub-panels.
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);

        // Prefer the state captured by the event handler; when the panel was
        // inactive at the time (no subscriptions), fall back to the flow state.
        PendingResult result = pendingResult;
        if (result == PendingResult.None && flowController != null)
            result = flowController.CurrentState == LevelState.Failed
                ? PendingResult.Failed
                : PendingResult.Completed;

        if (result == PendingResult.Failed)
            ShowFailure(pendingFailReason ?? (flowController != null ? flowController.LastFailReason : null));
        else
            ShowCompletion();
    }

    public void Hide()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
        else FinishHide();
    }

    private void FinishHide()
    {
        HideAll();
        pendingResult = PendingResult.None;
        pendingFailReason = null;
        gameObject.SetActive(false);
    }

    private void HandleLevelCompleted()
    {
        pendingResult = PendingResult.Completed;
        pendingFailReason = null;
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Result);
        else
            ShowCompletion();
    }

    private void HandleLevelFailed(string reason)
    {
        pendingResult = PendingResult.Failed;
        pendingFailReason = reason;
        UIManager.Instance.OpenPanel(UIPanelType.Result);
    }

    private void HandleResultClosed()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Result))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Result);
        else
            Hide();
    }

    // ─────────────────────────────────────────────────────────────
    //  Result displays
    // ─────────────────────────────────────────────────────────────

    private void ShowCompletion()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        int earnedStars = Mathf.Clamp(flowController != null ? flowController.LastCompletedStars : 1, 1, 3);

        failurePanel.SetActive(false);
        completionPanel.SetActive(true);

        int stage = GetStageNumber(level);
        completionTitleText.text = $"STAGE {stage}";
            
        SetStars(earnedStars);

        RewardBundle clearReward = flowController != null ? flowController.LastClearReward : null;
        coinsEarnedText.text = clearReward != null ? $"+{clearReward.CoinCount}" : "";

        if (nextLevelButton != null)
        {
            bool hasNext = flowController != null
                && flowController.CurrentLevelData != null
                && ProgressionManager.Instance != null
                && ProgressionManager.Instance.Database != null
                && ProgressionManager.Instance.Database.GetNextLevel(flowController.CurrentLevelData) != null;
            nextLevelButton.gameObject.SetActive(hasNext);
        }
    }

    public void SetStars(int earnedStars)
    {
        earnedStars = Mathf.Clamp(earnedStars, 0, 3);
        if (stars == null) return;
    
        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;
            stars[i].sprite = i < earnedStars ? starFilled : starEmpty;
        }
    }

    private static int GetStageNumber(LevelData level)
{
    var db = ProgressionManager.Instance != null ? ProgressionManager.Instance.Database : null;
    if (level == null || db == null) return 0;
 
    int index = 0;
    foreach (var world in db.Worlds)
    {
        if (world.levels == null) continue;
        foreach (var lvl in world.levels)
        {
            if (lvl == null) continue;
            index++;
            if (lvl == level) return index;
        }
    }
    return 0;
}

    private void ShowFailure(string reason)
    {
        var failedlevel = flowController != null ? flowController.CurrentLevelData : null;
        int failedstage = GetStageNumber(failedlevel);
        failTitleText.text = $"STAGE {failedstage}";

        if (completionPanel != null) completionPanel.SetActive(false);
        if (failurePanel != null) failurePanel.SetActive(true);
        if (failureReasonText != null)
            failureReasonText.text = string.IsNullOrEmpty(reason) ? "LEVEL FAILED" : reason;
    }

    public void HideAll()
    {
        if (completionPanel != null) completionPanel.SetActive(false);
        if (failurePanel != null) failurePanel.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Buttons
    // ─────────────────────────────────────────────────────────────

    private void OnNextLevel()
    {
        HandleResultClosed();
        flowController?.LoadNextLevel();
    }

    private void OnRestart()
    {
        HandleResultClosed();
        flowController?.RestartLevel();
    }

    private void OnReturnToMap()
    {
        HandleResultClosed();
        flowController?.ReturnToMap();
    }
}