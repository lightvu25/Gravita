#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor utility to generate/update New Player, Daily, Sector, and Achievement
/// <see cref="MissionDefinition"/> assets and register them into
/// <c>Assets/Data/Missions/MissionDatabase_Main.asset</c> while preserving all
/// existing weekly missions and milestone rewards (Gravita Economy GDD §10 & §11).
/// </summary>
public static class MissionDataEditorTool
{
    private const string MissionsFolderPath = "Assets/Data/Missions";
    private const string DatabaseAssetPath = "Assets/Data/Missions/MissionDatabase_Main.asset";

    private readonly struct MissionSeed
    {
        public readonly string AssetFileName;
        public readonly string MissionId;
        public readonly string Title;
        public readonly string Description;
        public readonly MissionCategory Category;
        public readonly MissionService.MissionType Type;
        public readonly int Target;
        public readonly int RewardAmount;
        public readonly MissionRewardType RewardType;
        public readonly int RequiredSector;
        public readonly string PowerUpId;

        public MissionSeed(
            string assetFileName,
            string missionId,
            string title,
            string description,
            MissionCategory category,
            MissionService.MissionType type,
            int target,
            int rewardAmount,
            MissionRewardType rewardType = MissionRewardType.Coins,
            int requiredSector = 1,
            string powerUpId = PowerUpIds.Breaker)
        {
            AssetFileName = assetFileName;
            MissionId = missionId;
            Title = title;
            Description = description;
            Category = category;
            Type = type;
            Target = target;
            RewardAmount = rewardAmount;
            RewardType = rewardType;
            RequiredSector = Mathf.Max(1, requiredSector);
            PowerUpId = powerUpId ?? PowerUpIds.Breaker;
        }
    }

