using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class CoinUI : MonoBehaviour, IUIPanel
{
    public event Action OnCoinBonusClosed;

    [Header("Coin Display")]
    [Tooltip("Displays the coin amount granted or doubled.")]
    [SerializeField] private TMP_Text coinsText;

    [Header("Actions")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Button collectButton;
    [SerializeField] private TMP_Text collectButtonLabel;
    [SerializeField] private Button collectAdButton;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private CoinBonusService bonusService;
    private AdRewardService adService;
    private bool buttonsBound;
    private bool subscribed;
    private bool isWaitingForAd;

    private bool isDirectRewardMode;
    private int directRewardBaseCoins;
    private bool directRewardBaseAlreadyGranted;
    private bool hasDoubledInCurrentSession;
    private int displayedCoinValue;

    public bool IsDirectRewardMode => isDirectRewardMode;
    public bool HasDoubledInCurrentSession => hasDoubledInCurrentSession;
    public int DisplayedCoinValue => displayedCoinValue;

    private void Awake()
    {
        ResolveReferences();
        BindButtons();
    }

    private void OnEnable()
    {
        ResolveReferences();
        BindButtons();
        Subscribe();
        isWaitingForAd = false;
        RefreshUI();
    }

    private void OnDisable()
    {
        Unsubscribe();
        isWaitingForAd = false;
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
    }

    public void Show() => OpenCoinBonus();
    public void Hide() => CloseCoinBonus();

    /// <summary>
    /// Opens the <see cref="CoinUI"/> popup (<see cref="UIPanelType.CoinBonus"/>) for a chest coin reward.
    /// </summary>
    public static bool TryShowCoinRewardPopup(int coinAmount, bool alreadyGrantedBase = true)
    {
        if (coinAmount <= 0)
            return false;

        CoinUI coinPanel = null;
        if (UIManager.Instance != null)
        {
            coinPanel = UIManager.Instance.GetPanel<CoinUI>(UIPanelType.CoinBonus);
        }

        if (coinPanel == null)
        {
            coinPanel = FindAnyObjectByType<CoinUI>(FindObjectsInactive.Include);
        }

        if (coinPanel == null)
            return false;

        coinPanel.PrepareDirectCoinReward(coinAmount, alreadyGrantedBase);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelRegistered(UIPanelType.CoinBonus))
        {
            if (!UIManager.Instance.IsPanelOpen(UIPanelType.CoinBonus))
                UIManager.Instance.PushPopup(UIPanelType.CoinBonus);
            else
                coinPanel.OpenCoinBonus();
        }
        else
        {
            coinPanel.OpenCoinBonus();
        }

        return true;
    }

    public void PrepareDirectCoinReward(int coinAmount, bool alreadyGrantedBase = true)
    {
        isDirectRewardMode = true;
        directRewardBaseCoins = Mathf.Max(0, coinAmount);
        directRewardBaseAlreadyGranted = alreadyGrantedBase;
        hasDoubledInCurrentSession = false;
        displayedCoinValue = 0;
        isWaitingForAd = false;
    }

    public void OpenCoinBonus()
    {
        ResolveReferences();
        BindButtons();
        isWaitingForAd = false;

        if (panelAnimator != null)
            panelAnimator.PlayIn();
        else
            gameObject.SetActive(true);

        RefreshUI(animateCurrency: true);
    }

    public void CloseCoinBonus()
    {
        isWaitingForAd = false;
        isDirectRewardMode = false;
        hasDoubledInCurrentSession = false;

        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(FinishCloseCoinBonus);
        else
            FinishCloseCoinBonus();
    }

    private void FinishCloseCoinBonus()
    {
        gameObject.SetActive(false);
        OnCoinBonusClosed?.Invoke();
    }

    private void ResolveReferences()
    {
        if (bonusService == null)
            bonusService = CoinBonusService.Instance ?? FindAnyObjectByType<CoinBonusService>();

        if (adService == null)
            adService = AdRewardService.Instance ?? FindAnyObjectByType<AdRewardService>();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);

        if (collectButton != null)
            collectButton.onClick.AddListener(OnCollectClicked);

        if (collectAdButton != null)
            collectAdButton.onClick.AddListener(OnCollectAdClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);

        if (collectButton != null)
            collectButton.onClick.RemoveListener(OnCollectClicked);

        if (collectAdButton != null)
            collectAdButton.onClick.RemoveListener(OnCollectAdClicked);
    }

    private void OnCloseClicked()
    {
        if (isWaitingForAd) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        EnsurePendingDirectBaseRewardGranted();
        CloseThroughManagerOrSelf();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (bonusService != null)
            bonusService.OnCoinBonusClaimed += HandleBonusClaimed;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (bonusService != null)
            bonusService.OnCoinBonusClaimed -= HandleBonusClaimed;
    }

    public void RefreshUI() => RefreshUI(animateCurrency: false);

    public void RefreshUI(bool animateCurrency)
    {
        ResolveReferences();

        if (isDirectRewardMode)
        {
            int targetCoins = hasDoubledInCurrentSession ? directRewardBaseCoins * 2 : directRewardBaseCoins;
            SetDisplayedCoins(targetCoins, animateCurrency);

            if (collectButton != null)
            {
                collectButton.gameObject.SetActive(true);
                collectButton.interactable = !isWaitingForAd;
            }

            if (collectButtonLabel != null)
                collectButtonLabel.text = "COLLECT";

            if (closeButton != null)
            {
                closeButton.gameObject.SetActive(true);
                closeButton.interactable = !isWaitingForAd;
            }

            if (collectAdButton != null)
            {
                bool showAdBtn = !hasDoubledInCurrentSession && directRewardBaseCoins > 0;
                if (showAdBtn && adService != null)
                {
                    showAdBtn = adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier)
                                || adService.IsAdAvailable(RewardedAdPlacement.LevelClearCoinDoubler);
                }

                collectAdButton.gameObject.SetActive(showAdBtn);
                collectAdButton.interactable = showAdBtn && !isWaitingForAd;
            }
            return;
        }

        if (hasDoubledInCurrentSession)
        {
            SetDisplayedCoins(displayedCoinValue, animateCurrency);

            if (collectButton != null)
            {
                collectButton.gameObject.SetActive(true);
                collectButton.interactable = true;
            }

            if (collectButtonLabel != null)
                collectButtonLabel.text = "COLLECT";

            if (collectAdButton != null)
                collectAdButton.gameObject.SetActive(false);

            return;
        }

        if (bonusService == null)
        {
            SetDisplayedCoins(0, false);
            if (collectButton != null)
                collectButton.interactable = false;
            if (collectButtonLabel != null)
                collectButtonLabel.text = "UNAVAILABLE";
            if (collectAdButton != null)
                collectAdButton.gameObject.SetActive(false);
            return;
        }

        int bonus = bonusService.CalculateBonusAmount();
        SetDisplayedCoins(bonus, animateCurrency);

        bool canCollect = bonusService.CanCollectBonus();
        if (canCollect)
        {
            if (collectButton != null)
                collectButton.interactable = !isWaitingForAd;
            if (collectButtonLabel != null)
                collectButtonLabel.text = "COLLECT";
        }
        else
        {
            TimeSpan remaining = bonusService.GetTimeUntilNextBonus();
            int hours = Mathf.Max(0, (int)remaining.TotalHours);
            int minutes = Mathf.Max(0, remaining.Minutes);

            if (collectButton != null)
                collectButton.interactable = false;
            if (collectButtonLabel != null)
                collectButtonLabel.text = $"{hours}H {minutes:D2}M";
        }

        if (collectAdButton != null)
        {
            bool adAvailable = canCollect
                               && !hasDoubledInCurrentSession
                               && adService != null
                               && adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier);
            collectAdButton.gameObject.SetActive(adAvailable);
            collectAdButton.interactable = adAvailable && !isWaitingForAd;
        }
    }

    private void SetDisplayedCoins(int targetCoins, bool animate)
    {
        displayedCoinValue = Mathf.Max(0, targetCoins);
        if (coinsText == null)
            return;

        CurrencyUI.Set(coinsText, displayedCoinValue, animate);
    }

    private void OnCollectClicked()
    {
        if (isWaitingForAd) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (isDirectRewardMode || hasDoubledInCurrentSession)
        {
            EnsurePendingDirectBaseRewardGranted();
            UIAudio.Play(UISoundType.RewardClaim);
            CloseThroughManagerOrSelf();
            return;
        }

        if (bonusService == null) return;

        bonusService.CollectBonus(false, success =>
        {
            if (this == null) return;
            RefreshUI();
            if (success)
            {
                UIAudio.Play(UISoundType.RewardClaim);
                CloseThroughManagerOrSelf();
            }
        });
    }

    private void OnCollectAdClicked()
    {
        if (isWaitingForAd || hasDoubledInCurrentSession) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (isDirectRewardMode)
        {
            if (directRewardBaseCoins <= 0)
                return;

            isWaitingForAd = true;
            RefreshUI(animateCurrency: false);

            ResolveReferences();
            if (adService != null)
            {
                RewardedAdPlacement placement = adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier)
                    ? RewardedAdPlacement.CoinBonusMultiplier
                    : RewardedAdPlacement.LevelClearCoinDoubler;

                int extraCoins = directRewardBaseAlreadyGranted ? directRewardBaseCoins : (directRewardBaseCoins * 2);
                adService.WatchAdForReward(placement, extraCoins, success =>
                {
                    isWaitingForAd = false;
                    if (this == null) return;

                    if (success)
                    {
                        directRewardBaseAlreadyGranted = true;
                        hasDoubledInCurrentSession = true;
                        UIAudio.Play(UISoundType.RewardClaim);
                        RefreshUI(animateCurrency: true);
                    }
                    else
                    {
                        UIAudio.Play(UISoundType.ErrorOrLocked);
                        RefreshUI(animateCurrency: false);
                    }
                });
            }
            else
            {
                int extraCoins = directRewardBaseAlreadyGranted ? directRewardBaseCoins : (directRewardBaseCoins * 2);
                GrantDirectCoins(extraCoins, "CoinUI_AdDouble");
                directRewardBaseAlreadyGranted = true;
                isWaitingForAd = false;
                hasDoubledInCurrentSession = true;
                UIAudio.Play(UISoundType.RewardClaim);
                RefreshUI(animateCurrency: true);
            }
            return;
        }

        if (bonusService == null || !bonusService.CanCollectBonus()) return;

        int baseBonus = bonusService.CalculateBonusAmount();
        isWaitingForAd = true;
        RefreshUI(animateCurrency: false);

        bonusService.CollectBonus(true, success =>
        {
            isWaitingForAd = false;
            if (this == null) return;

            if (success)
            {
                hasDoubledInCurrentSession = true;
                displayedCoinValue = baseBonus * 2;
                UIAudio.Play(UISoundType.RewardClaim);
                RefreshUI(animateCurrency: true);
            }
            else
            {
                RefreshUI(animateCurrency: false);
            }
        });
    }

    private void EnsurePendingDirectBaseRewardGranted()
    {
        if (!isDirectRewardMode || directRewardBaseAlreadyGranted || directRewardBaseCoins <= 0)
            return;

        GrantDirectCoins(directRewardBaseCoins, "CoinUI_Collect");
        directRewardBaseAlreadyGranted = true;
    }

    private static void GrantDirectCoins(int amount, string reason)
    {
        if (amount <= 0) return;

        var bundle = new RewardBundle();
        bundle.Add(RewardType.Coins, amount);

        RewardService.ExecuteTransaction(new RewardTransaction
        {
            RequireUniqueRewardId = false,
            RejectIfSkillCapExceeded = false,
            Payout = bundle,
            Reason = reason
        });
    }

    private void HandleBonusClaimed(int claimedAmount)
    {
        if (!isDirectRewardMode && !hasDoubledInCurrentSession)
            RefreshUI(animateCurrency: false);
    }

    private void CloseThroughManagerOrSelf()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.CoinBonus))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.CoinBonus);
        else
            CloseCoinBonus();
    }
}
