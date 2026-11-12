#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Non-destructive Editor setup & repair utility for Gravita Economy System v1.0.
/// Creates/updates:
///   1. <c>Assets/Resources/EconomyBalanceConfig.asset</c> (centralized GDD §14 balance config)
///   2. <c>Assets/Data/PowerUps/*.asset</c> (marks Breaker=12, Freeze=12, Teleport=8 active; Shield/Magnet/Echo dormant, preserving DisplayName & Icon!)
///   3. <c>Assets/Resources/CosmeticDatabase_Main.asset</c> + <c>Assets/Data/Cosmetics/*.asset</c> (tint-only PlayerSkin definitions, zero placeholder sprites)
///   4. <c>Assets/Data/Shop/ShopDatabase_Main.asset</c> + <c>Assets/Data/Shop/Items/*.asset</c> (replaces broken missing GUIDs with valid GDD Shop & IAP items)
///   5. <c>Assets/Data/Missions/MissionDatabase_Main.asset</c> (aligns Daily/Sector/Achievement missions with 3 active skills)
/// </summary>
public static class EconomySetupAndDataTool
{
    public const string BalanceConfigAssetPath = "Assets/Resources/EconomyBalanceConfig.asset";
    public const string CosmeticDatabaseAssetPath = "Assets/Resources/CosmeticDatabase_Main.asset";
    public const string CosmeticsFolderPath = "Assets/Data/Cosmetics";
    public const string ShopDatabaseAssetPath = "Assets/Data/Shop/ShopDatabase_Main.asset";
    public const string ShopItemsFolderPath = "Assets/Data/Shop/Items";

    [MenuItem("Tools/Gravita/Economy/Setup & Repair Economy Data Assets")]
    public static void RunFullEconomyAssetSetup()
    {
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Data");
        EnsureFolder(CosmeticsFolderPath);
        EnsureFolder("Assets/Data/Shop");
        EnsureFolder(ShopItemsFolderPath);

        // 1. EconomyBalanceConfig in Resources
        EconomyBalanceConfig balance = AssetDatabase.LoadAssetAtPath<EconomyBalanceConfig>(BalanceConfigAssetPath);
        if (balance == null)
        {
            balance = ScriptableObject.CreateInstance<EconomyBalanceConfig>();
            AssetDatabase.CreateAsset(balance, BalanceConfigAssetPath);
        }
        else
        {
            EditorUtility.SetDirty(balance);
        }
        EconomyBalanceConfig.SetActiveConfig(balance);

        // 2. Update PowerUpData economy flags WITHOUT altering DisplayName or Icon
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Breaker.asset",  campaignActive: true,  cap: 12, tutorialCharges: 2);
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Freeze.asset",   campaignActive: true,  cap: 12, tutorialCharges: 2);
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Teleport.asset", campaignActive: true,  cap: 8,  tutorialCharges: 2);
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Shield.asset",   campaignActive: false, cap: 12, tutorialCharges: 0);
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Magnet.asset",   campaignActive: false, cap: 12, tutorialCharges: 0);
        UpdatePowerUpEconomyFlag("Assets/Data/PowerUps/Echo.asset",     campaignActive: false, cap: 12, tutorialCharges: 0);

        // 3. Cosmetics (PlayerSkin only for v1.0, pure tint overrides — NO placeholder sprites)
        var cosmetics = new List<CosmeticDefinition>
        {
            CreateOrUpdateSkin("Skin_Cobalt.asset", "skin_cobalt", "Cobalt Core", "Cool cobalt chassis tint for Subject G-0.", new Color(0.35f, 0.78f, 1.00f, 1f)),
            CreateOrUpdateSkin("Skin_Amber.asset",  "skin_amber",  "Solar Amber", "Warm amber chassis tint for Subject G-0.",  new Color(1.00f, 0.76f, 0.28f, 1f)),
            CreateOrUpdateSkin("Skin_Prism.asset",  "skin_prism",  "Prism Violet", "Starter Pack exclusive violet tint for Subject G-0.", new Color(0.82f, 0.52f, 1.00f, 1f))
        };

        CosmeticDatabase cosmeticDb = AssetDatabase.LoadAssetAtPath<CosmeticDatabase>(CosmeticDatabaseAssetPath);
        if (cosmeticDb == null)
        {
            cosmeticDb = ScriptableObject.CreateInstance<CosmeticDatabase>();
            cosmeticDb.SetCosmetics(cosmetics);
            AssetDatabase.CreateAsset(cosmeticDb, CosmeticDatabaseAssetPath);
        }
        else
        {
            cosmeticDb.SetCosmetics(cosmetics);
            EditorUtility.SetDirty(cosmeticDb);
        }

        // 4. Repair ShopDatabase_Main & create clean GDD-compliant ShopItemDefinition assets
        PowerUpData breakerAsset = AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Breaker.asset");
        PowerUpData freezeAsset = AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Freeze.asset");
        PowerUpData teleportAsset = AssetDatabase.LoadAssetAtPath<PowerUpData>("Assets/Data/PowerUps/Teleport.asset");

        var shopItems = new List<ShopItemDefinition>
        {
            // Single Skill Charges — Coins ONLY (GDD §5.3)
            CreateOrUpdateShopItem(
                "ShopItem_Charge_Breaker.asset",
                "shop_charge_breaker_1",
                "Breaker Charge",
                "Adds +1 Breaker charge (Max Cap: 12).",
                ShopItemCategory.SkillChargeSingle,
                ShopCostType.Coins,
                150,
                new[] { new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Breaker) },
                itemIcon: breakerAsset != null ? breakerAsset.Icon : null),

            CreateOrUpdateShopItem(
                "ShopItem_Charge_Freeze.asset",
                "shop_charge_freeze_1",
                "Freeze Charge",
                "Adds +1 Freeze charge (Max Cap: 12).",
                ShopItemCategory.SkillChargeSingle,
                ShopCostType.Coins,
                150,
                new[] { new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Freeze) },
                itemIcon: freezeAsset != null ? freezeAsset.Icon : null),

