using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unified atomic reward & purchase ledger (GDD §2.3, §12.1, §12.2, §15).
/// Guarantees:
///   1. Idempotent at-most-once execution via <see cref="ProfileData.claimedRewardIds"/> and <see cref="ProfileData.processedIapTransactionIds"/>.
///   2. Pre-validation of wallet balances, Power Skill inventory caps (12/12/8), cosmetic duplicates, and Gem restrictions.
///   3. In-memory snapshot + single atomic <see cref="SaveManager.TrySaveProfile"/> commit with full rollback on disk write failure.
///   4. UI & Analytics event dispatch strictly AFTER durable persistence succeeds.
/// </summary>
public class RewardService : MonoBehaviour
{
    public static RewardService Instance { get; private set; }

    /// <summary>Fired whenever a reward bundle is successfully persisted.</summary>
    public event Action<RewardBundle> OnRewardGranted;

    /// <summary>Static ledger event for Analytics & observers: (transaction, result).</summary>
    public static event Action<RewardTransaction, RewardTransactionResult> OnTransactionExecuted;

    private static bool isExecutingTransaction;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Executes an atomic <see cref="RewardTransaction"/> even if no <see cref="RewardService"/>
    /// MonoBehaviour instance is currently spawned in the scene (e.g. headless EditMode unit tests).
    /// </summary>
    public static RewardTransactionResult ExecuteTransaction(RewardTransaction tx)
    {
        if (Instance != null)
            return Instance.TryExecuteTransaction(tx);

        RewardService found = FindAnyObjectByType<RewardService>();
        if (found != null)
            return found.TryExecuteTransaction(tx);

        return ExecuteCore(tx, null);
    }

    /// <summary>
    /// Validates, applies, and persists <paramref name="tx"/> atomically.
    /// </summary>
    public RewardTransactionResult TryExecuteTransaction(RewardTransaction tx)
    {
        return ExecuteCore(tx, this);
    }

    /// <summary>
    /// Legacy convenience wrapper kept for backward compatibility. Routes through the atomic pipeline.
    /// </summary>
    public bool GrantRewardBundle(RewardBundle bundle, string reason = "Unspecified")
    {
        if (bundle == null || bundle.IsEmpty)
        {
            Debug.LogWarning($"[RewardService] Attempted to grant null or empty bundle. Reason: {reason}");
            return false;
        }

        var tx = new RewardTransaction
        {
            RequireUniqueRewardId = false,
            RejectIfSkillCapExceeded = false,
            Payout = bundle,
            Reason = reason
        };

        RewardTransactionResult result = TryExecuteTransaction(tx);
        return result.Succeeded;
    }

