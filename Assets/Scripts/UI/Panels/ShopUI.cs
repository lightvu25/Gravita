using TMPro;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Serialization;

public class ShopUI : MonoBehaviour, IUIPanel
{
    [Header("Panel")]
    [SerializeField] private GameObject shopPanel;
    [SerializeField] private Button backButton;

    [Header("Currency Header")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;
    [FormerlySerializedAs("acclaimText")]

    [Header("Items")]
    [SerializeField] private Transform itemsContainer;
    [SerializeField] private GameObject itemCardTemplate;
    [SerializeField] private TMP_Text feedbackText;

    public event Action OnShopClosed;

    private readonly List<GameObject> spawnedCards = new List<GameObject>();
    private EconomyManager economy;
    private ShopManager shopManager;
    private IAPManager iapManager;
    private ProgressionManager progression;
    private bool subscribed;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (shopPanel != null ? shopPanel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (backButton != null) backButton.onClick.AddListener(OnBackClicked);
    }

    private void OnEnable()
    {
        if (shopManager == null) shopManager = ShopManager.Instance ?? FindAnyObjectByType<ShopManager>();
        if (economy == null) economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
        if (iapManager == null) iapManager = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
        if (progression == null) progression = ProgressionManager.Instance ?? FindAnyObjectByType<ProgressionManager>();
        Subscribe();
        RefreshAll();
    }

    private void OnDisable() => Unsubscribe();
    private void OnDestroy() => Unsubscribe();

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
    }

    // ─────────────────────────────────────────────────────────────
    //  Open / Close
    // ─────────────────────────────────────────────────────────────

    public void OpenShop()
    {
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else if (shopPanel != null) shopPanel.SetActive(true);
        AnalyticsService.Track(AnalyticsEvents.ShopOpened);
        // OnEnable performs RefreshAll; no extra work needed here.
    }

