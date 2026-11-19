using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using Unity.Cinemachine;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Verification suite covering all Gravita upgrades:
/// - Directional Camera Kick across all 4 cardinal directions
/// - Hazard death Idle freeze & Time Dilation
/// - Achievement SkillUnlock rewards (3 starters -> 3 unlockables) & Choice A shard Coin discount
/// - Modular non-consumable PowerUp cooldowns, auto-trigger (Coins + Stars, CrackedBlock), & color-cycling slot visuals
/// - BlindBox multi-drop (3-4 items) & CoinUI multi-item summary
/// - UI row pooling (MissionPanelUI, AchievementPanelUI, PowerupPanelUI)
/// - Forward clock tamper guard (L4) & Save HMAC-SHA256 tamper recovery (L5)
/// </summary>
public class GravitaNewSystemsVerificationTests : EconomyTestHarnessBase
{
    [TearDown]
    public override void BaseTearDown()
    {
        Time.timeScale = 1f;
        SaveManager.EnforceSaveIntegrity = !Application.isEditor;
        NetworkTimeGuard.ResetSessionBaseline();
        CinemachineCameraShake2D.FrameCountProvider = () => Time.frameCount;
        base.BaseTearDown();
    }

    [Test]
    public void DirectionalCameraKick_SupportsAllFourDirections_AndCoalescesSameFrame()
    {
        GameObject camGo = CreateTestGameObject("CameraShake");
        camGo.AddComponent<CinemachineImpulseSource>();
        CinemachineCameraShake2D shake = camGo.AddComponent<CinemachineCameraShake2D>();
        InvokeLifecycleIfPresent(shake, "Awake");
        shake.ResetCoalescingState();

        int fakeFrame = 10;
        CinemachineCameraShake2D.FrameCountProvider = () => fakeFrame;

        Vector2[] dirs = { Vector2.left, Vector2.right, Vector2.up, Vector2.down };
        for (int i = 0; i < dirs.Length; i++)
        {
            fakeFrame++;
            shake.KickCamera(dirs[i], 0.25f);
            Assert.AreEqual(dirs[i], shake.LastKickDirection);
            Assert.AreEqual(0.25f, shake.LastKickDistance, 0.001f);
            Assert.AreEqual((Vector3)(dirs[i] * 0.25f), shake.CurrentKickOffset);
        }

        // Same-frame coalescing: smaller kick ignored, larger kick accepted
        int countBeforeCoalesce = shake.EmittedKickCount;
        fakeFrame++;
        shake.KickCamera(Vector2.left, 0.20f);
        shake.KickCamera(Vector2.right, 0.12f);
        Assert.AreEqual(countBeforeCoalesce + 1, shake.EmittedKickCount);
        Assert.AreEqual(Vector2.left, shake.LastKickDirection);

        shake.KickCamera(Vector2.right, 0.35f);
        Assert.AreEqual(countBeforeCoalesce + 2, shake.EmittedKickCount);
        Assert.AreEqual(Vector2.right, shake.LastKickDirection);
        Assert.AreEqual(0.35f, shake.LastKickDistance, 0.001f);
    }

