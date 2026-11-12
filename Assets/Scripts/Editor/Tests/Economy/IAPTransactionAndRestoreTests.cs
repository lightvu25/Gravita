using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase E7 Verification (GDD §9, §12, & §14.2 Cases #19–#21):
///   - Gem Pack grants Base + Bonus Gems once; duplicate store <c>TransactionId</c> is rejected idempotently.
///   - Starter Pack is strictly one-time per profile.
///   - RestorePurchases on a fresh install restores Non-Consumables (<c>Remove Ads</c>, permanent <c>PlayerSkin</c>)
///     WITHOUT re-granting consumable Gems/Coins from <c>Starter Pack</c> or <c>Gem Pack</c>.
/// </summary>
public class IAPTransactionAndRestoreTests : EconomyTestHarnessBase
{
    private EconomyManager economy;
    private ShopManager shop;
    private IAPManager iap;
    private MockIAPService mockStore;
    private ShopDatabase shopDb;

    private ShopItemDefinition gemPack2;
    private ShopItemDefinition starterPack;
    private ShopItemDefinition removeAdsItem;
    private ShopItemDefinition premiumSkinIap;

    public override void BaseSetUp()
    {
        base.BaseSetUp();

        economy = SpawnSingleton<EconomyManager>("Economy");
        shop = SpawnSingleton<ShopManager>("Shop");
        var iapGo = CreateTestGameObject("IAP");
        mockStore = iapGo.AddComponent<MockIAPService>();
        iap = iapGo.AddComponent<IAPManager>();
        iap.SetIAPService(mockStore);

        gemPack2 = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        gemPack2.Configure(
            "shop_gem_2", "Gem Crate", "",
            ShopItemCategory.GemPack,
            ShopCostType.RealMoney, 0,
            new[] { new RewardItem(RewardType.Gems, 260) },
            iapProductId: "gem_pack_tier_2",
            productKind: IAPProductTypeKind.Consumable,
            baseGems: 240,
            bonusGems: 20);

        starterPack = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        starterPack.Configure(
            "shop_starter", "Starter Pack", "",
            ShopItemCategory.StarterPack,
            ShopCostType.RealMoney, 0,
            new[]
            {
                new RewardItem(RewardType.Gems, 150),
                new RewardItem(RewardType.Coins, 500),
                new RewardItem(RewardType.Cosmetic, 1, "skin_prism")
            },
            iapProductId: "starter_pack",
            productKind: IAPProductTypeKind.Consumable,
            oneTime: true);

        removeAdsItem = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        removeAdsItem.Configure(
            "shop_remove_ads", "Remove Ads", "",
            ShopItemCategory.RemoveAds,
            ShopCostType.RealMoney, 0,
            null,
            iapProductId: "remove_ads",
            productKind: IAPProductTypeKind.NonConsumable,
            oneTime: true,
            removeAds: true);

        premiumSkinIap = TrackAsset(ScriptableObject.CreateInstance<ShopItemDefinition>());
        premiumSkinIap.Configure(
            "shop_iap_skin_gold", "Gold Chassis", "",
            ShopItemCategory.Cosmetic,
            ShopCostType.RealMoney, 0,
            new[] { new RewardItem(RewardType.Cosmetic, 1, "skin_gold") },
            iapProductId: "skin_gold_iap",
            productKind: IAPProductTypeKind.NonConsumable,
            oneTime: true);

        shopDb = TrackAsset(ScriptableObject.CreateInstance<ShopDatabase>());
        shopDb.SetItems(new[] { gemPack2, starterPack, removeAdsItem, premiumSkinIap });
        shop.SetDatabase(shopDb);
    }

    [Test]
    public void GemPack_GrantsBasePlusBonusOnce_AndDuplicateTransactionIdCallbackNeverDoubleGrants()
    {
        mockStore.SetCustomTransactionIdForNextPurchase("tx_gem_crate_0001");

        bool ok1 = false;
        shop.PurchaseItem(gemPack2, ok => ok1 = ok);
        Assert.IsTrue(ok1);
        Assert.AreEqual(260, economy.Gems);
        Assert.AreEqual("240 + 20 Bonus Gems", ShopItemCardWidget.FormatContents(gemPack2));

        // Simulate store retrying PendingOrder callback with the EXACT same TransactionId -> idempotent, 0 extra Gems!
        bool savedStatic = IAPManager.ProcessVerifiedStoreOrderStatic("gem_pack_tier_2", "tx_gem_crate_0001", out bool wasAlreadyProcessed);
        Assert.IsTrue(savedStatic);
        Assert.IsTrue(wasAlreadyProcessed);
        Assert.AreEqual(260, economy.Gems, "Duplicate store TransactionId must NEVER grant Gems twice!");
    }

    [Test]
    public void StarterPack_GrantsBundleOnce_AndBlocksSecondPurchase()
    {
        bool firstBuy = false;
        shop.PurchaseItem(starterPack, ok => firstBuy = ok);
        Assert.IsTrue(firstBuy);
        Assert.AreEqual(150, economy.Gems);
        Assert.AreEqual(500, economy.Coins);
        Assert.IsTrue(CosmeticService.IsOwned("skin_prism"));

        // Second attempt to buy Starter Pack -> blocked before billing!
        Assert.IsFalse(shop.EvaluatePurchase(starterPack, out ShopPurchaseBlockReason blockReason));
        Assert.AreEqual(ShopPurchaseBlockReason.AlreadyOwned, blockReason);

        bool secondBuy = true;
        shop.PurchaseItem(starterPack, ok => secondBuy = ok);
        Assert.IsFalse(secondBuy);
        Assert.AreEqual(150, economy.Gems);
        Assert.AreEqual(500, economy.Coins);
    }

    [Test]
    public void RestorePurchases_OnFreshReinstallProfile_RestoresRemoveAdsAndSkin_WithoutDuplicatingConsumableGems()
    {
        // Simulate fresh reinstall: empty profile on disk, while store receipt owns remove_ads, skin_gold_iap, and starter_pack
        mockStore.ClearOwnedStoreProducts();
        mockStore.SeedOwnedStoreProduct("remove_ads");
        mockStore.SeedOwnedStoreProduct("skin_gold_iap");
        mockStore.SeedOwnedStoreProduct("starter_pack");

        Assert.AreEqual(0, economy.Gems);
        Assert.AreEqual(0, economy.Coins);
        Assert.IsFalse(iap.NoAdsPurchased);
        Assert.IsFalse(CosmeticService.IsOwned("skin_gold"));

        bool restoreSucceeded = false;
        int restoredCount = 0;
        iap.RestorePurchases((ok, count) =>
        {
            restoreSucceeded = ok;
            restoredCount = count;
        });

        Assert.IsTrue(restoreSucceeded);
        Assert.GreaterOrEqual(restoredCount, 2);

        // Non-consumable Remove Ads & permanent Cosmetic ARE restored!
        Assert.IsTrue(iap.NoAdsPurchased);
        Assert.IsTrue(AdPolicyCoordinator.IsRemoveAdsActive());
        Assert.IsTrue(CosmeticService.IsOwned("skin_gold"));
        Assert.IsTrue(CosmeticService.IsOwned("skin_prism"), "Permanent cosmetic inside Starter Pack is restored.");

        // Consumable Gems & Coins inside Starter Pack are NEVER re-granted on RestorePurchases!
        Assert.AreEqual(0, economy.Gems, "RestorePurchases must NEVER duplicate consumable Gems!");
        Assert.AreEqual(0, economy.Coins, "RestorePurchases must NEVER duplicate consumable Coins!");
    }
}

