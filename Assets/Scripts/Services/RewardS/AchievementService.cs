using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Standalone lifetime Achievement service decoupled from <see cref="MissionPanelUI"/>.
/// Evaluates lifetime player statistics and executes atomic reward transactions via <see cref="RewardService"/>.
/// </summary>
public static class AchievementService
{
    public static event Action OnAchievementProgressChanged;
    public static event Action<string, RewardBundle> OnAchievementRewardClaimed;

    private static bool claimInProgress;
    private static bool eventsForwarded;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureInitialized()
    {
        EnsureEventForwarding();
    }

    private static void EnsureEventForwarding()
    {
        if (eventsForwarded) return;
        MissionService.OnMissionProgressChanged += HandleMissionProgressChanged;
        eventsForwarded = true;
    }

    private static void HandleMissionProgressChanged()
    {
        OnAchievementProgressChanged?.Invoke();
    }

    public static bool IsEligibleForCurrentProgression(AchievementDefinition achievement)
    {
        if (achievement == null)
            return false;

        ProfileData profile = MissionService.EnsureProfileMigratedAndUpdated();
        int highestUnlockedSector = GetHighestUnlockedSector(profile);
        if (achievement.RequiredUnlockedSector > highestUnlockedSector)
            return false;

        if (achievement.RewardType == MissionRewardType.PowerUp &&
            !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(achievement.RewardPowerUpId))
        {
            return false;
        }

        return true;
    }

    public static int GetProgress(AchievementDefinition achievement)
    {
        if (achievement == null)
            return 0;
        if (!IsEligibleForCurrentProgression(achievement))
            return 0;

        int rawProgress = MissionService.GetProgress(achievement.Type, 1, MissionCategory.Achievement);
        if (IsClaimed(achievement))
            return Mathf.Max(rawProgress, achievement.Target);

        return rawProgress;
    }

    public static bool IsClaimed(AchievementDefinition achievement)
    {
        if (achievement == null || string.IsNullOrEmpty(achievement.AchievementId))
            return false;

        ProfileData profile = MissionService.EnsureProfileMigratedAndUpdated();
        if (profile == null)
            return false;

        string rewardId = EconomyRewardIds.Achievement(achievement.AchievementId);
        if (!string.IsNullOrEmpty(rewardId) && profile.HasClaimedReward(rewardId))
            return true;

        return profile.claimedAchievementIds != null
               && profile.claimedAchievementIds.Contains(achievement.AchievementId);
    }

    public static bool IsComplete(AchievementDefinition achievement)
    {
        if (achievement == null)
            return false;
        if (!IsEligibleForCurrentProgression(achievement))
            return false;

        return IsClaimed(achievement)
               || MissionService.GetProgress(achievement.Type, 1, MissionCategory.Achievement) >= achievement.Target;
    }

    public static bool TryClaim(AchievementDefinition achievement)
    {
        if (claimInProgress || MissionService.SimulateRewardFailureForTesting)
            return false;
        if (achievement == null || string.IsNullOrEmpty(achievement.AchievementId))
            return false;
        if (!IsEligibleForCurrentProgression(achievement))
            return false;
        if (IsClaimed(achievement) || !IsComplete(achievement))
            return false;

        EnsureEventForwarding();
        claimInProgress = true;
        try
        {
            ProfileData profile = MissionService.EnsureProfileMigratedAndUpdated();
            profile.Sanitize();

            string id = achievement.AchievementId;
            if (profile.claimedAchievementIds.Contains(id))
                return false;

            string rewardId = EconomyRewardIds.Achievement(id);
            RewardBundle payout = BuildRewardBundle(
                achievement.RewardType,
                achievement.RewardAmount,
                achievement.RewardPowerUpId,
                achievement.RewardCosmeticId);

            var tx = new RewardTransaction
            {
                RewardId = rewardId,
                RequireUniqueRewardId = true,
                Payout = payout,
                RejectIfSkillCapExceeded = achievement.RewardType == MissionRewardType.PowerUp,
                RejectIfCosmeticAlreadyOwned = false,
                Reason = $"AchievementClaim_{id}",
                OnBeforeSaveMutateProfile = p =>
                {
                    p.Sanitize();
                    if (!p.claimedAchievementIds.Contains(id))
                        p.claimedAchievementIds.Add(id);
                }
            };

            RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
            if (!result.Succeeded)
                return false;

            OnAchievementRewardClaimed?.Invoke(id, payout);
            OnAchievementProgressChanged?.Invoke();
            return true;
        }
        finally
        {
            claimInProgress = false;
        }
    }

    public static int GetClaimedCount(AchievementDatabase database)
    {
        if (database == null || database.Achievements == null)
            return 0;

        int count = 0;
        IReadOnlyList<AchievementDefinition> list = database.Achievements;
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i] != null && IsClaimed(list[i]))
                count++;
        }
        return count;
    }

    private static int GetHighestUnlockedSector(ProfileData profile)
    {
        if (ProgressionManager.Instance != null)
            return ProgressionManager.Instance.GetHighestUnlockedSectorNumber();

        if (profile?.completedLevelIDs != null)
        {
            if (profile.completedLevelIDs.Count >= 20)
                return 3;
            if (profile.completedLevelIDs.Count >= 10)
                return 2;
        }
        return 1;
    }

    public static RewardBundle BuildRewardBundle(
        MissionRewardType rewardType,
        int rewardAmount,
        string powerUpId,
        string cosmeticId)
    {
        var bundle = new RewardBundle();
        switch (rewardType)
        {
            case MissionRewardType.Coins:
                if (rewardAmount > 0)
                    bundle.Add(RewardType.Coins, rewardAmount);
                break;

            case MissionRewardType.Gems:
                if (rewardAmount > 0)
                    bundle.Add(RewardType.Gems, rewardAmount);
                break;

            case MissionRewardType.PowerUp:
                string safeSkill = !string.IsNullOrWhiteSpace(powerUpId) ? powerUpId : PowerUpIds.Breaker;
                if (rewardAmount > 0 && EconomyBalanceConfig.Active.IsSkillActiveInCampaign(safeSkill))
                    bundle.Add(RewardType.PowerUp, rewardAmount, safeSkill);
                break;

            case MissionRewardType.Cosmetic:
                if (!string.IsNullOrWhiteSpace(cosmeticId))
                    bundle.Add(RewardType.Cosmetic, 1, cosmeticId);
                break;

            case MissionRewardType.UnlockSkill:
                string unlockSkill = !string.IsNullOrWhiteSpace(powerUpId) ? powerUpId : PowerUpIds.Shield;
                bundle.Add(RewardType.SkillUnlock, 1, unlockSkill);
                break;
        }
        return bundle;
    }
}

