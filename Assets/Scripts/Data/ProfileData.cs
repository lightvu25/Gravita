using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Serialization;

public class ProfileData
{
    public const int CurrentSaveSchemaVersion = 2;

    // ── Save Schema Versioning ────────────────────────────────────────────
    public int saveSchemaVersion;

    // ── Run stats (roguelike layer, kept as stub data) ────────────────────
    public int totalGold;
    public int deaths;

    public List<string> unlockedSkillIDs = new List<string>();
    public List<string> persistenceKeys = new List<string>();

    public List<int> attemptLevelKeys   = new List<int>();
    public List<int> attemptLevelValues = new List<int>();

    // ── Player Account Level & EXP ────────────────────────────────────────
    public int playerLevel = 1;
    public int playerExp   = 0;

    // ── Level Progression ─────────────────────────────────────────────────
    public List<string> unlockedLevelIDs    = new List<string>();
    public List<string> completedLevelIDs   = new List<string>();
    public List<string> levelStarKeys       = new List<string>();
    public List<int>    levelStarValues     = new List<int>();
    public List<string> levelBestMoveKeys   = new List<string>();
    public List<int>    levelBestMoveValues = new List<int>();

    // ── Economy ───────────────────────────────────────────────────────────
    public int coins;
    public int gems;

    // ── Unified Transaction & IAP Ledgers (GDD §12.1) ─────────────────────
    public List<string> claimedRewardIds = new List<string>();
    public List<string> processedIapTransactionIds = new List<string>();

    // ── Power-up inventory & 4-Slot Pre-Level Loadout (GDD §5) ────────────
    public List<string> powerUpIds    = new List<string>();
    public List<int>    powerUpCounts = new List<int>();
    public List<string> loadoutSlots  = new List<string>();

    public List<string> powerUpLevelIds = new List<string>();
    public List<int>    powerUpLevels   = new List<int>();

    // ── Cosmetics (v1.0: PlayerSkin — GDD §6) ─────────────────────────────
    public List<string> ownedCosmeticIds = new List<string>();
    public string equippedPlayerSkinId = string.Empty;

    // ── Missions & Local-Time Anti-Rollback (GDD §10) ─────────────────────
    public int missionSchemaVersion;
    public bool hasCompletedFirstLogin;
    public long dailyMissionResetUtcTicks;
    public string dailyMissionResetDateLocal = string.Empty;
    public string weeklyMissionResetMondayLocal = string.Empty;
    public string monthlyMissionResetMonthLocal = string.Empty;
    public long highWaterLocalTicks;
    public bool dailyLoggedIn;

    public List<string> claimedMissionIds = new List<string>();
    public long weeklyMissionStartTicks;
    public int weeklyMissionPoints;
    public List<int> loggedInDaysThisWeek = new List<int>();
    public List<string> claimedWeeklyGoalIds = new List<string>();

    public List<string> claimedDailyMissionIds = new List<string>();
    public List<string> claimedWeeklyMissionIds = new List<string>();
    public List<string> claimedMonthlyWeeklyMissionIds = new List<string>();
    public List<string> claimedSectorMissionIds = new List<string>();
    public List<string> claimedNewPlayerMissionIds = new List<string>();
    public List<string> claimedAchievementIds = new List<string>();

    // ── Mission Progress Counters (Lifetime, Monthly, Weekly & Daily) ─────
    public int lifetimeCoinsCollected;
    public int lifetimeStarsCollected;
    public int lifetimeValidLevelClears;
    public int lifetimeNoDeathLevelClears;
    public int lifetimeNoSkillLevelClears;
    public int lifetimeWallsBroken;
    public int lifetimePowerUpsUsed;
    public List<string> usedPowerUpTypeIds = new List<string>();

    public int monthlyCoinsCollected;
    public int monthlyStarsCollected;
    public int monthlyValidLevelClears;
    public int monthlyNoDeathLevelClears;
    public int monthlyWallsBroken;
    public int monthlyPowerUpsUsed;
    public int monthlyLoginDays;

    public int weeklyCoinsCollected;
    public int weeklyStarsCollected;
    public int weeklyValidLevelClears;
    public int weeklyNoDeathLevelClears;
    public int weeklyWallsBroken;
    public int weeklyPowerUpsUsed;

    public int dailyCoinsCollected;
    public int dailyStarsCollected;
    public int dailyValidLevelClears;
    public int dailyNoDeathLevelClears;
    public int dailyWallsBroken;
    public int dailyPowerUpsUsed;

