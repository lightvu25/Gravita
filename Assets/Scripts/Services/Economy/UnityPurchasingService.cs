using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Purchasing;

/// <summary>
/// Production Unity IAP v5.4+ implementation of <see cref="IIAPService"/> using <see cref="StoreController"/>.
/// Strictly adheres to Unity IAP v5 best practices:
///   1. Subscribes to all <see cref="StoreController"/> events BEFORE calling <c>await Connect()</c>.
///   2. Fetches products defined in <see cref="ShopDatabase"/>, then calls <c>FetchPurchases()</c>.
///   3. Enforces Save-Before-Confirm in <see cref="HandlePurchasePending"/>: entitlements and
///      <c>PendingOrder.Info.TransactionID</c> are atomically saved to disk BEFORE <c>ConfirmPurchase</c> is called.
/// </summary>
public class UnityPurchasingService : MonoBehaviour, IIAPService
{
    private StoreController storeController;
    private bool isConnectingOrConnected;
    private bool isFullyInitialized;

    private readonly Dictionary<string, string> localizedPrices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> ownedStoreProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Action<IAPResult>> activePurchaseCallbacks = new Dictionary<string, Action<IAPResult>>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> lastPendingTransactionIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public bool IsInitialized => isFullyInitialized;

    private void Awake()
    {
        if (!Application.isEditor)
        {
            Initialize(null);
        }
    }

    private void OnDestroy()
    {
        UnsubscribeStoreEvents();
    }

    public void Initialize(Action<bool> onComplete)
    {
        if (isFullyInitialized)
        {
            onComplete?.Invoke(true);
            return;
        }

        if (isConnectingOrConnected)
        {
            return;
        }

        _ = ConnectAndFetchAsync(onComplete);
    }

    private async Task ConnectAndFetchAsync(Action<bool> onComplete)
    {
        isConnectingOrConnected = true;
        try
        {
            storeController = UnityIAPServices.StoreController();

            // Subscribe to ALL events BEFORE calling Connect() (Unity IAP v5 mandatory contract)
            UnsubscribeStoreEvents();
            storeController.OnStoreConnected += HandleStoreConnected;
            storeController.OnStoreDisconnected += HandleStoreDisconnected;
            storeController.OnAuthAccountChanged += HandleAuthAccountChanged;
            storeController.OnProductsFetched += HandleProductsFetched;
            storeController.OnProductsFetchFailed += HandleProductsFetchFailed;
            storeController.OnPurchasesFetched += HandlePurchasesFetched;
            storeController.OnPurchasesFetchFailed += HandlePurchasesFetchFailed;
            storeController.OnPurchasePending += HandlePurchasePending;
            storeController.OnPurchaseConfirmed += HandlePurchaseConfirmed;
            storeController.OnPurchaseFailed += HandlePurchaseFailed;
            storeController.OnPurchaseDeferred += HandlePurchaseDeferred;

            await storeController.Connect();

            List<ProductDefinition> definitions = BuildProductDefinitions();
            if (definitions.Count > 0)
            {
                storeController.FetchProducts(definitions);
            }
            else
            {
                isFullyInitialized = true;
                storeController.FetchPurchases();
            }

            onComplete?.Invoke(true);
        }
        catch (Exception ex)
        {
            isConnectingOrConnected = false;
            isFullyInitialized = false;
            Debug.LogError($"[UnityPurchasingService] Failed to connect to store: {ex.Message}");
            onComplete?.Invoke(false);
        }
    }

    private void UnsubscribeStoreEvents()
    {
        if (storeController == null)
            return;

        storeController.OnStoreConnected -= HandleStoreConnected;
        storeController.OnStoreDisconnected -= HandleStoreDisconnected;
        storeController.OnAuthAccountChanged -= HandleAuthAccountChanged;
        storeController.OnProductsFetched -= HandleProductsFetched;
        storeController.OnProductsFetchFailed -= HandleProductsFetchFailed;
        storeController.OnPurchasesFetched -= HandlePurchasesFetched;
        storeController.OnPurchasesFetchFailed -= HandlePurchasesFetchFailed;
        storeController.OnPurchasePending -= HandlePurchasePending;
        storeController.OnPurchaseConfirmed -= HandlePurchaseConfirmed;
        storeController.OnPurchaseFailed -= HandlePurchaseFailed;
        storeController.OnPurchaseDeferred -= HandlePurchaseDeferred;
    }

