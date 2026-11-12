using System;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
public class MissionAndClockGuardTests : EconomyTestHarnessBase
{
    [Test]
    public void DailyMissions_ResetAtLocalMidnight_AndBlockReclaimWhenSystemClockRolledBackward()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var clock = new FixedLocalClockProvider(new DateTime(2026, 5, 6, 23, 50, 0, DateTimeKind.Local)); // Wednesday 23:50
        LocalClock.Provider = clock;

        var dailyLogin = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        dailyLogin.Configure(
            id: "daily_login_test",
            missionCategory: MissionCategory.Daily,
            displayTitle: "Daily Login",
            displayDescription: "Log in today",
            missionType: MissionService.MissionType.DailyLogin,
            targetValue: 1,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 40);

        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsTrue(MissionService.IsComplete(dailyLogin));
        Assert.IsTrue(MissionService.TryClaim(dailyLogin));
        Assert.AreEqual(40, economy.Coins);
        Assert.IsFalse(MissionService.TryClaim(dailyLogin), "Cannot claim twice in same week.");

        // Thursday 00:05 of the same week -> stays claimed until Monday 00:00 weekly reset!
        clock.Current = new DateTime(2026, 5, 7, 0, 5, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsTrue(MissionService.IsClaimed(dailyLogin), "Daily/Weekday missions keep their state throughout the week and reset on Monday 00:00!");

        // Advance to Monday 00:05 of next week -> Weekly reset fires!
        clock.Current = new DateTime(2026, 5, 11, 0, 5, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsFalse(MissionService.IsClaimed(dailyLogin), "Daily/Weekday mission must reset on Monday 00:00 local time!");
        Assert.IsTrue(MissionService.TryClaim(dailyLogin));
        Assert.AreEqual(80, economy.Coins);

        // Case #24: Rewind device clock back to Sunday -> rollback detected, re-claim strictly blocked!
        clock.Current = new DateTime(2026, 5, 10, 12, 0, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsTrue(MissionService.IsClockRolledBack);
        Assert.IsFalse(MissionService.TryClaim(dailyLogin), "Rewinding system clock must NEVER allow re-claiming periodic missions!");
        Assert.AreEqual(80, economy.Coins);
    }

    [Test]
    public void CumulativeWeeklyMissions_AddPerWeekOfMonth_PreserveProgressAcrossWeeks_AndResetAtMonthEnd()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        // Day 3 of May 2026 -> Week 1 of month
        var clock = new FixedLocalClockProvider(new DateTime(2026, 5, 3, 10, 0, 0, DateTimeKind.Local));
        LocalClock.Provider = clock;
        MissionService.DebugOverrideWeekOfMonth = 0;
        MissionService.EnsureProfileMigratedAndUpdated();

        Assert.AreEqual(1, MissionService.GetCurrentWeekOfMonth());

        var db = TrackAsset(ScriptableObject.CreateInstance<MissionDatabase>());
        var w1Clear3 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        w1Clear3.Configure("cum_w1", MissionCategory.Weekly, "W1 Clear 3", "Clear 3", MissionService.MissionType.CompleteLevels, 3, MissionRewardType.Coins, 100, weekOfMonthIndex: 1);
        var w2Clear5 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        w2Clear5.Configure("cum_w2", MissionCategory.Weekly, "W2 Clear 5", "Clear 5", MissionService.MissionType.CompleteLevels, 5, MissionRewardType.Gems, 5, weekOfMonthIndex: 2);

        db.AddOrReplaceMission(w1Clear3);
        db.AddOrReplaceMission(w2Clear5);

        // In Week 1: only w1Clear3 is unlocked/visible; w2Clear5 is not yet unlocked
        Assert.AreEqual(1, db.GetCumulativeWeeklyMissions(MissionService.GetCurrentWeekOfMonth()).Count);
        Assert.IsTrue(MissionService.IsMissionUnlocked(w1Clear3));
        Assert.IsFalse(MissionService.IsMissionUnlocked(w2Clear5));

        // Complete 2 levels in Week 1 (unfinished: 2/3)
        MissionService.BeginLevelRun(501);
        MissionService.CommitCurrentLevelRunCompletion();
        MissionService.BeginLevelRun(502);
        MissionService.CommitCurrentLevelRunCompletion();
        Assert.AreEqual(2, MissionService.GetProgress(w1Clear3));
        Assert.IsFalse(MissionService.IsComplete(w1Clear3));

        // Advance to Day 11 of May 2026 -> Week 2 of month (and a new Monday!)
        clock.Current = new DateTime(2026, 5, 11, 10, 0, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.AreEqual(2, MissionService.GetCurrentWeekOfMonth());

        // Week 2 adds w2Clear5 (now 2 missions in cumulative list), AND w1Clear3 STILL preserves its 2/3 progress!
        Assert.AreEqual(2, db.GetCumulativeWeeklyMissions(MissionService.GetCurrentWeekOfMonth()).Count);
        Assert.AreEqual(2, MissionService.GetProgress(w1Clear3), "Unfinished Week 1 mission must preserve progress in Week 2!");
        Assert.AreEqual(2, MissionService.GetProgress(w2Clear5));

        // Complete 1 more level in Week 2 -> w1Clear3 reaches 3/3 and can be claimed!
        MissionService.BeginLevelRun(503);
        MissionService.CommitCurrentLevelRunCompletion();
        Assert.IsTrue(MissionService.IsComplete(w1Clear3));
        Assert.IsTrue(MissionService.TryClaim(w1Clear3));
        Assert.AreEqual(100, economy.Coins);

        // Advance to June 1, 2026 00:05 -> New month resets cumulative weekly missions and monthly progress!
        clock.Current = new DateTime(2026, 6, 1, 0, 5, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.AreEqual(1, MissionService.GetCurrentWeekOfMonth());
        Assert.IsFalse(MissionService.IsClaimed(w1Clear3), "Cumulative Weekly missions must reset at the start of a new month!");
        Assert.AreEqual(0, MissionService.GetProgress(w1Clear3));
    }

    [Test]
    public void LootPackDefinition_SoftAndHardPity_PersistsAndGuaranteesDrops()
    {
        var pack = TrackAsset(ScriptableObject.CreateInstance<LootPackDefinition>());
        pack.Configure(
            "test_pity_pack",
            "Test Pity Pack",
            new[]
            {
                new LootEntry
                {
                    entryId = "common_coin",
                    displayName = "50 Coins",
                    rewardType = RewardType.Coins,
                    minAmount = 50,
                    maxAmount = 50,
                    baseDropRate = 90f,
                    enablePity = false
                },
                new LootEntry
                {
                    entryId = "rare_teleport",
                    displayName = "1x Teleport",
                    rewardType = RewardType.PowerUp,
                    itemId = PowerUpIds.Teleport,
                    minAmount = 1,
                    maxAmount = 1,
                    baseDropRate = 10f,
                    enablePity = true,
                    softPityStartPull = 2,
                    softPityBonusPerPull = 15f,
                    hardPityGuaranteedPull = 4,
                    pityPriority = 10
                }
            });

        Assert.IsTrue(pack.AreBaseDropRatesValid(), "Base drop rates must sum to 100%.");

        var profile = SaveManager.loadProfile();
        var rareEntry = pack.Entries[1];

        // 0 misses: effective rate is base 10%
        float[] r0 = pack.ComputeEffectiveDropRates(profile, out int hp0);
        Assert.AreEqual(-1, hp0);
        Assert.AreEqual(10f, r0[1], 0.01f);
        // 2 misses (>= softPityStartPull 2): 10 + 1*15 = 25%
        profile.SetLootPackPity(pack.PackId, "rare_teleport", 2);
        float[] r2 = pack.ComputeEffectiveDropRates(profile, out int hp2);
        Assert.AreEqual(-1, hp2);
        Assert.AreEqual(25f, r2[1], 0.01f);
        // Simulate 3 misses on rare_teleport -> pity counter = 3
        profile.SetLootPackPity(pack.PackId, "rare_teleport", 3);
        Assert.AreEqual(3, profile.GetLootPackPity(pack.PackId, "rare_teleport"));

        // Pull 4 hits hardPityGuaranteedPull (4) -> guaranteed to roll rare_teleport and reset its pity to 0!
        Assert.IsTrue(pack.TryRollSingle(profile, out LootRollResult roll4, allowGems: true, customRng: new System.Random(1), mutateProfilePity: true));
        Assert.IsTrue(roll4.TriggeredHardPity, "4th pull must trigger hard pity guarantee!");
        Assert.AreEqual("rare_teleport", roll4.Entry.entryId);
        Assert.AreEqual(0, profile.GetLootPackPity(pack.PackId, "rare_teleport"), "Pity must reset to 0 after dropping the item!");
    }

    [Test]
    public void WeeklyMissions_GameplayAvailableFullWeek_WeekdayLoginMatchesActualDay_AndResetMondayMidnight()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        // Tuesday (dayIndex = 2) of Week 1
        var clock = new FixedLocalClockProvider(new DateTime(2026, 5, 5, 10, 0, 0, DateTimeKind.Local));
        LocalClock.Provider = clock;
        MissionService.EnsureProfileMigratedAndUpdated();

        // GDD §10.2: All days 1..7 of the week are unlocked for browsing and gameplay tasks!
        for (int day = 1; day <= 7; day++)
        {
            Assert.IsTrue(MissionService.IsDayUnlocked(day), $"Expected Weekly day {day} to be unlocked without consecutive-login lock.");
        }

        // Weekday-specific DailyLogin only completes for Tuesday (day 2), NOT Sunday (day 7) yet!
        var tuesdayLoginMission = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        tuesdayLoginMission.Configure(
            id: "wk_tue_login",
            missionCategory: MissionCategory.Weekly,
            displayTitle: "Tuesday Login",
            displayDescription: "Log in on Tuesday",
            missionType: MissionService.MissionType.DailyLogin,
            targetValue: 1,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 50,
            day: 2,
            weeklyPoints: 25);

        var sundayLoginMission = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        sundayLoginMission.Configure(
            id: "wk_sun_login",
            missionCategory: MissionCategory.Weekly,
            displayTitle: "Sunday Login",
            displayDescription: "Log in on Sunday",
            missionType: MissionService.MissionType.DailyLogin,
            targetValue: 1,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 50,
            day: 7,
            weeklyPoints: 25);

        Assert.IsTrue(MissionService.IsComplete(tuesdayLoginMission));
        Assert.IsFalse(MissionService.IsComplete(sundayLoginMission), "Sunday login must NOT auto-complete on Tuesday!");

        // Meanwhile, a Sunday gameplay mission IS completable on Tuesday without waiting for Sunday!
        var sundayClearMission = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        sundayClearMission.Configure(
            id: "wk_sun_clear",
            missionCategory: MissionCategory.Weekly,
            displayTitle: "Clear 1 Level",
            displayDescription: "Clear 1 level during the week",
            missionType: MissionService.MissionType.CompleteLevels,
            targetValue: 1,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 75,
            day: 7,
            weeklyPoints: 25);

        MissionService.BeginLevelRun(101);
        MissionService.CommitCurrentLevelRunCompletion();

        Assert.IsTrue(MissionService.TryClaim(sundayClearMission));
        Assert.AreEqual(75, economy.Coins);
        Assert.AreEqual(1, MissionService.GetWeeklyCompletedMissionCount());

        // Sunday 23:59 of the same week -> Sunday login now completes too, and earlier claim stays claimed
        clock.Current = new DateTime(2026, 5, 10, 23, 59, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsTrue(MissionService.IsClaimed(sundayClearMission));
        Assert.IsTrue(MissionService.IsComplete(sundayLoginMission));

        // Monday 00:01 of next week -> Weekly reset fires!
        clock.Current = new DateTime(2026, 5, 11, 0, 1, 0, DateTimeKind.Local);
        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.AreEqual(0, MissionService.GetWeeklyCompletedMissionCount());
        Assert.IsFalse(MissionService.IsClaimed(sundayClearMission));
    }

    [Test]
    public void WeeklyMilestones_ScriptableObjectThresholds_CountConcreteClaimedMissionsAndGrantRewards()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var clock = new FixedLocalClockProvider(new DateTime(2026, 5, 4, 9, 0, 0, DateTimeKind.Local)); // Monday
        LocalClock.Provider = clock;
        MissionService.EnsureProfileMigratedAndUpdated();

        var db = TrackAsset(ScriptableObject.CreateInstance<MissionDatabase>());
        var m1 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        m1.Configure("wk_m1", MissionCategory.Weekly, "Mon Login", "Login", MissionService.MissionType.DailyLogin, 1, MissionRewardType.Coins, 20, day: 1);
        var m2 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        m2.Configure("wk_m2", MissionCategory.Weekly, "Clear 1", "Clear", MissionService.MissionType.CompleteLevels, 1, MissionRewardType.Coins, 20, day: 1);
        var m3 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        m3.Configure("wk_m3", MissionCategory.Weekly, "Clear 2", "Clear", MissionService.MissionType.CompleteLevels, 2, MissionRewardType.Coins, 20, day: 2);

        db.AddOrReplaceMission(m1);
        db.AddOrReplaceMission(m2);
        db.AddOrReplaceMission(m3);

        var milestone3 = TrackAsset(ScriptableObject.CreateInstance<MissionMilestoneDefinition>());
        milestone3.Configure("milestone_3_tasks", "3 Tasks", 3, MissionRewardType.Gems, 5);
        db.AddOrReplaceMilestone(milestone3);

        Assert.AreEqual(0, MissionService.GetWeeklyCompletedMissionCount(db));
        Assert.IsFalse(MissionService.IsMilestoneComplete(milestone3, db));

        // Claim m1 -> count = 1/3
        Assert.IsTrue(MissionService.TryClaim(m1));
        Assert.AreEqual(1, MissionService.GetWeeklyCompletedMissionCount(db));
        Assert.IsFalse(MissionService.CanClaimMilestone(milestone3, db));

        // Complete 2 levels and claim m2 + m3 -> count = 3/3!
        MissionService.BeginLevelRun(201);
        MissionService.CommitCurrentLevelRunCompletion();
        MissionService.BeginLevelRun(202);
        MissionService.CommitCurrentLevelRunCompletion();

        Assert.IsTrue(MissionService.TryClaim(m2));
        Assert.IsTrue(MissionService.TryClaim(m3));
        Assert.AreEqual(3, MissionService.GetWeeklyCompletedMissionCount(db));
        Assert.IsTrue(MissionService.CanClaimMilestone(milestone3, db));

        // Claim ScriptableObject milestone -> +5 Gems, cannot claim twice!
        Assert.IsTrue(MissionService.TryClaimMilestone(milestone3, db));
        Assert.AreEqual(5, economy.Gems);
        Assert.IsTrue(MissionService.IsMilestoneClaimed(milestone3));
        Assert.IsFalse(MissionService.TryClaimMilestone(milestone3, db));
    }

    [Test]
    public void SectorGating_BlocksHigherSectorMissionsUntilUnlocked_AndAchievementsTrack3ActiveSkills()
    {
        var s3Mission = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        s3Mission.Configure(
            id: "sector3_only",
            missionCategory: MissionCategory.Sector,
            displayTitle: "Sector 3 Task",
            displayDescription: "Requires Sector 3",
            missionType: MissionService.MissionType.DailyLogin,
            targetValue: 1,
            rewardKind: MissionRewardType.Gems,
            rewardValue: 15,
            requiredSector: 3);

        MissionService.EnsureProfileMigratedAndUpdated();
        Assert.IsFalse(MissionService.IsMissionEligibleForCurrentProgression(s3Mission));
        Assert.IsFalse(MissionService.TryClaim(s3Mission));

        // Standalone AchievementDefinition + AchievementService tracks all 3 active skills and grants one-time rewards
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var allSkillsAch = TrackAsset(ScriptableObject.CreateInstance<AchievementDefinition>());
        allSkillsAch.Configure(
            id: "ach_3_skills",
            displayTitle: "Power Specialist",
            displayDescription: "Use all 3 active skills",
            objectiveType: MissionService.MissionType.UseAllPowerUpTypes,
            targetValue: 3,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 300);

        MissionService.RecordPowerUpActivated(PowerUpType.Breaker);
        MissionService.RecordPowerUpActivated(PowerUpType.Freeze);
        Assert.AreEqual(2, AchievementService.GetProgress(allSkillsAch));
        Assert.IsFalse(AchievementService.IsComplete(allSkillsAch));

        MissionService.RecordPowerUpActivated(PowerUpType.Teleport);
        Assert.AreEqual(3, AchievementService.GetProgress(allSkillsAch));
        Assert.IsTrue(AchievementService.IsComplete(allSkillsAch));
        Assert.IsTrue(AchievementService.TryClaim(allSkillsAch));
        Assert.AreEqual(300, economy.Coins);
        Assert.IsTrue(AchievementService.IsClaimed(allSkillsAch));
        Assert.IsFalse(AchievementService.TryClaim(allSkillsAch), "Achievements are one-time permanent rewards.");
    }

    [Test]
    public void MissionRowWidget_ClaimHidesLabel_ActivatesTickImage_AndMovesRowToBottom()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        MissionService.EnsureProfileMigratedAndUpdated();

        var container = CreateTestGameObject("RowContainer").AddComponent<RectTransform>();
        var row1Go = CreateTestGameObject("Row1");
        row1Go.transform.SetParent(container, false);
        var row2Go = CreateTestGameObject("Row2");
        row2Go.transform.SetParent(container, false);

        var row1 = row1Go.AddComponent<MissionRowWidget>();
        var btnGo = CreateTestGameObject("ClaimBtn");
        btnGo.transform.SetParent(row1Go.transform, false);
        var claimBtn = btnGo.AddComponent<Button>();

        var labelGo = CreateTestGameObject("ClaimLabel");
        labelGo.transform.SetParent(btnGo.transform, false);
        var claimLabel = labelGo.AddComponent<TextMeshProUGUI>();
        claimLabel.text = "CLAIM";

        var tickGo = CreateTestGameObject("ClaimTick");
        tickGo.transform.SetParent(btnGo.transform, false);
        var tickImage = tickGo.AddComponent<Image>();

        SetPrivateField(row1, "claimButton", claimBtn);
        SetPrivateField(row1, "claimButtonLabel", claimLabel);
        SetPrivateField(row1, "claimedTickImage", tickImage);

        var m1 = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        m1.Configure("m1_test", MissionCategory.Daily, "Login", "Login", MissionService.MissionType.DailyLogin, 1, MissionRewardType.Coins, 50);

        // Before claim: claimButton active, label active ("CLAIM"), tick hidden, row1 is first sibling (index 0)
        row1.Setup(m1, null);
        Assert.IsTrue(claimBtn.gameObject.activeSelf);
        Assert.IsTrue(claimLabel.gameObject.activeSelf);
        Assert.AreEqual("CLAIM", claimLabel.text);
        Assert.IsFalse(tickImage.gameObject.activeSelf);
        Assert.AreEqual(0, row1.transform.GetSiblingIndex());

        // Claim the mission via MissionService and re-bind -> hides claimButton, shows tickImage!
        Assert.IsTrue(MissionService.TryClaim(m1));
        row1.Setup(m1, null);
        Assert.IsFalse(claimBtn.gameObject.activeSelf, "Claimed mission must hide claimButton.");
        Assert.IsTrue(tickImage.gameObject.activeSelf, "Claimed mission must activate claimedTickImage.");
        Assert.IsTrue(tickImage.gameObject.activeInHierarchy, "claimedTickImage must stay active in hierarchy even when claimButton is hidden.");
        Assert.AreEqual(1f, tickImage.color.a, 0.001f, "Tick icon opacity must remain 100%.");
    }

    [Test]
    public void UIManager_GlitchBackdrop_PreservesSpriteColor_AndShowsBehindPopupPanels()
    {
        var uiManagerGo = CreateTestGameObject("UIManager");
        var uiManager = uiManagerGo.AddComponent<UIManager>();

        var glitchGo = CreateTestGameObject("GlitchBackdrop");
        var glitchRect = glitchGo.AddComponent<RectTransform>();
        glitchRect.SetParent(uiManagerGo.transform, false);
        var glitchImage = glitchGo.AddComponent<Image>();
        var tex = TrackAsset(new Texture2D(4, 4));
        var spr = TrackAsset(Sprite.Create(tex, new Rect(0, 0, 4, 4), new Vector2(0.5f, 0.5f)));
        glitchImage.sprite = spr;
        glitchImage.color = Color.white;

        var shopGo = CreateTestGameObject("ShopPopup");
        shopGo.AddComponent<RectTransform>().SetParent(uiManagerGo.transform, false);
        var shopPanel = shopGo.AddComponent<ShopUI>();

        var settingGo = CreateTestGameObject("SettingPopup");
        settingGo.AddComponent<RectTransform>().SetParent(uiManagerGo.transform, false);
        var settingPanel = settingGo.AddComponent<SettingUI>();

        var mappings = new System.Collections.Generic.List<UIManager.UIPanelMapping>
        {
            new UIManager.UIPanelMapping { panelType = UIPanelType.Shop, layer = UILayer.Popup, panelObject = shopGo, disableGlitchBackdrop = false },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Setting, layer = UILayer.Popup, panelObject = settingGo, disableGlitchBackdrop = true }
        };

        SetPrivateField(uiManager, "panelMappings", mappings);
        SetPrivateField(uiManager, "glitchBackdrop", glitchRect);

        InvokeLifecycleIfPresent(uiManager, "Awake");

        // Must preserve white color when sprite is assigned!
        Assert.AreEqual(Color.white, glitchImage.color, "GlitchBackdrop with assigned Sprite must preserve its Inspector color (1,1,1,1)!");

        uiManager.OpenPanel(UIPanelType.Setting);
        Assert.IsTrue(glitchRect.gameObject.activeSelf, "Shared GlitchBackdrop must activate behind Setting popup!");

        uiManager.CloseAllPopups();
        Assert.IsFalse(glitchRect.gameObject.activeSelf, "Shared GlitchBackdrop must hide when all popups close!");
    }

    [Test]
    public void LoginUI_UsesTMPInputFields_AndKeepsInputsInteractableOnOpen()
    {
        var loginGo = CreateTestGameObject("LoginPopup");
        var loginUI = loginGo.AddComponent<LoginUI>();

        var emailGo = CreateTestGameObject("EmailInput");
        emailGo.transform.SetParent(loginGo.transform, false);
        var emailInput = emailGo.AddComponent<TMP_InputField>();

        var passGo = CreateTestGameObject("PassInput");
        passGo.transform.SetParent(loginGo.transform, false);
        var passInput = passGo.AddComponent<TMP_InputField>();

        SetPrivateField(loginUI, "emailInput", emailInput);
        SetPrivateField(loginUI, "passwordInput", passInput);

        loginUI.Show();
        Assert.IsTrue(emailInput.interactable, "Email TMP_InputField must remain interactable when LoginUI opens!");
        Assert.IsTrue(passInput.interactable, "Password TMP_InputField must remain interactable when LoginUI opens!");
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private static void InvokePrivateMethod(object target, string methodName)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(method, $"Method '{methodName}' not found on {target.GetType().Name}");
        method.Invoke(target, null);
    }
}
