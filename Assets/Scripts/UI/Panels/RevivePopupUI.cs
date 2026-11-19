using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Modal popup displayed after the Death cutscene when mid-gameplay revive is enabled
/// and the player still has revive lives remaining.
/// </summary>
public class RevivePopupUI : MonoBehaviour, IUIPanel
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Labels")]
    [SerializeField] private TMP_Text livesRemainingText;

    [Header("Coin Revive Button")]
    [SerializeField] private Button coinReviveButton;
    [SerializeField] private TMP_Text coinCostText;

    [Header("Rewarded Ad Revive Button")]
    [SerializeField] private Button watchAdButton;

    [Header("Dismiss / Give Up Button")]
    [SerializeField] private Button dismissButton;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private bool buttonsBound;
    private bool economySubscribed;
    private bool isWaitingForAd;

    private void Awake()
    {
        ResolveReferences();
        BindButtons();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButtons();
        SubscribeEconomy();
        isWaitingForAd = false;
        RefreshUI();
    }

    private void OnDisable()
    {
        UnsubscribeEconomy();
        isWaitingForAd = false;
    }

    private void OnDestroy()
    {
        UnsubscribeEconomy();
        UnbindButtons();
    }

    public void Show()
    {
        ResolveReferences();
        if (!EconomyBalanceConfig.Active.EnableMidGameplayRevive)
        {
            gameObject.SetActive(false);
            if (flowController != null)
                flowController.DeclineRevive();
            else
                UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);
            return;
        }

        BindButtons();
        isWaitingForAd = false;

        if (PanelAnimator != null)
            PanelAnimator.PlayIn();
        else
            gameObject.SetActive(true);

        RefreshUI();
    }

    public void Hide()
    {
        isWaitingForAd = false;

        if (PanelAnimator != null)
            PanelAnimator.PlayOut(() => gameObject.SetActive(false));
        else
            gameObject.SetActive(false);
    }

    private void ResolveReferences()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (coinReviveButton != null)
            coinReviveButton.onClick.AddListener(OnCoinReviveClicked);

        if (watchAdButton != null)
            watchAdButton.onClick.AddListener(OnWatchAdClicked);

        if (dismissButton != null)
            dismissButton.onClick.AddListener(OnDismissClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (coinReviveButton != null)
            coinReviveButton.onClick.RemoveListener(OnCoinReviveClicked);

        if (watchAdButton != null)
            watchAdButton.onClick.RemoveListener(OnWatchAdClicked);

        if (dismissButton != null)
            dismissButton.onClick.RemoveListener(OnDismissClicked);
    }

    private void SubscribeEconomy()
    {
        if (economySubscribed || EconomyManager.Instance == null) return;
        EconomyManager.Instance.OnCoinsChanged += HandleCoinsChanged;
        economySubscribed = true;
    }

    private void UnsubscribeEconomy()
    {
        if (!economySubscribed || EconomyManager.Instance == null) return;
        EconomyManager.Instance.OnCoinsChanged -= HandleCoinsChanged;
        economySubscribed = false;
    }

    private void HandleCoinsChanged(int newTotal, int delta)
    {
        RefreshUI();
    }

    public void RefreshUI()
    {
        ResolveReferences();

        int remaining = flowController != null ? flowController.RemainingRevives : 0;
        int maxLives = flowController != null ? flowController.MaxRevivesPerLevel : 3;
        int coinCost = flowController != null ? flowController.CurrentReviveCoinCost : 100;

        if (livesRemainingText != null)
            livesRemainingText.text = $"LIVES: {remaining}/{maxLives}";

        if (coinCostText != null)
            coinCostText.text = coinCost.ToString();

        bool canAfford = EconomyManager.Instance != null && EconomyManager.Instance.CanAffordCoins(coinCost);
        if (coinReviveButton != null)
            coinReviveButton.interactable = !isWaitingForAd && remaining > 0 && canAfford;

        bool adReady = AdRewardService.Instance == null || AdRewardService.Instance.IsAdAvailable(RewardedAdPlacement.SecondChance);
        if (watchAdButton != null)
            watchAdButton.interactable = !isWaitingForAd && remaining > 0 && adReady;

        if (dismissButton != null)
            dismissButton.interactable = !isWaitingForAd;
    }

    private void OnCoinReviveClicked()
    {
        if (isWaitingForAd || flowController == null) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (!flowController.TryReviveWithCoins())
        {
            RefreshUI();
        }
    }

    private void OnWatchAdClicked()
    {
        if (isWaitingForAd || flowController == null || !flowController.CanRevive) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (AdRewardService.Instance == null)
        {
            flowController.ReviveWithAd();
            return;
        }

        isWaitingForAd = true;
        RefreshUI();

        AdRewardService.Instance.WatchAdForReward(RewardedAdPlacement.SecondChance, 0, success =>
        {
            isWaitingForAd = false;
            if (this == null) return;

            if (success && flowController != null)
            {
                flowController.ReviveWithAd();
            }
            else
            {
                RefreshUI();
            }
        });
    }

    private void OnDismissClicked()
    {
        if (isWaitingForAd) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (flowController != null)
            flowController.DeclineRevive();
        else
            UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);
    }
}
