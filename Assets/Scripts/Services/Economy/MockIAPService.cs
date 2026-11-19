using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mock implementation of <see cref="IIAPService"/> for Unity Editor playtesting and automated NUnit tests.
/// All price labels are explicitly prefixed with "[MOCK]" so Editor simulation is never mistaken for a live store.
/// </summary>
public class MockIAPService : MonoBehaviour, IIAPService
{
    [Header("Mock Settings")]
    [SerializeField] private bool autoInitialize = true;
    [SerializeField] private IAPResultStatus defaultStatus = IAPResultStatus.Success;

    private readonly Dictionary<string, string> mockPrices = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        { "no_ads", "$2.99" },
        { "remove_ads", "$2.99" },
        { "starter_pack", "$4.99" },
        { "gem_pack_small", "$0.99" },
        { "gem_pack_tier_1", "$0.99" },
        { "gem_pack_medium", "$4.99" },
        { "gem_pack_tier_2", "$4.99" },
        { "gem_pack_tier_3", "$9.99" },
        { "gem_pack_large", "$19.99" },
        { "gem_pack_tier_4", "$19.99" }
    };

    private readonly HashSet<string> purchasedNonConsumables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string forcedNextTransactionId;

    public bool IsInitialized { get; private set; } = true;

    public IAPResultStatus DefaultStatus
    {
        get => defaultStatus;
        set => defaultStatus = value;
    }

    private void Awake()
    {
        if (autoInitialize)
            Initialize(null);
    }

    public void Initialize(Action<bool> onComplete)
    {
        IsInitialized = true;
        Debug.Log("[MockIAPService] Initialized [MOCK] store successfully.");
        onComplete?.Invoke(true);
    }

    /// <summary>
    /// Test helper allowing NUnit tests to force a deterministic store TransactionId (e.g. to verify duplicate callback rejection).
    /// </summary>
    public void SetCustomTransactionIdForNextPurchase(string transactionId)
    {
        forcedNextTransactionId = transactionId;
    }

    /// <summary>
    /// Test helper simulating an existing store entitlement on Apple/Google prior to calling RestorePurchases on a fresh profile.
    /// </summary>
    public void SeedOwnedStoreProduct(string productId)
    {
        if (!string.IsNullOrEmpty(productId))
            purchasedNonConsumables.Add(productId);
    }

    public void ClearOwnedStoreProducts()
    {
        purchasedNonConsumables.Clear();
    }

    public void PurchaseProduct(string productId, Action<IAPResult> onComplete)
    {
        if (!IsInitialized)
        {
            Debug.LogError("[MockIAPService] Cannot purchase: Service not initialized.");
            onComplete?.Invoke(IAPResult.Failed(productId, "Store not initialized."));
            return;
        }

        string txId = !string.IsNullOrEmpty(forcedNextTransactionId)
            ? forcedNextTransactionId
            : $"mock_tx_{Guid.NewGuid():N}";
        forcedNextTransactionId = null;

        IAPResult result;
        switch (defaultStatus)
        {
            case IAPResultStatus.Success:
                purchasedNonConsumables.Add(productId);
                result = IAPResult.Success(productId, txId);
                break;
            case IAPResultStatus.Cancelled:
                result = IAPResult.Cancelled(productId);
                break;
            default:
                result = IAPResult.Failed(productId, "Simulated store purchase failure.");
                break;
        }

        Debug.Log($"[MockIAPService] [MOCK] PurchaseProduct '{productId}' -> {result.Status} (Tx: {result.TransactionId})");
        onComplete?.Invoke(result);
    }

    public void RestorePurchases(Action<bool, string> onComplete)
    {
        if (!IsInitialized)
        {
            onComplete?.Invoke(false, "Mock store not initialized.");
            return;
        }

        Debug.Log("[MockIAPService] [MOCK] RestorePurchases completed.");
        onComplete?.Invoke(true, "Restored mock purchases.");
    }

    public string GetLocalizedPrice(string productId)
    {
        if (!string.IsNullOrEmpty(productId) && mockPrices.TryGetValue(productId, out string price))
            return price;
        return "$0.99";
    }

    public bool IsProductPurchased(string productId)
    {
        return !string.IsNullOrEmpty(productId) && purchasedNonConsumables.Contains(productId);
    }

    public IReadOnlyCollection<string> GetOwnedStoreProductIds()
    {
        return purchasedNonConsumables;
    }
}
