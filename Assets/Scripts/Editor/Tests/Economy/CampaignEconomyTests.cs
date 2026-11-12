using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase E2 Verification (GDD §14.2 Cases #1–#6 + No-Paywall Invariant + Anti-Coin-Farming):
///   - First clear grants fixed Coins 1x only; stars on first clear grant 0 extra star bonus.
///   - Replay without star improvement grants 0 Coins.
///   - Replay star upgrades (1->2, 1->3, 0->2) grant ONLY newly unlocked star tiers once.
///   - Sector completion grants Coin + Gem milestone 1x only.
///   - Sequential Sector 1->2->3 unlock works with PlayerLevel=1 / 0 EXP and zero Ads/IAP/Skills.
///   - In-level Collectible coin pickup grants 0 wallet Coins in standard Campaign.
/// </summary>
public class CampaignEconomyTests : EconomyTestHarnessBase
{
    private CampaignDatabase campaignDb;
    private LevelData s1L1;
    private LevelData s1L2;
    private LevelData s2L1;
    private LevelData s3L1;

    public override void BaseSetUp()
    {
        base.BaseSetUp();

        s1L1 = CreateLevel("S1_01", 1);
        s1L2 = CreateLevel("S1_02", 1);
        s2L1 = CreateLevel("S2_01", 2);
        s3L1 = CreateLevel("S3_01", 3);

        campaignDb = TrackAsset(ScriptableObject.CreateInstance<CampaignDatabase>());
        campaignDb.SetWorlds(new[]
        {
            new WorldData
            {
                worldNumber = 1,
                worldName = "Sector 1",
                requiredPlayerLevelToUnlock = 1,
                requiredExpToUnlock = 0,
                levels = new[] { s1L1, s1L2 }
            },
            new WorldData
            {
                worldNumber = 2,
                worldName = "Sector 2",
                // Even if legacy Inspector values had PlayerLevel/EXP locks, GDD §2.1 bypasses them!
                requiredPlayerLevelToUnlock = 10,
                requiredExpToUnlock = 5000,
                levels = new[] { s2L1 }
            },
            new WorldData
            {
                worldNumber = 3,
                worldName = "Sector 3",
                requiredPlayerLevelToUnlock = 25,
                requiredExpToUnlock = 15000,
                levels = new[] { s3L1 }
            }
        });
    }

    private LevelData CreateLevel(string id, int world)
    {
        var lvl = TrackAsset(ScriptableObject.CreateInstance<LevelData>());
        lvl.levelId = id;
        lvl.displayName = id;
        lvl.world = world;
        return lvl;
    }

    [Test]
    public void FirstClear_UnimprovedReplay_AndStarUpgradeDeltas_MatchGddExactly()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        economy.LoadFromProfile();

        SectorEconomyRewardConfig s1Cfg = TestBalance.GetSectorConfig(1);

        // Case #1: First clear with 1 star -> FirstClearCoins ONLY (0 direct star bonus on first clear)
        LevelEconomySettlement clear1 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 1, movesUsed: 12, usedAnySkill: false, campaignDb);
        Assert.IsTrue(clear1.IsFirstClear);
        Assert.AreEqual(s1Cfg.firstClearCoinReward, clear1.TotalCoinsGranted);
        Assert.AreEqual(0, clear1.ReplayStarUpgradeCoinsGranted);
        Assert.AreEqual(s1Cfg.firstClearCoinReward, economy.Coins);
        Assert.AreEqual(0, economy.Gems);

        // Case #2: Replay clear with same 1 star (or 0 stars) -> 0 Coins
        LevelEconomySettlement replaySame = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 1, movesUsed: 10, usedAnySkill: false, campaignDb);
        Assert.IsFalse(replaySame.IsFirstClear);
        Assert.AreEqual(0, replaySame.TotalCoinsGranted);
        Assert.AreEqual(10, replaySame.BestMoves, "Best moves should still update on faster replay!");
        Assert.AreEqual(s1Cfg.firstClearCoinReward, economy.Coins);

        // Case #3: Replay improving 1 -> 2 stars -> star2UnlockCoinReward once
        LevelEconomySettlement replayTo2 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 2, movesUsed: 9, usedAnySkill: false, campaignDb);
        Assert.AreEqual(1, replayTo2.NewlyUnlockedStarsCount);
        Assert.AreEqual(s1Cfg.star2UnlockCoinReward, replayTo2.ReplayStarUpgradeCoinsGranted);
        Assert.AreEqual(s1Cfg.firstClearCoinReward + s1Cfg.star2UnlockCoinReward, economy.Coins);

        // Replaying at 2 stars again -> 0 Coins
        LevelEconomySettlement replay2Again = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 2, movesUsed: 9, usedAnySkill: false, campaignDb);
        Assert.AreEqual(0, replay2Again.TotalCoinsGranted);

        // Improving 2 -> 3 stars -> star3UnlockCoinReward once
        LevelEconomySettlement replayTo3 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 3, movesUsed: 8, usedAnySkill: false, campaignDb);
        Assert.AreEqual(s1Cfg.star3UnlockCoinReward, replayTo3.ReplayStarUpgradeCoinsGranted);
    }

    [Test]
    public void ReplayJumpFromZeroStarsToThreeStars_GrantsExactNewlyUnlockedStarTiersOnce()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        economy.LoadFromProfile();
        SectorEconomyRewardConfig s1Cfg = TestBalance.GetSectorConfig(1);

        // First clear with 0 stars collected (Decision 3: 0..3 stars supported)
        LevelEconomySettlement first0 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 0, movesUsed: 15, usedAnySkill: false, campaignDb);
        Assert.AreEqual(s1Cfg.firstClearCoinReward, first0.TotalCoinsGranted);

        // Jump straight 0 -> 2 stars on replay -> grants star_1 + star_2
        LevelEconomySettlement jump0To2 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 2, movesUsed: 11, usedAnySkill: false, campaignDb);
        Assert.AreEqual(2, jump0To2.NewlyUnlockedStarsCount);
        Assert.AreEqual(s1Cfg.star1UnlockCoinReward + s1Cfg.star2UnlockCoinReward, jump0To2.ReplayStarUpgradeCoinsGranted);

        // Jump 2 -> 3 stars -> grants ONLY star_3
        LevelEconomySettlement jump2To3 = CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 3, movesUsed: 9, usedAnySkill: false, campaignDb);
        Assert.AreEqual(1, jump2To3.NewlyUnlockedStarsCount);
        Assert.AreEqual(s1Cfg.star3UnlockCoinReward, jump2To3.ReplayStarUpgradeCoinsGranted);
    }

    [Test]
    public void SectorCompletion_GrantsCoinAndGemMilestoneOnce_AndSequentialSectorsUnlockWithZeroPaywall()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        economy.LoadFromProfile();
        var progression = SpawnSingleton<ProgressionManager>("Progression");
        progression.SetDatabase(campaignDb);

        Assert.AreEqual(1, progression.PlayerLevel);
        Assert.AreEqual(0, progression.TotalExp);
        Assert.IsTrue(progression.IsWorldUnlocked(campaignDb.Worlds[0]));
        Assert.IsFalse(progression.IsWorldUnlocked(campaignDb.Worlds[1]), "Sector 2 locked before Sector 1 complete.");

        // Complete S1_01
        CampaignEconomyService.SettleLevelCompletion(s1L1, earnedStars: 2, movesUsed: 6, usedAnySkill: false, campaignDb);
        Assert.IsFalse(progression.IsWorldUnlocked(campaignDb.Worlds[1]));

        // Complete S1_02 -> finishes Sector 1!
        SectorEconomyRewardConfig s1Cfg = TestBalance.GetSectorConfig(1);
        LevelEconomySettlement s1Finish = CampaignEconomyService.SettleLevelCompletion(s1L2, earnedStars: 3, movesUsed: 7, usedAnySkill: false, campaignDb);
        Assert.IsTrue(s1Finish.SectorCompletedThisRun);
        Assert.AreEqual(s1Cfg.sectorCompletionCoinReward, s1Finish.SectorCompletionCoinsGranted);
        Assert.AreEqual(s1Cfg.sectorCompletionGemReward, s1Finish.SectorCompletionGemsGranted);
        Assert.AreEqual(s1Cfg.sectorCompletionGemReward, economy.Gems);

        // Sector 2 unlocks immediately despite PlayerLevel=1 & 0 EXP (No-Paywall GDD §2.1)
        Assert.IsTrue(progression.IsWorldUnlocked(campaignDb.Worlds[1]));
        Assert.IsFalse(progression.IsWorldUnlocked(campaignDb.Worlds[2]));

        // Replaying S1_02 NEVER grants Sector 1 milestone again (Case #6)
        int gemsBeforeReplay = economy.Gems;
        int coinsBeforeReplay = economy.Coins;
        LevelEconomySettlement replayS1Finish = CampaignEconomyService.SettleLevelCompletion(s1L2, earnedStars: 3, movesUsed: 7, usedAnySkill: false, campaignDb);
        Assert.IsFalse(replayS1Finish.SectorCompletedThisRun);
        Assert.AreEqual(gemsBeforeReplay, economy.Gems);
        Assert.AreEqual(coinsBeforeReplay, economy.Coins);

        // Complete Sector 2 & Sector 3 without watching any ad, making any IAP, or using any skill
        CampaignEconomyService.SettleLevelCompletion(s2L1, earnedStars: 3, movesUsed: 8, usedAnySkill: false, campaignDb);
        Assert.IsTrue(progression.IsWorldUnlocked(campaignDb.Worlds[2]));

        LevelEconomySettlement finalClear = CampaignEconomyService.SettleLevelCompletion(s3L1, earnedStars: 3, movesUsed: 10, usedAnySkill: false, campaignDb);
        Assert.IsTrue(finalClear.CampaignCompletedThisRun);

        ProfileData profile = SaveManager.loadProfile();
        Assert.IsFalse(profile.hasWatchedAnyAdInCampaign);
        Assert.IsFalse(profile.hasMadeAnyIapInCampaign);
        // 5 valid no-skill clears in this test: s1L1, s1L2, s1L2 replay, s2L1, s3L1
        Assert.AreEqual(5, profile.lifetimeNoSkillLevelClears);
    }

    [Test]
    public void InLevelCoinCollectible_DoesNotGrantDirectWalletCoins_PreventingRestartFarming()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        economy.LoadFromProfile();
        Assert.IsFalse(EconomyBalanceConfig.Active.GrantWalletCoinsFromInLevelPickups);

        GameObject coinObj = CreateTestGameObject("InLevelCoin");
        coinObj.AddComponent<CircleCollider2D>().isTrigger = true;
        var collectible = coinObj.AddComponent<Collectible>();

        GameObject playerObj = CreateTestGameObject("Player");
        playerObj.tag = "Player";
        var playerCol = playerObj.AddComponent<CircleCollider2D>();

        // Invoke OnTriggerEnter2D via reflection to simulate touching Coin.prefab then restarting
        var triggerMethod = typeof(Collectible).GetMethod("OnTriggerEnter2D", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.IsNotNull(triggerMethod);
        triggerMethod.Invoke(collectible, new object[] { playerCol });

        Assert.AreEqual(0, economy.Coins, "In-level coin pickups must not inflate wallet balance directly!");
    }
}