    public void CloseShop()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseShop);
        else FinishCloseShop();
    }

    private void FinishCloseShop()
    {
        if (shopPanel != null) shopPanel.SetActive(false);
        OnShopClosed?.Invoke();
    }

    private void OnBackClicked() => CloseShop();

    // IUIPanel
    public void Show() => OpenShop();
    public void Hide() => CloseShop();

    // ─────────────────────────────────────────────────────────────
    //  Refresh
    // ─────────────────────────────────────────────────────────────

    public void RefreshAll()
    {
        UpdateCurrencyTexts();
        RebuildItemCards();
    }

    private void UpdateCurrencyTexts()
    {
        if (economy != null)
        {
            if (coinsText != null) coinsText.text = economy.Coins.ToString();
            if (gemsText != null) gemsText.text = economy.Gems.ToString();
        }
    }

    private void HandleCoinsChanged(int oldValue, int newValue) => UpdateCurrencyTexts();
    private void HandleGemsChanged(int oldValue, int newValue) => UpdateCurrencyTexts();
    private void HandleProgressionChanged() => UpdateCurrencyTexts();
    private void HandleItemPurchased(ShopItemDefinition item) => UpdateCardStates();

    private void RebuildItemCards()
    {
        if (itemsContainer == null || itemCardTemplate == null || shopManager == null) return;

        // Destroy generated cards (iterate backwards; skip the inactive template).
        for (int i = itemsContainer.childCount - 1; i >= 0; i--)
        {
            Transform child = itemsContainer.GetChild(i);
            if (child.gameObject == itemCardTemplate) continue;
            Destroy(child.gameObject);
        }
        spawnedCards.Clear();

        var db = shopManager.Database;
        if (db == null) return;

        foreach (var item in db.Items)
        {
            if (item == null) continue;
            var card = Instantiate(itemCardTemplate, itemsContainer);
            card.name = $"ShopCard_{item.ItemId}";
            card.SetActive(true);
            SetupCard(card, item);
            spawnedCards.Add(card);
        }
    }

    private void SetupCard(GameObject card, ShopItemDefinition item)
    {
        var nameText = FindChildText(card.transform, "NameText");
        var descText = FindChildText(card.transform, "DescText");
        var contentsText = FindChildText(card.transform, "ContentsText");
        var priceText = FindChildText(card.transform, "PriceText");
        var buyButton = card.transform.Find("BuyBtn")?.GetComponent<Button>();
        var buyLabel = buyButton != null ? buyButton.GetComponentInChildren<TMP_Text>() : null;

        if (nameText != null) nameText.text = item.DisplayName;
        if (descText != null) descText.text = item.Description;
        if (contentsText != null) contentsText.text = FormatContents(item);
        if (priceText != null) priceText.text = FormatPrice(item);

        if (buyButton != null)
        {
            buyButton.onClick.RemoveAllListeners();
            buyButton.onClick.AddListener(() => OnBuyClicked(item));
        }
        UpdateCardLabel(item, buyLabel);
    }

    private void UpdateCardStates()
    {
        if (shopManager == null || shopManager.Database == null) return;
        foreach (var card in spawnedCards)
        {
            var id = card.name.StartsWith("ShopCard_") ? card.name.Substring(9) : card.name;
            var item = shopManager.Database.GetItemById(id);
            var buyButton = card.transform.Find("BuyBtn");
            var label = buyButton != null ? buyButton.GetComponentInChildren<TMP_Text>() : null;
            UpdateCardLabel(item, label);
        }
    }

    private void UpdateCardLabel(ShopItemDefinition item, TMP_Text label)
    {
        if (item == null || label == null) return;
        bool owned = item.IsOneTimePurchase && shopManager.HasPurchasedOneTime(item.ItemId);
        label.text = owned ? "OWNED" : "BUY";
    }

    // ─────────────────────────────────────────────────────────────
    //  Purchase
    // ─────────────────────────────────────────────────────────────

    private void OnBuyClicked(ShopItemDefinition item)
    {
        if (shopManager == null || item == null) return;

        if (!shopManager.CanPurchase(item))
        {
            // insufficient_currency is only logged when the player genuinely
            // cannot afford the price — not for already-owned or other rejections.
            bool insufficient =
                (item.CostType == ShopCostType.Coins && economy != null && !economy.CanAffordCoins(item.CostAmount)) ||
                (item.CostType == ShopCostType.Gems  && economy != null && !economy.CanAffordGems(item.CostAmount));
            if (insufficient)
            {
                AnalyticsService.Track(AnalyticsEvents.ShopPurchaseFailed, new Dictionary<string, object>
                {
                    ["item_id"] = item.ItemId,
                    ["reason"]  = "insufficient_currency"
                });
            }
            ShowFeedback(RejectMessage(item));
            return;
        }

        ShowFeedback("Purchasing...");
        shopManager.PurchaseItem(item, success =>
        {
            ShowFeedback(success
                ? $"Purchased {item.DisplayName}!"
                : $"Could not purchase {item.DisplayName}.");
            UpdateCurrencyTexts();
            UpdateCardStates();
        });
    }

    private string RejectMessage(ShopItemDefinition item)
    {
        if (item.IsOneTimePurchase && shopManager.HasPurchasedOneTime(item.ItemId))
            return "Already owned.";

        switch (item.CostType)
        {
            case ShopCostType.Coins: return "Not enough Coins";
            case ShopCostType.Gems: return "Not enough Gems";
            case ShopCostType.RealMoney: return "Item unavailable.";
            default: return "Cannot purchase.";
        }
    }

    private void ShowFeedback(string message)
    {
        if (feedbackText != null) feedbackText.text = message;
    }

    // ─────────────────────────────────────────────────────────────
    //  Formatting helpers
    // ─────────────────────────────────────────────────────────────

    private string FormatPrice(ShopItemDefinition item)
    {
        switch (item.CostType)
        {
            case ShopCostType.Coins: return $"{item.CostAmount} Coins";
            case ShopCostType.Gems: return $"{item.CostAmount} Gems";
            case ShopCostType.RealMoney:
                string price = iapManager != null ? iapManager.GetPriceString(item.RealMoneyProductId) : null;
                return string.IsNullOrEmpty(price) ? "IAP" : price;
            default: return "-";
        }
    }

    private static string FormatContents(ShopItemDefinition item)
    {
        if (item.Rewards == null || item.Rewards.Count == 0) return "";
        var parts = new List<string>();
        foreach (var r in item.Rewards)
            parts.Add(string.IsNullOrEmpty(r.ItemId) ? $"{r.Amount} {r.Type}" : $"{r.Amount}x {r.ItemId}");
        return string.Join(", ", parts);
    }

    private static TMP_Text FindChildText(Transform root, string childName)
    {
        var t = root.Find(childName);
        return t != null ? t.GetComponent<TMP_Text>() : null;
    }
}