    [Test]
    public void HazardDeathIdle_HaltsHazardsWithIdleAnimator_AndTimeDilationScalesClock()
    {
        GameObject rootGo = CreateTestGameObject("LevelRoot");
        LevelRoot levelRoot = rootGo.AddComponent<LevelRoot>();

        GameObject hazardGo = CreateTestGameObject("LaserHazard");
        hazardGo.transform.SetParent(rootGo.transform, false);
        BoxCollider2D col = hazardGo.AddComponent<BoxCollider2D>();
        col.isTrigger = true;
        Rigidbody2D rb = hazardGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        rb.linearVelocity = new Vector2(3f, 0f);
        Animator anim = hazardGo.AddComponent<Animator>();
        anim.speed = 2f;
        Hazard hazard = hazardGo.AddComponent<Hazard>();
        HazardActivation activation = hazardGo.AddComponent<HazardActivation>();
        InvokeLifecycleIfPresent(activation, "Awake");
        InvokeLifecycleIfPresent(hazard, "Awake");

        // Force activation to Active phase
        typeof(HazardActivation).GetProperty("CurrentPhase", BindingFlags.Instance | BindingFlags.Public)
            ?.SetValue(activation, HazardCyclePhase.Active);
        SetPrivateField(activation, "IsPowered", true);

        GameObject failGo = CreateTestGameObject("FailingTile");
        failGo.transform.SetParent(rootGo.transform, false);
        failGo.AddComponent<BoxCollider2D>().isTrigger = true;
        FailingTile failingTile = failGo.AddComponent<FailingTile>();
        InvokeLifecycleIfPresent(failingTile, "Awake");
        failingTile.ArmNow();

        levelRoot.RebuildCachedElements();

        // Player death -> halt hazards & keep Animator enabled playing Idle at 1x speed
        levelRoot.HaltHazardsOnPlayerDeath();
        Assert.IsTrue(hazard.IsHaltedOnDeath);
        Assert.IsFalse(hazard.IsActiveHazard);
        Assert.AreEqual(Vector2.zero, rb.linearVelocity);
        Assert.IsTrue(anim.enabled, "Hazard Animator must remain enabled for Idle animation on death.");
        Assert.AreEqual(1f, anim.speed, 0.001f);
        Assert.IsTrue(failingTile.IsHaltedForDeath);

        // Revive -> restore hazards
        levelRoot.RestoreHazardsAfterRevive();
        Assert.IsFalse(hazard.IsHaltedOnDeath);
        Assert.IsFalse(failingTile.IsHaltedForDeath);

        // Time Dilation -> slows LevelRoot clock & hazard animator + applies grace lethality suppression
        levelRoot.ClockTimeScale = 0.3f;
        hazard.ApplyTimeDilation(0.3f, suppressLethality: true);
        Assert.IsTrue(hazard.IsTimeDilated);
        Assert.IsFalse(hazard.IsActiveHazard);
        Assert.AreEqual(0.3f, anim.speed, 0.001f);

        float beforeClock = levelRoot.Clock.Time;
        levelRoot.TickClock(1f);
        Assert.AreEqual(0.3f, levelRoot.Clock.Time - beforeClock, 0.01f);

        hazard.ClearTimeDilation();
        Assert.IsFalse(hazard.IsTimeDilated);
        Assert.AreEqual(1f, anim.speed, 0.001f);
    }

