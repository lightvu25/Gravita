using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Displays completion rewards, stars, and handles the '2x Coins' Ad button.
/// Strictly presentation logic.
/// </summary>
public class RewardOverlayUI : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private GameObject panel;

    [Header("Reward Displays")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;
    [FormerlySerializedAs("acclaimText")]
    [SerializeField] private TMP_Text starsText;
    
    [Header("Monetization")]
    [SerializeField] private Button doubleCoinsButton;
    [SerializeField] private Button continueButton;

    private RewardBundle lastBundle;
    private LevelData lastBundleLevel;
    private bool hasClaimedDouble = false;
    private bool rewardServiceSubscribed = false;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (panel != null ? panel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        if (doubleCoinsButton != null)
        {
            doubleCoinsButton.onClick.RemoveAllListeners();
            doubleCoinsButton.onClick.AddListener(OnDoubleCoinsClicked);
        }

        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinueClicked);
        }

        if (flowController != null)
        {
            flowController.OnLevelStarted += HandleLevelStarted;
            flowController.OnStateChanged += HandleStateChanged;
        }

        TrySubscribeRewardService();
    }

    private void Start()
    {
        TrySubscribeRewardService();
    }

    private void TrySubscribeRewardService()
    {
        if (rewardServiceSubscribed || RewardService.Instance == null) return;
        RewardService.Instance.OnRewardGranted += HandleRewardGranted;
        rewardServiceSubscribed = true;
    }

    private void OnDestroy()
    {
        if (flowController != null)
        {
            flowController.OnLevelStarted -= HandleLevelStarted;
            flowController.OnStateChanged -= HandleStateChanged;
        }

        if (rewardServiceSubscribed && RewardService.Instance != null)
        {
            RewardService.Instance.OnRewardGranted -= HandleRewardGranted;
        }
    }

    private void HandleLevelStarted()
    {
        TrySubscribeRewardService();
        hasClaimedDouble = false;
        lastBundle = null;
        lastBundleLevel = null;
        if (panel != null) panel.SetActive(false);
        if (doubleCoinsButton != null)
        {
            doubleCoinsButton.gameObject.SetActive(false);
        }
    }

    private void HandleRewardGranted(RewardBundle bundle)
    {
        // If we have already claimed the double reward for this level completion, ignore further grants
        if (hasClaimedDouble) return;

        // Only show overlay logic if this is a level clear
        if (flowController != null && flowController.CurrentState == LevelState.Completed)
        {
            lastBundle = bundle;
            lastBundleLevel = flowController.CurrentLevelData;
            if (PanelAnimator != null) PanelAnimator.PlayIn();
            else if (panel != null) panel.SetActive(true);
            AnalyticsService.Track(AnalyticsEvents.RewardShown, RewardParams());
            
            if (coinsText != null) coinsText.text = $"+{bundle.CoinCount}";
            if (gemsText != null) gemsText.text = $"+{bundle.GemCount}";
            if (starsText != null)
            {
                // LastCompletedStars is the authoritative rating — star pickups
                // drive it when placed, otherwise objective-based.
                int stars = flowController != null ? flowController.LastCompletedStars : 1;
                starsText.text = $"{stars} ★";
            }

            if (doubleCoinsButton != null)
            {
                // Only enable if Ad is available and we earned some coins to double
                bool canDouble = bundle.CoinCount > 0 && 
                                 AdRewardService.Instance != null && 
                                 AdRewardService.Instance.IsAdAvailable(RewardedAdPlacement.LevelClearCoinDoubler);
                doubleCoinsButton.gameObject.SetActive(canDouble);
                doubleCoinsButton.interactable = canDouble;
            }
        }
    }

    private void OnContinueClicked()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(() => panel.SetActive(false));
        else if (panel != null) panel.SetActive(false);
        if (flowController != null)
        {
            flowController.ReturnToMap();
        }
    }

    private void HandleStateChanged(LevelState state)
    {
        // Leaving the Completed state while a reward bundle was displayed means
        // the player accepted it — regardless of which exit button was pressed
        // (NEXT, MAP, or RESTART all dismiss the overlay).
        if (state != LevelState.Completed && lastBundle != null)
        {
            AnalyticsService.Track(AnalyticsEvents.RewardClaimed, RewardParams());
            lastBundle = null;
            lastBundleLevel = null;
        }
    }

    private System.Collections.Generic.Dictionary<string, object> RewardParams()
    {
        var p = new System.Collections.Generic.Dictionary<string, object>();
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
        if (lastBundle.CoinCount <= 0) return;

        hasClaimedDouble = true;
        // Disable button to prevent spam
        if (doubleCoinsButton != null)
            doubleCoinsButton.interactable = false;

        // Use the ad service to grant the reward upon completion
        AdRewardService.Instance.WatchAdForReward(RewardedAdPlacement.LevelClearCoinDoubler, lastBundle.CoinCount, success =>
        {
            if (success)
            {
                // Visual update
                if (coinsText != null) coinsText.text = $"+{lastBundle.CoinCount * 2}";
                if (doubleCoinsButton != null)
                    doubleCoinsButton.gameObject.SetActive(false); // Hide after use
            }
            else
            {
                // Restore interaction if failed
                hasClaimedDouble = false;
                if (doubleCoinsButton != null)
                    doubleCoinsButton.interactable = true;
            }
        });
    }
}

