using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central mission, daily quest, weekly quest, sector mission, and achievement service for Gravita (GDD §10 & §11).
///
/// Key guarantees:
/// - Local-time 00:00 Daily Reset & Monday 00:00 Weekly Reset via <see cref="LocalClock"/>.
/// - Anti-clock-rollback guard (<see cref="IsClockRolledBack"/>): rewinding device time never resets
///   already-claimed daily/weekly missions or allows duplicate reward claims (GDD §10.5).
/// - Weekly missions are playable throughout the entire week with zero consecutive-login penalty (GDD §10.2).
/// - Sector-appropriate gating: missions only unlock when <see cref="MissionDefinition.RequiredUnlockedSector"/> is reached.
/// - Unified atomic claiming through <see cref="RewardService.ExecuteTransaction"/> with period-stamped <see cref="EconomyRewardIds"/>.
/// </summary>
public static class MissionService
{
    public const int CurrentMissionSchemaVersion = 2;

    public enum MissionType
    {
        CompleteLevels = 0,
        EarnStars = 1,
        CollectCoins = 2,
        ReachPlayerLevel = 3,
        DailyLogin = 4,
        FirstLogin = 5,
        UsePowerUp = 6,
        UseAllPowerUpTypes = 7,
        BreakWalls = 8,
        CompleteLevelsWithoutDying = 9,
        CompleteLevelsWithoutSkills = 10,
        CompleteSector = 11
    }

    public static event Action OnWeeklyProgressChanged;
    public static event Action OnMissionProgressChanged;

    /// <summary>Analytics & UI notification fired after an atomic mission/achievement claim commits.</summary>
    public static event Action<string, MissionCategory, RewardBundle> OnMissionRewardClaimed;

    /// <summary>Optional 1..7 weekday override for testing in the Editor (0 = use active calendar).</summary>
    public static int DebugOverrideDayIndex { get; set; } = 0;

    /// <summary>Optional 1..4 week-of-month override for testing in the Editor (0 = use active calendar).</summary>
    public static int DebugOverrideWeekOfMonth { get; set; } = 0;

    /// <summary>Optional DateTime override retained for backward compatibility; prefer <see cref="LocalClock.Provider"/>.</summary>
    public static DateTime? DebugUtcNowOverride { get; set; } = null;

    /// <summary>Optional testing flag to simulate a reward grant failure inside ClaimMissionInternal.</summary>
    public static bool SimulateRewardFailureForTesting { get; set; } = false;

    /// <summary>True if the most recent clock check detected a backward system clock rollback beyond tolerance.</summary>
    public static bool IsClockRolledBack { get; private set; }

    private static readonly string[] DayShortNames =
    {
        "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN"
    };

    // ─────────────────────────────────────────────────────────────
    //  Runtime Gameplay Tracking State (Per-Run Buffers)
    // ─────────────────────────────────────────────────────────────

    private static bool eventsSubscribed;
    private static bool claimInProgress;

    private static bool isRunActive;
    private static long activeRunId;
    private static long lastCommittedRunId = -1;
    private static bool currentRunDisqualifiedNoDeath;
    private static bool currentRunUsedSkill;
    private static int pendingRunCoins;
    private static int pendingRunWallsBroken;

    private static readonly HashSet<int> collectedCoinIdsThisRun = new HashSet<int>();
    private static readonly HashSet<int> collectedStarIdsThisRun = new HashSet<int>();
    private static readonly HashSet<int> brokenWallIdsThisRun = new HashSet<int>();
    private static readonly HashSet<Collectible> collectedCoinObjectsThisRun = new HashSet<Collectible>();
    private static readonly HashSet<Collectible> collectedStarObjectsThisRun = new HashSet<Collectible>();
    private static readonly HashSet<CrackedBlock> brokenWallObjectsThisRun = new HashSet<CrackedBlock>();

    public static int PendingRunCoins => pendingRunCoins;
    public static int PendingRunWallsBroken => pendingRunWallsBroken;
    public static bool IsCurrentRunNoDeathEligible => isRunActive && !currentRunDisqualifiedNoDeath;

    // ─────────────────────────────────────────────────────────────
    //  Automatic Session Bootstrap & Event Subscription
    // ─────────────────────────────────────────────────────────────

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureSessionInitialized()
    {
        SubscribeGameplayEvents();
        EnsureProfileMigratedAndUpdated();
    }

    public static void SubscribeGameplayEvents()
    {
        if (eventsSubscribed) return;

        LevelFlowController.OnAnyRunStartedOrReset += HandleRunStartedOrReset;
        LevelFlowController.OnAnyHazardContacted += HandleHazardContacted;
        LevelFlowController.OnAnyRunFailedOrAbandoned += HandleRunFailedOrAbandoned;
        LevelFlowController.OnAnyRunCompletedValidly += HandleRunCompletedValidly;

        Collectible.OnAnyCoinCollected += HandleCoinCollected;
        Collectible.OnAnyStarCollected += HandleStarCollected;
        CrackedBlock.OnAnyBroken += HandleWallBroken;
        PowerUpController.OnAnyPowerUpUsed += HandlePowerUpUsed;

        eventsSubscribed = true;
    }

