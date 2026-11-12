using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Phase E1 Verification: Atomic <c>.tmp -> .bak -> profile.json</c> saves, crash recovery without wiping IAP/cosmetics,
/// v1 -> v2 schema migration, duplicate <c>RewardId</c>/<c>IapTransactionId</c> rejection, and rollback on save failure.
/// </summary>
public class SaveAndTransactionLedgerTests : EconomyTestHarnessBase
{
    [Test]
    public void AtomicSave_CreatesBackupAndRecoversCorruptedPrimaryWithoutWipingPurchases()
    {
        ProfileData p = SaveManager.loadProfile();
        p.coins = 750;
        p.gems = 45;
        p.noAdsPurchased = true;
        p.ownedCosmeticIds.Add("skin_cobalt");
        p.equippedPlayerSkinId = "skin_cobalt";
        Assert.IsTrue(SaveManager.TrySaveProfile(p));

        // Second save triggers copy of known-good primary to profile.json.bak
        p.coins = 820;
        Assert.IsTrue(SaveManager.TrySaveProfile(p));
        Assert.IsTrue(File.Exists(SaveManager.BackupProfileFilePath), "Expected profile.json.bak to exist.");

        // Simulate crash corrupting profile.json mid-write
        File.WriteAllText(SaveManager.ProfileFilePath, "{corrupted_truncated_json");
        SaveManager.ClearMemoryCacheForTesting();

        ProfileData recovered = SaveManager.ReloadProfileFromDisk();
        Assert.IsNotNull(recovered);
        Assert.IsTrue(recovered.noAdsPurchased, "Corrupted primary save must NOT wipe Remove Ads entitlement!");
        CollectionAssert.Contains(recovered.ownedCosmeticIds, "skin_cobalt");
        Assert.GreaterOrEqual(recovered.coins, 750);
    }

    [Test]
    public void LegacySchemaV1_MigratesToV2_AndBackfillsClaimedLevelAndStarRewardIds()
    {
        string legacyJson = @"{
            ""saveSchemaVersion"": 1,
            ""coins"": 120,
            ""gems"": 5,
            ""completedLevelIDs"": [""001_Tutorial""],
            ""levelStarKeys"": [""001_Tutorial""],
            ""levelStarValues"": [2]
        }";

        File.WriteAllText(SaveManager.ProfileFilePath, legacyJson);
        SaveManager.ClearMemoryCacheForTesting();

        ProfileData migrated = SaveManager.ReloadProfileFromDisk();
        Assert.AreEqual(ProfileData.CurrentSaveSchemaVersion, migrated.saveSchemaVersion);
        Assert.IsTrue(migrated.HasClaimedReward(EconomyRewardIds.LevelFirstClear("001_Tutorial")));
        Assert.IsTrue(migrated.HasClaimedReward(EconomyRewardIds.LevelStarUnlock("001_Tutorial", 1)));
        Assert.IsTrue(migrated.HasClaimedReward(EconomyRewardIds.LevelStarUnlock("001_Tutorial", 2)));
        Assert.IsFalse(migrated.HasClaimedReward(EconomyRewardIds.LevelStarUnlock("001_Tutorial", 3)));
        Assert.AreEqual(4, migrated.loadoutSlots.Count);
    }

    [Test]
    public void ExecuteTransaction_EnforcesUniqueRewardId_AndUniqueIapTransactionId()
    {
        var economy = SpawnSingleton<EconomyManager>("TestEconomy");
        economy.LoadFromProfile();

        var bundle = new RewardBundle();
        bundle.Add(RewardType.Coins, 100);

        var tx1 = new RewardTransaction
        {
            RewardId = "test_once_reward_01",
            RequireUniqueRewardId = true,
            Payout = bundle,
            Reason = "UnitTest"
        };

        RewardTransactionResult res1 = RewardService.ExecuteTransaction(tx1);
        Assert.IsTrue(res1.Succeeded);
        Assert.AreEqual(100, economy.Coins);

        // Re-submitting identical RewardId is rejected idempotently
        RewardTransactionResult resDup = RewardService.ExecuteTransaction(tx1);
        Assert.IsFalse(resDup.Succeeded);
        Assert.AreEqual(RewardTransactionStatus.DuplicateRewardId, resDup.Status);
        Assert.AreEqual(100, economy.Coins);

        // IAP TransactionId deduplication
        var iapBundle = new RewardBundle();
        iapBundle.Add(RewardType.Gems, 80);
        var iapTx = new RewardTransaction
        {
            IapTransactionId = "gpa.3391-0001",
            Payout = iapBundle,
            Reason = "IAP_GemPack"
        };

        Assert.IsTrue(RewardService.ExecuteTransaction(iapTx).Succeeded);
        Assert.AreEqual(80, economy.Gems);

        RewardTransactionResult dupIap = RewardService.ExecuteTransaction(iapTx);
        Assert.IsFalse(dupIap.Succeeded);
        Assert.AreEqual(RewardTransactionStatus.DuplicateIapTransaction, dupIap.Status);
        Assert.AreEqual(80, economy.Gems);
    }

    [Test]
    public void ExecuteTransaction_RollsBackInMemoryState_WhenDiskSaveFails()
    {
        var economy = SpawnSingleton<EconomyManager>("TestEconomy");
        economy.LoadFromProfile();

        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 200);
        Assert.IsTrue(RewardService.ExecuteTransaction(new RewardTransaction
        {
            RequireUniqueRewardId = false,
            Payout = seed,
            Reason = "Seed"
        }).Succeeded);
        Assert.AreEqual(200, economy.Coins);

        SaveManager.SimulateSaveFailureForTesting = true;
        LogAssert.Expect(LogType.Error, "[SaveManager] Simulated save failure for testing.");

        var purchasePayout = new RewardBundle();
        purchasePayout.Add(RewardType.Cosmetic, 1, "skin_cobalt");

        RewardTransactionResult failedRes = RewardService.ExecuteTransaction(new RewardTransaction
        {
            RewardId = "purchase_skin_cobalt",
            RequireUniqueRewardId = true,
            CostType = ShopCostType.Coins,
            CostAmount = 150,
            Payout = purchasePayout,
            Reason = "DiskFailTest"
        });

        Assert.IsFalse(failedRes.Succeeded);
        Assert.AreEqual(RewardTransactionStatus.SaveFailed, failedRes.Status);
        Assert.AreEqual(200, economy.Coins, "Wallet balance must roll back completely on save failure!");
        Assert.IsFalse(CosmeticService.IsOwned("skin_cobalt"), "Cosmetic must not remain unlocked in memory on save failure!");
        Assert.IsFalse(SaveManager.loadProfile().HasClaimedReward("purchase_skin_cobalt"));
    }
}
