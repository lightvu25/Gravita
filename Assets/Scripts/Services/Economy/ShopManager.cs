using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Specific reason why a Shop item cannot currently be purchased (used by UI states and tests).
/// </summary>
public enum ShopPurchaseBlockReason
{
    None = 0,
    NullItem = 1,
    AlreadyOwned = 2,
    InsufficientCoins = 3,
    InsufficientGems = 4,
    SkillCapExceeded = 5,
    InvalidCurrencyRule = 6,
    InactiveCampaignSkill = 7,
    IapUnavailable = 8
}

/// <summary>
/// Central manager for shop transactions across Free, Coins, Gems, and Real Money IAP.
/// Enforces Gravita Economy GDD v1.0 rules:
///   - Non-negative wallet balances & zero Coin/Gem conversion (§2.2, §3.1, §3.2).
///   - Skill inventory caps: Breaker=12, Freeze=12, Teleport=8 (§5.1, §5.3).
///   - Single skill charge cannot be sold for Gems; Gem skill offers must be bundles (§5.3).
///   - Permanent Cosmetics (PlayerSkin) & one-time items cannot be bought twice (§6.3, §9.2).
///   - Atomic single-save execution via <see cref="RewardService.ExecuteTransaction"/> (§12.2).
/// </summary>
public class ShopManager : MonoBehaviour
{
    public static ShopManager Instance { get; private set; }

    [SerializeField] private ShopDatabase database;

    public event Action<ShopItemDefinition> OnItemPurchased;
    public event Action<ShopItemDefinition, ShopPurchaseBlockReason> OnPurchaseBlocked;
    public event Action<ShopItemDefinition, IReadOnlyList<LootRollResult>> OnLootPackRolled;

    private readonly HashSet<string> purchasedOneTimeItemIDs = new HashSet<string>();
    private readonly List<LootRollResult> lastLootRollResults = new List<LootRollResult>();

    public ShopDatabase Database => database;
    public IReadOnlyList<LootRollResult> LastLootRollResults => lastLootRollResults;
    public string LastPurchasedLootSummary { get; private set; } = string.Empty;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
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

    public void SetDatabase(ShopDatabase customDatabase)
    {
        if (Instance == null)
            Instance = this;

        database = customDatabase;
    }

    public void SetDatabaseIfNull(ShopDatabase fallbackDatabase)
    {
        if (database == null && fallbackDatabase != null)
            database = fallbackDatabase;
    }

    public bool HasPurchasedOneTime(string itemId)
    {
        if (string.IsNullOrEmpty(itemId)) return false;
        LoadFromProfile();
        return purchasedOneTimeItemIDs.Contains(itemId);
    }