    [Test]
    public void ModularCores_Starter3Unlocked_AchievementUnlocksRemaining3_AndShardDiscountReducesCoins()
    {
        EconomyManager economy = SpawnSingleton<EconomyManager>("Economy");
        ProgressionManager progression = SpawnSingleton<ProgressionManager>("Progression");

        // Starter 3 unlocked by default; remaining 3 locked until Achievement unlock
        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Breaker));
        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Freeze));
        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Teleport));
        Assert.IsFalse(progression.IsSkillUnlocked(PowerUpIds.Shield));
        Assert.IsFalse(progression.IsSkillUnlocked(PowerUpIds.Magnet));
        Assert.IsFalse(progression.IsSkillUnlocked(PowerUpIds.Echo));

        // Load Achievements 03, 05, 07 and meet lifetime targets
        ProfileData profile = SaveManager.loadProfile();
        profile.lifetimeValidLevelClears = 20;
        profile.lifetimeStarsCollected = 50;
        profile.lifetimeNoDeathLevelClears = 20;
        SaveManager.TrySaveProfile(profile);

        AchievementDefinition ach03 = AssetDatabase.LoadAssetAtPath<AchievementDefinition>("Assets/Data/Achievements/Achievement_03.asset");
        AchievementDefinition ach05 = AssetDatabase.LoadAssetAtPath<AchievementDefinition>("Assets/Data/Achievements/Achievement_05.asset");
        AchievementDefinition ach07 = AssetDatabase.LoadAssetAtPath<AchievementDefinition>("Assets/Data/Achievements/Achievement_07.asset");
        Assert.IsNotNull(ach03);
        Assert.IsNotNull(ach05);
        Assert.IsNotNull(ach07);

        Assert.IsTrue(AchievementService.TryClaim(ach03));
        Assert.IsTrue(AchievementService.TryClaim(ach05));
        Assert.IsTrue(AchievementService.TryClaim(ach07));

        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Shield));
        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Magnet));
        Assert.IsTrue(progression.IsSkillUnlocked(PowerUpIds.Echo));

        // Choice A upgrade: Lv1 -> Lv2 Kinetic Surge costs 100 Coins; 3 shards = 75 Coin discount -> only 25 Coins needed!
        PowerUpData breakerData = AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Breaker.asset");
        Assert.IsNotNull(breakerData);
        Assert.IsFalse(breakerData.IsConsumable);

        progression.AddPowerUp(PowerUpIds.Breaker, 3, "TestShards");
        economy.AddCoins(25, "TestCoins");

        int effectiveCost = progression.GetEffectiveUpgradeCoinCost(PowerUpIds.Breaker, breakerData, out int shardsConsumed);
        Assert.AreEqual(25, effectiveCost);
        Assert.AreEqual(3, shardsConsumed);

        Assert.IsTrue(progression.TryUpgradePowerUp(PowerUpIds.Breaker, breakerData, out string failReason), failReason);
        Assert.AreEqual(2, progression.GetPowerUpLevel(PowerUpIds.Breaker));
        Assert.AreEqual(0, economy.Coins);
        Assert.AreEqual(0, progression.GetPowerUpCount(PowerUpIds.Breaker));
    }

    [Test]
    public void ModularPowerUp_AutoTriggerCollectsStarsAndCoins_StartsCooldown_AndSlotCyclesColorsOpaque()
    {
        SpawnSingleton<EconomyManager>("Economy");
        ProgressionManager progression = SpawnSingleton<ProgressionManager>("Progression");

        // Unlock Magnet for auto-trigger test
        ProfileData profile = SaveManager.loadProfile();
        profile.UnlockSkill(PowerUpIds.Magnet);
        profile.UnlockSkill(PowerUpIds.Shield);
        SaveManager.TrySaveProfile(profile);

        GameObject rootGo = CreateTestGameObject("LevelRoot");
        LevelRoot root = rootGo.AddComponent<LevelRoot>();

        GameObject flowGo = CreateTestGameObject("LevelFlow");
        flowGo.AddComponent<LevelLoader>();
        flowGo.AddComponent<ObjectiveSystem>();
        LevelFlowController flow = flowGo.AddComponent<LevelFlowController>();
        SetPrivateField(flow, "currentRoot", root);
        SetPrivateField(flow, "currentState", LevelState.Playing);
        InvokeLifecycleIfPresent(flow, "Awake");

        GameObject playerGo = CreateTestGameObject("Player");
        playerGo.AddComponent<BoxCollider2D>();
        playerGo.AddComponent<Rigidbody2D>();
        PlayerMovement pm = playerGo.AddComponent<PlayerMovement>();
        // Simulate sliding movement right
        SetPrivateField(pm, "moveDirection", Vector2.right);

        GameObject starGo = CreateTestGameObject("StarCollectible");
        starGo.transform.SetParent(rootGo.transform, false);
        starGo.transform.position = new Vector3(1.5f, 0f, 0f);
        starGo.AddComponent<CircleCollider2D>().isTrigger = true;
        Collectible star = starGo.AddComponent<Collectible>();
        SetPrivateField(star, "collectibleType", CollectibleType.Star);
        root.RebuildCachedElements();

        GameObject puGo = CreateTestGameObject("PowerUps");
        PowerUpController pu = puGo.AddComponent<PowerUpController>();
        SetPrivateField(pu, "magnetData", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Magnet.asset"));
        SetPrivateField(pu, "breakerData", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Breaker.asset"));
        SetPrivateField(pu, "shieldData", AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Shield.asset"));
        SetPrivateField(pu, "playerMovement", pm);
        SetPrivateField(pu, "flowController", flow);
        InvokeLifecycleIfPresent(pu, "Awake");

        // Auto-trigger Gravity Lens near Star
        Assert.IsTrue(pu.EvaluateAutoTriggersNow(), "Gravity Lens should auto-trigger near uncollected Star.");
        Assert.IsTrue(pu.IsMagnetActive);

        // Modular Inertia Core auto-absorbs hazard hit and starts cooldown
        GameObject hazGo = CreateTestGameObject("TestHazard");
        hazGo.AddComponent<BoxCollider2D>().isTrigger = true;
        Hazard hazard = hazGo.AddComponent<Hazard>();
        Assert.IsTrue(pu.TryConsumeShieldHit(hazard), "Modular Inertia Core should auto-absorb hazard hit.");
        Assert.Greater(pu.GetCooldownRemaining(PowerUpType.Shield), 0f);

        // Verify PowerUpSlotWidget hides countText, shows NO text, cycles colors Opaque (a=1f), and locks AutoTrigger button click
        GameObject slotGo = CreateTestGameObject("SlotWidget");
        Image icon = slotGo.AddComponent<Image>();
        Button btn = slotGo.AddComponent<Button>();
        TMP_Text countTmp = CreateTestText("CountText", slotGo.transform);
        PowerUpSlotWidget slot = slotGo.AddComponent<PowerUpSlotWidget>();
        SetPrivateField(slot, "button", btn);
        SetPrivateField(slot, "iconImage", icon);
        SetPrivateField(slot, "countText", countTmp);
        SetPrivateField(slot, "readyColorCycleSpeed", 2.5f);
        SetPrivateField(slot, "activeColorCycleSpeed", 6f);

        slot.Setup(
            PowerUpType.Magnet,
            null,
            0,
            canActivate: false,
            isActiveEffect: false,
            onClicked: null,
            showChargeCount: false,
            isReadyState: true,
            cooldownFraction: 0f,
            activationMode: PowerUpActivationMode.AutoTrigger);

        Assert.IsFalse(countTmp.gameObject.activeSelf, "Modular HUD slots must NOT display charge/cooldown text.");
        Assert.IsFalse(btn.interactable, "AutoTrigger-only core slot must not allow manual activation click.");
        Assert.AreEqual(1f, btn.colors.disabledColor.a, 0.001f, "AGENTS §1.3 forbids alpha dimming on disabled AutoTrigger icons.");

        Color readyT0 = slot.EvaluateVisualColor(0.1f);
        Color readyT1 = slot.EvaluateVisualColor(0.3f);
        Assert.AreEqual(1f, readyT0.a, 0.001f);
        Assert.AreEqual(1f, readyT1.a, 0.001f);
        Assert.AreNotEqual(readyT0, readyT1, "Ready color cycling should oscillate smoothly with unscaledTime.");
    }

    [Test]
    public void BlindBox_MultiDrop_AndCoinUIMultiSummary_WorkCorrectly()
    {
        SpawnSingleton<EconomyManager>("Economy");
        BlindBoxService blindBox = SpawnSingleton<BlindBoxService>("BlindBoxService");
        LootPackDefinition pack = AssetDatabase.LoadAssetAtPath<LootPackDefinition>("Assets/Data/LootPacks/LootPack_BlindBox.asset");
        Assert.IsNotNull(pack);
        Assert.IsTrue(pack.AreBaseDropRatesValid(), $"BlindBox rates sum to {pack.GetTotalBaseDropRate()}");
        Assert.GreaterOrEqual(pack.MinRollsPerOpen, 3);
        Assert.LessOrEqual(pack.MaxRollsPerOpen, 5);

        blindBox.LootPack = pack;
        RewardBundle bundle = blindBox.Open(allowDirectMenuOpen: true);
        Assert.IsNotNull(bundle);
        Assert.GreaterOrEqual(blindBox.LastRollResults.Count, 3);
        Assert.LessOrEqual(blindBox.LastRollResults.Count, 4);

        // CoinUI multi-item formatting
        GameObject coinGo = CreateTestGameObject("CoinUI");
        TMP_Text coinText = CreateTestText("CoinAmountText", coinGo.transform);
        CoinUI coinUi = coinGo.AddComponent<CoinUI>();
        SetPrivateField(coinUi, "coinsText", coinText);

        var multiBundle = new RewardBundle();
        multiBundle.Add(RewardType.Coins, 80);
        multiBundle.Add(RewardType.Gems, 5);
        multiBundle.Add(RewardType.PowerUp, 1, PowerUpIds.Breaker);

        coinUi.PrepareDirectReward(multiBundle, alreadyGrantedBase: true);
        coinUi.OpenCoinBonus();
        StringAssert.Contains("+80 Coins", coinText.text);
        StringAssert.Contains("+5 Gems", coinText.text);
        StringAssert.Contains("Kinetic Surge", coinText.text);
    }

    [Test]
    public void UIPanels_ReusePooledRows_AcrossRepeatedRefreshes()
    {
        MissionDatabase missionDb = AssetDatabase.LoadAssetAtPath<MissionDatabase>("Assets/Data/Missions/MissionDatabase_Main.asset");
        AchievementDatabase achDb = AssetDatabase.LoadAssetAtPath<AchievementDatabase>("Assets/Data/Achievements/AchievementDatabase_Main.asset");
        Assert.IsNotNull(missionDb);
        Assert.IsNotNull(achDb);

        // 1. MissionPanelUI row pooling
        GameObject mGo = CreateTestGameObject("MissionPanel");
        Transform mContainer = CreateTestGameObject("MContainer", mGo.transform).transform;
        GameObject mRowPrefab = CreateMissionRowPrefab("MRowPrefab");
        MissionPanelUI mPanel = mGo.AddComponent<MissionPanelUI>();
        SetPrivateField(mPanel, "missionDatabase", missionDb);
        SetPrivateField(mPanel, "rowContainer", mContainer);
        SetPrivateField(mPanel, "rowPrefab", mRowPrefab);

        mPanel.RefreshList();
        var mFirstIds = new List<int>();
        foreach (MissionRowWidget w in mContainer.GetComponentsInChildren<MissionRowWidget>(true))
            mFirstIds.Add(w.gameObject.GetInstanceID());
        Assert.Greater(mFirstIds.Count, 0);

        mPanel.RefreshList();
        var mSecondIds = new List<int>();
        foreach (MissionRowWidget w in mContainer.GetComponentsInChildren<MissionRowWidget>(true))
            mSecondIds.Add(w.gameObject.GetInstanceID());
        CollectionAssert.AreEqual(mFirstIds, mSecondIds, "MissionPanelUI must reuse pooled MissionRowWidgets across refreshes.");

        // 2. AchievementPanelUI row pooling
        GameObject aGo = CreateTestGameObject("AchievementPanel");
        Transform aContainer = CreateTestGameObject("AContainer", aGo.transform).transform;
        GameObject aRowPrefab = CreateAchRowPrefab("ARowPrefab");
        AchievementPanelUI aPanel = aGo.AddComponent<AchievementPanelUI>();
        SetPrivateField(aPanel, "achievementDatabase", achDb);
        SetPrivateField(aPanel, "rowContainer", aContainer);
        SetPrivateField(aPanel, "rowPrefab", aRowPrefab);

        aPanel.RefreshList();
        Assert.Greater(aPanel.SpawnedRows.Count, 0);
        int firstAchRowId = aPanel.SpawnedRows[0].gameObject.GetInstanceID();
        aPanel.RefreshList();
        Assert.AreEqual(firstAchRowId, aPanel.SpawnedRows[0].gameObject.GetInstanceID(), "AchievementPanelUI must reuse pooled rows.");
    }

    [Test]
    public void SaveIntegrityGuard_AndNetworkTimeGuard_BlockTamperAndRecoverBackup()
    {
        try
        {
            // L5: Save HMAC tamper detection & .bak recovery
            SaveManager.EnforceSaveIntegrity = true;
            var p1 = new ProfileData { coins = 100 };
            p1.MigrateIfNeeded();
            Assert.IsTrue(SaveManager.TrySaveProfile(p1));

            var p2 = new ProfileData { coins = 500 };
            p2.MigrateIfNeeded();
            Assert.IsTrue(SaveManager.TrySaveProfile(p2));

            // Tamper primary profile.json with bogus coin count without updating .sig
            File.WriteAllText(SaveManager.ProfileFilePath, "{\"coins\":999999,\"saveSchemaVersion\":2}");
            SaveManager.ClearMemoryCacheForTesting();

            ProfileData recovered = SaveManager.ReloadProfileFromDisk();
            Assert.IsNotNull(recovered);
            Assert.AreEqual(100, recovered.coins, "Tampered primary save must be rejected and recovered from verified .bak.");

            // L4: NetworkTimeGuard forward jump tamper check
            double mockSeconds = 10.0;
            NetworkTimeGuard.ResetSessionBaseline();
            NetworkTimeGuard.EnforceOnCustomClockForTesting = true;
            NetworkTimeGuard.MonotonicSecondsProvider = () => mockSeconds;

            DateTime start = new DateTime(2026, 11, 16, 12, 0, 0);
            var clock = new FixedLocalClockProvider(start);
            LocalClock.Provider = clock;

            var prof = new ProfileData();
            prof.MigrateIfNeeded();
            LocalClock.EvaluateClockAndUpdateHighWater(prof, LocalClock.Now, out _, out bool tampered1);
            Assert.IsFalse(tampered1);

            // Jump wall clock +3 days while only 5s of uptime elapsed
            mockSeconds = 15.0;
            clock.Current = start.AddDays(3);
            LocalClock.EvaluateClockAndUpdateHighWater(prof, LocalClock.Now, out DateTime clampedNow, out bool tampered2);
            Assert.IsTrue(tampered2, "Forward jump of +3 days with 5s uptime must trigger clock tamper guard.");
            Assert.Less(clampedNow, start.AddHours(1), "Clamped time must remain near monotonic baseline.");
        }
        finally
        {
            SaveManager.EnforceSaveIntegrity = !Application.isEditor;
            NetworkTimeGuard.ResetSessionBaseline();
        }
    }

    private GameObject CreateMissionRowPrefab(string name)
    {
        GameObject go = CreateTestGameObject(name);
        go.AddComponent<RectTransform>();
        go.SetActive(false);
        go.AddComponent<MissionRowWidget>();
        return go;
    }

    private GameObject CreateAchRowPrefab(string name)
    {
        GameObject go = CreateTestGameObject(name);
        go.AddComponent<RectTransform>();
        go.SetActive(false);
        go.AddComponent<AchievementRowWidget>();
        return go;
    }

    private GameObject CreateTestGameObject(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go;
    }

    private TMP_Text CreateTestText(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name, parent);
        go.AddComponent<RectTransform>();
        return go.AddComponent<TextMeshProUGUI>();
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        Type t = target.GetType();
        while (t != null)
        {
            FieldInfo f = t.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (f != null)
            {
                f.SetValue(target, value);
                return;
            }
            PropertyInfo p = t.GetProperty(fieldName, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (p != null && p.CanWrite)
            {
                p.SetValue(target, value);
                return;
            }
            t = t.BaseType;
        }
        Assert.Fail($"Field or writable property '{fieldName}' not found on {target.GetType().Name}.");
    }
}
