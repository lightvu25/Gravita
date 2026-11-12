using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class RewardOverlayUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Reward Displays")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;
    [SerializeField] private TMP_Text expText;
    [FormerlySerializedAs("acclaimText")]
    [SerializeField] private TMP_Text starsText;

    [Header("Monetization")]
    [SerializeField] private Button doubleCoinsButton;
    [SerializeField] private Button continueButton;

    private RewardBundle lastBundle;
    private LevelData lastBundleLevel;
    private int displayedCoinsCount;
    private bool hasClaimedDouble;
    private bool rewardServiceSubscribed;
    private bool flowSubscribed;
    private bool buttonsBound;
    private bool continueBound;
    private UIPanelAnimator panelAnimator;

    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        EnsureInitialized();
    }

    private void OnEnable()
    {
        EnsureInitialized();

        if (flowController != null
            && flowController.CurrentState == LevelState.Completed
            && flowController.LastClearReward != null
            && lastBundle != flowController.LastClearReward)
        {
            HandleRewardGranted(flowController.LastClearReward);
        }
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

        if (!flowSubscribed && flowController != null)
        {
            flowSubscribed = true;
            flowController.OnLevelStarted += HandleLevelStarted;
            flowController.OnLevelRestarted += HandleLevelStarted;
            flowController.OnStateChanged += HandleStateChanged;
        }

        TrySubscribeRewardService();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (doubleCoinsButton != null)
            doubleCoinsButton.onClick.AddListener(OnDoubleCoinsClicked);

        if (continueButton != null && GetComponentInParent<ResultPanelUI>(true) == null)
        {
            continueButton.onClick.AddListener(OnContinueClicked);
            continueBound = true;
        }
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (doubleCoinsButton != null)
            doubleCoinsButton.onClick.RemoveListener(OnDoubleCoinsClicked);

        if (continueBound && continueButton != null)
        {
            continueButton.onClick.RemoveListener(OnContinueClicked);
            continueBound = false;
        }
    }

    private void TrySubscribeRewardService()
    {
        if (rewardServiceSubscribed || RewardService.Instance == null) return;
        RewardService.Instance.OnRewardGranted += HandleRewardGranted;
        rewardServiceSubscribed = true;
    }

    private void OnDestroy()
    {
        if (flowSubscribed && flowController != null)
        {
            flowSubscribed = false;
            flowController.OnLevelStarted -= HandleLevelStarted;
            flowController.OnLevelRestarted -= HandleLevelStarted;
            flowController.OnStateChanged -= HandleStateChanged;
        }

        if (rewardServiceSubscribed && RewardService.Instance != null)
        {
            rewardServiceSubscribed = false;
            RewardService.Instance.OnRewardGranted -= HandleRewardGranted;
        }

        UnbindButtons();
    }

    private void HandleLevelStarted()
    {
        TrySubscribeRewardService();
        hasClaimedDouble = false;
        lastBundle = null;
        lastBundleLevel = null;
        displayedCoinsCount = 0;

        if (GetComponentInParent<ResultPanelUI>(true) == null)
            gameObject.SetActive(false);

        if (doubleCoinsButton != null)
            doubleCoinsButton.gameObject.SetActive(false);
    }

    private void HandleRewardGranted(RewardBundle bundle)
    {
        if (bundle == null || hasClaimedDouble || lastBundle == bundle)
            return;

        if (flowController != null && flowController.CurrentState == LevelState.Completed)
        {
            lastBundle = bundle;
            lastBundleLevel = flowController.CurrentLevelData;
            displayedCoinsCount = bundle.CoinCount;

            if (PanelAnimator != null) PanelAnimator.PlayIn();
            else gameObject.SetActive(true);

            AnalyticsService.Track(AnalyticsEvents.RewardShown, RewardParams());

            CurrencyUI.Animate(coinsText, 0, displayedCoinsCount);
            CurrencyUI.Animate(gemsText, 0, Mathf.Max(0, bundle.GemCount));
            if (expText != null) expText.text = string.Empty;
            if (starsText != null)
            {
                int stars = flowController != null ? flowController.LastCompletedStars : 0;
                starsText.text = $"{stars} *";
            }

            if (doubleCoinsButton != null)
            {
                bool canWatchBonus = AdRewardService.Instance != null
                    && AdRewardService.Instance.IsAdAvailable(RewardedAdPlacement.ResultScreenBonus);
                doubleCoinsButton.gameObject.SetActive(canWatchBonus);
                doubleCoinsButton.interactable = canWatchBonus;

                if (canWatchBonus)
                    AdPolicyCoordinator.NotifyAdOffered(RewardedAdPlacement.ResultScreenBonus);
            }
        }
    }

    private void OnContinueClicked()
    {
        bool completedExit = flowController != null && flowController.CurrentState == LevelState.Completed;
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (PanelAnimator != null) PanelAnimator.PlayOut(() => gameObject.SetActive(false));
        else gameObject.SetActive(false);

        if (completedExit)
            AdPolicyCoordinator.TryShowPostResultTransitionInterstitial();

        flowController?.ReturnToMap();
    }

    private void HandleStateChanged(LevelState state)
    {
        if (state != LevelState.Completed && lastBundle != null)
        {
            AnalyticsService.Track(AnalyticsEvents.RewardClaimed, RewardParams());
            lastBundle = null;
            lastBundleLevel = null;
        }
    }

    private Dictionary<string, object> RewardParams()
    {
        var p = new Dictionary<string, object>();
        var level = lastBundleLevel != null ? lastBundleLevel
            : (flowController != null ? flowController.CurrentLevelData : null);
        if (level != null)
        {
            p["level_id"] = level.LevelId;
            p["world_index"] = level.World;
        }
        if (lastBundle != null)
        {
            p["coins"] = lastBundle.CoinCount;
            p["gems"] = lastBundle.GemCount;
        }
        return p;
    }

    private void OnDoubleCoinsClicked()
    {
        if (hasClaimedDouble) return;
        if (doubleCoinsButton != null && !doubleCoinsButton.interactable) return;
        if (AdRewardService.Instance == null || lastBundle == null) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        hasClaimedDouble = true;
        if (doubleCoinsButton != null)
            doubleCoinsButton.interactable = false;

        RewardItem fixedItem = AdPolicyCoordinator.GetFixedRewardForPlacement(RewardedAdPlacement.ResultScreenBonus);
        AdRewardService.Instance.WatchAdForReward(RewardedAdPlacement.ResultScreenBonus, fixedItem.Amount, success =>
        {
            if (this == null) return;

            if (success)
            {
                UIAudio.Play(UISoundType.RewardClaim);
                if (fixedItem.Type == RewardType.Coins)
                {
                    int previousCoins = displayedCoinsCount;
                    displayedCoinsCount += fixedItem.Amount;
                    CurrencyUI.Animate(
                        coinsText,
                        previousCoins,
                        displayedCoinsCount);

                    GetComponentInParent<ResultPanelUI>(true)?.NotifyBonusCoinsAdded(fixedItem.Amount);
                }

                if (doubleCoinsButton != null)
                    doubleCoinsButton.gameObject.SetActive(false);
            }
            else
            {
                hasClaimedDouble = false;
                if (doubleCoinsButton != null)
                    doubleCoinsButton.interactable = true;
            }
        });
    }
}
