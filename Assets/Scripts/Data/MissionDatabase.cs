using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class WeeklyGoalReward
{
    public string goalId = "weekly_goal_1";
    [Tooltip("Weekly mission points required to reach and claim this goal reward.")]
    [Min(1)]
    public int requiredPoints = 50;

    [Header("Reward (Coin, Gem, PowerUp, or Cosmetic)")]
    [Min(1)]
    public int rewardAmount = 100;
    public MissionRewardType rewardType = MissionRewardType.Coins;
    [Tooltip("Power Skill ID granted when rewardType is PowerUp ('breaker', 'freeze', 'teleport').")]
    public string powerUpId = PowerUpIds.Breaker;
    [Tooltip("Cosmetic ID granted when rewardType is Cosmetic.")]
    public string cosmeticId = string.Empty;

    [Header("Milestone Button Sprites (Incomplete vs Complete)")]
    [Tooltip("Sprite shown on the milestone button when requiredPoints has not been reached yet.")]
    public Sprite incompleteSprite;
    [Tooltip("Sprite shown on the milestone button when requiredPoints is reached (complete).")]
    public Sprite completeSprite;
    public Sprite rewardIcon;

    public string GetFormattedRewardText()
    {
        switch (rewardType)
        {
            case MissionRewardType.Gems:
                return rewardAmount == 1 ? "1 Gem" : $"{rewardAmount} Gems";
            case MissionRewardType.PowerUp:
                string label = PowerUpIds.FormatDisplayName(powerUpId);
                return rewardAmount == 1 ? $"1 {label}" : $"{rewardAmount}x {label}";
            case MissionRewardType.Cosmetic:
                return "1 Skin";
            case MissionRewardType.UnlockSkill:
                return $"Unlock {PowerUpIds.FormatDisplayName(powerUpId)}";
            default:
                return rewardAmount == 1 ? "1 Coin" : $"{rewardAmount} Coins";
        }
    }
}

[CreateAssetMenu(fileName = "MissionDatabase", menuName = "Game/Mission/Mission Database")]
public class MissionDatabase : ScriptableObject
{
    [Header("Weekly Milestones (ScriptableObjects — Concrete Completed Task Counts)")]
    [SerializeField] private List<MissionMilestoneDefinition> milestones = new List<MissionMilestoneDefinition>();

    [Header("Legacy Weekly Goal Rewards (Kept for Migration Compatibility)")]
    [SerializeField] private List<WeeklyGoalReward> goalRewards = new List<WeeklyGoalReward>();

    [Header("All Missions (Daily, Weekly, Sector, NewPlayer, Achievement)")]
    [SerializeField] private List<MissionDefinition> missions = new List<MissionDefinition>();

    public IReadOnlyList<MissionMilestoneDefinition> Milestones => milestones;
    public IReadOnlyList<WeeklyGoalReward> GoalRewards => goalRewards;
    public IReadOnlyList<MissionDefinition> Missions => missions;

    public List<MissionDefinition> GetMissionsForDay(int dayIndex)
    {
        int clampedDay = Mathf.Clamp(dayIndex, 1, 7);
        var result = new List<MissionDefinition>();
        if (missions == null) return result;

        for (int i = 0; i < missions.Count; i++)
        {
            var m = missions[i];
            if (m != null && !m.IsMonthlyCumulativeWeekly && m.Category == MissionCategory.Weekly && m.DayIndex == clampedDay)
            {
                result.Add(m);
            }
        }

        return result;
    }