    private static readonly MissionSeed[] ProposedMissions =
    {
        // New Player (8)
        new MissionSeed("Mission_New_01.asset", "new_01", "Welcome to Gravita", "Launch and log into Gravita for the first time", MissionCategory.NewPlayer, MissionService.MissionType.FirstLogin, 1, 50),
        new MissionSeed("Mission_New_02.asset", "new_02", "First Steps", "Complete 1 level", MissionCategory.NewPlayer, MissionService.MissionType.CompleteLevels, 1, 50),
        new MissionSeed("Mission_New_03.asset", "new_03", "Coin Collector", "Collect 20 coins in completed levels", MissionCategory.NewPlayer, MissionService.MissionType.CollectCoins, 20, 60),
        new MissionSeed("Mission_New_04.asset", "new_04", "Star Hunter", "Collect 3 stars", MissionCategory.NewPlayer, MissionService.MissionType.EarnStars, 3, 75),
        new MissionSeed("Mission_New_05.asset", "new_05", "Power Up!", "Activate 1 Power Skill", MissionCategory.NewPlayer, MissionService.MissionType.UsePowerUp, 1, 75),
        new MissionSeed("Mission_New_06.asset", "new_06", "Break the Wall", "Break 1 wall in a completed level", MissionCategory.NewPlayer, MissionService.MissionType.BreakWalls, 1, 80),
        new MissionSeed("Mission_New_07.asset", "new_07", "Survivor", "Complete 1 level without touching a hazard or enemy", MissionCategory.NewPlayer, MissionService.MissionType.CompleteLevelsWithoutDying, 1, 100),
        new MissionSeed("Mission_New_08.asset", "new_08", "Maze Explorer", "Complete 5 levels (including replays)", MissionCategory.NewPlayer, MissionService.MissionType.CompleteLevels, 5, 150),

        // Daily (7 — Coin & Skill Charge rewards only per GDD §10.1)
        new MissionSeed("Mission_Daily_01.asset", "daily_01", "Daily Login", "Log in to Gravita today", MissionCategory.Daily, MissionService.MissionType.DailyLogin, 1, 40),
        new MissionSeed("Mission_Daily_02.asset", "daily_02", "Coin Rush", "Collect 30 coins in completed levels today", MissionCategory.Daily, MissionService.MissionType.CollectCoins, 30, 60),
        new MissionSeed("Mission_Daily_03.asset", "daily_03", "Level Runner", "Complete 3 levels today", MissionCategory.Daily, MissionService.MissionType.CompleteLevels, 3, 75),
        new MissionSeed("Mission_Daily_04.asset", "daily_04", "Star Collector", "Collect 5 stars today", MissionCategory.Daily, MissionService.MissionType.EarnStars, 5, 80),
        new MissionSeed("Mission_Daily_05.asset", "daily_05", "Power User", "Activate 3 Power Skills today", MissionCategory.Daily, MissionService.MissionType.UsePowerUp, 3, 80),
        new MissionSeed("Mission_Daily_06.asset", "daily_06", "Untouchable", "Complete 1 level today without touching a hazard or enemy", MissionCategory.Daily, MissionService.MissionType.CompleteLevelsWithoutDying, 1, 90),
        new MissionSeed("Mission_Daily_07.asset", "daily_07", "Break Everything", "Break 5 walls in completed levels today", MissionCategory.Daily, MissionService.MissionType.BreakWalls, 5, 1, MissionRewardType.PowerUp, 1, PowerUpIds.Breaker),

        // Sector Missions (3 — Gated by RequiredSector per GDD §10.1 / §11)
        new MissionSeed("Mission_Sector_01.asset", "sector_m_01", "Sector 1 Pioneer", "Complete all levels in Sector 1", MissionCategory.Sector, MissionService.MissionType.CompleteSector, 1, 120, MissionRewardType.Coins, 1),
        new MissionSeed("Mission_Sector_02.asset", "sector_m_02", "Sector 2 Navigator", "Complete all levels in Sector 2", MissionCategory.Sector, MissionService.MissionType.CompleteSector, 2, 180, MissionRewardType.Coins, 2),
        new MissionSeed("Mission_Sector_03.asset", "sector_m_03", "Sector 3 Master", "Complete all levels in Sector 3", MissionCategory.Sector, MissionService.MissionType.CompleteSector, 3, 25, MissionRewardType.Gems, 3),

        // Achievements (9 — Major milestones grant Gems or Coins per GDD §11)
        new MissionSeed("Mission_Ach_01.asset", "ach_01", "Coin Beginner", "Collect 100 coins across completed levels", MissionCategory.Achievement, MissionService.MissionType.CollectCoins, 100, 100),
        new MissionSeed("Mission_Ach_02.asset", "ach_02", "Coin Master", "Collect 1,000 coins across completed levels", MissionCategory.Achievement, MissionService.MissionType.CollectCoins, 1000, 500),
        new MissionSeed("Mission_Ach_03.asset", "ach_03", "Level Rookie", "Complete 10 levels", MissionCategory.Achievement, MissionService.MissionType.CompleteLevels, 10, 150),
        new MissionSeed("Mission_Ach_04.asset", "ach_04", "Maze Veteran", "Complete 30 Campaign levels", MissionCategory.Achievement, MissionService.MissionType.CompleteLevels, 30, 30, MissionRewardType.Gems),
        new MissionSeed("Mission_Ach_05.asset", "ach_05", "Star Collector", "Collect 30 stars", MissionCategory.Achievement, MissionService.MissionType.EarnStars, 30, 20, MissionRewardType.Gems),
        new MissionSeed("Mission_Ach_06.asset", "ach_06", "Power Specialist", "Use all 3 Campaign Power Skills at least once", MissionCategory.Achievement, MissionService.MissionType.UseAllPowerUpTypes, 3, 300),
        new MissionSeed("Mission_Ach_07.asset", "ach_07", "Untouchable", "Complete 10 levels without touching a hazard or enemy", MissionCategory.Achievement, MissionService.MissionType.CompleteLevelsWithoutDying, 10, 350),
        new MissionSeed("Mission_Ach_08.asset", "ach_08", "Wall Breaker", "Break 50 walls across completed levels", MissionCategory.Achievement, MissionService.MissionType.BreakWalls, 50, 350),
        new MissionSeed("Mission_Ach_09.asset", "ach_09", "Pure Tactician", "Complete 10 levels without using any Power Skill", MissionCategory.Achievement, MissionService.MissionType.CompleteLevelsWithoutSkills, 10, 20, MissionRewardType.Gems)
    };

