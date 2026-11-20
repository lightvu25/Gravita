using System.Threading.Tasks;

/// <summary>
/// Platform-level adapter contract for Google Play Games Services SDK v2.
/// Keeps SDK dependencies isolated behind an injectable test seam.
/// </summary>
public interface IPlayGamesBackend
{
    bool IsSupported { get; }
    bool IsAuthenticated { get; }
    string UserId { get; }
    string DisplayName { get; }

    void Initialize();
    Task<bool> AuthenticateAsync(bool interactive);
    Task<string> ReadSavedGameJsonAsync(string slotName);
    Task<bool> WriteSavedGameJsonAsync(string slotName, string json);
    Task<bool> UnlockAchievementAsync(string gpgsId);
    Task<bool> ReportScoreAsync(string gpgsLeaderboardId, long score);
    void ShowAchievementsUI();
    void ShowLeaderboardsUI(string gpgsLeaderboardId = null);
}
