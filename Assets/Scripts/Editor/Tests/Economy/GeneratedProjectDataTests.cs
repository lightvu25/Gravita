using NUnit.Framework;
using UnityEditor;

/// <summary>Guards the data produced by <c>ProjectDataAssetGenerator</c>.</summary>
public class GeneratedProjectDataTests
{
    [Test]
    public void CampaignWorld1_Has20Levels_WithEasyEasyMediumMediumHardPattern()
    {
        var db = AssetDatabase.LoadAssetAtPath<CampaignDatabase>("Assets/Data/Campaign/CampaignDatabase.asset");
        Assert.IsNotNull(db);
        Assert.IsNotEmpty(db.Worlds);
        LevelData[] levels = db.Worlds[0].levels;
        Assert.AreEqual(20, levels.Length);

        LevelDifficulty[] pattern =
        {
            LevelDifficulty.Easy, LevelDifficulty.Easy, LevelDifficulty.Medium,
            LevelDifficulty.Medium, LevelDifficulty.Hard
        };
        for (int i = 0; i < levels.Length; i++)
        {
            Assert.IsNotNull(levels[i], $"Level index {i} is null");
            Assert.IsNotNull(levels[i].layoutPrefab, $"Level index {i} has no layout prefab");
            Assert.AreEqual(pattern[i % 5], levels[i].difficulty, $"Level index {i}");
        }
    }

    [Test]
    public void LootPacks_BaseRatesSumTo100()
    {
        string[] guids = AssetDatabase.FindAssets("t:LootPackDefinition", new[] { "Assets/Data/LootPacks" });
        Assert.GreaterOrEqual(guids.Length, 3);
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var pack = AssetDatabase.LoadAssetAtPath<LootPackDefinition>(path);
            Assert.IsTrue(pack.AreBaseDropRatesValid(), $"{path} base rates sum to {pack.GetTotalBaseDropRate()}");
        }
    }

    [Test]
    public void MissionDatabase_Has12CumulativeWeeklyMissions()
    {
        var db = AssetDatabase.LoadAssetAtPath<MissionDatabase>("Assets/Data/Missions/MissionDatabase_Main.asset");
        Assert.IsNotNull(db);
        int count = 0;
        foreach (MissionDefinition m in db.Missions)
            if (m != null && m.IsMonthlyCumulativeWeekly) count++;
        Assert.AreEqual(12, count);
    }
}
