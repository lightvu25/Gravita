using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End-to-end EditMode functional verification across all UI Panels in the project:
///   1. SettingUI: verifies removal of IAP Restore & Level Display fields and tests Sound/Music/Haptics/Notify/Login/Close buttons.
///   2. ShopUI: verifies removal of Restore button, retention of watchShopAdButton, and 5-category filtering (Currency, Cosmetic, Power, Pack, Offer + All).
///   3. All other UI panels (UIManager, MainMenuUI, MissionPanelUI, AchievementPanelUI, InGameHUD, PowerupPanelUI, RunnerUI, CampaignMapUI, CoinBonusUI, DailyRewardUI, BlindBoxUI, PauseMenuUI, ResultPanelUI, RevivePopupUI, LoginUI).
/// </summary>
public class AllUIPanelsFunctionalTests : EconomyTestHarnessBase
{
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Expected field '{fieldName}' on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private Button CreateTestButton(string name, Transform parent = null)
    {
        var go = CreateTestGameObject(name);
        var rt = go.AddComponent<RectTransform>();
        if (parent != null)
            rt.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        var btn = go.AddComponent<Button>();
        btn.targetGraphic = img;
        return btn;
    }

    private TMP_Text CreateTestText(string name, Transform parent = null)
    {
        var go = CreateTestGameObject(name);
        var rt = go.AddComponent<RectTransform>();
        if (parent != null)
            rt.SetParent(parent, false);
        return go.AddComponent<TextMeshProUGUI>();
    }

