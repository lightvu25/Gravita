using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

/// <summary>
/// UI widget representing a single Shop Item Card.
/// Supports GDD v1.0 states:
///   - Owned PlayerSkin Cosmetics: interactive "EQUIP" / "EQUIPPED" toggle.
///   - Skill Charge Cap reached (12/12/8): "MAX CAP" disabled state.
///   - One-time bundles / Remove Ads: "OWNED" disabled state.
///   - Gem Packs: "Base + Bonus Gems" breakdown.
/// </summary>
public class ShopItemCardWidget : MonoBehaviour
{
    [Header("Visuals & Icon")]
    [Tooltip("Image component that displays this shop item's unique Sprite icon.")]
    [FormerlySerializedAs("icon")]
    [SerializeField] private Image iconImage;

    [Tooltip("Optional Image component for displaying the cost currency icon (Coins/Gems).")]
    [SerializeField] private Image costCurrencyIcon;

    [Header("Text Elements")]
    [Tooltip("Displays the shop item's name.")]
    [SerializeField] private TMP_Text nameText;

    [Tooltip("Displays the shop item's description (or reward contents if description is empty).")]
    [SerializeField] private TMP_Text descriptionText;

    [Tooltip("Optional separate text for reward contents.")]
    [SerializeField] private TMP_Text contentsText;

    [Tooltip("Displays the item's cost text (e.g. 'FREE', '100', 'EQUIP', 'MAX CAP', 'OWNED') on or near the buy button.")]
    [FormerlySerializedAs("priceText")]
    [FormerlySerializedAs("buyLabel")]
    [SerializeField] private TMP_Text costText;

    [Header("Interaction")]
    [Tooltip("Button clicked to purchase, claim, or equip this shop item.")]
    [SerializeField] private Button buyButton;

    private ShopItemDefinition boundItem;
    private Action<ShopItemDefinition> onBuyCallback;
    private bool buttonBound;

    public ShopItemDefinition Item => boundItem;
    public Image IconImage => iconImage;
    public TMP_Text CostText => costText;

    private void Awake()
    {
        if (buyButton == null)
            buyButton = GetComponent<Button>();
    }

    private void OnDestroy()
    {
        if (buttonBound && buyButton != null)
        {
            buyButton.onClick.RemoveListener(HandleBuyClicked);
            buttonBound = false;
        }
    }

    public void Setup(
        ShopItemDefinition item,
        Action<ShopItemDefinition> onBuyClicked,
        IAPManager iapManager = null,
        ShopManager shopManager = null)
    {
        if (buyButton == null)
            buyButton = GetComponent<Button>();

        boundItem = item;
        onBuyCallback = onBuyClicked;

        if (buyButton != null && !buttonBound)
        {
            buyButton.onClick.AddListener(HandleBuyClicked);
            buttonBound = true;
        }

        Refresh(iapManager, shopManager);
    }

    public void Refresh(IAPManager iapManager = null, ShopManager shopManager = null)
    {
        if (boundItem == null) return;

        if (iconImage != null && boundItem.Icon != null)
        {
            iconImage.sprite = boundItem.Icon;
            iconImage.enabled = true;
        }

        if (costCurrencyIcon != null)
        {
            if (boundItem.CostIcon != null)
            {
                costCurrencyIcon.sprite = boundItem.CostIcon;
                costCurrencyIcon.gameObject.SetActive(true);
            }
            else
            {
                costCurrencyIcon.gameObject.SetActive(false);
            }
        }

        if (nameText != null)
            nameText.text = boundItem.DisplayName;

        string formattedContents = FormatContents(boundItem);
        if (descriptionText != null)
        {
            descriptionText.text = !string.IsNullOrWhiteSpace(boundItem.Description)
                ? boundItem.Description
                : formattedContents;
        }

        if (contentsText != null)
            contentsText.text = formattedContents;

        EvaluateButtonDisplayState(boundItem, iapManager, shopManager, out string buttonLabel, out bool interactable);

        if (costText != null)
            costText.text = buttonLabel;

        if (buyButton != null)
            buyButton.interactable = interactable;
    }

