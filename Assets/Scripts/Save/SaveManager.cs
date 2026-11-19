using System;
using System.IO;
using UnityEngine;

public static class SaveManager
{
    /// <summary>
    /// Optional isolated save directory used by automated EditMode tests so real developer
    /// save files under <see cref="Application.persistentDataPath"/> are never touched.
    /// </summary>
    public static string CustomSaveDirectoryForTesting { get; set; }

    private static string ActiveDirectory =>
        !string.IsNullOrEmpty(CustomSaveDirectoryForTesting)
            ? CustomSaveDirectoryForTesting
            : Application.persistentDataPath;

    public static string ProfileFilePath => Path.Combine(ActiveDirectory, "profile.json");
    public static string TempProfileFilePath => ProfileFilePath + ".tmp";
    public static string BackupProfileFilePath => ProfileFilePath + ".bak";

    private static ProfileData cachedProfile;
    private static ProfileData pendingDeferredProfile;
    private static bool hasPendingDeferredSave;
    private static PlayerMovement boundSlideFlushPlayer;
    private static bool slideFlushAppHooksRegistered;

    /// <summary>
    /// Optional testing hook to simulate a disk write failure and verify rollback/retry behavior.
    /// </summary>
    public static bool SimulateSaveFailureForTesting { get; set; } = false;

    /// <summary>
    /// Optional testing hook enabling slide-deferral semantics in EditMode tests where
    /// <see cref="Application.isPlaying"/> is false.
    /// </summary>
    public static bool ForceSlideSaveDeferralForTesting { get; set; } = false;

    /// <summary>
    /// Enables strict HMAC-SHA256 verification against <c>.sig</c> sidecar files when loading saves (L5).
    /// Enabled automatically in Player builds; tests can toggle explicitly while verifying tamper recovery.
    /// </summary>
    public static bool EnforceSaveIntegrity { get; set; } = !Application.isEditor;

    public static bool HasPendingDeferredSave => hasPendingDeferredSave;
    public static int DiskSaveWriteCountForTesting { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticSaveState()
    {
        cachedProfile = null;
        pendingDeferredProfile = null;
        hasPendingDeferredSave = false;
        DiskSaveWriteCountForTesting = 0;
        UnbindSlideFlushPlayer();
        ForceSlideSaveDeferralForTesting = false;
        SimulateSaveFailureForTesting = false;
        EnforceSaveIntegrity = !Application.isEditor;
    }

    public static void ClearMemoryCacheForTesting()
    {
        cachedProfile = null;
        pendingDeferredProfile = null;
        hasPendingDeferredSave = false;
        DiskSaveWriteCountForTesting = 0;
        UnbindSlideFlushPlayer();
        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = null;
    }

    public static void saveProfile(ProfileData data)
    {
        TrySaveProfile(data);
    }

    /// <summary>
    /// Updates the in-memory profile immediately, and defers the synchronous disk write until
    /// the current high-speed slide stops so pickups/abilities never stall frame pacing mid-corridor.
    /// Falls back to immediate disk persistence when stationary or outside active slides.
    /// </summary>
    public static bool SaveOrDeferDuringSlide(ProfileData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[SaveManager] Cannot save a null ProfileData.");
            return false;
        }

        PlayerMovement activePlayer = PlayerMovement.Instance;
        bool shouldDefer = (Application.isPlaying || ForceSlideSaveDeferralForTesting)
            && !SimulateSaveFailureForTesting
            && activePlayer != null
            && activePlayer.IsMoving;

        if (!shouldDefer)
            return TrySaveProfile(data);

        if (data.saveSchemaVersion < ProfileData.CurrentSaveSchemaVersion)
            data.saveSchemaVersion = ProfileData.CurrentSaveSchemaVersion;

        data.Sanitize();
        cachedProfile = data;
        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = data;

        pendingDeferredProfile = data;
        hasPendingDeferredSave = true;
        EnsureSlideFlushHooks(activePlayer);
        return true;
    }

    /// <summary>Flushes any deferred mid-slide profile write immediately to disk.</summary>
    public static bool FlushPendingSave()
    {
        if (!hasPendingDeferredSave)
            return true;

        ProfileData toSave = pendingDeferredProfile ?? cachedProfile;
        hasPendingDeferredSave = false;
        pendingDeferredProfile = null;
        UnbindSlideFlushPlayer();

        if (toSave == null)
            return true;

        return TrySaveProfile(toSave);
    }