    public static void UnsubscribeGameplayEvents()
    {
        if (!eventsSubscribed) return;

        LevelFlowController.OnAnyRunStartedOrReset -= HandleRunStartedOrReset;
        LevelFlowController.OnAnyHazardContacted -= HandleHazardContacted;
        LevelFlowController.OnAnyRunFailedOrAbandoned -= HandleRunFailedOrAbandoned;
        LevelFlowController.OnAnyRunCompletedValidly -= HandleRunCompletedValidly;

        Collectible.OnAnyCoinCollected -= HandleCoinCollected;
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        CrackedBlock.OnAnyBroken -= HandleWallBroken;
        PowerUpController.OnAnyPowerUpUsed -= HandlePowerUpUsed;

        eventsSubscribed = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Local Calendar, Daily 00:00 Reset, Monday 00:00 Reset & Anti-Rollback
    // ─────────────────────────────────────────────────────────────

    public static DateTime GetLocalNow()
    {
        if (DebugUtcNowOverride.HasValue)
            return DebugUtcNowOverride.Value;

        return LocalClock.Now;
    }

    public static DateTime GetUtcNow()
    {
        return DebugUtcNowOverride ?? DateTime.UtcNow;
    }

    /// <summary>
    /// Maps the current local weekday to 1..7 (Monday = 1, Tuesday = 2, ..., Sunday = 7).
    /// </summary>
    public static int GetCurrentDayOfWeekIndex()
    {
        if (DebugOverrideDayIndex >= 1 && DebugOverrideDayIndex <= 7)
            return DebugOverrideDayIndex;

        return DayOfWeekToIndex(GetLocalNow().DayOfWeek);
    }

    public static int DayOfWeekToIndex(DayOfWeek dayOfWeek)
    {
        return LocalClock.DayOfWeekToOneBased(dayOfWeek);
    }

    public static string GetDayShortName(int dayIndex)
    {
        int clamped = Mathf.Clamp(dayIndex, 1, 7);
        return DayShortNames[clamped - 1];
    }

    /// <summary>
    /// Per GDD §10.2, all Weekly missions within the active week are unlocked without requiring
    /// sequential daily logins or waiting for later weekdays.
    /// </summary>
    public static bool IsDayUnlocked(int dayIndex)
    {
        if (dayIndex < 1 || dayIndex > 7)
            return false;

        if (DebugOverrideDayIndex >= 1 && DebugOverrideDayIndex <= 7)
            return dayIndex <= DebugOverrideDayIndex;

        return true;
    }

    public static TimeSpan GetTimeRemainingUntilLocalMidnight()
    {
        DateTime now = GetLocalNow();
        DateTime nextMidnight = now.Date.AddDays(1);
        TimeSpan diff = nextMidnight - now;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    public static TimeSpan GetTimeRemainingUntilNextMondayMidnight()
    {
        DateTime now = GetLocalNow();
        DateTime nextMonday = LocalClock.GetMondayOfWeek(now).AddDays(7);
        TimeSpan diff = nextMonday - now;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    /// <summary>
    /// Maps the current local day of month to week 1..4 (days 1-7 = 1, 8-14 = 2, 15-21 = 3, 22-31 = 4).
    /// </summary>
    public static int GetCurrentWeekOfMonth()
    {
        if (DebugOverrideWeekOfMonth >= 1 && DebugOverrideWeekOfMonth <= 4)
            return DebugOverrideWeekOfMonth;

        int dayOfMonth = GetLocalNow().Day;
        return Mathf.Clamp(((dayOfMonth - 1) / 7) + 1, 1, 4);
    }

    public static TimeSpan GetTimeRemainingUntilNextMonthMidnight()
    {
        DateTime now = GetLocalNow();
        DateTime firstOfNextMonth = new DateTime(now.Year, now.Month, 1, 0, 0, 0, now.Kind).AddMonths(1);
        TimeSpan diff = firstOfNextMonth - now;
        return diff > TimeSpan.Zero ? diff : TimeSpan.Zero;
    }

    /// <summary>
    /// Ensures migration, evaluates anti-clock-rollback guard, performs monotonic local 00:00 daily,
    /// Monday 00:00 weekly, and 1st-of-month 00:00 monthly resets, and records today's login.
    /// </summary>
    public static ProfileData EnsureProfileMigratedAndUpdated()
    {
        SubscribeGameplayEvents();

        ProfileData profile = GetActiveProfile() ?? new ProfileData();
        profile.MigrateIfNeeded();

        bool dirty = false;

        if (MigrateLegacyMissionStateIfNeeded(profile))
            dirty = true;

        if (!profile.hasCompletedFirstLogin)
        {
            profile.hasCompletedFirstLogin = true;
            dirty = true;
        }

        // Anti-clock-rollback evaluation (GDD §10.5)
        DateTime rawLocalNow = GetLocalNow();
        if (LocalClock.EvaluateClockAndUpdateHighWater(profile, rawLocalNow, out DateTime effectiveNow, out bool rolledBack))
        {
            dirty = true;
        }
        IsClockRolledBack = rolledBack;

        // Monotonic Local 00:00 Daily Login Tracking
        string todayLocalKey = LocalClock.FormatDateKey(effectiveNow);
        if (string.IsNullOrEmpty(profile.dailyMissionResetDateLocal))
        {
            profile.dailyMissionResetDateLocal = todayLocalKey;
            profile.dailyMissionResetUtcTicks = effectiveNow.Date.Ticks;
            profile.dailyLoggedIn = true;
            if (profile.monthlyLoginDays <= 0)
                profile.monthlyLoginDays = 1;
            dirty = true;
        }
        else if (!rolledBack && string.CompareOrdinal(todayLocalKey, profile.dailyMissionResetDateLocal) > 0)
        {
            profile.dailyMissionResetDateLocal = todayLocalKey;
            profile.dailyMissionResetUtcTicks = effectiveNow.Date.Ticks;
            profile.dailyLoggedIn = true;
            profile.monthlyLoginDays++;
            dirty = true;
        }
        else if (!profile.dailyLoggedIn && !rolledBack)
        {
            profile.dailyLoggedIn = true;
            dirty = true;
        }

        // Monotonic Local Monday 00:00 Weekly Reset (resets Daily & Weekday missions + weekly milestones)
        string currentMondayLocalKey = LocalClock.FormatMondayKey(effectiveNow);
        if (string.IsNullOrEmpty(profile.weeklyMissionResetMondayLocal))
        {
            profile.weeklyMissionResetMondayLocal = currentMondayLocalKey;
            profile.weeklyMissionStartTicks = LocalClock.GetMondayOfWeek(effectiveNow).Ticks;
            dirty = true;
        }
        else if (!rolledBack && string.CompareOrdinal(currentMondayLocalKey, profile.weeklyMissionResetMondayLocal) > 0)
        {
            profile.weeklyMissionResetMondayLocal = currentMondayLocalKey;
            profile.weeklyMissionStartTicks = LocalClock.GetMondayOfWeek(effectiveNow).Ticks;
            profile.weeklyMissionPoints = 0;
            profile.loggedInDaysThisWeek.Clear();
            profile.claimedWeeklyGoalIds.Clear();
            profile.claimedMissionIds.Clear();
            profile.claimedWeeklyMissionIds.Clear();
            profile.claimedDailyMissionIds.Clear();

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

            profile.PruneExpiredPeriodicRewardIds(effectiveNow, 40);
            dirty = true;
        }

        // Monotonic Local 1st-of-Month 00:00 Monthly Reset (resets cumulative WEEKLY tab missions)
        string currentMonthLocalKey = effectiveNow.ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
        if (string.IsNullOrEmpty(profile.monthlyMissionResetMonthLocal))
        {
            profile.monthlyMissionResetMonthLocal = currentMonthLocalKey;
            if (profile.monthlyLoginDays <= 0)
                profile.monthlyLoginDays = 1;
            dirty = true;
        }
        else if (!rolledBack && string.CompareOrdinal(currentMonthLocalKey, profile.monthlyMissionResetMonthLocal) > 0)
        {
            profile.monthlyMissionResetMonthLocal = currentMonthLocalKey;
            profile.claimedMonthlyWeeklyMissionIds.Clear();

            profile.monthlyCoinsCollected = 0;
            profile.monthlyStarsCollected = 0;
            profile.monthlyValidLevelClears = 0;
            profile.monthlyNoDeathLevelClears = 0;
            profile.monthlyWallsBroken = 0;
            profile.monthlyPowerUpsUsed = 0;
            profile.monthlyLoginDays = 1;
            dirty = true;
        }

        int todayIndex = DayOfWeekToIndex(effectiveNow.DayOfWeek);
        if (!profile.loggedInDaysThisWeek.Contains(todayIndex))
        {
            profile.loggedInDaysThisWeek.Add(todayIndex);
            dirty = true;
        }

        if (dirty)
        {
            SaveProfileState(profile);
        }

        return profile;
    }

    public static ProfileData EnsureWeeklyStateAndRecordLogin()
    {
        return EnsureProfileMigratedAndUpdated();
    }

    private static bool MigrateLegacyMissionStateIfNeeded(ProfileData profile)
    {
        if (profile == null || profile.missionSchemaVersion >= CurrentMissionSchemaVersion)
            return false;

        profile.Sanitize();

        for (int i = profile.claimedMissionIds.Count - 1; i >= 0; i--)
        {
            string id = profile.claimedMissionIds[i];
            if (string.IsNullOrEmpty(id))
            {
                profile.claimedMissionIds.RemoveAt(i);
                continue;
            }

            if (id.StartsWith("new_", StringComparison.OrdinalIgnoreCase))
            {
                if (!profile.claimedNewPlayerMissionIds.Contains(id))
                    profile.claimedNewPlayerMissionIds.Add(id);
                profile.claimedMissionIds.RemoveAt(i);
            }
            else if (id.StartsWith("daily_", StringComparison.OrdinalIgnoreCase))
            {
                if (!profile.claimedDailyMissionIds.Contains(id))
                    profile.claimedDailyMissionIds.Add(id);
                profile.claimedMissionIds.RemoveAt(i);
            }
            else if (id.StartsWith("ach_", StringComparison.OrdinalIgnoreCase))
            {
                if (!profile.claimedAchievementIds.Contains(id))
                    profile.claimedAchievementIds.Add(id);
                profile.claimedMissionIds.RemoveAt(i);
            }
        }

        int completedCampaignCount = profile.completedLevelIDs != null ? profile.completedLevelIDs.Count : 0;
        if (completedCampaignCount > profile.lifetimeValidLevelClears)
            profile.lifetimeValidLevelClears = completedCampaignCount;

        int savedCampaignStars = 0;
        if (profile.levelStarValues != null)
        {
            for (int i = 0; i < profile.levelStarValues.Count; i++)
                savedCampaignStars += Mathf.Clamp(profile.levelStarValues[i], 0, 3);
        }
        if (savedCampaignStars > profile.lifetimeStarsCollected)
            profile.lifetimeStarsCollected = savedCampaignStars;

        profile.hasCompletedFirstLogin = true;
        profile.missionSchemaVersion = CurrentMissionSchemaVersion;
        return true;
    }

    public static bool HasLoggedInOnDay(int dayIndex)
    {
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        return profile != null
               && profile.loggedInDaysThisWeek != null
               && profile.loggedInDaysThisWeek.Contains(Mathf.Clamp(dayIndex, 1, 7));
    }

    public static int GetWeeklyPoints()
    {
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        return profile != null ? Mathf.Max(0, profile.weeklyMissionPoints) : 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Gameplay Event Handlers (Pending & Immediate Progress)
    // ─────────────────────────────────────────────────────────────

    private static void HandleRunStartedOrReset(LevelFlowController controller, long runId)
    {
        isRunActive = true;
        activeRunId = runId;
        currentRunDisqualifiedNoDeath = false;
        currentRunUsedSkill = false;
        DiscardPendingRunProgress(clearStarRunSet: true);
    }

    private static void HandleHazardContacted(LevelFlowController controller, Hazard hazard)
    {
        if (!isRunActive) return;
        currentRunDisqualifiedNoDeath = true;
    }

    private static void HandleRunFailedOrAbandoned(LevelFlowController controller, long runId)
    {
        currentRunDisqualifiedNoDeath = true;
        DiscardPendingRunProgress(clearStarRunSet: false);
    }

    private static void HandleCoinCollected(Collectible collectible)
    {
        if (collectible == null || !collectible.IsCoin) return;

        EnsureRunActiveFromSceneIfNeeded();
        if (!isRunActive) return;

        if (!collectedCoinObjectsThisRun.Add(collectible))
            return;

        pendingRunCoins += Mathf.Max(1, collectible.Value);
    }

    private static void HandleWallBroken(CrackedBlock block)
    {
        if (block == null) return;

        EnsureRunActiveFromSceneIfNeeded();
        if (!isRunActive) return;

        if (!brokenWallObjectsThisRun.Add(block))
            return;

        pendingRunWallsBroken++;
    }

    private static void HandleStarCollected(Collectible collectible)
    {
        if (collectible == null || !collectible.IsStar) return;

        if (isRunActive && !collectedStarObjectsThisRun.Add(collectible))
            return;

        int amount = Mathf.Max(1, collectible.Value);
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        profile.lifetimeStarsCollected += amount;
        profile.monthlyStarsCollected += amount;
        profile.weeklyStarsCollected += amount;
        profile.dailyStarsCollected += amount;

        SaveProfileState(profile);
        NotifyProgressChanged();
    }

    private static void HandlePowerUpUsed(PowerUpType type)
    {
        currentRunUsedSkill = true;

        ProfileData profile = EnsureProfileMigratedAndUpdated();
        profile.lifetimePowerUpsUsed++;
        profile.monthlyPowerUpsUsed++;
        profile.weeklyPowerUpsUsed++;
        profile.dailyPowerUpsUsed++;

        string typeKey = type.ToString();
        if (!profile.usedPowerUpTypeIds.Contains(typeKey))
        {
            profile.usedPowerUpTypeIds.Add(typeKey);
        }

        SaveProfileState(profile);
        NotifyProgressChanged();
    }

    private static void HandleRunCompletedValidly(
        LevelFlowController controller,
        long runId,
        bool controllerReportedNoHazardContact)
    {
        if (runId > 0 && runId == lastCommittedRunId)
            return;

        if (runId > 0)
            lastCommittedRunId = runId;

        bool noDeathEligible = !currentRunDisqualifiedNoDeath && controllerReportedNoHazardContact;
        bool noSkillUsed = !currentRunUsedSkill && (controller == null || !controller.UsedAnySkillThisRun);
        int committedCoins = Mathf.Max(0, pendingRunCoins);
        int committedWalls = Mathf.Max(0, pendingRunWallsBroken);

        isRunActive = false;
        currentRunUsedSkill = false;
        DiscardPendingRunProgress(clearStarRunSet: true);

        ProfileData profile = EnsureProfileMigratedAndUpdated();

        profile.lifetimeValidLevelClears++;
        profile.monthlyValidLevelClears++;
        profile.weeklyValidLevelClears++;
        profile.dailyValidLevelClears++;

        if (noDeathEligible)
        {
            profile.lifetimeNoDeathLevelClears++;
            profile.monthlyNoDeathLevelClears++;
            profile.weeklyNoDeathLevelClears++;
            profile.dailyNoDeathLevelClears++;
        }

        if (noSkillUsed && controller == null)
        {
            // When driven directly in headless tests without LevelFlowController/CampaignEconomyService
            profile.lifetimeNoSkillLevelClears++;
        }

        if (committedCoins > 0)
        {
            profile.lifetimeCoinsCollected += committedCoins;
            profile.monthlyCoinsCollected += committedCoins;
            profile.weeklyCoinsCollected += committedCoins;
            profile.dailyCoinsCollected += committedCoins;
        }

        if (committedWalls > 0)
        {
            profile.lifetimeWallsBroken += committedWalls;
            profile.monthlyWallsBroken += committedWalls;
            profile.weeklyWallsBroken += committedWalls;
            profile.dailyWallsBroken += committedWalls;
        }

        SaveProfileState(profile);
        NotifyProgressChanged();
    }

    private static void EnsureRunActiveFromSceneIfNeeded()
    {
        if (isRunActive) return;

        LevelFlowController flow = UnityEngine.Object.FindAnyObjectByType<LevelFlowController>();
        if (flow != null && flow.CurrentState == LevelState.Playing)
        {
            isRunActive = true;
            activeRunId = flow.CurrentRunId;
            currentRunDisqualifiedNoDeath = flow.HasTouchedHazardThisRun;
        }
    }

    private static void DiscardPendingRunProgress(bool clearStarRunSet)
    {
        pendingRunCoins = 0;
        pendingRunWallsBroken = 0;
        collectedCoinIdsThisRun.Clear();
        brokenWallIdsThisRun.Clear();
        collectedCoinObjectsThisRun.Clear();
        brokenWallObjectsThisRun.Clear();
        if (clearStarRunSet)
        {
            collectedStarIdsThisRun.Clear();
            collectedStarObjectsThisRun.Clear();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Explicit Recording Helpers (Used by Gameplay & Tests)
    // ─────────────────────────────────────────────────────────────

    public static void BeginLevelRun(long runId = 0)
    {
        long effectiveRunId = runId > 0 ? runId : ++activeRunId;
        HandleRunStartedOrReset(null, effectiveRunId);
    }

    public static void RecordPendingCoinPickup(int coinValue = 1, int uniquePickupInstanceId = 0)
    {
        if (!isRunActive) return;
        if (uniquePickupInstanceId != 0 && !collectedCoinIdsThisRun.Add(uniquePickupInstanceId))
            return;

        pendingRunCoins += Mathf.Max(1, coinValue);
    }

    public static void RecordPendingWallBroken(int uniqueWallInstanceId = 0)
    {
        if (!isRunActive) return;
        if (uniqueWallInstanceId != 0 && !brokenWallIdsThisRun.Add(uniqueWallInstanceId))
            return;

        pendingRunWallsBroken++;
    }

    public static void RecordStarPickupImmediate(int starCount = 1, int uniqueStarInstanceId = 0)
    {
        if (isRunActive && uniqueStarInstanceId != 0 && !collectedStarIdsThisRun.Add(uniqueStarInstanceId))
            return;

        int amount = Mathf.Max(1, starCount);
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        profile.lifetimeStarsCollected += amount;
        profile.monthlyStarsCollected += amount;
        profile.weeklyStarsCollected += amount;
        profile.dailyStarsCollected += amount;

        SaveProfileState(profile);
        NotifyProgressChanged();
    }

    public static void RecordHazardContactInCurrentRun()
    {
        if (!isRunActive) return;
        currentRunDisqualifiedNoDeath = true;
    }

    public static void AbortCurrentLevelRun()
    {
        HandleRunFailedOrAbandoned(null, activeRunId);
    }

    public static void CommitCurrentLevelRunCompletion()
    {
        if (!isRunActive) return;
        HandleRunCompletedValidly(null, activeRunId, !currentRunDisqualifiedNoDeath);
    }

    public static void RecordPowerUpActivated(PowerUpType type)
    {
        HandlePowerUpUsed(type);
    }

    // ─────────────────────────────────────────────────────────────
    //  Objective Progress Evaluation (By Category & Type)
    // ─────────────────────────────────────────────────────────────

    private static int GetHighestUnlockedSector(ProfileData profile)
    {
        if (ProgressionManager.Instance != null)
            return ProgressionManager.Instance.GetHighestUnlockedSectorNumber();

        if (profile?.completedLevelIDs != null)
        {
            if (profile.completedLevelIDs.Count >= 20)
                return 3;
            if (profile.completedLevelIDs.Count >= 10)
                return 2;
        }
        return 1;
    }

    private static int CountCompletedSectors(ProfileData profile)
    {
        int count = 0;
        for (int s = 1; s <= 3; s++)
        {
            if (ProgressionManager.Instance != null && ProgressionManager.Instance.IsSectorCompleted(s))
                count++;
            else if (profile != null && profile.HasClaimedReward(EconomyRewardIds.SectorCompletion(s)))
                count++;
        }
        return count;
    }

    public static int GetProgress(MissionType type, int dayIndex = 1, MissionCategory category = MissionCategory.Weekly)
    {
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (profile == null) return 0;

        bool isDaily = category == MissionCategory.Daily;
        bool isWeekly = category == MissionCategory.Weekly;

        switch (type)
        {
            case MissionType.FirstLogin:
                return profile.hasCompletedFirstLogin ? 1 : 0;

            case MissionType.DailyLogin:
                if (isDaily)
                    return profile.dailyLoggedIn ? 1 : 0;
                if (isWeekly)
                    return profile.loggedInDaysThisWeek != null
                           && profile.loggedInDaysThisWeek.Contains(Mathf.Clamp(dayIndex, 1, 7)) ? 1 : 0;
                return profile.hasCompletedFirstLogin ? 1 : 0;

            case MissionType.CompleteLevels:
                if (isDaily)
                    return profile.dailyValidLevelClears;
                if (isWeekly)
                    return profile.weeklyValidLevelClears;
                int campaignCount = ProgressionManager.Instance != null
                    ? ProgressionManager.Instance.GetCompletedLevelCount()
                    : (profile.completedLevelIDs != null ? profile.completedLevelIDs.Count : 0);
                return Mathf.Max(profile.lifetimeValidLevelClears, campaignCount);

            case MissionType.EarnStars:
                if (isDaily)
                    return profile.dailyStarsCollected;
                if (isWeekly)
                    return profile.weeklyStarsCollected;
                int campaignStars = ProgressionManager.Instance != null
                    ? ProgressionManager.Instance.TotalStars
                    : 0;
                return Mathf.Max(profile.lifetimeStarsCollected, campaignStars);

            case MissionType.CollectCoins:
                if (isDaily)
                    return profile.dailyCoinsCollected;
                if (isWeekly)
                    return profile.weeklyCoinsCollected;
                return profile.lifetimeCoinsCollected;

            case MissionType.ReachPlayerLevel:
                return ProgressionManager.Instance != null
                    ? ProgressionManager.Instance.PlayerLevel
                    : Mathf.Max(1, profile.playerLevel);

            case MissionType.UsePowerUp:
                if (isDaily)
                    return profile.dailyPowerUpsUsed;
                if (isWeekly)
                    return profile.weeklyPowerUpsUsed;
                return profile.lifetimePowerUpsUsed;

            case MissionType.UseAllPowerUpTypes:
                return CountDistinctActiveCampaignSkillsUsed(profile);

            case MissionType.BreakWalls:
                if (isDaily)
                    return profile.dailyWallsBroken;
                if (isWeekly)
                    return profile.weeklyWallsBroken;
                return profile.lifetimeWallsBroken;

            case MissionType.CompleteLevelsWithoutDying:
                if (isDaily)
                    return profile.dailyNoDeathLevelClears;
                if (isWeekly)
                    return profile.weeklyNoDeathLevelClears;
                return profile.lifetimeNoDeathLevelClears;

            case MissionType.CompleteLevelsWithoutSkills:
                return profile.lifetimeNoSkillLevelClears;

            case MissionType.CompleteSector:
                return CountCompletedSectors(profile);

            default:
                return 0;
        }
    }

    private static int CountDistinctActiveCampaignSkillsUsed(ProfileData profile)
    {
        if (profile == null || profile.usedPowerUpTypeIds == null || profile.usedPowerUpTypeIds.Count == 0)
            return 0;

        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        IReadOnlyList<PowerUpType> supported = PowerUpController.GetSupportedPowerUpTypes();
        int count = 0;
        for (int i = 0; i < supported.Count; i++)
        {
            if (!balance.IsSkillActiveInCampaign(supported[i]))
                continue;

            string key = supported[i].ToString();
            if (profile.usedPowerUpTypeIds.Contains(key))
                count++;
        }
        return count;
    }

    // ─────────────────────────────────────────────────────────────
    //  ScriptableObject MissionDefinition API
    // ─────────────────────────────────────────────────────────────

    public static bool IsMissionEligibleForCurrentProgression(MissionDefinition mission)
    {
        if (mission == null)
            return false;

        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (mission.RequiredUnlockedSector > GetHighestUnlockedSector(profile))
            return false;

        if (mission.RewardType == MissionRewardType.PowerUp &&
            !EconomyBalanceConfig.Active.IsSkillActiveInCampaign(mission.RewardPowerUpId))
        {
            return false;
        }

        return true;
    }

    public static bool IsMissionUnlocked(MissionDefinition mission)
    {
        if (!IsMissionEligibleForCurrentProgression(mission))
            return false;

        if (mission.IsMonthlyCumulativeWeekly)
            return mission.WeekOfMonth <= GetCurrentWeekOfMonth();

        if (mission.Category == MissionCategory.Weekly)
            return IsDayUnlocked(mission.DayIndex);

        return true;
    }

    public static int GetMonthlyProgress(ProfileData profile, MissionType type)
    {
        if (profile == null) return 0;

        return type switch
        {
            MissionType.FirstLogin                   => profile.hasCompletedFirstLogin ? 1 : 0,
            MissionType.DailyLogin                   => Mathf.Max(1, profile.monthlyLoginDays),
            MissionType.CompleteLevels               => profile.monthlyValidLevelClears,
            MissionType.EarnStars                    => profile.monthlyStarsCollected,
            MissionType.CollectCoins                 => profile.monthlyCoinsCollected,
            MissionType.ReachPlayerLevel             => ProgressionManager.Instance != null ? ProgressionManager.Instance.PlayerLevel : Mathf.Max(1, profile.playerLevel),
            MissionType.UsePowerUp                   => profile.monthlyPowerUpsUsed,
            MissionType.UseAllPowerUpTypes           => CountDistinctActiveCampaignSkillsUsed(profile),
            MissionType.BreakWalls                   => profile.monthlyWallsBroken,
            MissionType.CompleteLevelsWithoutDying   => profile.monthlyNoDeathLevelClears,
            MissionType.CompleteLevelsWithoutSkills  => profile.lifetimeNoSkillLevelClears,
            MissionType.CompleteSector               => CountCompletedSectors(profile),
            _                                        => 0
        };
    }

    public static int GetProgress(MissionDefinition mission)
    {
        if (mission == null) return 0;
        if (!IsMissionUnlocked(mission)) return 0;

        int rawProgress;
        if (mission.IsMonthlyCumulativeWeekly)
        {
            ProfileData profile = EnsureProfileMigratedAndUpdated();
            rawProgress = GetMonthlyProgress(profile, mission.Type);
        }
        else
        {
            rawProgress = GetProgress(mission.Type, mission.DayIndex, mission.Category);
        }

        if (IsClaimed(mission))
        {
            return Mathf.Max(rawProgress, mission.Target);
        }

        return rawProgress;
    }

    public static bool IsClaimed(MissionDefinition mission)
    {
        if (mission == null || string.IsNullOrEmpty(mission.MissionId))
            return false;

        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (profile == null)
            return false;

        string rewardId = BuildCanonicalMissionRewardId(profile, mission.Category, mission.MissionId, mission.IsMonthlyCumulativeWeekly);
        if (!string.IsNullOrEmpty(rewardId) && profile.HasClaimedReward(rewardId))
            return true;

        List<string> claimList = GetClaimListForCategory(profile, mission.Category, mission.IsMonthlyCumulativeWeekly);
        return claimList != null && claimList.Contains(mission.MissionId);
    }

    public static bool IsComplete(MissionDefinition mission)
    {
        if (mission == null) return false;
        if (!IsMissionUnlocked(mission)) return false;

        if (IsClaimed(mission))
            return true;

        return GetProgress(mission) >= mission.Target;
    }

    public static MissionStatus GetStatus(MissionDefinition mission)
    {
        if (mission == null) return MissionStatus.Incomplete;
        return IsComplete(mission) ? MissionStatus.Done : MissionStatus.Incomplete;
    }

    /// <summary>
    /// Claims the mission reward atomically via <see cref="RewardService.ExecuteTransaction"/>.
    /// Blocked during system clock rollback for periodic Daily/Weekly missions (GDD §10.5).
    /// </summary>
    public static bool TryClaim(MissionDefinition mission)
    {
        if (mission == null || string.IsNullOrEmpty(mission.MissionId)) return false;
        if (!IsMissionUnlocked(mission)) return false;
        if (IsClaimed(mission) || !IsComplete(mission)) return false;

        int pointsToGrant = mission.Category == MissionCategory.Weekly ? mission.MissionPoints : 0;

        return ClaimMissionInternal(
            mission.MissionId,
            mission.Category,
            pointsToGrant,
            mission.RewardType,
            mission.RewardAmount,
            mission.RewardPowerUpId,
            mission.RewardCosmeticId,
            mission.IsMonthlyCumulativeWeekly);
    }

    // ─────────────────────────────────────────────────────────────
    //  Weekly Milestone ScriptableObject API (Concrete Task Counts)
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns the concrete number of Weekly missions completed and claimed in the active week.
    /// Each claimed weekly mission increments this count by 1 toward weekly milestones.
    /// </summary>
    public static int GetWeeklyCompletedMissionCount(MissionDatabase database = null)
    {
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (profile == null)
            return 0;

        if (database != null && database.Missions != null && database.Missions.Count > 0)
        {
            var countedIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < database.Missions.Count; i++)
            {
                MissionDefinition def = database.Missions[i];
                if (def == null || (def.Category != MissionCategory.Weekly && def.Category != MissionCategory.Daily) || string.IsNullOrEmpty(def.MissionId))
                    continue;

                if (def.IsMonthlyCumulativeWeekly)
                {
                    if (profile.claimedWeeklyMissionIds != null && profile.claimedWeeklyMissionIds.Contains(def.MissionId))
                        countedIds.Add(def.MissionId);
                }
                else if (IsClaimed(def))
                {
                    countedIds.Add(def.MissionId);
                }
            }
            return countedIds.Count;
        }

        if (profile.claimedWeeklyMissionIds != null && profile.claimedWeeklyMissionIds.Count > 0)
            return profile.claimedWeeklyMissionIds.Count;

        return profile.claimedMissionIds != null ? profile.claimedMissionIds.Count : 0;
    }

    public static bool IsMilestoneComplete(MissionMilestoneDefinition milestone, MissionDatabase database = null)
    {
        if (milestone == null)
            return false;

        return GetWeeklyCompletedMissionCount(database) >= milestone.RequiredCompletedMissions;
    }

    public static bool IsMilestoneClaimed(MissionMilestoneDefinition milestone)
    {
        if (milestone == null || string.IsNullOrEmpty(milestone.MilestoneId))
            return false;

        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (profile == null)
            return false;

        string rewardId = EconomyRewardIds.WeeklyGoal(profile.weeklyMissionResetMondayLocal, milestone.MilestoneId);
        if (profile.HasClaimedReward(rewardId))
            return true;

        return profile.claimedWeeklyGoalIds != null && profile.claimedWeeklyGoalIds.Contains(milestone.MilestoneId);
    }

    public static bool CanClaimMilestone(MissionMilestoneDefinition milestone, MissionDatabase database = null)
    {
        return milestone != null
               && !IsClockRolledBack
               && IsMilestoneComplete(milestone, database)
               && !IsMilestoneClaimed(milestone);
    }

    public static bool TryClaimMilestone(MissionMilestoneDefinition milestone, MissionDatabase database = null)
    {
        if (claimInProgress || SimulateRewardFailureForTesting)
            return false;
        if (!CanClaimMilestone(milestone, database))
            return false;

        claimInProgress = true;
        try
        {
            ProfileData profile = EnsureProfileMigratedAndUpdated();
            string id = milestone.MilestoneId;
            if (IsClockRolledBack || profile.claimedWeeklyGoalIds.Contains(id))
                return false;

            RewardBundle payout = BuildMissionRewardBundle(
                milestone.RewardType,
                milestone.RewardAmount,
                milestone.PowerUpId,
                milestone.CosmeticId);
            string rewardId = EconomyRewardIds.WeeklyGoal(profile.weeklyMissionResetMondayLocal, id);

            var tx = new RewardTransaction
            {
                RewardId = rewardId,
                RequireUniqueRewardId = true,
                Payout = payout,
                RejectIfSkillCapExceeded = milestone.RewardType == MissionRewardType.PowerUp,
                RejectIfCosmeticAlreadyOwned = false,
                Reason = $"WeeklyMilestone_{id}",
                OnBeforeSaveMutateProfile = p =>
                {
                    if (!p.claimedWeeklyGoalIds.Contains(id))
                        p.claimedWeeklyGoalIds.Add(id);
                }
            };

            RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
            if (!result.Succeeded)
                return false;

            OnMissionRewardClaimed?.Invoke(id, MissionCategory.Weekly, payout);
            NotifyProgressChanged();
            return true;
        }
        finally
        {
            claimInProgress = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Legacy Weekly Goal Reward Milestones API
    // ─────────────────────────────────────────────────────────────

    public static bool IsGoalComplete(WeeklyGoalReward goal)
    {
        if (goal == null) return false;
        return GetWeeklyPoints() >= Mathf.Max(1, goal.requiredPoints);
    }

    public static bool IsGoalClaimed(WeeklyGoalReward goal)
    {
        if (goal == null || string.IsNullOrEmpty(goal.goalId)) return false;
        ProfileData profile = EnsureProfileMigratedAndUpdated();
        if (profile == null) return false;

        string goalRewardId = EconomyRewardIds.WeeklyGoal(profile.weeklyMissionResetMondayLocal, goal.goalId);
        if (profile.HasClaimedReward(goalRewardId))
            return true;

        return profile.claimedWeeklyGoalIds != null && profile.claimedWeeklyGoalIds.Contains(goal.goalId);
    }

    public static bool CanClaimGoal(WeeklyGoalReward goal)
    {
        return goal != null && !IsClockRolledBack && IsGoalComplete(goal) && !IsGoalClaimed(goal);
    }

    public static bool TryClaimGoal(WeeklyGoalReward goal)
    {
        if (claimInProgress || SimulateRewardFailureForTesting) return false;
        if (!CanClaimGoal(goal)) return false;

        claimInProgress = true;
        try
        {
            ProfileData profile = EnsureProfileMigratedAndUpdated();
            if (IsClockRolledBack || profile.claimedWeeklyGoalIds.Contains(goal.goalId))
                return false;

            RewardBundle payout = BuildMissionRewardBundle(goal.rewardType, goal.rewardAmount, goal.powerUpId, goal.cosmeticId);
            string rewardId = EconomyRewardIds.WeeklyGoal(profile.weeklyMissionResetMondayLocal, goal.goalId);

            var tx = new RewardTransaction
            {
                RewardId = rewardId,
                RequireUniqueRewardId = true,
                Payout = payout,
                RejectIfSkillCapExceeded = goal.rewardType == MissionRewardType.PowerUp,
                RejectIfCosmeticAlreadyOwned = false,
                Reason = $"WeeklyGoal_{goal.goalId}",
                OnBeforeSaveMutateProfile = p =>
                {
                    if (!p.claimedWeeklyGoalIds.Contains(goal.goalId))
                        p.claimedWeeklyGoalIds.Add(goal.goalId);
                }
            };

            RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
            if (!result.Succeeded)
                return false;

            OnMissionRewardClaimed?.Invoke(goal.goalId, MissionCategory.Weekly, payout);
            NotifyProgressChanged();
            return true;
        }
        finally
        {
            claimInProgress = false;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Internal Atomic Claim & Helpers
    // ─────────────────────────────────────────────────────────────

    private static string BuildCanonicalMissionRewardId(
        ProfileData profile,
        MissionCategory category,
        string missionId,
        bool isMonthlyCumulativeWeekly = false)
    {
        if (string.IsNullOrEmpty(missionId))
            return string.Empty;

        string mondayKey = profile != null && !string.IsNullOrEmpty(profile.weeklyMissionResetMondayLocal)
            ? profile.weeklyMissionResetMondayLocal
            : LocalClock.FormatMondayKey(GetLocalNow());
        string monthKey = profile != null && !string.IsNullOrEmpty(profile.monthlyMissionResetMonthLocal)
            ? profile.monthlyMissionResetMonthLocal
            : GetLocalNow().ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

        if (isMonthlyCumulativeWeekly)
            return EconomyRewardIds.MonthlyWeeklyMission(monthKey, missionId);

        return category switch
        {
            MissionCategory.Daily       => EconomyRewardIds.DailyMission(mondayKey, missionId),
            MissionCategory.Weekly      => EconomyRewardIds.WeeklyMission(mondayKey, missionId),
            MissionCategory.Sector      => EconomyRewardIds.SectorMission(missionId),
            MissionCategory.NewPlayer   => EconomyRewardIds.NewPlayerMission(missionId),
            MissionCategory.Achievement => EconomyRewardIds.Achievement(missionId),
            _                           => EconomyRewardIds.WeeklyMission(mondayKey, missionId)
        };
    }

    private static List<string> GetClaimListForCategory(
        ProfileData profile,
        MissionCategory category,
        bool isMonthlyCumulativeWeekly = false)
    {
        if (profile == null) return null;
        profile.Sanitize();

        if (isMonthlyCumulativeWeekly)
            return profile.claimedMonthlyWeeklyMissionIds;

        return category switch
        {
            MissionCategory.NewPlayer   => profile.claimedNewPlayerMissionIds,
            MissionCategory.Daily       => profile.claimedDailyMissionIds,
            MissionCategory.Sector      => profile.claimedSectorMissionIds,
            MissionCategory.Achievement => profile.claimedAchievementIds,
            _                           => profile.claimedMissionIds
        };
    }

    private static RewardBundle BuildMissionRewardBundle(
        MissionRewardType rewardType,
        int rewardAmount,
        string powerUpId,
        string cosmeticId)
    {
        var bundle = new RewardBundle();
        switch (rewardType)
        {
            case MissionRewardType.Coins:
                if (rewardAmount > 0)
                    bundle.Add(RewardType.Coins, rewardAmount);
                break;

            case MissionRewardType.Gems:
                if (rewardAmount > 0)
                    bundle.Add(RewardType.Gems, rewardAmount);
                break;

            case MissionRewardType.PowerUp:
                string safeSkill = !string.IsNullOrWhiteSpace(powerUpId) ? powerUpId : PowerUpIds.Breaker;
                if (rewardAmount > 0 && EconomyBalanceConfig.Active.IsSkillActiveInCampaign(safeSkill))
                    bundle.Add(RewardType.PowerUp, rewardAmount, safeSkill);
                break;

            case MissionRewardType.Cosmetic:
                if (!string.IsNullOrWhiteSpace(cosmeticId))
                    bundle.Add(RewardType.Cosmetic, 1, cosmeticId);
                break;
        }
        return bundle;
    }

    private static bool ClaimMissionInternal(
        string missionId,
        MissionCategory category,
        int missionPoints,
        MissionRewardType rewardType,
        int rewardAmount,
        string powerUpId,
        string cosmeticId,
        bool isMonthlyCumulativeWeekly = false)
    {
        if (claimInProgress || SimulateRewardFailureForTesting) return false;
        if (string.IsNullOrEmpty(missionId)) return false;

        claimInProgress = true;
        try
        {
            ProfileData profile = EnsureProfileMigratedAndUpdated();
            if (IsClockRolledBack && (category == MissionCategory.Daily || category == MissionCategory.Weekly))
            {
                Debug.LogWarning($"[MissionService] Blocked periodic mission claim '{missionId}' due to detected system clock rollback.");
                return false;
            }

            List<string> claimList = GetClaimListForCategory(profile, category, isMonthlyCumulativeWeekly);
            if (claimList == null || claimList.Contains(missionId))
                return false;

            string rewardId = BuildCanonicalMissionRewardId(profile, category, missionId, isMonthlyCumulativeWeekly);
            RewardBundle payout = BuildMissionRewardBundle(rewardType, rewardAmount, powerUpId, cosmeticId);
            int pointsAdded = category == MissionCategory.Weekly ? Mathf.Max(0, missionPoints) : 0;

            var tx = new RewardTransaction
            {
                RewardId = rewardId,
                RequireUniqueRewardId = true,
                Payout = payout,
                RejectIfSkillCapExceeded = rewardType == MissionRewardType.PowerUp,
                RejectIfCosmeticAlreadyOwned = false,
                Reason = $"MissionClaim_{category}_{missionId}",
                OnBeforeSaveMutateProfile = p =>
                {
                    List<string> listToMutate = GetClaimListForCategory(p, category, isMonthlyCumulativeWeekly);
                    if (listToMutate != null && !listToMutate.Contains(missionId))
                        listToMutate.Add(missionId);

                    if ((category == MissionCategory.Weekly || category == MissionCategory.Daily) && !p.claimedWeeklyMissionIds.Contains(missionId))
                        p.claimedWeeklyMissionIds.Add(missionId);

                    p.weeklyMissionPoints += pointsAdded;
                }
            };

            RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
            if (!result.Succeeded)
                return false;

            OnMissionRewardClaimed?.Invoke(missionId, category, payout);
            NotifyProgressChanged();
            return true;
        }
        finally
        {
            claimInProgress = false;
        }
    }

    private static void NotifyProgressChanged()
    {
        OnWeeklyProgressChanged?.Invoke();
        OnMissionProgressChanged?.Invoke();
    }

    private static bool SaveProfileState(ProfileData profile)
    {
        if (profile == null) return false;
        return SaveManager.TrySaveProfile(profile);
    }

    private static ProfileData GetActiveProfile()
    {
        return SaveManager.loadProfile();
    }
}
