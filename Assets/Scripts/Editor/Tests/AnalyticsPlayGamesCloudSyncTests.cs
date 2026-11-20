using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using Firebase.Analytics;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class AnalyticsPlayGamesCloudSyncTests : EconomyTestHarnessBase
{
    private bool originalEnforceIntegrity;

    [SetUp]
    public override void BaseSetUp()
    {
        base.BaseSetUp();
        originalEnforceIntegrity = SaveManager.EnforceSaveIntegrity;
        AnalyticsService.ResetForTesting();
        PlayGamesConfig.SetActiveConfig(null);
    }

    [TearDown]
    public override void BaseTearDown()
    {
        SaveManager.EnforceSaveIntegrity = originalEnforceIntegrity;
        PlayGamesConfig.SetActiveConfig(null);
        AnalyticsService.ResetForTesting();
        base.BaseTearDown();
    }

    [Test]
    public void FirebaseAnalyticsProvider_BuffersEarlyEvents_SanitizesGA4Limits_AndAnalyticsServiceReplaysMockEvents()
    {
        var dispatched = new List<(string Name, Parameter[] Params)>();
        var userIds = new List<string>();
        bool collectionEnabled = false;

        var bufferedProvider = new FirebaseAnalyticsProvider(
            logWithParams: (n, p) => dispatched.Add((n, p)),
            logWithoutParams: n => dispatched.Add((n, Array.Empty<Parameter>())),
            setUserId: uid => userIds.Add(uid),
            setCollectionEnabled: enabled => collectionEnabled = enabled,
            echoToConsoleInEditor: false);

        // Buffer events & userId while backend is not ready yet
        bufferedProvider.SetUserId("early_user_42");
        bufferedProvider.TrackEvent("firebase_custom_event", new Dictionary<string, object>
        {
            ["google_key"] = true,
            ["2nd_attempt"] = 7,
            ["nan_metric"] = float.NaN,
            ["long_text"] = new string('x', 140)
        });

        Assert.IsFalse(bufferedProvider.IsBackendReady);
        Assert.AreEqual(1, bufferedProvider.BufferedEventCount);
        Assert.AreEqual(0, dispatched.Count);
        Assert.AreEqual(0, userIds.Count);

        // Marking backend ready enables collection, applies cached userId, and drains buffer FIFO
        bufferedProvider.MarkBackendReady();
        Assert.IsTrue(collectionEnabled);
        Assert.IsTrue(bufferedProvider.IsBackendReady);
        Assert.AreEqual(0, bufferedProvider.BufferedEventCount);
        Assert.AreEqual(1, userIds.Count);
        Assert.AreEqual("early_user_42", userIds[0]);
        Assert.AreEqual(1, dispatched.Count);
        Assert.AreEqual("app_firebase_custom_event", dispatched[0].Name);
        Assert.AreEqual(4, dispatched[0].Params.Length);

        var paramDict = ReadFirebaseParameters(dispatched[0].Params);
        Assert.AreEqual(1L, paramDict["app_google_key"]);
        Assert.AreEqual(7L, paramDict["p_2nd_attempt"]);
        Assert.AreEqual(0d, paramDict["nan_metric"]);
        Assert.AreEqual(100, ((string)paramDict["long_text"]).Length);

        // Verify GA4 identifier sanitization & 25-param cap
        Assert.AreEqual("game_session_start", FirebaseAnalyticsProvider.SanitizeEventName("session_start"));
        Assert.AreEqual("e_1st_clear", FirebaseAnalyticsProvider.SanitizeEventName("1st_clear"));
        string truncatedName = FirebaseAnalyticsProvider.SanitizeEventName("firebase_" + new string('a', 60));
        Assert.IsTrue(truncatedName.StartsWith("app_firebase_", StringComparison.Ordinal));
        Assert.LessOrEqual(truncatedName.Length, FirebaseAnalyticsProvider.MaxIdentifierLength);

        var oversizedParams = new Dictionary<string, object>();
        for (int i = 0; i < 30; i++)
            oversizedParams[$"key_{i:00}"] = i;
        Parameter[] capped = FirebaseAnalyticsProvider.BuildParameters(oversizedParams);
        Assert.AreEqual(FirebaseAnalyticsProvider.MaxParameterCount, capped.Length);

        // Verify AnalyticsService replays early MockAnalyticsProvider events when connecting Firebase
        var replayed = new List<(string Name, Parameter[] Params)>();
        var replayUserIds = new List<string>();
        AnalyticsService svc = SpawnSingleton<AnalyticsService>("AnalyticsService");
        Assert.IsInstanceOf<MockAnalyticsProvider>(svc.Provider);

        AnalyticsService.Track(AnalyticsEvents.LevelCompleted, new Dictionary<string, object>
        {
            ["level_id"] = "Level_001",
            ["stars"] = 3
        });

        var serviceFbProvider = new FirebaseAnalyticsProvider(
            logWithParams: (n, p) => replayed.Add((n, p)),
            logWithoutParams: n => replayed.Add((n, Array.Empty<Parameter>())),
            setUserId: uid => replayUserIds.Add(uid),
            setCollectionEnabled: unusedEnabled => { },
            echoToConsoleInEditor: false);

        AnalyticsService.ConnectFirebaseAnalytics("uid_abc", serviceFbProvider);
        Assert.AreSame(serviceFbProvider, svc.Provider);
        Assert.AreEqual("uid_abc", serviceFbProvider.LastUserId);
        Assert.Contains("uid_abc", replayUserIds);
        Assert.AreEqual(2, replayed.Count);
        Assert.AreEqual("game_session_start", replayed[0].Name);
        Assert.AreEqual("level_completed", replayed[1].Name);

        AnalyticsService.UpdateUserId("uid_xyz");
        Assert.AreEqual("uid_xyz", serviceFbProvider.LastUserId);
    }

    [Test]
    public void PlayGamesService_AutoSignsIn_SyncsSavedGames_AndForwardsAchievementsAndRunnerLeaderboards()
    {
        SpawnSingleton<EconomyManager>("Economy");
        SpawnSingleton<ProgressionManager>("Progression");
        PlayGamesService pgService = SpawnSingleton<PlayGamesService>("PlayGamesService");
        ProfileCloudSyncService syncService = SpawnSingleton<ProfileCloudSyncService>("CloudSync");

        PlayGamesConfig config = TrackAsset(ScriptableObject.CreateInstance<PlayGamesConfig>());
        config.ConfigureForTesting(
            enabled: true,
            autoSignIn: true,
            enableCloudSync: true,
            syncAchs: true,
            syncBoards: true,
            achievements: new[]
            {
                new PlayGamesAchievementMap
                {
                    internalAchievementId = "ach_03",
                    gpgsAchievementId = "CgkI_ach03"
                }
            },
            leaderboards: new[]
            {
                new PlayGamesLeaderboardMap
                {
                    leaderboardKey = PlayGamesConfig.LeaderboardRunnerEndless,
                    gpgsLeaderboardId = "CgkI_lb_endless"
                }
            });
        PlayGamesConfig.SetActiveConfig(config);

        var fakeBackend = new FakePlayGamesBackend();
        pgService.SetBackendForTesting(fakeBackend);

        AuthResult signInResult = pgService.SignInAsync(interactive: false).GetAwaiter().GetResult();
        Assert.IsTrue(signInResult.Success);
        Assert.AreEqual("gpgs_player_1", signInResult.UserId);
        Assert.IsTrue(pgService.IsAuthenticated);
        Assert.IsNotNull(pgService.CloudSave);
        Assert.IsTrue(pgService.CloudSave.IsAvailable);

        // Seed GPGS Saved Games slot and verify ProfileCloudSyncService merges it via GPGS alone
        var remoteSeed = new ProfileData
        {
            noAdsPurchased = true,
            bestRunnerEndlessRooms = 11
        };
        remoteSeed.MigrateIfNeeded();
        fakeBackend.SavedSlots[ProfileCloudSyncService.ProfileSlot] = JsonUtility.ToJson(remoteSeed, false);

        bool synced = syncService.ReconcileAndSyncAsync(pgService.CloudSave, pgService.PlayerId).GetAwaiter().GetResult();
        Assert.IsTrue(synced);

        ProfileData mergedLocal = SaveManager.loadProfile();
        Assert.IsTrue(mergedLocal.noAdsPurchased);
        Assert.AreEqual(11, mergedLocal.bestRunnerEndlessRooms);

        // Claim Achievement_03 -> forwards to GPGS ach_03 -> CgkI_ach03
        mergedLocal.lifetimeValidLevelClears = 20;
        SaveManager.TrySaveProfile(mergedLocal);

        AchievementDefinition ach03 = AssetDatabase.LoadAssetAtPath<AchievementDefinition>("Assets/Data/Achievements/Achievement_03.asset");
        Assert.IsNotNull(ach03);
        bool claimed = AchievementService.TryClaim(ach03);
        Assert.IsTrue(claimed);
        Assert.Contains("CgkI_ach03", fakeBackend.UnlockedAchievementIds);

        // Record new Maze Runner best -> forwards to GPGS leaderboard CgkI_lb_endless
        bool isNewBest = mergedLocal.RecordRunnerRun(RunnerModeKind.Endless, 15, completedTarget: false);
        Assert.IsTrue(isNewBest);
        Assert.AreEqual(1, fakeBackend.ReportedScores.Count);
        Assert.AreEqual("CgkI_lb_endless", fakeBackend.ReportedScores[0].LeaderboardId);
        Assert.AreEqual(15L, fakeBackend.ReportedScores[0].Score);
    }

    [Test]
    public void SafeUnityPackageImporter_ExtractsDotSlashTarGzEntries_PreservesMetas_AndSkipsBundledEdm4u()
    {
        string pkgPath = Path.Combine(IsolatedSaveDir, "MockGpgsPackage.unitypackage");
        string extractRoot = Path.Combine(IsolatedSaveDir, "ExtractedProject");
        Directory.CreateDirectory(extractRoot);

        byte[] sampleAssetBytes = Encoding.UTF8.GetBytes("// GPGS Sample File");
        byte[] sampleMetaBytes = Encoding.UTF8.GetBytes("fileFormatVersion: 2\nguid: 11111111111111111111111111111111\n");
        byte[] folderMetaBytes = Encoding.UTF8.GetBytes("fileFormatVersion: 2\nfolderAsset: yes\n");
        byte[] edmBytes = Encoding.UTF8.GetBytes("stale edm4u dll");

        BuildMockUnityPackage(pkgPath, new[]
        {
            ("./guid_gpgs_folder", "./Assets/Public/GooglePlayGames", null, folderMetaBytes),
            ("./guid_gpgs_file", "./Assets/Public/GooglePlayGames/Sample.txt", sampleAssetBytes, sampleMetaBytes),
            ("./guid_edm4u", "./Assets/ExternalDependencyManager/Editor/Old.dll", edmBytes, sampleMetaBytes),
            ("./guid_traversal", "../../outside.txt", edmBytes, null)
        });

        SafePackageImportReport report = SafeUnityPackageImporter.ExtractPackageToProjectRoot(
            pkgPath,
            extractRoot,
            skipExternalDependencyManager: true);

        string extractedFile = Path.Combine(extractRoot, "Assets", "Public", "GooglePlayGames", "Sample.txt");
        string extractedMeta = extractedFile + ".meta";
        string extractedFolder = Path.Combine(extractRoot, "Assets", "Public", "GooglePlayGames");
        string extractedFolderMeta = extractedFolder + ".meta";
        string skippedEdmFile = Path.Combine(extractRoot, "Assets", "ExternalDependencyManager", "Editor", "Old.dll");
        string escapedFile = Path.GetFullPath(Path.Combine(extractRoot, "..", "..", "outside.txt"));

        Assert.IsTrue(File.Exists(extractedFile));
        Assert.IsTrue(File.Exists(extractedMeta));
        Assert.AreEqual(sampleAssetBytes, File.ReadAllBytes(extractedFile));
        Assert.AreEqual(sampleMetaBytes, File.ReadAllBytes(extractedMeta));
        Assert.IsTrue(Directory.Exists(extractedFolder));
        Assert.IsTrue(File.Exists(extractedFolderMeta));
        Assert.IsFalse(File.Exists(skippedEdmFile));
        Assert.IsFalse(File.Exists(escapedFile));

        Assert.IsTrue(report.ContainsGooglePlayGames);
        Assert.AreEqual(1, report.SkippedEdm4uCount);
        Assert.AreEqual(1, report.SkippedInvalidPathCount);
        Assert.GreaterOrEqual(report.ExtractedAssetCount, 1);
    }

    [Test]
    public void ProfileCloudMerger_AndSyncService_ReconcileReinstallAndDivergedDevicesWithoutCoinDuplication()
    {
        EconomyManager economy = SpawnSingleton<EconomyManager>("Economy");
        ProfileCloudSyncService syncService = SpawnSingleton<ProfileCloudSyncService>("CloudSync");

        int ledgerEventsCount = 0;
        void HandleLedger(EconomyCurrencyType unusedCurr, int unusedDelta, int unusedBal, string unusedTag) => ledgerEventsCount++;
        EconomyManager.OnCurrencyLedgerEvent += HandleLedger;

        try
        {
            // Scenario A: Fresh reinstall local + rich cloud -> adopts cloud wallet and progression
            var freshLocal = new ProfileData();
            freshLocal.MigrateIfNeeded();

            var richCloud = new ProfileData
            {
                coins = 540,
                gems = 12,
                playerExp = 350,
                noAdsPurchased = true,
                bestRunnerEndlessRooms = 9,
                highWaterLocalTicks = 5000L
            };
            richCloud.unlockedLevelIDs.AddRange(new[] { "Level_001", "Level_002" });
            richCloud.completedLevelIDs.AddRange(new[] { "Level_001", "Level_002" });
            richCloud.SetLevelStarsBest("Level_001", 2);
            richCloud.MigrateIfNeeded();

            ProfileData reinstallMerged = ProfileCloudMerger.SmartMerge(freshLocal, richCloud);
            Assert.AreEqual(540, reinstallMerged.coins);
            Assert.AreEqual(12, reinstallMerged.gems);
            Assert.IsTrue(reinstallMerged.noAdsPurchased);
            Assert.AreEqual(9, reinstallMerged.bestRunnerEndlessRooms);
            Assert.AreEqual(2, reinstallMerged.GetLevelStars("Level_001"));

            // Scenario B: Diverged devices -> keeps newer/primary wallet (no coin duplication) while merging progression
            var localDiverged = new ProfileData
            {
                coins = 120,
                gems = 3,
                playerExp = 400,
                lifetimeCoinsCollected = 800,
                bestRunnerEndlessRooms = 7,
                highWaterLocalTicks = 2000L
            };
            localDiverged.completedLevelIDs.AddRange(new[] { "Level_001", "Level_003" });
            localDiverged.SetLevelStarsBest("Level_001", 3);
            localDiverged.SetLevelBestMoves("Level_001", 4);
            localDiverged.UnlockSkill("shield");
            localDiverged.SetPowerUpLevel("breaker", 3);
            localDiverged.MigrateIfNeeded();

            var cloudDiverged = new ProfileData
            {
                coins = 900,
                gems = 20,
                playerExp = 650,
                lifetimeCoinsCollected = 900,
                bestRunnerEndlessRooms = 18,
                noAdsPurchased = true,
                highWaterLocalTicks = 1000L
            };
            cloudDiverged.completedLevelIDs.AddRange(new[] { "Level_001", "Level_002" });
            cloudDiverged.SetLevelStarsBest("Level_001", 1);
            cloudDiverged.SetLevelBestMoves("Level_001", 8);
            cloudDiverged.SetPowerUpLevel("freeze", 4);
            cloudDiverged.claimedAchievementIds.Add("ach_03");
            cloudDiverged.MigrateIfNeeded();

            ProfileData divergedMerged = ProfileCloudMerger.SmartMerge(localDiverged, cloudDiverged);
            Assert.AreEqual(120, divergedMerged.coins);
            Assert.AreEqual(3, divergedMerged.gems);
            Assert.IsTrue(divergedMerged.completedLevelIDs.Contains("Level_001"));
            Assert.IsTrue(divergedMerged.completedLevelIDs.Contains("Level_002"));
            Assert.IsTrue(divergedMerged.completedLevelIDs.Contains("Level_003"));
            Assert.AreEqual(3, divergedMerged.GetLevelStars("Level_001"));
            Assert.AreEqual(4, divergedMerged.GetBestMoves("Level_001"));
            Assert.IsTrue(divergedMerged.HasSkill("shield"));
            Assert.AreEqual(3, divergedMerged.GetPowerUpLevel("breaker"));
            Assert.AreEqual(4, divergedMerged.GetPowerUpLevel("freeze"));
            Assert.AreEqual(18, divergedMerged.bestRunnerEndlessRooms);
            Assert.IsTrue(divergedMerged.noAdsPurchased);
            Assert.IsTrue(divergedMerged.claimedAchievementIds.Contains("ach_03"));

            // Scenario C: End-to-end ProfileCloudSyncService reconcile with HMAC verification
            SaveManager.EnforceSaveIntegrity = true;
            Assert.IsTrue(SaveManager.TrySaveProfile(localDiverged));

            var fakeCloud = new FakeCloudSaveProvider();
            fakeCloud.Slots[ProfileCloudSyncService.ProfileSlot] = JsonUtility.ToJson(cloudDiverged, false);

            bool synced = syncService.ReconcileAndSyncAsync(fakeCloud, "uid_sync").GetAwaiter().GetResult();
            Assert.IsTrue(synced);
            Assert.AreEqual("uid_sync", syncService.LastSyncedUserId);

            string savedJson = File.ReadAllText(SaveManager.ProfileFilePath);
            Assert.IsTrue(SaveIntegrityGuard.VerifyFileSignature(SaveManager.ProfileFilePath, savedJson));

            ProfileData reloaded = SaveManager.ReloadProfileFromDisk();
            Assert.AreEqual(120, reloaded.coins);
            Assert.AreEqual(18, reloaded.bestRunnerEndlessRooms);
            Assert.IsTrue(reloaded.completedLevelIDs.Contains("Level_002"));
            Assert.IsTrue(reloaded.noAdsPurchased);

            ProfileData uploadedCloud = SaveManager.DeserializeAndMigrateJson(fakeCloud.Slots[ProfileCloudSyncService.ProfileSlot]);
            Assert.IsNotNull(uploadedCloud);
            Assert.IsTrue(uploadedCloud.completedLevelIDs.Contains("Level_003"));
            Assert.AreEqual(18, uploadedCloud.bestRunnerEndlessRooms);
            Assert.AreEqual(120, economy.Coins);
            Assert.AreEqual(3, economy.Gems);
            Assert.AreEqual(0, ledgerEventsCount, "Cloud merge wallet sync must never emit synthetic currency ledger analytics.");
        }
        finally
        {
            EconomyManager.OnCurrencyLedgerEvent -= HandleLedger;
        }
    }

    private static Dictionary<string, object> ReadFirebaseParameters(Parameter[] parameters)
    {
        var dict = new Dictionary<string, object>(parameters.Length);
        FieldInfo nameField = typeof(Parameter).GetField("<Name>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        FieldInfo valueField = typeof(Parameter).GetField("<Value>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(nameField);
        Assert.IsNotNull(valueField);

        for (int i = 0; i < parameters.Length; i++)
        {
            string name = (string)nameField.GetValue(parameters[i]);
            object val = valueField.GetValue(parameters[i]);
            dict[name] = val;
        }
        return dict;
    }

    private static void BuildMockUnityPackage(
        string destPath,
        IEnumerable<(string GuidDir, string Pathname, byte[] AssetBytes, byte[] MetaBytes)> items)
    {
        using FileStream fs = File.Create(destPath);
        using var gzip = new GZipStream(fs, CompressionMode.Compress);

        foreach (var item in items)
        {
            WriteTarEntry(gzip, $"{item.GuidDir}/pathname", Encoding.UTF8.GetBytes(item.Pathname + "\n"));
            if (item.AssetBytes != null)
                WriteTarEntry(gzip, $"{item.GuidDir}/asset", item.AssetBytes);
            if (item.MetaBytes != null)
                WriteTarEntry(gzip, $"{item.GuidDir}/asset.meta", item.MetaBytes);
        }

        byte[] endBlocks = new byte[1024];
        gzip.Write(endBlocks, 0, endBlocks.Length);
    }

    private static void WriteTarEntry(Stream stream, string entryName, byte[] payload)
    {
        byte[] header = new byte[512];
        byte[] nameBytes = Encoding.UTF8.GetBytes(entryName);
        Array.Copy(nameBytes, 0, header, 0, Mathf.Min(nameBytes.Length, 100));

        WriteOctalField(header, 100, 8, 420); // 0644
        WriteOctalField(header, 108, 8, 0);
        WriteOctalField(header, 116, 8, 0);
        WriteOctalField(header, 124, 12, payload.Length);
        WriteOctalField(header, 136, 12, 0);

        for (int i = 148; i < 156; i++)
            header[i] = (byte)' ';

        header[156] = (byte)'0';

        int checksum = 0;
        for (int i = 0; i < 512; i++)
            checksum += header[i];
        WriteOctalChecksum(header, 148, checksum);

        stream.Write(header, 0, 512);
        if (payload.Length > 0)
        {
            stream.Write(payload, 0, payload.Length);
            int rem = payload.Length % 512;
            if (rem > 0)
            {
                byte[] pad = new byte[512 - rem];
                stream.Write(pad, 0, pad.Length);
            }
        }
    }

    private static void WriteOctalField(byte[] dst, int offset, int width, long value)
    {
        string oct = Convert.ToString(value, 8).PadLeft(width - 1, '0');
        byte[] bytes = Encoding.ASCII.GetBytes(oct);
        Array.Copy(bytes, 0, dst, offset, bytes.Length);
        dst[offset + width - 1] = 0;
    }

    private static void WriteOctalChecksum(byte[] dst, int offset, int checksum)
    {
        string oct = Convert.ToString(checksum, 8).PadLeft(6, '0');
        byte[] bytes = Encoding.ASCII.GetBytes(oct);
        Array.Copy(bytes, 0, dst, offset, 6);
        dst[offset + 6] = 0;
        dst[offset + 7] = (byte)' ';
    }

    private sealed class FakePlayGamesBackend : IPlayGamesBackend
    {
        public bool IsSupported => true;
        public bool IsAuthenticated { get; private set; }
        public string UserId { get; private set; }
        public string DisplayName => "TestPlayer";

        public readonly Dictionary<string, string> SavedSlots = new Dictionary<string, string>();
        public readonly List<string> UnlockedAchievementIds = new List<string>();
        public readonly List<(string LeaderboardId, long Score)> ReportedScores = new List<(string, long)>();

        public void Initialize() { }

        public Task<bool> AuthenticateAsync(bool interactive)
        {
            IsAuthenticated = true;
            UserId = "gpgs_player_1";
            return Task.FromResult(true);
        }

        public Task<string> ReadSavedGameJsonAsync(string slotName)
        {
            SavedSlots.TryGetValue(slotName, out string json);
            return Task.FromResult(json);
        }

        public Task<bool> WriteSavedGameJsonAsync(string slotName, string json)
        {
            SavedSlots[slotName] = json;
            return Task.FromResult(true);
        }

        public Task<bool> UnlockAchievementAsync(string gpgsId)
        {
            UnlockedAchievementIds.Add(gpgsId);
            return Task.FromResult(true);
        }

        public Task<bool> ReportScoreAsync(string gpgsLeaderboardId, long score)
        {
            ReportedScores.Add((gpgsLeaderboardId, score));
            return Task.FromResult(true);
        }

        public void ShowAchievementsUI() { }
        public void ShowLeaderboardsUI(string gpgsLeaderboardId = null) { }
    }

    private sealed class FakeCloudSaveProvider : ICloudSaveProvider
    {
        public bool IsAvailable => true;
        public readonly Dictionary<string, string> Slots = new Dictionary<string, string>();

        public Task<bool> UploadAsync(string slot, string json)
        {
            Slots[slot] = json;
            return Task.FromResult(true);
        }

        public Task<string> DownloadAsync(string slot)
        {
            Slots.TryGetValue(slot, out string payload);
            return Task.FromResult(payload);
        }
    }
}