    private static void EnsureSlideFlushHooks(PlayerMovement player)
    {
        if (boundSlideFlushPlayer != player)
        {
            UnbindSlideFlushPlayer();
            if (player != null)
            {
                boundSlideFlushPlayer = player;
                boundSlideFlushPlayer.OnMoveStopped += HandlePlayerStoppedForDeferredSave;
            }
        }

        if (!slideFlushAppHooksRegistered)
        {
            slideFlushAppHooksRegistered = true;
            Application.focusChanged -= HandleApplicationFocusChangedForDeferredSave;
            Application.focusChanged += HandleApplicationFocusChangedForDeferredSave;
            Application.quitting -= HandleApplicationQuittingForDeferredSave;
            Application.quitting += HandleApplicationQuittingForDeferredSave;
        }
    }

    private static void UnbindSlideFlushPlayer()
    {
        if (boundSlideFlushPlayer != null)
        {
            boundSlideFlushPlayer.OnMoveStopped -= HandlePlayerStoppedForDeferredSave;
            boundSlideFlushPlayer = null;
        }
    }

    private static void HandlePlayerStoppedForDeferredSave()
    {
        FlushPendingSave();
    }

    private static void HandleApplicationFocusChangedForDeferredSave(bool hasFocus)
    {
        if (!hasFocus)
            FlushPendingSave();
    }

    private static void HandleApplicationQuittingForDeferredSave()
    {
        FlushPendingSave();
    }

    /// <summary>
    /// Saves the profile to disk atomically (.tmp -> .bak -> profile.json) and updates
    /// the canonical in-memory profile reference.
    /// Returns true if the disk write succeeded, or false if saving failed.
    /// </summary>
    public static bool TrySaveProfile(ProfileData data)
    {
        if (data == null)
        {
            Debug.LogWarning("[SaveManager] Cannot save a null ProfileData.");
            return false;
        }

        if (data.saveSchemaVersion < ProfileData.CurrentSaveSchemaVersion)
            data.saveSchemaVersion = ProfileData.CurrentSaveSchemaVersion;

        data.Sanitize();

        if (SimulateSaveFailureForTesting)
        {
            Debug.LogError("[SaveManager] Simulated save failure for testing.");
            return false;
        }

        try
        {
            string targetPath = ProfileFilePath;
            string tempPath = TempProfileFilePath;
            string backupPath = BackupProfileFilePath;

            string directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            string json = JsonUtility.ToJson(data, !Application.isPlaying);
            if (string.IsNullOrWhiteSpace(json))
            {
                Debug.LogError("[SaveManager] Serialized profile JSON was empty; aborting save.");
                return false;
            }

            File.WriteAllText(tempPath, json);
            SaveIntegrityGuard.WriteSignature(tempPath, json);

            // Preserve previous known-good save in .bak before replacing profile.json (GDD §12.3)
            if (File.Exists(targetPath))
            {
                try
                {
                    FileInfo existingInfo = new FileInfo(targetPath);
                    if (existingInfo.Length > 2)
                    {
                        File.Copy(targetPath, backupPath, overwrite: true);
                        SaveIntegrityGuard.CopySignatureIfExists(targetPath, backupPath);
                    }
                }
                catch (Exception bakEx)
                {
                    Debug.LogWarning($"[SaveManager] Could not refresh backup file '{backupPath}': {bakEx.Message}");
                }

                File.Delete(targetPath);
            }

            File.Move(tempPath, targetPath);
            SaveIntegrityGuard.MoveSignatureIfExists(tempPath, targetPath);

            DiskSaveWriteCountForTesting++;
            hasPendingDeferredSave = false;
            pendingDeferredProfile = null;
            UnbindSlideFlushPlayer();

            cachedProfile = data;
            if (GameSession.Instance != null)
                GameSession.Instance.currentProfile = data;

            return true;
        }
        catch (Exception ex)
        {
            Debug.LogError($"[SaveManager] Failed to save profile to '{ProfileFilePath}': {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Returns the canonical active <see cref="ProfileData"/> instance in memory,
    /// loading from disk only when not yet initialized. This prevents subsystems
    /// from overwriting each other with stale deserialized snapshots.
    /// </summary>
    public static ProfileData loadProfile()
    {
        if (GameSession.Instance != null && GameSession.Instance.currentProfile != null)
        {
            cachedProfile = GameSession.Instance.currentProfile;
            cachedProfile.MigrateIfNeeded();
            return cachedProfile;
        }

        if (cachedProfile != null)
        {
            cachedProfile.MigrateIfNeeded();
            if (GameSession.Instance != null)
                GameSession.Instance.currentProfile = cachedProfile;
            return cachedProfile;
        }

        return ReloadProfileFromDisk();
    }

    /// <summary>
    /// Forces a fresh read of <see cref="ProfileData"/> from disk (with automatic .bak/.tmp
    /// crash recovery before falling back to a new profile) and updates the canonical reference.
    /// </summary>
    public static ProfileData ReloadProfileFromDisk()
    {
        string primaryPath = ProfileFilePath;
        string backupPath = BackupProfileFilePath;
        string tempPath = TempProfileFilePath;

        ProfileData profile = TryDeserializeProfileFile(primaryPath, logWarningOnFailure: true);

        if (profile == null && File.Exists(backupPath))
        {
            Debug.LogWarning($"[SaveManager] Primary save missing or corrupted; attempting recovery from backup '{backupPath}'.");
            profile = TryDeserializeProfileFile(backupPath, logWarningOnFailure: true);
            if (profile != null)
            {
                try
                {
                    File.Copy(backupPath, primaryPath, overwrite: true);
                    SaveIntegrityGuard.CopySignatureIfExists(backupPath, primaryPath);
                    Debug.Log("[SaveManager] Successfully restored primary profile.json from profile.json.bak.");
                }
                catch (Exception restoreEx)
                {
                    Debug.LogWarning($"[SaveManager] Loaded backup profile in memory, but copying to primary path failed: {restoreEx.Message}");
                }
            }
        }

        if (profile == null && File.Exists(tempPath))
        {
            Debug.LogWarning($"[SaveManager] Attempting recovery from interrupted temp save '{tempPath}'.");
            profile = TryDeserializeProfileFile(tempPath, logWarningOnFailure: true);
        }

        if (profile == null)
        {
            profile = new ProfileData
            {
                saveSchemaVersion = ProfileData.CurrentSaveSchemaVersion
            };
        }

        profile.MigrateIfNeeded();

        cachedProfile = profile;
        if (GameSession.Instance != null)
            GameSession.Instance.currentProfile = profile;

        return profile;
    }

    /// <summary>
    /// Safely deserializes a JSON payload (e.g. from Cloud Save) and runs schema migration.
    /// Returns null if the JSON is invalid.
    /// </summary>
    public static ProfileData DeserializeAndMigrateJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        string trimmed = json.Trim();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal) || !trimmed.EndsWith("}", StringComparison.Ordinal))
            return null;

        try
        {
            ProfileData parsed = JsonUtility.FromJson<ProfileData>(trimmed);
            if (parsed == null)
                return null;

            parsed.MigrateIfNeeded();
            return parsed;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[SaveManager] Failed to parse profile JSON payload: {ex.Message}");
            return null;
        }
    }

