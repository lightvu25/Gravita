using System;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Daily Reward popup. Disabled in standard Campaign when
/// <see cref="EconomyBalanceConfig.EnableLegacyExtraRewardSystems"/> is false (GDD v1.0 uses Daily Missions instead).
/// </summary>
public class DailyRewardUI : MonoBehaviour, IUIPanel
{
    public event Action OnDailyRewardClosed;

    [Header("Navigation")]
    [SerializeField] private Button backButton;

    [Header("Reward Display")]
    [SerializeField] private TMP_Text dayText;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private TMP_Text statusText;
    [SerializeField] private Button claimButton;
    [SerializeField] private TMP_Text claimButtonLabel;
    [SerializeField] private TMP_Text feedbackText;

    private DailyRewardService dailyService;
    private UIPanelAnimator panelAnimator;
    private bool buttonsBound;
    private bool subscribed;

    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        BindButtons();
    }

    private void OnEnable()
    {
        if (dailyService == null)
            dailyService = DailyRewardService.Instance ?? FindAnyObjectByType<DailyRewardService>();

        BindButtons();
        Subscribe();
        RefreshAll();
    }

    private void OnDisable() => Unsubscribe();

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
        if (claimButton != null)
            claimButton.onClick.AddListener(OnClaimClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
        if (claimButton != null)
            claimButton.onClick.RemoveListener(OnClaimClicked);
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (dailyService != null)
            dailyService.OnDailyRewardClaimed += HandleDailyRewardClaimed;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (dailyService != null)
            dailyService.OnDailyRewardClaimed -= HandleDailyRewardClaimed;
    }

    public void Show() => OpenDailyReward();
    public void Hide() => CloseDailyReward();

    private void OnBackClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.DailyReward))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.DailyReward);
        else
            CloseDailyReward();
    }

    public void OpenDailyReward()
    {
        BindButtons();
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
        RefreshAll();
    }

    public void CloseDailyReward()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseDailyReward);
        else FinishCloseDailyReward();
    }

    private void FinishCloseDailyReward()
    {
        gameObject.SetActive(false);
        OnDailyRewardClosed?.Invoke();
    }

    private void RefreshAll()
    {
        if (!EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems)
        {
            if (statusText != null) statusText.text = "See Daily Missions for daily rewards.";
            if (claimButton != null) claimButton.interactable = false;
            return;
        }

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

    private void OnClaimClicked()
    {
        if (dailyService == null) return;

        GameHaptics.Play(HapticEvent.UISelect);
        if (dailyService.ClaimDailyReward())
        {
            UIAudio.Play(UISoundType.RewardClaim);
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
            if (feedbackText != null) feedbackText.text = "Already claimed today.";
        }
    }

    private void HandleDailyRewardClaimed(int day, RewardBundle bundle)
    {
        if (feedbackText != null)
            feedbackText.text = $"Claimed Day {day}: {FormatBundle(bundle)}";
        RefreshAll();
    }
}
