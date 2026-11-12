using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

public class PowerUpUpgradeTests : EconomyTestHarnessBase
{
    private static void SetPrivateField(object target, string fieldName, object value)
    {
        var field = target.GetType().GetField(
            fieldName,
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Public);
        field?.SetValue(target, value);
    }

    private PowerUpData CreateTestPowerUpData(string id, PowerUpType type, PowerUpStatKind statKind, float baseVal)
    {
        var data = TrackAsset(ScriptableObject.CreateInstance<PowerUpData>());
        data.Initialize(
            id, id, "Test power", type,
            duration: statKind == PowerUpStatKind.DurationSeconds ? baseVal : 0f,
            effectValue: baseVal);

        // Populate 5 levels: L1 cost 0, L2 cost 100, L3 cost 200, L4 cost 350, L5 cost 500
        var steps = new List<PowerUpLevelStep>
        {
            new PowerUpLevelStep { upgradeCostCoins = 0, statValue = baseVal },
            new PowerUpLevelStep { upgradeCostCoins = 100, statValue = baseVal + 1f },
            new PowerUpLevelStep { upgradeCostCoins = 200, statValue = baseVal + 2f },
            new PowerUpLevelStep { upgradeCostCoins = 350, statValue = baseVal + 3f },
            new PowerUpLevelStep { upgradeCostCoins = 500, statValue = baseVal + 4f },
        };

        SetPrivateField(data, "upgradeStat", statKind);
        SetPrivateField(data, "statUnitLabel", statKind == PowerUpStatKind.DurationSeconds ? "sec" : "cells");
        SetPrivateField(data, "levelSteps", steps);
        return data;
    }

    [Test]
    public void TryUpgradePowerUp_DeductsCoins_AndIncreasesLevel()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        // Seed 500 coins
        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 500);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "Seed" });

        var freezeData = CreateTestPowerUpData("freeze", PowerUpType.Freeze, PowerUpStatKind.DurationSeconds, 4f);
        Assert.AreEqual(1, progression.GetPowerUpLevel("freeze"));
        Assert.AreEqual(5, freezeData.MaxLevel);
        Assert.AreEqual(100, freezeData.GetUpgradeCost(1));

        bool eventFired = false;
        string upgradedId = null;
        int upgradedLvl = 0;
        progression.OnPowerUpUpgraded += (id, lvl) =>
        {
            eventFired = true;
            upgradedId = id;
            upgradedLvl = lvl;
        };

        // Upgrade from L1 -> L2 (cost: 100)
        bool success = progression.TryUpgradePowerUp("freeze", freezeData, out string failReason);
        Assert.IsTrue(success, $"Upgrade failed: {failReason}");
        Assert.AreEqual(400, economy.Coins);
        Assert.AreEqual(2, progression.GetPowerUpLevel("freeze"));
        Assert.AreEqual(5f, freezeData.GetDuration(progression.GetPowerUpLevel("freeze")));
        Assert.IsTrue(eventFired);
        Assert.AreEqual("freeze", upgradedId);
        Assert.AreEqual(2, upgradedLvl);
    }

    [Test]
    public void TryUpgradePowerUp_FailsWhenInsufficientCoins()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        // 0 coins
        var freezeData = CreateTestPowerUpData("freeze", PowerUpType.Freeze, PowerUpStatKind.DurationSeconds, 4f);

        bool success = progression.TryUpgradePowerUp("freeze", freezeData, out string failReason);
        Assert.IsFalse(success);
        Assert.AreEqual("Not enough Coins.", failReason);
        Assert.AreEqual(1, progression.GetPowerUpLevel("freeze"));
    }

    [Test]
    public void TryUpgradePowerUp_FailsWhenMaxLevel()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        // Seed generous coins
        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 10000);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "Seed" });

        var breakerData = CreateTestPowerUpData("breaker", PowerUpType.Breaker, PowerUpStatKind.WallCells, 2f);

        // Upgrade 4 times to reach max level 5
        for (int i = 1; i <= 4; i++)
        {
            Assert.IsTrue(progression.TryUpgradePowerUp("breaker", breakerData, out _));
        }

        Assert.AreEqual(5, progression.GetPowerUpLevel("breaker"));
        Assert.AreEqual(-1, breakerData.GetUpgradeCost(5));

        // Attempting to upgrade past max level fails
        bool success = progression.TryUpgradePowerUp("breaker", breakerData, out string failReason);
        Assert.IsFalse(success);
        Assert.AreEqual("Max level.", failReason);
        Assert.AreEqual(5, progression.GetPowerUpLevel("breaker"));
    }

    [Test]
    public void GetPowerUpLevel_SnapshotsDuringRun_IgnoresMidRunUpgrade()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 5000);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "Seed" });

        var teleportData = CreateTestPowerUpData("teleport", PowerUpType.Teleport, PowerUpStatKind.WallCells, 3f);
        Assert.AreEqual(1, progression.GetPowerUpLevel("teleport"));

        // Lock run
        progression.LockLoadoutForActiveRun();
        Assert.IsTrue(progression.IsLoadoutLocked);

        // Upgrade teleport while run is locked
        bool upgraded = progression.TryUpgradePowerUp("teleport", teleportData, out _);
        Assert.IsTrue(upgraded);

        // Run snapshot should STILL return level 1!
        Assert.AreEqual(1, progression.GetPowerUpLevel("teleport"));

        // Unlock after run
        progression.UnlockLoadoutAfterRun();
        Assert.IsFalse(progression.IsLoadoutLocked);

        // Now returns updated level 2!
        Assert.AreEqual(2, progression.GetPowerUpLevel("teleport"));
    }

    [Test]
    public void PowerupPanelUI_EquipAndSwapLogic_WorksCorrectly()
    {
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        var go = CreateTestGameObject("PowerupOverlay");
        var panel = go.AddComponent<PowerupPanelUI>();

        Assert.AreEqual(PowerupPanelUI.PowerupTab.Equip, panel.ActiveTab);

        // Clear all slots first for a clean test
        for (int i = 0; i < 4; i++)
            panel.TryUnequipSlot(i);
        Assert.AreEqual(string.Empty, progression.GetLoadoutSlots()[0]);

        // Tap to equip Breaker -> goes to slot 0
        Assert.IsTrue(panel.TryEquipToFirstEmptySlot(PowerUpIds.Breaker));
        Assert.AreEqual(PowerUpIds.Breaker, progression.GetLoadoutSlots()[0]);

        // Tap to equip Freeze -> goes to slot 1
        Assert.IsTrue(panel.TryEquipToFirstEmptySlot(PowerUpIds.Freeze));
        Assert.AreEqual(PowerUpIds.Freeze, progression.GetLoadoutSlots()[1]);

        // Swap slot 0 and slot 1
        Assert.IsTrue(panel.TrySwapSlots(0, 1));
        Assert.AreEqual(PowerUpIds.Freeze, progression.GetLoadoutSlots()[0]);
        Assert.AreEqual(PowerUpIds.Breaker, progression.GetLoadoutSlots()[1]);

        // Tap Freeze again -> since already equipped, it unequips
        Assert.IsTrue(panel.TryEquipToFirstEmptySlot(PowerUpIds.Freeze));
        Assert.AreEqual(string.Empty, progression.GetLoadoutSlots()[0]);
        Assert.AreEqual(PowerUpIds.Breaker, progression.GetLoadoutSlots()[1]);

        // Unequip slot 1 directly
        Assert.IsTrue(panel.TryUnequipSlot(1));
        Assert.AreEqual(string.Empty, progression.GetLoadoutSlots()[1]);
    }
}