            CreateOrUpdateShopItem(
                "ShopItem_Charge_Teleport.asset",
                "shop_charge_teleport_1",
                "Teleport Charge",
                "Adds +1 Teleport charge (Max Cap: 8).",
                ShopItemCategory.SkillChargeSingle,
                ShopCostType.Coins,
                200,
                new[] { new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Teleport) },
                itemIcon: teleportAsset != null ? teleportAsset.Icon : null),

            // Skill Charge Bundle — Gems ONLY for multi-unit bundles (GDD §5.3)
            CreateOrUpdateShopItem(
                "ShopItem_Bundle_TacticalPack.asset",
                "shop_bundle_tactical_3",
                "Tactical Skill Pack",
                "Bundle of 1x Breaker, 1x Freeze, and 1x Teleport.",
                ShopItemCategory.SkillChargeBundle,
                ShopCostType.Gems,
                45,
                new[]
                {
                    new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Breaker),
                    new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Freeze),
                    new RewardItem(RewardType.PowerUp, 1, PowerUpIds.Teleport)
                }),

            // Permanent PlayerSkin Cosmetics (GDD §6)
            CreateOrUpdateShopItem(
                "ShopItem_Skin_Cobalt.asset",
                "shop_skin_cobalt",
                "Cobalt Core Skin",
                "Permanent Subject G-0 Player Skin.",
                ShopItemCategory.Cosmetic,
                ShopCostType.Coins,
                600,
                new[] { new RewardItem(RewardType.Cosmetic, 1, "skin_cobalt") },
                oneTime: true),

            CreateOrUpdateShopItem(
                "ShopItem_Skin_Amber.asset",
                "shop_skin_amber",
                "Solar Amber Skin",
                "Permanent Subject G-0 Player Skin.",
                ShopItemCategory.Cosmetic,
                ShopCostType.Gems,
                80,
                new[] { new RewardItem(RewardType.Cosmetic, 1, "skin_amber") },
                oneTime: true),

            // IAP Starter Pack — One-time purchase (GDD §9.2)
            CreateOrUpdateShopItem(
                "ShopItem_IAP_StarterPack.asset",
                "shop_iap_starter_pack",
                "Starter Pack",
                "One-time bundle: Gems, Coins, and exclusive Prism Violet Skin!",
                ShopItemCategory.StarterPack,
                ShopCostType.RealMoney,
                0,
                new[]
                {
                    new RewardItem(RewardType.Gems, 150),
                    new RewardItem(RewardType.Coins, 500),
                    new RewardItem(RewardType.Cosmetic, 1, "skin_prism")
                },
                iapProductId: "starter_pack",
                productKind: IAPProductTypeKind.Consumable,
                oneTime: true),

            // IAP Remove Ads — Non-Consumable, disables Interstitials permanently (GDD §8.5 & §9.2)
            CreateOrUpdateShopItem(
                "ShopItem_IAP_RemoveAds.asset",
                "shop_iap_remove_ads",
                "Remove Ads",
                "Permanently removes all Interstitial Ads. Opt-in Rewarded Ads remain available.",
                ShopItemCategory.RemoveAds,
                ShopCostType.RealMoney,
                0,
                null,
                iapProductId: "remove_ads",
                productKind: IAPProductTypeKind.NonConsumable,
                oneTime: true,
                removeAds: true),

            // 4 IAP Gem Pack Tiers — Base + Bonus Gems (GDD §9.1)
            CreateOrUpdateShopItem(
                "ShopItem_IAP_GemPack1.asset",
                "shop_iap_gem_pack_1",
                "Gem Pouch",
                "Small pack of premium Gems.",
                ShopItemCategory.GemPack,
                ShopCostType.RealMoney,
                0,
                new[] { new RewardItem(RewardType.Gems, 80) },
                iapProductId: "gem_pack_tier_1",
                productKind: IAPProductTypeKind.Consumable,
                baseGems: 80,
                bonusGems: 0),

            CreateOrUpdateShopItem(
                "ShopItem_IAP_GemPack2.asset",
                "shop_iap_gem_pack_2",
                "Gem Crate",
                "Medium pack of Gems with bonus value.",
                ShopItemCategory.GemPack,
                ShopCostType.RealMoney,
                0,
                new[] { new RewardItem(RewardType.Gems, 260) },
                iapProductId: "gem_pack_tier_2",
                productKind: IAPProductTypeKind.Consumable,
                baseGems: 240,
                bonusGems: 20),

            CreateOrUpdateShopItem(
                "ShopItem_IAP_GemPack3.asset",
                "shop_iap_gem_pack_3",
                "Gem Vault",
                "Large pack of Gems with bonus value.",
                ShopItemCategory.GemPack,
                ShopCostType.RealMoney,
                0,
                new[] { new RewardItem(RewardType.Gems, 600) },
                iapProductId: "gem_pack_tier_3",
                productKind: IAPProductTypeKind.Consumable,
                baseGems: 520,
                bonusGems: 80),

            CreateOrUpdateShopItem(
                "ShopItem_IAP_GemPack4.asset",
                "shop_iap_gem_pack_4",
                "Gravity Core Treasury",
                "Best-value treasury of Gems.",
                ShopItemCategory.GemPack,
                ShopCostType.RealMoney,
                0,
                new[] { new RewardItem(RewardType.Gems, 1350) },
                iapProductId: "gem_pack_tier_4",
                productKind: IAPProductTypeKind.Consumable,
                baseGems: 1100,
                bonusGems: 250)
        };

        ShopDatabase shopDb = AssetDatabase.LoadAssetAtPath<ShopDatabase>(ShopDatabaseAssetPath);
        if (shopDb == null)
        {
            shopDb = ScriptableObject.CreateInstance<ShopDatabase>();
            shopDb.SetItems(shopItems);
            AssetDatabase.CreateAsset(shopDb, ShopDatabaseAssetPath);
        }
        else
        {
            shopDb.SetItems(shopItems);
            EditorUtility.SetDirty(shopDb);
        }

        // 5. Align MissionDatabase_Main
        MissionDataEditorTool.GenerateOrUpdateMissionAssets();

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[EconomySetupAndDataTool] Full Economy v1.0 data setup & asset repair completed cleanly.");
    }

    private static void UpdatePowerUpEconomyFlag(string assetPath, bool campaignActive, int cap, int tutorialCharges)
    {
        PowerUpData data = AssetDatabase.LoadAssetAtPath<PowerUpData>(assetPath);
        if (data == null)
            return;

        data.ConfigureEconomyPolicy(campaignActive, cap, tutorialCharges);
        EditorUtility.SetDirty(data);
    }

    private static CosmeticDefinition CreateOrUpdateSkin(string fileName, string id, string title, string desc, Color tint)
    {
        string path = $"{CosmeticsFolderPath}/{fileName}";
        CosmeticDefinition def = AssetDatabase.LoadAssetAtPath<CosmeticDefinition>(path);
        bool isNew = def == null;
        if (isNew)
            def = ScriptableObject.CreateInstance<CosmeticDefinition>();

        def.Configure(
            id: id,
            nameLabel: title,
            desc: desc,
            cosmeticCategory: CosmeticCategory.PlayerSkin,
            defaultUnlocked: false,
            previewIcon: def.Icon,
            skinSprite: def.PlayerSprite,
            tint: tint,
            animOverride: def.AnimatorOverride);

        if (isNew)
            AssetDatabase.CreateAsset(def, path);
        else
            EditorUtility.SetDirty(def);

        return def;
    }

    private static ShopItemDefinition CreateOrUpdateShopItem(
        string fileName,
        string id,
        string title,
        string desc,
        ShopItemCategory category,
        ShopCostType costType,
        int costAmount,
        IEnumerable<RewardItem> rewards,
        string iapProductId = "",
        IAPProductTypeKind productKind = IAPProductTypeKind.Consumable,
        bool oneTime = false,
        bool removeAds = false,
        int baseGems = 0,
        int bonusGems = 0,
        Sprite itemIcon = null)
    {
        string path = $"{ShopItemsFolderPath}/{fileName}";
        ShopItemDefinition def = AssetDatabase.LoadAssetAtPath<ShopItemDefinition>(path);
        bool isNew = def == null;
        if (isNew)
            def = ScriptableObject.CreateInstance<ShopItemDefinition>();

        def.Configure(
            id: id,
            title: title,
            desc: desc,
            itemCategory: category,
            priceType: costType,
            priceAmount: costAmount,
            rewardItems: rewards,
            iapProductId: iapProductId,
            productKind: productKind,
            oneTime: oneTime,
            removeAds: removeAds,
            baseGems: baseGems,
            bonusGems: bonusGems,
            itemIcon: itemIcon != null ? itemIcon : def.Icon);

        if (isNew)
            AssetDatabase.CreateAsset(def, path);
        else
            EditorUtility.SetDirty(def);

        return def;
    }

    private static void EnsureFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
            return;

        string[] parts = folderPath.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = $"{current}/{parts[i]}";
            if (!AssetDatabase.IsValidFolder(next))
                AssetDatabase.CreateFolder(current, parts[i]);
            current = next;
        }
    }
}
#endif

