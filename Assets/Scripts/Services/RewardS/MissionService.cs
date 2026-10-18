using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static mission logic — evaluates progress for serialized mission entries
/// and handles claiming rewards into the profile. Progress sources:
/// completed level count, total stars, or current coin balance.
/// </summary>
public static class MissionService
{
    public enum MissionType { CompleteLevels, EarnStars, CollectCoins }

    [Serializable]
    public class MissionEntry
    {
        public string missionId;
        public string title = "Mission";
        public MissionType type = MissionType.CompleteLevels;
        [Min(1)] public int target = 1;
        [Min(0)] public int rewardCoins = 25;
        [Min(0)] public int rewardGems = 0;
        [Tooltip("Icon art — assign your mission sprite.")]
        public Sprite icon;
    }

    public static int GetProgress(MissionEntry mission)
    {
        switch (mission.type)
        {
            case MissionType.CompleteLevels:
                return ProgressionManager.Instance != null ? ProgressionManager.Instance.GetCompletedLevelCount() : 0;
            case MissionType.EarnStars:
                return ProgressionManager.Instance != null ? ProgressionManager.Instance.TotalStars : 0;
            case MissionType.CollectCoins:
                return EconomyManager.Instance != null ? EconomyManager.Instance.Coins : 0;
            default:
                return 0;
        }
    }

    public static bool IsClaimed(MissionEntry mission)
    {
        var profile = GameSession.Instance != null ? GameSession.Instance.currentProfile : null;
        return profile != null && profile.claimedMissionIds != null && profile.claimedMissionIds.Contains(mission.missionId);
    }

    public static bool IsComplete(MissionEntry mission) => GetProgress(mission) >= mission.target;

    /// <summary>Grants the mission reward once. Returns false if already claimed or incomplete.</summary>
    public static bool TryClaim(MissionEntry mission)
    {
        if (mission == null || string.IsNullOrEmpty(mission.missionId)) return false;
        if (IsClaimed(mission) || !IsComplete(mission)) return false;

        var profile = GameSession.Instance != null ? GameSession.Instance.currentProfile : null;
        if (profile == null) return false;

        if (profile.claimedMissionIds == null)
            profile.claimedMissionIds = new List<string>();
        profile.claimedMissionIds.Add(mission.missionId);

        var rewardService = RewardService.Instance != null ? RewardService.Instance : UnityEngine.Object.FindAnyObjectByType<RewardService>();
        if (rewardService != null && (mission.rewardCoins > 0 || mission.rewardGems > 0))
        {
            var bundle = new RewardBundle();
            if (mission.rewardCoins > 0) bundle.Add(RewardType.Coins, mission.rewardCoins);
            if (mission.rewardGems > 0) bundle.Add(RewardType.Gems, mission.rewardGems);
            rewardService.GrantRewardBundle(bundle, $"Mission_{mission.missionId}");
        }

        SaveManager.saveProfile(profile);
        return true;
    }
}
