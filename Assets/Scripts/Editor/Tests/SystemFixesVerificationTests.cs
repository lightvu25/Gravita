using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Automated EditMode verification suite covering the system fixes and 20-level campaign overhaul:
/// daily Mystery Box cadence, audio sliders + mute/unmute restore, power-up UI/data fixes,
/// mission claim label overlap fix, RuntimeTilemapMerger corridor-safe auto-fill, Star pickup filtering,
/// and all 20 Campaign level assets + Star.prefab integrity.
/// </summary>
public class SystemFixesVerificationTests : EconomyTestHarnessBase
{
    private const string StarPrefabPath = "Assets/Prefabs/Gameplay/Star.prefab";
    private const string CampaignDbPath = "Assets/Data/Campaign/CampaignDatabase.asset";

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Expected field '{fieldName}' on {target.GetType().Name}");
        field.SetValue(target, value);
    }

    private Sprite CreateTestSprite(string name = "TestSprite")
    {
        var tex = TrackAsset(new Texture2D(4, 4, TextureFormat.RGBA32, false));
        tex.name = name + "_Tex";
        tex.Apply();
        var sprite = TrackAsset(Sprite.Create(tex, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f)));
        sprite.name = name;
        return sprite;
    }

    private (Button button, Image icon, Slider slider) CreateAudioRow(string prefix, Transform parent)
    {
        GameObject btnGo = CreateTestGameObject(prefix + "Btn");
        btnGo.AddComponent<RectTransform>().SetParent(parent, false);
        Image iconImg = btnGo.AddComponent<Image>();
        Button btn = btnGo.AddComponent<Button>();
        btn.targetGraphic = iconImg;

        GameObject sliderGo = CreateTestGameObject(prefix + "Slider");
        sliderGo.AddComponent<RectTransform>().SetParent(parent, false);
        Slider slider = sliderGo.AddComponent<Slider>();

        return (btn, iconImg, slider);
    }

    private ScrollRect CreateScrollHierarchy(string prefix, Transform parent, out Transform content)
    {
        GameObject scrollGo = CreateTestGameObject(prefix + "Scroll");
        RectTransform scrollRt = scrollGo.AddComponent<RectTransform>();
        scrollRt.SetParent(parent, false);
        ScrollRect sr = scrollGo.AddComponent<ScrollRect>();
        sr.horizontal = false;
        sr.vertical = true;

        GameObject contentGo = CreateTestGameObject(prefix + "Container");
        RectTransform contentRt = contentGo.AddComponent<RectTransform>();
        contentRt.SetParent(scrollRt, false);
        sr.content = contentRt;
        content = contentRt;
        return sr;
    }

    [Test]
    public void DailyMysteryBox_AllowedOncePerLocalDay_FiresEventAndResetsAtMidnight()
    {
        var clock = new FixedLocalClockProvider(new DateTime(2026, 11, 14, 10, 0, 0, DateTimeKind.Local));
        LocalClock.Provider = clock;

        BlindBoxService service = SpawnSingleton<BlindBoxService>("BlindBoxService");
        int eventCount = 0;
        Action onStateChanged = () => eventCount++;
        BlindBoxService.OnDailyBoxStateChanged += onStateChanged;

        try
        {
            Assert.IsTrue(BlindBoxService.CanClaimDailyBox(), "Fresh profile should allow daily Mystery Box claim.");

            RewardBundle first = service.Open(allowDirectMenuOpen: true);
            Assert.IsNotNull(first, "First direct menu Mystery Box open of the day must grant a reward bundle.");
            Assert.AreEqual(1, eventCount, "Opening daily Mystery Box from menu must fire OnDailyBoxStateChanged.");
            Assert.IsFalse(BlindBoxService.CanClaimDailyBox(), "Same local calendar day must block a second daily claim.");

            service.ResetBox();
            RewardBundle sameDayRetry = service.Open(allowDirectMenuOpen: true);
            Assert.IsNull(sameDayRetry, "Even after ResetBox(), same-day direct menu open must remain blocked.");

            clock.Current = clock.Current.AddDays(1);
            Assert.IsTrue(BlindBoxService.CanClaimDailyBox(), "Advancing to next local calendar day must re-enable daily Mystery Box.");

            RewardBundle nextDay = service.Open(allowDirectMenuOpen: true);
            Assert.IsNotNull(nextDay, "Next-day Mystery Box open must succeed.");
            Assert.AreEqual(2, eventCount, "Next-day claim must fire OnDailyBoxStateChanged again.");
        }
        finally
        {
            BlindBoxService.OnDailyBoxStateChanged -= onStateChanged;
        }
    }

    [Test]
    public void AudioSlidersAndButtons_SyncOnOffSpritesAndRestorePreMuteVolume()
    {
        string[] prefKeys = { "SoundVolume", "PreviousSoundVolume", "MusicVolume", "PreviousMusicVolume" };
        var savedPrefs = new Dictionary<string, int?>();
        for (int i = 0; i < prefKeys.Length; i++)
        {
            string k = prefKeys[i];
            savedPrefs[k] = PlayerPrefs.HasKey(k) ? PlayerPrefs.GetInt(k) : (int?)null;
            PlayerPrefs.DeleteKey(k);
        }

        try
        {
            SoundManager soundMgr = SpawnSingleton<SoundManager>("SoundManager");
            MusicManager musicMgr = SpawnSingleton<MusicManager>("MusicManager");

            Sprite soundOn = CreateTestSprite("SoundOn");
            Sprite soundOff = CreateTestSprite("SoundOff");
            Sprite musicOn = CreateTestSprite("MusicOn");
            Sprite musicOff = CreateTestSprite("MusicOff");

            GameObject settingGo = CreateTestGameObject("SettingOverlay");
            settingGo.AddComponent<RectTransform>();
            SettingUI settingUI = settingGo.AddComponent<SettingUI>();

            var soundWidgets = CreateAudioRow("Sound", settingGo.transform);
            var musicWidgets = CreateAudioRow("Music", settingGo.transform);

            SetPrivateField(settingUI, "soundButton", soundWidgets.button);
            SetPrivateField(settingUI, "soundIcon", soundWidgets.icon);
            SetPrivateField(settingUI, "soundSlider", soundWidgets.slider);
            SetPrivateField(settingUI, "soundOnSprite", soundOn);
            SetPrivateField(settingUI, "soundOffSprite", soundOff);

            SetPrivateField(settingUI, "musicButton", musicWidgets.button);
            SetPrivateField(settingUI, "musicIcon", musicWidgets.icon);
            SetPrivateField(settingUI, "musicSlider", musicWidgets.slider);
            SetPrivateField(settingUI, "musicOnSprite", musicOn);
            SetPrivateField(settingUI, "musicOffSprite", musicOff);

            InvokeLifecycleIfPresent(settingUI, "Awake");
            InvokeLifecycleIfPresent(settingUI, "OnEnable");

            Assert.AreEqual(0f, soundWidgets.slider.minValue);
            Assert.AreEqual(10f, soundWidgets.slider.maxValue);
            Assert.IsTrue(soundWidgets.slider.wholeNumbers);
            Assert.AreEqual(0f, musicWidgets.slider.minValue);
            Assert.AreEqual(10f, musicWidgets.slider.maxValue);
            Assert.IsTrue(musicWidgets.slider.wholeNumbers);

            // Sound slider > 0 updates manager & shows ON sprite
            soundWidgets.slider.value = 7f;
            Assert.AreEqual(7, soundMgr.GetSoundVolume());
            Assert.AreSame(soundOn, soundWidgets.icon.sprite);

            // Sound slider -> 0 mutes manager & shows OFF sprite while saving pre-zero volume
            soundWidgets.slider.value = 0f;
            Assert.AreEqual(0, soundMgr.GetSoundVolume());
            Assert.AreSame(soundOff, soundWidgets.icon.sprite);
            Assert.AreEqual(7, soundMgr.GetPreviousSoundVolume());

            // Clicking Sound button while muted restores pre-mute volume (7) on slider + ON sprite
            soundWidgets.button.onClick.Invoke();
            Assert.AreEqual(7, soundMgr.GetSoundVolume());
            Assert.AreEqual(7, Mathf.RoundToInt(soundWidgets.slider.value));
            Assert.AreSame(soundOn, soundWidgets.icon.sprite);

            // Clicking Sound button while ON mutes to 0 & preserves previous volume (7)
            soundWidgets.button.onClick.Invoke();
            Assert.AreEqual(0, soundMgr.GetSoundVolume());
            Assert.AreEqual(0, Mathf.RoundToInt(soundWidgets.slider.value));
            Assert.AreSame(soundOff, soundWidgets.icon.sprite);
            Assert.AreEqual(7, soundMgr.GetPreviousSoundVolume());

            // Verify Music channel symmetry
            musicWidgets.slider.value = 8f;
            Assert.AreEqual(8, musicMgr.GetMusicVolume());
            Assert.AreSame(musicOn, musicWidgets.icon.sprite);

            musicWidgets.slider.value = 0f;
            Assert.AreEqual(0, musicMgr.GetMusicVolume());
            Assert.AreSame(musicOff, musicWidgets.icon.sprite);

            musicWidgets.button.onClick.Invoke();
            Assert.AreEqual(8, musicMgr.GetMusicVolume());
            Assert.AreEqual(8, Mathf.RoundToInt(musicWidgets.slider.value));
            Assert.AreSame(musicOn, musicWidgets.icon.sprite);
        }
        finally
        {
            foreach (var kv in savedPrefs)
            {
                if (kv.Value.HasValue)
                    PlayerPrefs.SetInt(kv.Key, kv.Value.Value);
                else
                    PlayerPrefs.DeleteKey(kv.Key);
            }
            PlayerPrefs.Save();
        }
    }

    [Test]
    public void PowerupUI_EmptySlotKeepsGameObjectActiveAndShowsPlus_NestedScrollDisabled_HorizontalScrollEnabled()
    {
        // 1. PowerupLoadoutSlotWidget: miswired emptyRoot == gameObject must NOT hide slot & must show '+' sprite
        Sprite plusSprite = CreateTestSprite("PlusIcon");
        Sprite equipSprite = CreateTestSprite("EquipIcon");

        GameObject slotGo = CreateTestGameObject("LoadoutSlot");
        slotGo.AddComponent<RectTransform>();
        slotGo.AddComponent<Image>();
        slotGo.AddComponent<Button>();
        PowerupLoadoutSlotWidget slotWidget = slotGo.AddComponent<PowerupLoadoutSlotWidget>();

        GameObject slotIconGo = CreateTestGameObject("SlotIcon");
        slotIconGo.AddComponent<RectTransform>().SetParent(slotGo.transform, false);
        Image slotIconImg = slotIconGo.AddComponent<Image>();

        SetPrivateField(slotWidget, "iconImage", slotIconImg);
        SetPrivateField(slotWidget, "emptyRoot", slotGo);
        SetPrivateField(slotWidget, "emptyPlusSprite", plusSprite);

        slotWidget.Bind(0, () => false, null, null, null, null, null);
        slotWidget.Refresh(string.Empty, null);

        Assert.IsTrue(slotGo.activeSelf, "Empty loadout slot GameObject must remain active even if emptyRoot points to self.");
        Assert.IsTrue(slotIconImg.enabled, "Empty loadout slot iconImage must remain enabled for '+' sprite.");
        Assert.AreSame(plusSprite, slotIconImg.sprite, "Empty loadout slot must show '+' sprite.");

        slotWidget.Refresh(PowerUpIds.Breaker, equipSprite);
        Assert.IsTrue(slotIconImg.enabled);
        Assert.AreSame(equipSprite, slotIconImg.sprite);

        slotWidget.Refresh(null, null);
        Assert.IsTrue(slotGo.activeSelf);
        Assert.IsTrue(slotIconImg.enabled);
        Assert.AreSame(plusSprite, slotIconImg.sprite);

        // 2. PowerupUpgradeCardWidget: empty nested child ScrollRect must be disabled
        GameObject cardGo = CreateTestGameObject("UpgradeCard");
        cardGo.AddComponent<RectTransform>();
        PowerupUpgradeCardWidget card = cardGo.AddComponent<PowerupUpgradeCardWidget>();

        GameObject nestedScrollGo = CreateTestGameObject("BrokenNestedScroll");
        nestedScrollGo.AddComponent<RectTransform>().SetParent(cardGo.transform, false);
        ScrollRect nestedScroll = nestedScrollGo.AddComponent<ScrollRect>();

        card.Setup(null, null, "Test", 1, 0, null);
        Assert.IsFalse(nestedScroll.enabled, "Empty nested ScrollRect on PowerupUpgradeCardWidget must be disabled.");
        Assert.IsFalse(nestedScrollGo.activeSelf, "Empty nested ScrollRect GameObject must be deactivated.");

        // 3. PowerupPanelUI: rowContainer and upgradeContainer ScrollRects forced horizontal-only
        GameObject panelGo = CreateTestGameObject("PowerupPanel");
        panelGo.AddComponent<RectTransform>();
        PowerupPanelUI panel = panelGo.AddComponent<PowerupPanelUI>();

        ScrollRect rowScroll = CreateScrollHierarchy("Row", panelGo.transform, out Transform rowContainer);
        ScrollRect upgradeScroll = CreateScrollHierarchy("Upgrade", panelGo.transform, out Transform upgradeContainer);

        SetPrivateField(panel, "rowContainer", rowContainer);
        SetPrivateField(panel, "upgradeContainer", upgradeContainer);

        InvokeLifecycleIfPresent(panel, "Awake");
        InvokeLifecycleIfPresent(panel, "OnEnable");

        Assert.IsTrue(rowScroll.horizontal && !rowScroll.vertical, "Equip row ScrollRect must enforce horizontal scrolling only.");
        Assert.IsTrue(upgradeScroll.horizontal && !upgradeScroll.vertical, "Upgrade card ScrollRect must enforce horizontal scrolling only.");
    }

    [Test]
    public void PowerUpAssets_HaveConciseDescriptions_AndTeleportUpgradeSteps2To6()
    {
        string[] guids = AssetDatabase.FindAssets("t:PowerUpData", new[] { "Assets/Data/PowerUps" });
        Assert.AreEqual(6, guids.Length, "Expected exactly 6 PowerUpData assets under Assets/Data/PowerUps.");

        PowerUpData teleportData = null;
        for (int i = 0; i < guids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[i]);
            PowerUpData data = AssetDatabase.LoadAssetAtPath<PowerUpData>(path);
            Assert.IsNotNull(data, $"Failed to load PowerUpData at '{path}'.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(data.Description), $"PowerUp '{data.name}' must have a non-empty Description.");
            Assert.LessOrEqual(data.Description.Length, 60,
                $"PowerUp '{data.name}' description should be concise (<= 60 chars), got {data.Description.Length}: '{data.Description}'.");

            if (string.Equals(data.Id, PowerUpIds.Teleport, StringComparison.OrdinalIgnoreCase))
                teleportData = data;
        }

        Assert.IsNotNull(teleportData, "Teleport PowerUpData asset ('teleport') must exist.");
        Assert.AreEqual(5, teleportData.MaxLevel, "Teleport must have 5 upgrade steps.");
        for (int lvl = 1; lvl <= 5; lvl++)
        {
            Assert.AreEqual(lvl + 1f, teleportData.GetStatAtLevel(lvl, 0f), 0.01f,
                $"Teleport Level {lvl} stat must equal {lvl + 1}.");
        }
    }

    [Test]
    public void MissionRowWidget_ClaimableLabelShowsClaimWithoutOverlappingRewardText()
    {
        MissionDefinition mission = TrackAsset(ScriptableObject.CreateInstance<MissionDefinition>());
        mission.name = "TestClaimLabelMission";
        SetPrivateField(mission, "missionId", "test_m_claim_overlap");
        SetPrivateField(mission, "category", MissionCategory.Weekly);
        SetPrivateField(mission, "type", MissionService.MissionType.CompleteLevels);
        SetPrivateField(mission, "target", 3);
        SetPrivateField(mission, "rewardType", MissionRewardType.Coins);
        SetPrivateField(mission, "rewardAmount", 100);

        GameObject rowGo = CreateTestGameObject("MissionRow");
        rowGo.AddComponent<RectTransform>();
        MissionRowWidget row = rowGo.AddComponent<MissionRowWidget>();

        GameObject rewardGo = CreateTestGameObject("RewardText");
        rewardGo.AddComponent<RectTransform>().SetParent(rowGo.transform, false);
        TMP_Text rewardText = rewardGo.AddComponent<TextMeshProUGUI>();

        GameObject claimBtnGo = CreateTestGameObject("ClaimBtn");
        claimBtnGo.AddComponent<RectTransform>().SetParent(rowGo.transform, false);
        claimBtnGo.AddComponent<Image>();
        Button claimBtn = claimBtnGo.AddComponent<Button>();

        GameObject claimLabelGo = CreateTestGameObject("ClaimBtnLabel");
        claimLabelGo.AddComponent<RectTransform>().SetParent(claimBtnGo.transform, false);
        TMP_Text claimBtnLabel = claimLabelGo.AddComponent<TextMeshProUGUI>();
        claimBtnLabel.text = "CLAIM";

        SetPrivateField(row, "rewardText", rewardText);
        SetPrivateField(row, "claimButton", claimBtn);
        SetPrivateField(row, "claimButtonLabel", claimBtnLabel);

        try
        {
            row.Setup(mission, null);
            Assert.IsFalse(claimBtn.interactable, "Claim button should start non-interactable at 0/3.");
            Assert.AreEqual("CLAIM", claimBtnLabel.text, "Authored CLAIM label must remain preserved when mission is incomplete.");
            Assert.AreEqual("100", rewardText.text);

            for (int r = 1; r <= 3; r++)
            {
                MissionService.BeginLevelRun(900 + r);
                MissionService.CommitCurrentLevelRunCompletion();
            }

            row.Refresh();
            Assert.IsTrue(claimBtn.interactable, "Claim button must become interactable once target 3/3 is met.");
            Assert.AreEqual("CLAIM", claimBtnLabel.text, "Claimable label must display 'CLAIM' without overlapping coin reward amount.");
            Assert.AreEqual("100", rewardText.text, "Reward text must display digits only.");
        }
        finally
        {
            MissionService.AbortCurrentLevelRun();
        }
    }

    [Test]
    public void RuntimeTilemapMerger_And_CollectPickupsObjective_BehaveCorrectly()
    {
        // Part A: Corridor-safe local tilemap auto-fill leaves interior open & spawns zero extra child objects
        GameObject levelRoot = CreateTestGameObject("MergeTestLevel");
        levelRoot.AddComponent<Grid>();

        GameObject wallGo = CreateTestGameObject("WallTilemap");
        wallGo.transform.SetParent(levelRoot.transform, false);
        Tilemap wallTilemap = wallGo.AddComponent<Tilemap>();

        GameObject bgGo = CreateTestGameObject("BackgroundTilemap");
        bgGo.transform.SetParent(levelRoot.transform, false);
        Tilemap bgTilemap = bgGo.AddComponent<Tilemap>();

        GameObject spawnGo = CreateTestGameObject("PlayerSpawn");
        spawnGo.transform.SetParent(levelRoot.transform, false);
        spawnGo.transform.position = new Vector3(0.5f, 0.5f, 0f);
        spawnGo.AddComponent<PlayerSpawnPoint>();

        Tile wallTile = TrackAsset(ScriptableObject.CreateInstance<Tile>());
        Tile bgTile = TrackAsset(ScriptableObject.CreateInstance<Tile>());

        for (int x = -2; x <= 2; x++)
        {
            for (int y = -2; y <= 2; y++)
            {
                if (Math.Abs(x) == 2 || Math.Abs(y) == 2)
                    wallTilemap.SetTile(new Vector3Int(x, y, 0), wallTile);
            }
        }

        int childCountBefore = levelRoot.transform.childCount;
        RuntimeTilemapMerger.AutoFillLocalLevelTilemaps(levelRoot, wallTile, bgTile, padding: 2);

        Assert.IsFalse(wallTilemap.HasTile(Vector3Int.zero), "Interior cell must remain open without wall fill.");
        Assert.IsTrue(bgTilemap.HasTile(Vector3Int.zero), "Interior cell must receive background tile fill.");
        Assert.IsTrue(wallTilemap.HasTile(new Vector3Int(3, 0, 0)), "Exterior padded border cell must receive wall fill.");
        Assert.IsTrue(bgTilemap.HasTile(new Vector3Int(3, 0, 0)), "Exterior padded border cell must receive background fill.");
        Assert.IsTrue(wallTilemap.HasTile(new Vector3Int(-3, -3, 0)), "Exterior corner padding cell must receive wall fill.");
        Assert.AreEqual(childCountBefore, levelRoot.transform.childCount, "AutoFill must not spawn extra per-cell child GameObjects.");

        // Part B: CollectPickupsObjective Star filter ignores Coins and completes on 3 Stars
        var starObjectiveDef = new ObjectiveDefinition
        {
            objectiveType = ObjectiveType.CollectPickups,
            requiredCount = 3,
            description = "Collect 3 Stars"
        };

        GameObject coinGo = CreateTestGameObject("CoinPickup");
        coinGo.AddComponent<BoxCollider2D>();
        Collectible coin = coinGo.AddComponent<Collectible>();
        SetPrivateField(coin, "collectibleType", CollectibleType.Coin);

        GameObject starGo = CreateTestGameObject("StarPickup");
        starGo.AddComponent<BoxCollider2D>();
        Collectible star = starGo.AddComponent<Collectible>();
        SetPrivateField(star, "collectibleType", CollectibleType.Star);

        var obj = new CollectPickupsObjective();
        obj.Initialize(starObjectiveDef, null);

        obj.NotifyPickupCollected(coin);
        Assert.AreEqual(0, obj.CurrentCount, "Star objective must ignore Coin collectibles.");
        Assert.IsFalse(obj.IsComplete);

        obj.NotifyPickupCollected(star);
        obj.NotifyPickupCollected(star);
        Assert.AreEqual(2, obj.CurrentCount);
        Assert.IsFalse(obj.IsComplete);

        obj.NotifyPickupCollected(star);
        Assert.AreEqual(3, obj.CurrentCount);
        Assert.IsTrue(obj.IsComplete, "Collecting 3 Stars must complete the objective.");
    }

    [Test]
    public void CampaignLevels20_And_StarPrefab_HaveValidAssetsBackgroundsPickupsAndZeroErrors()
    {
        GameObject starPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StarPrefabPath);
        Assert.IsNotNull(starPrefab, $"Star prefab missing at '{StarPrefabPath}'.");
        Assert.AreEqual(0, GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(starPrefab),
            "Star.prefab must have 0 Missing Script components.");

        CampaignDatabase db = AssetDatabase.LoadAssetAtPath<CampaignDatabase>(CampaignDbPath);
        Assert.IsNotNull(db, $"CampaignDatabase missing at '{CampaignDbPath}'.");

        var allLevels = new List<LevelData>();
        if (db.Worlds != null)
        {
            for (int w = 0; w < db.Worlds.Count; w++)
            {
                WorldData world = db.Worlds[w];
                if (world?.levels == null) continue;
                for (int i = 0; i < world.levels.Length; i++)
                {
                    if (world.levels[i] != null)
                        allLevels.Add(world.levels[i]);
                }
            }
        }

        Assert.AreEqual(20, allLevels.Count, "CampaignDatabase must contain all 20 levels.");

        for (int i = 0; i < allLevels.Count; i++)
        {
            LevelData level = allLevels[i];
            Assert.IsNotNull(level.LayoutPrefab, $"Level '{level.LevelId}' has null LayoutPrefab.");

            Tilemap[] tilemaps = level.LayoutPrefab.GetComponentsInChildren<Tilemap>(true);
            Tilemap bgTilemap = null;
            for (int t = 0; t < tilemaps.Length; t++)
            {
                if (tilemaps[t] != null &&
                    string.Equals(tilemaps[t].gameObject.name, "BackgroundTilemap", StringComparison.OrdinalIgnoreCase))
                {
                    bgTilemap = tilemaps[t];
                    break;
                }
            }

            Assert.IsNotNull(bgTilemap, $"Level '{level.LevelId}' layout prefab is missing 'BackgroundTilemap'.");
            bgTilemap.CompressBounds();
            Assert.Greater(bgTilemap.GetUsedTilesCount(), 0,
                $"Level '{level.LevelId}' BackgroundTilemap must contain non-zero background tiles.");

            Collectible[] collectibles = level.LayoutPrefab.GetComponentsInChildren<Collectible>(true);
            int stars = 0;
            int coins = 0;
            for (int c = 0; c < collectibles.Length; c++)
            {
                if (collectibles[c] == null) continue;
                if (collectibles[c].IsStar) stars++;
                else if (collectibles[c].IsCoin) coins++;
            }

            Assert.GreaterOrEqual(stars, 3, $"Level '{level.LevelId}' must contain at least 3 Stars (found {stars}).");
            Assert.GreaterOrEqual(coins, 5, $"Level '{level.LevelId}' must contain at least 5 Coins (found {coins}).");

            List<LevelDataValidator.ValidationCheck> checks = LevelDataValidator.Validate(level);
            for (int k = 0; k < checks.Count; k++)
            {
                Assert.AreNotEqual(LevelDataValidator.CheckResult.Error, checks[k].Result,
                    $"Level '{level.LevelId}' failed LevelDataValidator [{checks[k].Label}]: {checks[k].Message}");
            }
        }
    }

    [Test]
    public void EquipPowerUI_ShowsOnlyOwnedRowsWithPlainCount_ShowsCountOnLoadout_AndShopRemovesMockPrefix()
    {
        EconomyManager economy = SpawnSingleton<EconomyManager>("EconomyManager");
        ProgressionManager progression = SpawnSingleton<ProgressionManager>("ProgressionManager");
        Assert.IsNotNull(economy);

        for (int i = 0; i < ProgressionManager.MaxLoadoutSlotCount; i++)
            progression.TryAssignLoadoutSlot(i, string.Empty);

        progression.AddPowerUp(PowerUpIds.Breaker, 3, "UnitTest");
        progression.AddPowerUp(PowerUpIds.Freeze, 1, "UnitTest");
        Assert.AreEqual(3, progression.GetPowerUpCount(PowerUpIds.Breaker));
        Assert.AreEqual(1, progression.GetPowerUpCount(PowerUpIds.Freeze));
        Assert.AreEqual(0, progression.GetPowerUpCount(PowerUpIds.Teleport));

        GameObject panelGo = CreateTestGameObject("PowerupPanel");
        panelGo.AddComponent<RectTransform>();
        PowerupPanelUI panel = panelGo.AddComponent<PowerupPanelUI>();

        CreateScrollHierarchy("Row", panelGo.transform, out Transform rowContainer);

        GameObject rowPrefabGo = CreateTestGameObject("RowPrefab");
        rowPrefabGo.AddComponent<RectTransform>();
        rowPrefabGo.SetActive(false);
        Button rowBtn = rowPrefabGo.AddComponent<Button>();
        PowerupRowWidget rowPrefab = rowPrefabGo.AddComponent<PowerupRowWidget>();

        GameObject rowIconGo = CreateTestGameObject("Icon");
        rowIconGo.AddComponent<RectTransform>().SetParent(rowPrefabGo.transform, false);
        Image rowIcon = rowIconGo.AddComponent<Image>();

        GameObject rowCountGo = CreateTestGameObject("CountLabel");
        rowCountGo.AddComponent<RectTransform>().SetParent(rowPrefabGo.transform, false);
        TextMeshProUGUI rowCountTmp = rowCountGo.AddComponent<TextMeshProUGUI>();

        SetPrivateField(rowPrefab, "iconImage", rowIcon);
        SetPrivateField(rowPrefab, "countText", rowCountTmp);
        SetPrivateField(rowPrefab, "tapButton", rowBtn);

        var slotWidgets = new PowerupLoadoutSlotWidget[ProgressionManager.MaxLoadoutSlotCount];
        for (int i = 0; i < slotWidgets.Length; i++)
        {
            GameObject slotGo = CreateTestGameObject($"Slot_{i}");
            slotGo.AddComponent<RectTransform>().SetParent(panelGo.transform, false);
            slotGo.AddComponent<Image>();
            slotGo.AddComponent<Button>();
            PowerupLoadoutSlotWidget slot = slotGo.AddComponent<PowerupLoadoutSlotWidget>();

            GameObject slotIconGo = CreateTestGameObject("Icon");
            slotIconGo.AddComponent<RectTransform>().SetParent(slotGo.transform, false);
            Image slotIcon = slotIconGo.AddComponent<Image>();

            GameObject slotCountGo = CreateTestGameObject("CountLabel");
            slotCountGo.AddComponent<RectTransform>().SetParent(slotGo.transform, false);
            TextMeshProUGUI slotCountTmp = slotCountGo.AddComponent<TextMeshProUGUI>();

            SetPrivateField(slot, "iconImage", slotIcon);
            SetPrivateField(slot, "countText", slotCountTmp);
            slotWidgets[i] = slot;
        }

        PowerUpData[] assets =
        {
            AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Breaker.asset"),
            AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Freeze.asset"),
            AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Teleport.asset")
        };

        SetPrivateField(panel, "powerUpAssets", assets);
        SetPrivateField(panel, "rowContainer", rowContainer);
        SetPrivateField(panel, "rowPrefab", rowPrefabGo);
        SetPrivateField(panel, "slotWidgets", slotWidgets);

        InvokeLifecycleIfPresent(panel, "Awake");
        InvokeLifecycleIfPresent(panel, "OnEnable");

        // Bottom row: modular unlocked cores stay equippable even with 0 upgrade shards, displaying plain shard count.
        Assert.AreEqual(3, panel.SpawnedRows.Count, "All 3 unlocked starter modular cores remain equippable.");
        Assert.AreEqual(PowerUpIds.Breaker, panel.SpawnedRows[0].SkillId);
        Assert.AreEqual("3", panel.SpawnedRows[0].CountText.text);
        Assert.AreEqual(PowerUpIds.Freeze, panel.SpawnedRows[1].SkillId);
        Assert.AreEqual("1", panel.SpawnedRows[1].CountText.text);
        Assert.AreEqual(PowerUpIds.Teleport, panel.SpawnedRows[2].SkillId);
        Assert.AreEqual("0", panel.SpawnedRows[2].CountText.text);

        // Equip Freeze -> slot 0 displays count "1", empty slots hide count text.
        Assert.IsTrue(panel.TryEquipToFirstEmptySlot(PowerUpIds.Freeze));
        Assert.IsNotNull(slotWidgets[0].CountText);
        Assert.IsTrue(slotWidgets[0].CountText.gameObject.activeSelf);
        Assert.AreEqual("1", slotWidgets[0].CountText.text);

        // Equip Breaker -> slot 1 displays count "3", slots 2 & 3 stay hidden.
        Assert.IsTrue(panel.TryEquipToFirstEmptySlot(PowerUpIds.Breaker));
        Assert.IsTrue(slotWidgets[1].CountText.gameObject.activeSelf);
        Assert.AreEqual("3", slotWidgets[1].CountText.text);
        Assert.IsFalse(slotWidgets[2].CountText.gameObject.activeSelf);
        Assert.IsFalse(slotWidgets[3].CountText.gameObject.activeSelf);

        // Adding charges refreshes both loadout slot count and row count automatically.
        progression.AddPowerUp(PowerUpIds.Freeze, 2, "UnitTestTopUp");
        Assert.AreEqual("3", slotWidgets[0].CountText.text);
        Assert.AreEqual("3", panel.SpawnedRows[1].CountText.text);

        // Shop: remove "[MOCK] " prefix from real-money price strings.
        GameObject iapGo = CreateTestGameObject("IAPManager");
        MockIAPService mockIap = iapGo.AddComponent<MockIAPService>();
        IAPManager iapMgr = iapGo.AddComponent<IAPManager>();
        iapMgr.SetIAPService(mockIap);

        ShopDatabase shopDb = AssetDatabase.LoadAssetAtPath<ShopDatabase>("Assets/Data/Shop/ShopDatabase_Main.asset");
        Assert.IsNotNull(shopDb, "ShopDatabase_Main.asset must exist.");
        int verifiedRealMoneyItems = 0;
        for (int i = 0; i < shopDb.Items.Count; i++)
        {
            ShopItemDefinition item = shopDb.Items[i];
            if (item == null || item.CostType != ShopCostType.RealMoney)
                continue;

            verifiedRealMoneyItems++;
            string formattedCost = ShopItemCardWidget.FormatCost(item, iapMgr);
            Assert.IsFalse(string.IsNullOrWhiteSpace(formattedCost));
            Assert.That(formattedCost.IndexOf("MOCK", StringComparison.OrdinalIgnoreCase), Is.LessThan(0),
                $"Shop item '{item.ItemId}' price '{formattedCost}' must not contain '[MOCK]'.");
        }
        Assert.Greater(verifiedRealMoneyItems, 0, "Expected at least one RealMoney shop item to verify.");
    }
}