    public static void EvaluateButtonDisplayState(
        ShopItemDefinition item,
        IAPManager iapManager,
        ShopManager shopManager,
        out string buttonLabel,
        out bool interactable)
    {
        if (item == null)
        {
            buttonLabel = "FREE";
            interactable = false;
            return;
        }

        // Owned PlayerSkin cosmetic -> allow direct Equip / Unequip toggle in Shop
        if (TryGetSingleCosmeticId(item, out string cosmeticId) && CosmeticService.IsOwned(cosmeticId))
        {
            bool isEquipped = string.Equals(CosmeticService.GetEquippedPlayerSkinId(), cosmeticId, StringComparison.Ordinal);
            buttonLabel = isEquipped ? "EQUIPPED" : "EQUIP";
            interactable = true;
            return;
        }

        if (shopManager != null && !shopManager.EvaluatePurchase(item, out ShopPurchaseBlockReason blockReason))
        {
            if (blockReason == ShopPurchaseBlockReason.AlreadyOwned)
            {
                buttonLabel = "OWNED";
                interactable = false;
                return;
            }

            if (blockReason == ShopPurchaseBlockReason.SkillCapExceeded)
            {
                buttonLabel = "MAX CAP";
                interactable = false;
                return;
            }
        }

        bool legacyOwned = item.IsOneTimePurchase && shopManager != null && shopManager.HasPurchasedOneTime(item.ItemId);
        buttonLabel = legacyOwned ? "OWNED" : FormatCost(item, iapManager);
        interactable = !legacyOwned;
    }

    public static bool TryGetSingleCosmeticId(ShopItemDefinition item, out string cosmeticId)
    {
        cosmeticId = string.Empty;
        if (item == null || item.Rewards == null)
            return false;

        string found = string.Empty;
        for (int i = 0; i < item.Rewards.Count; i++)
        {
            RewardItem r = item.Rewards[i];
            if (r.Type == RewardType.Cosmetic && !string.IsNullOrEmpty(r.ItemId))
            {
                if (!string.IsNullOrEmpty(found))
                    return false; // Multi-cosmetic bundle
                found = r.ItemId;
            }
            else if (r.Amount > 0)
            {
                return false; // Mixed bundle (e.g., Starter Pack)
            }
        }

        cosmeticId = found;
        return !string.IsNullOrEmpty(cosmeticId);
    }

    public static string FormatCost(ShopItemDefinition item, IAPManager iapManager = null)
    {
        if (item == null) return "FREE";
        return item.GetFormattedCostText(iapManager);
    }

    public static string FormatContents(ShopItemDefinition item)
    {
        if (item == null) return string.Empty;

        if (item.Category == ShopItemCategory.GemPack && item.TotalPackGems > 0)
        {
            return item.BonusGemsAmount > 0
                ? $"{item.BaseGemsAmount} + {item.BonusGemsAmount} Bonus Gems"
                : $"{item.TotalPackGems} Gems";
        }

        if (item.Rewards == null || item.Rewards.Count == 0)
        {
            return item.GrantsRemoveInterstitialAds ? "No Interstitial Ads" : string.Empty;
        }

        var parts = new List<string>(item.Rewards.Count + 1);
        for (int i = 0; i < item.Rewards.Count; i++)
        {
            var r = item.Rewards[i];
            parts.Add(string.IsNullOrEmpty(r.ItemId) ? $"{r.Amount} {r.Type}" : $"{r.Amount}x {r.ItemId}");
        }

        if (item.GrantsRemoveInterstitialAds)
            parts.Add("No Interstitial Ads");

        return string.Join(", ", parts);
    }

    private void HandleBuyClicked()
    {
        if (boundItem == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);
        onBuyCallback?.Invoke(boundItem);
    }
}
