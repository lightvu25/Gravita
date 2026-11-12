using System;
using UnityEngine;

/// <summary>
/// Identifies the two currencies allowed by Gravita Economy GDD §2.2 & §3.
/// Scrap and bidirectional Coin/Gem conversion are strictly prohibited.
/// </summary>
public enum EconomyCurrencyType
{
    Coins = 0,
    Gems = 1
}

/// <summary>
/// Singleton service managing game currencies:
///   1. Coins (soft currency earned from level clears, star upgrades, missions, ads)
///   2. Gems (premium currency from sector milestones, selected achievements/weekly goals, and IAP)
///
/// Strictly guarantees non-negative balances and participates in unified atomic
/// transactions owned by <see cref="RewardService"/>.
/// </summary>
public class EconomyManager : MonoBehaviour
{
    public static EconomyManager Instance { get; private set; }

    [Header("Optional Balance Config Reference")]
    [SerializeField] private EconomyBalanceConfig balanceConfig;

    // ─────────────────────────────────────────────────────────────
    //  Events (newBalance, delta)
    // ─────────────────────────────────────────────────────────────
    public event Action<int, int> OnCoinsChanged;
    public event Action<int, int> OnGemsChanged;

    /// <summary>
    /// Detailed currency ledger event for Economy Analytics (GDD §13):
    /// (currencyType, signedDelta, newBalance, sourceOrSinkTag).
    /// </summary>
    public static event Action<EconomyCurrencyType, int, int, string> OnCurrencyLedgerEvent;

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
            Destroy(this);
            return;
        }

        Instance = this;
        if (balanceConfig != null)
            EconomyBalanceConfig.SetActiveConfig(balanceConfig);

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
    /// Adds coins to the player's wallet and persists immediately. Amount must be positive.
    /// </summary>
    public void AddCoins(int amount, string source = "")
    {
        if (amount <= 0) return;

        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        int prevCoins = Mathf.Max(0, profile.coins);
        int nextCoins = prevCoins + amount;
        profile.coins = nextCoins;

        if (!SaveManager.TrySaveProfile(profile))
        {
            profile.coins = prevCoins;
            return;
        }

        coins = nextCoins;
        gems = Mathf.Max(0, profile.gems);
        DispatchCoinsChanged(coins, amount, source);
        Debug.Log($"[EconomyManager] Added {amount} Coins (Source: {source}). New Balance: {coins}");
    }

    /// <summary>
    /// Checks whether the player can afford the specified coin cost.
    /// </summary>
    public bool CanAffordCoins(int amount)
    {
        if (amount < 0) return false;
        SyncWithActiveProfileInMemory();
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

        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        int prevCoins = Mathf.Max(0, profile.coins);
        if (prevCoins < amount)
            return false;

        int nextCoins = prevCoins - amount;
        profile.coins = nextCoins;

        if (!SaveManager.TrySaveProfile(profile))
        {
            profile.coins = prevCoins;
            return false;
        }

        coins = nextCoins;
        gems = Mathf.Max(0, profile.gems);
        DispatchCoinsChanged(coins, -amount, reason);
        Debug.Log($"[EconomyManager] Spent {amount} Coins (Reason: {reason}). Remaining: {coins}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Gems API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Adds gems to the player's wallet and persists immediately. Amount must be positive.
    /// </summary>
    public void AddGems(int amount, string source = "")
    {
        if (amount <= 0) return;

        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        int prevGems = Mathf.Max(0, profile.gems);
        int nextGems = prevGems + amount;
        profile.gems = nextGems;

        if (!SaveManager.TrySaveProfile(profile))
        {
            profile.gems = prevGems;
            return;
        }

        gems = nextGems;
        coins = Mathf.Max(0, profile.coins);
        DispatchGemsChanged(gems, amount, source);
        Debug.Log($"[EconomyManager] Added {amount} Gems (Source: {source}). New Balance: {gems}");
    }

    /// <summary>
    /// Checks whether the player can afford the specified gem cost.
    /// </summary>
    public bool CanAffordGems(int amount)
    {
        if (amount < 0) return false;
        SyncWithActiveProfileInMemory();
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

        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        int prevGems = Mathf.Max(0, profile.gems);
        if (prevGems < amount)
            return false;

        int nextGems = prevGems - amount;
        profile.gems = nextGems;

        if (!SaveManager.TrySaveProfile(profile))
        {
            profile.gems = prevGems;
            return false;
        }

        gems = nextGems;
        coins = Mathf.Max(0, profile.coins);
        DispatchGemsChanged(gems, -amount, reason);
        Debug.Log($"[EconomyManager] Spent {amount} Gems (Reason: {reason}). Remaining: {gems}");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Atomic Transaction Hooks (Used Exclusively by RewardService)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Synchronizes cached balances after an atomic <see cref="RewardService"/> commit
    /// and notifies UI/Analytics listeners once.
    /// </summary>
    internal void CommitFromValidatedTransaction(ProfileData committedProfile, int coinDelta, int gemDelta, string tag)
    {
        coins = committedProfile != null ? Mathf.Max(0, committedProfile.coins) : coins;
        gems = committedProfile != null ? Mathf.Max(0, committedProfile.gems) : gems;

        if (coinDelta != 0)
            DispatchCoinsChanged(coins, coinDelta, tag);

        if (gemDelta != 0)
            DispatchGemsChanged(gems, gemDelta, tag);
    }

    private void DispatchCoinsChanged(int newBalance, int signedDelta, string tag)
    {
        OnCoinsChanged?.Invoke(newBalance, signedDelta);
        OnCurrencyLedgerEvent?.Invoke(EconomyCurrencyType.Coins, signedDelta, newBalance, tag ?? string.Empty);
    }

    private void DispatchGemsChanged(int newBalance, int signedDelta, string tag)
    {
        OnGemsChanged?.Invoke(newBalance, signedDelta);
        OnCurrencyLedgerEvent?.Invoke(EconomyCurrencyType.Gems, signedDelta, newBalance, tag ?? string.Empty);
    }

    internal static void NotifyStaticLedgerOnly(EconomyCurrencyType currency, int signedDelta, int newBalance, string tag)
    {
        OnCurrencyLedgerEvent?.Invoke(currency, signedDelta, newBalance, tag ?? string.Empty);
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

    private void SyncWithActiveProfileInMemory()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile != null)
        {
            coins = Math.Max(0, profile.coins);
            gems = Math.Max(0, profile.gems);
        }
    }

    /// <summary>
    /// Testing helper to reset economy values in memory and save.
    /// </summary>
    public void ResetEconomy()
    {
        int oldCoins = coins;
        int oldGems = gems;

        ProfileData profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.coins = 0;
        profile.gems = 0;
        SaveManager.TrySaveProfile(profile);

        coins = 0;
        gems = 0;

        if (oldCoins != 0)
            DispatchCoinsChanged(0, -oldCoins, "ResetEconomy");
        if (oldGems != 0)
            DispatchGemsChanged(0, -oldGems, "ResetEconomy");

        Debug.Log("[EconomyManager] Economy reset to zero.");
    }
}
