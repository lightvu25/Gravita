using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Mock implementation of IIAPService for Editor testing and non-store runtime environments.
/// Simulates store receipts and deterministic outcomes.
/// </summary>
public class MockIAPService : MonoBehaviour, IIAPService
{
    [Header("Mock Settings")]
    [SerializeField] private bool autoInitialize = true;
    [SerializeField] private IAPResultStatus defaultStatus = IAPResultStatus.Success;

    private readonly Dictionary<string, string> mockPrices = new Dictionary<string, string>
    {
        { "no_ads", "$2.99" },
        { "starter_pack", "$4.99" },
        { "gem_pack_small", "$0.99" },
        { "gem_pack_medium", "$4.99" },
        { "gem_pack_large", "$19.99" }
    };

    private readonly HashSet<string> purchasedNonConsumables = new HashSet<string>();

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
        Debug.Log("[MockIAPService] Initialized mock store successfully.");
        onComplete?.Invoke(true);
    }

    public void PurchaseProduct(string productId, Action<IAPResult> onComplete)
    {
        if (!IsInitialized)
        {
            Debug.LogError("[MockIAPService] Cannot purchase: Service not initialized.");
            onComplete?.Invoke(IAPResult.Failed(productId, "Store not initialized."));
            return;
        }

        IAPResult result;
        switch (defaultStatus)
        {
            case IAPResultStatus.Success:
                purchasedNonConsumables.Add(productId);
                result = IAPResult.Success(productId, $"mock_tx_{Guid.NewGuid().ToString().Substring(0, 8)}");
                break;
            case IAPResultStatus.Cancelled:
                result = IAPResult.Cancelled(productId);
                break;
            default:
                result = IAPResult.Failed(productId, "Simulated store purchase failure.");
                break;
        }

        Debug.Log($"[MockIAPService] PurchaseProduct '{productId}' -> {result.Status}");
        onComplete?.Invoke(result);
    }

    public void RestorePurchases(Action<bool, string> onComplete)
    {
        Debug.Log("[MockIAPService] RestorePurchases completed.");
        onComplete?.Invoke(true, "Restored mock purchases.");
    }

    public string GetLocalizedPrice(string productId)
    {
        if (mockPrices.TryGetValue(productId, out string price))
            return price;
        return "$0.99";
    }

    public bool IsProductPurchased(string productId)
    {
        return purchasedNonConsumables.Contains(productId);
    }
}

