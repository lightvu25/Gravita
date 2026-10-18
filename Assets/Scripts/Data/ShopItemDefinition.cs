using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject defining an item, pack, or bundle sold in the shop.
/// Can be priced in Coins, Gems, or Real Money.
/// </summary>
[CreateAssetMenu(fileName = "NewShopItem", menuName = "Game/Shop/Item Definition")]
public class ShopItemDefinition : ScriptableObject
{
    [Header("Identity")]
    [SerializeField] private string itemId;
    [SerializeField] private string displayName;
    [TextArea(2, 4)]
    [SerializeField] private string description;
    [SerializeField] private Sprite icon;

    [Header("Pricing")]
    [SerializeField] private ShopCostType costType = ShopCostType.Coins;
    [Tooltip("Price in Coins or Gems.")]
    [SerializeField] private int costAmount = 100;
    [Tooltip("Store product ID for real money purchases (e.g. 'starter_pack').")]
    [SerializeField] private string realMoneyProductId;

    [Header("Rules")]
    [Tooltip("If true, this product can only be purchased once per player profile.")]
    [SerializeField] private bool isOneTimePurchase;

    [Header("Contents")]
    [Tooltip("Items delivered upon successful transaction.")]
    [SerializeField] private List<RewardItem> rewards = new List<RewardItem>();

    public string ItemId => itemId;
    public string DisplayName => displayName;
    public string Description => description;
    public Sprite Icon => icon;

    public ShopCostType CostType => costType;
    public int CostAmount => costAmount;
    public string RealMoneyProductId => realMoneyProductId;
    public bool IsOneTimePurchase => isOneTimePurchase;

    public IReadOnlyList<RewardItem> Rewards => rewards;

    /// <summary>
    /// Converts the rewards list into an atomic RewardBundle.
    /// </summary>
    public RewardBundle CreateRewardBundle()
    {
        return new RewardBundle(rewards);
    }
}

