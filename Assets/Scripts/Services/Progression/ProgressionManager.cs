using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages campaign progression: unlocked levels, completed levels, and world unlock requirements.
/// Fully data-driven via CampaignDatabase.
/// </summary>
public class ProgressionManager : MonoBehaviour
{
    public static ProgressionManager Instance { get; private set; }

    [Header("Campaign Database")]
    [Tooltip("Data-driven definition of campaign worlds and levels.")]
    [SerializeField] private CampaignDatabase campaignDatabase;

    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    public event Action<LevelData> OnLevelUnlocked;
    public event Action OnProgressionChanged;

    // ─────────────────────────────────────────────────────────────
    //  Public Properties
    // ─────────────────────────────────────────────────────────────

    public CampaignDatabase Database => campaignDatabase;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Query API
    // ─────────────────────────────────────────────────────────────

    public bool IsWorldUnlocked(WorldData world)
    {
        if (world == null) return false;
        if (world.requiredCompletedLevelsToUnlock <= 0) return true;

        int completedCount = GetCompletedLevelCount();
        return completedCount >= world.requiredCompletedLevelsToUnlock;
    }

    public bool IsLevelCompleted(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return false;

        var profile = GetProfile();
        return profile != null && profile.completedLevelIDs != null && profile.completedLevelIDs.Contains(level.LevelId);
    }

    public bool IsLevelUnlocked(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return false;

        // First level of campaign is always unlocked
        if (campaignDatabase != null && campaignDatabase.GetFirstLevel() == level)
            return true;

        var profile = GetProfile();
        if (profile != null && profile.unlockedLevelIDs != null && profile.unlockedLevelIDs.Contains(level.LevelId))
            return true;

        // If it's already completed, it's unlocked
        return IsLevelCompleted(level);
    }

    public int GetLevelStars(string levelId)
    {
        if (string.IsNullOrEmpty(levelId)) return 0;
        var profile = GetProfile();
        if (profile == null) return 0;

        int idx = profile.levelStarKeys.IndexOf(levelId);
        return idx >= 0 ? profile.levelStarValues[idx] : 0;
    }

    // ─────────────────────────────────────────────────────────────
    //  Power-up inventory
    // ─────────────────────────────────────────────────────────────

    public int GetPowerUpCount(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return 0;
        var profile = GetProfile();
        if (profile == null || profile.powerUpIds == null) return 0;

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        return idx >= 0 ? profile.powerUpCounts[idx] : 0;
    }

    public void AddPowerUp(string powerUpId, int count = 1)
    {
        if (string.IsNullOrEmpty(powerUpId) || count <= 0) return;
        var profile = GetProfile();
        if (profile == null) return;

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        if (idx >= 0)
            profile.powerUpCounts[idx] += count;
        else
        {
            profile.powerUpIds.Add(powerUpId);
            profile.powerUpCounts.Add(count);
        }

        SaveManager.saveProfile(profile);
        OnProgressionChanged?.Invoke();
    }

    /// <summary>Spends one charge of the power-up. Returns false when none are banked.</summary>
    public bool ConsumePowerUp(string powerUpId)
    {
        if (string.IsNullOrEmpty(powerUpId)) return false;
        var profile = GetProfile();
        if (profile == null || profile.powerUpIds == null) return false;

        int idx = profile.powerUpIds.IndexOf(powerUpId);
        if (idx < 0 || profile.powerUpCounts[idx] <= 0) return false;

        profile.powerUpCounts[idx]--;
        SaveManager.saveProfile(profile);
        OnProgressionChanged?.Invoke();
        return true;
    }

    public int GetCompletedLevelCount()
    {
        var profile = GetProfile();
        return profile != null && profile.completedLevelIDs != null ? profile.completedLevelIDs.Count : 0;
    }

    /// <summary>
    /// Total stars earned across all levels — the campaign's prestige metric.
    /// Fires OnProgressionChanged whenever this value may have changed.
    /// </summary>
    public int TotalStars
    {
        get
        {
            var profile = GetProfile();
            if (profile == null || profile.levelStarValues == null) return 0;

            int total = 0;
            for (int i = 0; i < profile.levelStarValues.Count; i++)
                total += profile.levelStarValues[i];
            return total;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Progression Modification
    // ─────────────────────────────────────────────────────────────

    public void CompleteLevel(LevelData level, int stars = 1)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return;

        var profile = GetProfile();
        if (profile == null) return;

        if (profile.completedLevelIDs == null)
            profile.completedLevelIDs = new List<string>();

        if (!profile.completedLevelIDs.Contains(level.LevelId))
        {
            profile.completedLevelIDs.Add(level.LevelId);
        }

        // Save highest stars
        int currentStars = GetLevelStars(level.LevelId);
        if (stars > currentStars)
        {
            int idx = profile.levelStarKeys.IndexOf(level.LevelId);
            if (idx >= 0)
            {
                profile.levelStarValues[idx] = stars;
            }
            else
            {
                profile.levelStarKeys.Add(level.LevelId);
                profile.levelStarValues.Add(stars);
            }
        }

        // Data-driven next level unlock:
        if (campaignDatabase != null)
        {
            LevelData nextLevel = campaignDatabase.GetNextLevel(level);
            if (nextLevel != null)
            {
                UnlockLevel(nextLevel);
            }
        }

        SaveManager.saveProfile(profile);
        OnProgressionChanged?.Invoke();
    }

    public void UnlockLevel(LevelData level)
    {
        if (level == null || string.IsNullOrEmpty(level.LevelId)) return;

        var profile = GetProfile();
        if (profile == null) return;

        if (profile.unlockedLevelIDs == null)
            profile.unlockedLevelIDs = new List<string>();

        if (!profile.unlockedLevelIDs.Contains(level.LevelId))
        {
            profile.unlockedLevelIDs.Add(level.LevelId);
            SaveManager.saveProfile(profile);
            OnLevelUnlocked?.Invoke(level);
            OnProgressionChanged?.Invoke();
        }
    }

    public void ResetAllProgression()
    {
        var profile = GetProfile();
        if (profile != null)
        {
            profile.unlockedLevelIDs.Clear();
            profile.completedLevelIDs.Clear();
            profile.levelStarKeys.Clear();
            profile.levelStarValues.Clear();
            SaveManager.saveProfile(profile);
            OnProgressionChanged?.Invoke();
            Debug.Log("[ProgressionManager] All campaign progression cleared.");
        }
    }

    private ProfileData GetProfile()
    {
        if (GameSession.Instance != null && GameSession.Instance.currentProfile != null)
            return GameSession.Instance.currentProfile;

        return SaveManager.loadProfile();
    }
}

