using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// EditMode functional verification suite for:
///   1. Bootstrapped Live-Ops Event ScriptableObject assets (`Assets/Data/Events/`).
///   2. MainMenuUI bottom tab swap (`EventTabBtn` -> `UIPanelType.Event` Screen layer without GlitchBackdrop).
///   3. EventPanelUI Banner feed (`Image`, `Title`, `Description`, `Days Left` via `LocalClock`) and Banner click opening `EventDetailPopupUI`.
///   4. EventDetailPopupUI task claims via `RewardService`, idempotency, slider progress, and CTA navigation.
///   5. Merged `MissionCategory.Achievement` tab inside `MissionPanelUI` switching top header progress cleanly and claiming lifetime achievements.
/// </summary>
public class EventAndAchievementMergeTests : EconomyTestHarnessBase
{
    [OneTimeSetUp]
    public void OneTimeBootstrapEvents()
    {
        EventAssetBootstrap.EnsureEventAssetsExist(forceRebuild: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [TearDown]
    public override void BaseTearDown()
    {
        Time.timeScale = 1f;
        base.BaseTearDown();
    }

    [Test]
    public void BootstrapEventAssets_ExistAndValidateCleanly()
    {
        EventDatabase db = AssetDatabase.LoadAssetAtPath<EventDatabase>(EventAssetBootstrap.EventDatabaseAssetPath);
        GameEventDefinition neonRush = AssetDatabase.LoadAssetAtPath<GameEventDefinition>(EventAssetBootstrap.NeonMazeRushAssetPath);
        GameEventDefinition starHunt = AssetDatabase.LoadAssetAtPath<GameEventDefinition>(EventAssetBootstrap.SectorStarHuntAssetPath);

        Assert.IsNotNull(db, "EventDatabase_Main.asset should be generated.");
        Assert.IsNotNull(neonRush, "Event_NeonMazeRush.asset should be generated.");
        Assert.IsNotNull(starHunt, "Event_SectorStarHunt.asset should be generated.");
        Assert.AreEqual(2, db.Events.Count);
        Assert.GreaterOrEqual(neonRush.Tasks.Count, 3);
        Assert.GreaterOrEqual(starHunt.Tasks.Count, 3);
    }

    [Test]
    public void MainMenuEventTab_OpensEventOverlay_RendersBannersAndDaysLeft_AndOpensEventDetailPopup()
    {
        var clock = new FixedLocalClockProvider(new DateTime(2026, 11, 16, 0, 0, 0));
        LocalClock.Provider = clock;

        // UIManager + GlitchBackdrop + Panels
        GameObject uiMgrGo = CreateTestGameObject("UIManager");
        UIManager uiMgr = uiMgrGo.AddComponent<UIManager>();
        GameObject backdropGo = CreateTestGameObject("GlitchBackdrop");
        backdropGo.AddComponent<RectTransform>();
        backdropGo.SetActive(false);
        SetPrivateField(uiMgr, "glitchBackdrop", backdropGo.GetComponent<RectTransform>());

        GameObject mapGo = CreateTestGameObject("CampaignMapOverlay");
        mapGo.AddComponent<RectTransform>();
        mapGo.AddComponent<CampaignMapUI>();

        GameObject mainMenuGo = CreateTestGameObject("MainMenuScreen");
        mainMenuGo.AddComponent<RectTransform>();
        MainMenuUI mainMenu = mainMenuGo.AddComponent<MainMenuUI>();
        Button mapTabBtn = CreateTestButton("MapTabBtn", mainMenuGo.transform);
        Button eventTabBtn = CreateTestButton("EventTabBtn", mainMenuGo.transform);
        SetPrivateField(mainMenu, "mapTabButton", mapTabBtn);
        SetPrivateField(mainMenu, "eventsTabButton", eventTabBtn);

        // EventOverlay (EventPanelUI) + BannerCard Prefab
        GameObject eventGo = CreateTestGameObject("EventOverlay");
        eventGo.AddComponent<RectTransform>();
        EventPanelUI eventPanel = eventGo.AddComponent<EventPanelUI>();
        GameObject bannerContainer = CreateTestGameObject("BannerContainer", eventGo.transform);
        bannerContainer.AddComponent<RectTransform>();

        EventBannerCardWidget bannerTemplate = CreateBannerTemplate("BannerTemplate", null,
            out TMP_Text _, out TMP_Text _, out TMP_Text _, out GameObject _, out Button _);

        EventDatabase db = TrackAsset(ScriptableObject.CreateInstance<EventDatabase>());
        GameEventDefinition ev1 = TrackAsset(ScriptableObject.CreateInstance<GameEventDefinition>());
        ev1.Configure(
            id: "ev_rolling_7d",
            displayTitle: "NEON RUSH",
            shortDescription: "Slide through endless neon rooms.",
            rulesText: "Clear stitched runner rooms to earn event coins.",
            fixedDateSchedule: false,
            isoStartDate: string.Empty,
            daysDuration: 7,
            hideExpired: false,
            bannerClickAction: EventBannerActionType.OpenEventDetailPopup,
            detailCtaLabel: "GO TO MAP",
            detailCtaAction: EventBannerActionType.JumpToPanel,
            detailCtaPanel: UIPanelType.Map,
            detailCtaRunnerMode: RunnerModeKind.Endless,
            eventTasks: new[]
            {
                new GameEventTaskEntry
                {
                    taskId = "t_coins",
                    title = "Collect 50 Coins",
                    objectiveKind = EventObjectiveKind.MissionTypeStat,
                    missionType = MissionService.MissionType.CollectCoins,
                    target = 50,
                    rewardType = MissionRewardType.Coins,
                    rewardAmount = 150,
                },
                new GameEventTaskEntry
                {
                    taskId = "t_runner",
                    title = "Best Room 4",
                    objectiveKind = EventObjectiveKind.RunnerBestRooms,
                    missionType = MissionService.MissionType.CompleteLevels,
                    target = 4,
                    rewardType = MissionRewardType.Coins,
                    rewardAmount = 200,
                }
            });

        GameEventDefinition ev2ExpiredHidden = TrackAsset(ScriptableObject.CreateInstance<GameEventDefinition>());
        ev2ExpiredHidden.Configure(
            id: "ev_expired_hidden",
            displayTitle: "OLD EVENT",
            shortDescription: "Should stay hidden when expired.",
            rulesText: "Expired",
            fixedDateSchedule: true,
            isoStartDate: "2026-10-01",
            daysDuration: 5,
            hideExpired: true,
            bannerClickAction: EventBannerActionType.OpenEventDetailPopup,
            detailCtaLabel: "PLAY",
            detailCtaAction: EventBannerActionType.JumpToPanel,
            detailCtaPanel: UIPanelType.Map,
            detailCtaRunnerMode: RunnerModeKind.Endless,
            eventTasks: null);

        db.SetEvents(new[] { ev1, ev2ExpiredHidden });

        // EventDetailPopupUI
        GameObject detailGo = CreateTestGameObject("EventDetailPopup");
        detailGo.AddComponent<RectTransform>();
        EventDetailPopupUI detailPopup = detailGo.AddComponent<EventDetailPopupUI>();
        TMP_Text detailTitle = CreateTestText("DetailTitle", detailGo.transform);
        TMP_Text detailRules = CreateTestText("DetailRules", detailGo.transform);
        TMP_Text detailDaysLeft = CreateTestText("DetailDaysLeft", detailGo.transform);
        Slider detailSlider = CreateTestSlider("DetailSlider", detailGo.transform);
        TMP_Text detailProgText = CreateTestText("DetailProgText", detailGo.transform);
        GameObject taskContainer = CreateTestGameObject("TaskContainer", detailGo.transform);
        taskContainer.AddComponent<RectTransform>();
        MissionRowWidget taskRowPrefab = CreateRowPrefab("EventTaskRowPrefab", null, out _);
        Button ctaBtn = CreateTestButton("CtaBtn", detailGo.transform);
        TMP_Text ctaLabel = CreateTestText("CtaLabel", ctaBtn.transform);
        Button detailCloseBtn = CreateTestButton("DetailCloseBtn", detailGo.transform);

        SetPrivateField(detailPopup, "titleText", detailTitle);
        SetPrivateField(detailPopup, "rulesDescriptionText", detailRules);
        SetPrivateField(detailPopup, "daysLeftText", detailDaysLeft);
        SetPrivateField(detailPopup, "eventProgressSlider", detailSlider);
        SetPrivateField(detailPopup, "eventProgressText", detailProgText);
        SetPrivateField(detailPopup, "taskRowContainer", taskContainer.transform);
        SetPrivateField(detailPopup, "taskRowPrefab", taskRowPrefab.gameObject);
        SetPrivateField(detailPopup, "ctaActionButton", ctaBtn);
        SetPrivateField(detailPopup, "ctaActionLabel", ctaLabel);
        SetPrivateField(detailPopup, "closeButton", detailCloseBtn);
        InvokeLifecycleIfPresent(detailPopup, "Awake");

        SetPrivateField(eventPanel, "eventDatabase", db);
        SetPrivateField(eventPanel, "bannerContainer", bannerContainer.transform);
        SetPrivateField(eventPanel, "bannerCardPrefab", bannerTemplate.gameObject);
        SetPrivateField(eventPanel, "eventDetailPopup", detailPopup);
        InvokeLifecycleIfPresent(eventPanel, "Awake");

        var mappings = new List<UIManager.UIPanelMapping>
        {
            new UIManager.UIPanelMapping { panelType = UIPanelType.MainMenu, panelObject = mainMenuGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Map, panelObject = mapGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Event, panelObject = eventGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.EventDetail, panelObject = detailGo, layer = UILayer.Popup },
        };
        SetPrivateField(uiMgr, "panelMappings", mappings);
        InvokeLifecycleIfPresent(uiMgr, "Awake");

        InvokeLifecycleIfPresent(mainMenu, "Awake");
        InvokeLifecycleIfPresent(mainMenu, "Start");

        // Grant enough coins stat so t_coins is ready to claim & shows claimableBadge
        ProfileData profile = SaveManager.loadProfile();
        profile.lifetimeCoinsCollected = 75;
        SaveManager.TrySaveProfile(profile);

        // 1. Click Event tab on MainMenu bottom bar
        eventTabBtn.onClick.Invoke();
        Assert.AreEqual(UIPanelType.Event, uiMgr.CurrentScreen);
        Assert.IsTrue(eventGo.activeSelf);
        Assert.IsFalse(backdropGo.activeSelf, "Event tab is a Screen overlay and must not trigger GlitchBackdrop.");
        Assert.AreEqual(1, eventPanel.SpawnedCards.Count, "Expired hidden events must be filtered out.");

        EventBannerCardWidget card = eventPanel.SpawnedCards[0];
        TMP_Text cardTitle = GetPrivateField<TMP_Text>(card, "titleText");
        TMP_Text cardDesc = GetPrivateField<TMP_Text>(card, "descriptionText");
        TMP_Text cardDaysLeft = GetPrivateField<TMP_Text>(card, "daysLeftText");
        GameObject badge = GetPrivateField<GameObject>(card, "claimableBadge");

        Assert.AreEqual("NEON RUSH", cardTitle.text);
        Assert.AreEqual("Slide through endless neon rooms.", cardDesc.text);
        Assert.AreEqual("7D 0H LEFT", cardDaysLeft.text);
        Assert.IsTrue(badge.activeSelf, "Claimable badge must show when an event task is ready.");

        // 2. Advance clock by 4d 12h and verify live countdown updates cleanly
        clock.Current = clock.Current.AddDays(4).AddHours(12);
        eventPanel.RefreshBanners();
        card = eventPanel.SpawnedCards[0];
        cardDaysLeft = GetPrivateField<TMP_Text>(card, "daysLeftText");
        Assert.AreEqual("2D 12H LEFT", cardDaysLeft.text);

        // 3. Click banner -> pushes EventDetailPopup & binds tasks
        card.BannerButton.onClick.Invoke();
        Assert.IsTrue(uiMgr.IsPanelOpen(UIPanelType.EventDetail));
        Assert.IsTrue(detailGo.activeSelf);
        Assert.IsTrue(backdropGo.activeSelf, "EventDetailPopup is a modal Popup and must activate GlitchBackdrop.");
        Assert.AreEqual("NEON RUSH", detailTitle.text);
        Assert.AreEqual("2D 12H LEFT", detailDaysLeft.text);
        Assert.AreEqual("0/2", detailProgText.text);
        Assert.AreEqual(2, detailPopup.SpawnedRows.Count);
        Assert.AreEqual("GO TO MAP", ctaLabel.text);

        // 4. Claim completed task row atomically via RewardService
        MissionRowWidget coinRow = detailPopup.SpawnedRows[0];
        Assert.AreEqual("t_coins", coinRow.EventTaskId);
        Assert.IsTrue(coinRow.ClaimButton.interactable);

        coinRow.ClaimButton.onClick.Invoke();
        Assert.AreEqual(150, SaveManager.loadProfile().coins, "Event claim must grant 150 coins atomically.");
        Assert.IsTrue(EventService.IsTaskClaimed(ev1, ev1.Tasks[0]));
        Assert.AreEqual("1/2", detailProgText.text);
        Assert.AreEqual(1f, detailSlider.value, 0.01f);
        Assert.IsFalse(EventService.TryClaimTask(ev1, ev1.Tasks[0]), "Duplicate claim must fail idempotently.");

        // 5. Clicking CTA jumps cleanly to target panel (UIPanelType.Map)
        ctaBtn.onClick.Invoke();
        Assert.IsFalse(uiMgr.IsPanelOpen(UIPanelType.EventDetail));
        Assert.AreEqual(UIPanelType.Map, uiMgr.CurrentScreen);

        // 6. Advancing clock past 7 days marks event ENDED and blocks late claims
        clock.Current = new DateTime(2026, 11, 25, 0, 0, 0);
        Assert.IsFalse(EventService.IsEventActive(ev1));
        Assert.AreEqual("ENDED", EventService.FormatRemainingTime(ev1));
        profile = SaveManager.loadProfile();
        profile.bestRunnerEndlessRooms = 10;
        SaveManager.TrySaveProfile(profile);
        Assert.IsFalse(EventService.TryClaimTask(ev1, ev1.Tasks[1]), "Expired events must block late task claims.");
    }

    [Test]
    public void StandaloneAchievementPanel_UpdatesSummaryHeader_ClaimsSkillUnlock_AndMissionPanelStaysDedicated()
    {
        AchievementDatabase achDb = AssetDatabase.LoadAssetAtPath<AchievementDatabase>("Assets/Data/Achievements/AchievementDatabase_Main.asset");
        MissionDatabase missionDb = AssetDatabase.LoadAssetAtPath<MissionDatabase>("Assets/Data/Missions/MissionDatabase_Main.asset");
        Assert.IsNotNull(achDb);
        Assert.IsNotNull(missionDb);

        // Standalone AchievementPanelUI setup
        GameObject achGo = CreateTestGameObject("AchievementOverlay");
        achGo.AddComponent<RectTransform>();
        AchievementPanelUI achPanel = achGo.AddComponent<AchievementPanelUI>();

        Slider topSlider = CreateTestSlider("AchSlider", achGo.transform);
        TMP_Text topProgressText = CreateTestText("AchProgressText", achGo.transform);
        GameObject achContainer = CreateTestGameObject("AchRowContainer", achGo.transform);
        achContainer.AddComponent<RectTransform>();
        achContainer.AddComponent<VerticalLayoutGroup>();

        AchievementRowWidget achRowPrefab = CreateAchievementRowPrefab("AchRowPrefab", null);

        SetPrivateField(achPanel, "achievementDatabase", achDb);
        SetPrivateField(achPanel, "totalCompletionSlider", topSlider);
        SetPrivateField(achPanel, "totalCompletionText", topProgressText);
        SetPrivateField(achPanel, "rowContainer", achContainer.transform);
        SetPrivateField(achPanel, "rowPrefab", achRowPrefab.gameObject);

        // Dedicated MissionPanelUI keeps Weekly & Daily without Achievement merging
        GameObject panelGo = CreateTestGameObject("MissionOverlay");
        panelGo.AddComponent<RectTransform>();
        MissionPanelUI missionPanel = panelGo.AddComponent<MissionPanelUI>();

        Slider missionSlider = CreateTestSlider("WeeklySlider", panelGo.transform);
        TMP_Text missionProgressText = CreateTestText("WeeklyProgressText", panelGo.transform);
        TMP_Text resetTimeText = CreateTestText("TimeText", panelGo.transform);
        Button chestBtn = CreateTestButton("ChestBtn", panelGo.transform);
        GameObject milestoneGo = CreateTestGameObject("Milestone1", panelGo.transform);
        milestoneGo.AddComponent<RectTransform>();
        MissionMilestoneWidget milestoneWidget = milestoneGo.AddComponent<MissionMilestoneWidget>();

        Button weeklyTabBtn = CreateTestButton("Tab_Weekly", panelGo.transform);
        TMP_Text weeklyTabLabel = CreateTestText("WeeklyLabel", weeklyTabBtn.transform);
        Button dailyTabBtn = CreateTestButton("Tab_Daily", panelGo.transform);
        TMP_Text dailyTabLabel = CreateTestText("DailyLabel", dailyTabBtn.transform);

        var tabs = new List<MissionCategoryTabUI>
        {
            new MissionCategoryTabUI { category = MissionCategory.Weekly, button = weeklyTabBtn, categoryLabel = weeklyTabLabel },
            new MissionCategoryTabUI { category = MissionCategory.Daily, button = dailyTabBtn, categoryLabel = dailyTabLabel },
        };

        GameObject rowContainer = CreateTestGameObject("RowContainer", panelGo.transform);
        rowContainer.AddComponent<RectTransform>();
        rowContainer.AddComponent<VerticalLayoutGroup>();
        MissionRowWidget rowPrefab = CreateRowPrefab("MissionRowPrefab", null, out _);

        SetPrivateField(missionPanel, "missionDatabase", missionDb);
        SetPrivateField(missionPanel, "categoryTabs", tabs);
        SetPrivateField(missionPanel, "weeklyProgressSlider", missionSlider);
        SetPrivateField(missionPanel, "weeklyProgressText", missionProgressText);
        SetPrivateField(missionPanel, "weeklyResetTimeText", resetTimeText);
        SetPrivateField(missionPanel, "topRewardChestButton", chestBtn);
        SetPrivateField(missionPanel, "milestoneWidgets", new List<MissionMilestoneWidget> { milestoneWidget });
        SetPrivateField(missionPanel, "rowContainer", rowContainer.transform);
        SetPrivateField(missionPanel, "rowPrefab", rowPrefab.gameObject);

        // Make Sector-1 achievements complete (15 valid clears -> Achievement_03 unlocks Shield!)
        ProfileData profile = SaveManager.loadProfile();
        profile.coins = 0;
        profile.completedLevelIDs.Add("Level_001");
        profile.lifetimeValidLevelClears = 15;
        SaveManager.TrySaveProfile(profile);

        InvokeLifecycleIfPresent(achPanel, "Awake");
        achPanel.Show();

        int eligibleTotal = 0;
        for (int i = 0; i < achDb.Achievements.Count; i++)
        {
            if (AchievementService.IsEligibleForCurrentProgression(achDb.Achievements[i]))
                eligibleTotal++;
        }
        Assert.Greater(eligibleTotal, 0);
        Assert.AreEqual($"0/{eligibleTotal}", topProgressText.text);

        AchievementRowWidget ach03Row = null;
        for (int i = 0; i < achPanel.SpawnedRows.Count; i++)
        {
            AchievementRowWidget r = achPanel.SpawnedRows[i];
            if (r != null && r.AchievementDefinition != null && r.AchievementDefinition.AchievementId == "ach_03")
            {
                ach03Row = r;
                break;
            }
        }
        Assert.IsNotNull(ach03Row, "Expected Achievement_03 row in standalone AchievementPanelUI.");
        Assert.IsTrue(ach03Row.ClaimButton.interactable);
        ach03Row.ClaimButton.onClick.Invoke();

        Assert.IsTrue(AchievementService.IsClaimed(ach03Row.AchievementDefinition));
        Assert.IsTrue(SaveManager.loadProfile().HasSkill(PowerUpIds.Shield), "Claiming Achievement_03 must unlock Inertia Core ('shield').");
        Assert.AreEqual($"1/{eligibleTotal}", topProgressText.text);
        Assert.AreEqual(1f, topSlider.value, 0.01f);

        // Verify MissionPanelUI stays Weekly/Daily dedicated with top bar decorations intact
        InvokeLifecycleIfPresent(missionPanel, "Awake");
        missionPanel.Show();
        Assert.AreEqual(MissionCategory.Weekly, missionPanel.SelectedCategory);
        Assert.IsTrue(resetTimeText.gameObject.activeSelf);
        Assert.IsTrue(chestBtn.gameObject.activeSelf);
        Assert.IsTrue(milestoneWidget.gameObject.activeSelf);
    }

    private AchievementRowWidget CreateAchievementRowPrefab(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name, parent);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(800f, 180f);
        go.SetActive(false);

        TMP_Text title = CreateTestText("Title", go.transform);
        TMP_Text desc = CreateTestText("Description", go.transform);
        TMP_Text progText = CreateTestText("ProgressText", go.transform);
        TMP_Text rewText = CreateTestText("RewardText", go.transform);
        Slider slider = CreateTestSlider("Slider", go.transform);
        Button claimBtn = CreateTestButton("ClaimBtn", go.transform);
        TMP_Text claimLabel = CreateTestText("ClaimLabel", claimBtn.transform);
        claimLabel.text = "CLAIM";

        GameObject tickGo = CreateTestGameObject("Tick", go.transform);
        tickGo.AddComponent<RectTransform>();
        Image tickImg = tickGo.AddComponent<Image>();

        AchievementRowWidget row = go.AddComponent<AchievementRowWidget>();
        SetPrivateField(row, "titleText", title);
        SetPrivateField(row, "descriptionText", desc);
        SetPrivateField(row, "progressSlider", slider);
        SetPrivateField(row, "progressText", progText);
        SetPrivateField(row, "rewardText", rewText);
        SetPrivateField(row, "claimButton", claimBtn);
        SetPrivateField(row, "claimButtonLabel", claimLabel);
        SetPrivateField(row, "claimedTickImage", tickImg);
        return row;
    }

    // ─────────────────────────────────────────────────────────────
    //  Local Test UI Builders
    // ─────────────────────────────────────────────────────────────

    private EventBannerCardWidget CreateBannerTemplate(
        string name,
        Transform parent,
        out TMP_Text title,
        out TMP_Text desc,
        out TMP_Text daysLeft,
        out GameObject badge,
        out Button btn)
    {
        GameObject go = CreateTestGameObject(name, parent);
        go.AddComponent<RectTransform>();
        go.SetActive(false);
        Image bg = go.AddComponent<Image>();
        btn = go.AddComponent<Button>();
        btn.targetGraphic = bg;

        title = CreateTestText("Title", go.transform);
        desc = CreateTestText("Desc", go.transform);
        daysLeft = CreateTestText("DaysLeft", go.transform);
        badge = CreateTestGameObject("ClaimableBadge", go.transform);
        badge.AddComponent<RectTransform>();

        EventBannerCardWidget widget = go.AddComponent<EventBannerCardWidget>();
        SetPrivateField(widget, "bannerImage", bg);
        SetPrivateField(widget, "titleText", title);
        SetPrivateField(widget, "descriptionText", desc);
        SetPrivateField(widget, "daysLeftText", daysLeft);
        SetPrivateField(widget, "claimableBadge", badge);
        SetPrivateField(widget, "bannerButton", btn);
        return widget;
    }

    private MissionRowWidget CreateRowPrefab(string name, Transform parent, out Button claimBtn)
    {
        GameObject go = CreateTestGameObject(name, parent);
        var rect = go.AddComponent<RectTransform>();
        rect.sizeDelta = new Vector2(800f, 180f);
        go.SetActive(false);

        TMP_Text title = CreateTestText("Title", go.transform);
        TMP_Text progText = CreateTestText("ProgressText", go.transform);
        TMP_Text rewText = CreateTestText("RewardText", go.transform);
        Slider slider = CreateTestSlider("Slider", go.transform);
        claimBtn = CreateTestButton("ClaimBtn", go.transform);
        TMP_Text claimLabel = CreateTestText("ClaimLabel", claimBtn.transform);
        claimLabel.text = "CLAIM";

        GameObject tickGo = CreateTestGameObject("Tick", go.transform);
        tickGo.AddComponent<RectTransform>();
        Image tickImg = tickGo.AddComponent<Image>();

        MissionRowWidget row = go.AddComponent<MissionRowWidget>();
        SetPrivateField(row, "titleText", title);
        SetPrivateField(row, "progressSlider", slider);
        SetPrivateField(row, "progressText", progText);
        SetPrivateField(row, "rewardText", rewText);
        SetPrivateField(row, "claimButton", claimBtn);
        SetPrivateField(row, "claimButtonLabel", claimLabel);
        SetPrivateField(row, "claimedTickImage", tickImg);
        return row;
    }

    private GameObject CreateTestGameObject(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go;
    }

    private Button CreateTestButton(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name, parent);
        go.AddComponent<RectTransform>();
        Image img = go.AddComponent<Image>();
        Button btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private TMP_Text CreateTestText(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name, parent);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TextMeshProUGUI>();
    }

    private Slider CreateTestSlider(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name, parent);
        go.AddComponent<RectTransform>();
        return go.AddComponent<Slider>();
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static T GetPrivateField<T>(object target, string fieldName) where T : class
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
        return field.GetValue(target) as T;
    }
}
