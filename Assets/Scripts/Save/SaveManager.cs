using UnityEngine;
using System.IO;

public static class SaveManager
{
    private static string profilePath => Path.Combine(Application.persistentDataPath, "profile.json");

    public static void saveProfile(ProfileData data)
    {
        string json = JsonUtility.ToJson(data, true);
        File.WriteAllText(profilePath, json);
    }

    public static ProfileData loadProfile()
    {
        if (File.Exists(profilePath))
        {
            string json = File.ReadAllText(profilePath);
            return JsonUtility.FromJson<ProfileData>(json);
        }

        return new ProfileData();
    }

    /// <summary>Returns true if a persisted profile file exists on disk.</summary>
    public static bool HasSavedProfile() => File.Exists(profilePath);

    public static void ResetCampaignProgression()
    {
        ProfileData profile = loadProfile();
        if (profile != null)
        {
            profile.unlockedLevelIDs?.Clear();
            profile.completedLevelIDs?.Clear();
            profile.levelStarKeys?.Clear();
            profile.levelStarValues?.Clear();
            saveProfile(profile);
            Debug.Log("[SaveManager] Campaign progression has been reset!");
        }
    }

    public static void ResetEconomy()
    {
        ProfileData profile = loadProfile();
        if (profile != null)
        {
            profile.coins = 0;
            profile.gems = 0;
            saveProfile(profile);
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
            profile.noAdsPurchased = false;
            profile.purchasedProductIDs?.Clear();
            profile.purchasedShopItemIDs?.Clear();
            profile.lastCoinBonusClaimUtcTicks = 0;
            profile.lastDailyRewardClaimUtcTicks = 0;
            profile.tutorialCompleted = false;
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