    // ── Loot Pack Pity Tracking (Shop Packs & BlindBox) ───────────────────
    public List<string> lootPackPityKeys = new List<string>();
    public List<int> lootPackPityCounts = new List<int>();

    // ── Ads Scheduling & Daily Limits (GDD §7 & §8) ───────────────────────
    public string dailyAdResetDateLocal = string.Empty;
    public int dailyRewardedAdCount;
    public int interstitialCompletedLevelsSinceLastAd;
    public int interstitialTargetLevelsInterval;

    // ── Analytics Campaign Flags (GDD §13) ────────────────────────────────
    public bool hasWatchedAnyAdInCampaign;
    public bool hasMadeAnyIapInCampaign;
    public bool campaignAllLevelsCompletedReported;

    // ── Purchases & Services ──────────────────────────────────────────────
    public bool noAdsPurchased;
    public List<string> purchasedProductIDs = new List<string>();
    public List<string> purchasedShopItemIDs = new List<string>();
    [FormerlySerializedAs("lastVisitorTipClaimUtcTicks")]
    public long lastCoinBonusClaimUtcTicks;
    public long lastDailyRewardClaimUtcTicks;
    public int dailyRewardStreakDay;
    public string lastBlindBoxClaimDateKey = string.Empty;
    public bool tutorialCompleted;

    // ── Maze Runner Progression ───────────────────────────────────────────
    public int bestRunnerEndlessRooms;
    public int bestRunner10RoomsClears;
    public int bestRunner20RoomsClears;
    public int lifetimeRunnerRoomsCleared;

    // ── Live-Ops Events Tracking ──────────────────────────────────────────
    public List<string> claimedEventTaskIds = new List<string>();
    public List<string> eventStartIds = new List<string>();
    public List<string> eventStartDateKeys = new List<string>();

    public void MigrateIfNeeded()
    {
        Sanitize();

        if (saveSchemaVersion >= CurrentSaveSchemaVersion)
            return;

        // Backfill First-Clear reward IDs for already-completed levels
        for (int i = 0; i < completedLevelIDs.Count; i++)
        {
            string levelId = completedLevelIDs[i];
            if (!string.IsNullOrEmpty(levelId))
            {
                MarkRewardClaimed($"level_{levelId}_first_clear");
            }
        }

        // Backfill per-star unlock IDs for stars already earned on cleared levels
        for (int i = 0; i < levelStarKeys.Count; i++)
        {
            string levelId = levelStarKeys[i];
            if (string.IsNullOrEmpty(levelId)) continue;

            int bestStars = GetLevelStars(levelId);
            for (int s = 1; s <= bestStars; s++)
            {
                MarkRewardClaimed($"level_{levelId}_star_{s}");
            }
        }

        // Backfill permanent achievement claims into the unified ledger
        for (int i = 0; i < claimedAchievementIds.Count; i++)
        {
            string achId = claimedAchievementIds[i];
            if (!string.IsNullOrEmpty(achId))
                MarkRewardClaimed($"achievement_{achId}");
        }

        // Initialize 4-slot loadout without duplicating any skill across slots (GDD §5.5)
        if (loadoutSlots.Count == 0)
        {
            string[] activeBaseline = { PowerUpIds.Breaker, PowerUpIds.Freeze, PowerUpIds.Teleport };
            for (int i = 0; i < activeBaseline.Length; i++)
            {
                loadoutSlots.Add(activeBaseline[i]);
            }
            // 4th slot starts empty per GDD §5.5
            loadoutSlots.Add(string.Empty);
        }

        saveSchemaVersion = CurrentSaveSchemaVersion;
        Sanitize();
    }

