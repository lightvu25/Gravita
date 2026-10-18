using System;

/// <summary>
/// Architectural abstraction decoupling billing logic from store providers (Google Play Billing, Apple StoreKit).
/// </summary>
public interface IIAPService
{
    bool IsInitialized { get; }

    void Initialize(Action<bool> onComplete);

    void PurchaseProduct(string productId, Action<IAPResult> onComplete);

    void RestorePurchases(Action<bool, string> onComplete);

    string GetLocalizedPrice(string productId);

    bool IsProductPurchased(string productId);
}