    /// <summary>
    /// Evaluates whether the player is currently eligible to purchase <paramref name="item"/>
    /// and outputs the exact <see cref="ShopPurchaseBlockReason"/> when blocked.
    /// </summary>
    public bool EvaluatePurchase(ShopItemDefinition item, out ShopPurchaseBlockReason blockReason)
    {
        if (item == null)
        {
            blockReason = ShopPurchaseBlockReason.NullItem;
            return false;
        }

        ProfileData profile = SaveManager.loadProfile();
        LoadFromProfile();

        // 1. One-time / Remove Ads ownership check
        if (item.IsOneTimePurchase && HasPurchasedOneTime(item.ItemId))
        {
            blockReason = ShopPurchaseBlockReason.AlreadyOwned;
            return false;
        }

        if (item.GrantsRemoveInterstitialAds &&
            ((profile != null && profile.noAdsPurchased) || (IAPManager.Instance != null && IAPManager.Instance.NoAdsPurchased)))
        {
            blockReason = ShopPurchaseBlockReason.AlreadyOwned;
            return false;
        }

        var iap = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
        if (item.CostType == ShopCostType.RealMoney && item.IsOneTimePurchase && !string.IsNullOrEmpty(item.RealMoneyProductId))
        {
            if ((iap != null && iap.HasPurchased(item.RealMoneyProductId)) ||
                (profile != null && profile.purchasedProductIDs != null && profile.purchasedProductIDs.Contains(item.RealMoneyProductId)))
            {
                blockReason = ShopPurchaseBlockReason.AlreadyOwned;
                return false;
            }
        }

        // 2. Inspect contents against GDD currency & cap invariants
        RewardBundle bundle = item.CreateRewardBundle();
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;

        // Prohibit Coin <-> Gem conversion in soft/hard currency shop offers (GDD §2.2 & §3.1/§3.2)
        if (item.CostType == ShopCostType.Coins && bundle.HasType(RewardType.Gems))
        {
            blockReason = ShopPurchaseBlockReason.InvalidCurrencyRule;
            return false;
        }
        if (item.CostType == ShopCostType.Gems && bundle.HasType(RewardType.Coins) && item.Category != ShopItemCategory.Bundle)
        {
            blockReason = ShopPurchaseBlockReason.InvalidCurrencyRule;
            return false;
        }

        int totalSkillUnits = 0;
        int totalCosmeticItems = 0;
        int ownedCosmeticItems = 0;
        var skillAdditions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (RewardItem reward in bundle.Items)
        {
            if (reward.Type == RewardType.PowerUp)
            {
                if (string.IsNullOrEmpty(reward.ItemId) || !balance.IsSkillActiveInCampaign(reward.ItemId))
                {
                    blockReason = ShopPurchaseBlockReason.InactiveCampaignSkill;
                    return false;
                }

                totalSkillUnits += Mathf.Max(0, reward.Amount);
                int prev = skillAdditions.TryGetValue(reward.ItemId, out int existing) ? existing : 0;
                skillAdditions[reward.ItemId] = prev + Mathf.Max(0, reward.Amount);
            }
            else if (reward.Type == RewardType.Cosmetic && !string.IsNullOrEmpty(reward.ItemId))
            {
                totalCosmeticItems++;
                if (CosmeticService.IsOwned(reward.ItemId, profile))
                    ownedCosmeticItems++;
            }
        }

        // GDD §5.3: Gem cannot purchase a single Skill Charge directly; Gem skill offers must be bundles (>= 2 charges or multi-type bundle)
        if (item.CostType == ShopCostType.Gems &&
            (item.Category == ShopItemCategory.SkillChargeSingle ||
             (totalSkillUnits == 1 && bundle.Items.Count == 1)))
        {
            blockReason = ShopPurchaseBlockReason.InvalidCurrencyRule;
            return false;
        }

        // GDD §6.3: Cosmetics cannot be purchased a second time
        if (totalCosmeticItems > 0 && ownedCosmeticItems >= totalCosmeticItems &&
            (item.Category == ShopItemCategory.Cosmetic || bundle.Items.Count == totalCosmeticItems))
        {
            blockReason = ShopPurchaseBlockReason.AlreadyOwned;
            return false;
        }

        // GDD §5.1 & §5.3: Enforce skill inventory caps (Breaker=12, Freeze=12, Teleport=8) for fixed Coin & Gem skill offers
        if (!item.HasLootPack && item.CostType != ShopCostType.RealMoney && skillAdditions.Count > 0)
        {
            foreach (KeyValuePair<string, int> kv in skillAdditions)
            {
                int currentOwned = GetOwnedSkillCount(profile, kv.Key);
                int cap = balance.GetSkillInventoryCap(kv.Key);
                if (currentOwned + kv.Value > cap)
                {
                    blockReason = ShopPurchaseBlockReason.SkillCapExceeded;
                    return false;
                }
            }
        }

        // 3. Affordability check
        if (item.IsFree)
        {
            blockReason = ShopPurchaseBlockReason.None;
            return true;
        }

        var eco = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
        int currentCoins = eco != null ? eco.Coins : (profile != null ? Mathf.Max(0, profile.coins) : 0);
        int currentGems = eco != null ? eco.Gems : (profile != null ? Mathf.Max(0, profile.gems) : 0);

        switch (item.CostType)
        {
            case ShopCostType.Free:
                blockReason = ShopPurchaseBlockReason.None;
                return true;

            case ShopCostType.Coins:
                if (item.CostAmount > 0 && currentCoins < item.CostAmount)
                {
                    blockReason = ShopPurchaseBlockReason.InsufficientCoins;
                    return false;
                }
                blockReason = ShopPurchaseBlockReason.None;
                return true;

            case ShopCostType.Gems:
                if (item.CostAmount > 0 && currentGems < item.CostAmount)
                {
                    blockReason = ShopPurchaseBlockReason.InsufficientGems;
                    return false;
                }
                blockReason = ShopPurchaseBlockReason.None;
                return true;

            case ShopCostType.RealMoney:
                if (string.IsNullOrEmpty(item.RealMoneyProductId))
                {
                    blockReason = ShopPurchaseBlockReason.IapUnavailable;
                    return false;
                }
                blockReason = ShopPurchaseBlockReason.None;
                return true;

            default:
                blockReason = ShopPurchaseBlockReason.InvalidCurrencyRule;
                return false;
        }
    }

    /// <summary>
    /// Evaluates whether the player is currently eligible to purchase the specified item.
    /// </summary>
    public bool CanPurchase(ShopItemDefinition item)
    {
        return EvaluatePurchase(item, out _);
    }

