using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class PlayGamesAchievementMap
{
    public string internalAchievementId;
    public string gpgsAchievementId;
}

[Serializable]
public class PlayGamesLeaderboardMap
{
    public string leaderboardKey;
    public string gpgsLeaderboardId;
}

/// <summary>
/// Centralized ScriptableObject configuring Google Play Games Services behavior,
/// achievement ID mappings (<c>ach_01..ach_09</c>), and Maze Runner leaderboard IDs.
/// </summary>
[CreateAssetMenu(fileName = "PlayGamesConfig", menuName = "Gravita/Services/Play Games Config")]
public class PlayGamesConfig : ScriptableObject
{
    public const string DefaultResourcePath = "PlayGamesConfig";
    public const string LeaderboardRunnerEndless = "runner_endless_best";
    public const string LeaderboardRunner10Rooms = "runner_10rooms_clears";
    public const string LeaderboardRunner20Rooms = "runner_20rooms_clears";

    private static PlayGamesConfig activeOverride;
    private static PlayGamesConfig runtimeFallback;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticConfig()
    {
        activeOverride = null;
        runtimeFallback = null;
    }

    public static PlayGamesConfig Active
    {
        get
        {
            if (activeOverride != null)
                return activeOverride;

            PlayGamesConfig loaded = Resources.Load<PlayGamesConfig>(DefaultResourcePath);
            if (loaded != null)
                return loaded;

            if (runtimeFallback == null)
            {
                runtimeFallback = CreateInstance<PlayGamesConfig>();
                runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
                runtimeFallback.PopulateDefaultKeysIfEmpty();
            }

            return runtimeFallback;
        }
    }

    public static void SetActiveConfig(PlayGamesConfig config)
    {
        activeOverride = config;
    }

    [Header("Sign-In & Cloud Save")]
    [SerializeField] private bool enablePlayGames = true;
    [Tooltip("Automatically attempts silent GPGS v2 sign-in on Android startup.")]
    [SerializeField] private bool autoSignInOnStartup = true;
    [Tooltip("Synchronizes ProfileData via GPGS Saved Games (ISavedGameClient) with Smart Merge.")]
    [SerializeField] private bool enableSavedGamesCloudSync = true;

    [Header("Progression Sync")]
    [SerializeField] private bool syncAchievements = true;
    [SerializeField] private bool syncLeaderboards = true;

    [Header("Google Play Console Mappings")]
    [SerializeField] private List<PlayGamesAchievementMap> achievementMappings = new List<PlayGamesAchievementMap>();
    [SerializeField] private List<PlayGamesLeaderboardMap> leaderboardMappings = new List<PlayGamesLeaderboardMap>();

    public bool EnablePlayGames => enablePlayGames;
    public bool AutoSignInOnStartup => autoSignInOnStartup;
    public bool EnableSavedGamesCloudSync => enableSavedGamesCloudSync;
    public bool SyncAchievements => syncAchievements;
    public bool SyncLeaderboards => syncLeaderboards;
    public IReadOnlyList<PlayGamesAchievementMap> AchievementMappings => achievementMappings;
    public IReadOnlyList<PlayGamesLeaderboardMap> LeaderboardMappings => leaderboardMappings;

    public static string GetLeaderboardKey(RunnerModeKind mode)
    {
        return mode switch
        {
            RunnerModeKind.Rooms10 => LeaderboardRunner10Rooms,
            RunnerModeKind.Rooms20 => LeaderboardRunner20Rooms,
            _                      => LeaderboardRunnerEndless
        };
    }

    public bool TryGetGpgsAchievementId(string internalId, out string gpgsId)
    {
        gpgsId = null;
        if (string.IsNullOrWhiteSpace(internalId) || achievementMappings == null)
            return false;

        string target = internalId.Trim();
        for (int i = 0; i < achievementMappings.Count; i++)
        {
            PlayGamesAchievementMap entry = achievementMappings[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.internalAchievementId))
                continue;

            if (string.Equals(entry.internalAchievementId.Trim(), target, StringComparison.OrdinalIgnoreCase) &&
                IsValidGpgsId(entry.gpgsAchievementId))
            {
                gpgsId = entry.gpgsAchievementId.Trim();
                return true;
            }
        }

