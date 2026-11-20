using System;
using System.Threading.Tasks;

/// <summary>
/// Game-facing Google Play Games service abstraction.
/// Handles player account sign-in, Saved Games cloud storage, achievements, and leaderboards.
/// </summary>
public interface IPlayGamesService
{
    bool IsSupported { get; }
    bool IsAuthenticated { get; }
    string PlayerId { get; }
    string DisplayName { get; }
    ICloudSaveProvider CloudSave { get; }

    event Action<bool> OnAuthenticationChanged;

    Task<AuthResult> SignInAsync(bool interactive = true);
    Task<bool> TryUnlockAchievementAsync(string internalAchievementId);
    Task<bool> TryReportRunnerRecordAsync(RunnerModeKind mode, int scoreValue);
    void ShowAchievementsUI();
    void ShowLeaderboardsUI(string leaderboardKey = null);
}
