using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Live-Ops Event domain service managing rolling/fixed event windows via <see cref="LocalClock"/>,
/// task progress queries, and idempotent reward claims through <see cref="RewardService"/>.
/// </summary>
public static class EventService
{
    public static event Action OnEventStateChanged;
    public static event Action<string, string, RewardBundle> OnEventTaskRewardClaimed;

    private static bool claimInProgress;
    private static bool eventsSubscribed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    public static void EnsureInitialized()
    {
        if (eventsSubscribed)
            return;

        MissionService.OnMissionProgressChanged -= HandleExternalProgressChanged;
        MissionService.OnMissionProgressChanged += HandleExternalProgressChanged;
        MazeRunnerService.OnAnyRunEnded -= HandleRunnerRunEnded;
        MazeRunnerService.OnAnyRunEnded += HandleRunnerRunEnded;
        eventsSubscribed = true;
    }

    private static void HandleExternalProgressChanged() => OnEventStateChanged?.Invoke();
    private static void HandleRunnerRunEnded(RunnerRunSummary _) => OnEventStateChanged?.Invoke();

    public static bool TryGetEventWindowLocal(GameEventDefinition def, out DateTime startLocal, out DateTime endLocal)
    {
        DateTime now = LocalClock.Now;
        startLocal = now.Date;
        endLocal = now.Date.AddDays(1);

        if (def == null)
            return false;

        int duration = Mathf.Max(1, def.DurationDays);

        if (def.UseFixedDateSchedule)
        {
            if (!TryParseDateKey(def.StartDateIso, out DateTime parsedStart))
                return false;

            startLocal = parsedStart.Date;
            endLocal = startLocal.AddDays(duration);
            return true;
        }

        ProfileData profile = MissionService.EnsureProfileMigratedAndUpdated();
        if (profile == null)
        {
            endLocal = startLocal.AddDays(duration);
            return true;
        }

        string storedKey = profile.GetEventStartDateKey(def.EventId);
        if (!TryParseDateKey(storedKey, out DateTime rollingStart))
        {
            rollingStart = now.Date;
            profile.SetEventStartDateKey(def.EventId, LocalClock.FormatDateKey(rollingStart));
            SaveManager.TrySaveProfile(profile);
        }

        startLocal = rollingStart.Date;
        endLocal = startLocal.AddDays(duration);
        return true;
    }

    private static readonly string[] AcceptedDateFormats = { "yyyy_MM_dd", "yyyy-MM-dd" };

    private static bool TryParseDateKey(string rawKey, out DateTime parsedDate)
    {
        if (string.IsNullOrWhiteSpace(rawKey))
        {
            parsedDate = default;
            return false;
        }

        return DateTime.TryParseExact(
            rawKey.Trim(),
            AcceptedDateFormats,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out parsedDate);
    }

    public static bool IsEventActive(GameEventDefinition def)
    {
        if (def == null || !TryGetEventWindowLocal(def, out DateTime startLocal, out DateTime endLocal))
            return false;

        DateTime now = LocalClock.Now;
        return now >= startLocal && now < endLocal;
    }

