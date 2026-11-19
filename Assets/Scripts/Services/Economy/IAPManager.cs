using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for In-App Purchases (Gravita Economy GDD §9 & §12).
/// Guarantees:
///   - Verified store receipt/order required before granting any Gem Pack, Starter Pack, or Remove Ads.
///   - Idempotent deduplication by store <c>TransactionId</c> (<see cref="ProfileData.processedIapTransactionIds"/>).
///   - One-time purchase blocking for Starter Pack, Remove Ads, and Non-Consumables.
///   - Safe cross-device <see cref="RestorePurchases"/> that restores permanent entitlements (Remove Ads, PlayerSkin)
///     without duplicating consumable Gems/Coins.
/// </summary>
public class IAPManager : MonoBehaviour
{
    public static IAPManager Instance { get; private set; }

    private IIAPService iapService;

    public event Action<string> OnPurchaseSucceeded;
    public event Action<string, string> OnPurchaseFailed;
    public event Action OnPurchasesRestored;

    // Static analytics events (GDD §13)
    public static event Action<string> OnIapPurchaseInitiated;
    public static event Action<string, string> OnIapPurchaseCompleted;
    public static event Action<string, string> OnIapPurchaseFailedEvent;
    public static event Action<bool, int> OnIapRestoreCompleted;

    private readonly HashSet<string> purchasedProducts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private bool noAdsPurchased;

    public bool NoAdsPurchased
    {
        get
        {
            LoadFromProfile();
            return noAdsPurchased;
        }
    }

    public IReadOnlyCollection<string> PurchasedProductIDs => purchasedProducts;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        iapService = ResolveOrCreateDefaultService();
        LoadFromProfile();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetIAPService(IIAPService service)
    {
        iapService = service;
    }

    public bool HasPurchased(string productId)
    {
        if (string.IsNullOrEmpty(productId))
            return false;

        LoadFromProfile();
        if (IsRemoveAdsProductId(productId) && noAdsPurchased)
            return true;

        return purchasedProducts.Contains(productId);
    }

    public string GetPriceString(string productId)
    {
        if (iapService == null)
            iapService = ResolveOrCreateDefaultService();

        string price = iapService != null ? iapService.GetLocalizedPrice(productId) : string.Empty;
        if (!string.IsNullOrEmpty(price))
            return price;

        return Application.isEditor ? "$0.99" : "--";
    }

    /// <summary>
    /// Purchases a <see cref="ShopItemDefinition"/> configured with <see cref="ShopCostType.RealMoney"/>.
    /// </summary>
    public void PurchaseShopItem(ShopItemDefinition item, Action<bool> onComplete = null)
    {
        if (item == null || string.IsNullOrEmpty(item.RealMoneyProductId))
        {
            Debug.LogError("[IAPManager] Cannot purchase: Invalid RealMoney ShopItemDefinition.");
            onComplete?.Invoke(false);
            return;
        }

        ExecutePurchaseInternal(item.RealMoneyProductId, item, onComplete);
    }

    private static ShopManager ResolveShopManager()
    {
        return ShopManager.Instance != null ? ShopManager.Instance : FindAnyObjectByType<ShopManager>();
    }

    /// <summary>
    /// Initiates a purchase flow by store product ID. Rewards and entitlements are granted ONLY upon verified store success.
    /// </summary>
    public void Purchase(string productId, Action<bool> onComplete = null)
    {
        ShopManager shopMgr = ResolveShopManager();
        ShopItemDefinition matchedItem = shopMgr != null
            ? shopMgr.FindByRealMoneyProductId(productId)
            : null;

        ExecutePurchaseInternal(productId, matchedItem, onComplete);
    }