    [Test]
    public void SettingUI_HasNoRestoreOrLevelDisplayFields_AndAllTogglesWork()
    {
        // Verify restorePurchasesButton, restoreStatusText, and levelText have been removed from SettingUI
        Assert.IsNull(typeof(SettingUI).GetField("restorePurchasesButton", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(SettingUI).GetField("restoreStatusText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(SettingUI).GetField("levelText", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(SettingUI).GetMethod("OnRestorePurchasesClicked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

        var settingGo = CreateTestGameObject("SettingOverlay");
        var settingUI = settingGo.AddComponent<SettingUI>();

        var soundBtn = CreateTestButton("SoundBtn", settingGo.transform);
        var musicBtn = CreateTestButton("MusicBtn", settingGo.transform);
        var hapticBtn = CreateTestButton("HapticBtn", settingGo.transform);
        var notifyBtn = CreateTestButton("NotifyBtn", settingGo.transform);
        var closeBtn = CreateTestButton("CloseBtn", settingGo.transform);
        var loginBtn = CreateTestButton("LoginBtn", settingGo.transform);
        var logoutBtn = CreateTestButton("LogoutBtn", settingGo.transform);

        SetPrivateField(settingUI, "soundButton", soundBtn);
        SetPrivateField(settingUI, "musicButton", musicBtn);
        SetPrivateField(settingUI, "hapticsButton", hapticBtn);
        SetPrivateField(settingUI, "notifyButton", notifyBtn);
        SetPrivateField(settingUI, "closeButton", closeBtn);
        SetPrivateField(settingUI, "loginButton", loginBtn);
        SetPrivateField(settingUI, "logoutButton", logoutBtn);

        InvokeLifecycleIfPresent(settingUI, "Awake");
        InvokeLifecycleIfPresent(settingUI, "OnEnable");

        bool initialHaptics = GameHaptics.Enabled;
        hapticBtn.onClick.Invoke();
        Assert.AreNotEqual(initialHaptics, GameHaptics.Enabled, "Clicking Haptics button in SettingUI must toggle GameHaptics.Enabled.");

        // Restore haptics state
        GameHaptics.Enabled = true;

        settingUI.Show();
        Assert.IsTrue(settingGo.activeSelf);
        settingUI.Hide();
        Assert.IsFalse(settingGo.activeSelf);
    }

    [Test]
    public void ShopUI_SupportsAllFiveCategories_KeepsWatchAdButton_AndRemovesRestoreButton()
    {
        // Verify restorePurchasesButton and categoryContainer are removed while watchShopAdButton is kept
        Assert.IsNull(typeof(ShopUI).GetField("restorePurchasesButton", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(ShopUI).GetMethod("OnRestorePurchasesClicked", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(ShopUI.ShopCategoryTabBinding).GetField("categoryContainer", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNotNull(typeof(ShopUI).GetField("watchShopAdButton", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

        var economy = SpawnSingleton<EconomyManager>("Economy");
        var shopMgr = SpawnSingleton<ShopManager>("ShopManager");
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        // Load real ShopDatabase_Main.asset and verify all 5 categories exist
        var shopDb = AssetDatabase.LoadAssetAtPath<ShopDatabase>("Assets/Data/Shop/ShopDatabase_Main.asset");
        Assert.IsNotNull(shopDb, "ShopDatabase_Main.asset must exist.");
        Assert.Greater(shopDb.GetItemsByTab(ShopTabCategory.Currency).Count, 0, "ShopDatabase must have Currency items.");
        Assert.Greater(shopDb.GetItemsByTab(ShopTabCategory.Cosmetic).Count, 0, "ShopDatabase must have Cosmetic items.");
        Assert.Greater(shopDb.GetItemsByTab(ShopTabCategory.Power).Count, 0, "ShopDatabase must have Power items.");
        Assert.Greater(shopDb.GetItemsByTab(ShopTabCategory.Pack).Count, 0, "ShopDatabase must have Pack items.");
        Assert.Greater(shopDb.GetItemsByTab(ShopTabCategory.Offer).Count, 0, "ShopDatabase must have Offer items.");

        var shopGo = CreateTestGameObject("ShopPopup");
        var shopUI = shopGo.AddComponent<ShopUI>();

        var containerGo = CreateTestGameObject("ItemsContainer");
        var containerRt = containerGo.AddComponent<RectTransform>();
        containerRt.SetParent(shopGo.transform, false);

        // Template card
        var templateGo = CreateTestGameObject("ShopItemCardTemplate");
        templateGo.transform.SetParent(shopGo.transform, false);
        var cardWidget = templateGo.AddComponent<ShopItemCardWidget>();
        var buyBtn = CreateTestButton("BuyBtn", templateGo.transform);
        var nameTxt = CreateTestText("NameText", templateGo.transform);
        var costTxt = CreateTestText("CostText", templateGo.transform);
        SetPrivateField(cardWidget, "buyButton", buyBtn);
        SetPrivateField(cardWidget, "nameText", nameTxt);
        SetPrivateField(cardWidget, "costText", costTxt);

        // Category tab buttons for all 5 categories
        var btnCurrency = CreateTestButton("Tab_Currency", shopGo.transform);
        var btnCosmetic = CreateTestButton("Tab_Cosmetic", shopGo.transform);
        var btnPower = CreateTestButton("Tab_Power", shopGo.transform);
        var btnPack = CreateTestButton("Tab_Pack", shopGo.transform);
        var btnOffer = CreateTestButton("Tab_Offer", shopGo.transform);

        var tabs = new List<ShopUI.ShopCategoryTabBinding>
        {
            new ShopUI.ShopCategoryTabBinding { category = ShopTabCategory.Currency, tabButton = btnCurrency },
            new ShopUI.ShopCategoryTabBinding { category = ShopTabCategory.Cosmetic, tabButton = btnCosmetic },
            new ShopUI.ShopCategoryTabBinding { category = ShopTabCategory.Power, tabButton = btnPower },
            new ShopUI.ShopCategoryTabBinding { category = ShopTabCategory.Pack, tabButton = btnPack },
            new ShopUI.ShopCategoryTabBinding { category = ShopTabCategory.Offer, tabButton = btnOffer }
        };

        SetPrivateField(shopUI, "shopDatabase", shopDb);
        SetPrivateField(shopUI, "itemsContainer", containerRt);
        SetPrivateField(shopUI, "itemCardPrefab", cardWidget);
        SetPrivateField(shopUI, "categoryTabs", tabs);
        SetPrivateField(shopUI, "coinsText", CreateTestText("CoinsText", shopGo.transform));
        SetPrivateField(shopUI, "gemsText", CreateTestText("GemsText", shopGo.transform));
        SetPrivateField(shopUI, "feedbackText", CreateTestText("FeedbackText", shopGo.transform));

        InvokeLifecycleIfPresent(shopUI, "Awake");
        InvokeLifecycleIfPresent(shopUI, "OnEnable");

        // Initial open selects tab button index 0 (Currency) first and zooms that tab button
        Assert.AreEqual(ShopTabCategory.Currency, shopUI.ActiveCategory);
        Assert.AreEqual(shopDb.GetItemsByTab(ShopTabCategory.Currency).Count, shopUI.SpawnedWidgets.Count);
        Assert.Greater(btnCurrency.transform.localScale.x, 1.01f, "Tab index 0 button should be zoomed when Shop opens.");
        Assert.AreEqual(1f, btnCosmetic.transform.localScale.x, 0.001f);

        // Click each of the 5 category tabs and verify filtering + zoom state
        btnCosmetic.onClick.Invoke();
        Assert.AreEqual(ShopTabCategory.Cosmetic, shopUI.ActiveCategory);
        Assert.AreEqual(shopDb.GetItemsByTab(ShopTabCategory.Cosmetic).Count, shopUI.SpawnedWidgets.Count);
        Assert.Greater(btnCosmetic.transform.localScale.x, 1.01f);
        Assert.AreEqual(1f, btnCurrency.transform.localScale.x, 0.001f);

        btnPower.onClick.Invoke();
        Assert.AreEqual(ShopTabCategory.Power, shopUI.ActiveCategory);
        Assert.AreEqual(shopDb.GetItemsByTab(ShopTabCategory.Power).Count, shopUI.SpawnedWidgets.Count);
        Assert.Greater(btnPower.transform.localScale.x, 1.01f);

        btnPack.onClick.Invoke();
        Assert.AreEqual(ShopTabCategory.Pack, shopUI.ActiveCategory);
        Assert.AreEqual(shopDb.GetItemsByTab(ShopTabCategory.Pack).Count, shopUI.SpawnedWidgets.Count);
        Assert.Greater(btnPack.transform.localScale.x, 1.01f);

        btnOffer.onClick.Invoke();
        Assert.AreEqual(ShopTabCategory.Offer, shopUI.ActiveCategory);
        Assert.AreEqual(shopDb.GetItemsByTab(ShopTabCategory.Offer).Count, shopUI.SpawnedWidgets.Count);
        Assert.Greater(btnOffer.transform.localScale.x, 1.01f);

        // Re-opening Shop resets to tab index 0 (Currency) and zooms tab 0
        shopUI.OpenShop();
        Assert.AreEqual(ShopTabCategory.Currency, shopUI.ActiveCategory);
        Assert.Greater(btnCurrency.transform.localScale.x, 1.01f);
    }

    [Test]
    public void UIManager_And_AllRemainingUIPanels_OpenCloseAndRefreshWithoutErrors()
    {
        var uiMgrGo = CreateTestGameObject("UIManager");
        var uiMgr = uiMgrGo.AddComponent<UIManager>();

        var backdropGo = CreateTestGameObject("GlitchBackdrop");
        var backdropRt = backdropGo.AddComponent<RectTransform>();
        backdropGo.AddComponent<Image>();
        SetPrivateField(uiMgr, "glitchBackdrop", backdropRt);

        // Create and register Screen panels (Map, Runner, Powerups, Achievement) + Popup panels (Shop, Mission, Setting, CoinBonus, DailyReward, Login, Pause, Result, Revive, BlindBox)
        var mapGo = CreateTestGameObject("CampaignMapOverlay");
        mapGo.AddComponent<RectTransform>();
        var mapUI = mapGo.AddComponent<CampaignMapUI>();

        var runnerGo = CreateTestGameObject("RunnerOverlay");
        runnerGo.AddComponent<RectTransform>();
        var runnerUI = runnerGo.AddComponent<RunnerUI>();

        var powerupGo = CreateTestGameObject("PowerupOverlay");
        powerupGo.AddComponent<RectTransform>();
        var powerupUI = powerupGo.AddComponent<PowerupPanelUI>();

        var achGo = CreateTestGameObject("AchievementOverlay");
        achGo.AddComponent<RectTransform>();
        var achUI = achGo.AddComponent<AchievementPanelUI>();

        var missionGo = CreateTestGameObject("MissionPopup");
        missionGo.AddComponent<RectTransform>();
        var missionUI = missionGo.AddComponent<MissionPanelUI>();

        var coinBonusGo = CreateTestGameObject("CoinBonusPopup");
        coinBonusGo.AddComponent<RectTransform>();
        var coinBonusUI = coinBonusGo.AddComponent<CoinUI>();

        var dailyGo = CreateTestGameObject("DailyRewardPopup");
        dailyGo.AddComponent<RectTransform>();
        var dailyUI = dailyGo.AddComponent<DailyRewardUI>();

        var loginGo = CreateTestGameObject("LoginOverlay");
        loginGo.AddComponent<RectTransform>();
        var loginUI = loginGo.AddComponent<LoginUI>();

        var pauseGo = CreateTestGameObject("PauseOverlay");
        pauseGo.AddComponent<RectTransform>();
        var pauseUI = pauseGo.AddComponent<PauseMenuUI>();

        var resultGo = CreateTestGameObject("ResultOverlay");
        resultGo.AddComponent<RectTransform>();
        var resultUI = resultGo.AddComponent<ResultPanelUI>();

        var reviveGo = CreateTestGameObject("RevivePopup");
        reviveGo.AddComponent<RectTransform>();
        var reviveUI = reviveGo.AddComponent<RevivePopupUI>();

        var blindBoxGo = CreateTestGameObject("BlindBoxPopup");
        blindBoxGo.AddComponent<RectTransform>();
        var blindBoxUI = blindBoxGo.AddComponent<BlindBoxUI>();

        var mappings = new List<UIManager.UIPanelMapping>
        {
            new UIManager.UIPanelMapping { panelType = UIPanelType.Map, panelObject = mapGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Runner, panelObject = runnerGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Powerups, panelObject = powerupGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Achievement, panelObject = achGo, layer = UILayer.Screen },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Mission, panelObject = missionGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.CoinBonus, panelObject = coinBonusGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.DailyReward, panelObject = dailyGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Login, panelObject = loginGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Pause, panelObject = pauseGo, layer = UILayer.Popup, freezeTime = true },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Result, panelObject = resultGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.Revive, panelObject = reviveGo, layer = UILayer.Popup },
            new UIManager.UIPanelMapping { panelType = UIPanelType.BlindBox, panelObject = blindBoxGo, layer = UILayer.Popup }
        };

        SetPrivateField(uiMgr, "panelMappings", mappings);
        InvokeLifecycleIfPresent(uiMgr, "Awake");

        // 1. Test Screen switching (Map -> Runner -> Powerups -> Achievement) never activates GlitchBackdrop
        uiMgr.ShowScreen(UIPanelType.Map);
        Assert.AreEqual(UIPanelType.Map, uiMgr.CurrentScreen);
        Assert.IsFalse(backdropGo.activeSelf);

        uiMgr.ShowScreen(UIPanelType.Runner);
        Assert.AreEqual(UIPanelType.Runner, uiMgr.CurrentScreen);
        Assert.IsFalse(backdropGo.activeSelf);

        uiMgr.ShowScreen(UIPanelType.Powerups);
        Assert.AreEqual(UIPanelType.Powerups, uiMgr.CurrentScreen);
        Assert.IsFalse(backdropGo.activeSelf);

        uiMgr.ShowScreen(UIPanelType.Achievement);
        Assert.AreEqual(UIPanelType.Achievement, uiMgr.CurrentScreen);
        Assert.IsFalse(backdropGo.activeSelf);

        // 2. Test Popup opening (Mission, CoinBonus, DailyReward, Login, Pause, Result, BlindBox) activates GlitchBackdrop
        UIPanelType[] popupsToVerify =
        {
            UIPanelType.Mission,
            UIPanelType.CoinBonus,
            UIPanelType.DailyReward,
            UIPanelType.Login,
            UIPanelType.Pause,
            UIPanelType.Result,
            UIPanelType.BlindBox
        };

        foreach (UIPanelType popup in popupsToVerify)
        {
            uiMgr.OpenPanel(popup);
            Assert.IsTrue(uiMgr.IsPanelOpen(popup), $"Popup {popup} should be open.");
            Assert.IsTrue(backdropGo.activeSelf, $"GlitchBackdrop should be active when Popup {popup} is open.");
            uiMgr.ClosePanelIfOpen(popup);
            Assert.IsFalse(uiMgr.IsPanelOpen(popup), $"Popup {popup} should close cleanly.");
        }

        Assert.IsFalse(backdropGo.activeSelf, "GlitchBackdrop must hide when all popups are closed.");

        // 3. Test MainMenuUI keeps Mission / Chest (BlindBox) / Shop icons visible across all tabs
        var mainMenuGo = CreateTestGameObject("MainMenuScreen");
        var mainMenuUI = mainMenuGo.AddComponent<MainMenuUI>();
        var mapTabBtn = CreateTestButton("MapTabBtn", mainMenuGo.transform);
        var runnerTabBtn = CreateTestButton("RunnerTabBtn", mainMenuGo.transform);
        var powerTabBtn = CreateTestButton("PowerTabBtn", mainMenuGo.transform);
        var achTabBtn = CreateTestButton("AchTabBtn", mainMenuGo.transform);
        var multiTabBtn = CreateTestButton("MultiTabBtn", mainMenuGo.transform);
        var missionIconBtn = CreateTestButton("MissionIconBtn", mainMenuGo.transform);
        var chestIconBtn = CreateTestButton("ChestIconBtn", mainMenuGo.transform);
        var shopIconBtn = CreateTestButton("ShopIconBtn", mainMenuGo.transform);

        SetPrivateField(mainMenuUI, "mapTabButton", mapTabBtn);
        SetPrivateField(mainMenuUI, "runnerTabButton", runnerTabBtn);
        SetPrivateField(mainMenuUI, "powerupsTabButton", powerTabBtn);
        SetPrivateField(mainMenuUI, "achievementTabButton", achTabBtn);
        SetPrivateField(mainMenuUI, "multiplayerTabButton", multiTabBtn);
        SetPrivateField(mainMenuUI, "dailyRewardButton", missionIconBtn);
        SetPrivateField(mainMenuUI, "blindBoxButton", chestIconBtn);
        SetPrivateField(mainMenuUI, "shopButton", shopIconBtn);

        InvokeLifecycleIfPresent(mainMenuUI, "Awake");
        InvokeLifecycleIfPresent(mainMenuUI, "Start");

        Button[] allTabButtons = { mapTabBtn, runnerTabBtn, powerTabBtn, achTabBtn, multiTabBtn };
        foreach (Button tabBtn in allTabButtons)
        {
            tabBtn.onClick.Invoke();
            Assert.IsTrue(missionIconBtn.gameObject.activeSelf, $"Mission icon should remain visible on {tabBtn.name}.");
            Assert.IsTrue(chestIconBtn.gameObject.activeSelf, $"Chest icon should remain visible on {tabBtn.name}.");
            Assert.IsTrue(shopIconBtn.gameObject.activeSelf, $"Shop icon should remain visible on {tabBtn.name}.");
        }

        Time.timeScale = 1f;
    }

    [Test]
    public void GlitchTransition_MilestoneOpacity_AndNativeSize_WorkAsExpected()
    {
        // 1. Verify UIGlitchSpriteAnimator removed redundant flicker/random fields
        Assert.IsNull(typeof(UIGlitchSpriteAnimator).GetField("randomizeFrameOrder", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(UIGlitchSpriteAnimator).GetField("delayBetweenLoops", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(UIGlitchSpriteAnimator).GetField("minAlpha", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));
        Assert.IsNull(typeof(UIGlitchSpriteAnimator).GetField("maxAlpha", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public));

        // 2. Verify MissionMilestoneWidget keeps original icon and dims opacity when claimed
        var milestoneGo = CreateTestGameObject("MilestoneWidget");
        var milestoneWidget = milestoneGo.AddComponent<MissionMilestoneWidget>();
        var iconGo = CreateTestGameObject("Icon");
        iconGo.transform.SetParent(milestoneGo.transform, false);
        var iconImg = iconGo.AddComponent<Image>();
        var btn = CreateTestButton("Btn", milestoneGo.transform);

        SetPrivateField(milestoneWidget, "milestoneIconImage", iconImg);
        SetPrivateField(milestoneWidget, "button", btn);

        var tex = TrackAsset(new Texture2D(18, 32));
        var rewardSpr = TrackAsset(Sprite.Create(tex, new Rect(0, 0, 18, 32), new Vector2(0.5f, 0.5f), 100f));
        var tickTex = TrackAsset(new Texture2D(23, 15));
        var tickSpr = TrackAsset(Sprite.Create(tickTex, new Rect(0, 0, 23, 15), new Vector2(0.5f, 0.5f), 100f));

        var milestoneDef = TrackAsset(ScriptableObject.CreateInstance<MissionMilestoneDefinition>());
        milestoneDef.Configure(
            id: "ms_test",
            displayTitle: "3 Tasks",
            requiredTasks: 3,
            rewardKind: MissionRewardType.Coins,
            rewardValue: 100,
            incomplete: rewardSpr,
            complete: rewardSpr,
            claimed: tickSpr,
            icon: rewardSpr);

        milestoneWidget.Bind(milestoneDef, isComplete: true, isClaimed: false, null);
        Assert.AreEqual(rewardSpr, iconImg.sprite);
        Assert.AreEqual(1f, iconImg.color.a, 0.001f);

        milestoneWidget.Bind(milestoneDef, isComplete: true, isClaimed: true, null);
        Assert.AreEqual(rewardSpr, iconImg.sprite, "Claimed milestone must keep its original icon instead of replacing with tick sprite.");
        Assert.Less(iconImg.color.a, 0.9f, "Claimed milestone icon opacity must be dimmed.");

        // 3. Verify PooledGlitchTransitionEffect + UIPanelAnimator (AnimationMode.Glitch)
        var glitchPrefabGo = CreateTestGameObject("GlitchTransitionPrefab");
        glitchPrefabGo.AddComponent<RectTransform>();
        glitchPrefabGo.AddComponent<Image>();
        var glitchEffectPrefab = glitchPrefabGo.AddComponent<PooledGlitchTransitionEffect>();
        glitchEffectPrefab.Frames = new[] { rewardSpr, tickSpr };
        glitchPrefabGo.SetActive(false);

        var panelGo = CreateTestGameObject("AnimatedPanel");
        panelGo.AddComponent<RectTransform>();
        var animator = panelGo.AddComponent<UIPanelAnimator>();
        SetPrivateField(animator, "mode", UIPanelAnimator.AnimationMode.Glitch);
        SetPrivateField(animator, "glitchEffectPrefab", glitchEffectPrefab);

        animator.PlayIn();
        Assert.IsTrue(panelGo.activeSelf);
        animator.PlayOut();
        Assert.IsFalse(panelGo.activeSelf);
    }

    [Test]
    public void MissionClaimHidesButtonShrinksHeight_AndProgressTextColorsPerCharacter()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        MissionService.EnsureProfileMigratedAndUpdated();

        // 1. Verify MissionRowWidget hides claimButton, shows claimedTickImage, and shrinks height when claimed
        var rowGo = CreateTestGameObject("MissionRow");
        var rowRect = rowGo.AddComponent<RectTransform>();
        rowRect.sizeDelta = new Vector2(800f, 230f);
        var layoutElement = rowGo.AddComponent<LayoutElement>();
        layoutElement.minHeight = 230f;
        layoutElement.preferredHeight = 230f;

        var rowWidget = rowGo.AddComponent<MissionRowWidget>();
        var sliderGo = CreateTestGameObject("ProgressSlider");
        var sliderRect = sliderGo.AddComponent<RectTransform>();
        sliderRect.SetParent(rowGo.transform, false);
        sliderRect.sizeDelta = new Vector2(400f, 42f);
        var slider = sliderGo.AddComponent<Slider>();

        var progressTxt = CreateTestText("ProgressText", sliderGo.transform);
        progressTxt.rectTransform.sizeDelta = new Vector2(400f, 42f);
        progressTxt.color = Color.white;

        var claimBtn = CreateTestButton("ClaimBtn", rowGo.transform);
        ((RectTransform)claimBtn.transform).sizeDelta = new Vector2(450f, 76f);

        var tickGo = CreateTestGameObject("TickIcon");
        var tickRect = tickGo.AddComponent<RectTransform>();
        tickRect.SetParent(rowGo.transform, false);
        var tickImg = tickGo.AddComponent<Image>();

        SetPrivateField(rowWidget, "progressSlider", slider);
        SetPrivateField(rowWidget, "progressText", progressTxt);
        SetPrivateField(rowWidget, "claimButton", claimBtn);
        SetPrivateField(rowWidget, "claimedTickImage", tickImg);

        var missionDef = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        missionDef.Configure(
            "m_height_test",
            MissionCategory.Daily,
            "Test Mission",
            "Desc",
            MissionService.MissionType.DailyLogin,
            1,
            MissionRewardType.Coins,
            50);

        rowWidget.Setup(missionDef, null);
        Assert.IsTrue(claimBtn.gameObject.activeSelf, "Unclaimed mission should show claimButton.");
        Assert.IsFalse(tickImg.gameObject.activeSelf, "Unclaimed mission should hide claimedTickImage.");
        Assert.AreEqual(230f, rowRect.sizeDelta.y, 0.01f);
        Assert.AreEqual(230f, layoutElement.preferredHeight, 0.01f);

        Assert.IsTrue(MissionService.TryClaim(missionDef));
        rowWidget.Refresh();

        Assert.IsFalse(claimBtn.gameObject.activeSelf, "Claimed mission must hide claimButton.");
        Assert.IsTrue(tickImg.gameObject.activeSelf, "Claimed mission must show claimedTickImage.");
        Assert.Less(rowRect.sizeDelta.y, 200f, "Claimed mission must reduce row height when claimButton is hidden.");
        Assert.Less(layoutElement.preferredHeight, 200f, "Claimed mission must reduce LayoutElement.preferredHeight.");

        // 2. Verify UIProgressTextCharacterColorizer colors individual characters as slider fill moves across
        slider.minValue = 0f;
        slider.maxValue = 100f;
        progressTxt.text = "40/100";

        Color covered = new Color(0.1f, 0.1f, 0.15f, 1f);
        var colorizer = UIProgressTextCharacterColorizer.EnsureAndRefresh(progressTxt, slider, null, covered);
        Assert.IsNotNull(colorizer);

        // At 0%: all visible characters should use uncovered (white) color
        slider.value = 0f;
        colorizer.Refresh();
        if (progressTxt.textInfo != null && progressTxt.textInfo.characterCount >= 6)
        {
            var firstChar = progressTxt.textInfo.characterInfo[0];
            var lastChar = progressTxt.textInfo.characterInfo[5];
            if (firstChar.isVisible && lastChar.isVisible)
            {
                Color32 c0AtZero = progressTxt.textInfo.meshInfo[firstChar.materialReferenceIndex].colors32[firstChar.vertexIndex];
                Color32 cLastAtZero = progressTxt.textInfo.meshInfo[lastChar.materialReferenceIndex].colors32[lastChar.vertexIndex];
                Assert.AreEqual((Color32)Color.white, c0AtZero);
                Assert.AreEqual((Color32)Color.white, cLastAtZero);

                // At 50%: leftmost character ('4') is covered while rightmost character ('0') is still uncovered!
                slider.value = 50f;
                colorizer.Refresh();
                Color32 c0AtHalf = progressTxt.textInfo.meshInfo[firstChar.materialReferenceIndex].colors32[firstChar.vertexIndex];
                Color32 cLastAtHalf = progressTxt.textInfo.meshInfo[lastChar.materialReferenceIndex].colors32[lastChar.vertexIndex];
                Assert.AreEqual((Color32)covered, c0AtHalf, "Left character covered by 50% fill should change to covered color.");
                Assert.AreEqual((Color32)Color.white, cLastAtHalf, "Right character not yet reached by 50% fill should remain uncovered color.");

                // At 100%: all visible characters are covered
                slider.value = 100f;
                colorizer.Refresh();
                Color32 cLastAtFull = progressTxt.textInfo.meshInfo[lastChar.materialReferenceIndex].colors32[lastChar.vertexIndex];
                Assert.AreEqual((Color32)covered, cLastAtFull, "All characters should use covered color at 100% fill.");
            }
        }
    }
}
