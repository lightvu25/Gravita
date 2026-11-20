using System;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID
using GooglePlayGames;
using GooglePlayGames.BasicApi;
using GooglePlayGames.BasicApi.SavedGame;
#endif

/// <summary>
/// <see cref="ICloudSaveProvider"/> backed by Google Play Games Saved Games (<c>ISavedGameClient</c>).
/// </summary>
public sealed class PlayGamesCloudSaveProvider : ICloudSaveProvider
{
    private readonly PlayGamesService owner;

    public PlayGamesCloudSaveProvider(PlayGamesService owner)
    {
        this.owner = owner;
    }

    public bool IsAvailable
    {
        get
        {
            PlayGamesConfig cfg = PlayGamesConfig.Active;
            return owner != null
                && owner.IsAuthenticated
                && (cfg == null || (cfg.EnablePlayGames && cfg.EnableSavedGamesCloudSync));
        }
    }

    public Task<bool> UploadAsync(string slot, string json)
    {
        return owner != null ? owner.WriteSavedGameInternalAsync(slot, json) : Task.FromResult(false);
    }

    public Task<string> DownloadAsync(string slot)
    {
        return owner != null ? owner.ReadSavedGameInternalAsync(slot) : Task.FromResult<string>(null);
    }
}

/// <summary>
/// Fallback backend when the Google Play Games Unity plugin is not active
/// or when running outside Android.
/// </summary>
public sealed class UnsupportedPlayGamesBackend : IPlayGamesBackend
{
    public bool IsSupported => false;
    public bool IsAuthenticated => false;
    public string UserId => null;
    public string DisplayName => null;

    public void Initialize() { }
    public Task<bool> AuthenticateAsync(bool interactive) => Task.FromResult(false);
    public Task<string> ReadSavedGameJsonAsync(string slotName) => Task.FromResult<string>(null);
    public Task<bool> WriteSavedGameJsonAsync(string slotName, string json) => Task.FromResult(false);
    public Task<bool> UnlockAchievementAsync(string gpgsId) => Task.FromResult(false);
    public Task<bool> ReportScoreAsync(string gpgsLeaderboardId, long score) => Task.FromResult(false);
    public void ShowAchievementsUI() { }
    public void ShowLeaderboardsUI(string gpgsLeaderboardId = null) { }
}

#if GOOGLE_PLAY_GAMES && UNITY_ANDROID
/// <summary>
/// Native Google Play Games Services v2 backend compiled automatically when
/// <c>com.google.play.games</c> is installed and <c>GOOGLE_PLAY_GAMES</c> is defined on Android.
/// </summary>
public sealed class GooglePlayGamesNativeBackend : IPlayGamesBackend
{
    private bool initialized;

    public bool IsSupported => true;
    public bool IsAuthenticated => PlayGamesPlatform.Instance != null && PlayGamesPlatform.Instance.IsAuthenticated();
    public string UserId => IsAuthenticated ? PlayGamesPlatform.Instance.GetUserId() : null;
    public string DisplayName => IsAuthenticated ? PlayGamesPlatform.Instance.GetUserDisplayName() : null;

    public void Initialize()
    {
        if (initialized)
            return;

        initialized = true;
        PlayGamesPlatform.Activate();
    }