    private void ExecutePurchaseInternal(string productId, ShopItemDefinition explicitItem, Action<bool> onComplete)
    {
        if (string.IsNullOrEmpty(productId))
        {
            Debug.LogError("[IAPManager] Cannot purchase: Empty product ID.");
            onComplete?.Invoke(false);
            return;
        }

        // Enforce one-time purchase guard BEFORE invoking the billing dialog (GDD §9.2)
        bool isOneTime = (explicitItem != null && explicitItem.IsOneTimePurchase) ||
                         IsRemoveAdsProductId(productId) ||
                         string.Equals(productId, "starter_pack", StringComparison.OrdinalIgnoreCase);

        ShopManager shopMgr = ResolveShopManager();
        if (isOneTime && (HasPurchased(productId) || (explicitItem != null && shopMgr != null && shopMgr.HasPurchasedOneTime(explicitItem.ItemId))))
        {
            const string alreadyOwnedMsg = "One-time IAP product is already owned.";
            Debug.LogWarning($"[IAPManager] Blocked duplicate purchase of '{productId}': {alreadyOwnedMsg}");
            OnPurchaseFailed?.Invoke(productId, alreadyOwnedMsg);
            OnIapPurchaseFailedEvent?.Invoke(productId, alreadyOwnedMsg);
            onComplete?.Invoke(false);
            return;
        }

        if (iapService == null)
            iapService = ResolveOrCreateDefaultService();

        if (iapService == null || !iapService.IsInitialized)
        {
            const string uninitMsg = "IAP service not initialized.";
            Debug.LogError($"[IAPManager] Cannot purchase '{productId}': {uninitMsg}");
            OnPurchaseFailed?.Invoke(productId, uninitMsg);
            OnIapPurchaseFailedEvent?.Invoke(productId, uninitMsg);
            onComplete?.Invoke(false);
            return;
        }

        OnIapPurchaseInitiated?.Invoke(productId);

        iapService.PurchaseProduct(productId, result =>
        {
            if (!result.IsSuccess)
            {
                string err = !string.IsNullOrEmpty(result.ErrorMessage) ? result.ErrorMessage : result.Status.ToString();
                Debug.LogWarning($"[IAPManager] Purchase failed or cancelled for '{productId}': {err}");
                OnPurchaseFailed?.Invoke(productId, err);
                OnIapPurchaseFailedEvent?.Invoke(productId, err);
                onComplete?.Invoke(false);
                return;
            }

            // UnityPurchasingService already ran ProcessVerifiedStoreOrderStatic inside HandlePurchasePending BEFORE ConfirmPurchase.
            // Calling ProcessVerifiedStoreOrder here is 100% idempotent:
            //   - For MockIAPService / custom IIAPService: commits the RewardTransaction once.
            //   - For UnityPurchasingService: sees the already-saved TransactionId and does NOT double-grant!
            bool alreadyProcessedByPendingHook = iapService is UnityPurchasingService &&
                                                 !string.IsNullOrEmpty(result.TransactionId) &&
                                                 (SaveManager.loadProfile()?.HasProcessedIapTransaction(result.TransactionId) ?? false);

            if (!alreadyProcessedByPendingHook)
            {
                bool saved = ProcessVerifiedStoreOrder(productId, result.TransactionId, explicitItem, out bool wasDuplicate);
                if (!saved || wasDuplicate)
                {
                    string dupErr = wasDuplicate ? "Duplicate store TransactionId ignored." : "Failed to persist IAP entitlement.";
                    Debug.LogWarning($"[IAPManager] Rejected order completion for '{productId}' (Tx: {result.TransactionId}): {dupErr}");
                    OnPurchaseFailed?.Invoke(productId, dupErr);
                    OnIapPurchaseFailedEvent?.Invoke(productId, dupErr);
                    onComplete?.Invoke(false);
                    return;
                }
            }

            LoadFromProfile();
            OnPurchaseSucceeded?.Invoke(productId);
            OnIapPurchaseCompleted?.Invoke(productId, result.TransactionId);
            Debug.Log($"[IAPManager] Completed verified IAP purchase for '{productId}' (Tx: {result.TransactionId}).");
            onComplete?.Invoke(true);
        });
    }

    /// <summary>
    /// Static entry point used by <see cref="UnityPurchasingService.HandlePurchasePending"/> BEFORE <c>ConfirmPurchase</c>.
    /// Returns true if the order is durably persisted on disk (either newly committed now or already processed earlier).
    /// </summary>
    public static bool ProcessVerifiedStoreOrderStatic(string productId, string transactionId, out bool wasAlreadyProcessed)
    {
        ShopManager shopMgr = ResolveShopManager();
        ShopItemDefinition matchedItem = shopMgr != null
            ? shopMgr.FindByRealMoneyProductId(productId)
            : null;

        if (Instance != null)
            return Instance.ProcessVerifiedStoreOrder(productId, transactionId, matchedItem, out wasAlreadyProcessed);

        return ExecuteVerifiedOrderCommit(productId, transactionId, matchedItem, out wasAlreadyProcessed);
    }

    public bool ProcessVerifiedStoreOrder(
        string productId,
        string transactionId,
        ShopItemDefinition explicitItem,
        out bool wasAlreadyProcessed)
    {
        bool ok = ExecuteVerifiedOrderCommit(productId, transactionId, explicitItem, out wasAlreadyProcessed);
        if (ok)
            LoadFromProfile();
        return ok;
    }

