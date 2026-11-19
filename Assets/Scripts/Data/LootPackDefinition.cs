using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Configurable entry inside a <see cref="LootPackDefinition"/>.
/// Base drop rates across a pack sum to 100%, and each entry can optionally enable
/// conditional Soft Pity (rate ramp after N misses) and Hard Pity (100% guarantee on pull M).
/// </summary>
[Serializable]
public class LootEntry
{
    [Header("Identity & Visuals")]
    public string entryId = "loot_entry_01";
    public string displayName = "Coins";
    public Sprite icon;

    [Header("Reward Payload")]
    public RewardType rewardType = RewardType.Coins;
    [Tooltip("Skill ID ('breaker', 'freeze', 'teleport') when rewardType is PowerUp, or Cosmetic ID when Cosmetic.")]
    public string itemId = string.Empty;
    [Min(1)] public int minAmount = 10;
    [Min(1)] public int maxAmount = 10;

    [Header("Base Drop Rate (Pack Total = 100%)")]
    [Range(0f, 100f)]
    public float baseDropRate = 25f;

    [Header("Conditional Pity Rules (Per-Item)")]
    [Tooltip("If enabled, consecutive pulls without rolling this entry increase its drop rate and/or guarantee it at Hard Pity.")]
    public bool enablePity;
    [Tooltip("Number of consecutive misses before Soft Pity begins increasing this item's drop rate.")]
    [Min(1)]
    public int softPityStartPull = 3;
    [Tooltip("Additional percentage points (+%) added to this item's drop rate for each pull at or above softPityStartPull.")]
    [Range(0f, 100f)]
    public float softPityBonusPerPull = 5f;
    [Tooltip("Pull number on which this item is 100% guaranteed if not rolled yet (0 = disabled). E.g. 10 means the 10th pull without this item is guaranteed.")]
    [Min(0)]
    public int hardPityGuaranteedPull = 10;
    [Tooltip("Priority when multiple entries reach Hard Pity on the same pull (higher wins first).")]
    public int pityPriority = 0;

    public string EntryId => !string.IsNullOrWhiteSpace(entryId) ? entryId.Trim() : displayName;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : rewardType.ToString();

    public void Sanitize()
    {
        minAmount = Mathf.Max(1, minAmount);
        maxAmount = Mathf.Max(minAmount, maxAmount);
        baseDropRate = Mathf.Clamp(baseDropRate, 0f, 100f);
        softPityStartPull = Mathf.Max(1, softPityStartPull);
        softPityBonusPerPull = Mathf.Clamp(softPityBonusPerPull, 0f, 100f);
        hardPityGuaranteedPull = Mathf.Max(0, hardPityGuaranteedPull);
    }
}

/// <summary>
/// Outcome of a single roll against a <see cref="LootPackDefinition"/>.
/// </summary>
public readonly struct LootRollResult
{
    public LootEntry Entry { get; }
    public int Amount { get; }
    public RewardItem Reward { get; }
    public bool TriggeredHardPity { get; }
    public float EffectiveDropRate { get; }

    public string FormattedLabel =>
        Entry == null
            ? string.Empty
            : (Amount > 1 ? $"{Amount} {Entry.DisplayName}" : Entry.DisplayName);

    public LootRollResult(
        LootEntry entry,
        int amount,
        RewardItem reward,
        bool triggeredHardPity,
        float effectiveDropRate)
    {
        Entry = entry;
        Amount = amount;
        Reward = reward;
        TriggeredHardPity = triggeredHardPity;
        EffectiveDropRate = effectiveDropRate;
    }
}

/// <summary>
/// ScriptableObject defining a random loot pack / mystery box shared by both Shop Packs and BlindBox.
/// Enforces a 100% total base drop rate and supports per-item Soft Pity rate ramping + Hard Pity guarantees
/// with persistent pity counters stored in <see cref="ProfileData"/>.
/// </summary>
[CreateAssetMenu(fileName = "LootPack_", menuName = "Game/Economy/Loot Pack Definition")]
public class LootPackDefinition : ScriptableObject
{
    public const float TotalPercentage = 100f;
    public const float RateTolerance = 0.05f;

