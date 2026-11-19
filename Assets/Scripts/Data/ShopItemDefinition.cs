using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// High-level Shop catalog category defined in Gravita Economy GDD v1.0 (§5.3, §6, §9).
/// </summary>
public enum ShopItemCategory
{
    SkillChargeSingle = 0,
    SkillChargeBundle = 1,
    Cosmetic = 2,
    GemPack = 3,
    StarterPack = 4,
    RemoveAds = 5,
    Bundle = 6
}

/// <summary>
/// Shop UI tab / section classification: Currency, Cosmetic, Power, Pack, Offer (plus All for unfiltered display).
/// </summary>
public enum ShopTabCategory
{
    All = 0,
    Currency = 1,
    Cosmetic = 2,
    Power = 3,
    Pack = 4,
    Offer = 5
}

/// <summary>
/// Store product classification for Real-Money IAP definitions (GDD §9).
/// </summary>
public enum IAPProductTypeKind
{
    Consumable = 0,
    NonConsumable = 1
}

/// <summary>
/// ScriptableObject defining an item, pack, or bundle sold in the shop.
/// Can be priced as Free, Coins, Gems, or Real Money.
/// </summary>
[CreateAssetMenu(fileName = "NewShopItem", menuName = "Game/Shop/Item Definition")]
public class ShopItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string itemId;
    [SerializeField] private string displayName;
    [TextArea(2, 4)]
    [SerializeField] private string description;
    [Tooltip("Unique icon Sprite displayed on this shop item's card.")]
    [SerializeField] private Sprite icon;
    [SerializeField] private ShopItemCategory category = ShopItemCategory.SkillChargeSingle;
    [Tooltip("Shop UI tab/category section (Currency, Cosmetic, Power, Pack, Offer). If set to All, resolves automatically from Category.")]
    [SerializeField] private ShopTabCategory shopTab = ShopTabCategory.All;

    [Header("Pricing")]
    [SerializeField] private ShopCostType costType = ShopCostType.Coins;
    [Tooltip("Price in Coins or Gems (0 = Free).")]
    [Min(0)]
    [SerializeField] private int costAmount = 100;
    [Tooltip("Optional custom cost label override (e.g. 'FREE', '0-100', '$1.99'). Leave empty to auto-format from CostType & CostAmount.")]
    [SerializeField] private string customCostText;
    [Tooltip("Optional currency icon Sprite shown on the buy/cost area.")]
    [SerializeField] private Sprite costIcon;

    [Header("IAP Store Configuration (RealMoney Only)")]
    [Tooltip("Cross-platform catalog ID for real money purchases (e.g. 'starter_pack', 'gem_pack_tier_1', 'remove_ads').")]
    [SerializeField] private string realMoneyProductId;
    [SerializeField] private IAPProductTypeKind iapProductType = IAPProductTypeKind.Consumable;
    [Tooltip("Optional Google Play specific product ID override. Uses realMoneyProductId if empty.")]
    [SerializeField] private string storeProductIdAndroid;
    [Tooltip("Optional Apple App Store specific product ID override. Uses realMoneyProductId if empty.")]
    [SerializeField] private string storeProductIdIOS;

    [Header("Gem Pack Breakdown (Optional — GDD §9.1)")]
    [Min(0)]
    [SerializeField] private int baseGemsAmount;
    [Min(0)]
    [SerializeField] private int bonusGemsAmount;

    [Header("Rules")]
    [Tooltip("If true, this product can only be purchased once per player profile.")]
    [SerializeField] private bool isOneTimePurchase;
    [Tooltip("If true, purchasing or restoring this item permanently disables Interstitial Ads (GDD §8.5 & §9.2).")]
    [SerializeField] private bool grantsRemoveInterstitialAds;

    [Header("Contents")]
    [Tooltip("Items delivered upon successful transaction.")]
    [SerializeField] private List<RewardItem> rewards = new List<RewardItem>();

    [Header("Random Loot Pack & Pity (Optional — For Shop Pack Tab)")]
    [Tooltip("Optional random LootPackDefinition with 100% base drop rates and soft/hard pity rules.")]
    [SerializeField] private LootPackDefinition lootPack;
    [Tooltip("Number of rolls executed on lootPack per purchase.")]
    [Min(1)]
    [SerializeField] private int lootRollsPerPurchase = 1;

    public string ItemId => string.IsNullOrEmpty(itemId) ? name : itemId;
    public LootPackDefinition LootPack => lootPack;
    public int LootRollsPerPurchase => Mathf.Max(1, lootRollsPerPurchase);
    public bool HasLootPack => lootPack != null && lootPack.Entries != null && lootPack.Entries.Count > 0;

    public void SetLootPack(LootPackDefinition pack, int rolls = 1)
    {
        lootPack = pack;
        lootRollsPerPurchase = Mathf.Max(1, rolls);
    }
    public string DisplayName => string.IsNullOrEmpty(displayName) ? name : displayName;
    public string Description => description;
    public Sprite Icon => icon;
    public ShopItemCategory Category => category;
    public ShopTabCategory ShopTab => shopTab != ShopTabCategory.All ? shopTab : GetDefaultTabForCategory(category);

    public static ShopTabCategory GetDefaultTabForCategory(ShopItemCategory itemCategory)
    {
        switch (itemCategory)
        {
            case ShopItemCategory.GemPack:
                return ShopTabCategory.Currency;
            case ShopItemCategory.Cosmetic:
                return ShopTabCategory.Cosmetic;
            case ShopItemCategory.SkillChargeSingle:
                return ShopTabCategory.Power;
            case ShopItemCategory.SkillChargeBundle:
            case ShopItemCategory.Bundle:
                return ShopTabCategory.Pack;
            case ShopItemCategory.StarterPack:
            case ShopItemCategory.RemoveAds:
                return ShopTabCategory.Offer;
            default:
                return ShopTabCategory.Power;
        }
    }

    public void SetShopTab(ShopTabCategory tab)
    {
        shopTab = tab;
    }

    private void OnValidate()
    {
        costAmount = Mathf.Max(0, costAmount);
        baseGemsAmount = Mathf.Max(0, baseGemsAmount);
        bonusGemsAmount = Mathf.Max(0, bonusGemsAmount);
        if (shopTab == ShopTabCategory.All)
        {
            shopTab = GetDefaultTabForCategory(category);
        }
    }

    public ShopCostType CostType => costType;
    public int CostAmount => Mathf.Max(0, costAmount);
    public string CustomCostText => customCostText;
    public Sprite CostIcon => costIcon;

    public string RealMoneyProductId => realMoneyProductId;
    public IAPProductTypeKind IapProductType => iapProductType;
    public string StoreProductIdAndroid => string.IsNullOrWhiteSpace(storeProductIdAndroid) ? realMoneyProductId : storeProductIdAndroid;
    public string StoreProductIdIOS => string.IsNullOrWhiteSpace(storeProductIdIOS) ? realMoneyProductId : storeProductIdIOS;

    public int BaseGemsAmount => Mathf.Max(0, baseGemsAmount);
    public int BonusGemsAmount => Mathf.Max(0, bonusGemsAmount);
    public int TotalPackGems => BaseGemsAmount + BonusGemsAmount;

    public bool IsOneTimePurchase => isOneTimePurchase || iapProductType == IAPProductTypeKind.NonConsumable || category == ShopItemCategory.StarterPack || category == ShopItemCategory.RemoveAds;
    public bool GrantsRemoveInterstitialAds => grantsRemoveInterstitialAds || category == ShopItemCategory.RemoveAds;
    public bool IsFree => costType == ShopCostType.Free || ((costType == ShopCostType.Coins || costType == ShopCostType.Gems) && costAmount <= 0);

    public IReadOnlyList<RewardItem> Rewards => rewards;

    /// <summary>
    /// Formats the cost text for UI display (e.g. "FREE", "100", "$1.99", or localized store price).
    /// </summary>
    public string GetFormattedCostText(IAPManager iapManager = null)
    {
        if (costType == ShopCostType.RealMoney)
        {
            var resolvedIap = iapManager != null ? iapManager : IAPManager.Instance;
            string price = resolvedIap != null ? resolvedIap.GetPriceString(realMoneyProductId) : null;
            if (!string.IsNullOrEmpty(price))
                return price;
        }

        if (!string.IsNullOrWhiteSpace(customCostText))
            return customCostText;

        if (IsFree)
            return "FREE";

        switch (costType)
        {
            case ShopCostType.Coins:
                return $"{CostAmount}";
            case ShopCostType.Gems:
                return $"{CostAmount}";
            case ShopCostType.RealMoney:
                return costAmount > 0 ? $"${CostAmount}" : "IAP";
            case ShopCostType.Free:
                return "FREE";
            default:
                return $"{CostAmount}";
        }
    }

    /// <summary>
    /// Converts the rewards list (and Gem Pack Base + Bonus breakdown if configured) into an atomic RewardBundle.
    /// </summary>
    public RewardBundle CreateRewardBundle()
    {
        var bundle = new RewardBundle(rewards);
        if (category == ShopItemCategory.GemPack && TotalPackGems > 0 && !bundle.HasType(RewardType.Gems))
        {
            bundle.Add(RewardType.Gems, TotalPackGems);
        }
        return bundle;
    }

    /// <summary>
    /// Programmatic initializer for Editor setup tools and automated NUnit tests.
    /// </summary>
    public void Configure(
        string id,
        string title,
        string desc,
        ShopItemCategory itemCategory,
        ShopCostType priceType,
        int priceAmount,
        IEnumerable<RewardItem> rewardItems = null,
        string iapProductId = "",
        IAPProductTypeKind productKind = IAPProductTypeKind.Consumable,
        bool oneTime = false,
        bool removeAds = false,
        int baseGems = 0,
        int bonusGems = 0,
        string customPriceLabel = "",
        Sprite itemIcon = null,
        Sprite currencyIcon = null,
        ShopTabCategory explicitTab = ShopTabCategory.All)
    {
        itemId = id;
        displayName = title;
        description = desc;
        category = itemCategory;
        shopTab = explicitTab != ShopTabCategory.All ? explicitTab : GetDefaultTabForCategory(itemCategory);
        costType = priceType;
        costAmount = Mathf.Max(0, priceAmount);
        realMoneyProductId = iapProductId ?? string.Empty;
        iapProductType = productKind;
        isOneTimePurchase = oneTime;
        grantsRemoveInterstitialAds = removeAds;
        baseGemsAmount = Mathf.Max(0, baseGems);
        bonusGemsAmount = Mathf.Max(0, bonusGems);
        customCostText = customPriceLabel ?? string.Empty;
        if (itemIcon != null)
            icon = itemIcon;
        if (currencyIcon != null)
            costIcon = currencyIcon;

        rewards ??= new List<RewardItem>();
        rewards.Clear();
        if (rewardItems != null)
        {
            rewards.AddRange(rewardItems);
        }
    }
}
