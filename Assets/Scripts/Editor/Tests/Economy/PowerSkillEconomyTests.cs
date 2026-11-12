using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase E3 Verification (GDD §14.2 Cases #7–#13):
///   - Tutorial initial skill grant is 1x only per skill.
///   - Skill inventory caps: Breaker=12, Freeze=12, Teleport=8.
///   - Shop blocks single-charge & bundle purchases when <c>current + bundle &gt; cap</c>.
///   - Shop rejects single-charge Gem offers (<c>InvalidCurrencyRule</c>).
///   - 4-slot Loadout allows empty 4th slot, blocks duplicates, and locks during an active level.
///   - PowerUpController blocks unequipped skills, consumes 0 charges on invalid target, and consumes 1 on valid activation without refund on restart.
/// </summary>
public class PowerSkillEconomyTests : EconomyTestHarnessBase
{
    [Test]
    public void TutorialSkillGrant_GrantsInitialChargesOnce_AndNeverDuplicatesOnReplay()
    {
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        RewardTransactionResult first = progression.TryGrantTutorialSkillCharges(PowerUpIds.Breaker);
        Assert.IsTrue(first.Succeeded);
        int expectedInitial = TestBalance.GetInitialTutorialCharges(PowerUpIds.Breaker);
        Assert.AreEqual(expectedInitial, progression.GetPowerUpCount(PowerUpIds.Breaker));

        RewardTransactionResult second = progression.TryGrantTutorialSkillCharges(PowerUpIds.Breaker);
        Assert.IsFalse(second.Succeeded);
        Assert.AreEqual(RewardTransactionStatus.DuplicateRewardId, second.Status);
        Assert.AreEqual(expectedInitial, progression.GetPowerUpCount(PowerUpIds.Breaker));
    }

    [Test]
    public void SkillInventoryCaps_Enforce12_12_8_AndBlockSingleAndBundleOverflowsAndSingleChargeGemOffers()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var progression = SpawnSingleton<ProgressionManager>("Progression");
        var shop = SpawnSingleton<ShopManager>("Shop");

        // Seed generous currency
        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 10000);
        seed.Add(RewardType.Gems, 1000);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "TestSeed" });

        Assert.AreEqual(12, progression.GetPowerUpCap(PowerUpIds.Breaker));
        Assert.AreEqual(12, progression.GetPowerUpCap(PowerUpIds.Freeze));
        Assert.AreEqual(8, progression.GetPowerUpCap(PowerUpIds.Teleport));

        // Invalid offer: 1 single charge priced in Gems -> must be blocked per GDD §5.3!
        var invalidSingleGemOffer = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        invalidSingleGemOffer.Configure(
            "bad_gem_single", "Bad Offer", "",
            ShopItemCategory.SkillChargeSingle,
            ShopCostType.Gems, 10,
            new[] { new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Teleport) });

        Assert.IsFalse(shop.EvaluatePurchase(invalidSingleGemOffer, out ShopPurchaseBlockReason badReason));
        Assert.AreEqual(ShopPurchaseBlockReason.InvalidCurrencyRule, badReason);

        // Valid Coin single charge for Teleport (cap 8)
        var teleportSingleCoin = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        teleportSingleCoin.Configure(
            "teleport_1", "Teleport +1", "",
            ShopItemCategory.SkillChargeSingle,
            ShopCostType.Coins, 100,
            new[] { new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Teleport) });

        // Fill Teleport to 7/8
        progression.AddPowerUp(PowerUpIds.Teleport, 7);
        Assert.AreEqual(7, progression.GetPowerUpCount(PowerUpIds.Teleport));

        // Gem bundle containing +2 Teleport when at 7/8 -> 7 + 2 = 9 > 8 -> MUST BLOCK!
        var teleportGemBundle2 = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        teleportGemBundle2.Configure(
            "teleport_bundle_2", "Teleport x2", "",
            ShopItemCategory.SkillChargeBundle,
            ShopCostType.Gems, 30,
            new[] { new RewardItem(RewardType.PowerUp, 2, PowerUpIds.Teleport) });

        Assert.IsFalse(shop.EvaluatePurchase(teleportGemBundle2, out ShopPurchaseBlockReason bundleBlock));
        Assert.AreEqual(ShopPurchaseBlockReason.SkillCapExceeded, bundleBlock);

        // Single Coin charge 7 -> 8 succeeds!
        bool bought8th = false;
        shop.PurchaseItem(teleportSingleCoin, ok => bought8th = ok);
        Assert.IsTrue(bought8th);
        Assert.AreEqual(8, progression.GetPowerUpCount(PowerUpIds.Teleport));

        // Buying at 8/8 is blocked with SkillCapExceeded and spends 0 Coins!
        int coinsAtCap = economy.Coins;
        bool bought9th = true;
        shop.PurchaseItem(teleportSingleCoin, ok => bought9th = ok);
        Assert.IsFalse(bought9th);
        Assert.AreEqual(8, progression.GetPowerUpCount(PowerUpIds.Teleport));
        Assert.AreEqual(coinsAtCap, economy.Coins);
    }

    [Test]
    public void FourSlotLoadout_AllowsEmptyFourthSlot_BlocksDuplicateSkills_AndLocksDuringActiveRun()
    {
        var progression = SpawnSingleton<ProgressionManager>("Progression");

        // Valid 3 skills + empty 4th slot
        Assert.IsTrue(progression.TrySetLoadout(new[] { PowerUpIds.Breaker, PowerUpIds.Freeze, PowerUpIds.Teleport, "" }));
        Assert.IsTrue(progression.IsSkillEquippedInLoadout(PowerUpIds.Breaker));
        Assert.IsTrue(string.IsNullOrEmpty(progression.GetLoadoutSlots()[3]));

        // Duplicate skill across slots -> rejected!
        Assert.IsFalse(progression.TrySetLoadout(new[] { PowerUpIds.Breaker, PowerUpIds.Breaker, "", "" }));

        // Dormant skill (Shield) -> rejected!
        Assert.IsFalse(progression.TrySetLoadout(new[] { PowerUpIds.Shield, "", "", "" }));

        // Lock loadout for active run -> modifications blocked until run ends!
        progression.LockLoadoutForActiveRun();
        Assert.IsTrue(progression.IsLoadoutLocked);
        Assert.IsFalse(progression.TrySetLoadout(new[] { PowerUpIds.Breaker, "", "", "" }));
        Assert.IsFalse(progression.TryAssignLoadoutSlot(0, ""));

        progression.UnlockLoadoutAfterRun();
        Assert.IsFalse(progression.IsLoadoutLocked);
        Assert.IsTrue(progression.TrySetLoadout(new[] { PowerUpIds.Breaker, "", "", "" }));
        Assert.IsFalse(progression.IsSkillEquippedInLoadout(PowerUpIds.Teleport));
    }

    [Test]
    public void PowerUpController_EnforcesLoadout_ZeroDeductionOnInvalidTarget_AndNoRefundAfterRestart()
    {
        var progression = SpawnSingleton<ProgressionManager>("Progression");
        progression.AddPowerUp(PowerUpIds.Breaker, 3);
        progression.AddPowerUp(PowerUpIds.Teleport, 2);

        // Equip ONLY Breaker in loadout (leave Teleport unequipped)
        Assert.IsTrue(progression.TrySetLoadout(new[] { PowerUpIds.Breaker, "", "", "" }));
        progression.LockLoadoutForActiveRun();

        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;

        var flowGo = CreateTestGameObject("Flow");
        var flow = flowGo.AddComponent<LevelFlowController>();
        // Put flow in Playing state via its backing field for lightweight EditMode check
        typeof(LevelFlowController)
            .GetField("currentState", flags)
            ?.SetValue(flow, LevelState.Playing);

        var playerGo = CreateTestGameObject("Player");
        playerGo.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
        playerGo.AddComponent<BoxCollider2D>();
        var playerMove = playerGo.AddComponent<PlayerMovement>();
        InvokeLifecycleIfPresent(playerMove, "Awake");
        // Establish cardinal facing direction Up = (0, 1)
        typeof(PlayerMovement)
            .GetField("lastMoveDirection", flags)
            ?.SetValue(playerMove, Vector2.up);

        var puGo = CreateTestGameObject("PowerUpCtrl");
        var puCtrl = puGo.AddComponent<PowerUpController>();
        InvokeLifecycleIfPresent(puCtrl, "Awake");

        // Wire flowController & playerMovement into PowerUpController
        typeof(PowerUpController).GetField("flowController", flags)?.SetValue(puCtrl, flow);
        typeof(PowerUpController).GetField("playerMovement", flags)?.SetValue(puCtrl, playerMove);

        // 1. Teleport is NOT in loadout -> blocked with NotInLoadout, 0 charges lost!
        string lastReason = null;
        void CaptureReason(PowerUpType t, bool ok, string reason) => lastReason = reason;
        PowerUpController.OnSkillActivationAttempted += CaptureReason;
        try
        {
            Assert.IsFalse(puCtrl.CanActivate(PowerUpType.Teleport));
            Assert.IsFalse(puCtrl.TryActivate(PowerUpType.Teleport));
            Assert.AreEqual("NotInLoadout", lastReason);
            Assert.AreEqual(2, progression.GetPowerUpCount(PowerUpIds.Teleport));

            // 2. Breaker IS in loadout, but there is NO CrackedBlock / trap ahead -> PreconditionFailed, 0 charges lost!
            Assert.IsFalse(puCtrl.TryActivate(PowerUpType.Breaker));
            Assert.AreEqual(3, progression.GetPowerUpCount(PowerUpIds.Breaker), "Invalid target must NEVER consume a charge!");

            // 3. Place a CrackedBlock 1 cell ahead (default facing is Up = (0, 1)) -> valid activation consumes 1 charge!
            var wallGo = CreateTestGameObject("CrackedWallAhead");
            wallGo.transform.position = new Vector3(0f, 1f, 0f);
            wallGo.AddComponent<BoxCollider2D>();
            wallGo.AddComponent<CrackedBlock>();
            Physics2D.SyncTransforms();

            Assert.IsTrue(puCtrl.TryActivate(PowerUpType.Breaker));
            Assert.AreEqual(2, progression.GetPowerUpCount(PowerUpIds.Breaker), "Valid activation must deduct exactly 1 charge.");

            // 4. Simulate reset/death -> consumed charge is NEVER refunded (GDD §5.4)
            puCtrl.ClearAllActiveEffects(resetLevelUsageCounts: true);
            Assert.AreEqual(2, progression.GetPowerUpCount(PowerUpIds.Breaker));
        }
        finally
        {
            PowerUpController.OnSkillActivationAttempted -= CaptureReason;
        }
    }
}
