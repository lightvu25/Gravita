using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages campaign progression (unlocked/completed levels, stars, best moves, sectors),
/// Power Skill charge caps (Breaker=12, Freeze=12, Teleport=8), one-time tutorial grants,
/// and the 4-slot pre-level loadout system (locked during active gameplay per GDD §5.5).
/// </summary>
public class ProgressionManager : MonoBehaviour
{
    public const int MaxLoadoutSlotCount = 4;

    public static ProgressionManager Instance { get; private set; }

    [Header("Campaign Database")]
    [Tooltip("Data-driven definition of campaign worlds, levels, and EXP curve.")]
    [SerializeField] private CampaignDatabase campaignDatabase;

    [Header("Player EXP Config (Optional Override)")]
    [Tooltip("Optional override for Player EXP curve. If unassigned, uses campaignDatabase.ExpConfig or default formula.")]
    [SerializeField] private PlayerExpConfig expConfigOverride;

    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    public event Action<LevelData> OnLevelUnlocked;
    public event Action OnProgressionChanged;
    public event Action OnLoadoutChanged;
    /// <summary>Fired when the player gains EXP: (totalExp, expAdded).</summary>
    public event Action<int, int> OnPlayerExpChanged;
    /// <summary>Fired when the player levels up: (newPlayerLevel, previousPlayerLevel).</summary>
    public event Action<int, int> OnPlayerLevelUp;

    /// <summary>
    /// Static analytics event for Power Skill inventory changes:
    /// (skillId, signedDelta, remainingCount, cap, actionReason).
    /// </summary>
    public static event Action<string, int, int, int, string> OnSkillInventoryChanged;
    public event Action<string, int> OnPowerUpUpgraded;

    // ─────────────────────────────────────────────────────────────
    //  Public Properties
    // ─────────────────────────────────────────────────────────────

    public CampaignDatabase Database => campaignDatabase;
    public PlayerExpConfig ExpConfig => expConfigOverride != null
        ? expConfigOverride
        : (campaignDatabase != null ? campaignDatabase.ExpConfig : null);

    private ProfileData fallbackProfile;
    private bool isLoadoutLockedForRun;
    private readonly List<string> lockedRunLoadout = new List<string>(MaxLoadoutSlotCount);
    private readonly Dictionary<string, int> lockedRunLevels = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public bool IsLoadoutLocked => isLoadoutLockedForRun;