    public static TimeSpan GetRemainingTime(GameEventDefinition def)
    {
        if (def == null || !TryGetEventWindowLocal(def, out DateTime startLocal, out DateTime endLocal))
            return TimeSpan.Zero;

        DateTime now = LocalClock.Now;
        if (now < startLocal)
            return startLocal - now;

        TimeSpan remaining = endLocal - now;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    public static string FormatRemainingTime(GameEventDefinition def)
    {
        if (def == null || !TryGetEventWindowLocal(def, out DateTime startLocal, out DateTime endLocal))
            return "ENDED";

        DateTime now = LocalClock.Now;
        if (now < startLocal)
        {
            TimeSpan untilStart = startLocal - now;
            return untilStart.TotalDays >= 1d
                ? $"STARTS IN {(int)untilStart.TotalDays}D {untilStart.Hours}H"
                : $"STARTS IN {Mathf.Max(0, (int)untilStart.TotalHours)}H {untilStart.Minutes}M";
        }

        TimeSpan rem = endLocal - now;
        if (rem <= TimeSpan.Zero)
            return "ENDED";

        return rem.TotalDays >= 1d
            ? $"{(int)rem.TotalDays}D {rem.Hours}H LEFT"
            : $"{Mathf.Max(0, (int)rem.TotalHours)}H {rem.Minutes}M LEFT";
    }

    public static int GetTaskProgress(GameEventDefinition def, GameEventTaskEntry task)
    {
        if (def == null || string.IsNullOrEmpty(task.taskId))
            return 0;

        int raw = 0;
        switch (task.objectiveKind)
        {
            case EventObjectiveKind.RunnerBestRooms:
            {
                ProfileData profile = SaveManager.loadProfile();
                raw = profile != null ? Mathf.Max(0, profile.bestRunnerEndlessRooms) : 0;
                break;
            }
            case EventObjectiveKind.RunnerLifetimeRooms:
            {
                ProfileData profile = SaveManager.loadProfile();
                raw = profile != null ? Mathf.Max(0, profile.lifetimeRunnerRoomsCleared) : 0;
                break;
            }
            default:
                raw = MissionService.GetProgress(task.missionType, 1, MissionCategory.Achievement);
                break;
        }

        return IsTaskClaimed(def, task) ? Mathf.Max(raw, Mathf.Max(1, task.target)) : raw;
    }

    public static bool IsTaskClaimed(GameEventDefinition def, GameEventTaskEntry task)
    {
        if (def == null || string.IsNullOrEmpty(task.taskId))
            return false;

        ProfileData profile = MissionService.EnsureProfileMigratedAndUpdated();
        if (profile == null)
            return false;

        string rewardId = EconomyRewardIds.EventTask(def.EventId, task.taskId);
        if (!string.IsNullOrEmpty(rewardId) && profile.HasClaimedReward(rewardId))
            return true;

        string key = BuildTaskCompositeKey(def.EventId, task.taskId);
        return profile.claimedEventTaskIds != null && profile.claimedEventTaskIds.Contains(key);
    }

    public static bool IsTaskComplete(GameEventDefinition def, GameEventTaskEntry task)
    {
        if (def == null || string.IsNullOrEmpty(task.taskId))
            return false;

        return IsTaskClaimed(def, task) || GetTaskProgress(def, task) >= Mathf.Max(1, task.target);
    }

    public static bool HasAnyClaimableTask(GameEventDefinition def)
    {
        if (def == null || !IsEventActive(def) || def.Tasks == null)
            return false;

        IReadOnlyList<GameEventTaskEntry> list = def.Tasks;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsTaskComplete(def, list[i]) && !IsTaskClaimed(def, list[i]))
                return true;
        }

        return false;
    }

    public static int GetClaimedTaskCount(GameEventDefinition def)
    {
        if (def == null || def.Tasks == null)
            return 0;

        int count = 0;
        IReadOnlyList<GameEventTaskEntry> list = def.Tasks;
        for (int i = 0; i < list.Count; i++)
        {
            if (IsTaskClaimed(def, list[i]))
                count++;
        }

        return count;
    }

    public static bool TryClaimTask(GameEventDefinition def, GameEventTaskEntry task)
    {
        if (claimInProgress || MissionService.SimulateRewardFailureForTesting)
            return false;

        if (def == null || string.IsNullOrEmpty(task.taskId))
            return false;

        if (!IsEventActive(def))
            return false;

        if (IsTaskClaimed(def, task) || !IsTaskComplete(def, task))
            return false;

        EnsureInitialized();
        claimInProgress = true;
        try
        {
            string rewardId = EconomyRewardIds.EventTask(def.EventId, task.taskId);
            string compositeKey = BuildTaskCompositeKey(def.EventId, task.taskId);
            RewardBundle payout = AchievementService.BuildRewardBundle(
                task.rewardType,
                task.rewardAmount,
                task.rewardPowerUpId,
                task.rewardCosmeticId);

            var tx = new RewardTransaction
            {
                RewardId = rewardId,
                RequireUniqueRewardId = true,
                Payout = payout,
                RejectIfSkillCapExceeded = task.rewardType == MissionRewardType.PowerUp,
                RejectIfCosmeticAlreadyOwned = false,
                Reason = $"EventTaskClaim_{def.EventId}_{task.taskId}",
                OnBeforeSaveMutateProfile = p =>
                {
                    p.Sanitize();
                    if (!p.claimedEventTaskIds.Contains(compositeKey))
                        p.claimedEventTaskIds.Add(compositeKey);
                }
            };

            RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
            if (!result.Succeeded)
                return false;

            OnEventTaskRewardClaimed?.Invoke(def.EventId, task.taskId, payout);
            OnEventStateChanged?.Invoke();
            return true;
        }
        finally
        {
            claimInProgress = false;
        }
    }

    private static string BuildTaskCompositeKey(string eventId, string taskId) => $"{eventId}::{taskId}";
}
