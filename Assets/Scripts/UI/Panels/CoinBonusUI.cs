using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

/// <summary>
/// Coin Bonus screen. Exposes CoinBonusService state: bonus amount,
/// cooldown status, and collection. Optional ad-doubling path is shown
/// only when AdRewardService reports the placement as available.
/// All bonus/cooldown logic stays in CoinBonusService.
/// </summary>
public class CoinBonusUI : MonoBehaviour, IUIPanel
{

    public event Action OnCoinBonusClosed;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text titleText;

    [Header("Currency")]
    [SerializeField] private TMP_Text coinsText;

    [Header("Bonus Display")]
    [FormerlySerializedAs("tipText")]
    [SerializeField] private TMP_Text bonusText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button collectButton;
    [SerializeField] private TMP_Text collectButtonLabel;
    [SerializeField] private Button collectAdButton;
    [SerializeField] private TMP_Text feedbackText;

    private CoinBonusService bonusService;
    private EconomyManager economy;
    private AdRewardService adService;
    private bool subscribed;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (panelRoot != null ? panelRoot : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (backButton != null) backButton.onClick.AddListener(CloseCoinBonus);
        if (collectButton != null) collectButton.onClick.AddListener(OnCollectClicked);
        if (collectAdButton != null) collectAdButton.onClick.AddListener(OnCollectAdClicked);
    }

    private void OnEnable()
    {
        if (bonusService == null) bonusService = CoinBonusService.Instance ?? FindAnyObjectByType<CoinBonusService>();
        if (economy == null) economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
        if (adService == null) adService = AdRewardService.Instance ?? FindAnyObjectByType<AdRewardService>();
        Subscribe();
        RefreshAll();
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (bonusService != null)
            bonusService.OnCoinBonusClaimed += HandleBonusClaimed;

        if (economy != null)
            economy.OnCoinsChanged += HandleCoinsChanged;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (bonusService != null)
            bonusService.OnCoinBonusClaimed -= HandleBonusClaimed;

        if (economy != null)
            economy.OnCoinsChanged -= HandleCoinsChanged;
    }

    public void Show() => OpenCoinBonus();
    public void Hide() => CloseCoinBonus();

    public void OpenCoinBonus()
    {
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else panelRoot.SetActive(true);
        RefreshAll();
    }

    public void CloseCoinBonus()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseCoinBonus);
        else FinishCloseCoinBonus();
    }

    private void FinishCloseCoinBonus()
    {
        panelRoot.SetActive(false);
        OnCoinBonusClosed?.Invoke();
    }

    private void RefreshAll()
    {
        if (coinsText != null && economy != null)
            coinsText.text = economy.Coins.ToString();

        if (bonusService == null)
        {
            if (statusText != null) statusText.text = "Coin bonus unavailable.";
            if (collectButton != null) collectButton.interactable = false;
            if (collectAdButton != null) collectAdButton.gameObject.SetActive(false);
            return;
        }

        int bonus = bonusService.CalculateBonusAmount();
        if (bonusText != null)
            bonusText.text = $"Bonus: {bonus} Coins";

        bool canCollect = bonusService.CanCollectBonus();
        if (canCollect)
        {
            if (statusText != null) statusText.text = "Bonus ready to collect!";
            if (collectButton != null) collectButton.interactable = true;
            if (collectButtonLabel != null) collectButtonLabel.text = "COLLECT";
        }
        else
        {
            var remaining = bonusService.GetTimeUntilNextBonus();
            if (statusText != null)
                statusText.text = $"Next bonus in {remaining.Hours}h {remaining.Minutes}m";
            if (collectButton != null) collectButton.interactable = false;
            if (collectButtonLabel != null) collectButtonLabel.text = "COLLECTED";
        }

        // Ad-doubling is only offered when the ad service reports the
        // placement as available AND a collection is actually ready.
        if (collectAdButton != null)
        {
            bool adAvailable = canCollect && adService != null
                && adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier);
            collectAdButton.gameObject.SetActive(adAvailable);
        }
    }

    private void OnCollectClicked()
    {
        if (bonusService == null) return;
        bonusService.CollectBonus(false, success =>
        {
            if (!success && feedbackText != null)
                feedbackText.text = "Bonus is not ready yet.";
        });
    }

    private void OnCollectAdClicked()
    {
        if (bonusService == null) return;
        bonusService.CollectBonus(true, success =>
        {
            if (!success && feedbackText != null)
                feedbackText.text = "Bonus is not ready yet.";
        });
    }

    private void HandleBonusClaimed(int amount)
    {
        if (feedbackText != null)
            feedbackText.text = $"Collected {amount} Coins!";
        RefreshAll();
    }

    private void HandleCoinsChanged(int oldValue, int newValue)
    {
        if (coinsText != null) coinsText.text = newValue.ToString();
    }
}