    public void SetDatabase(CampaignDatabase db)
    {
        campaignDatabase = db;
        OnProgressionChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  Player Level & EXP Properties
    // ─────────────────────────────────────────────────────────────

    public int TotalExp
    {
        get
        {
            var profile = GetProfile();
            return profile != null ? Mathf.Max(0, profile.playerExp) : 0;
        }
    }

    public int PlayerLevel
    {
        get
        {
            EvaluatePlayerExpState(out int playerLevel, out _, out _, out _);
            return playerLevel;
        }
    }

    public int CurrentLevelExp
    {
        get
        {
            EvaluatePlayerExpState(out _, out int currentLevelExp, out _, out _);
            return currentLevelExp;
        }
    }

    public int ExpToNextLevel
    {
        get
        {
            EvaluatePlayerExpState(out _, out _, out int expForNextLevel, out _);
            return expForNextLevel;
        }
    }

    public float ExpProgressNormalized
    {
        get
        {
            EvaluatePlayerExpState(out _, out _, out _, out float normalized);
            return normalized;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        SyncProfileLevelWithExp();
    }

    private void Start()
    {
        OnProgressionChanged?.Invoke();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Player EXP API
    // ─────────────────────────────────────────────────────────────

    public void EvaluatePlayerExpState(
        out int playerLevel,
        out int currentLevelExp,
        out int expForNextLevel,
        out float normalizedProgress)
    {
        int totalExp = TotalExp;
        if (expConfigOverride != null)
        {
            expConfigOverride.EvaluateTotalExp(
                totalExp,
                out playerLevel,
                out currentLevelExp,
                out expForNextLevel,
                out normalizedProgress);
        }
        else if (campaignDatabase != null)
        {
            campaignDatabase.EvaluatePlayerExp(
                totalExp,
                out playerLevel,
                out currentLevelExp,
                out expForNextLevel,
                out normalizedProgress);
        }
        else
        {
            PlayerExpConfig.EvaluateTotalExp(
                totalExp,
                PlayerExpConfig.DefaultMaxPlayerLevel,
                PlayerExpConfig.DefaultBaseExpPerLevel,
                PlayerExpConfig.DefaultExpIncreasePerLevel,
                1f,
                null,
                out playerLevel,
                out currentLevelExp,
                out expForNextLevel,
                out normalizedProgress);
        }

        var profile = GetProfile();
        if (profile != null && profile.playerLevel > playerLevel)
        {
            playerLevel = profile.playerLevel;
        }
    }

    public void AddExp(int amount, string source = "")
    {
        if (amount <= 0) return;

        ProfileData profile = SaveManager.loadProfile() ?? GetProfile() ?? new ProfileData();
        profile.Sanitize();

        EvaluatePlayerExpState(out int previousLevel, out _, out _, out _);

        profile.playerExp = Mathf.Max(0, profile.playerExp + amount);

        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = profile;
        fallbackProfile = profile;

        EvaluatePlayerExpState(out int newLevel, out int curExp, out int reqExp, out _);
        profile.playerLevel = Mathf.Max(profile.playerLevel, newLevel);

        SaveManager.saveProfile(profile);

        OnPlayerExpChanged?.Invoke(profile.playerExp, amount);
        if (profile.playerLevel > previousLevel)
        {
            OnPlayerLevelUp?.Invoke(profile.playerLevel, previousLevel);
        }
        OnProgressionChanged?.Invoke();
    }

    private void SyncProfileLevelWithExp()
    {
        var profile = GetProfile();
        if (profile == null) return;

        EvaluatePlayerExpState(out int calculatedLevel, out _, out _, out _);
        if (calculatedLevel > profile.playerLevel)
        {
            profile.playerLevel = calculatedLevel;
            SaveManager.saveProfile(profile);
        }
    }

    internal void NotifyExternalAtomicProfileCommit(Dictionary<string, int> skillDeltas, string reason)
    {
        var profile = GetProfile();
        if (profile != null)
        {
            EvaluatePlayerExpState(out int calculatedLevel, out _, out _, out _);
            if (calculatedLevel > profile.playerLevel)
            {
                profile.playerLevel = calculatedLevel;
                SaveManager.saveProfile(profile);
            }
        }

        if (skillDeltas != null && profile != null)
        {
            foreach (KeyValuePair<string, int> kv in skillDeltas)
            {
                if (kv.Value != 0)
                {
                    int remaining = GetPowerUpCount(kv.Key);
                    int cap = GetPowerUpCap(kv.Key);
                    OnSkillInventoryChanged?.Invoke(kv.Key, kv.Value, remaining, cap, reason ?? "Transaction");
                }
            }
        }

        OnProgressionChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  Sector & Level Query API (No-Paywall Sequential Unlock — GDD §2.1 & §4)
    // ─────────────────────────────────────────────────────────────

    public bool IsWorldUnlocked(WorldData world)
    {
        if (world == null) return false;

        ProfileData profile = GetProfile();
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;

        // Sequential Sector unlocking (GDD §4: Sector 1 unlocked at start; Sector N unlocked when Sector N-1 is completed)
        if (campaignDatabase != null && campaignDatabase.Worlds != null && campaignDatabase.Worlds.Count > 0)
        {
            int idx = -1;
            for (int i = 0; i < campaignDatabase.Worlds.Count; i++)
            {
                if (campaignDatabase.Worlds[i] == world)
                {
                    idx = i;
                    break;
                }
            }

            if (idx == 0)
                return true;

            if (idx > 0 && balance.BypassPlayerLevelAndExpWorldLocks)
            {
                if (world.requiredCompletedLevelsToUnlock > 0)
                {
                    return GetCompletedLevelCount() >= world.requiredCompletedLevelsToUnlock;
                }

                WorldData previousWorld = campaignDatabase.Worlds[idx - 1];
                return CampaignDatabase.AreAllLevelsInWorldCompleted(previousWorld, profile);
            }
        }

        if (world.requiredCompletedLevelsToUnlock > 0)
        {
            int completedCount = GetCompletedLevelCount();
            if (completedCount < world.requiredCompletedLevelsToUnlock)
                return false;
        }

        if (!balance.BypassPlayerLevelAndExpWorldLocks)
        {
            if (world.requiredPlayerLevelToUnlock > 1 && PlayerLevel < world.requiredPlayerLevelToUnlock)
                return false;

            if (world.requiredExpToUnlock > 0 && TotalExp < world.requiredExpToUnlock)
                return false;
        }

        return true;
    }

    public string GetWorldUnlockRequirementText(WorldData world)
    {
        if (world == null || IsWorldUnlocked(world))
            return string.Empty;

        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        var reasons = new List<string>(3);

        if (!balance.BypassPlayerLevelAndExpWorldLocks)
        {
            if (world.requiredPlayerLevelToUnlock > 1 && PlayerLevel < world.requiredPlayerLevelToUnlock)
                reasons.Add($"LVL {world.requiredPlayerLevelToUnlock}");

            if (world.requiredExpToUnlock > 0 && TotalExp < world.requiredExpToUnlock)
                reasons.Add($"{world.requiredExpToUnlock} EXP");
        }

        if (world.requiredCompletedLevelsToUnlock > 0 && GetCompletedLevelCount() < world.requiredCompletedLevelsToUnlock)
        {
            reasons.Add($"{world.requiredCompletedLevelsToUnlock} Stages");
        }
        else if (campaignDatabase != null)
        {
            int sectorNum = campaignDatabase.GetOneBasedWorldIndex(world);
            if (sectorNum > 1)
                reasons.Add($"Clear Sector {sectorNum - 1}");
        }

        return reasons.Count > 0 ? $"Req: {string.Join(" | ", reasons)}" : "Locked";
    }

    public int GetHighestUnlockedSectorNumber()
    {
        if (campaignDatabase == null || campaignDatabase.Worlds == null || campaignDatabase.Worlds.Count == 0)
            return 1;

        int highest = 1;
        for (int i = 0; i < campaignDatabase.Worlds.Count; i++)
        {
            if (IsWorldUnlocked(campaignDatabase.Worlds[i]))
                highest = i + 1;
        }
        return highest;
    }

    public bool IsSectorCompleted(int oneBasedSectorNumber)
    {
        ProfileData profile = GetProfile();
        if (profile == null) return false;

        if (profile.HasClaimedReward(EconomyRewardIds.SectorCompletion(oneBasedSectorNumber)))
            return true;

        if (campaignDatabase?.Worlds != null &&
            oneBasedSectorNumber >= 1 &&
            oneBasedSectorNumber <= campaignDatabase.Worlds.Count)
        {
            return CampaignDatabase.AreAllLevelsInWorldCompleted(campaignDatabase.Worlds[oneBasedSectorNumber - 1], profile);
        }

        return false;
    }

    public bool IsLevelCompleted(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return false;

        var profile = GetProfile();
        return profile != null && profile.completedLevelIDs != null && profile.completedLevelIDs.Contains(level.LevelId);
    }

    public bool IsLevelUnlocked(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return false;

        if (campaignDatabase != null && campaignDatabase.GetFirstLevel() == level)
            return true;

        var profile = GetProfile();
        if (profile != null && profile.unlockedLevelIDs != null && profile.unlockedLevelIDs.Contains(level.LevelId))
            return true;

        return IsLevelCompleted(level);
    }

    public int GetLevelStars(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return 0;
        return GetLevelStars(level.LevelId);
    }

    public int GetLevelStars(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return 0;
        var profile = GetProfile();
        return profile != null ? profile.GetLevelStars(levelId) : 0;
    }

    public int GetBestMoves(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return 0;
        var profile = GetProfile();
        return profile != null ? profile.GetBestMoves(levelId) : 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Power Skill Inventory & Caps (GDD §5.1–§5.4)
    // ─────────────────────────────────────────────────────────────

    public int GetPowerUpCap(string powerUpId)
    {
        return EconomyBalanceConfig.Active.GetSkillInventoryCap(powerUpId);
    }

    public bool IsPowerUpAtCap(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return true;
        return GetPowerUpCount(powerUpId) >= GetPowerUpCap(powerUpId);
    }

    public bool CanAddPowerUpWithoutExceedingCap(string powerUpId, int amount = 1)
    {
        if (string.IsNullOrEmpty(powerUpId) || amount <= 0) return false;
        return GetPowerUpCount(powerUpId) + amount <= GetPowerUpCap(powerUpId);
    }

    public int GetPowerUpCount(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return 0;
        var profile = GetProfile();
        if (profile == null || profile.powerUpIds == null || profile.powerUpCounts == null) return 0;

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        return (idx >= 0 && idx < profile.powerUpCounts.Count) ? Mathf.Max(0, profile.powerUpCounts[idx]) : 0;
    }

    public void AddPowerUp(string powerUpId, int count = 1, string reason = "Grant")
    {
        if (string.IsNullOrEmpty(powerUpId) || count <= 0) return;
        var profile = SaveManager.loadProfile() ?? GetProfile();
        if (profile == null) return;

        profile.Sanitize();

        int cap = GetPowerUpCap(powerUpId);
        int current = GetPowerUpCount(powerUpId);
        int nextCount = Mathf.Clamp(current + count, 0, cap);
        int addedDelta = nextCount - current;
        if (addedDelta <= 0)
        {
            Debug.Log($"[ProgressionManager] Skill '{powerUpId}' is already at cap ({current}/{cap}); 0 charges added.");
            return;
        }

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        if (idx >= 0 && idx < profile.powerUpCounts.Count)
            profile.powerUpCounts[idx] = nextCount;
        else
        {
            profile.powerUpIds.Add(powerUpId);
            profile.powerUpCounts.Add(nextCount);
        }

        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = profile;
        fallbackProfile = profile;

        SaveManager.saveProfile(profile);
        OnSkillInventoryChanged?.Invoke(powerUpId, addedDelta, nextCount, cap, reason);
        OnProgressionChanged?.Invoke();
    }

    /// <summary>Spends one charge of the power-up. Returns false when none are banked.</summary>
    public bool ConsumePowerUp(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return false;
        var profile = GetProfile();
        if (profile == null || profile.powerUpIds == null || profile.powerUpCounts == null) return false;

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        if (idx < 0 || idx >= profile.powerUpCounts.Count || profile.powerUpCounts[idx] <= 0) return false;

        profile.powerUpCounts[idx]--;
        int remaining = profile.powerUpCounts[idx];
        int cap = GetPowerUpCap(powerUpId);

        SaveManager.saveProfile(profile);
        OnSkillInventoryChanged?.Invoke(powerUpId, -1, remaining, cap, "SkillUsed");
        OnProgressionChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Grants the one-time initial tutorial charges for a newly introduced Power Skill (GDD §5.2).
    /// Idempotent across replays and app restarts via <see cref="EconomyRewardIds.TutorialSkillGrant"/>.
    /// </summary>
    public RewardTransactionResult TryGrantTutorialSkillCharges(string skillId)
    {
        if (string.IsNullOrEmpty(skillId))
            return new RewardTransactionResult(RewardTransactionStatus.InvalidRequest, "Skill ID is empty.");

        if (!EconomyBalanceConfig.Active.IsSkillActiveInCampaign(skillId))
            return new RewardTransactionResult(RewardTransactionStatus.InvalidRequest, $"Skill '{skillId}' is not active in Campaign.");

        int initialCharges = EconomyBalanceConfig.Active.GetInitialTutorialCharges(skillId);
        var bundle = new RewardBundle();
        if (initialCharges > 0)
            bundle.Add(RewardType.PowerUp, initialCharges, skillId);

        var tx = new RewardTransaction
        {
            RewardId = EconomyRewardIds.TutorialSkillGrant(skillId),
            RequireUniqueRewardId = true,
            RejectIfSkillCapExceeded = false, // Clamped safely to cap if player somehow already bought charges
            Payout = bundle,
            Reason = $"TutorialSkillUnlock_{skillId}",
            OnBeforeSaveMutateProfile = p =>
            {
                if (!p.unlockedSkillIDs.Contains(skillId))
                    p.unlockedSkillIDs.Add(skillId);
            }
        };

        return RewardService.ExecuteTransaction(tx);
    }

    // ─────────────────────────────────────────────────────────────
    //  4-Slot Pre-Level Loadout System (GDD §5.5)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the 4 loadout slot skill IDs. During an active level run, returns the locked snapshot.
    /// Empty slots are represented as <see cref="string.Empty"/>.
    /// </summary>
    public IReadOnlyList<string> GetLoadoutSlots()
    {
        if (isLoadoutLockedForRun && lockedRunLoadout.Count == MaxLoadoutSlotCount)
            return lockedRunLoadout;

        ProfileData profile = GetProfile();
        EnsureNormalizedLoadoutOnProfile(profile);
        return profile != null ? profile.loadoutSlots : Array.Empty<string>();
    }

    public bool IsSkillEquippedInLoadout(string skillId)
    {
        if (string.IsNullOrEmpty(skillId))
            return false;

        IReadOnlyList<string> slots = GetLoadoutSlots();
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i], skillId, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Replaces the entire 4-slot loadout before a level starts.
    /// Rejects changes while <see cref="IsLoadoutLocked"/> is true, rejects > 4 slots,
    /// rejects inactive skills, and rejects duplicate non-empty skills across slots (GDD §5.5).
    /// </summary>
    public bool TrySetLoadout(IList<string> desiredSlots)
    {
        if (isLoadoutLockedForRun)
        {
            Debug.LogWarning("[ProgressionManager] Cannot modify Power Skill loadout while a level is in progress.");
            return false;
        }

        if (desiredSlots == null || desiredSlots.Count > MaxLoadoutSlotCount)
            return false;

        var normalized = new List<string>(MaxLoadoutSlotCount);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;

        for (int i = 0; i < MaxLoadoutSlotCount; i++)
        {
            string entry = (i < desiredSlots.Count ? desiredSlots[i] : string.Empty)?.Trim() ?? string.Empty;
            if (!string.IsNullOrEmpty(entry))
            {
                if (!balance.IsSkillActiveInCampaign(entry))
                    return false;

                if (!seen.Add(entry))
                    return false; // Duplicate skill across slots is prohibited (GDD §5.5)
            }
            normalized.Add(entry);
        }

        ProfileData profile = GetProfile();
        if (profile == null) return false;

        profile.loadoutSlots.Clear();
        profile.loadoutSlots.AddRange(normalized);

        if (!SaveManager.TrySaveProfile(profile))
            return false;

        OnLoadoutChanged?.Invoke();
        OnProgressionChanged?.Invoke();
        return true;
    }

    /// <summary>
    /// Interactive UI helper: assigns <paramref name="skillId"/> (or <c>""</c> to clear) to <paramref name="slotIndex"/>.
    /// If <paramref name="skillId"/> is already in another slot, moves/swaps it instead of duplicating it.
    /// </summary>
    public bool TryAssignLoadoutSlot(int slotIndex, string skillId)
    {
        if (isLoadoutLockedForRun || slotIndex < 0 || slotIndex >= MaxLoadoutSlotCount)
            return false;

        string normalizedSkill = skillId?.Trim() ?? string.Empty;
        if (!string.IsNullOrEmpty(normalizedSkill) && !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(normalizedSkill))
            return false;

        ProfileData profile = GetProfile();
        if (profile == null) return false;
        EnsureNormalizedLoadoutOnProfile(profile);

        var next = new List<string>(profile.loadoutSlots);
        if (!string.IsNullOrEmpty(normalizedSkill))
        {
            for (int i = 0; i < next.Count; i++)
            {
                if (i != slotIndex && string.Equals(next[i], normalizedSkill, StringComparison.OrdinalIgnoreCase))
                {
                    // Swap with current occupant of target slot so no skill is ever duplicated
                    next[i] = next[slotIndex];
                    break;
                }
            }
        }

        next[slotIndex] = normalizedSkill;
        return TrySetLoadout(next);
    }

    public void LockLoadoutForActiveRun()
    {
        ProfileData profile = GetProfile();
        EnsureNormalizedLoadoutOnProfile(profile);

        lockedRunLoadout.Clear();
        if (profile != null)
            lockedRunLoadout.AddRange(profile.loadoutSlots);

        while (lockedRunLoadout.Count < MaxLoadoutSlotCount)
            lockedRunLoadout.Add(string.Empty);

        lockedRunLevels.Clear();
        if (profile != null && profile.powerUpLevelIds != null && profile.powerUpLevels != null)
        {
            for (int i = 0; i < profile.powerUpLevelIds.Count && i < profile.powerUpLevels.Count; i++)
            {
                if (!string.IsNullOrEmpty(profile.powerUpLevelIds[i]))
                    lockedRunLevels[profile.powerUpLevelIds[i]] = Mathf.Max(1, profile.powerUpLevels[i]);
            }
        }

        isLoadoutLockedForRun = true;
    }

    public void UnlockLoadoutAfterRun()
    {
        isLoadoutLockedForRun = false;
        lockedRunLoadout.Clear();
        lockedRunLevels.Clear();
    }

    /// <summary>
    /// Level used by gameplay: the run snapshot while a level is in progress, else the saved level.
    /// </summary>
    public int GetPowerUpLevel(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return 1;
        if (isLoadoutLockedForRun)
        {
            if (lockedRunLevels.TryGetValue(powerUpId, out int snap)) return snap;
            return 1;
        }
        ProfileData profile = GetProfile();
        return profile != null ? profile.GetPowerUpLevel(powerUpId) : 1;
    }

    public bool TryUpgradePowerUp(string powerUpId, PowerUpData data, out string failReason)
    {
        failReason = null;
        if (data == null || string.IsNullOrEmpty(powerUpId)) { failReason = "Invalid power."; return false; }

        int level = GetSavedPowerUpLevel(powerUpId);
        int cost = data.GetUpgradeCost(level);
        if (cost < 0) { failReason = "Max level."; return false; }

        int nextLevel = level + 1;
        var tx = new RewardTransaction
        {
            CostType = cost > 0 ? ShopCostType.Coins : ShopCostType.Free,
            CostAmount = cost,
            Payout = new RewardBundle(),
            Reason = $"PowerUpUpgrade_{powerUpId}_L{nextLevel}",
            OnBeforeSaveMutateProfile = p => p.SetPowerUpLevel(powerUpId, nextLevel)
        };
        RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
        if (!result.Succeeded)
        {
            failReason = result.Status == RewardTransactionStatus.InsufficientFunds ? "Not enough Coins." : "Upgrade failed.";
            return false;
        }

        ProfileData activeProfile = GetProfile();
        if (activeProfile != null)
            activeProfile.SetPowerUpLevel(powerUpId, nextLevel);

        if (GameSession.Instance != null && GameSession.Instance.currentProfile != null && GameSession.Instance.currentProfile != activeProfile)
            GameSession.Instance.currentProfile.SetPowerUpLevel(powerUpId, nextLevel);

        OnPowerUpUpgraded?.Invoke(powerUpId, nextLevel);
        OnProgressionChanged?.Invoke();
        return true;
    }

    private int GetSavedPowerUpLevel(string id)
    {
        ProfileData p = SaveManager.loadProfile() ?? GetProfile();
        return p != null ? p.GetPowerUpLevel(id) : 1;
    }

    private static void EnsureNormalizedLoadoutOnProfile(ProfileData profile)
    {
        if (profile == null) return;
        profile.loadoutSlots ??= new List<string>();

        if (profile.loadoutSlots.Count == 0)
        {
            profile.loadoutSlots.Add(PowerUpIds.Breaker);
            profile.loadoutSlots.Add(PowerUpIds.Freeze);
            profile.loadoutSlots.Add(PowerUpIds.Teleport);
            profile.loadoutSlots.Add(string.Empty);
        }

        while (profile.loadoutSlots.Count < MaxLoadoutSlotCount)
            profile.loadoutSlots.Add(string.Empty);

        while (profile.loadoutSlots.Count > MaxLoadoutSlotCount)
            profile.loadoutSlots.RemoveAt(profile.loadoutSlots.Count - 1);
    }

    public int GetCompletedLevelCount()
    {
        var profile = GetProfile();
        return profile != null && profile.completedLevelIDs != null ? profile.completedLevelIDs.Count : 0;
    }

    public int TotalStars
    {
        get
        {
            var profile = GetProfile();
            if (profile == null || profile.levelStarValues == null) return 0;

            int total = 0;
            for (int i = 0; i < profile.levelStarValues.Count; i++)
                total += Mathf.Clamp(profile.levelStarValues[i], 0, 3);
            return total;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Progression Modification
    // ─────────────────────────────────────────────────────────────

    public void CompleteLevel(LevelData level, int stars = 1, int movesUsed = 0)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return;

        var profile = GetProfile();
        if (profile == null) return;

        profile.Sanitize();

        if (!profile.completedLevelIDs.Contains(level.LevelId))
        {
            profile.completedLevelIDs.Add(level.LevelId);
        }

        int clampedStars = Mathf.Clamp(stars, 0, 3);
        profile.SetLevelStarsBest(level.LevelId, clampedStars);

        if (movesUsed > 0)
        {
            profile.SetLevelBestMoves(level.LevelId, movesUsed);
        }

        LevelData unlockedNext = null;
        if (campaignDatabase != null)
        {
            LevelData nextLevel = campaignDatabase.GetNextLevel(level);
            if (nextLevel != null && !string.IsNullOrEmpty(nextLevel.LevelId))
            {
                if (!profile.unlockedLevelIDs.Contains(nextLevel.LevelId))
                {
                    profile.unlockedLevelIDs.Add(nextLevel.LevelId);
                    unlockedNext = nextLevel;
                }
            }
        }

        SaveManager.saveProfile(profile);
        if (unlockedNext != null)
            OnLevelUnlocked?.Invoke(unlockedNext);
        OnProgressionChanged?.Invoke();
    }

    public void UnlockLevel(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return;

        var profile = GetProfile();
        if (profile == null) return;

        profile.Sanitize();

        if (!profile.unlockedLevelIDs.Contains(level.LevelId))
        {
            profile.unlockedLevelIDs.Add(level.LevelId);
            SaveManager.saveProfile(profile);
            OnLevelUnlocked?.Invoke(level);
            OnProgressionChanged?.Invoke();
        }
    }

    public void ReloadProgression()
    {
        ProfileData loaded = SaveManager.ReloadProfileFromDisk();
        fallbackProfile = loaded;
        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = loaded;

        SyncProfileLevelWithExp();
        OnProgressionChanged?.Invoke();
        OnLoadoutChanged?.Invoke();
    }

    public void ResetAllProgression()
    {
        var profile = GetProfile();
        if (profile != null)
        {
            profile.Sanitize();
            profile.playerLevel = 1;
            profile.playerExp = 0;
            profile.unlockedLevelIDs.Clear();
            profile.completedLevelIDs.Clear();
            profile.levelStarKeys.Clear();
            profile.levelStarValues.Clear();
            profile.levelBestMoveKeys.Clear();
            profile.levelBestMoveValues.Clear();
            SaveManager.saveProfile(profile);
            OnProgressionChanged?.Invoke();
            Debug.Log("[ProgressionManager] All campaign progression cleared.");
        }
    }

    private ProfileData GetProfile()
    {
        ProfileData active = SaveManager.loadProfile();
        fallbackProfile = active;
        return active;
    }
}