    /// <summary>
    /// Executes an atomic purchase transaction via <see cref="RewardService"/> (for Free/Coins/Gems)
    /// or delegates to <see cref="IAPManager"/> (for Real Money).
    /// </summary>
    public void PurchaseItem(ShopItemDefinition item, Action<bool> onComplete = null)
    {
        if (!EvaluatePurchase(item, out ShopPurchaseBlockReason blockReason))
        {
            Debug.LogWarning($"[ShopManager] Purchase rejected for '{item?.DisplayName}': {blockReason}.");
            OnPurchaseBlocked?.Invoke(item, blockReason);
            onComplete?.Invoke(false);
            return;
        }

        if (item.CostType == ShopCostType.RealMoney && !item.IsFree)
        {
            var iap = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
            if (iap == null)
            {
                Debug.LogError("[ShopManager] IAPManager unavailable for real-money purchase.");
                OnPurchaseBlocked?.Invoke(item, ShopPurchaseBlockReason.IapUnavailable);
                onComplete?.Invoke(false);
                return;
            }

            iap.PurchaseShopItem(item, success =>
            {
                if (success)
                {
                    LoadFromProfile();
                    OnItemPurchased?.Invoke(item);
                }
                onComplete?.Invoke(success);
            });
            return;
        }

        lastLootRollResults.Clear();
        LastPurchasedLootSummary = string.Empty;

        RewardBundle finalPayout;
        ProfileData workingPityProfile = null;
        List<LootRollResult> rolledItems = null;

        if (item.HasLootPack)
        {
            workingPityProfile = SaveManager.loadProfile() ?? new ProfileData();
            workingPityProfile.Sanitize();

            RewardBundle rolledBundle = item.LootPack.RollBundle(
                workingPityProfile,
                out rolledItems,
                item.LootRollsPerPurchase,
                allowGems: item.CostType != ShopCostType.Coins,
                customRng: null,
                mutateProfilePity: true);

            finalPayout = (rolledBundle != null && !rolledBundle.IsEmpty)
                ? rolledBundle
                : item.CreateRewardBundle();
        }
        else
        {
            finalPayout = item.CreateRewardBundle();
        }

        var tx = new RewardTransaction
        {
            CostType = item.IsFree ? ShopCostType.Free : item.CostType,
            CostAmount = item.IsFree ? 0 : item.CostAmount,
            Payout = finalPayout,
            RejectIfSkillCapExceeded = !item.HasLootPack,
            RejectIfCosmeticAlreadyOwned = item.Category == ShopItemCategory.Cosmetic,
            OneTimeShopItemId = item.IsOneTimePurchase ? item.ItemId : string.Empty,
            GrantsRemoveInterstitialAds = item.GrantsRemoveInterstitialAds,
            Reason = $"ShopPurchase_{item.ItemId}",
            OnBeforeSaveMutateProfile = workingPityProfile != null
                ? p =>
                {
                    p.Sanitize();
                    p.lootPackPityKeys.Clear();
                    p.lootPackPityCounts.Clear();
                    p.lootPackPityKeys.AddRange(workingPityProfile.lootPackPityKeys);
                    p.lootPackPityCounts.AddRange(workingPityProfile.lootPackPityCounts);
                }
                : null
        };

        RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
        if (!result.Succeeded)
        {
            Debug.LogWarning($"[ShopManager] Atomic transaction failed for '{item.DisplayName}': {result.Status} ({result.DiagnosticMessage})");
            onComplete?.Invoke(false);
            return;
        }

        if (rolledItems != null && rolledItems.Count > 0)
        {
            lastLootRollResults.AddRange(rolledItems);
            var labels = new List<string>(rolledItems.Count);
            for (int i = 0; i < rolledItems.Count; i++)
            {
                if (!string.IsNullOrEmpty(rolledItems[i].FormattedLabel))
                    labels.Add(rolledItems[i].FormattedLabel);
            }
            LastPurchasedLootSummary = string.Join(", ", labels);
            OnLootPackRolled?.Invoke(item, lastLootRollResults);
        }

        if (item.IsOneTimePurchase)
            purchasedOneTimeItemIDs.Add(item.ItemId);

        OnItemPurchased?.Invoke(item);
        Debug.Log($"[ShopManager] Completed purchase for '{item.DisplayName}'.");
        onComplete?.Invoke(true);
    }

    public ShopItemDefinition FindByRealMoneyProductId(string productId)
    {
        if (string.IsNullOrEmpty(productId) || database?.Items == null)
            return null;

        for (int i = 0; i < database.Items.Count; i++)
        {
            ShopItemDefinition item = database.Items[i];
            if (item == null || item.CostType != ShopCostType.RealMoney)
                continue;

            if (string.Equals(item.RealMoneyProductId, productId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.StoreProductIdAndroid, productId, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(item.StoreProductIdIOS, productId, StringComparison.OrdinalIgnoreCase))
            {
                return item;
            }
        }

        return null;
    }

    private static int GetOwnedSkillCount(ProfileData profile, string skillId)
    {
        if (ProgressionManager.Instance != null)
            return ProgressionManager.Instance.GetPowerUpCount(skillId);

        if (profile == null || profile.powerUpIds == null || profile.powerUpCounts == null)
            return 0;

        int idx = profile.powerUpIds.IndexOf(skillId);
        return (idx >= 0 && idx < profile.powerUpCounts.Count) ? Mathf.Max(0, profile.powerUpCounts[idx]) : 0;
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
}
