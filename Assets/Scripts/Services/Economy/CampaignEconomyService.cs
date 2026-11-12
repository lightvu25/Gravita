using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Immutable settlement DTO produced when a Campaign level is completed.
/// Consumed by UI screens (<see cref="ResultPanelUI"/>, <see cref="RewardOverlayUI"/>)
/// and Analytics without coupling UI or <see cref="LevelFlowController"/> to economy math.
/// </summary>
public readonly struct LevelEconomySettlement
{
    public string LevelId { get; }
    public int SectorNumber { get; }
    public bool IsFirstClear { get; }
    public int PreviousBestStars { get; }
    public int EarnedStars { get; }
    public int NewBestStars { get; }
    public int NewlyUnlockedStarsCount { get; }
    public int MovesUsed { get; }
    public int BestMoves { get; }
    public bool IsNewBestMoves { get; }
    public bool UsedAnySkill { get; }

    public int FirstClearCoinsGranted { get; }
    public int ReplayStarUpgradeCoinsGranted { get; }
    public bool SectorCompletedThisRun { get; }
    public int SectorCompletionCoinsGranted { get; }
    public int SectorCompletionGemsGranted { get; }
    public bool CampaignCompletedThisRun { get; }

    public int TotalCoinsGranted => FirstClearCoinsGranted + ReplayStarUpgradeCoinsGranted + SectorCompletionCoinsGranted;
    public int TotalGemsGranted => SectorCompletionGemsGranted;
    public RewardBundle GrantedBundle { get; }

    public LevelEconomySettlement(
        string levelId,
        int sectorNumber,
        bool isFirstClear,
        int previousBestStars,
        int earnedStars,
        int newBestStars,
        int newlyUnlockedStarsCount,
        int movesUsed,
        int bestMoves,
        bool isNewBestMoves,
        bool usedAnySkill,
        int firstClearCoinsGranted,
        int replayStarUpgradeCoinsGranted,
        bool sectorCompletedThisRun,
        int sectorCompletionCoinsGranted,
        int sectorCompletionGemsGranted,
        bool campaignCompletedThisRun,
        RewardBundle grantedBundle)
    {
        LevelId = levelId ?? string.Empty;
        SectorNumber = sectorNumber;
        IsFirstClear = isFirstClear;
        PreviousBestStars = previousBestStars;
        EarnedStars = earnedStars;
        NewBestStars = newBestStars;
        NewlyUnlockedStarsCount = newlyUnlockedStarsCount;
        MovesUsed = movesUsed;
        BestMoves = bestMoves;
        IsNewBestMoves = isNewBestMoves;
        UsedAnySkill = usedAnySkill;
        FirstClearCoinsGranted = firstClearCoinsGranted;
        ReplayStarUpgradeCoinsGranted = replayStarUpgradeCoinsGranted;
        SectorCompletedThisRun = sectorCompletedThisRun;
        SectorCompletionCoinsGranted = sectorCompletionCoinsGranted;
        SectorCompletionGemsGranted = sectorCompletionGemsGranted;
        CampaignCompletedThisRun = campaignCompletedThisRun;
        GrantedBundle = grantedBundle ?? new RewardBundle();
    }
}

/// <summary>
/// Domain service implementing Gravita Economy GDD §4:
///   - §4.1 First-Time Level Clear: one-time Coin reward (never granted again on replay).
///   - §4.2 Star System: 0..3 stars on clear; NO direct Coin/Gem star bonus on first clear;
///          on Replay, awards Coins ONLY for newly unlocked star tiers (star_1, star_2, star_3).
///   - §4.3 Sector Completion: one-time Coin + Gem milestone when all levels in a Sector are completed.
/// Commits progression + economy rewards in a single atomic <see cref="RewardTransaction"/>.
/// </summary>
public static class CampaignEconomyService
{
    public static event Action<LevelEconomySettlement> OnLevelEconomySettled;