    public bool HasMonthlyCumulativeWeeklyMissions()
    {
        if (missions == null) return false;
        for (int i = 0; i < missions.Count; i++)
        {
            var m = missions[i];
            if (m != null && m.IsMonthlyCumulativeWeekly)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns all cumulative Weekly missions (<see cref="MissionDefinition.IsMonthlyCumulativeWeekly"/>)
    /// unlocked up to <paramref name="currentWeekOfMonth"/> (1..4).
    /// Each week of the month adds 2-3 new missions while keeping earlier weeks' missions active until month end.
    /// </summary>
    public List<MissionDefinition> GetCumulativeWeeklyMissions(int currentWeekOfMonth)
    {
        int clampedWeek = Mathf.Clamp(currentWeekOfMonth, 1, 4);
        var result = new List<MissionDefinition>();
        if (missions == null) return result;

        for (int i = 0; i < missions.Count; i++)
        {
            var m = missions[i];
            if (m != null && m.IsMonthlyCumulativeWeekly && m.WeekOfMonth <= clampedWeek)
            {
                result.Add(m);
            }
        }

        return result;
    }

    public List<MissionDefinition> GetMissionsByCategory(MissionCategory category)
    {
        var result = new List<MissionDefinition>();
        if (missions == null) return result;

        for (int i = 0; i < missions.Count; i++)
        {
            var m = missions[i];
            if (m != null && m.Category == category)
            {
                result.Add(m);
            }
        }

        return result;
    }

    public void AddOrReplaceMission(MissionDefinition definition)
    {
        if (definition == null || string.IsNullOrEmpty(definition.MissionId))
            return;

        missions ??= new List<MissionDefinition>();
        for (int i = 0; i < missions.Count; i++)
        {
            if (missions[i] != null && missions[i].MissionId == definition.MissionId)
            {
                missions[i] = definition;
                return;
            }
        }

        missions.Add(definition);
    }

    public void SetMilestones(IEnumerable<MissionMilestoneDefinition> definitions)
    {
        milestones ??= new List<MissionMilestoneDefinition>();
        milestones.Clear();
        if (definitions == null)
            return;

        foreach (var def in definitions)
        {
            if (def != null)
                milestones.Add(def);
        }

        milestones.Sort((a, b) => a.RequiredCompletedMissions.CompareTo(b.RequiredCompletedMissions));
    }

    public void AddOrReplaceMilestone(MissionMilestoneDefinition definition)
    {
        if (definition == null || string.IsNullOrEmpty(definition.MilestoneId))
            return;

        milestones ??= new List<MissionMilestoneDefinition>();
        for (int i = 0; i < milestones.Count; i++)
        {
            if (milestones[i] != null && milestones[i].MilestoneId == definition.MilestoneId)
            {
                milestones[i] = definition;
                milestones.Sort((a, b) => a.RequiredCompletedMissions.CompareTo(b.RequiredCompletedMissions));
                return;
            }
        }

        milestones.Add(definition);
        milestones.Sort((a, b) => a.RequiredCompletedMissions.CompareTo(b.RequiredCompletedMissions));
    }

    public int GetWeeklyMissionCount()
    {
        int count = 0;
        if (missions != null)
        {
            for (int i = 0; i < missions.Count; i++)
            {
                if (missions[i] != null && missions[i].Category == MissionCategory.Weekly)
                    count++;
            }
        }
        return count;
    }

    public int GetMaxMilestoneRequiredMissions()
    {
        int max = 0;
        if (milestones != null)
        {
            for (int i = 0; i < milestones.Count; i++)
            {
                if (milestones[i] != null && milestones[i].RequiredCompletedMissions > max)
                    max = milestones[i].RequiredCompletedMissions;
            }
        }

        if (max > 0)
            return max;

        int weeklyCount = GetWeeklyMissionCount();
        return weeklyCount > 0 ? weeklyCount : 14;
    }

    public int GetMaxGoalPoints()
    {
        int max = 0;
        if (goalRewards != null)
        {
            for (int i = 0; i < goalRewards.Count; i++)
            {
                if (goalRewards[i] != null && goalRewards[i].requiredPoints > max)
                {
                    max = goalRewards[i].requiredPoints;
                }
            }
        }
        return max > 0 ? max : 100;
    }
}