    private static bool ExecuteVerifiedOrderCommit(
        string productId,
        string transactionId,
        ShopItemDefinition matchedItem,
        out bool wasAlreadyProcessed)
    {
        wasAlreadyProcessed = false;
        if (string.IsNullOrEmpty(productId))
            return false;

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
            return false;

        profile.MigrateIfNeeded();

        if (!string.IsNullOrEmpty(transactionId) && profile.HasProcessedIapTransaction(transactionId))
        {
            wasAlreadyProcessed = true;
            return true;
        }

        if (matchedItem == null)
        {
            ShopManager shopMgr = ResolveShopManager();
            if (shopMgr != null)
                matchedItem = shopMgr.FindByRealMoneyProductId(productId);
        }

        bool isNonConsumable = IsNonConsumableOrOneTime(productId, matchedItem);
        bool grantsNoAds = (matchedItem != null && matchedItem.GrantsRemoveInterstitialAds) || IsRemoveAdsProductId(productId);

        RewardBundle payout = matchedItem != null ? matchedItem.CreateRewardBundle() : new RewardBundle();

        var tx = new RewardTransaction
        {
            RewardId = !string.IsNullOrEmpty(transactionId) ? EconomyRewardIds.IapTransaction(transactionId) : string.Empty,
            RequireUniqueRewardId = !string.IsNullOrEmpty(transactionId),
            IapTransactionId = transactionId ?? string.Empty,
            Payout = payout,
            RejectIfSkillCapExceeded = false, // Clamp safely so a verified real-money purchase is never rejected
            RejectIfCosmeticAlreadyOwned = false,
            OneTimeShopItemId = (matchedItem != null && matchedItem.IsOneTimePurchase) ? matchedItem.ItemId : string.Empty,
            NonConsumableProductId = isNonConsumable ? productId : string.Empty,
            GrantsRemoveInterstitialAds = grantsNoAds,
            Reason = $"IAPPurchase_{productId}",
            OnBeforeSaveMutateProfile = p =>
            {
                p.hasMadeAnyIapInCampaign = true;
            }
        };

        RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
        return result.Succeeded;
    }

    /// <summary>
    /// Restores previous non-consumable / permanent purchases from the platform store (GDD §9.3).
    /// Restores Remove Ads, one-time ownership flags, and permanent Cosmetics WITHOUT re-granting consumable Gems/Coins.
    /// </summary>
    public void RestorePurchases(Action<bool> onComplete = null)
    {
        RestorePurchases((success, _) => onComplete?.Invoke(success));
    }

    public void RestorePurchases(Action<bool, int> onComplete)
    {
        if (iapService == null)
            iapService = ResolveOrCreateDefaultService();

        if (iapService == null)
        {
            OnIapRestoreCompleted?.Invoke(false, 0);
            onComplete?.Invoke(false, 0);
            return;
        }

        iapService.RestorePurchases((success, msg) =>
        {
            int restoredCount = 0;
            if (success)
            {
                restoredCount = ReconcileOwnedNonConsumables(DiscoverOwnedProductIdsFromService());
                LoadFromProfile();
                OnPurchasesRestored?.Invoke();
            }

            OnIapRestoreCompleted?.Invoke(success, restoredCount);
            onComplete?.Invoke(success, restoredCount);
        });
    }