    private static List<ProductDefinition> BuildProductDefinitions()
    {
        var list = new List<ProductDefinition>();
        var addedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        ShopDatabase db = ShopManager.Instance != null ? ShopManager.Instance.Database : null;
        if (db?.Items != null)
        {
            for (int i = 0; i < db.Items.Count; i++)
            {
                ShopItemDefinition item = db.Items[i];
                if (item == null || item.CostType != ShopCostType.RealMoney || string.IsNullOrEmpty(item.RealMoneyProductId))
                    continue;

                if (!addedIds.Add(item.RealMoneyProductId))
                    continue;

                ProductType unityType = item.IapProductType == IAPProductTypeKind.NonConsumable
                    ? ProductType.NonConsumable
                    : ProductType.Consumable;

                string storeSpecificId = ResolvePlatformStoreId(item);
                if (!string.IsNullOrEmpty(storeSpecificId) && !string.Equals(storeSpecificId, item.RealMoneyProductId, StringComparison.Ordinal))
                    list.Add(new ProductDefinition(item.RealMoneyProductId, storeSpecificId, unityType));
                else
                    list.Add(new ProductDefinition(item.RealMoneyProductId, unityType));
            }
        }

        // Ensure canonical Remove Ads & Starter Pack fallback IDs are registered if ShopDatabase wasn't bound yet
        if (addedIds.Add("remove_ads"))
            list.Add(new ProductDefinition("remove_ads", ProductType.NonConsumable));
        if (addedIds.Add("no_ads"))
            list.Add(new ProductDefinition("no_ads", ProductType.NonConsumable));

        return list;
    }

    private static string ResolvePlatformStoreId(ShopItemDefinition item)
    {
        if (item == null)
            return string.Empty;

#if UNITY_IOS || UNITY_STANDALONE_OSX
        return item.StoreProductIdIOS;
#else
        return item.StoreProductIdAndroid;
#endif
    }

    public void PurchaseProduct(string productId, Action<IAPResult> onComplete)
    {
        if (string.IsNullOrEmpty(productId))
        {
            onComplete?.Invoke(IAPResult.Failed(productId, "Empty product ID."));
            return;
        }

        if (storeController == null || !isFullyInitialized)
        {
            onComplete?.Invoke(IAPResult.Failed(productId, "Store is not connected or products are still loading."));
            return;
        }

        Product product = storeController.GetProductById(productId);
        if (product == null || !product.availableToPurchase)
        {
            onComplete?.Invoke(IAPResult.Failed(productId, $"Product '{productId}' is not available to purchase on this store."));
            return;
        }

        activePurchaseCallbacks[productId] = onComplete;
        storeController.PurchaseProduct(product);
    }

    public void RestorePurchases(Action<bool, string> onComplete)
    {
        if (storeController == null)
        {
            onComplete?.Invoke(false, "StoreController unavailable.");
            return;
        }

        storeController.RestoreTransactions((restored, message) =>
        {
            storeController.FetchPurchases();
            onComplete?.Invoke(restored, message ?? string.Empty);
        });
    }

    public string GetLocalizedPrice(string productId)
    {
        if (!string.IsNullOrEmpty(productId) && localizedPrices.TryGetValue(productId, out string cached) && !string.IsNullOrEmpty(cached))
            return cached;

        Product product = storeController?.GetProductById(productId);
        string fromMeta = product?.metadata?.localizedPriceString;
        if (!string.IsNullOrEmpty(fromMeta))
        {
            localizedPrices[productId] = fromMeta;
            return fromMeta;
        }

        return string.Empty;
    }

    public bool IsProductPurchased(string productId)
    {
        return !string.IsNullOrEmpty(productId) && ownedStoreProductIds.Contains(productId);
    }

    public IReadOnlyCollection<string> GetOwnedStoreProductIds()
    {
        return ownedStoreProductIds;
    }

    // ─────────────────────────────────────────────────────────────
    //  StoreController Event Handlers
    // ─────────────────────────────────────────────────────────────

    private void HandleStoreConnected()
    {
        Debug.Log("[UnityPurchasingService] Connected to store.");
    }

    private void HandleStoreDisconnected(StoreConnectionFailureDescription failure)
    {
        isFullyInitialized = false;
        isConnectingOrConnected = false;
        Debug.LogWarning($"[UnityPurchasingService] Store disconnected: {failure?.Message}");
    }

    private void HandleAuthAccountChanged()
    {
        ownedStoreProductIds.Clear();
        localizedPrices.Clear();
        if (storeController != null)
        {
            List<ProductDefinition> defs = BuildProductDefinitions();
            if (defs.Count > 0)
                storeController.FetchProducts(defs);
        }
    }

    private void HandleProductsFetched(List<Product> products)
    {
        localizedPrices.Clear();
        if (products != null)
        {
            for (int i = 0; i < products.Count; i++)
            {
                Product p = products[i];
                if (p?.definition != null && !string.IsNullOrEmpty(p.definition.id))
                {
                    string price = p.metadata?.localizedPriceString ?? string.Empty;
                    if (!string.IsNullOrEmpty(price))
                        localizedPrices[p.definition.id] = price;
                }
            }
        }

        isFullyInitialized = true;
        storeController?.FetchPurchases();
        Debug.Log($"[UnityPurchasingService] Fetched {products?.Count ?? 0} store product(s).");
    }