        return false;
    }

    public bool TryGetGpgsLeaderboardId(string leaderboardKey, out string gpgsId)
    {
        gpgsId = null;
        if (string.IsNullOrWhiteSpace(leaderboardKey) || leaderboardMappings == null)
            return false;

        string target = leaderboardKey.Trim();
        for (int i = 0; i < leaderboardMappings.Count; i++)
        {
            PlayGamesLeaderboardMap entry = leaderboardMappings[i];
            if (entry == null || string.IsNullOrWhiteSpace(entry.leaderboardKey))
                continue;

            if (string.Equals(entry.leaderboardKey.Trim(), target, StringComparison.OrdinalIgnoreCase) &&
                IsValidGpgsId(entry.gpgsLeaderboardId))
            {
                gpgsId = entry.gpgsLeaderboardId.Trim();
                return true;
            }
        }

        return false;
    }

    public void ConfigureForTesting(
        bool enabled = true,
        bool autoSignIn = true,
        bool enableCloudSync = true,
        bool syncAchs = true,
        bool syncBoards = true,
        IEnumerable<PlayGamesAchievementMap> achievements = null,
        IEnumerable<PlayGamesLeaderboardMap> leaderboards = null)
    {
        enablePlayGames = enabled;
        autoSignInOnStartup = autoSignIn;
        enableSavedGamesCloudSync = enableCloudSync;
        syncAchievements = syncAchs;
        syncLeaderboards = syncBoards;

        achievementMappings ??= new List<PlayGamesAchievementMap>();
        leaderboardMappings ??= new List<PlayGamesLeaderboardMap>();

        if (achievements != null)
        {
            achievementMappings.Clear();
            achievementMappings.AddRange(achievements);
        }

        if (leaderboards != null)
        {
            leaderboardMappings.Clear();
            leaderboardMappings.AddRange(leaderboards);
        }
    }

    public void PopulateDefaultKeysIfEmpty()
    {
        achievementMappings ??= new List<PlayGamesAchievementMap>();
        if (achievementMappings.Count == 0)
        {
            for (int i = 1; i <= 9; i++)
            {
                achievementMappings.Add(new PlayGamesAchievementMap
                {
                    internalAchievementId = $"ach_{i:00}",
                    gpgsAchievementId = string.Empty
                });
            }
        }

        leaderboardMappings ??= new List<PlayGamesLeaderboardMap>();
        if (leaderboardMappings.Count == 0)
        {
            leaderboardMappings.Add(new PlayGamesLeaderboardMap
            {
                leaderboardKey = LeaderboardRunnerEndless,
                gpgsLeaderboardId = string.Empty
            });
            leaderboardMappings.Add(new PlayGamesLeaderboardMap
            {
                leaderboardKey = LeaderboardRunner10Rooms,
                gpgsLeaderboardId = string.Empty
            });
            leaderboardMappings.Add(new PlayGamesLeaderboardMap
            {
                leaderboardKey = LeaderboardRunner20Rooms,
                gpgsLeaderboardId = string.Empty
            });
        }
    }

    private static bool IsValidGpgsId(string rawId)
    {
        if (string.IsNullOrWhiteSpace(rawId))
            return false;

        string trimmed = rawId.Trim();
        return !trimmed.StartsWith("TODO", StringComparison.OrdinalIgnoreCase);
    }

    private void OnValidate()
    {
        if (achievementMappings != null)
        {
            for (int i = 0; i < achievementMappings.Count; i++)
            {
                PlayGamesAchievementMap map = achievementMappings[i];
                if (map == null) continue;
                map.internalAchievementId = map.internalAchievementId != null ? map.internalAchievementId.Trim() : string.Empty;
                map.gpgsAchievementId = map.gpgsAchievementId != null ? map.gpgsAchievementId.Trim() : string.Empty;
            }
        }

        if (leaderboardMappings != null)
        {
            for (int i = 0; i < leaderboardMappings.Count; i++)
            {
                PlayGamesLeaderboardMap map = leaderboardMappings[i];
                if (map == null) continue;
                map.leaderboardKey = map.leaderboardKey != null ? map.leaderboardKey.Trim() : string.Empty;
                map.gpgsLeaderboardId = map.gpgsLeaderboardId != null ? map.gpgsLeaderboardId.Trim() : string.Empty;
            }
        }
    }
}
