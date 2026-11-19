using System;
using System.Collections.Generic;
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
    [SerializeField] private Button collectButton;
    [SerializeField] private TMP_Text collectButtonLabel;
    [SerializeField] private Button collectAdButton;

    [Header("Optional Multi-Reward Visuals")]
    [Tooltip("Optional image updated for Coin, Gem, or Power-Up reward popups.")]
    [SerializeField] private Image rewardIconImage;
    [SerializeField] private Sprite coinRewardSprite;
    [SerializeField] private Sprite gemRewardSprite;
    [SerializeField] private PowerUpData[] powerUpAssets;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private CoinBonusService bonusService;
    private AdRewardService adService;
    private bool buttonsBound;
    private bool subscribed;
    private bool isWaitingForAd;

    private bool isDirectRewardMode;
    private int directRewardBaseCoins;
    private RewardBundle directRewardBundle;
    private Action pendingOnClosedCallback;
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

        var bundle = new RewardBundle();
        bundle.Add(RewardType.Coins, coinAmount);
        return TryShowRewardPopup(bundle, alreadyGrantedBase, null);
    }

    /// <summary>
    /// Opens the <see cref="CoinUI"/> popup (<see cref="UIPanelType.CoinBonus"/>) to display any
    /// reward bundle (Coins, Gems, or Power-Up) and invokes <paramref name="onClosedCallback"/> when closed.
    /// </summary>
    public static bool TryShowRewardPopup(RewardBundle bundle, bool alreadyGrantedBase = true, Action onClosedCallback = null)
    {
        if (bundle == null || bundle.IsEmpty)
            return false;

        CoinUI coinPanel = null;
        if (UIManager.Instance != null)
            coinPanel = UIManager.Instance.GetPanel<CoinUI>(UIPanelType.CoinBonus);

        if (coinPanel == null)
            coinPanel = FindAnyObjectByType<CoinUI>(FindObjectsInactive.Include);

        if (coinPanel == null)
            return false;

        coinPanel.PrepareDirectReward(bundle, alreadyGrantedBase, onClosedCallback);

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
        var bundle = new RewardBundle();
        if (coinAmount > 0)
            bundle.Add(RewardType.Coins, coinAmount);
        PrepareDirectReward(bundle, alreadyGrantedBase, null);
    }

    public void PrepareDirectReward(RewardBundle bundle, bool alreadyGrantedBase = true, Action onClosedCallback = null)
    {
        isDirectRewardMode = true;
        directRewardBundle = CloneBundle(bundle);
        directRewardBaseCoins = directRewardBundle != null ? directRewardBundle.CoinCount : 0;
        pendingOnClosedCallback = onClosedCallback;
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
        directRewardBundle = null;
        hasDoubledInCurrentSession = false;

        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(FinishCloseCoinBonus);
        else
            FinishCloseCoinBonus();
    }

    private void FinishCloseCoinBonus()
    {
        gameObject.SetActive(false);
        Action callback = pendingOnClosedCallback;
        pendingOnClosedCallback = null;
        callback?.Invoke();
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

        if (collectButton != null)
            collectButton.onClick.AddListener(OnCollectClicked);

        if (collectAdButton != null)
            collectAdButton.onClick.AddListener(OnCollectAdClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (collectButton != null)
            collectButton.onClick.RemoveListener(OnCollectClicked);

        if (collectAdButton != null)
            collectAdButton.onClick.RemoveListener(OnCollectAdClicked);
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
            RefreshDirectRewardDisplay(animateCurrency);

            if (collectButton != null)
            {
                collectButton.gameObject.SetActive(true);
                collectButton.interactable = !isWaitingForAd;
            }

            if (collectButtonLabel != null)
                collectButtonLabel.text = "COLLECT";

            if (collectAdButton != null)
            {
                bool showAdBtn = CanDoubleDirectRewardViaAd();
                collectAdButton.gameObject.SetActive(showAdBtn);
                collectAdButton.interactable = showAdBtn && !isWaitingForAd;
            }
            return;
        }

        ApplyRewardIcon(coinRewardSprite);

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
            bool hasCoinsOnly = directRewardBundle == null ||
                                (directRewardBundle.CoinCount > 0 && directRewardBundle.GemCount == 0 && !directRewardBundle.HasType(RewardType.PowerUp));
            if (directRewardBaseCoins <= 0 && (directRewardBundle == null || directRewardBundle.IsEmpty))
                return;

            int multiplier = directRewardBaseAlreadyGranted ? 1 : 2;
            RewardBundle grantBundle = BuildDirectRewardBundle(multiplier);
            int extraCoins = directRewardBaseAlreadyGranted ? directRewardBaseCoins : (directRewardBaseCoins * 2);

            isWaitingForAd = true;
            RefreshUI(animateCurrency: false);

            ResolveReferences();
            if (adService != null)
            {
                if (hasCoinsOnly && extraCoins > 0)
                {
                    RewardedAdPlacement placement = adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier)
                        ? RewardedAdPlacement.CoinBonusMultiplier
                        : RewardedAdPlacement.LevelClearCoinDoubler;

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
                    adService.WatchAdForReward(RewardedAdPlacement.BlindBoxDoubler, 0, success =>
                    {
                        isWaitingForAd = false;
                        if (this == null) return;

                        if (success)
                        {
                            GrantDirectBundle(grantBundle, "CoinUI_AdDouble");
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
            }
            else
            {
                GrantDirectBundle(grantBundle, "CoinUI_AdDouble");
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
        if (!isDirectRewardMode || directRewardBaseAlreadyGranted)
            return;

        RewardBundle baseBundle = BuildDirectRewardBundle(1);
        if (baseBundle.IsEmpty)
            return;

        GrantDirectBundle(baseBundle, "CoinUI_Collect");
        directRewardBaseAlreadyGranted = true;
    }

    private RewardBundle BuildDirectRewardBundle(int multiplier)
    {
        int factor = Mathf.Max(1, multiplier);
        var bundle = new RewardBundle();

        if (directRewardBundle != null && !directRewardBundle.IsEmpty)
        {
            IReadOnlyList<RewardItem> items = directRewardBundle.Items;
            for (int i = 0; i < items.Count; i++)
            {
                RewardItem item = items[i];
                if (item.Type == RewardType.Cosmetic)
                    bundle.Add(item);
                else if (item.Amount > 0)
                    bundle.Add(item.Type, item.Amount * factor, item.ItemId);
            }
        }
        else if (directRewardBaseCoins > 0)
        {
            bundle.Add(RewardType.Coins, directRewardBaseCoins * factor);
        }

        return bundle;
    }

    private static RewardBundle CloneBundle(RewardBundle source)
    {
        if (source == null || source.IsEmpty)
            return null;
        return new RewardBundle(source.Items);
    }

    private bool CanDoubleDirectRewardViaAd()
    {
        if (hasDoubledInCurrentSession)
            return false;

        if (directRewardBundle != null && directRewardBundle.GemCount > 0)
            return false;

        bool hasCoins = directRewardBaseCoins > 0;
        bool hasPowerUp = directRewardBundle != null && directRewardBundle.HasType(RewardType.PowerUp);
        if (!hasCoins && !hasPowerUp)
            return false;

        if (adService == null)
            return true;

        if (hasPowerUp)
            return adService.IsAdAvailable(RewardedAdPlacement.BlindBoxDoubler);

        return adService.IsAdAvailable(RewardedAdPlacement.CoinBonusMultiplier)
               || adService.IsAdAvailable(RewardedAdPlacement.LevelClearCoinDoubler);
    }

    private void RefreshDirectRewardDisplay(bool animateCurrency)
    {
        int factor = hasDoubledInCurrentSession ? 2 : 1;

        if (directRewardBundle != null && !directRewardBundle.IsEmpty)
        {
            IReadOnlyList<RewardItem> items = directRewardBundle.Items;
            int totalCoins = 0;
            int totalGems = 0;
            var powerCounts = new List<(string skillId, int count)>();
            int nonZeroCategories = 0;

            for (int i = 0; i < items.Count; i++)
            {
                RewardItem item = items[i];
                if (item.Amount <= 0) continue;

                switch (item.Type)
                {
                    case RewardType.Coins:
                        if (totalCoins == 0) nonZeroCategories++;
                        totalCoins += item.Amount;
                        break;
                    case RewardType.Gems:
                        if (totalGems == 0) nonZeroCategories++;
                        totalGems += item.Amount;
                        break;
                    case RewardType.PowerUp:
                    {
                        bool merged = false;
                        for (int p = 0; p < powerCounts.Count; p++)
                        {
                            if (string.Equals(powerCounts[p].skillId, item.ItemId, StringComparison.OrdinalIgnoreCase))
                            {
                                powerCounts[p] = (powerCounts[p].skillId, powerCounts[p].count + item.Amount);
                                merged = true;
                                break;
                            }
                        }
                        if (!merged)
                        {
                            powerCounts.Add((item.ItemId, item.Amount));
                            nonZeroCategories++;
                        }
                        break;
                    }
                }
            }

            if (nonZeroCategories > 1)
            {
                ApplyRewardIcon(coinRewardSprite);
                var lines = new List<string>(nonZeroCategories);
                if (totalCoins > 0)
                    lines.Add($"+{totalCoins * factor} Coins");
                if (totalGems > 0)
                    lines.Add($"+{totalGems * factor} Gems");
                for (int p = 0; p < powerCounts.Count; p++)
                {
                    ResolvePowerUpVisuals(powerCounts[p].skillId, out string displayName, out _);
                    lines.Add($"+{powerCounts[p].count * factor} {displayName}");
                }

                displayedCoinValue = totalCoins > 0 ? totalCoins * factor : 0;
                if (coinsText != null)
                    coinsText.text = string.Join("\n", lines);
                return;
            }

            RewardItem primary = default;
            for (int i = 0; i < items.Count; i++)
            {
                if (items[i].Amount > 0)
                {
                    primary = items[i];
                    break;
                }
            }

            if (primary.Type == RewardType.Coins)
            {
                ApplyRewardIcon(coinRewardSprite);
                SetDisplayedCoins(primary.Amount * factor, animateCurrency);
                return;
            }

            if (primary.Type == RewardType.Gems)
            {
                ApplyRewardIcon(gemRewardSprite);
                int totalGemDisplay = primary.Amount * factor;
                displayedCoinValue = totalGemDisplay;
                if (coinsText != null)
                    coinsText.text = gemRewardSprite != null ? totalGemDisplay.ToString() : $"+{totalGemDisplay} GEMS";
                return;
            }

            if (primary.Type == RewardType.PowerUp)
            {
                ResolvePowerUpVisuals(primary.ItemId, out string displayName, out Sprite powerIcon);
                ApplyRewardIcon(powerIcon);
                int totalCount = primary.Amount * factor;
                displayedCoinValue = totalCount;
                if (coinsText != null)
                    coinsText.text = (rewardIconImage != null && powerIcon != null)
                        ? $"x{totalCount} {displayName}"
                        : $"+{totalCount} {displayName}";
                return;
            }
        }

        ApplyRewardIcon(coinRewardSprite);
        SetDisplayedCoins(directRewardBaseCoins * factor, animateCurrency);
    }

    private void ApplyRewardIcon(Sprite sprite)
    {
        if (rewardIconImage != null && sprite != null)
            rewardIconImage.sprite = sprite;
    }

    private void ResolvePowerUpVisuals(string skillId, out string displayName, out Sprite icon)
    {
        if (powerUpAssets != null && !string.IsNullOrEmpty(skillId))
        {
            for (int i = 0; i < powerUpAssets.Length; i++)
            {
                PowerUpData asset = powerUpAssets[i];
                if (asset == null) continue;
                string mappedId = PowerUpController.GetPowerUpId(asset.PowerUpType);
                if (string.Equals(asset.Id, skillId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(mappedId, skillId, StringComparison.OrdinalIgnoreCase))
                {
                    displayName = asset.DisplayName;
                    icon = asset.Icon;
                    return;
                }
            }
        }

        PowerUpData controllerData = PowerUpController.Instance != null ? PowerUpController.Instance.GetPowerUpData(skillId) : null;
        if (controllerData != null)
        {
            displayName = controllerData.DisplayName;
            icon = controllerData.Icon;
            return;
        }

        displayName = PowerUpController.GetFallbackDisplayName(skillId);
        icon = null;
    }

    private static void GrantDirectBundle(RewardBundle bundle, string reason)
    {
        if (bundle == null || bundle.IsEmpty) return;

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
