using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for shop transactions across Coins, Gems, and Real Money IAP.
/// Strictly guarantees non-negative currency balances, atomic deduction,
/// and entitlement delivery via RewardService.
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    [SerializeField] private ShopDatabase database;

    public event Action<ShopItemDefinition> OnItemPurchased;

    private readonly HashSet<string> purchasedOneTimeItemIDs = new HashSet<string>();

    public ShopDatabase Database => database;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        LoadFromProfile();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public bool HasPurchasedOneTime(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        return purchasedOneTimeItemIDs.Contains(itemId);
    }

    /// <summary>
    /// Evaluates whether the player is currently eligible to purchase the specified item.
    /// </summary>
    public bool CanPurchase(ShopItemDefinition item)
    {
        if (item == null) return false;

        // One-time purchase check
        if (item.IsOneTimePurchase && HasPurchasedOneTime(item.ItemId))
            return false;

        var eco = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();

        switch (item.CostType)
        {
            case ShopCostType.Coins:
                return eco != null && eco.CanAffordCoins(item.CostAmount);

            case ShopCostType.Gems:
                return eco != null && eco.CanAffordGems(item.CostAmount);

            case ShopCostType.RealMoney:
                var iap = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
                if (item.IsOneTimePurchase && iap != null && iap.HasPurchased(item.RealMoneyProductId))
                    return false;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Executes a purchase transaction. Deducts currency or prompts IAP, then grants rewards via RewardService.
    /// </summary>
    public void PurchaseItem(ShopItemDefinition item, Action<bool> onComplete = null)
    {
        if (!CanPurchase(item))
        {
            Debug.LogWarning($"[ShopManager] Purchase rejected for '{item?.DisplayName}': Player cannot afford or already owns.");
            onComplete?.Invoke(false);
            return;
        }

        var eco = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
        var rewardService = RewardService.Instance ?? FindAnyObjectByType<RewardService>();

        switch (item.CostType)
        {
            case ShopCostType.Coins:
                if (eco == null || !eco.SpendCoins(item.CostAmount, $"ShopPurchase_{item.ItemId}"))
                {
                    onComplete?.Invoke(false);
                    return;
                }
                FinalizePurchase(item);
                onComplete?.Invoke(true);
                break;

            case ShopCostType.Gems:
                if (eco == null || !eco.SpendGems(item.CostAmount, $"ShopPurchase_{item.ItemId}"))
                {
                    onComplete?.Invoke(false);
                    return;
                }
                FinalizePurchase(item);
                onComplete?.Invoke(true);
                break;

            case ShopCostType.RealMoney:
                var iap = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
                if (iap == null)
                {
                    Debug.LogError("[ShopManager] IAPManager unavailable for real-money purchase.");
                    onComplete?.Invoke(false);
                    return;
                }

                iap.Purchase(item.RealMoneyProductId, success =>
                {
                    if (success)
                    {
                        FinalizePurchase(item);
                    }
                    onComplete?.Invoke(success);
                });
                break;
        }
    }

    private void FinalizePurchase(ShopItemDefinition item)
    {
        var rewardService = RewardService.Instance ?? FindAnyObjectByType<RewardService>();
        if (rewardService != null)
        {
            rewardService.GrantRewardBundle(item.CreateRewardBundle(), $"Shop_{item.ItemId}");
        }

        if (item.IsOneTimePurchase)
        {
            purchasedOneTimeItemIDs.Add(item.ItemId);
            SaveToProfile();
        }

        OnItemPurchased?.Invoke(item);
        Debug.Log($"[ShopManager] Completed purchase for '{item.DisplayName}'.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence Integration
    // ─────────────────────────────────────────────────────────────

    public void LoadFromProfile()
    {
        purchasedOneTimeItemIDs.Clear();
        ProfileData profile = SaveManager.loadProfile();
        if (profile != null && profile.purchasedShopItemIDs != null)
        {
            foreach (var id in profile.purchasedShopItemIDs)
            {
                if (!string.IsNullOrEmpty(id))
                    purchasedOneTimeItemIDs.Add(id);
            }
        }
    }

    private void SaveToProfile()
    {
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        if (profile.purchasedShopItemIDs == null)
            profile.purchasedShopItemIDs = new List<string>();

        profile.purchasedShopItemIDs.Clear();
        profile.purchasedShopItemIDs.AddRange(purchasedOneTimeItemIDs);

        SaveManager.saveProfile(profile);
    }
}

