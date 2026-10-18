using TMPro;
using System;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Daily Reward screen. Exposes DailyRewardService state: streak day,
/// reward preview, availability, and countdown. All claim logic stays in
/// DailyRewardService / RewardService — this panel only reads state and
/// forwards the claim call.
/// </summary>
public class DailyRewardUI : MonoBehaviour, IUIPanel
{

    public event Action OnDailyRewardClosed;

    [Header("Panel")]
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private Button backButton;
    [SerializeField] private TMP_Text titleText;

    [Header("Currency")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;

    [Header("Reward Display")]
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button claimButton;
    [SerializeField] private TMP_Text claimButtonLabel;
    [SerializeField] private TMP_Text feedbackText;

    private DailyRewardService dailyService;
    private EconomyManager economy;
    private bool subscribed;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (panelRoot != null ? panelRoot : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (panelRoot == null) panelRoot = gameObject;
        if (backButton != null) backButton.onClick.AddListener(CloseDailyReward);
        if (claimButton != null) claimButton.onClick.AddListener(OnClaimClicked);
    }

    private void OnEnable()
    {
        if (dailyService == null) dailyService = DailyRewardService.Instance ?? FindAnyObjectByType<DailyRewardService>();
        if (economy == null) economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
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

        if (dailyService != null)
            dailyService.OnDailyRewardClaimed += HandleDailyRewardClaimed;

        if (economy != null)
        {
            economy.OnCoinsChanged += HandleCurrencyChanged;
            economy.OnGemsChanged += HandleCurrencyChanged;
        }
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (dailyService != null)
            dailyService.OnDailyRewardClaimed -= HandleDailyRewardClaimed;

        if (economy != null)
        {
            economy.OnCoinsChanged -= HandleCurrencyChanged;
            economy.OnGemsChanged -= HandleCurrencyChanged;
        }
    }

    public void Show() => OpenDailyReward();
    public void Hide() => CloseDailyReward();

    public void OpenDailyReward()
    {
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else panelRoot.SetActive(true);
        RefreshAll();
    }

    public void CloseDailyReward()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseDailyReward);
        else FinishCloseDailyReward();
    }

    private void FinishCloseDailyReward()
    {
        panelRoot.SetActive(false);
        OnDailyRewardClosed?.Invoke();
    }

    private void RefreshAll()
    {
        UpdateCurrencyTexts();

        if (dailyService == null)
        {
            if (statusText != null) statusText.text = "Daily rewards unavailable.";
            if (claimButton != null) claimButton.interactable = false;
            return;
        }

        int upcomingDay = dailyService.GetUpcomingStreakDay();
        if (dayText != null) dayText.text = $"Day {upcomingDay} of 7";

        if (rewardText != null)
        {
            var bundle = dailyService.BuildStreakRewardBundle(upcomingDay);
            rewardText.text = FormatBundle(bundle);
        }

        bool canClaim = dailyService.CanClaimDailyReward();
        if (canClaim)
        {
            if (statusText != null) statusText.text = "Reward available!";
            if (claimButton != null) claimButton.interactable = true;
            if (claimButtonLabel != null) claimButtonLabel.text = "CLAIM";
        }
        else
        {
            var remaining = dailyService.GetTimeUntilNextDailyReward();
            if (statusText != null)
                statusText.text = $"Next reward in {remaining.Hours}h {remaining.Minutes}m";
            if (claimButton != null) claimButton.interactable = false;
            if (claimButtonLabel != null) claimButtonLabel.text = "CLAIMED";
        }
    }

    private static string FormatBundle(RewardBundle bundle)
    {
        if (bundle == null || bundle.IsEmpty) return "No reward";
        var sb = new StringBuilder();
        for (int i = 0; i < bundle.Items.Count; i++)
        {
            if (sb.Length > 0) sb.Append("  +  ");
            sb.Append(bundle.Items[i].ToString());
        }
        return sb.ToString();
    }

    private void UpdateCurrencyTexts()
    {
        if (economy == null) return;
        if (coinsText != null) coinsText.text = economy.Coins.ToString();
        if (gemsText != null) gemsText.text = economy.Gems.ToString();
    }

    private void OnClaimClicked()
    {
        if (dailyService == null) return;
        // ClaimDailyReward guards double-claim internally; UI only forwards.
        if (!dailyService.ClaimDailyReward())
        {
            if (feedbackText != null) feedbackText.text = "Already claimed today.";
        }
    }

    private void HandleDailyRewardClaimed(int day, RewardBundle bundle)
    {
        if (feedbackText != null)
            feedbackText.text = $"Claimed Day {day}: {FormatBundle(bundle)}";
        RefreshAll();
    }

    private void HandleCurrencyChanged(int oldValue, int newValue) => UpdateCurrencyTexts();
}