    public static LevelEconomySettlement SettleLevelCompletion(
        LevelData level,
        int earnedStars,
        int movesUsed = 0,
        bool usedAnySkill = false,
        CampaignDatabase explicitCampaignDb = null)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId))
        {
            return default;
        }

        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
        {
            return default;
        }

        profile.MigrateIfNeeded();

        CampaignDatabase campaignDb = explicitCampaignDb != null
            ? explicitCampaignDb
            : (ProgressionManager.Instance != null ? ProgressionManager.Instance.Database : null);

        string levelId = level.LevelId;
        int clampedStars = Mathf.Clamp(earnedStars, 0, 3);
        int sectorNumber = campaignDb != null
            ? campaignDb.GetSectorNumberForLevel(level)
            : Mathf.Max(1, level.World);

        WorldData containingWorld = campaignDb != null
            ? campaignDb.GetWorldForLevel(level, out sectorNumber)
            : null;

        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;

        string firstClearRewardId = EconomyRewardIds.LevelFirstClear(levelId);
        bool wasCompletedBefore = profile.completedLevelIDs.Contains(levelId) || profile.HasClaimedReward(firstClearRewardId);
        int previousBestStars = profile.GetLevelStars(levelId);
        int previousBestMoves = profile.GetBestMoves(levelId);

        int firstClearCoins = 0;
        int replayStarUpgradeCoins = 0;
        int newlyUnlockedStarsCount = 0;

        string primaryRewardId = string.Empty;
        var extraRewardIds = new List<string>(5);

        if (!wasCompletedBefore)
        {
            // GDD §4.1: First-Time Level Clear grants fixed Coins once; stars earned on first clear grant 0 direct star bonus (§4.2).
            firstClearCoins = balance.GetFirstClearCoins(level, sectorNumber);
            primaryRewardId = firstClearRewardId;

            // Mark stars achieved on first clear as already unlocked so replay only ever pays for strictly higher star milestones.
            for (int s = 1; s <= clampedStars; s++)
            {
                string starId = EconomyRewardIds.LevelStarUnlock(levelId, s);
                if (!profile.HasClaimedReward(starId))
                    extraRewardIds.Add(starId);
            }
        }
        else if (clampedStars > previousBestStars)
        {
            // GDD §4.2: Replay Star Improvement — grant Coins ONLY for newly unlocked star tiers.
            for (int s = previousBestStars + 1; s <= clampedStars; s++)
            {
                string starId = EconomyRewardIds.LevelStarUnlock(levelId, s);
                if (profile.HasClaimedReward(starId))
                    continue;

                int starCoins = balance.GetReplayStarUnlockCoins(sectorNumber, s);
                replayStarUpgradeCoins += starCoins;
                newlyUnlockedStarsCount++;

                if (string.IsNullOrEmpty(primaryRewardId))
                    primaryRewardId = starId;
                else
                    extraRewardIds.Add(starId);
            }
        }

        // Evaluate whether completing this level finishes the entire Sector for the first time (GDD §4.3)
        bool willCompleteSector = false;
        int sectorCoins = 0;
        int sectorGems = 0;
        string sectorRewardId = EconomyRewardIds.SectorCompletion(sectorNumber);

        if (containingWorld != null && !profile.HasClaimedReward(sectorRewardId))
        {
            if (WillAllLevelsInWorldBeCompleted(containingWorld, profile, levelId))
            {
                willCompleteSector = true;
                SectorEconomyRewardConfig sectorCfg = balance.GetSectorConfig(sectorNumber);
                sectorCoins = Mathf.Max(0, sectorCfg.sectorCompletionCoinReward);
                sectorGems = Mathf.Max(0, sectorCfg.sectorCompletionGemReward);

                if (string.IsNullOrEmpty(primaryRewardId))
                    primaryRewardId = sectorRewardId;
                else
                    extraRewardIds.Add(sectorRewardId);
            }
        }

        var payout = new RewardBundle();
        int totalCoins = firstClearCoins + replayStarUpgradeCoins + sectorCoins;
        if (totalCoins > 0)
            payout.Add(RewardType.Coins, totalCoins);
        if (sectorGems > 0)
            payout.Add(RewardType.Gems, sectorGems);

        LevelData nextLevelToUnlock = campaignDb != null ? campaignDb.GetNextLevel(level) : null;
        bool unlockedNextLevel = false;
        bool isNewBestMoves = false;
        int resultingBestMoves = previousBestMoves;
        int resultingBestStars = Mathf.Max(previousBestStars, clampedStars);
        bool campaignCompletedNow = false;

        var tx = new RewardTransaction
        {
            RewardId = primaryRewardId,
            RequireUniqueRewardId = !string.IsNullOrEmpty(primaryRewardId),
            AdditionalRewardIdsToMark = extraRewardIds.Count > 0 ? extraRewardIds.ToArray() : null,
            RejectIfSkillCapExceeded = false,
            Payout = payout,
            Reason = !wasCompletedBefore
                ? $"LevelFirstClear_{levelId}"
                : (newlyUnlockedStarsCount > 0 ? $"LevelReplayStarUpgrade_{levelId}" : $"LevelReplayNoReward_{levelId}"),
            OnBeforeSaveMutateProfile = p =>
            {
                if (!p.completedLevelIDs.Contains(levelId))
                    p.completedLevelIDs.Add(levelId);

                p.SetLevelStarsBest(levelId, clampedStars);
                resultingBestStars = p.GetLevelStars(levelId);

                if (movesUsed > 0)
                {
                    isNewBestMoves = p.SetLevelBestMoves(levelId, movesUsed);
                }
                resultingBestMoves = p.GetBestMoves(levelId);

                if (!usedAnySkill)
                {
                    p.lifetimeNoSkillLevelClears = Mathf.Max(0, p.lifetimeNoSkillLevelClears + 1);
                }

                if (nextLevelToUnlock != null &&
                    !string.IsNullOrEmpty(nextLevelToUnlock.LevelId) &&
                    !p.unlockedLevelIDs.Contains(nextLevelToUnlock.LevelId))
                {
                    p.unlockedLevelIDs.Add(nextLevelToUnlock.LevelId);
                    unlockedNextLevel = true;
                }

                if (campaignDb != null && !p.campaignAllLevelsCompletedReported && campaignDb.AreAllCampaignLevelsCompleted(p))
                {
                    p.campaignAllLevelsCompletedReported = true;
                    campaignCompletedNow = true;
                }
            }
        };

        RewardTransactionResult txResult = RewardService.ExecuteTransaction(tx);
        if (!txResult.Succeeded)
        {
            Debug.LogWarning($"[CampaignEconomyService] Settlement transaction failed for '{levelId}': {txResult.Status} - {txResult.DiagnosticMessage}");
            return new LevelEconomySettlement(
                levelId,
                sectorNumber,
                !wasCompletedBefore,
                previousBestStars,
                clampedStars,
                previousBestStars,
                0,
                movesUsed,
                previousBestMoves,
                false,
                usedAnySkill,
                0,
                0,
                false,
                0,
                0,
                false,
                new RewardBundle());
        }

        if (unlockedNextLevel && nextLevelToUnlock != null && ProgressionManager.Instance != null)
        {
            // ProgressionManager.OnProgressionChanged was already invoked by RewardService; fire level unlock hook if needed.
            ProgressionManager.Instance.UnlockLevel(nextLevelToUnlock);
        }

        var settlement = new LevelEconomySettlement(
            levelId: levelId,
            sectorNumber: sectorNumber,
            isFirstClear: !wasCompletedBefore,
            previousBestStars: previousBestStars,
            earnedStars: clampedStars,
            newBestStars: resultingBestStars,
            newlyUnlockedStarsCount: newlyUnlockedStarsCount,
            movesUsed: movesUsed,
            bestMoves: resultingBestMoves,
            isNewBestMoves: isNewBestMoves,
            usedAnySkill: usedAnySkill,
            firstClearCoinsGranted: firstClearCoins,
            replayStarUpgradeCoinsGranted: replayStarUpgradeCoins,
            sectorCompletedThisRun: willCompleteSector,
            sectorCompletionCoinsGranted: sectorCoins,
            sectorCompletionGemsGranted: sectorGems,
            campaignCompletedThisRun: campaignCompletedNow,
            grantedBundle: payout);

        OnLevelEconomySettled?.Invoke(settlement);
        return settlement;
    }

    private static bool WillAllLevelsInWorldBeCompleted(WorldData world, ProfileData profile, string newlyCompletedLevelId)
    {
        if (world?.levels == null || profile?.completedLevelIDs == null)
            return false;

        int validCount = 0;
        for (int i = 0; i < world.levels.Length; i++)
        {
            LevelData lvl = world.levels[i];
            if (lvl == null || string.IsNullOrEmpty(lvl.LevelId))
                continue;

            validCount++;
            if (lvl.LevelId == newlyCompletedLevelId)
                continue;

            if (!profile.completedLevelIDs.Contains(lvl.LevelId))
                return false;
        }

        return validCount > 0;
    }
}