    private static RewardTransactionResult ExecuteCore(RewardTransaction tx, RewardService instance)
    {
        if (tx == null)
        {
            return Fail(tx, RewardTransactionStatus.InvalidRequest, "Transaction is null.");
        }

        if (isExecutingTransaction)
        {
            return Fail(tx, RewardTransactionStatus.ReentrancyBlocked, "Nested re-entrant transaction blocked.");
        }

        if (tx.RequireUniqueRewardId && string.IsNullOrWhiteSpace(tx.RewardId))
        {
            return Fail(tx, RewardTransactionStatus.InvalidRequest, "Unique RewardId required but missing.");
        }

        if (tx.CostAmount < 0)
        {
            return Fail(tx, RewardTransactionStatus.InvalidRequest, "Negative cost amount is invalid.");
        }

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
        {
            return Fail(tx, RewardTransactionStatus.SaveFailed, "Active ProfileData unavailable.");
        }

        isExecutingTransaction = true;
        try
        {
            profile.Sanitize();

            // 1. Idempotency checks (RewardId & IAP TransactionId)
            if (!string.IsNullOrEmpty(tx.RewardId) && profile.HasClaimedReward(tx.RewardId))
            {
                return Fail(tx, RewardTransactionStatus.DuplicateRewardId, $"RewardId '{tx.RewardId}' was already claimed.");
            }

            if (tx.AdditionalRewardIdsToMark != null)
            {
                for (int i = 0; i < tx.AdditionalRewardIdsToMark.Length; i++)
                {
                    string extraId = tx.AdditionalRewardIdsToMark[i];
                    if (!string.IsNullOrEmpty(extraId) && profile.HasClaimedReward(extraId))
                    {
                        return Fail(tx, RewardTransactionStatus.DuplicateRewardId, $"Additional RewardId '{extraId}' was already claimed.");
                    }
                }
            }

            if (!string.IsNullOrEmpty(tx.IapTransactionId) && profile.HasProcessedIapTransaction(tx.IapTransactionId))
            {
                return Fail(tx, RewardTransactionStatus.DuplicateIapTransaction, $"IAP TransactionId '{tx.IapTransactionId}' was already processed.");
            }

            // 2. Wallet cost check
            int coinCost = tx.CostType == ShopCostType.Coins ? tx.CostAmount : 0;
            int gemCost = tx.CostType == ShopCostType.Gems ? tx.CostAmount : 0;

            if (coinCost > 0 && profile.coins < coinCost)
            {
                return Fail(tx, RewardTransactionStatus.InsufficientFunds, $"Insufficient Coins ({profile.coins} < {coinCost}).");
            }

            if (gemCost > 0 && profile.gems < gemCost)
            {
                return Fail(tx, RewardTransactionStatus.InsufficientFunds, $"Insufficient Gems ({profile.gems} < {gemCost}).");
            }

            // 3. Payout validation (Gems restriction, Skill Inventory Cap, Cosmetic duplicate check)
            EconomyBalanceConfig balanceConfig = EconomyBalanceConfig.Active;
            var pendingSkillAdditions = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var pendingSkillUnlocks = new List<string>();
            var pendingCosmetics = new HashSet<string>(StringComparer.Ordinal);
            int coinGain = 0;
            int gemGain = 0;
            int expGain = 0;
            int overflowCoins = 0;

            if (tx.Payout != null && !tx.Payout.IsEmpty)
            {
                foreach (RewardItem item in tx.Payout.Items)
                {
                    switch (item.Type)
                    {
                        case RewardType.Coins:
                            if (item.Amount > 0)
                                coinGain += item.Amount;
                            break;

                        case RewardType.Gems:
                            if (tx.ForbidGemsInPayout)
                            {
                                return Fail(tx, RewardTransactionStatus.ForbiddenRewardType, "Gems are forbidden in this reward source (GDD §7.2).");
                            }
                            if (item.Amount > 0)
                                gemGain += item.Amount;
                            break;

                        case RewardType.PowerUp:
                            if (item.Amount <= 0 || string.IsNullOrEmpty(item.ItemId))
                                break;

                            if (!tx.RejectIfSkillCapExceeded && profile.GetPowerUpLevel(item.ItemId) >= 5)
                            {
                                coinGain += item.Amount * 25;
                                break;
                            }

                            int currentOwned = GetProfileSkillCount(profile, item.ItemId);
                            int alreadyQueued = pendingSkillAdditions.TryGetValue(item.ItemId, out int q) ? q : 0;
                            int cap = balanceConfig.GetSkillInventoryCap(item.ItemId);

                            if (tx.RejectIfSkillCapExceeded && (currentOwned + alreadyQueued + item.Amount > cap))
                            {
                                return Fail(
                                    tx,
                                    RewardTransactionStatus.SkillCapExceeded,
                                    $"Skill '{item.ItemId}' would exceed cap ({currentOwned + alreadyQueued + item.Amount} > {cap}).");
                            }

                            pendingSkillAdditions[item.ItemId] = alreadyQueued + item.Amount;
                            break;

                        case RewardType.Cosmetic:
                            if (string.IsNullOrEmpty(item.ItemId))
                                break;

                            bool alreadyOwned = CosmeticService.IsOwned(item.ItemId, profile) || pendingCosmetics.Contains(item.ItemId);
                            if (tx.RejectIfCosmeticAlreadyOwned && alreadyOwned)
                            {
                                return Fail(
                                    tx,
                                    RewardTransactionStatus.CosmeticAlreadyOwned,
                                    $"Cosmetic '{item.ItemId}' is already owned.");
                            }
                            pendingCosmetics.Add(item.ItemId);
                            break;

                        case RewardType.Exp:
                            if (item.Amount > 0)
                                expGain += item.Amount;
                            break;

                        case RewardType.SkillUnlock:
                            if (!string.IsNullOrEmpty(item.ItemId) && !pendingSkillUnlocks.Contains(item.ItemId))
                                pendingSkillUnlocks.Add(item.ItemId);
                            break;
                    }
                }
            }

            // 4. Snapshot JSON before mutating in-memory profile so disk save failure rolls back 100% cleanly
            string preMutationSnapshotJson = JsonUtility.ToJson(profile);

            // 5. Apply all mutations in memory
            profile.coins = Mathf.Max(0, profile.coins - coinCost + coinGain);
            profile.gems = Mathf.Max(0, profile.gems - gemCost + gemGain);

            bool skillsChanged = false;
            var actualSkillDeltas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, int> kv in pendingSkillAdditions)
            {
                int currentOwned = GetProfileSkillCount(profile, kv.Key);
                int cap = balanceConfig.GetSkillInventoryCap(kv.Key);
                int uncappedTotal = currentOwned + kv.Value;
                int clampedNewTotal = Mathf.Clamp(uncappedTotal, 0, cap);
                int appliedDelta = clampedNewTotal - currentOwned;
                if (appliedDelta > 0)
                {
                    SetProfileSkillCount(profile, kv.Key, clampedNewTotal);
                    actualSkillDeltas[kv.Key] = appliedDelta;
                    skillsChanged = true;
                }

                int excessShards = Mathf.Max(0, uncappedTotal - cap);
                if (!tx.RejectIfSkillCapExceeded && excessShards > 0)
                {
                    int convCoins = excessShards * 25;
                    overflowCoins += convCoins;
                    profile.coins = Mathf.Max(0, profile.coins + convCoins);
                }
            }

            for (int i = 0; i < pendingSkillUnlocks.Count; i++)
            {
                string unlockId = pendingSkillUnlocks[i];
                if (!profile.HasSkill(unlockId))
                {
                    profile.UnlockSkill(unlockId);
                    skillsChanged = true;
                }
                else if (!tx.RejectIfSkillCapExceeded)
                {
                    overflowCoins += 25;
                    profile.coins = Mathf.Max(0, profile.coins + 25);
                }
            }

            bool cosmeticsChanged = false;
            foreach (string cosmeticId in pendingCosmetics)
            {
                if (!profile.ownedCosmeticIds.Contains(cosmeticId))
                {
                    profile.ownedCosmeticIds.Add(cosmeticId);
                    cosmeticsChanged = true;
                }
            }

            if (expGain > 0)
            {
                profile.playerExp = Mathf.Max(0, profile.playerExp + expGain);
            }

            if (!string.IsNullOrEmpty(tx.OneTimeShopItemId) && !profile.purchasedShopItemIDs.Contains(tx.OneTimeShopItemId))
            {
                profile.purchasedShopItemIDs.Add(tx.OneTimeShopItemId);
            }

            if (!string.IsNullOrEmpty(tx.NonConsumableProductId) && !profile.purchasedProductIDs.Contains(tx.NonConsumableProductId))
            {
                profile.purchasedProductIDs.Add(tx.NonConsumableProductId);
            }

            if (tx.GrantsRemoveInterstitialAds)
            {
                profile.noAdsPurchased = true;
            }

            if (!string.IsNullOrEmpty(tx.RewardId))
            {
                profile.MarkRewardClaimed(tx.RewardId);
            }

            if (tx.AdditionalRewardIdsToMark != null)
            {
                for (int i = 0; i < tx.AdditionalRewardIdsToMark.Length; i++)
                {
                    if (!string.IsNullOrEmpty(tx.AdditionalRewardIdsToMark[i]))
                        profile.MarkRewardClaimed(tx.AdditionalRewardIdsToMark[i]);
                }
            }

            if (!string.IsNullOrEmpty(tx.IapTransactionId))
            {
                profile.MarkIapTransactionProcessed(tx.IapTransactionId);
            }

            tx.OnBeforeSaveMutateProfile?.Invoke(profile);

            // 6. Single atomic disk commit BEFORE notifying any UI
            if (!SaveManager.TrySaveProfile(profile))
            {
                JsonUtility.FromJsonOverwrite(preMutationSnapshotJson, profile);
                profile.Sanitize();
                return Fail(tx, RewardTransactionStatus.SaveFailed, "Disk write failed; rolled back in-memory transaction.");
            }

            // 7. Synchronize live runtime services & fire events strictly post-commit
            int netCoinsDelta = coinGain + overflowCoins - coinCost;
            int netGemsDelta = gemGain - gemCost;

            EconomyManager economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();
            if (economy != null)
            {
                economy.CommitFromValidatedTransaction(profile, netCoinsDelta, netGemsDelta, tx.Reason);
            }
            else
            {
                if (netCoinsDelta != 0)
                    DispatchFallbackLedger(EconomyCurrencyType.Coins, netCoinsDelta, profile.coins, tx.Reason);
                if (netGemsDelta != 0)
                    DispatchFallbackLedger(EconomyCurrencyType.Gems, netGemsDelta, profile.gems, tx.Reason);
            }

            ProgressionManager progression = ProgressionManager.Instance ?? FindAnyObjectByType<ProgressionManager>();
            if (progression != null && (skillsChanged || expGain > 0 || tx.OnBeforeSaveMutateProfile != null))
            {
                progression.NotifyExternalAtomicProfileCommit(actualSkillDeltas, tx.Reason);
            }

            if (cosmeticsChanged)
            {
                CosmeticService.NotifyCosmeticsUpdated();
            }

            ShopManager shop = ShopManager.Instance ?? FindAnyObjectByType<ShopManager>();
            if (shop != null && !string.IsNullOrEmpty(tx.OneTimeShopItemId))
            {
                shop.LoadFromProfile();
            }

            IAPManager iap = IAPManager.Instance ?? FindAnyObjectByType<IAPManager>();
            if (iap != null && (!string.IsNullOrEmpty(tx.NonConsumableProductId) || tx.GrantsRemoveInterstitialAds))
            {
                iap.LoadFromProfile();
            }

            var appliedBundle = tx.Payout ?? new RewardBundle();
            if (!appliedBundle.IsEmpty)
            {
                instance?.OnRewardGranted?.Invoke(appliedBundle);
            }

            var successResult = new RewardTransactionResult(
                RewardTransactionStatus.Success,
                "Committed atomically.",
                netCoinsDelta,
                netGemsDelta,
                appliedBundle);

            OnTransactionExecuted?.Invoke(tx, successResult);
            return successResult;
        }
        finally
        {
            isExecutingTransaction = false;
        }
    }

    private static void DispatchFallbackLedger(EconomyCurrencyType currency, int delta, int balance, string reason)
    {
        EconomyManager.NotifyStaticLedgerOnly(currency, delta, balance, reason);
    }

    private static int GetProfileSkillCount(ProfileData profile, string skillId)
    {
        if (profile == null || string.IsNullOrEmpty(skillId) || profile.powerUpIds == null || profile.powerUpCounts == null)
            return 0;

        int idx = profile.powerUpIds.IndexOf(skillId);
        return (idx >= 0 && idx < profile.powerUpCounts.Count) ? Mathf.Max(0, profile.powerUpCounts[idx]) : 0;
    }

    private static void SetProfileSkillCount(ProfileData profile, string skillId, int newCount)
    {
        int clamped = Mathf.Max(0, newCount);
        int idx = profile.powerUpIds.IndexOf(skillId);
        if (idx >= 0 && idx < profile.powerUpCounts.Count)
        {
            profile.powerUpCounts[idx] = clamped;
        }
        else
        {
            profile.powerUpIds.Add(skillId);
            profile.powerUpCounts.Add(clamped);
        }
    }

    private static RewardTransactionResult Fail(RewardTransaction tx, RewardTransactionStatus status, string message)
    {
        var result = new RewardTransactionResult(status, message);
        OnTransactionExecuted?.Invoke(tx, result);
        return result;
    }
}
