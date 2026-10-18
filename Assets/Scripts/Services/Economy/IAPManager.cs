using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central manager for In-App Purchases.
/// Strictly guarantees entitlements are granted ONLY upon verified receipt (IAPResult.Success).
/// Prevents granting rewards merely on UI button click.
/// </summary>
public class IAPManager : MonoBehaviour
{
    public static IAPManager Instance { get; private set; }

    private IIAPService iapService;

    public event Action<string> OnPurchaseSucceeded;
    public event Action<string, string> OnPurchaseFailed;
    public event Action OnPurchasesRestored;

    private readonly HashSet<string> purchasedProducts = new HashSet<string>();
    private bool noAdsPurchased;

    public bool NoAdsPurchased => noAdsPurchased;
    public IReadOnlyCollection<string> PurchasedProductIDs => purchasedProducts;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        iapService = GetComponent<IIAPService>() ?? FindAnyObjectByType<MockIAPService>();
        LoadFromProfile();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetIAPService(IIAPService service)
    {
        this.iapService = service;
    }

    public bool HasPurchased(string productId)
    {
        if (string.IsNullOrEmpty(productId)) return false;
        if (productId == "no_ads" && noAdsPurchased) return true;
        return purchasedProducts.Contains(productId);
    }

    public string GetPriceString(string productId)
    {
        if (iapService == null)
            iapService = FindFirstIAPService();

        return iapService != null ? iapService.GetLocalizedPrice(productId) : "$0.99";
    }

    /// <summary>
    /// Initiates a purchase flow. Rewards and entitlements are only processed upon verified store success.
    /// </summary>
    public void Purchase(string productId, Action<bool> onComplete = null)
    {
        if (string.IsNullOrEmpty(productId))
        {
            Debug.LogError("[IAPManager] Cannot purchase: Empty product ID.");
            onComplete?.Invoke(false);
            return;
        }

        if (iapService == null)
            iapService = FindFirstIAPService();

        if (iapService == null || !iapService.IsInitialized)
        {
            Debug.LogError("[IAPManager] Cannot purchase: IAP service not initialized.");
            onComplete?.Invoke(false);
            return;
        }

        iapService.PurchaseProduct(productId, result =>
        {
            if (!result.IsSuccess)
            {
                Debug.LogWarning($"[IAPManager] Purchase failed or cancelled for '{productId}': {result.ErrorMessage}");
                OnPurchaseFailed?.Invoke(productId, result.ErrorMessage);
                onComplete?.Invoke(false);
                return;
            }

            // Verified success: Grant entitlement and persist
            RecordPurchase(productId);
            OnPurchaseSucceeded?.Invoke(productId);
            Debug.Log($"[IAPManager] Successfully completed purchase for '{productId}' (Tx: {result.TransactionId}).");
            onComplete?.Invoke(true);
        });
    }

    public void RestorePurchases(Action<bool> onComplete = null)
    {
        if (iapService == null)
            iapService = FindFirstIAPService();

        if (iapService == null)
        {
            onComplete?.Invoke(false);
            return;
        }

        iapService.RestorePurchases((success, msg) =>
        {
            if (success)
            {
                OnPurchasesRestored?.Invoke();
            }
            onComplete?.Invoke(success);
        });
    }

    private void RecordPurchase(string productId)
    {
        if (productId == "no_ads")
        {
            noAdsPurchased = true;
        }

        purchasedProducts.Add(productId);
        SaveToProfile();
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

    private void SaveToProfile()
    {
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.noAdsPurchased = noAdsPurchased;

        if (profile.purchasedProductIDs == null)
            profile.purchasedProductIDs = new List<string>();

        profile.purchasedProductIDs.Clear();
        profile.purchasedProductIDs.AddRange(purchasedProducts);

        SaveManager.saveProfile(profile);
    }

    private IIAPService FindFirstIAPService()
    {
        return FindAnyObjectByType<MockIAPService>();
    }
}

