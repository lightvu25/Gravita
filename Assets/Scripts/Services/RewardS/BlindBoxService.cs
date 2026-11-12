using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Post-completion and menu mystery box service.
/// Uses <see cref="LootPackDefinition"/> with 100% total base drop rate and conditional
/// Soft/Hard Pity persisted in <see cref="ProfileData"/>, falling back to <see cref="rewardTable"/>
/// converted into an in-memory <see cref="LootPackDefinition"/> if no ScriptableObject is assigned.
/// </summary>
public class BlindBoxService : MonoBehaviour
{
    public static BlindBoxService Instance { get; private set; }

    [Header("Loot Pack ScriptableObject (Shared with Shop Pack Logic)")]
    [Tooltip("ScriptableObject defining drop rates (sum = 100%) and per-item Soft/Hard Pity rules.")]
    [SerializeField] private LootPackDefinition lootPack;

    [Header("Fallback Loot Table (Used when lootPack is unassigned)")]
    [SerializeField] private List<BlindBoxRewardEntry> rewardTable = new List<BlindBoxRewardEntry>
    {
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 15, maxAmount = 30, weight = 40, displayName = "Coins" },
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 50, maxAmount = 80, weight = 15, displayName = "Coins" },
        new BlindBoxRewardEntry { type = RewardType.Gems,    minAmount = 1,  maxAmount = 2,  weight = 20, displayName = "Gems" },
        new BlindBoxRewardEntry { type = RewardType.Gems,    minAmount = 3,  maxAmount = 5,  weight = 5,  displayName = "Gems" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Breaker, minAmount = 1, maxAmount = 1, weight = 20, displayName = "Breaker" },
    };

    /// <summary>True once the current box has been opened.</summary>
    public bool IsOpened { get; private set; }

    /// <summary>True once the rolled reward has already been doubled via ad.</summary>
    public bool IsDoubled { get; private set; }

    /// <summary>Human-readable label of the last rolled reward.</summary>
    public string LastRewardLabel { get; private set; } = "";

    /// <summary>Details of the most recent roll (entry, amount, effective rate, hard pity trigger).</summary>
    public LootRollResult LastRollResult { get; private set; }

    public LootPackDefinition LootPack
    {
        get => lootPack;
        set => lootPack = value;
    }

    private RewardBundle lastBundle;
    private LootPackDefinition runtimeFallbackPack;

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

        if (runtimeFallbackPack != null)
        {
            Destroy(runtimeFallbackPack);
            runtimeFallbackPack = null;
        }
    }

    /// <summary>Resets the box so the next completion can open a fresh one.</summary>
    public void ResetBox()
    {
        IsOpened = false;
        IsDoubled = false;
        lastBundle = null;
        LastRewardLabel = "";
        LastRollResult = default;
    }

    /// <summary>
    /// Rolls the shared <see cref="LootPackDefinition"/> (with Soft/Hard Pity and <see cref="ProfileData"/> persistence)
    /// and grants the reward atomically through <see cref="RewardService"/>.
    /// </summary>
    public RewardBundle Open(bool allowDirectMenuOpen = false)
    {
        bool allowGems = EconomyBalanceConfig.Active.EnablePostLevelBlindBox;
        if (!allowGems && !allowDirectMenuOpen)
            return null;

        if (IsOpened)
            return null;

        LootPackDefinition activePack = ResolveActiveLootPack();
        if (activePack == null || activePack.Entries == null || activePack.Entries.Count == 0)
        {
            Debug.LogWarning("[BlindBoxService] Loot pack is empty or unassigned.");
            return null;
        }

        ProfileData workingProfile = SaveManager.loadProfile() ?? new ProfileData();
        workingProfile.Sanitize();

        if (!activePack.TryRollSingle(workingProfile, out LootRollResult rollResult, allowGems: allowGems, customRng: null, mutateProfilePity: true))
        {
            Debug.LogWarning("[BlindBoxService] Loot table roll produced no eligible entry.");
            return null;
        }

        var bundle = new RewardBundle();
        bundle.Add(rollResult.Reward);

        var tx = new RewardTransaction
        {
            RequireUniqueRewardId = false,
            RejectIfSkillCapExceeded = false,
            Payout = bundle,
            Reason = "BlindBox",
            OnBeforeSaveMutateProfile = p =>
            {
                p.Sanitize();
                p.lootPackPityKeys.Clear();
                p.lootPackPityCounts.Clear();
                p.lootPackPityKeys.AddRange(workingProfile.lootPackPityKeys);
                p.lootPackPityCounts.AddRange(workingProfile.lootPackPityCounts);
            }
        };

        RewardTransactionResult txResult = RewardService.ExecuteTransaction(tx);
        if (!txResult.Succeeded)
        {
            Debug.LogWarning($"[BlindBoxService] Failed to grant BlindBox bundle: {txResult.Status}");
            return null;
        }

        lastBundle = bundle;
        LastRollResult = rollResult;
        IsOpened = true;
        LastRewardLabel = rollResult.FormattedLabel;

        Debug.Log($"[BlindBoxService] Box opened — rolled '{LastRewardLabel}' (EffectiveRate={rollResult.EffectiveDropRate:0.##}%, HardPity={rollResult.TriggeredHardPity}).");
        return bundle;
    }

    /// <summary>
    /// Watches a rewarded ad; on completion grants the same bundle again.
    /// </summary>
    public void DoubleWithAd(Action<bool> onComplete = null)
    {
        if (!EconomyBalanceConfig.Active.EnablePostLevelBlindBox || !IsOpened || IsDoubled || lastBundle == null)
        {
            onComplete?.Invoke(false);
            return;
        }

        var adService = AdRewardService.Instance != null ? AdRewardService.Instance : FindAnyObjectByType<AdRewardService>();
        if (adService == null || !adService.IsAdAvailable(RewardedAdPlacement.BlindBoxDoubler))
        {
            Debug.LogWarning("[BlindBoxService] Cannot double — ad service unavailable.");
            onComplete?.Invoke(false);
            return;
        }

        adService.WatchAdForReward(RewardedAdPlacement.BlindBoxDoubler, 0, success =>
        {
            if (success)
            {
                IsDoubled = true;
                GrantBundle(lastBundle, "BlindBox_x2");
            }
            onComplete?.Invoke(success);
        });
    }

    private LootPackDefinition ResolveActiveLootPack()
    {
        if (lootPack != null && lootPack.Entries != null && lootPack.Entries.Count > 0)
            return lootPack;

        if (rewardTable == null || rewardTable.Count == 0)
            return null;

        if (runtimeFallbackPack == null)
        {
            runtimeFallbackPack = ScriptableObject.CreateInstance<LootPackDefinition>();
            runtimeFallbackPack.hideFlags = HideFlags.DontSave;
        }

        var convertedEntries = new List<LootEntry>(rewardTable.Count);
        for (int i = 0; i < rewardTable.Count; i++)
        {
            BlindBoxRewardEntry legacy = rewardTable[i];
            if (legacy == null || legacy.weight <= 0)
                continue;

            bool isRare = legacy.weight <= 20 || legacy.type == RewardType.Gems || legacy.type == RewardType.PowerUp;
            convertedEntries.Add(new LootEntry
            {
                entryId = $"blindbox_entry_{i}_{legacy.type}",
                displayName = !string.IsNullOrWhiteSpace(legacy.displayName) ? legacy.displayName : legacy.type.ToString(),
                rewardType = legacy.type,
                itemId = legacy.itemId ?? string.Empty,
                minAmount = Mathf.Max(1, legacy.minAmount),
                maxAmount = Mathf.Max(Mathf.Max(1, legacy.minAmount), legacy.maxAmount),
                baseDropRate = Mathf.Max(0f, legacy.weight),
                enablePity = isRare,
                softPityStartPull = 3,
                softPityBonusPerPull = 5f,
                hardPityGuaranteedPull = legacy.weight <= 10 ? 12 : 8,
                pityPriority = legacy.type == RewardType.Gems ? 2 : 1
            });
        }

        runtimeFallbackPack.Configure("blindbox_default", "Blind Box", convertedEntries, normalizeTo100: true);
        return runtimeFallbackPack;
    }

    private void GrantBundle(RewardBundle bundle, string reason)
    {
        var rewardService = RewardService.Instance != null ? RewardService.Instance : FindAnyObjectByType<RewardService>();
        if (rewardService != null)
            rewardService.GrantRewardBundle(bundle, reason);
        else
            RewardService.ExecuteTransaction(new RewardTransaction
            {
                RequireUniqueRewardId = false,
                RejectIfSkillCapExceeded = false,
                Payout = bundle,
                Reason = reason
            });
    }
}