    public Task<bool> AuthenticateAsync(bool interactive)
    {
        Initialize();
        var tcs = new TaskCompletionSource<bool>();
        try
        {
            if (interactive)
                PlayGamesPlatform.Instance.ManuallyAuthenticate(status => tcs.TrySetResult(status == SignInStatus.Success));
            else
                PlayGamesPlatform.Instance.Authenticate(status => tcs.TrySetResult(status == SignInStatus.Success));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGames] Authenticate failed: {ex.Message}");
            tcs.TrySetResult(false);
        }
        return tcs.Task;
    }

    public Task<string> ReadSavedGameJsonAsync(string slotName)
    {
        if (!IsAuthenticated || PlayGamesPlatform.Instance == null || PlayGamesPlatform.Instance.SavedGame == null)
            return Task.FromResult<string>(null);

        string safeFilename = SanitizeSlotFileName(slotName);
        var tcs = new TaskCompletionSource<string>();
        try
        {
            PlayGamesPlatform.Instance.SavedGame.OpenWithAutomaticConflictResolution(
                safeFilename,
                DataSource.ReadCacheOrNetwork,
                ConflictResolutionStrategy.UseMostRecentlySaved,
                (openStatus, metadata) =>
                {
                    if (openStatus != SavedGameRequestStatus.Success || metadata == null)
                    {
                        tcs.TrySetResult(null);
                        return;
                    }

                    PlayGamesPlatform.Instance.SavedGame.ReadBinaryData(metadata, (readStatus, bytes) =>
                    {
                        if (readStatus != SavedGameRequestStatus.Success)
                        {
                            tcs.TrySetResult(null);
                            return;
                        }

                        if (bytes == null || bytes.Length == 0)
                        {
                            tcs.TrySetResult(string.Empty);
                            return;
                        }

                        tcs.TrySetResult(Encoding.UTF8.GetString(bytes));
                    });
                });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGames] ReadSavedGameJsonAsync failed: {ex.Message}");
            tcs.TrySetResult(null);
        }
        return tcs.Task;
    }

    public Task<bool> WriteSavedGameJsonAsync(string slotName, string json)
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(json) || PlayGamesPlatform.Instance == null || PlayGamesPlatform.Instance.SavedGame == null)
            return Task.FromResult(false);

        string safeFilename = SanitizeSlotFileName(slotName);
        byte[] payload = Encoding.UTF8.GetBytes(json);
        var tcs = new TaskCompletionSource<bool>();
        try
        {
            PlayGamesPlatform.Instance.SavedGame.OpenWithAutomaticConflictResolution(
                safeFilename,
                DataSource.ReadCacheOrNetwork,
                ConflictResolutionStrategy.UseMostRecentlySaved,
                (openStatus, metadata) =>
                {
                    if (openStatus != SavedGameRequestStatus.Success || metadata == null)
                    {
                        tcs.TrySetResult(false);
                        return;
                    }

                    SavedGameMetadataUpdate metadataUpdate = new SavedGameMetadataUpdate.Builder()
                        .WithUpdatedDescription("Gravita Profile Save")
                        .Build();

                    PlayGamesPlatform.Instance.SavedGame.CommitUpdate(
                        metadata,
                        metadataUpdate,
                        payload,
                        (commitStatus, committedMeta) =>
                        {
                            tcs.TrySetResult(commitStatus == SavedGameRequestStatus.Success);
                        });
                });
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGames] WriteSavedGameJsonAsync failed: {ex.Message}");
            tcs.TrySetResult(false);
        }
        return tcs.Task;
    }

    public Task<bool> UnlockAchievementAsync(string gpgsId)
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(gpgsId))
            return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();
        try
        {
            PlayGamesPlatform.Instance.ReportProgress(gpgsId, 100.0, success => tcs.TrySetResult(success));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGames] ReportProgress failed: {ex.Message}");
            tcs.TrySetResult(false);
        }
        return tcs.Task;
    }

    public Task<bool> ReportScoreAsync(string gpgsLeaderboardId, long score)
    {
        if (!IsAuthenticated || string.IsNullOrEmpty(gpgsLeaderboardId))
            return Task.FromResult(false);

        var tcs = new TaskCompletionSource<bool>();
        try
        {
            PlayGamesPlatform.Instance.ReportScore(score, gpgsLeaderboardId, success => tcs.TrySetResult(success));
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGames] ReportScore failed: {ex.Message}");
            tcs.TrySetResult(false);
        }
        return tcs.Task;
    }

    public void ShowAchievementsUI()
    {
        if (!IsAuthenticated) return;
        PlayGamesPlatform.Instance.ShowAchievementsUI();
    }

    public void ShowLeaderboardsUI(string gpgsLeaderboardId = null)
    {
        if (!IsAuthenticated) return;
        if (string.IsNullOrEmpty(gpgsLeaderboardId))
            PlayGamesPlatform.Instance.ShowLeaderboardUI();
        else
            PlayGamesPlatform.Instance.ShowLeaderboardUI(gpgsLeaderboardId);
    }

    private static string SanitizeSlotFileName(string rawSlot)
    {
        if (string.IsNullOrWhiteSpace(rawSlot))
            return "gravita_profile_v2";

        var sb = new StringBuilder(Mathf.Min(rawSlot.Length, 100));
        for (int i = 0; i < rawSlot.Length && sb.Length < 100; i++)
        {
            char c = rawSlot[i];
            bool allowed = (c >= 'a' && c <= 'z')
                || (c >= 'A' && c <= 'Z')
                || (c >= '0' && c <= '9')
                || c == '-' || c == '.' || c == '_' || c == '~';
            sb.Append(allowed ? c : '_');
        }

        string cleaned = sb.ToString();
        return string.IsNullOrEmpty(cleaned) ? "gravita_profile_v2" : cleaned;
    }
}
#endif