    public int ReconcileOwnedNonConsumables(IEnumerable<string> ownedProductIds)
    {
        if (ownedProductIds == null)
            return 0;

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
            return 0;

        profile.MigrateIfNeeded();

        var cosmeticsToRestore = new RewardBundle();
        bool grantsNoAds = false;
        var productIdsToRecord = new List<string>();
        var shopItemIdsToRecord = new List<string>();

        ShopManager shopMgr = ResolveShopManager();
        foreach (string rawProductId in ownedProductIds)
        {
            if (string.IsNullOrEmpty(rawProductId))
                continue;

            ShopItemDefinition item = shopMgr != null
                ? shopMgr.FindByRealMoneyProductId(rawProductId)
                : null;

            if (!IsNonConsumableOrOneTime(rawProductId, item))
                continue;

            bool newlyRestoredOnThisProfile = !profile.purchasedProductIDs.Contains(rawProductId) ||
                                              (IsRemoveAdsProductId(rawProductId) && !profile.noAdsPurchased);

            if (newlyRestoredOnThisProfile)
                productIdsToRecord.Add(rawProductId);

            if (item != null && item.IsOneTimePurchase && !profile.purchasedShopItemIDs.Contains(item.ItemId))
                shopItemIdsToRecord.Add(item.ItemId);

            if ((item != null && item.GrantsRemoveInterstitialAds) || IsRemoveAdsProductId(rawProductId))
                grantsNoAds = true;

            // Restore permanent Cosmetic entitlements inside non-consumable bundles without re-crediting consumable Gems/Coins
            if (item != null && item.Rewards != null)
            {
                for (int i = 0; i < item.Rewards.Count; i++)
                {
                    RewardItem rw = item.Rewards[i];
                    if (rw.Type == RewardType.Cosmetic && !string.IsNullOrEmpty(rw.ItemId) && !CosmeticService.IsOwned(rw.ItemId, profile))
                    {
                        cosmeticsToRestore.Add(RewardType.Cosmetic, 1, rw.ItemId);
                    }
                }
            }
        }

        bool needsCommit = productIdsToRecord.Count > 0 ||
                           shopItemIdsToRecord.Count > 0 ||
                           (grantsNoAds && !profile.noAdsPurchased) ||
                           !cosmeticsToRestore.IsEmpty;

        if (!needsCommit)
            return 0;

        var tx = new RewardTransaction
        {
            Payout = cosmeticsToRestore,
            GrantsRemoveInterstitialAds = grantsNoAds,
            RejectIfSkillCapExceeded = false,
            RejectIfCosmeticAlreadyOwned = false,
            Reason = "IAPRestorePurchases",
            OnBeforeSaveMutateProfile = p =>
            {
                p.hasMadeAnyIapInCampaign = true;
                for (int i = 0; i < productIdsToRecord.Count; i++)
                {
                    if (!p.purchasedProductIDs.Contains(productIdsToRecord[i]))
                        p.purchasedProductIDs.Add(productIdsToRecord[i]);
                }
                for (int i = 0; i < shopItemIdsToRecord.Count; i++)
                {
                    if (!p.purchasedShopItemIDs.Contains(shopItemIdsToRecord[i]))
                        p.purchasedShopItemIDs.Add(shopItemIdsToRecord[i]);
                }
            }
        };

        RewardTransactionResult txResult = RewardService.ExecuteTransaction(tx);
        if (!txResult.Succeeded)
            return 0;

        LoadFromProfile();
        return Mathf.Max(1, productIdsToRecord.Count);
    }

    private IEnumerable<string> DiscoverOwnedProductIdsFromService()
    {
        if (iapService is MockIAPService mock)
            return mock.GetOwnedStoreProductIds();

        if (iapService is UnityPurchasingService live)
            return live.GetOwnedStoreProductIds();

        var discovered = new List<string>();
        string[] builtInCandidates = { "no_ads", "remove_ads", "starter_pack" };
        for (int i = 0; i < builtInCandidates.Length; i++)
        {
            if (iapService != null && iapService.IsProductPurchased(builtInCandidates[i]))
                discovered.Add(builtInCandidates[i]);
        }

        ShopManager shopMgr = ResolveShopManager();
        ShopDatabase db = shopMgr != null ? shopMgr.Database : null;
        if (db?.Items != null && iapService != null)
        {
            for (int i = 0; i < db.Items.Count; i++)
            {
                ShopItemDefinition item = db.Items[i];
                if (item != null && !string.IsNullOrEmpty(item.RealMoneyProductId) && iapService.IsProductPurchased(item.RealMoneyProductId))
                    discovered.Add(item.RealMoneyProductId);
            }
        }

        return discovered;
    }

    private static bool IsRemoveAdsProductId(string productId)
    {
        return string.Equals(productId, "no_ads", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(productId, "remove_ads", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsNonConsumableOrOneTime(string productId, ShopItemDefinition item)
    {
        if (item != null)
            return item.IapProductType == IAPProductTypeKind.NonConsumable || item.IsOneTimePurchase;

        return IsRemoveAdsProductId(productId) ||
               string.Equals(productId, "starter_pack", StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence Integration
    // ─────────────────────────────────────────────────────────────

    public void LoadFromProfile()
    {
        purchasedProducts.Clear();
        noAdsPurchased = false;

        ProfileData profile = SaveManager.loadProfile();
        if (profile != null)
        {
            noAdsPurchased = profile.noAdsPurchased;
            if (profile.purchasedProductIDs != null)
            {
                foreach (var id in profile.purchasedProductIDs)
                {
                    if (!string.IsNullOrEmpty(id))
                        purchasedProducts.Add(id);
                }
            }
        }
    }

    private IIAPService ResolveOrCreateDefaultService()
    {
        IIAPService attached = GetComponent<IIAPService>();
        if (attached != null)
            return attached;

        if (!Application.isEditor)
        {
            UnityPurchasingService live = FindAnyObjectByType<UnityPurchasingService>();
            if (live == null)
                live = gameObject.AddComponent<UnityPurchasingService>();
            return live;
        }

        MockIAPService mock = FindAnyObjectByType<MockIAPService>();
        if (mock == null)
            mock = gameObject.AddComponent<MockIAPService>();
        return mock;
    }
}
