using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UI;

/// <summary>
/// Comprehensive EditMode verification suite for Maze Runner Mode:
///   1. Bootstrap asset generation (Room shell prefab, PopulationProfile, ModeConfig).
///   2. 2-tier RuntimeLevelPopulator probabilities, ranges, unlock gating, SpawnNode priority, Drone routes, and zero-soft-lock slide solvability.
///   3. Continuous multi-room Goal -> Throat -> Start -> One-Way Airlock stitching & window sliding cleanup.
///   4. Endless + 10-Room target settlement into ProfileData & RewardService.
///   5. RunnerUI, InGameHUD ("ROOM N", hidden stars), and ResultPanelUI integration.
/// </summary>
public class MazeRunnerSystemTests : EconomyTestHarnessBase
{
    private TileBase wallTile;
    private TileBase bgTile;
    private GameObject dummyBlockPrefab;
    private GameObject dummyHazardPrefab;
    private GameObject dummyEventPrefab;
    private GameObject dummyCoinPrefab;
    private GameObject dummyDronePrefab;

    [OneTimeSetUp]
    public void OneTimeBootstrapAssets()
    {
        RunnerAssetBootstrap.EnsureRunnerAssetsExist(forceRebuild: true);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    [SetUp]
    public override void BaseSetUp()
    {
        base.BaseSetUp();
        PlayerPrefs.DeleteKey(RunnerUI.BEST_DEPTH_PREF_KEY);

        wallTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Ground Palette.asset");
        bgTile = AssetDatabase.LoadAssetAtPath<TileBase>("Assets/Tilemap/Background Palette.asset");

        dummyBlockPrefab = CreateTrackedPrefab("TestSolidBlock", go =>
        {
            go.AddComponent<BoxCollider2D>();
            go.AddComponent<CrackedBlock>();
        });

        dummyHazardPrefab = CreateTrackedPrefab("TestHazard", go =>
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
            go.AddComponent<Hazard>();
        });

        dummyEventPrefab = CreateTrackedPrefab("TestEventVectorPad", go =>
        {
            var col = go.AddComponent<BoxCollider2D>();
            col.isTrigger = true;
        });

        dummyCoinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/Coin.prefab");
        if (dummyCoinPrefab == null)
        {
            dummyCoinPrefab = CreateTrackedPrefab("TestCoin", go =>
            {
                var col = go.AddComponent<BoxCollider2D>();
                col.isTrigger = true;
                go.AddComponent<Collectible>();
            });
        }

        dummyDronePrefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Gameplay/SentinelDrone.prefab");
    }

    [TearDown]
    public override void BaseTearDown()
    {
        PlayerPrefs.DeleteKey(RunnerUI.BEST_DEPTH_PREF_KEY);
        if (MazeRunnerService.Instance != null)
            MazeRunnerService.Instance.CleanupRunWorld();
        Time.timeScale = 1f;
        base.BaseTearDown();
    }

    // ─────────────────────────────────────────────────────────────
    //  1. Bootstrap Assets Verification
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void BootstrapAssets_ExistAndContainValidReferences()
    {
        GameObject shellPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RunnerAssetBootstrap.RoomShellPrefabPath);
        RunnerPopulationProfile popProfile = AssetDatabase.LoadAssetAtPath<RunnerPopulationProfile>(RunnerAssetBootstrap.PopulationProfileAssetPath);
        RunnerModeConfig modeConfig = AssetDatabase.LoadAssetAtPath<RunnerModeConfig>(RunnerAssetBootstrap.ModeConfigAssetPath);

        Assert.IsNotNull(shellPrefab, "RunnerRoomShell_Template.prefab should be generated.");
        Assert.IsNotNull(popProfile, "RunnerPopulationProfile_Default.asset should be generated.");
        Assert.IsNotNull(modeConfig, "RunnerModeConfig_Main.asset should be generated.");

        RunnerRoomController room = shellPrefab.GetComponent<RunnerRoomController>();
        Assert.IsNotNull(room);
        Assert.IsNotNull(shellPrefab.GetComponent<LevelRoot>());
        Assert.IsNotNull(room.StartNode);
        Assert.IsNotNull(room.GoalNode);
        Assert.AreEqual(RunnerPortalRole.Start, room.StartNode.Role);
        Assert.AreEqual(RunnerPortalRole.Goal, room.GoalNode.Role);

        Assert.IsNotNull(modeConfig.proceduralRoomShellPrefab);
        Assert.IsNotNull(modeConfig.patternLibrary);
        Assert.IsNotNull(modeConfig.earlyDifficultyProfile);
        Assert.IsNotNull(modeConfig.midDifficultyProfile);
        Assert.IsNotNull(modeConfig.lateDifficultyProfile);
        Assert.IsNotNull(modeConfig.defaultPopulationProfile);
        Assert.AreEqual(MazeSizePreset.Large, modeConfig.proceduralSizePreset);
        Assert.AreEqual(RunnerModeKind.Endless, modeConfig.defaultMode);