/// <summary>
/// Central coordinator for Google Play Games Services sign-in, Saved Games cloud storage,
/// achievement forwarding, and Maze Runner leaderboard reporting.
/// </summary>
public class PlayGamesService : MonoBehaviour, IPlayGamesService
{
    public static PlayGamesService Instance { get; private set; }

    private IPlayGamesBackend backend;
    private ICloudSaveProvider cloudSave;
    private bool initializedBackend;
    private bool achievementSubscribed;
    private bool lastReportedAuthState;
    private Task<AuthResult> inFlightSignInTask;

    public bool IsSupported => Backend.IsSupported;
    public bool IsAuthenticated => Backend.IsAuthenticated;
    public string PlayerId => Backend.UserId;
    public string DisplayName => Backend.DisplayName;
    public ICloudSaveProvider CloudSave => cloudSave ??= new PlayGamesCloudSaveProvider(this);

    public event Action<bool> OnAuthenticationChanged;

    private IPlayGamesBackend Backend
    {
        get
        {
            if (backend == null)
                backend = CreateDefaultBackend();
            return backend;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void SpawnServiceInPlayMode()
    {
        if (!Application.isPlaying || Instance != null)
            return;

        var go = new GameObject(nameof(PlayGamesService));
        go.AddComponent<PlayGamesService>();
        DontDestroyOnLoad(go);
    }

    private static IPlayGamesBackend CreateDefaultBackend()
    {
#if GOOGLE_PLAY_GAMES && UNITY_ANDROID
        if (!Application.isEditor)
            return new GooglePlayGamesNativeBackend();
#endif
        return new UnsupportedPlayGamesBackend();
    }

    public void SetBackendForTesting(IPlayGamesBackend customBackend)
    {
        backend = customBackend ?? new UnsupportedPlayGamesBackend();
        cloudSave = new PlayGamesCloudSaveProvider(this);
        initializedBackend = false;
        lastReportedAuthState = backend.IsAuthenticated;
    }

    private void Awake()
    {
        if (Instance != null && !ReferenceEquals(Instance, this))
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (cloudSave == null)
            cloudSave = new PlayGamesCloudSaveProvider(this);

        if (Application.isPlaying)
            DontDestroyOnLoad(gameObject);

        SubscribeAchievements();
    }

    private void Start()
    {
        if (!Application.isPlaying)
            return;

        PlayGamesConfig config = PlayGamesConfig.Active;
        if (config != null && config.EnablePlayGames && config.AutoSignInOnStartup && Backend.IsSupported)
            _ = SignInAsync(interactive: false);
    }

    private void OnDestroy()
    {
        UnsubscribeAchievements();

        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    public Task<AuthResult> SignInAsync(bool interactive = true)
    {
        if (inFlightSignInTask != null && !inFlightSignInTask.IsCompleted)
            return inFlightSignInTask;

        inFlightSignInTask = SignInInternalAsync(interactive);
        return inFlightSignInTask;
    }

    private async Task<AuthResult> SignInInternalAsync(bool interactive)
    {
        PlayGamesConfig config = PlayGamesConfig.Active;
        if (config != null && !config.EnablePlayGames)
            return AuthResult.Fail("Google Play Games Services is disabled in PlayGamesConfig.");

        if (!Backend.IsSupported)
            return AuthResult.Fail("Google Play Games Services is only supported on Android builds.");

        if (!initializedBackend)
        {
            Backend.Initialize();
            initializedBackend = true;
        }

        bool authed = false;
        try
        {
            authed = await Backend.AuthenticateAsync(interactive);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGamesService] Authentication exception: {ex.Message}");
            authed = false;
        }

        NotifyAuthStateIfChanged(authed);

        if (!authed)
            return AuthResult.Fail("Google Play Games sign-in was cancelled or unavailable.");

        if (Application.isPlaying)
            AnalyticsService.UpdateUserId(Backend.UserId);

        return AuthResult.Ok(Backend.UserId);
    }

    internal async Task<string> ReadSavedGameInternalAsync(string slotName)
    {
        PlayGamesConfig config = PlayGamesConfig.Active;
        if (!IsAuthenticated || (config != null && (!config.EnablePlayGames || !config.EnableSavedGamesCloudSync)))
            return null;

        try
        {
            return await Backend.ReadSavedGameJsonAsync(slotName);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGamesService] ReadSavedGame failed: {ex.Message}");
            return null;
        }
    }

    internal async Task<bool> WriteSavedGameInternalAsync(string slotName, string json)
    {
        PlayGamesConfig config = PlayGamesConfig.Active;
        if (!IsAuthenticated || string.IsNullOrEmpty(json) || (config != null && (!config.EnablePlayGames || !config.EnableSavedGamesCloudSync)))
            return false;

        try
        {
            return await Backend.WriteSavedGameJsonAsync(slotName, json);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGamesService] WriteSavedGame failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryUnlockAchievementAsync(string internalAchievementId)
    {
        PlayGamesConfig config = PlayGamesConfig.Active;
        if (config == null || !config.EnablePlayGames || !config.SyncAchievements || !IsAuthenticated)
            return false;

        if (!config.TryGetGpgsAchievementId(internalAchievementId, out string gpgsId))
            return false;

        try
        {
            return await Backend.UnlockAchievementAsync(gpgsId);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGamesService] Unlock achievement '{internalAchievementId}' failed: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> TryReportRunnerRecordAsync(RunnerModeKind mode, int scoreValue)
    {
        PlayGamesConfig config = PlayGamesConfig.Active;
        if (config == null || !config.EnablePlayGames || !config.SyncLeaderboards || !IsAuthenticated || scoreValue <= 0)
            return false;

        string key = PlayGamesConfig.GetLeaderboardKey(mode);
        if (!config.TryGetGpgsLeaderboardId(key, out string gpgsLeaderboardId))
            return false;

        try
        {
            return await Backend.ReportScoreAsync(gpgsLeaderboardId, scoreValue);
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[PlayGamesService] Report score for '{key}' failed: {ex.Message}");
            return false;
        }
    }

    public void ShowAchievementsUI()
    {
        if (!IsAuthenticated) return;
        Backend.ShowAchievementsUI();
    }

    public void ShowLeaderboardsUI(string leaderboardKey = null)
    {
        if (!IsAuthenticated) return;

        string resolvedId = null;
        if (!string.IsNullOrEmpty(leaderboardKey))
            PlayGamesConfig.Active.TryGetGpgsLeaderboardId(leaderboardKey, out resolvedId);

        Backend.ShowLeaderboardsUI(resolvedId);
    }

    private void SubscribeAchievements()
    {
        if (achievementSubscribed)
            return;

        achievementSubscribed = true;
        AchievementService.OnAchievementRewardClaimed += HandleAchievementRewardClaimed;
    }

    private void UnsubscribeAchievements()
    {
        if (!achievementSubscribed)
            return;

        achievementSubscribed = false;
        AchievementService.OnAchievementRewardClaimed -= HandleAchievementRewardClaimed;
    }

    private void HandleAchievementRewardClaimed(string achievementId, RewardBundle unusedPayout)
    {
        if (!string.IsNullOrEmpty(achievementId))
            _ = TryUnlockAchievementAsync(achievementId);
    }

    private void NotifyAuthStateIfChanged(bool isAuthenticated)
    {
        if (lastReportedAuthState == isAuthenticated)
            return;

        lastReportedAuthState = isAuthenticated;
        OnAuthenticationChanged?.Invoke(isAuthenticated);
    }
}
