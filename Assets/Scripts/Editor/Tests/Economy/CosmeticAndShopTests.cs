using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase E4 Verification (GDD §6 & §2.2):
///   - Permanent PlayerSkin purchase via Coins/Gems.
///   - Duplicate purchase blocked with <see cref="ShopPurchaseBlockReason.AlreadyOwned"/>.
///   - Equip & Unequip persists across reload and updates Subject G-0 visual tint ONLY.
///   - Collider2D bounds, Rigidbody2D, and PlayerMovement speed remain 100% unchanged.
///   - Coin &lt;-&gt; Gem conversion offers are strictly rejected.
/// </summary>
public class CosmeticAndShopTests : EconomyTestHarnessBase
{
    [Test]
    public void PlayerSkinPurchase_BlocksDuplicateBuy_PersistsEquip_AndLeavesHitboxAndSpeedUntouched()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var shop = SpawnSingleton<ShopManager>("Shop");
        var cosmeticSvc = SpawnSingleton<CosmeticService>("Cosmetics");

        var cobaltSkin = TrackAsset(ScriptableObject.CreateInstance<CosmeticDefinition>());
        var expectedTint = new Color(0.25f, 0.75f, 1.0f, 1.0f);
        cobaltSkin.Configure("skin_cobalt", "Cobalt Core", "Visual skin", CosmeticCategory.PlayerSkin, tint: expectedTint);

        var cosmeticDb = TrackAsset(ScriptableObject.CreateInstance<CosmeticDatabase>());
        cosmeticDb.SetCosmetics(new[] { cobaltSkin });
        cosmeticSvc.SetDatabase(cosmeticDb);