    [Header("Pack Identity")]
    [SerializeField] private string packId = "loot_pack_01";
    [SerializeField] private string displayName = "Loot Pack";

    [Header("Loot Table (Base Rates Sum To 100%)")]
    [SerializeField] private List<LootEntry> entries = new List<LootEntry>();

    [Header("Multi-Drop Configuration")]
    [SerializeField, Min(1)] private int minRollsPerOpen = 1;
    [SerializeField, Min(1)] private int maxRollsPerOpen = 1;
    [Tooltip("Avoids rolling duplicate entries inside a single multi-drop open when enough distinct entries exist.")]
    [SerializeField] private bool preferDistinctEntriesPerOpen = false;

    public string PackId => !string.IsNullOrWhiteSpace(packId) ? packId.Trim() : name;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : name;
    public IReadOnlyList<LootEntry> Entries => entries;
    public int MinRollsPerOpen => Mathf.Max(1, minRollsPerOpen);
    public int MaxRollsPerOpen => Mathf.Max(MinRollsPerOpen, maxRollsPerOpen);
    public bool PreferDistinctEntriesPerOpen => preferDistinctEntriesPerOpen;

    public void ConfigureMultiDrop(int minRolls, int maxRolls, bool preferDistinct = true)
    {
        minRollsPerOpen = Mathf.Max(1, minRolls);
        maxRollsPerOpen = Mathf.Max(minRollsPerOpen, maxRolls);
        preferDistinctEntriesPerOpen = preferDistinct;
    }