    private void HandleProductsFetchFailed(ProductFetchFailed failure)
    {
        Debug.LogWarning($"[UnityPurchasingService] FetchProducts failed: {failure?.FailureReason}");
    }

    private void HandlePurchasesFetched(Orders orders)
    {
        if (orders == null)
            return;

        if (orders.ConfirmedOrders != null)
        {
            for (int i = 0; i < orders.ConfirmedOrders.Count; i++)
            {
                ConfirmedOrder confirmed = orders.ConfirmedOrders[i];
                string productId = ExtractPrimaryProductId(confirmed);
                if (!string.IsNullOrEmpty(productId))
                    ownedStoreProductIds.Add(productId);
            }

            if (ownedStoreProductIds.Count > 0 && IAPManager.Instance != null)
            {
                IAPManager.Instance.ReconcileOwnedNonConsumables(ownedStoreProductIds);
            }
        }

        // Recover any interrupted PendingOrders that were paid on store before a crash/disconnect
        if (orders.PendingOrders != null)
        {
            for (int i = 0; i < orders.PendingOrders.Count; i++)
            {
                PendingOrder pending = orders.PendingOrders[i];
                if (pending != null)
                    HandlePurchasePending(pending);
            }
        }
    }

    private void HandlePurchasesFetchFailed(PurchasesFetchFailureDescription failure)
    {
        Debug.LogWarning($"[UnityPurchasingService] FetchPurchases failed: {failure?.Message}");
    }

    private void HandlePurchasePending(PendingOrder pendingOrder)
    {
        if (pendingOrder == null)
            return;

        string productId = ExtractPrimaryProductId(pendingOrder);
        string transactionId = pendingOrder.Info?.TransactionID ?? string.Empty;
        if (string.IsNullOrEmpty(productId))
        {
            Debug.LogError("[UnityPurchasingService] PendingOrder contained no valid CatalogListingId; leaving unconfirmed.");
            return;
        }

        // Save-Before-Confirm: ONLY call ConfirmPurchase AFTER durable save succeeds (or if tx was already saved)
        bool durablySaved = IAPManager.ProcessVerifiedStoreOrderStatic(productId, transactionId, out _);
        if (!durablySaved)
        {
            Debug.LogError($"[UnityPurchasingService] Durable entitlement save failed for '{productId}' (Tx: {transactionId}). Leaving PendingOrder unconfirmed so the store retries safely.");
            return;
        }

        lastPendingTransactionIds[productId] = transactionId;
        ownedStoreProductIds.Add(productId);
        storeController?.ConfirmPurchase(pendingOrder);
    }

    private void HandlePurchaseConfirmed(Order order)
    {
        string productId = ExtractPrimaryProductId(order);
        if (string.IsNullOrEmpty(productId))
            return;

        if (order is FailedOrder failedConfirmation)
        {
            HandlePurchaseFailed(failedConfirmation);
            return;
        }

        string txId = lastPendingTransactionIds.TryGetValue(productId, out string cachedTx)
            ? cachedTx
            : (order?.Info?.TransactionID ?? string.Empty);
        lastPendingTransactionIds.Remove(productId);

        if (activePurchaseCallbacks.TryGetValue(productId, out Action<IAPResult> callback))
        {
            activePurchaseCallbacks.Remove(productId);
            callback?.Invoke(IAPResult.Success(productId, txId));
        }
    }

    private void HandlePurchaseFailed(FailedOrder failedOrder)
    {
        string productId = ExtractPrimaryProductId(failedOrder);
        PurchaseFailureReason reason = failedOrder != null ? failedOrder.FailureReason : PurchaseFailureReason.Unknown;
        string details = failedOrder?.Details ?? reason.ToString();

        if (!string.IsNullOrEmpty(productId) && activePurchaseCallbacks.TryGetValue(productId, out Action<IAPResult> callback))
        {
            activePurchaseCallbacks.Remove(productId);
            if (reason == PurchaseFailureReason.UserCancelled)
                callback?.Invoke(IAPResult.Cancelled(productId));
            else
                callback?.Invoke(IAPResult.Failed(productId, details));
        }
    }

    private void HandlePurchaseDeferred(DeferredOrder deferredOrder)
    {
        string productId = ExtractPrimaryProductId(deferredOrder);
        Debug.Log($"[UnityPurchasingService] Purchase deferred (Ask-to-Buy / pending external payment) for '{productId}'.");
    }

    private static string ExtractPrimaryProductId(Order order)
    {
        IReadOnlyList<CartItem> items = order?.CartOrdered?.Items();
        if (items == null || items.Count == 0 || items[0] == null)
            return string.Empty;

        if (!string.IsNullOrEmpty(items[0].CatalogListingId))
            return items[0].CatalogListingId;

        return items[0].Product?.definition?.id ?? string.Empty;
    }
}

