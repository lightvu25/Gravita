using System;
using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class ShopUI : MonoBehaviour, IUIPanel
{
    [Serializable]
    public class ShopCategoryTabBinding
    {
        public ShopTabCategory category = ShopTabCategory.Currency;
        public Button tabButton;
        public Image tabBackground;
    }

    [Header("Navigation")]
    [SerializeField] private Button backButton;

    [Header("Shop Data")]
    [Tooltip("ShopDatabase ScriptableObject containing all ShopItemDefinition assets. Falls back to ShopManager.Database if unassigned.")]
    [SerializeField] private ShopDatabase shopDatabase;

    [Header("Category Tabs (Currency / Cosmetic / Power / Pack / Offer)")]
    [SerializeField] private ShopTabCategory defaultCategory = ShopTabCategory.Currency;
    [SerializeField] private List<ShopCategoryTabBinding> categoryTabs = new List<ShopCategoryTabBinding>
    {
        new ShopCategoryTabBinding { category = ShopTabCategory.Currency },
        new ShopCategoryTabBinding { category = ShopTabCategory.Cosmetic },
        new ShopCategoryTabBinding { category = ShopTabCategory.Power },
        new ShopCategoryTabBinding { category = ShopTabCategory.Pack },
        new ShopCategoryTabBinding { category = ShopTabCategory.Offer }
    };
    [SerializeField] private Color selectedCategoryTabColor = new Color(0.22f, 0.68f, 0.98f, 1f);
    [SerializeField] private Color unselectedCategoryTabColor = new Color(0.18f, 0.24f, 0.36f, 1f);
    [SerializeField, Min(1f)] private float selectedTabScale = 1.2f;
    [SerializeField, Min(0.01f)] private float tabScaleTweenDuration = 0.18f;

    [Header("Currency Header")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;

    [Header("Item Cards")]
    [FormerlySerializedAs("acclaimText")]
    [SerializeField] private Transform itemsContainer;
    [FormerlySerializedAs("itemCardWidgetTemplate")]
    [SerializeField] private ShopItemCardWidget itemCardPrefab;
    [SerializeField] private TMP_Text feedbackText;

    [Header("Optional Shop Actions")]
    [SerializeField] private Button watchShopAdButton;

    [Header("Optional Animation")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    public event Action OnShopClosed;
    public event Action<ShopTabCategory> OnCategoryChanged;

    private readonly List<ShopItemCardWidget> spawnedWidgets = new List<ShopItemCardWidget>();
    private readonly List<UnityEngine.Events.UnityAction> categoryTabListeners = new List<UnityEngine.Events.UnityAction>();
    private EconomyManager economy;
    private ShopManager shopManager;
    private IAPManager iapManager;
    private ProgressionManager progression;
    private ShopTabCategory activeCategory = ShopTabCategory.All;
    private bool categoryInitialized;
    private bool buttonsBound;
    private bool subscribed;

    public ShopTabCategory ActiveCategory => activeCategory;
    public IReadOnlyList<ShopItemCardWidget> SpawnedWidgets => spawnedWidgets;
    public IReadOnlyList<ShopCategoryTabBinding> CategoryTabs => categoryTabs;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private void OnValidate()
    {
        if (categoryTabs == null || categoryTabs.Count == 0)
        {
            categoryTabs = new List<ShopCategoryTabBinding>
            {
                new ShopCategoryTabBinding { category = ShopTabCategory.Currency },
                new ShopCategoryTabBinding { category = ShopTabCategory.Cosmetic },
                new ShopCategoryTabBinding { category = ShopTabCategory.Power },
                new ShopCategoryTabBinding { category = ShopTabCategory.Pack },
                new ShopCategoryTabBinding { category = ShopTabCategory.Offer }
            };
        }
    }

    private ShopTabCategory ResolveInitialTabCategory()
    {
        if (categoryTabs != null && categoryTabs.Count > 0 && categoryTabs[0] != null)
            return categoryTabs[0].category;

        return defaultCategory != ShopTabCategory.All ? defaultCategory : ShopTabCategory.Currency;
    }

    private void Awake()
    {
        if (!categoryInitialized)
        {
            activeCategory = ResolveInitialTabCategory();
            categoryInitialized = true;
        }

        BindButtons();

        if (itemCardPrefab != null && itemCardPrefab.gameObject.scene.IsValid())
            itemCardPrefab.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        if (!categoryInitialized)
        {
            activeCategory = ResolveInitialTabCategory();
            categoryInitialized = true;
        }

        ResolveReferences();
        BindButtons();

        if (shopManager != null && shopDatabase != null)
            shopManager.SetDatabaseIfNull(shopDatabase);

        Subscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        Unsubscribe();
        if (categoryTabs != null)
        {
            for (int i = 0; i < categoryTabs.Count; i++)
            {
                if (categoryTabs[i]?.tabButton != null)
                    categoryTabs[i].tabButton.transform.DOKill();
            }
        }
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
    }

    private void ResolveReferences()
    {
        if (shopManager == null) shopManager = ShopManager.Instance ?? FindAnyObjectByType<ShopManager>();
        if (economy == null) economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
        if (iapManager == null) iapManager = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
        if (progression == null) progression = ProgressionManager.Instance ?? FindAnyObjectByType<ProgressionManager>();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (backButton != null)
            backButton.onClick.AddListener(OnBackClicked);
        if (watchShopAdButton != null)
            watchShopAdButton.onClick.AddListener(OnWatchShopAdClicked);

        categoryTabListeners.Clear();
        if (categoryTabs != null)
        {
            for (int i = 0; i < categoryTabs.Count; i++)
            {
                ShopCategoryTabBinding binding = categoryTabs[i];
                if (binding == null || binding.tabButton == null)
                {
                    categoryTabListeners.Add(null);
                    continue;
                }

                ShopTabCategory capturedCategory = binding.category;
                UnityEngine.Events.UnityAction action = () => OnCategoryTabClicked(capturedCategory);
                categoryTabListeners.Add(action);
                binding.tabButton.onClick.AddListener(action);
            }
        }
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (backButton != null)
            backButton.onClick.RemoveListener(OnBackClicked);
        if (watchShopAdButton != null)
            watchShopAdButton.onClick.RemoveListener(OnWatchShopAdClicked);

        if (categoryTabs != null)
        {
            for (int i = 0; i < categoryTabs.Count && i < categoryTabListeners.Count; i++)
            {
                if (categoryTabs[i]?.tabButton != null && categoryTabListeners[i] != null)
                    categoryTabs[i].tabButton.onClick.RemoveListener(categoryTabListeners[i]);
            }
        }
        categoryTabListeners.Clear();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (economy != null)
        {
            economy.OnCoinsChanged += HandleCoinsChanged;
            economy.OnGemsChanged += HandleGemsChanged;
        }
        if (progression != null)
            progression.OnProgressionChanged += HandleProgressionChanged;
        if (shopManager != null)
            shopManager.OnItemPurchased += HandleItemPurchased;

        CosmeticService.OnCosmeticsChanged += UpdateCardStates;
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (economy != null)
        {
            economy.OnCoinsChanged -= HandleCoinsChanged;
            economy.OnGemsChanged -= HandleGemsChanged;
        }
        if (progression != null)
            progression.OnProgressionChanged -= HandleProgressionChanged;
        if (shopManager != null)
            shopManager.OnItemPurchased -= HandleItemPurchased;

        CosmeticService.OnCosmeticsChanged -= UpdateCardStates;
    }

    // ─────────────────────────────────────────────────────────────
    //  Open / Close
    // ─────────────────────────────────────────────────────────────

    public void OpenShop()
    {
        activeCategory = ResolveInitialTabCategory();
        categoryInitialized = true;

        BindButtons();
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);

        RefreshAll();
        AnalyticsService.Track(AnalyticsEvents.ShopOpened);
    }

    public void CloseShop()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseShop);
        else FinishCloseShop();
    }

    private void FinishCloseShop()
    {
        gameObject.SetActive(false);
        OnShopClosed?.Invoke();
    }

    private void OnBackClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Shop))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Shop);
        else
            CloseShop();
    }

    public void Show() => OpenShop();
    public void Hide() => CloseShop();

    // ─────────────────────────────────────────────────────────────
    //  Refresh & Category Filtering
    // ─────────────────────────────────────────────────────────────

    public void SelectCategory(ShopTabCategory category)
    {
        categoryInitialized = true;
        activeCategory = category;
        RefreshCategoryTabVisuals();
        RebuildItemCards();
        OnCategoryChanged?.Invoke(activeCategory);
    }

    private void OnCategoryTabClicked(ShopTabCategory category)
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        SelectCategory(category);
    }

    private void RefreshCategoryTabVisuals()
    {
        if (categoryTabs == null) return;

        for (int i = 0; i < categoryTabs.Count; i++)
        {
            ShopCategoryTabBinding binding = categoryTabs[i];
            if (binding == null) continue;

            bool isSelected = binding.category == activeCategory;
            Image targetBg = binding.tabBackground != null
                ? binding.tabBackground
                : (binding.tabButton != null ? binding.tabButton.targetGraphic as Image : null);

            if (targetBg != null)
            {
                targetBg.color = isSelected ? selectedCategoryTabColor : unselectedCategoryTabColor;
            }

            if (binding.tabButton != null)
            {
                Transform btnTransform = binding.tabButton.transform;
                btnTransform.DOKill();
                float targetScale = isSelected ? selectedTabScale : 1f;

                if (Application.isPlaying && binding.tabButton.gameObject.activeInHierarchy)
                {
                    btnTransform
                        .DOScale(targetScale, tabScaleTweenDuration)
                        .SetEase(isSelected ? Ease.OutBack : Ease.OutQuad)
                        .SetUpdate(true)
                        .SetLink(binding.tabButton.gameObject);
                }
                else
                {
                    btnTransform.localScale = Vector3.one * targetScale;
                }
            }
        }
    }

    public void RefreshAll()
    {
        if (!categoryInitialized)
        {
            activeCategory = ResolveInitialTabCategory();
            categoryInitialized = true;
        }

        ResolveReferences();
        UpdateCurrencyTexts(animate: false);
        RefreshShopAdButton();
        RefreshCategoryTabVisuals();
        RebuildItemCards();
    }

    private void UpdateCurrencyTexts(bool animate = false)
    {
        if (economy == null) return;
        CurrencyUI.Set(coinsText, economy.Coins, animate);
        CurrencyUI.Set(gemsText, economy.Gems, animate);
    }

    private void RefreshShopAdButton()
    {
        if (watchShopAdButton == null)
            return;

        bool canWatch = AdRewardService.Instance != null
            && AdRewardService.Instance.IsAdAvailable(RewardedAdPlacement.ShopBonus);

        watchShopAdButton.interactable = canWatch;
        if (canWatch)
            AdPolicyCoordinator.NotifyAdOffered(RewardedAdPlacement.ShopBonus);
    }

    private void HandleCoinsChanged(int newBalance, int delta) => UpdateCurrencyTexts(animate: delta != 0);
    private void HandleGemsChanged(int newBalance, int delta) => UpdateCurrencyTexts(animate: delta != 0);

    private void HandleProgressionChanged()
    {
        UpdateCurrencyTexts();
        UpdateCardStates();
        RefreshShopAdButton();
    }

    private void HandleItemPurchased(ShopItemDefinition item) => UpdateCardStates();

    private ShopDatabase ResolveDatabase()
    {
        if (shopDatabase != null) return shopDatabase;
        if (shopManager != null && shopManager.Database != null) return shopManager.Database;
        return null;
    }

    private static bool ShouldDisplayItemInCampaignShop(ShopItemDefinition item)
    {
        if (item == null) return false;

        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        if (item.Rewards != null)
        {
            for (int i = 0; i < item.Rewards.Count; i++)
            {
                RewardItem r = item.Rewards[i];
                if (r.Type == RewardType.PowerUp && !balance.IsSkillActiveInCampaign(r.ItemId))
                    return false;
            }
        }
        return true;
    }

    private void ClearSpawnedCards(GameObject templateObj)
    {
        if (itemsContainer != null)
        {
            for (int i = itemsContainer.childCount - 1; i >= 0; i--)
            {
                Transform child = itemsContainer.GetChild(i);
                if (child.gameObject == templateObj) continue;
                if (Application.isPlaying)
                    Destroy(child.gameObject);
                else
                    DestroyImmediate(child.gameObject);
            }
        }

        spawnedWidgets.Clear();
    }

    private void RebuildItemCards()
    {
        if (itemCardPrefab == null || itemsContainer == null)
            return;

        GameObject templateObj = itemCardPrefab.gameObject;
        if (templateObj.scene.IsValid() && templateObj.activeSelf)
            templateObj.SetActive(false);

        ClearSpawnedCards(templateObj);

        ShopDatabase db = ResolveDatabase();
        if (db == null || db.Items == null)
            return;

        foreach (var item in db.Items)
        {
            if (!ShouldDisplayItemInCampaignShop(item)) continue;
            if (activeCategory != ShopTabCategory.All && item.ShopTab != activeCategory)
                continue;

            ShopItemCardWidget widget = Instantiate(itemCardPrefab, itemsContainer);
            widget.name = $"ShopCard_{item.ItemId}";
            widget.gameObject.SetActive(true);
            widget.Setup(item, OnBuyClicked, iapManager, shopManager);
            spawnedWidgets.Add(widget);
        }
    }

    private void UpdateCardStates()
    {
        for (int i = 0; i < spawnedWidgets.Count; i++)
        {
            if (spawnedWidgets[i] != null)
                spawnedWidgets[i].Refresh(iapManager, shopManager);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Purchase, Equip, Shop Ad & Restore
    // ─────────────────────────────────────────────────────────────

    private void OnBuyClicked(ShopItemDefinition item)
    {
        if (item == null) return;

        // Direct Equip / Unequip toggle for already-owned PlayerSkin cosmetics (GDD §6.3)
        if (ShopItemCardWidget.TryGetSingleCosmeticId(item, out string cosmeticId) && CosmeticService.IsOwned(cosmeticId))
        {
            bool currentlyEquipped = string.Equals(CosmeticService.GetEquippedPlayerSkinId(), cosmeticId, StringComparison.Ordinal);
            string nextSkin = currentlyEquipped ? CosmeticService.DefaultSkinId : cosmeticId;
            if (CosmeticService.TryEquipPlayerSkin(nextSkin))
            {
                UIAudio.Play(UISoundType.ButtonClick);
                ShowFeedback(currentlyEquipped
                    ? "Restored default Subject G-0 skin."
                    : $"Equipped {item.DisplayName}!");
            }
            UpdateCardStates();
            return;
        }

        if (shopManager == null)
            shopManager = ShopManager.Instance ?? FindAnyObjectByType<ShopManager>();
        if (shopManager == null) return;

        if (!shopManager.EvaluatePurchase(item, out ShopPurchaseBlockReason blockReason))
        {
            if (blockReason == ShopPurchaseBlockReason.InsufficientCoins || blockReason == ShopPurchaseBlockReason.InsufficientGems)
            {
                AnalyticsService.Track(AnalyticsEvents.ShopPurchaseFailed, new Dictionary<string, object>
                {
                    ["item_id"] = item.ItemId,
                    ["reason"]  = "insufficient_currency"
                });
            }
            UIAudio.Play(UISoundType.ErrorOrLocked);
            ShowFeedback(RejectMessage(blockReason));
            UpdateCardStates();
            return;
        }

        ShowFeedback("Purchasing...");
        shopManager.PurchaseItem(item, success =>
        {
            if (this == null) return;

            if (success)
                UIAudio.Play(UISoundType.PurchaseSuccess);
            else
                UIAudio.Play(UISoundType.ErrorOrLocked);

            string successMsg = (item.HasLootPack && !string.IsNullOrEmpty(shopManager.LastPurchasedLootSummary))
                ? $"Opened {item.DisplayName}: {shopManager.LastPurchasedLootSummary}!"
                : $"Purchased {item.DisplayName}!";

            ShowFeedback(success
                ? successMsg
                : $"Could not purchase {item.DisplayName}.");
            UpdateCurrencyTexts(animate: true);
            UpdateCardStates();
            RefreshShopAdButton();
        });
    }

    public void OnWatchShopAdClicked()
    {
        if (AdRewardService.Instance == null) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (watchShopAdButton != null)
            watchShopAdButton.interactable = false;

        RewardItem fixedItem = AdPolicyCoordinator.GetFixedRewardForPlacement(RewardedAdPlacement.ShopBonus);
        AdRewardService.Instance.WatchAdForReward(RewardedAdPlacement.ShopBonus, fixedItem.Amount, success =>
        {
            if (this == null) return;
            if (success)
            {
                UIAudio.Play(UISoundType.RewardClaim);
                ShowFeedback($"Ad reward claimed: +{fixedItem.Amount} {fixedItem.Type}!");
            }
            else
            {
                UIAudio.Play(UISoundType.ErrorOrLocked);
                ShowFeedback("Rewarded Ad was not completed.");
            }
            UpdateCurrencyTexts();
            UpdateCardStates();
            RefreshShopAdButton();
        });
    }

    private static string RejectMessage(ShopPurchaseBlockReason reason)
    {
        return reason switch
        {
            ShopPurchaseBlockReason.AlreadyOwned          => "Already owned.",
            ShopPurchaseBlockReason.InsufficientCoins     => "Not enough Coins.",
            ShopPurchaseBlockReason.InsufficientGems      => "Not enough Gems.",
            ShopPurchaseBlockReason.SkillCapExceeded      => "Skill inventory at max cap!",
            ShopPurchaseBlockReason.InvalidCurrencyRule   => "Invalid offer configuration.",
            ShopPurchaseBlockReason.InactiveCampaignSkill => "Skill unavailable in Campaign.",
            ShopPurchaseBlockReason.IapUnavailable        => "Store unavailable.",
            _                                             => "Cannot purchase."
        };
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null) feedbackText.text = message;
    }
}
