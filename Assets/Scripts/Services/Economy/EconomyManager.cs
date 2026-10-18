using System;
using UnityEngine;

/// <summary>
/// Singleton service managing game currencies:
///   1. Coins (soft currency earned from level clears and bonuses)
///   2. Gems (premium currency for bundles and shop offers)
///
/// Stars are the prestige metric — they are earned via level completion and
/// owned by ProgressionManager, not this wallet.
///
/// Persistence is integrated directly with SaveManager / ProfileData.
/// Strictly guarantees non-negative balances.
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Events (newBalance, delta)
    // ─────────────────────────────────────────────────────────────
    public event Action<int, int> OnCoinsChanged;
    public event Action<int, int> OnGemsChanged;

    // ─────────────────────────────────────────────────────────────
    //  Runtime Cache
    // ─────────────────────────────────────────────────────────────
    private int coins;
    private int gems;

    public int Coins => coins;
    public int Gems => gems;

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

    // ─────────────────────────────────────────────────────────────
    //  Coins API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds coins to the player's wallet. Amount must be positive.
    /// </summary>
    public void AddCoins(int amount, string source = "")
    {
        if (amount <= 0) return;

        coins += amount;
        SaveToProfile();
        OnCoinsChanged?.Invoke(coins, amount);
        Debug.Log($"[EconomyManager] Added {amount} Coins (Source: {source}). New Balance: {coins}");
    }

    /// <summary>
    /// Checks whether the player can afford the specified coin cost.
    /// </summary>
    public bool CanAffordCoins(int amount)
    {
        if (amount < 0) return false;
        return coins >= amount;
    }

    /// <summary>
    /// Spends coins from the player's wallet. Returns false if insufficient funds or amount <= 0.
    /// Guaranteed never to produce a negative balance.
    /// </summary>
    public bool SpendCoins(int amount, string reason = "")
    {
        if (amount <= 0) return false;
        if (!CanAffordCoins(amount))
        {
            Debug.LogWarning($"[EconomyManager] SpendCoins failed: Insufficient coins ({coins} < {amount}). Reason: {reason}");
            return false;
        }

        coins -= amount;
        SaveToProfile();
        OnCoinsChanged?.Invoke(coins, -amount);
        Debug.Log($"[EconomyManager] Spent {amount} Coins (Reason: {reason}). Remaining: {coins}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Gems API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds gems to the player's wallet. Amount must be positive.
    /// </summary>
    public void AddGems(int amount, string source = "")
    {
        if (amount <= 0) return;

        gems += amount;
        SaveToProfile();
        OnGemsChanged?.Invoke(gems, amount);
        Debug.Log($"[EconomyManager] Added {amount} Gems (Source: {source}). New Balance: {gems}");
    }

    /// <summary>
    /// Checks whether the player can afford the specified gem cost.
    /// </summary>
    public bool CanAffordGems(int amount)
    {
        if (amount < 0) return false;
        return gems >= amount;
    }

    /// <summary>
    /// Spends gems from the player's wallet. Returns false if insufficient funds or amount <= 0.
    /// Guaranteed never to produce a negative balance.
    /// </summary>
    public bool SpendGems(int amount, string reason = "")
    {
        if (amount <= 0) return false;
        if (!CanAffordGems(amount))
        {
            Debug.LogWarning($"[EconomyManager] SpendGems failed: Insufficient gems ({gems} < {amount}). Reason: {reason}");
            return false;
        }

        gems -= amount;
        SaveToProfile();
        OnGemsChanged?.Invoke(gems, -amount);
        Debug.Log($"[EconomyManager] Spent {amount} Gems (Reason: {reason}). Remaining: {gems}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence Integration
    // ─────────────────────────────────────────────────────────────

    public void LoadFromProfile()
    {
        ProfileData profile = GameSession.Instance?.currentProfile ?? SaveManager.loadProfile();
        if (profile != null)
        {
            coins = Math.Max(0, profile.coins);
            gems = Math.Max(0, profile.gems);
        }
        else
        {
            coins = 0;
            gems = 0;
        }
    }

    private void SaveToProfile()
    {
        // Always load fresh from disk: GameSession.currentProfile can be a stale
        // snapshot that would silently revert fields written by other services.
        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.coins = coins;
        profile.gems = gems;
        SaveManager.saveProfile(profile);

        if (GameSession.Instance != null)
        {
            GameSession.Instance.currentProfile = profile;
        }
    }

    /// <summary>
    /// Testing helper to reset economy values in memory and save.
    /// </summary>
    public void ResetEconomy()
    {
        int oldCoins = coins;
        int oldGems = gems;

        coins = 0;
        gems = 0;
        SaveToProfile();

        OnCoinsChanged?.Invoke(0, -oldCoins);
        OnGemsChanged?.Invoke(0, -oldGems);
        Debug.Log("[EconomyManager] Economy reset to zero.");
    }
}