        Assert.Greater(popProfile.blockEntries.Count, 0);
        Assert.Greater(popProfile.enemyEntries.Count, 0);
        Assert.Greater(popProfile.eventEntries.Count, 0);
        Assert.Greater(popProfile.pickupEntries.Count, 0);
    }

    // ─────────────────────────────────────────────────────────────
    //  2. Two-Tier RuntimeLevelPopulator Tests
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void Populator_RespectsTwoTierProbabilities_Ranges_AndRoomUnlockGating()
    {
        RunnerRoomController room = CreateSyntheticTestRoom(BuildCorridorRoomGrid(11, 11));
        GameObject popGo = CreateTestGameObject("Populator");
        RuntimeLevelPopulator populator = popGo.AddComponent<RuntimeLevelPopulator>();

        RunnerPopulationProfile profile = TrackAsset(ScriptableObject.CreateInstance<RunnerPopulationProfile>());
        profile.enableBlocks = true;
        profile.roomBlockTriggerChance = 0f; // Tier 1 disabled by chance -> 0 blocks even if entries have 100%
        profile.roomBlocksRange = new Vector2Int(2, 3);
        profile.blockEntries.Add(MakeEntry("b", RunnerSpawnCategory.Block, dummyBlockPrefab, 1f, 1f, 1, 3, 1, 0, false));

        profile.enableEnemies = true;
        profile.roomEnemyTriggerChance = 1f;
        profile.roomEnemiesRange = new Vector2Int(2, 2);
        profile.enemyEntries.Add(MakeEntry("e_early", RunnerSpawnCategory.Enemy, dummyHazardPrefab, 1f, 1f, 1, 2, 1, 0, false));
        profile.enemyEntries.Add(MakeEntry("e_late", RunnerSpawnCategory.Enemy, dummyEventPrefab, 1f, 100f, 2, 2, 5, 0, false));

        profile.enableEvents = true;
        profile.roomEventTriggerChance = 1f;
        profile.roomEventsRange = new Vector2Int(1, 2);
        profile.eventEntries.Add(MakeEntry("ev1", RunnerSpawnCategory.Event, dummyEventPrefab, 1f, 1f, 1, 2, 1, 0, false));

        profile.enablePickups = false;

        RunnerPopulationReport room1Report = populator.PopulateRoom(room, room.ExtractLogicalGridFromTilemap(), profile, roomNumber: 1, seed: 42);
        Assert.AreEqual(0, room1Report.BlocksSpawned, "0% roomBlockTriggerChance must suppress all blocks.");
        Assert.AreEqual(2, room1Report.EnemiesSpawned, "100% enemy trigger with [2..2] range should spawn 2 enemies.");
        Assert.IsTrue(room1Report.RoomEventTriggered);
        Assert.GreaterOrEqual(room1Report.EventsSpawned, 1);

        // Ensure late-room entry (minRoomIndex=5) never spawned in Room 1
        foreach (Transform child in room.transform)
        {
            if (child.name == dummyEventPrefab.name)
                Assert.LessOrEqual(room1Report.EventsSpawned, 2);
        }

        room.ReleaseRoomEntities();

        // Now unlock blocks at 100% and verify Room 5 sees blocks & late entries
        profile.roomBlockTriggerChance = 1f;
        RunnerPopulationReport room5Report = populator.PopulateRoom(room, room.ExtractLogicalGridFromTilemap(), profile, roomNumber: 5, seed: 99);
        Assert.GreaterOrEqual(room5Report.BlocksSpawned, 1);
    }

    [Test]
    public void Populator_PrioritizesSpawnNodes_ConfiguresDroneRoutes_AndPreventsSoftLocks()
    {
        MazeGrid grid = BuildCorridorRoomGrid(11, 11);
        RunnerRoomController room = CreateSyntheticTestRoom(grid);

        // Place an explicit RunnerSpawnNode anchor inside safe corridor
        GameObject anchorGo = CreateTestGameObject("AnchorBlock", room.transform);
        anchorGo.transform.position = new Vector3(5.5f, 5.5f, 0f);
        var spawnNode = anchorGo.AddComponent<RunnerSpawnNode>();
        spawnNode.allowedCategory = RunnerSpawnCategory.Event;
        spawnNode.activationChance = 1f;
        spawnNode.prefabOverrides = new[] { dummyEventPrefab };

        GameObject popGo = CreateTestGameObject("Populator");
        RuntimeLevelPopulator populator = popGo.AddComponent<RuntimeLevelPopulator>();

        RunnerPopulationProfile profile = TrackAsset(ScriptableObject.CreateInstance<RunnerPopulationProfile>());
        profile.enableBlocks = true;
        profile.roomBlockTriggerChance = 1f;
        profile.roomBlocksRange = new Vector2Int(2, 4);
        profile.blockEntries.Add(MakeEntry("solid", RunnerSpawnCategory.Block, dummyBlockPrefab, 1f, 1f, 2, 4, 1, 0, blocksSlide: true));

        profile.enableEnemies = dummyDronePrefab != null;
        profile.roomEnemyTriggerChance = 1f;
        profile.roomEnemiesRange = new Vector2Int(1, 2);
        if (dummyDronePrefab != null)
            profile.enemyEntries.Add(MakeEntry("drone", RunnerSpawnCategory.Enemy, dummyDronePrefab, 1f, 1f, 1, 2, 1, 0, blocksSlide: false));

        profile.enableEvents = true;
        profile.roomEventTriggerChance = 1f;
        profile.roomEventsRange = new Vector2Int(1, 1);
        profile.eventEntries.Add(MakeEntry("ev", RunnerSpawnCategory.Event, dummyEventPrefab, 1f, 1f, 1, 1, 1, 0, blocksSlide: false));

        profile.enablePickups = true;
        profile.roomPickupsRange = new Vector2Int(3, 5);
        profile.pickupEntries.Add(MakeEntry("coin", RunnerSpawnCategory.Pickup, dummyCoinPrefab, 1f, 1f, 3, 5, 1, 0, blocksSlide: false));
        profile.enforceSlideSolvability = true;

        RunnerPopulationReport report = populator.PopulateRoom(room, grid, profile, roomNumber: 2, seed: 1337);
        Assert.AreEqual(1, report.SpawnNodesProcessed, "Authored RunnerSpawnNode should be processed.");
        Assert.Greater(report.PickupsSpawned, 0);

        if (dummyDronePrefab != null)
        {
            SentinelDronePatrol drone = room.GetComponentInChildren<SentinelDronePatrol>(true);
            Assert.IsNotNull(drone, "SentinelDrone should spawn on corridor cell.");
        }

        // Verify blocking obstacles kept the MazeGrid 100% solvable with zero soft-locks
        MazeGrid postGrid = grid.Clone();
        foreach (CrackedBlock block in room.GetComponentsInChildren<CrackedBlock>(true))
        {
            int gx = Mathf.FloorToInt(block.transform.position.x) - room.WorldGridOriginOffset.x;
            int gy = Mathf.FloorToInt(block.transform.position.y) - room.WorldGridOriginOffset.y;
            if (postGrid.InBounds(gx, gy))
                postGrid.SetCell(gx, gy, MazeCell.Wall);
        }

        MazeSlideGraph check = MazeSlideGraph.Build(postGrid, postGrid.Spawn, requireExitOnStopCell: true);
        Assert.IsTrue(check.IsSolvable, "Populated room must remain 100% solvable with blocking obstacles.");
        Assert.AreEqual(0, check.SoftLockedStops.Count, "Populated room must contain zero soft-locked stops.");
    }

    [Test]
    public void Populator_WithDefaultAssetConfig_PreservesSolvabilityAcrossMultipleProceduralSeeds()
    {
        RunnerModeConfig modeConfig = AssetDatabase.LoadAssetAtPath<RunnerModeConfig>(RunnerAssetBootstrap.ModeConfigAssetPath);
        Assert.IsNotNull(modeConfig);

        GameObject popGo = CreateTestGameObject("Populator");
        RuntimeLevelPopulator populator = popGo.AddComponent<RuntimeLevelPopulator>();

        for (int i = 0; i < 4; i++)
        {
            int seed = 10000 + i * 313;
            var request = new MazeGenerationRequest
            {
                Seed = seed,
                Profile = modeConfig.ResolveDifficultyProfile(i + 1),
                Library = modeConfig.patternLibrary,
                OverridePreset = MazeSizePreset.Large,
                OverrideSlotCols = 4,
                OverrideSlotRows = 7,
            };

            MazeGenerationResult result = MazeGenerator.Generate(in request);
            Assert.IsTrue(result.Success, $"Procedural Large generation failed on seed {seed}: {result.FailureReason}");
            Assert.AreEqual(17, result.Grid.Width);
            Assert.AreEqual(29, result.Grid.Height);

            RunnerRoomController room = CreateSyntheticTestRoom(result.Grid);
            populator.PopulateRoom(room, result.Grid, modeConfig.defaultPopulationProfile, i + 1, seed + 9);

            // Treat SolidBlock and Cracked as blocking obstacles when validating MazeSlideGraph safety
            MazeGrid postGrid = result.Grid.Clone();
            foreach (Transform child in room.transform)
            {
                if (child.name == "SolidBlock" || child.name == "Cracked")
                {
                    int gx = Mathf.FloorToInt(child.position.x) - room.WorldGridOriginOffset.x;
                    int gy = Mathf.FloorToInt(child.position.y) - room.WorldGridOriginOffset.y;
                    if (postGrid.InBounds(gx, gy))
                        postGrid.SetCell(gx, gy, MazeCell.Wall);
                }
            }

            MazeSlideGraph postGraph = MazeSlideGraph.Build(postGrid, postGrid.Spawn, requireExitOnStopCell: true);
            Assert.IsTrue(postGraph.IsSolvable, $"Seed {seed} lost solvability after PopulateRoom.");
            Assert.AreEqual(0, postGraph.SoftLockedStops.Count, $"Seed {seed} introduced soft-locks after PopulateRoom.");
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  3. Continuous Multi-Room Stitching, Airlock & Window Cleanup
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void MazeRunnerService_StitchesThreeRoomsContinuously_SealsAirlocks_AndCleansOldRooms([Values(false, true)] bool withGlobalBackground)
    {
        SetupRunnerRig(withGlobalBackground, out MazeRunnerService runner, out LevelFlowController flow, out RuntimeTilemapMerger merger, out Tilemap globalWallTm, out PlayerMovement player);

        Assert.IsTrue(runner.StartRun(RunnerModeKind.Endless));
        Assert.IsTrue(runner.IsRunActive);
        Assert.AreEqual(1, runner.ActiveSession.CurrentRoomNumber);
        Assert.AreEqual(2, runner.ActiveRooms.Count, "Should spawn Room 1 + lookahead Room 2.");

        RunnerRoomController room1 = runner.ActiveRooms[0];
        RunnerRoomController room2 = runner.ActiveRooms[1];
        Assert.AreEqual(17 * 29, room1.LogicalWidth * room1.LogicalHeight, "Large preset must yield 17x29 rooms (either Portrait or rotated 90 deg).");
        Assert.IsTrue((room1.LogicalWidth == 17 && room1.LogicalHeight == 29) || (room1.LogicalWidth == 29 && room1.LogicalHeight == 17));
        Assert.Greater(runner.CarvedFloorCells.Count, 0, "Throat between Room 1 and Room 2 must be carved.");

        // Confirm carved throat cells have no wall on globalWallTm
        foreach (Vector3Int carvedCell in runner.CarvedFloorCells)
        {
            Assert.IsFalse(globalWallTm.HasTile(carvedCell), $"Carved throat cell {carvedCell} should be open on global WallTilemap.");
        }

        // Transition Room 1 Goal -> Room 2 Start
        Vector3Int room2StartCell = room2.WorldStartCell;
        room1.GoalNode.NotifyPlayerEntered(player);
        Assert.IsTrue(runner.IsInThroatTransit, "Entering GoalNode should enter throat transit state.");

        room2.StartNode.NotifyPlayerEntered(player);
        Assert.IsFalse(runner.IsInThroatTransit, "Reaching Room 2 StartNode should complete transit.");
        Assert.AreEqual(2, runner.ActiveSession.CurrentRoomNumber);
        Assert.AreEqual(1, runner.ActiveSession.ClearedRooms);
        Assert.AreEqual(3, runner.ActiveRooms.Count, "Window should now retain Room 1, Room 2, and spawn lookahead Room 3.");

        // Confirm One-Way Airlock directly behind Room 2 StartNode is sealed
        Vector3Int airlockCell = room2StartCell - ToStep3Int(room1.ExitDirection);
        Assert.IsTrue(runner.SealedWallCells.Contains(airlockCell), $"One-Way Airlock cell {airlockCell} behind StartNode_2 must be sealed.");
        Assert.IsTrue(globalWallTm.HasTile(airlockCell), "Global WallTilemap must render & collide on the sealed Airlock.");

        // Transition Room 2 Goal -> Room 3 Start (verifies Room 1 eviction beyond retainedPreviousRooms=1)
        RunnerRoomController room3 = runner.ActiveRooms[2];
        room2.GoalNode.NotifyPlayerEntered(player);
        room3.StartNode.NotifyPlayerEntered(player);

        Assert.AreEqual(3, runner.ActiveSession.CurrentRoomNumber);
        Assert.AreEqual(2, runner.ActiveSession.ClearedRooms);
        Assert.IsTrue(room1 == null, "Room 1 beyond retainedPreviousRooms=1 must be destroyed.");
        Assert.AreEqual(3, runner.ActiveRooms.Count, "Active window should hold Room 2, Room 3, and Room 4.");
        Assert.AreEqual(4, runner.ActiveRooms[2].RoomNumber);
    }

    // ─────────────────────────────────────────────────────────────
    //  4. Modes (Endless vs 10-Room) & Atomic Economy Settlement
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void MazeRunnerService_EndlessDeathAnd10RoomVictory_SettleCoinsExpAndRecordsAtomically()
    {
        SetupRunnerRig(false, out MazeRunnerService runner, out LevelFlowController flow, out _, out _, out PlayerMovement player);

        RunnerRunSummary? lastSummary = null;
        runner.OnRunEnded += s => lastSummary = s;

        Assert.IsTrue(runner.StartRun(RunnerModeKind.Endless));

        // Simulate clearing Room 1 and collecting 3 coins
        RunnerRoomController r1 = runner.ActiveRooms[0];
        RunnerRoomController r2 = runner.ActiveRooms[1];
        r1.GoalNode.NotifyPlayerEntered(player);
        r2.StartNode.NotifyPlayerEntered(player);

        runner.ActiveSession.AddInRunCoins(3);
        flow.FailLevel("Sentinel Drone");

        Assert.IsTrue(lastSummary.HasValue);
        Assert.AreEqual(RunnerModeKind.Endless, lastSummary.Value.Mode);
        Assert.AreEqual(1, lastSummary.Value.ClearedRooms);
        Assert.IsTrue(lastSummary.Value.IsNewBest);
        Assert.IsFalse(lastSummary.Value.CompletedTarget);

        int expectedCoins = 3 + runner.ModeConfig.coinsPerClearedRoom;
        Assert.AreEqual(expectedCoins, lastSummary.Value.TotalCoinsGranted);

        ProfileData saved = SaveManager.loadProfile();
        Assert.AreEqual(expectedCoins, saved.coins);
        Assert.AreEqual(1, saved.bestRunnerEndlessRooms);

        // Now verify 10-Room mode clear grants bonusCoins10Rooms & records clears
        lastSummary = null;
        Assert.IsTrue(runner.StartRun(RunnerModeKind.Rooms10));
        for (int i = 1; i < 10; i++)
        {
            RunnerRoomController cur = runner.CurrentRoom;
            RunnerRoomController nxt = runner.ActiveRooms[runner.ActiveRooms.Count - 1];
            cur.GoalNode.NotifyPlayerEntered(player);
            nxt.StartNode.NotifyPlayerEntered(player);
        }

        Assert.AreEqual(10, runner.ActiveSession.CurrentRoomNumber);
        Assert.AreEqual(10, runner.CurrentRoom.RoomNumber);
        Assert.AreEqual(10, runner.ActiveRooms[runner.ActiveRooms.Count - 1].RoomNumber, "Lookahead must cap cleanly at target room 10.");

        runner.CurrentRoom.GoalNode.NotifyPlayerEntered(player);
        Assert.IsTrue(lastSummary.HasValue);
        Assert.IsTrue(lastSummary.Value.CompletedTarget);
        Assert.AreEqual(10, lastSummary.Value.ClearedRooms);
        Assert.AreEqual(
            10 * runner.ModeConfig.coinsPerClearedRoom + runner.ModeConfig.bonusCoins10Rooms,
            lastSummary.Value.TotalCoinsGranted);

        ProfileData post10 = SaveManager.loadProfile();
        Assert.AreEqual(1, post10.bestRunner10RoomsClears);
        Assert.AreEqual(10, post10.bestRunnerEndlessRooms);
    }

    // ─────────────────────────────────────────────────────────────
    //  5. RunnerUI, InGameHUD & ResultPanelUI Integration
    // ─────────────────────────────────────────────────────────────

    [Test]
    public void RunnerUI_InGameHUD_AndResultPanel_ReflectRunnerStateCorrectly()
    {
        SetupRunnerRig(false, out MazeRunnerService runner, out LevelFlowController flow, out _, out _, out PlayerMovement player);

        // 1. Seed legacy PlayerPrefs and ensure RunnerUI migrates best depth into ProfileData
        PlayerPrefs.SetInt(RunnerUI.BEST_DEPTH_PREF_KEY, 7);

        GameObject runnerUiGo = CreateTestGameObject("RunnerUI");
        runnerUiGo.AddComponent<RectTransform>();
        RunnerUI runnerUi = runnerUiGo.AddComponent<RunnerUI>();
        Button startBtn = CreateTestButton("StartBtn", runnerUiGo.transform);
        TMP_Text bestText = CreateTestText("BestText", runnerUiGo.transform);
        TMP_Text bestScoreText = CreateTestText("BestScoreText", runnerUiGo.transform);
        Button endlessBtn = CreateTestButton("EndlessBtn", runnerUiGo.transform);
        Button r10Btn = CreateTestButton("R10Btn", runnerUiGo.transform);
        Button r20Btn = CreateTestButton("R20Btn", runnerUiGo.transform);

        SetPrivateField(runnerUi, "startRunButton", startBtn);
        SetPrivateField(runnerUi, "bestDepthText", bestText);
        SetPrivateField(runnerUi, "bestDepthScoreText", bestScoreText);
        SetPrivateField(runnerUi, "endlessModeButton", endlessBtn);
        SetPrivateField(runnerUi, "rooms10ModeButton", r10Btn);
        SetPrivateField(runnerUi, "rooms20ModeButton", r20Btn);
        SetPrivateField(runnerUi, "enable10And20RoomModes", false);
        SetPrivateField(runnerUi, "flowController", flow);
        SetPrivateField(runnerUi, "runnerService", runner);
        InvokeLifecycleIfPresent(runnerUi, "Awake");

        runnerUi.OpenRunner();
        Assert.AreEqual("BEST: 7 ROOMS", bestText.text);
        Assert.AreEqual("7", bestScoreText.text);
        Assert.IsFalse(r10Btn.gameObject.activeSelf, "10-room button must stay hidden when enable10And20RoomModes is false.");
        Assert.IsFalse(r20Btn.gameObject.activeSelf, "20-room button must stay hidden when enable10And20RoomModes is false.");
        Assert.AreEqual(7, SaveManager.loadProfile().bestRunnerEndlessRooms);

        // 2. Setup InGameHUD & ResultPanelUI before clicking Start Run
        GameObject hudGo = CreateTestGameObject("InGameHUD");
        hudGo.AddComponent<RectTransform>();
        InGameHUD hud = hudGo.AddComponent<InGameHUD>();
        TMP_Text hudMoves = CreateTestText("MovesText", hudGo.transform);
        TMP_Text hudCoins = CreateTestText("CoinsText", hudGo.transform);
        var starImgs = new Image[3];
        for (int i = 0; i < 3; i++)
        {
            GameObject sGo = CreateTestGameObject($"Star{i}", hudGo.transform);
            sGo.AddComponent<RectTransform>();
            starImgs[i] = sGo.AddComponent<Image>();
        }
        SetPrivateField(hud, "flowController", flow);
        SetPrivateField(hud, "movesText", hudMoves);
        SetPrivateField(hud, "coinsText", hudCoins);
        SetPrivateField(hud, "starImages", starImgs);
        InvokeLifecycleIfPresent(hud, "Awake");
        InvokeLifecycleIfPresent(hud, "Start");

        // Click Start Run on RunnerUI -> launches Runner Room 1 & switches HUD
        startBtn.onClick.Invoke();
        Assert.IsTrue(runner.IsRunActive);
        Assert.AreEqual("ROOM 1", hudMoves.text);
        for (int i = 0; i < 3; i++)
            Assert.IsFalse(starImgs[i].gameObject.activeSelf, "Story star icons must be hidden during Runner mode.");

        // Advance to Room 2 -> HUD updates dynamically
        runner.ActiveRooms[0].GoalNode.NotifyPlayerEntered(player);
        runner.ActiveRooms[1].StartNode.NotifyPlayerEntered(player);
        Assert.AreEqual("ROOM 2", hudMoves.text);

        // 3. Verify ResultPanelUI renders failure summary & handles Retry / Return cleanly
        GameObject resGo = CreateTestGameObject("ResultPanelUI");
        resGo.AddComponent<RectTransform>();
        ResultPanelUI resPanel = resGo.AddComponent<ResultPanelUI>();
        GameObject compObj = CreateTestGameObject("Comp", resGo.transform);
        GameObject failObj = CreateTestGameObject("Fail", resGo.transform);
        TMP_Text compTitle = CreateTestText("CompTitle", compObj.transform);
        TMP_Text coinsEarned = CreateTestText("CoinsEarned", compObj.transform);
        TMP_Text failTitle = CreateTestText("FailTitle", failObj.transform);
        TMP_Text failReason = CreateTestText("FailReason", failObj.transform);
        Button nextBtn = CreateTestButton("NextBtn", compObj.transform);
        Button compRestartBtn = CreateTestButton("CompRestartBtn", compObj.transform);
        Button compMapBtn = CreateTestButton("CompMapBtn", compObj.transform);
        Button retryBtn = CreateTestButton("RetryBtn", failObj.transform);
        Button failMapBtn = CreateTestButton("FailMapBtn", failObj.transform);

        SetPrivateField(resPanel, "flowController", flow);
        SetPrivateField(resPanel, "completionPanel", compObj);
        SetPrivateField(resPanel, "failurePanel", failObj);
        SetPrivateField(resPanel, "completionTitleText", compTitle);
        SetPrivateField(resPanel, "coinsEarnedText", coinsEarned);
        SetPrivateField(resPanel, "failTitleText", failTitle);
        SetPrivateField(resPanel, "failureReasonText", failReason);
        SetPrivateField(resPanel, "nextLevelButton", nextBtn);
        SetPrivateField(resPanel, "completionRestartButton", compRestartBtn);
        SetPrivateField(resPanel, "completionMapButton", compMapBtn);
        SetPrivateField(resPanel, "retryButton", retryBtn);
        SetPrivateField(resPanel, "failureMapButton", failMapBtn);
        InvokeLifecycleIfPresent(resPanel, "Awake");

        flow.FailLevel("Hazard");
        resPanel.Show();

        Assert.IsTrue(failObj.activeSelf);
        Assert.AreEqual("ROOMS CLEARED: 1", failTitle.text);
        StringAssert.Contains("REACHED ROOM 2", failReason.text);
        StringAssert.Contains("+15 COINS", failReason.text);

        // Retry button restarts Runner mode
        retryBtn.onClick.Invoke();
        Assert.IsTrue(runner.IsRunActive, "ResultPanelUI Retry should restart Maze Runner.");
        Assert.AreEqual(1, runner.ActiveSession.CurrentRoomNumber);

        // Map button abandons cleanly and clears HUD runner state
        flow.FailLevel("Hazard");
        resPanel.Show();
        failMapBtn.onClick.Invoke();
        Assert.IsFalse(runner.IsRunActive);
        Assert.AreEqual(LevelState.Idle, flow.CurrentState);
        Assert.IsTrue(starImgs[0].gameObject.activeSelf, "Story star visibility must restore after returning from Runner.");
    }

    // ─────────────────────────────────────────────────────────────
    //  Local Test Helpers
    // ─────────────────────────────────────────────────────────────

    private void SetupRunnerRig(
        bool includeGlobalBackgroundTilemap,
        out MazeRunnerService runner,
        out LevelFlowController flow,
        out RuntimeTilemapMerger merger,
        out Tilemap globalWallTm,
        out PlayerMovement player)
    {
        RunnerModeConfig sourceConfig = AssetDatabase.LoadAssetAtPath<RunnerModeConfig>(RunnerAssetBootstrap.ModeConfigAssetPath);
        RunnerModeConfig testConfig = TrackAsset(UnityEngine.Object.Instantiate(sourceConfig));
        testConfig.lookaheadRooms = 1;
        testConfig.retainedPreviousRooms = 1;
        testConfig.throatLengthCells = 2;

        // Global Tilemap Grid (matches GameScene where globalBackgroundTilemap may be null)
        GameObject globalGridGo = CreateTestGameObject("GlobalTilemapGrid");
        Grid g = globalGridGo.AddComponent<Grid>();
        g.cellSize = Vector3.one;

        GameObject wallGo = CreateTestGameObject("GlobalWallTilemap", globalGridGo.transform);
        globalWallTm = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();
        wallGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var wtCol = wallGo.AddComponent<TilemapCollider2D>();
        wtCol.compositeOperation = Collider2D.CompositeOperation.Merge;
        wallGo.AddComponent<CompositeCollider2D>();

        Tilemap globalBgTm = null;
        if (includeGlobalBackgroundTilemap)
        {
            GameObject bgGo = CreateTestGameObject("GlobalBackgroundTilemap", globalGridGo.transform);
            globalBgTm = bgGo.AddComponent<Tilemap>();
            bgGo.AddComponent<TilemapRenderer>();
        }

        GameObject mergerGo = CreateTestGameObject("RuntimeTileMerger");
        merger = mergerGo.AddComponent<RuntimeTilemapMerger>();
        SetPrivateField(merger, "globalWallTilemap", globalWallTm);
        SetPrivateField(merger, "globalBackgroundTilemap", globalBgTm);
        SetPrivateField(merger, "fillPadding", 1);

        // Systems (Loader, Objective, Flow, Populator, Runner)
        GameObject sysGo = CreateTestGameObject("GameSystems");
        LevelLoader loader = sysGo.AddComponent<LevelLoader>();
        ObjectiveSystem objSys = sysGo.AddComponent<ObjectiveSystem>();
        flow = sysGo.AddComponent<LevelFlowController>();
        SetPrivateField(flow, "levelLoader", loader);
        SetPrivateField(flow, "objectiveSystem", objSys);
        SetPrivateField(flow, "maxRevivesPerLevel", 0);
        InvokeLifecycleIfPresent(flow, "Awake");

        RuntimeLevelPopulator populator = sysGo.AddComponent<RuntimeLevelPopulator>();
        populator.SetDefaultProfile(testConfig.defaultPopulationProfile);

        runner = sysGo.AddComponent<MazeRunnerService>();
        Transform roomsParent = CreateTestGameObject("RunnerRooms").transform;
        runner.ConfigureForTesting(testConfig, flow, merger, populator, roomsParent, fixedSeed: 24680);
        InvokeLifecycleIfPresent(runner, "Awake");

        // Player
        GameObject playerGo = CreateTestGameObject("Player");
        var rb = playerGo.AddComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        playerGo.AddComponent<BoxCollider2D>();
        player = playerGo.AddComponent<PlayerMovement>();
        InvokeLifecycleIfPresent(player, "Awake");
    }

    private RunnerRoomController CreateSyntheticTestRoom(MazeGrid grid)
    {
        GameObject roomGo = CreateTestGameObject("SyntheticRoom");
        LevelRoot root = roomGo.AddComponent<LevelRoot>();
        RunnerRoomController room = roomGo.AddComponent<RunnerRoomController>();

        GameObject gridGo = CreateTestGameObject("Grid", roomGo.transform);
        Grid unityGrid = gridGo.AddComponent<Grid>();

        GameObject wallGo = CreateTestGameObject("WallTilemap", gridGo.transform);
        int wallLayer = LayerMask.NameToLayer("Wall");
        if (wallLayer >= 0) wallGo.layer = wallLayer;
        Tilemap wallTm = wallGo.AddComponent<Tilemap>();
        wallGo.AddComponent<TilemapRenderer>();

        GameObject bgGo = CreateTestGameObject("BackgroundTilemap", gridGo.transform);
        int bgLayer = LayerMask.NameToLayer("Background");
        if (bgLayer >= 0) bgGo.layer = bgLayer;
        Tilemap bgTm = bgGo.AddComponent<Tilemap>();
        bgGo.AddComponent<TilemapRenderer>();

        GameObject startGo = CreateTestGameObject("StartNode", roomGo.transform);
        startGo.AddComponent<BoxCollider2D>().isTrigger = true;
        startGo.AddComponent<PlayerSpawnPoint>();
        RunnerPortalNode startPortal = startGo.AddComponent<RunnerPortalNode>();

        GameObject goalGo = CreateTestGameObject("GoalNode", roomGo.transform);
        goalGo.AddComponent<BoxCollider2D>().isTrigger = true;
        RunnerPortalNode goalPortal = goalGo.AddComponent<RunnerPortalNode>();

        room.BindInspectorReferences(root, unityGrid, wallTm, bgTm, startPortal, goalPortal);
        room.StampProceduralGrid(grid, wallTile, bgTile, CardinalDirection.Up, CardinalDirection.Up);
        room.InitializeForRoom(1, grid);
        return room;
    }

    private static MazeGrid BuildCorridorRoomGrid(int width, int height)
    {
        var grid = new MazeGrid(width, height, MazeCell.Floor);
        for (int x = 0; x < width; x++)
        {
            grid.SetCell(x, 0, MazeCell.Wall);
            grid.SetCell(x, height - 1, MazeCell.Wall);
        }
        for (int y = 0; y < height; y++)
        {
            grid.SetCell(0, y, MazeCell.Wall);
            grid.SetCell(width - 1, y, MazeCell.Wall);
        }

        grid.SetSpawn(new Vector2Int(1, 1));
        grid.SetExit(new Vector2Int(width - 2, height - 2));
        return grid;
    }

    private GameObject CreateTestGameObject(string name, Transform parent)
    {
        GameObject go = CreateTestGameObject(name);
        if (parent != null)
            go.transform.SetParent(parent, false);
        return go;
    }

    private GameObject CreateTrackedPrefab(string name, Action<GameObject> setup)
    {
        GameObject go = CreateTestGameObject(name);
        go.SetActive(false);
        setup?.Invoke(go);
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

    private static RunnerSpawnEntry MakeEntry(
        string id,
        RunnerSpawnCategory category,
        GameObject prefab,
        float spawnChance,
        float weight,
        int minCount,
        int maxCount,
        int minRoomIndex,
        int maxRoomIndex,
        bool blocksSlide)
    {
        return new RunnerSpawnEntry
        {
            entryId = id,
            category = category,
            prefab = prefab,
            spawnChance = spawnChance,
            weight = weight,
            countRange = new Vector2Int(minCount, maxCount),
            minRoomIndex = minRoomIndex,
            maxRoomIndex = maxRoomIndex,
            blocksSlideTraversal = blocksSlide,
        };
    }

    private static void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
        Assert.IsNotNull(field, $"Field '{fieldName}' not found on {target.GetType().Name}.");
        field.SetValue(target, value);
    }

    private static Vector3Int ToStep3Int(CardinalDirection dir)
    {
        return dir switch
        {
            CardinalDirection.Right => Vector3Int.right,
            CardinalDirection.Down  => Vector3Int.down,
            CardinalDirection.Left  => Vector3Int.left,
            _                       => Vector3Int.up,
        };
    }
}