    [MenuItem("Tools/Gravita/Generate Mission Data Assets")]
    public static void GenerateOrUpdateMissionAssets()
    {
        EnsureFolderExists("Assets", "Data");
        EnsureFolderExists("Assets/Data", "Missions");

        MissionDatabase database = AssetDatabase.LoadAssetAtPath<MissionDatabase>(DatabaseAssetPath);
        Sprite coinIcon = LoadSpriteFromExistingMission("Assets/Data/Missions/Mission_BankCoins.asset");
        Sprite starIcon = LoadSpriteFromExistingMission("Assets/Data/Missions/Mission_CollectStars.asset");
        Sprite levelIcon = LoadSpriteFromExistingMission("Assets/Data/Missions/Mission_ClearLevels.asset");

        int createdCount = 0;
        int updatedCount = 0;

        for (int i = 0; i < ProposedMissions.Length; i++)
        {
            MissionSeed seed = ProposedMissions[i];
            string assetPath = $"{MissionsFolderPath}/{seed.AssetFileName}";
            MissionDefinition def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(assetPath);
            bool isNew = false;

            if (def == null)
            {
                def = ScriptableObject.CreateInstance<MissionDefinition>();
                isNew = true;
            }

            Sprite icon = seed.Type switch
            {
                MissionService.MissionType.CollectCoins => coinIcon,
                MissionService.MissionType.DailyLogin => coinIcon,
                MissionService.MissionType.FirstLogin => coinIcon,
                MissionService.MissionType.EarnStars => starIcon,
                _ => levelIcon != null ? levelIcon : coinIcon
            };

            def.Configure(
                id: seed.MissionId,
                missionCategory: seed.Category,
                displayTitle: seed.Title,
                displayDescription: seed.Description,
                missionType: seed.Type,
                targetValue: seed.Target,
                rewardKind: seed.RewardType,
                rewardValue: seed.RewardAmount,
                powerUpId: seed.PowerUpId,
                day: 1,
                weeklyPoints: 0,
                missionIcon: icon,
                rewardSprite: coinIcon,
                requiredSector: seed.RequiredSector);

            if (isNew)
            {
                AssetDatabase.CreateAsset(def, assetPath);
                createdCount++;
            }
            else
            {
                EditorUtility.SetDirty(def);
                updatedCount++;
            }

            if (database != null)
            {
                database.AddOrReplaceMission(def);
            }
        }

        if (database != null)
        {
            SanitizeLegacyDormantSkillRewards(database);
            EditorUtility.SetDirty(database);
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[MissionDataEditorTool] Completed: {createdCount} created, {updatedCount} updated. Registered in '{DatabaseAssetPath}'.");
    }

    private static void SanitizeLegacyDormantSkillRewards(MissionDatabase database)
    {
        if (database == null) return;

        if (database.Missions != null)
        {
            for (int i = 0; i < database.Missions.Count; i++)
            {
                MissionDefinition def = database.Missions[i];
                if (def == null) continue;
                SerializedObject so = new SerializedObject(def);
                SerializedProperty prop = so.FindProperty("rewardPowerUpId");
                if (prop != null && !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(prop.stringValue))
                {
                    prop.stringValue = PowerUpIds.Breaker;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(def);
                }
            }
        }

        if (database.Milestones != null)
        {
            for (int i = 0; i < database.Milestones.Count; i++)
            {
                MissionMilestoneDefinition milestone = database.Milestones[i];
                if (milestone == null) continue;
                SerializedObject so = new SerializedObject(milestone);
                SerializedProperty prop = so.FindProperty("powerUpId");
                if (prop != null && !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(prop.stringValue))
                {
                    prop.stringValue = PowerUpIds.Breaker;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(milestone);
                }
            }
        }

        if (database.GoalRewards != null)
        {
            for (int i = 0; i < database.GoalRewards.Count; i++)
            {
                WeeklyGoalReward goal = database.GoalRewards[i];
                if (goal != null && !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(goal.powerUpId))
                {
                    goal.powerUpId = PowerUpIds.Breaker;
                }
            }
        }
    }

    private static Sprite LoadSpriteFromExistingMission(string path)
    {
        MissionDefinition def = AssetDatabase.LoadAssetAtPath<MissionDefinition>(path);
        return def != null ? def.Icon : null;
    }

    private static void EnsureFolderExists(string parentPath, string folderName)
    {
        string combined = $"{parentPath}/{folderName}";
        if (!AssetDatabase.IsValidFolder(combined))
        {
            AssetDatabase.CreateFolder(parentPath, folderName);
        }
    }
}
#endif
