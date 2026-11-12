using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class ResultPanelUI : MonoBehaviour, IUIPanel
{
    private enum PendingResult { None, Completed, Failed }

    private PendingResult pendingResult = PendingResult.None;
    private string pendingFailReason;
    private bool buttonsBound;
    private bool isSubscribed;
    private bool hasClaimedResultAdThisRun;
    private int lastDisplayedCoins;
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

    [Header("Economy Result Controls")]
    [SerializeField] private Button watchBonusAdButton;

    [Header("Star Pop-In Animation (DOTween)")]
    [SerializeField] private float starPopDuration = 0.35f;
    [SerializeField] private float starStaggerDelay = 0.14f;

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
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();
    }

    private void Start()
    {
        EnsureInitialized();
    }

    private void EnsureInitialized()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        BindButtons();

        if (!isSubscribed && flowController != null)
        {
            isSubscribed = true;
            flowController.OnLevelCompleted += HandleLevelCompleted;
            flowController.OnLevelFailed += HandleLevelFailed;
            flowController.OnLevelRestarted += HandleResultClosed;
            flowController.OnReturnToMapRequested += HandleResultClosed;
        }
    }

    private void OnDestroy()
    {
        if (isSubscribed && flowController != null)
        {
            isSubscribed = false;
            flowController.OnLevelCompleted -= HandleLevelCompleted;
            flowController.OnLevelFailed -= HandleLevelFailed;
            flowController.OnLevelRestarted -= HandleResultClosed;
            flowController.OnReturnToMapRequested -= HandleResultClosed;
        }

        UnbindButtons();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (nextLevelButton != null)
            nextLevelButton.onClick.AddListener(OnNextLevel);
        if (completionRestartButton != null)
            completionRestartButton.onClick.AddListener(OnRestart);
        if (completionMapButton != null)
            completionMapButton.onClick.AddListener(OnReturnToMap);
        if (retryButton != null)
            retryButton.onClick.AddListener(OnRestart);
        if (failureMapButton != null)
            failureMapButton.onClick.AddListener(OnReturnToMap);
        if (watchBonusAdButton != null)
            watchBonusAdButton.onClick.AddListener(OnWatchBonusAdClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (nextLevelButton != null)
            nextLevelButton.onClick.RemoveListener(OnNextLevel);
        if (completionRestartButton != null)
            completionRestartButton.onClick.RemoveListener(OnRestart);
        if (completionMapButton != null)
            completionMapButton.onClick.RemoveListener(OnReturnToMap);
        if (retryButton != null)
            retryButton.onClick.RemoveListener(OnRestart);
        if (failureMapButton != null)
            failureMapButton.onClick.RemoveListener(OnReturnToMap);
        if (watchBonusAdButton != null)
            watchBonusAdButton.onClick.RemoveListener(OnWatchBonusAdClicked);
    }

    // ─────────────────────────────────────────────────────────────
    //  IUIPanel + flow events
    // ─────────────────────────────────────────────────────────────

    public void Show()
    {
        EnsureInitialized();

        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);

        PendingResult result = pendingResult;
        if (flowController != null)
        {
            if (flowController.CurrentState == LevelState.Failed)
                result = PendingResult.Failed;
            else if (flowController.CurrentState == LevelState.Completed)
                result = PendingResult.Completed;
        }

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
        hasClaimedResultAdThisRun = false;
        gameObject.SetActive(false);
    }

    private void HandleLevelCompleted()
    {
        pendingResult = PendingResult.Completed;
        pendingFailReason = null;
        hasClaimedResultAdThisRun = false;
        if (UIManager.Instance == null)
            ShowCompletion();
        else if (UIManager.Instance.IsPanelOpen(UIPanelType.Result))
            ShowCompletion();
    }

    private void HandleLevelFailed(string reason)
    {
        pendingResult = PendingResult.Failed;
        pendingFailReason = reason;
        hasClaimedResultAdThisRun = false;
        if (UIManager.Instance == null)
            ShowFailure(reason);
        else if (UIManager.Instance.IsPanelOpen(UIPanelType.Result))
            ShowFailure(reason);
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
        int earnedStars = Mathf.Clamp(flowController != null ? flowController.LastCompletedStars : 0, 0, 3);

        if (failurePanel != null) failurePanel.SetActive(false);
        if (completionPanel != null) completionPanel.SetActive(true);
        UIAudio.Play(UISoundType.VictoryPopup);

        int stage = GetStageNumber(level);
        if (completionTitleText != null)
            completionTitleText.text = $"STAGE {stage}";

        SetStars(earnedStars);

        LevelEconomySettlement settlement = flowController != null ? flowController.LastEconomySettlement : default;
        RewardBundle clearReward = flowController != null ? flowController.LastClearReward : null;
        bool hasSettlement = !string.IsNullOrEmpty(settlement.LevelId);

        lastDisplayedCoins = hasSettlement
            ? settlement.TotalCoinsGranted
            : (clearReward != null ? clearReward.CoinCount : 0);

        CurrencyUI.Animate(coinsEarnedText, 0, lastDisplayedCoins);

        RefreshBonusAdButton();

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

    public void NotifyBonusCoinsAdded(int addedCoins)
    {
        if (addedCoins <= 0) return;
        hasClaimedResultAdThisRun = true;
        int previousCoins = lastDisplayedCoins;
        lastDisplayedCoins += addedCoins;
        CurrencyUI.Animate(
            coinsEarnedText,
            previousCoins,
            lastDisplayedCoins);
        if (watchBonusAdButton != null)
            watchBonusAdButton.gameObject.SetActive(false);
    }

    private void RefreshBonusAdButton()
    {
        if (watchBonusAdButton == null)
            return;

        bool canWatch = !hasClaimedResultAdThisRun
            && AdRewardService.Instance != null
            && AdRewardService.Instance.IsAdAvailable(RewardedAdPlacement.ResultScreenBonus);

        watchBonusAdButton.gameObject.SetActive(canWatch);
        watchBonusAdButton.interactable = canWatch;

        if (canWatch)
            AdPolicyCoordinator.NotifyAdOffered(RewardedAdPlacement.ResultScreenBonus);
    }

    private void OnWatchBonusAdClicked()
    {
        if (hasClaimedResultAdThisRun || AdRewardService.Instance == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (watchBonusAdButton != null)
            watchBonusAdButton.interactable = false;

        RewardItem fixedItem = AdPolicyCoordinator.GetFixedRewardForPlacement(RewardedAdPlacement.ResultScreenBonus);
        AdRewardService.Instance.WatchAdForReward(RewardedAdPlacement.ResultScreenBonus, fixedItem.Amount, success =>
        {
            if (this == null)
                return;

            if (success)
            {
                hasClaimedResultAdThisRun = true;
                UIAudio.Play(UISoundType.RewardClaim);
                if (fixedItem.Type == RewardType.Coins)
                    NotifyBonusCoinsAdded(fixedItem.Amount);
                else if (watchBonusAdButton != null)
                    watchBonusAdButton.gameObject.SetActive(false);
            }
            else
            {
                RefreshBonusAdButton();
            }
        });
    }

    public void SetStars(int earnedStars)
    {
        earnedStars = Mathf.Clamp(earnedStars, 0, 3);
        if (stars == null) return;

        for (int i = 0; i < stars.Length; i++)
        {
            Image starImage = stars[i];
            if (starImage == null) continue;

            bool isEarned = i < earnedStars;
            starImage.SetSpriteNative(isEarned ? starFilled : starEmpty);

            starImage.transform.DOKill();
            if (isEarned)
            {
                starImage.transform.localScale = Vector3.zero;
                starImage.transform
                    .DOScale(1f, starPopDuration)
                    .SetDelay(i * starStaggerDelay)
                    .SetEase(Ease.OutBack)
                    .SetUpdate(true)
                    .SetLink(starImage.gameObject);
            }
            else
            {
                starImage.transform.localScale = Vector3.one;
            }
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
        var failedLevel = flowController != null ? flowController.CurrentLevelData : null;
        int failedStage = GetStageNumber(failedLevel);
        if (failTitleText != null)
            failTitleText.text = $"STAGE {failedStage}";

        if (completionPanel != null) completionPanel.SetActive(false);
        if (failurePanel != null) failurePanel.SetActive(true);
        UIAudio.Play(UISoundType.DefeatPopup);
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

    private bool IsExitingCompletedState()
    {
        if (pendingResult == PendingResult.Completed)
            return true;
        return flowController != null && flowController.CurrentState == LevelState.Completed;
    }

    private void OnNextLevel()
    {
        bool completedExit = IsExitingCompletedState();
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        HandleResultClosed();
        if (completedExit)
            AdPolicyCoordinator.TryShowPostResultTransitionInterstitial();
        flowController?.LoadNextLevel();
    }

    private void OnRestart()
    {
        // GDD §8.3: Never show Interstitial on Restart or Death
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        HandleResultClosed();
        flowController?.RestartLevel();
    }

    private void OnReturnToMap()
    {
        bool completedExit = IsExitingCompletedState();
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        HandleResultClosed();
        if (completedExit)
            AdPolicyCoordinator.TryShowPostResultTransitionInterstitial();
        flowController?.ReturnToMap();
    }
}
