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

    /// <summary>Fired after the daily mystery box claim state commits to ProfileData.</summary>
    public static event Action OnDailyBoxStateChanged;

    [Header("Loot Pack ScriptableObject (Shared with Shop Pack Logic)")]
    [Tooltip("ScriptableObject defining drop rates (sum = 100%) and per-item Soft/Hard Pity rules.")]
    [SerializeField] private LootPackDefinition lootPack;

    [Header("Fallback Loot Table (Used when lootPack is unassigned)")]
    [SerializeField] private List<BlindBoxRewardEntry> rewardTable = new List<BlindBoxRewardEntry>
    {
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 40, maxAmount = 60,  weight = 32, displayName = "40-60 Coins" },
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 90, maxAmount = 130, weight = 20, displayName = "90-130 Coins" },
        new BlindBoxRewardEntry { type = RewardType.Gems,    minAmount = 4,  maxAmount = 6,   weight = 15, displayName = "4-6 Gems" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Breaker,  minAmount = 1, maxAmount = 1, weight = 10, displayName = "1x Kinetic Surge" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Freeze,   minAmount = 1, maxAmount = 1, weight = 8,  displayName = "1x Time Dilation" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Teleport, minAmount = 1, maxAmount = 1, weight = 6,  displayName = "1x Phase Shift" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Shield,   minAmount = 1, maxAmount = 1, weight = 4,  displayName = "1x Inertia Core" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Magnet,   minAmount = 1, maxAmount = 1, weight = 3,  displayName = "1x Gravity Lens" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.Echo,     minAmount = 1, maxAmount = 1, weight = 2,  displayName = "1x Temporal Anchor" },
    };

    /// <summary>True once the current box has been opened.</summary>
    public bool IsOpened { get; private set; }

    /// <summary>True once the rolled reward has already been doubled via ad.</summary>
    public bool IsDoubled { get; private set; }

    /// <summary>Human-readable label of the last rolled reward(s).</summary>
    public string LastRewardLabel { get; private set; } = "";

    /// <summary>Primary details of the most recent roll (first item when multi-drop).</summary>
    public LootRollResult LastRollResult { get; private set; }

    /// <summary>All items rolled in the most recent box open.</summary>
    public IReadOnlyList<LootRollResult> LastRollResults => lastRollResults;

    public LootPackDefinition LootPack
    {
        get => lootPack;
        set => lootPack = value;
    }

    private readonly List<LootRollResult> lastRollResults = new List<LootRollResult>(5);
    private RewardBundle lastBundle;
    private LootPackDefinition runtimeFallbackPack;
    private bool openedFromMenu;

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
            if (Application.isPlaying)
                Destroy(runtimeFallbackPack);
            else
                DestroyImmediate(runtimeFallbackPack);
            runtimeFallbackPack = null;
        }
    }

    /// <summary>Resets the box so the next completion can open a fresh one.</summary>
    public void ResetBox()
    {
        IsOpened = false;
        IsDoubled = false;
        openedFromMenu = false;
        lastBundle = null;
        LastRewardLabel = "";
        LastRollResult = default;
        lastRollResults.Clear();
    }

    /// <summary>
    /// Returns true when the player has not yet claimed today's local-calendar daily Mystery Box
    /// (enforcing the monotonic anti-clock-rollback guard via <see cref="LocalClock"/>).
    /// </summary>
    public static bool CanClaimDailyBox(ProfileData profile = null)
    {
        ProfileData target = profile ?? SaveManager.loadProfile() ?? new ProfileData();
        target.Sanitize();

        LocalClock.EvaluateClockAndUpdateHighWater(target, LocalClock.Now, out DateTime effectiveNow, out bool isRolledBack);
        if (isRolledBack && !string.IsNullOrEmpty(target.lastBlindBoxClaimDateKey))
            return false;

        string todayKey = LocalClock.FormatDateKey(effectiveNow);
        return !string.Equals(target.lastBlindBoxClaimDateKey, todayKey, StringComparison.Ordinal);
    }

    /// <summary>
    /// Rolls the shared <see cref="LootPackDefinition"/> (with Soft/Hard Pity and <see cref="ProfileData"/> persistence)
    /// and grants the reward atomically through <see cref="RewardService"/>.
    /// </summary>
    public RewardBundle Open(bool allowDirectMenuOpen = false)
    {
        bool configEnabled = EconomyBalanceConfig.Active.EnablePostLevelBlindBox;
        if (!configEnabled && !allowDirectMenuOpen)
            return null;

        if (IsOpened)
            return null;

        ProfileData workingProfile = SaveManager.loadProfile() ?? new ProfileData();
        workingProfile.Sanitize();

        if (allowDirectMenuOpen && !CanClaimDailyBox(workingProfile))
            return null;

        LootPackDefinition activePack = ResolveActiveLootPack();
        if (activePack == null || activePack.Entries == null || activePack.Entries.Count == 0)
        {
            Debug.LogWarning("[BlindBoxService] Loot pack is empty or unassigned.");
            return null;
        }

        bool allowGems = configEnabled || allowDirectMenuOpen;
        RewardBundle bundle = activePack.RollConfiguredBundle(workingProfile, out List<LootRollResult> rollResults, allowGems: allowGems, customRng: null, mutateProfilePity: true);
        if (bundle == null || bundle.IsEmpty || rollResults == null || rollResults.Count == 0)
        {
            Debug.LogWarning("[BlindBoxService] Loot table roll produced no eligible entry.");
            return null;
        }

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

                if (allowDirectMenuOpen)
                {
                    LocalClock.EvaluateClockAndUpdateHighWater(p, LocalClock.Now, out DateTime claimNow, out _);
                    p.lastBlindBoxClaimDateKey = LocalClock.FormatDateKey(claimNow);
                }
            }
        };

        RewardTransactionResult txResult = RewardService.ExecuteTransaction(tx);
        if (!txResult.Succeeded)
        {
            Debug.LogWarning($"[BlindBoxService] Failed to grant BlindBox bundle: {txResult.Status}");
            return null;
        }

        lastBundle = bundle;
        lastRollResults.Clear();
        lastRollResults.AddRange(rollResults);
        LastRollResult = rollResults[0];
        IsOpened = true;
        openedFromMenu = allowDirectMenuOpen;

        var labels = new List<string>(rollResults.Count);
        for (int i = 0; i < rollResults.Count; i++)
        {
            if (!string.IsNullOrEmpty(rollResults[i].FormattedLabel))
                labels.Add(rollResults[i].FormattedLabel);
        }
        LastRewardLabel = string.Join(", ", labels);

        if (allowDirectMenuOpen)
            OnDailyBoxStateChanged?.Invoke();

        Debug.Log($"[BlindBoxService] Box opened ({rollResults.Count} drops) — rolled '{LastRewardLabel}'.");
        return bundle;
    }

    /// <summary>
    /// Watches a rewarded ad; on completion grants the same bundle again.
    /// </summary>
    public void DoubleWithAd(Action<bool> onComplete = null)
    {
        if ((!EconomyBalanceConfig.Active.EnablePostLevelBlindBox && !openedFromMenu) || !IsOpened || IsDoubled || lastBundle == null)
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
        runtimeFallbackPack.ConfigureMultiDrop(3, 4, preferDistinct: true);
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