    /// <summary>
    /// Ensures all collections are initialized and parallel key/value lists
    /// remain synchronized when loading older or partially populated save data.
    /// </summary>
    public void Sanitize()
    {
        playerLevel = Mathf.Max(1, playerLevel);
        playerExp   = Mathf.Max(0, playerExp);
        coins       = Mathf.Max(0, coins);
        gems        = Mathf.Max(0, gems);
        deaths      = Mathf.Max(0, deaths);
        bestRunnerEndlessRooms = Mathf.Max(0, bestRunnerEndlessRooms);
        bestRunner10RoomsClears = Mathf.Max(0, bestRunner10RoomsClears);
        bestRunner20RoomsClears = Mathf.Max(0, bestRunner20RoomsClears);
        lifetimeRunnerRoomsCleared = Mathf.Max(0, lifetimeRunnerRoomsCleared);

        unlockedSkillIDs ??= new List<string>();
        persistenceKeys ??= new List<string>();
        attemptLevelKeys ??= new List<int>();
        attemptLevelValues ??= new List<int>();
        unlockedLevelIDs ??= new List<string>();
        completedLevelIDs ??= new List<string>();
        levelStarKeys ??= new List<string>();
        levelStarValues ??= new List<int>();
        levelBestMoveKeys ??= new List<string>();
        levelBestMoveValues ??= new List<int>();

        claimedRewardIds ??= new List<string>();
        processedIapTransactionIds ??= new List<string>();

        powerUpIds ??= new List<string>();
        powerUpCounts ??= new List<int>();
        loadoutSlots ??= new List<string>();

        ownedCosmeticIds ??= new List<string>();
        equippedPlayerSkinId ??= string.Empty;

        dailyMissionResetDateLocal ??= string.Empty;
        weeklyMissionResetMondayLocal ??= string.Empty;
        monthlyMissionResetMonthLocal ??= string.Empty;
        claimedMissionIds ??= new List<string>();
        weeklyMissionPoints = Mathf.Max(0, weeklyMissionPoints);
        loggedInDaysThisWeek ??= new List<int>();
        claimedWeeklyGoalIds ??= new List<string>();
        claimedDailyMissionIds ??= new List<string>();
        claimedWeeklyMissionIds ??= new List<string>();
        claimedMonthlyWeeklyMissionIds ??= new List<string>();
        claimedSectorMissionIds ??= new List<string>();
        claimedNewPlayerMissionIds ??= new List<string>();
        claimedAchievementIds ??= new List<string>();
        powerUpLevelIds ??= new List<string>();
        powerUpLevels   ??= new List<int>();
        
        int lvPairs = Mathf.Min(powerUpLevelIds.Count, powerUpLevels.Count);
        if (powerUpLevelIds.Count > lvPairs) powerUpLevelIds.RemoveRange(lvPairs, powerUpLevelIds.Count - lvPairs);
        if (powerUpLevels.Count   > lvPairs) powerUpLevels.RemoveRange(lvPairs, powerUpLevels.Count - lvPairs);
        for (int i = 0; i < powerUpLevels.Count; i++) powerUpLevels[i] = Mathf.Max(1, powerUpLevels[i]);

        lifetimeCoinsCollected = Mathf.Max(0, lifetimeCoinsCollected);
        lifetimeStarsCollected = Mathf.Max(0, lifetimeStarsCollected);
        lifetimeValidLevelClears = Mathf.Max(0, lifetimeValidLevelClears);
        lifetimeNoDeathLevelClears = Mathf.Max(0, lifetimeNoDeathLevelClears);
        lifetimeNoSkillLevelClears = Mathf.Max(0, lifetimeNoSkillLevelClears);
        lifetimeWallsBroken = Mathf.Max(0, lifetimeWallsBroken);
        lifetimePowerUpsUsed = Mathf.Max(0, lifetimePowerUpsUsed);
        usedPowerUpTypeIds ??= new List<string>();

        monthlyCoinsCollected = Mathf.Max(0, monthlyCoinsCollected);
        monthlyStarsCollected = Mathf.Max(0, monthlyStarsCollected);
        monthlyValidLevelClears = Mathf.Max(0, monthlyValidLevelClears);
        monthlyNoDeathLevelClears = Mathf.Max(0, monthlyNoDeathLevelClears);
        monthlyWallsBroken = Mathf.Max(0, monthlyWallsBroken);
        monthlyPowerUpsUsed = Mathf.Max(0, monthlyPowerUpsUsed);
        monthlyLoginDays = Mathf.Max(0, monthlyLoginDays);

        weeklyCoinsCollected = Mathf.Max(0, weeklyCoinsCollected);
        weeklyStarsCollected = Mathf.Max(0, weeklyStarsCollected);
        weeklyValidLevelClears = Mathf.Max(0, weeklyValidLevelClears);
        weeklyNoDeathLevelClears = Mathf.Max(0, weeklyNoDeathLevelClears);
        weeklyWallsBroken = Mathf.Max(0, weeklyWallsBroken);
        weeklyPowerUpsUsed = Mathf.Max(0, weeklyPowerUpsUsed);

        dailyCoinsCollected = Mathf.Max(0, dailyCoinsCollected);
        dailyStarsCollected = Mathf.Max(0, dailyStarsCollected);
        dailyValidLevelClears = Mathf.Max(0, dailyValidLevelClears);
        dailyNoDeathLevelClears = Mathf.Max(0, dailyNoDeathLevelClears);
        dailyWallsBroken = Mathf.Max(0, dailyWallsBroken);
        dailyPowerUpsUsed = Mathf.Max(0, dailyPowerUpsUsed);

        lootPackPityKeys ??= new List<string>();
        lootPackPityCounts ??= new List<int>();
        claimedEventTaskIds ??= new List<string>();
        eventStartIds ??= new List<string>();
        eventStartDateKeys ??= new List<string>();

        int eventPairCount = Mathf.Min(eventStartIds.Count, eventStartDateKeys.Count);
        if (eventStartIds.Count > eventPairCount)
            eventStartIds.RemoveRange(eventPairCount, eventStartIds.Count - eventPairCount);
        if (eventStartDateKeys.Count > eventPairCount)
            eventStartDateKeys.RemoveRange(eventPairCount, eventStartDateKeys.Count - eventPairCount);
        for (int i = eventStartIds.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(eventStartIds[i]) || string.IsNullOrEmpty(eventStartDateKeys[i]))
            {
                eventStartIds.RemoveAt(i);
                eventStartDateKeys.RemoveAt(i);
            }
        }
        int pityPairCount = Mathf.Min(lootPackPityKeys.Count, lootPackPityCounts.Count);
        if (lootPackPityKeys.Count > pityPairCount)
            lootPackPityKeys.RemoveRange(pityPairCount, lootPackPityKeys.Count - pityPairCount);
        if (lootPackPityCounts.Count > pityPairCount)
            lootPackPityCounts.RemoveRange(pityPairCount, lootPackPityCounts.Count - pityPairCount);
        for (int i = lootPackPityKeys.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(lootPackPityKeys[i]))
            {
                lootPackPityKeys.RemoveAt(i);
                lootPackPityCounts.RemoveAt(i);
                continue;
            }
            lootPackPityCounts[i] = Mathf.Max(0, lootPackPityCounts[i]);
        }

        dailyAdResetDateLocal ??= string.Empty;
        lastBlindBoxClaimDateKey ??= string.Empty;
        dailyRewardedAdCount = Mathf.Max(0, dailyRewardedAdCount);
        interstitialCompletedLevelsSinceLastAd = Mathf.Max(0, interstitialCompletedLevelsSinceLastAd);
        interstitialTargetLevelsInterval = Mathf.Max(0, interstitialTargetLevelsInterval);

        purchasedProductIDs ??= new List<string>();
        purchasedShopItemIDs ??= new List<string>();

        // Synchronize and deduplicate levelStarKeys / levelStarValues while preserving best stars.
        int starPairCount = Mathf.Min(levelStarKeys.Count, levelStarValues.Count);
        if (levelStarKeys.Count != levelStarValues.Count)
        {
            if (levelStarKeys.Count > starPairCount)
                levelStarKeys.RemoveRange(starPairCount, levelStarKeys.Count - starPairCount);
            if (levelStarValues.Count > starPairCount)
                levelStarValues.RemoveRange(starPairCount, levelStarValues.Count - starPairCount);
        }

        for (int i = levelStarKeys.Count - 1; i >= 0; i--)
        {
            string key = levelStarKeys[i];
            if (string.IsNullOrEmpty(key))
            {
                levelStarKeys.RemoveAt(i);
                levelStarValues.RemoveAt(i);
                continue;
            }

            int clamped = Mathf.Clamp(levelStarValues[i], 0, 3);
            levelStarValues[i] = clamped;

            int firstIdx = levelStarKeys.IndexOf(key);
            if (firstIdx >= 0 && firstIdx < i)
            {
                levelStarValues[firstIdx] = Mathf.Max(Mathf.Clamp(levelStarValues[firstIdx], 0, 3), clamped);
                levelStarKeys.RemoveAt(i);
                levelStarValues.RemoveAt(i);
            }
        }

        // Synchronize and deduplicate levelBestMoveKeys / levelBestMoveValues while preserving lowest move count.
        int movePairCount = Mathf.Min(levelBestMoveKeys.Count, levelBestMoveValues.Count);
        if (levelBestMoveKeys.Count > movePairCount)
            levelBestMoveKeys.RemoveRange(movePairCount, levelBestMoveKeys.Count - movePairCount);
        if (levelBestMoveValues.Count > movePairCount)
            levelBestMoveValues.RemoveRange(movePairCount, levelBestMoveValues.Count - movePairCount);

        for (int i = levelBestMoveKeys.Count - 1; i >= 0; i--)
        {
            string key = levelBestMoveKeys[i];
            int val = levelBestMoveValues[i];
            if (string.IsNullOrEmpty(key) || val <= 0)
            {
                levelBestMoveKeys.RemoveAt(i);
                levelBestMoveValues.RemoveAt(i);
                continue;
            }

            int firstIdx = levelBestMoveKeys.IndexOf(key);
            if (firstIdx >= 0 && firstIdx < i)
            {
                int prev = levelBestMoveValues[firstIdx];
                levelBestMoveValues[firstIdx] = prev > 0 ? Mathf.Min(prev, val) : val;
                levelBestMoveKeys.RemoveAt(i);
                levelBestMoveValues.RemoveAt(i);
            }
        }

        // Synchronize and normalize powerUpIds / powerUpCounts (including legacy "kinetic_shield" -> "shield")
        int powerPairCount = Mathf.Min(powerUpIds.Count, powerUpCounts.Count);
        if (powerUpIds.Count > powerPairCount)
            powerUpIds.RemoveRange(powerPairCount, powerUpIds.Count - powerPairCount);
        if (powerUpCounts.Count > powerPairCount)
            powerUpCounts.RemoveRange(powerPairCount, powerUpCounts.Count - powerPairCount);

        for (int i = powerUpIds.Count - 1; i >= 0; i--)
        {
            string rawId = powerUpIds[i];
            if (string.IsNullOrEmpty(rawId))
            {
                powerUpIds.RemoveAt(i);
                powerUpCounts.RemoveAt(i);
                continue;
            }

            if (string.Equals(rawId, PowerUpIds.KineticShield, StringComparison.OrdinalIgnoreCase))
                rawId = PowerUpIds.Shield;

            powerUpIds[i] = rawId;
            powerUpCounts[i] = Mathf.Max(0, powerUpCounts[i]);
        }

        for (int i = powerUpIds.Count - 1; i >= 0; i--)
        {
            int firstIdx = powerUpIds.FindIndex(id => string.Equals(id, powerUpIds[i], StringComparison.OrdinalIgnoreCase));
            if (firstIdx >= 0 && firstIdx < i)
            {
                powerUpCounts[firstIdx] = Mathf.Max(0, powerUpCounts[firstIdx] + powerUpCounts[i]);
                powerUpIds.RemoveAt(i);
                powerUpCounts.RemoveAt(i);
            }
        }

        // Normalize loadoutSlots: cap to 4 slots and prevent duplicate non-empty skill IDs
        while (loadoutSlots.Count > 4)
            loadoutSlots.RemoveAt(loadoutSlots.Count - 1);

        var seenLoadoutSkills = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < loadoutSlots.Count; i++)
        {
            string slotSkill = loadoutSlots[i] ?? string.Empty;
            if (!string.IsNullOrEmpty(slotSkill) && !seenLoadoutSkills.Add(slotSkill))
            {
                loadoutSlots[i] = string.Empty;
            }
            else
            {
                loadoutSlots[i] = slotSkill;
            }
        }

        // Synchronize attemptLevelKeys / attemptLevelValues
        int attemptPairCount = Mathf.Min(attemptLevelKeys.Count, attemptLevelValues.Count);
        if (attemptLevelKeys.Count > attemptPairCount)
            attemptLevelKeys.RemoveRange(attemptPairCount, attemptLevelKeys.Count - attemptPairCount);
        if (attemptLevelValues.Count > attemptPairCount)
            attemptLevelValues.RemoveRange(attemptPairCount, attemptLevelValues.Count - attemptPairCount);

        DeduplicateStringList(claimedRewardIds);
        DeduplicateStringList(processedIapTransactionIds);
        DeduplicateStringList(ownedCosmeticIds);
    }

    private static void DeduplicateStringList(List<string> list)
    {
        if (list == null || list.Count <= 1) return;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < list.Count; i++)
        {
            string entry = list[i];
            if (string.IsNullOrEmpty(entry) || !seen.Add(entry))
            {
                list.RemoveAt(i);
                i--;
            }
        }
    }

    public bool HasClaimedReward(string rewardId)
    {
        if (string.IsNullOrEmpty(rewardId) || claimedRewardIds == null)
            return false;
        return claimedRewardIds.Contains(rewardId);
    }

    public bool MarkRewardClaimed(string rewardId)
    {
        if (string.IsNullOrEmpty(rewardId))
            return false;
        claimedRewardIds ??= new List<string>();
        if (claimedRewardIds.Contains(rewardId))
            return false;
        claimedRewardIds.Add(rewardId);
        return true;
    }

    public bool HasProcessedIapTransaction(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId) || processedIapTransactionIds == null)
            return false;
        return processedIapTransactionIds.Contains(transactionId);
    }

    public bool MarkIapTransactionProcessed(string transactionId)
    {
        if (string.IsNullOrEmpty(transactionId))
            return false;
        processedIapTransactionIds ??= new List<string>();
        if (processedIapTransactionIds.Contains(transactionId))
            return false;
        processedIapTransactionIds.Add(transactionId);
        return true;
    }

    /// <summary>
    /// Prunes periodic daily/weekly reward IDs older than <paramref name="keepDays"/> while
    /// keeping all permanent level, star, sector, tutorial, achievement, and IAP entries intact.
    /// </summary>
    public void PruneExpiredPeriodicRewardIds(DateTime referenceLocalDate, int keepDays = 40)
    {
        if (claimedRewardIds == null || claimedRewardIds.Count == 0)
            return;

        DateTime cutoff = referenceLocalDate.Date.AddDays(-Mathf.Max(14, keepDays));
        for (int i = claimedRewardIds.Count - 1; i >= 0; i--)
        {
            string id = claimedRewardIds[i];
            if (string.IsNullOrEmpty(id))
            {
                claimedRewardIds.RemoveAt(i);
                continue;
            }

            if (TryExtractPeriodicDate(id, "daily_", out DateTime dailyDate) && dailyDate < cutoff)
            {
                claimedRewardIds.RemoveAt(i);
            }
            else if (TryExtractPeriodicDate(id, "weekly_", out DateTime weeklyDate) && weeklyDate < cutoff)
            {
                claimedRewardIds.RemoveAt(i);
            }
        }
    }

    private static bool TryExtractPeriodicDate(string rewardId, string prefix, out DateTime parsedDate)
    {
        parsedDate = default;
        if (!rewardId.StartsWith(prefix, StringComparison.Ordinal) || rewardId.Length < prefix.Length + 10)
            return false;

        string datePart = rewardId.Substring(prefix.Length, 10);
        return DateTime.TryParseExact(datePart, "yyyy_MM_dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out parsedDate);
    }

    /// <summary>Returns the saved best star count (0-3) for the given level ID.</summary>
    public int GetLevelStars(string levelId)
    {
        if (string.IsNullOrEmpty(levelId) || levelStarKeys == null || levelStarValues == null)
            return 0;

        int idx = levelStarKeys.IndexOf(levelId);
        if (idx < 0 || idx >= levelStarValues.Count)
            return 0;

        return Mathf.Clamp(levelStarValues[idx], 0, 3);
    }

    /// <summary>
    /// Updates the saved star count for <paramref name="levelId"/> only if
    /// <paramref name="stars"/> exceeds the previously saved best result.
    /// Returns true if the saved star count increased.
    /// </summary>
    public bool SetLevelStarsBest(string levelId, int stars)
    {
        if (string.IsNullOrEmpty(levelId))
            return false;

        levelStarKeys ??= new List<string>();
        levelStarValues ??= new List<int>();

        int clampedStars = Mathf.Clamp(stars, 0, 3);
        int currentBest = GetLevelStars(levelId);
        if (clampedStars <= currentBest)
            return false;

        int idx = levelStarKeys.IndexOf(levelId);
        if (idx >= 0 && idx < levelStarValues.Count)
        {
            levelStarValues[idx] = clampedStars;
        }
        else
        {
            if (idx >= 0)
                levelStarKeys.RemoveAt(idx);
            levelStarKeys.Add(levelId);
            levelStarValues.Add(clampedStars);
        }

        return true;
    }

    /// <summary>Returns the lowest recorded move count for <paramref name="levelId"/>, or 0 if unrecorded.</summary>
    public int GetBestMoves(string levelId)
    {
        if (string.IsNullOrEmpty(levelId) || levelBestMoveKeys == null || levelBestMoveValues == null)
            return 0;

        int idx = levelBestMoveKeys.IndexOf(levelId);
        if (idx < 0 || idx >= levelBestMoveValues.Count)
            return 0;

        return Mathf.Max(0, levelBestMoveValues[idx]);
    }

    /// <summary>
    /// Records <paramref name="moves"/> if it is positive and lower than any previously recorded best move count.
    /// Returns true when a new best move record is established.
    /// </summary>
    public bool SetLevelBestMoves(string levelId, int moves)
    {
        if (string.IsNullOrEmpty(levelId) || moves <= 0)
            return false;

        levelBestMoveKeys ??= new List<string>();
        levelBestMoveValues ??= new List<int>();

        int currentBest = GetBestMoves(levelId);
        if (currentBest > 0 && moves >= currentBest)
            return false;

        int idx = levelBestMoveKeys.IndexOf(levelId);
        if (idx >= 0 && idx < levelBestMoveValues.Count)
        {
            levelBestMoveValues[idx] = moves;
        }
        else
        {
            if (idx >= 0)
                levelBestMoveKeys.RemoveAt(idx);
            levelBestMoveKeys.Add(levelId);
            levelBestMoveValues.Add(moves);
        }

        return true;
    }

    public int GetLevelAttempts(int levelIndex)
    {
        if (attemptLevelKeys == null || attemptLevelValues == null) return 0;
        int idx = attemptLevelKeys.IndexOf(levelIndex);
        return (idx >= 0 && idx < attemptLevelValues.Count) ? attemptLevelValues[idx] : 0;
    }

    public void IncrementLevelAttempt(int levelIndex)
    {
        attemptLevelKeys ??= new List<int>();
        attemptLevelValues ??= new List<int>();

        int idx = attemptLevelKeys.IndexOf(levelIndex);
        if (idx >= 0 && idx < attemptLevelValues.Count)
            attemptLevelValues[idx]++;
        else
        {
            attemptLevelKeys.Add(levelIndex);
            attemptLevelValues.Add(1);
        }
    }

    public bool HasSkill(string skillID)
    {
        if (unlockedSkillIDs == null || string.IsNullOrEmpty(skillID))
            return false;

        for (int i = 0; i < unlockedSkillIDs.Count; i++)
        {
            if (string.Equals(unlockedSkillIDs[i], skillID, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    public bool UnlockSkill(string skillID)
    {
        if (string.IsNullOrEmpty(skillID))
            return false;

        unlockedSkillIDs ??= new List<string>();
        if (HasSkill(skillID))
            return false;

        unlockedSkillIDs.Add(skillID.Trim().ToLowerInvariant());
        return true;
    }

    public static string BuildLootPackPityKey(string packId, string entryId)
    {
        string safePack = string.IsNullOrWhiteSpace(packId) ? "default_pack" : packId.Trim();
        string safeEntry = string.IsNullOrWhiteSpace(entryId) ? "entry" : entryId.Trim();
        return $"{safePack}:{safeEntry}";
    }

    public int GetLootPackPity(string packId, string entryId)
    {
        if (lootPackPityKeys == null || lootPackPityCounts == null)
            return 0;

        string key = BuildLootPackPityKey(packId, entryId);
        int idx = lootPackPityKeys.IndexOf(key);
        if (idx < 0 || idx >= lootPackPityCounts.Count)
            return 0;

        return Mathf.Max(0, lootPackPityCounts[idx]);
    }

    public void SetLootPackPity(string packId, string entryId, int count)
    {
        lootPackPityKeys ??= new List<string>();
        lootPackPityCounts ??= new List<int>();
        claimedEventTaskIds ??= new List<string>();
        eventStartIds ??= new List<string>();
        eventStartDateKeys ??= new List<string>();

        int eventPairCount = Mathf.Min(eventStartIds.Count, eventStartDateKeys.Count);
        if (eventStartIds.Count > eventPairCount)
            eventStartIds.RemoveRange(eventPairCount, eventStartIds.Count - eventPairCount);
        if (eventStartDateKeys.Count > eventPairCount)
            eventStartDateKeys.RemoveRange(eventPairCount, eventStartDateKeys.Count - eventPairCount);
        for (int i = eventStartIds.Count - 1; i >= 0; i--)
        {
            if (string.IsNullOrEmpty(eventStartIds[i]) || string.IsNullOrEmpty(eventStartDateKeys[i]))
            {
                eventStartIds.RemoveAt(i);
                eventStartDateKeys.RemoveAt(i);
            }
        }

        string key = BuildLootPackPityKey(packId, entryId);
        int clamped = Mathf.Max(0, count);
        int idx = lootPackPityKeys.IndexOf(key);
        if (idx >= 0 && idx < lootPackPityCounts.Count)
        {
            lootPackPityCounts[idx] = clamped;
        }
        else
        {
            if (idx >= 0)
                lootPackPityKeys.RemoveAt(idx);
            lootPackPityKeys.Add(key);
            lootPackPityCounts.Add(clamped);
        }
    }

    public void ResetLootPackPity(string packId, string entryId)
    {
        SetLootPackPity(packId, entryId, 0);
    }

    public int GetPowerUpLevel(string id)
    {
        if (string.IsNullOrEmpty(id) || powerUpLevelIds == null || powerUpLevels == null) return 1;
        int i = powerUpLevelIds.IndexOf(id);
        return i >= 0 && i < powerUpLevels.Count ? Mathf.Max(1, powerUpLevels[i]) : 1;
    }

    public void SetPowerUpLevel(string id, int level)
    {
        if (string.IsNullOrEmpty(id)) return;
        powerUpLevelIds ??= new List<string>();
        powerUpLevels ??= new List<int>();
        level = Mathf.Max(1, level);
        int i = powerUpLevelIds.IndexOf(id);
        if (i >= 0 && i < powerUpLevels.Count) powerUpLevels[i] = level;
        else { powerUpLevelIds.Add(id); powerUpLevels.Add(level); }
    }

    public int GetBestRunnerRooms(RunnerModeKind mode)
    {
        return mode switch
        {
            RunnerModeKind.Rooms10 => Mathf.Max(0, bestRunner10RoomsClears),
            RunnerModeKind.Rooms20 => Mathf.Max(0, bestRunner20RoomsClears),
            _                      => Mathf.Max(0, bestRunnerEndlessRooms),
        };
    }

    public bool RecordRunnerRun(RunnerModeKind mode, int roomsCleared, bool completedTarget)
    {
        int safeCleared = Mathf.Max(0, roomsCleared);
        lifetimeRunnerRoomsCleared = Mathf.Max(0, lifetimeRunnerRoomsCleared) + safeCleared;

        bool isNewBest = false;
        if (safeCleared > bestRunnerEndlessRooms)
        {
            bestRunnerEndlessRooms = safeCleared;
            isNewBest = true;
        }

        if (completedTarget)
        {
            if (mode == RunnerModeKind.Rooms10)
            {
                bestRunner10RoomsClears = Mathf.Max(0, bestRunner10RoomsClears) + 1;
                isNewBest = true;
            }
            else if (mode == RunnerModeKind.Rooms20)
            {
                bestRunner20RoomsClears = Mathf.Max(0, bestRunner20RoomsClears) + 1;
                isNewBest = true;
            }
        }

        if (isNewBest && PlayGamesService.Instance != null)
            _ = PlayGamesService.Instance.TryReportRunnerRecordAsync(mode, GetBestRunnerRooms(mode));

        return isNewBest;
    }

    public string GetEventStartDateKey(string eventId)
    {
        if (string.IsNullOrEmpty(eventId) || eventStartIds == null || eventStartDateKeys == null)
            return string.Empty;

        int index = eventStartIds.IndexOf(eventId);
        return index >= 0 && index < eventStartDateKeys.Count ? eventStartDateKeys[index] : string.Empty;
    }

    public void SetEventStartDateKey(string eventId, string dateKey)
    {
        if (string.IsNullOrEmpty(eventId) || string.IsNullOrEmpty(dateKey))
            return;

        eventStartIds ??= new List<string>();
        eventStartDateKeys ??= new List<string>();

        int index = eventStartIds.IndexOf(eventId);
        if (index >= 0 && index < eventStartDateKeys.Count)
            eventStartDateKeys[index] = dateKey;
        else
        {
            eventStartIds.Add(eventId);
            eventStartDateKeys.Add(dateKey);
        }
    }
}