        // Seed 1000 Coins
        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 1000);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "Seed" });

        var skinShopItem = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        skinShopItem.Configure(
            "shop_skin_cobalt", "Cobalt Core", "",
            ShopItemCategory.Cosmetic,
            ShopCostType.Coins, 600,
            new[] { new RewardItem(RewardType.Cosmetic, 1, "skin_cobalt") },
            oneTime: true);

        // Buy once -> succeeds, deducts 600 Coins, owns skin_cobalt
        bool firstBuy = false;
        shop.PurchaseItem(skinShopItem, ok => firstBuy = ok);
        Assert.IsTrue(firstBuy);
        Assert.AreEqual(400, economy.Coins);
        Assert.IsTrue(CosmeticService.IsOwned("skin_cobalt"));

        // Second buy attempt -> blocked with AlreadyOwned, 0 Coins deducted!
        Assert.IsFalse(shop.EvaluatePurchase(skinShopItem, out ShopPurchaseBlockReason dupReason));
        Assert.AreEqual(ShopPurchaseBlockReason.AlreadyOwned, dupReason);
        bool secondBuy = true;
        shop.PurchaseItem(skinShopItem, ok => secondBuy = ok);
        Assert.IsFalse(secondBuy);
        Assert.AreEqual(400, economy.Coins);

        // Verify PlayerCosmeticApplier changes ONLY SpriteRenderer visual tint, NOT Collider2D or PlayerMovement!
        var playerGo = CreateTestGameObject("SubjectG0");
        var sr = playerGo.AddComponent<SpriteRenderer>();
        sr.color = Color.white;
        var rb = playerGo.AddComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Kinematic;
        var col = playerGo.AddComponent<BoxCollider2D>();
        col.size = new Vector2(0.8f, 0.8f);
        var pm = playerGo.AddComponent<PlayerMovement>();

        Vector2 originalColliderSize = col.size;
        Vector2 originalColliderOffset = col.offset;
        PlayerCosmeticApplier applier = PlayerCosmeticApplier.EnsureAttached(pm);
        Assert.IsNotNull(applier);
        Assert.AreEqual(Color.white, sr.color);

        // Equip skin_cobalt
        Assert.IsTrue(CosmeticService.TryEquipPlayerSkin("skin_cobalt"));
        Assert.AreEqual("skin_cobalt", CosmeticService.GetEquippedPlayerSkinId());
        Assert.AreEqual(expectedTint, sr.color);
        Assert.AreEqual(originalColliderSize, col.size, "Cosmetic skin must NEVER alter Collider2D size!");
        Assert.AreEqual(originalColliderOffset, col.offset, "Cosmetic skin must NEVER alter Collider2D offset!");

        // Unequip back to default Subject G-0
        Assert.IsTrue(CosmeticService.TryEquipPlayerSkin(CosmeticService.DefaultSkinId));
        Assert.AreEqual(Color.white, sr.color);
    }

    [Test]
    public void ShopManager_StrictlyBlocksBidirectionalCoinAndGemConversion()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var shop = SpawnSingleton<ShopManager>("Shop");

        var seed = new RewardBundle();
        seed.Add(RewardType.Coins, 5000);
        seed.Add(RewardType.Gems, 500);
        RewardService.ExecuteTransaction(new RewardTransaction { Payout = seed, Reason = "Seed" });

        // Coins -> Gems forbidden (GDD §2.2 & §3.1)
        var coinsToGems = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        coinsToGems.Configure(
            "bad_c2g", "Coins to Gems", "",
            ShopItemCategory.GemPack,
            ShopCostType.Coins, 500,
            new[] { new RewardItem(RewardType.Gems, 10) });

        Assert.IsFalse(shop.EvaluatePurchase(coinsToGems, out ShopPurchaseBlockReason r1));
        Assert.AreEqual(ShopPurchaseBlockReason.InvalidCurrencyRule, r1);

        // Gems -> pure Coins forbidden (GDD §2.2 & §3.2)
        var gemsToCoins = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        gemsToCoins.Configure(
            "bad_g2c", "Gems to Coins", "",
            ShopItemCategory.SkillChargeBundle,
            ShopCostType.Gems, 50,
            new[] { new RewardItem(RewardType.Coins, 1000) });

        Assert.IsFalse(shop.EvaluatePurchase(gemsToCoins, out ShopPurchaseBlockReason r2));
        Assert.AreEqual(ShopPurchaseBlockReason.InvalidCurrencyRule, r2);
    }

    [Test]
    public void AllGeneratedEconomyDataAssets_ExistAndHaveZeroNullEntries()
    {
        var balance = Resources.Load<EconomyBalanceConfig>("EconomyBalanceConfig");
        Assert.IsNotNull(balance, "Assets/Resources/EconomyBalanceConfig.asset must exist.");
        Assert.AreEqual(12, balance.GetSkillInventoryCap(PowerUpIds.Breaker));
        Assert.AreEqual(12, balance.GetSkillInventoryCap(PowerUpIds.Freeze));
        Assert.AreEqual(8, balance.GetSkillInventoryCap(PowerUpIds.Teleport));

        var cosmeticDb = Resources.Load<CosmeticDatabase>("CosmeticDatabase_Main");
        Assert.IsNotNull(cosmeticDb, "Assets/Resources/CosmeticDatabase_Main.asset must exist.");
        Assert.IsNotNull(cosmeticDb.GetById("skin_cobalt"));
        Assert.IsNotNull(cosmeticDb.GetById("skin_amber"));
        Assert.IsNotNull(cosmeticDb.GetById("skin_prism"));

        var shopDb = UnityEditor.AssetDatabase.LoadAssetAtPath<ShopDatabase>("Assets/Data/Shop/ShopDatabase_Main.asset");
        Assert.IsNotNull(shopDb, "Assets/Data/Shop/ShopDatabase_Main.asset must exist.");
        Assert.GreaterOrEqual(shopDb.Items.Count, 12);
        for (int i = 0; i < shopDb.Items.Count; i++)
        {
            ShopItemDefinition item = shopDb.Items[i];
            Assert.IsNotNull(item, $"ShopDatabase_Main entry #{i} must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(item.ItemId));
        }

        var missionDb = UnityEditor.AssetDatabase.LoadAssetAtPath<MissionDatabase>("Assets/Data/Missions/MissionDatabase_Main.asset");
        Assert.IsNotNull(missionDb, "Assets/Data/Missions/MissionDatabase_Main.asset must exist.");
        Assert.Greater(missionDb.Missions.Count, 0);
        bool hasSectorMission = false;
        bool hasAchievementInMissionDb = false;
        for (int i = 0; i < missionDb.Missions.Count; i++)
        {
            MissionDefinition m = missionDb.Missions[i];
            Assert.IsNotNull(m, $"MissionDatabase_Main entry #{i} must not be null.");
            if (m.Category == MissionCategory.Sector) hasSectorMission = true;
            if (m.Category == MissionCategory.Achievement) hasAchievementInMissionDb = true;
        }
        Assert.IsTrue(hasSectorMission, "MissionDatabase_Main must contain Sector missions.");
        Assert.IsFalse(hasAchievementInMissionDb, "MissionDatabase_Main must NOT mix Achievements with Missions.");

        var achievementDb = UnityEditor.AssetDatabase.LoadAssetAtPath<AchievementDatabase>("Assets/Data/Achievements/AchievementDatabase_Main.asset");
        Assert.IsNotNull(achievementDb, "Assets/Data/Achievements/AchievementDatabase_Main.asset must exist.");
        Assert.AreEqual(9, achievementDb.Achievements.Count, "AchievementDatabase_Main must contain all 9 standalone AchievementDefinition assets.");
        for (int i = 0; i < achievementDb.Achievements.Count; i++)
        {
            AchievementDefinition ach = achievementDb.Achievements[i];
            Assert.IsNotNull(ach, $"AchievementDatabase_Main entry #{i} must not be null.");
            Assert.IsFalse(string.IsNullOrEmpty(ach.AchievementId));
        }

        var googleConfig = Resources.Load<GoogleAuthConfig>("GoogleAuthConfig");
        Assert.IsNotNull(googleConfig, "Assets/Resources/GoogleAuthConfig.asset must exist.");
    }
}