    private static ProfileData TryDeserializeProfileFile(string path, bool logWarningOnFailure)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            return null;

        try
        {
            string json = File.ReadAllText(path);
            if (EnforceSaveIntegrity && !SaveIntegrityGuard.VerifyFileSignature(path, json))
            {
                if (logWarningOnFailure)
                    Debug.LogWarning($"[SaveManager] Profile file '{path}' failed HMAC integrity verification.");
                return null;
            }

            ProfileData parsed = DeserializeAndMigrateJson(json);
            if (parsed == null && logWarningOnFailure)
            {
                Debug.LogWarning($"[SaveManager] Profile file '{path}' was empty or structurally truncated.");
            }
            return parsed;
        }
        catch (Exception ex)
        {
            if (logWarningOnFailure)
                Debug.LogWarning($"[SaveManager] Failed to read or parse profile at '{path}': {ex.Message}");
            return null;
        }
    }

    /// <summary>Returns true if a persisted profile file (or valid backup) exists on disk.</summary>
    public static bool HasSavedProfile() => File.Exists(ProfileFilePath) || File.Exists(BackupProfileFilePath);

    public static void ResetCampaignProgression()
    {
        ProfileData profile = loadProfile();

        if (profile != null)
        {
            profile.Sanitize();
            profile.unlockedLevelIDs.Clear();
            profile.completedLevelIDs.Clear();
            profile.levelStarKeys.Clear();
            profile.levelStarValues.Clear();
            profile.levelBestMoveKeys.Clear();
            profile.levelBestMoveValues.Clear();
            profile.claimedRewardIds.RemoveAll(id =>
                id != null && (id.StartsWith("level_", StringComparison.Ordinal) ||
                               id.StartsWith("sector_", StringComparison.Ordinal)));
            saveProfile(profile);
            Debug.Log("[SaveManager] Campaign progression has been reset!");
        }
    }

    public static void ResetEconomy()
    {
        ProfileData profile = loadProfile();

        if (profile != null)
        {
            profile.Sanitize();
            profile.coins = 0;
            profile.gems = 0;
            saveProfile(profile);
            if (EconomyManager.Instance != null)
                EconomyManager.Instance.LoadFromProfile();
            Debug.Log("[SaveManager] Economy has been reset!");
        }
    }

    public static void ResetAllMetaAndProgression()
    {
        ResetCampaignProgression();
        ResetEconomy();
        ProfileData profile = loadProfile();

        if (profile != null)
        {
            profile.Sanitize();
            profile.noAdsPurchased = false;
            profile.purchasedProductIDs.Clear();
            profile.purchasedShopItemIDs.Clear();
            profile.claimedRewardIds.Clear();
            profile.processedIapTransactionIds.Clear();
            profile.powerUpIds.Clear();
            profile.powerUpCounts.Clear();
            profile.powerUpLevelIds.Clear();
            profile.powerUpLevels.Clear();
            profile.loadoutSlots.Clear();
            profile.ownedCosmeticIds.Clear();
            profile.equippedPlayerSkinId = string.Empty;

            profile.lastCoinBonusClaimUtcTicks = 0;
            profile.lastDailyRewardClaimUtcTicks = 0;
            profile.dailyRewardStreakDay = 1;
            profile.tutorialCompleted = false;

            profile.hasCompletedFirstLogin = false;
            profile.dailyMissionResetUtcTicks = 0;
            profile.dailyMissionResetDateLocal = string.Empty;
            profile.weeklyMissionResetMondayLocal = string.Empty;
            profile.monthlyMissionResetMonthLocal = string.Empty;
            profile.highWaterLocalTicks = 0;
            profile.dailyLoggedIn = false;
            profile.claimedMissionIds.Clear();
            profile.weeklyMissionStartTicks = 0;
            profile.weeklyMissionPoints = 0;
            profile.loggedInDaysThisWeek.Clear();
            profile.claimedWeeklyGoalIds.Clear();
            profile.claimedDailyMissionIds.Clear();
            profile.claimedWeeklyMissionIds.Clear();
            profile.claimedMonthlyWeeklyMissionIds.Clear();
            profile.claimedSectorMissionIds.Clear();
            profile.claimedNewPlayerMissionIds.Clear();
            profile.claimedAchievementIds.Clear();

            profile.lifetimeCoinsCollected = 0;
            profile.lifetimeStarsCollected = 0;
            profile.lifetimeValidLevelClears = 0;
            profile.lifetimeNoDeathLevelClears = 0;
            profile.lifetimeNoSkillLevelClears = 0;
            profile.lifetimeWallsBroken = 0;
            profile.lifetimePowerUpsUsed = 0;
            profile.usedPowerUpTypeIds.Clear();

            profile.monthlyCoinsCollected = 0;
            profile.monthlyStarsCollected = 0;
            profile.monthlyValidLevelClears = 0;
            profile.monthlyNoDeathLevelClears = 0;
            profile.monthlyWallsBroken = 0;
            profile.monthlyPowerUpsUsed = 0;
            profile.monthlyLoginDays = 0;

            profile.weeklyCoinsCollected = 0;
            profile.weeklyStarsCollected = 0;
            profile.weeklyValidLevelClears = 0;
            profile.weeklyNoDeathLevelClears = 0;
            profile.weeklyWallsBroken = 0;
            profile.weeklyPowerUpsUsed = 0;

            profile.dailyCoinsCollected = 0;
            profile.dailyStarsCollected = 0;
            profile.dailyValidLevelClears = 0;
            profile.dailyNoDeathLevelClears = 0;
            profile.dailyWallsBroken = 0;
            profile.dailyPowerUpsUsed = 0;

            profile.lootPackPityKeys.Clear();
            profile.lootPackPityCounts.Clear();

            profile.dailyAdResetDateLocal = string.Empty;
            profile.dailyRewardedAdCount = 0;
            profile.interstitialCompletedLevelsSinceLastAd = 0;
            profile.interstitialTargetLevelsInterval = 0;
            profile.hasWatchedAnyAdInCampaign = false;
            profile.hasMadeAnyIapInCampaign = false;
            profile.campaignAllLevelsCompletedReported = false;

            profile.MigrateIfNeeded();
            saveProfile(profile);
            Debug.Log("[SaveManager] All meta and progression data has been reset!");
        }
    }

#if UNITY_EDITOR
    [UnityEditor.MenuItem("Game/Reset Campaign Progression")]
    public static void ResetCampaignProgressionEditor()
    {
        ResetCampaignProgression();
    }

    [UnityEditor.MenuItem("Game/Reset Economy")]
    public static void ResetEconomyEditor()
    {
        ResetEconomy();
    }

    [UnityEditor.MenuItem("Game/Reset ALL Meta & Progression")]
    public static void ResetAllMetaAndProgressionEditor()
    {
        ResetAllMetaAndProgression();
    }
#endif
}