    private void OnValidate()
    {
        minRollsPerOpen = Mathf.Max(1, minRollsPerOpen);
        maxRollsPerOpen = Mathf.Max(minRollsPerOpen, maxRollsPerOpen);
        if (entries == null || entries.Count == 0)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            entries[i]?.Sanitize();
        }
    }

    public float GetTotalBaseDropRate()
    {
        if (entries == null || entries.Count == 0)
            return 0f;

        float sum = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] != null)
                sum += Mathf.Max(0f, entries[i].baseDropRate);
        }
        return sum;
    }

    public bool AreBaseDropRatesValid(float tolerance = RateTolerance)
    {
        if (entries == null || entries.Count == 0)
            return false;

        return Mathf.Abs(GetTotalBaseDropRate() - TotalPercentage) <= Mathf.Max(0.001f, tolerance);
    }

    /// <summary>
    /// Normalizes all entry baseDropRate values proportionally so their total equals 100%.
    /// </summary>
    [ContextMenu("Normalize Base Drop Rates To 100%")]
    public void NormalizeBaseDropRatesTo100()
    {
        if (entries == null || entries.Count == 0)
            return;

        int validCount = 0;
        float sum = 0f;
        int lastValidIndex = -1;

        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] == null) continue;
            entries[i].Sanitize();
            sum += entries[i].baseDropRate;
            validCount++;
            lastValidIndex = i;
        }

        if (validCount == 0)
            return;

        if (sum <= 0.0001f)
        {
            float equalShare = Mathf.Round((TotalPercentage / validCount) * 100f) / 100f;
            float running = 0f;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i] == null) continue;
                if (i == lastValidIndex)
                    entries[i].baseDropRate = Mathf.Clamp(TotalPercentage - running, 0f, TotalPercentage);
                else
                {
                    entries[i].baseDropRate = equalShare;
                    running += equalShare;
                }
            }
            return;
        }

        float accumulated = 0f;
        for (int i = 0; i < entries.Count; i++)
        {
            if (entries[i] == null) continue;
            if (i == lastValidIndex)
            {
                entries[i].baseDropRate = Mathf.Clamp(
                    Mathf.Round((TotalPercentage - accumulated) * 100f) / 100f,
                    0f,
                    TotalPercentage);
            }
            else
            {
                float normalized = Mathf.Round((entries[i].baseDropRate / sum) * TotalPercentage * 100f) / 100f;
                entries[i].baseDropRate = normalized;
                accumulated += normalized;
            }
        }
    }

    public void Configure(string id, string title, IEnumerable<LootEntry> lootEntries, bool normalizeTo100 = true)
    {
        packId = id;
        displayName = title;
        entries ??= new List<LootEntry>();
        entries.Clear();
        if (lootEntries != null)
        {
            foreach (LootEntry entry in lootEntries)
            {
                if (entry != null)
                {
                    entry.Sanitize();
                    entries.Add(entry);
                }
            }
        }

        if (normalizeTo100)
            NormalizeBaseDropRatesTo100();
    }

    /// <summary>
    /// Computes the current effective drop rate (0..100%, summing to 100%) for each entry in <see cref="Entries"/>
    /// given the player's saved pity counters in <paramref name="profile"/>.
    /// </summary>
    public float[] ComputeEffectiveDropRates(ProfileData profile, out int hardPityWinnerIndex, bool allowGems = true)
    {
        hardPityWinnerIndex = -1;
        if (entries == null || entries.Count == 0)
            return Array.Empty<float>();

        int count = entries.Count;
        float[] rates = new float[count];
        bool[] eligible = new bool[count];
        int eligibleCount = 0;

        for (int i = 0; i < count; i++)
        {
            LootEntry e = entries[i];
            if (e == null)
                continue;
            if (!allowGems && e.rewardType == RewardType.Gems)
                continue;

            eligible[i] = true;
            eligibleCount++;
        }

        if (eligibleCount == 0)
            return rates;

        // 1. Check Hard Pity: next pull is (currentPity + 1). If (currentPity + 1) >= hardPityGuaranteedPull,
        //    the item with highest pityPriority (tie-breaker: most overdue pulls) wins 100%.
        int bestPriority = int.MinValue;
        int bestOverdue = int.MinValue;

        for (int i = 0; i < count; i++)
        {
            if (!eligible[i]) continue;
            LootEntry e = entries[i];
            if (!e.enablePity || e.hardPityGuaranteedPull <= 0)
                continue;

            int currentPity = profile != null ? profile.GetLootPackPity(PackId, e.EntryId) : 0;
            int nextPullNumber = currentPity + 1;
            if (nextPullNumber >= e.hardPityGuaranteedPull)
            {
                int overdue = nextPullNumber - e.hardPityGuaranteedPull;
                if (hardPityWinnerIndex < 0
                    || e.pityPriority > bestPriority
                    || (e.pityPriority == bestPriority && overdue > bestOverdue))
                {
                    hardPityWinnerIndex = i;
                    bestPriority = e.pityPriority;
                    bestOverdue = overdue;
                }
            }
        }

        if (hardPityWinnerIndex >= 0)
        {
            rates[hardPityWinnerIndex] = TotalPercentage;
            return rates;
        }

        // 2. Compute Soft Pity bonuses for entries that have reached softPityStartPull
        bool[] isBoosted = new bool[count];
        float boostedTotal = 0f;
        float nonBoostedBaseTotal = 0f;

        for (int i = 0; i < count; i++)
        {
            if (!eligible[i]) continue;
            LootEntry e = entries[i];
            float baseRate = Mathf.Max(0f, e.baseDropRate);
            int currentPity = profile != null ? profile.GetLootPackPity(PackId, e.EntryId) : 0;

            if (e.enablePity && e.softPityBonusPerPull > 0f && currentPity >= e.softPityStartPull)
            {
                int softPulls = currentPity - e.softPityStartPull + 1;
                float boosted = baseRate + (softPulls * e.softPityBonusPerPull);
                rates[i] = boosted;
                isBoosted[i] = true;
                boostedTotal += boosted;
            }
            else
            {
                rates[i] = baseRate;
                nonBoostedBaseTotal += baseRate;
            }
        }

        // 3. Normalize so the total effective probability across eligible entries is strictly 100%
        if (boostedTotal >= TotalPercentage || nonBoostedBaseTotal <= 0.0001f)
        {
            float activeSum = boostedTotal > 0.0001f ? boostedTotal : nonBoostedBaseTotal;
            if (activeSum <= 0.0001f)
            {
                float equal = TotalPercentage / eligibleCount;
                for (int i = 0; i < count; i++)
                {
                    if (eligible[i])
                        rates[i] = equal;
                }
                return rates;
            }

            for (int i = 0; i < count; i++)
            {
                if (!eligible[i])
                {
                    rates[i] = 0f;
                    continue;
                }

                if (boostedTotal >= TotalPercentage && !isBoosted[i])
                {
                    rates[i] = 0f;
                }
                else
                {
                    rates[i] = (rates[i] / activeSum) * TotalPercentage;
                }
            }
            return rates;
        }

        float remainingPercentage = TotalPercentage - boostedTotal;
        for (int i = 0; i < count; i++)
        {
            if (!eligible[i])
            {
                rates[i] = 0f;
                continue;
            }

            if (!isBoosted[i])
            {
                rates[i] = (rates[i] / nonBoostedBaseTotal) * remainingPercentage;
            }
        }

        return rates;
    }

    /// <summary>
    /// Rolls a single item from this pack, updating the saved pity counters in <paramref name="profile"/> when
    /// <paramref name="mutateProfilePity"/> is true.
    /// </summary>
    public bool TryRollSingle(
        ProfileData profile,
        out LootRollResult rollResult,
        bool allowGems = true,
        System.Random customRng = null,
        bool mutateProfilePity = true)
    {
        rollResult = default;
        if (entries == null || entries.Count == 0)
            return false;

        float[] effectiveRates = ComputeEffectiveDropRates(profile, out int hardPityWinnerIndex, allowGems);
        int selectedIndex = hardPityWinnerIndex;

        if (selectedIndex < 0)
        {
            float totalRate = 0f;
            for (int i = 0; i < effectiveRates.Length; i++)
                totalRate += Mathf.Max(0f, effectiveRates[i]);

            if (totalRate <= 0.0001f)
                return false;

            float sample = customRng != null
                ? (float)(customRng.NextDouble() * totalRate)
                : UnityEngine.Random.Range(0f, totalRate);

            for (int i = 0; i < effectiveRates.Length; i++)
            {
                float r = Mathf.Max(0f, effectiveRates[i]);
                if (r <= 0f) continue;

                sample -= r;
                if (sample <= 0f)
                {
                    selectedIndex = i;
                    break;
                }
            }

            if (selectedIndex < 0)
            {
                for (int i = effectiveRates.Length - 1; i >= 0; i--)
                {
                    if (effectiveRates[i] > 0f)
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
        }

        if (selectedIndex < 0 || selectedIndex >= entries.Count || entries[selectedIndex] == null)
            return false;

        LootEntry chosen = entries[selectedIndex];
        chosen.Sanitize();

        int amount = chosen.minAmount == chosen.maxAmount
            ? chosen.minAmount
            : (customRng != null
                ? customRng.Next(chosen.minAmount, chosen.maxAmount + 1)
                : UnityEngine.Random.Range(chosen.minAmount, chosen.maxAmount + 1));

        if (mutateProfilePity && profile != null)
        {
            ApplyPityMutationForWinner(profile, selectedIndex);
        }

        var rewardItem = new RewardItem(chosen.rewardType, amount, chosen.itemId);
        rollResult = new LootRollResult(
            chosen,
            amount,
            rewardItem,
            hardPityWinnerIndex == selectedIndex,
            effectiveRates[selectedIndex]);
        return true;
    }

    /// <summary>
    /// Rolls <paramref name="rollsCount"/> times and returns the aggregated <see cref="RewardBundle"/>.
    /// </summary>
    public RewardBundle RollBundle(
        ProfileData profile,
        out List<LootRollResult> rollResults,
        int rollsCount = 1,
        bool allowGems = true,
        System.Random customRng = null,
        bool mutateProfilePity = true,
        bool preferDistinct = false)
    {
        rollResults = new List<LootRollResult>();
        var bundle = new RewardBundle();
        int safeRolls = Mathf.Max(1, rollsCount);
        var seenEntryIds = preferDistinct ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) : null;
        int distinctAvailable = 0;
        if (preferDistinct && entries != null)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                LootEntry e = entries[i];
                if (e != null && (allowGems || e.rewardType != RewardType.Gems))
                    distinctAvailable++;
            }
        }

        for (int r = 0; r < safeRolls; r++)
        {
            LootRollResult chosen = default;
            bool rolled = false;
            int maxAttempts = (preferDistinct && seenEntryIds != null && seenEntryIds.Count < distinctAvailable) ? 8 : 1;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                // On retry attempts, defer pity mutation until the accepted pick so intermediate rejected rolls do not advance pity twice.
                bool shouldMutate = mutateProfilePity && attempt == maxAttempts - 1;
                if (!TryRollSingle(profile, out LootRollResult candidate, allowGems, customRng, shouldMutate))
                    continue;

                string candEntryId = candidate.Entry != null ? candidate.Entry.EntryId : string.Empty;
                if (seenEntryIds == null || (!string.IsNullOrEmpty(candEntryId) && seenEntryIds.Add(candEntryId)) || attempt == maxAttempts - 1)
                {
                    if (mutateProfilePity && !shouldMutate && entries != null && !string.IsNullOrEmpty(candEntryId))
                    {
                        for (int idx = 0; idx < entries.Count; idx++)
                        {
                            if (entries[idx] != null && string.Equals(entries[idx].EntryId, candEntryId, StringComparison.OrdinalIgnoreCase))
                            {
                                ApplyPityMutationForWinner(profile, idx);
                                break;
                            }
                        }
                    }

                    chosen = candidate;
                    rolled = true;
                    break;
                }
            }

            if (rolled)
            {
                rollResults.Add(chosen);
                bundle.Add(chosen.Reward);
            }
        }

        return bundle;
    }

    /// <summary>
    /// Rolls a random count within [<see cref="MinRollsPerOpen"/>, <see cref="MaxRollsPerOpen"/>]
    /// respecting <see cref="PreferDistinctEntriesPerOpen"/>.
    /// </summary>
    public RewardBundle RollConfiguredBundle(
        ProfileData profile,
        out List<LootRollResult> rollResults,
        bool allowGems = true,
        System.Random customRng = null,
        bool mutateProfilePity = true)
    {
        int min = MinRollsPerOpen;
        int max = MaxRollsPerOpen;
        int rollsCount = min == max
            ? min
            : (customRng != null ? customRng.Next(min, max + 1) : UnityEngine.Random.Range(min, max + 1));

        return RollBundle(profile, out rollResults, rollsCount, allowGems, customRng, mutateProfilePity, preferDistinctEntriesPerOpen);
    }

    public void ApplyPityMutationForWinner(ProfileData profile, int winningEntryIndex)
    {
        if (profile == null || entries == null)
            return;

        for (int i = 0; i < entries.Count; i++)
        {
            LootEntry entry = entries[i];
            if (entry == null)
                continue;

            if (i == winningEntryIndex)
            {
                if (entry.enablePity)
                    profile.ResetLootPackPity(PackId, entry.EntryId);
            }
            else if (entry.enablePity)
            {
                int current = profile.GetLootPackPity(PackId, entry.EntryId);
                profile.SetLootPackPity(PackId, entry.EntryId, current + 1);
            }
        }
    }
}
