using System;
using UnityEngine;

/// <summary>
/// Per-sector economy reward configuration (GDD §4.1–§4.3 & §14.1).
/// All numeric values are exposed for designer balance tuning rather than scattered in code.
/// </summary>
[Serializable]
public class SectorEconomyRewardConfig
{
    [Tooltip("1-based Sector index (Sector 1 = 1, Sector 2 = 2, Sector 3 = 3).")]
    [Min(1)]
    public int sectorNumber = 1;

    [Header("Level Clear (One-Time Only) — Pending Balance Tuning")]
    [Tooltip("Coins awarded once when clearing a level in this Sector for the first time.")]
    [Min(0)]
    public int firstClearCoinReward = 50;

    [Header("Replay Star Improvement (One-Time Per Newly Unlocked Star) — Pending Balance Tuning")]
    [Tooltip("Coins awarded on replay when unlocking Star 1 after a 0-star first clear.")]
    [Min(0)]
    public int star1UnlockCoinReward = 10;

    [Tooltip("Coins awarded on replay when unlocking Star 2 for the first time.")]
    [Min(0)]
    public int star2UnlockCoinReward = 20;

    [Tooltip("Coins awarded on replay when unlocking Star 3 for the first time.")]
    [Min(0)]
    public int star3UnlockCoinReward = 30;

    [Header("Sector Completion Milestone (One-Time Only) — Pending Balance Tuning")]
    [Tooltip("Coins awarded once when all levels in this Sector are completed.")]
    [Min(0)]
    public int sectorCompletionCoinReward = 200;

    [Tooltip("Gems awarded once when all levels in this Sector are completed.")]
    [Min(0)]
    public int sectorCompletionGemReward = 10;
}

/// <summary>
/// Per-skill inventory cap and tutorial grant configuration (GDD §5.1–§5.2).
/// </summary>
[Serializable]
public class PowerSkillEconomyConfig
{
    public PowerUpType skillType = PowerUpType.Breaker;
    public string skillId = "breaker";
    public bool enabledInCampaign = true;
    public bool unlockedByDefault = true;

    [Tooltip("Hard inventory cap specified in GDD §5.1 (Breaker=12, Freeze=12, Teleport=8).")]
    [Min(1)]
    public int inventoryCap = 12;

    [Tooltip("One-time initial charges granted when the skill tutorial unlocks (Pending Balance Tuning).")]
    [Min(0)]
    public int initialTutorialCharges = 2;
}

/// <summary>
/// Centralized ScriptableObject holding all economy balance parameters, caps, ad limits,
/// and campaign feature toggles defined in Gravita Economy GDD v1.0.
/// </summary>
[CreateAssetMenu(fileName = "EconomyBalanceConfig", menuName = "Gravita/Economy/Economy Balance Config")]
public class EconomyBalanceConfig : ScriptableObject
{
    public const string DefaultResourcePath = "EconomyBalanceConfig";

    private static EconomyBalanceConfig activeOverride;
    private static EconomyBalanceConfig runtimeFallback;

    /// <summary>
    /// Returns the active <see cref="EconomyBalanceConfig"/> (test/inspector override,
    /// Resources/EconomyBalanceConfig, or an in-memory default baseline).
    /// </summary>
    public static EconomyBalanceConfig Active
    {
        get
        {
            if (activeOverride != null)
                return activeOverride;

            EconomyBalanceConfig loaded = Resources.Load<EconomyBalanceConfig>(DefaultResourcePath);
            if (loaded != null)
            {
                activeOverride = loaded;
                return activeOverride;
            }

            if (runtimeFallback == null)
            {
                runtimeFallback = CreateInstance<EconomyBalanceConfig>();
                runtimeFallback.hideFlags = HideFlags.HideAndDontSave;
                runtimeFallback.ResetToBaselineDefaults();
            }

            return runtimeFallback;
        }
    }

    public static void SetActiveConfig(EconomyBalanceConfig config)
    {
        activeOverride = config;
    }

    [Header("1. Campaign & Sector Rewards (Pending Balance Tuning — GDD §4)")]
    [Tooltip("If true and LevelData.baseCoinReward > 0, uses LevelData.baseCoinReward instead of the Sector first-clear baseline.")]
    [SerializeField] private bool allowPerLevelFirstClearOverride;

    [SerializeField] private SectorEconomyRewardConfig[] sectorRewards =
    {
        new SectorEconomyRewardConfig
        {
            sectorNumber = 1,
            firstClearCoinReward = 50,
            star1UnlockCoinReward = 10,
            star2UnlockCoinReward = 20,
            star3UnlockCoinReward = 30,
            sectorCompletionCoinReward = 200,
            sectorCompletionGemReward = 10
        },
        new SectorEconomyRewardConfig
        {
            sectorNumber = 2,
            firstClearCoinReward = 70,
            star1UnlockCoinReward = 15,
            star2UnlockCoinReward = 25,
            star3UnlockCoinReward = 40,
            sectorCompletionCoinReward = 300,
            sectorCompletionGemReward = 15
        },
        new SectorEconomyRewardConfig
        {
            sectorNumber = 3,
            firstClearCoinReward = 90,
            star1UnlockCoinReward = 20,
            star2UnlockCoinReward = 35,
            star3UnlockCoinReward = 50,
            sectorCompletionCoinReward = 450,
            sectorCompletionGemReward = 25
        }
    };

    [Header("2. Power Skill Caps & Loadout (GDD §5)")]
    [Tooltip("Maximum slots selectable before entering a level (GDD §5.5 = 4; 4th slot may remain empty).")]
    [Range(1, 4)]
    [SerializeField] private int maxLoadoutSlots = 4;

    [SerializeField] private PowerSkillEconomyConfig[] powerSkills =
    {
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Breaker,
            skillId = PowerUpIds.Breaker,
            enabledInCampaign = true,
            unlockedByDefault = true,
            inventoryCap = 12,
            initialTutorialCharges = 2
        },
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Freeze,
            skillId = PowerUpIds.Freeze,
            enabledInCampaign = true,
            unlockedByDefault = true,
            inventoryCap = 12,
            initialTutorialCharges = 2
        },
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Teleport,
            skillId = PowerUpIds.Teleport,
            enabledInCampaign = true,
            unlockedByDefault = true,
            inventoryCap = 8,
            initialTutorialCharges = 2
        },
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Shield,
            skillId = PowerUpIds.Shield,
            enabledInCampaign = true,
            unlockedByDefault = false,
            inventoryCap = 10,
            initialTutorialCharges = 0
        },
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Magnet,
            skillId = PowerUpIds.Magnet,
            enabledInCampaign = true,
            unlockedByDefault = false,
            inventoryCap = 10,
            initialTutorialCharges = 0
        },
        new PowerSkillEconomyConfig
        {
            skillType = PowerUpType.Echo,
            skillId = PowerUpIds.Echo,
            enabledInCampaign = true,
            unlockedByDefault = false,
            inventoryCap = 8,
            initialTutorialCharges = 0
        }
    };

    [Header("3. Rewarded Ads Policy (Pending Balance Tuning — GDD §7)")]
    [Tooltip("Maximum rewarded ad rewards a player can claim per local calendar day.")]
    [Min(1)]
    [SerializeField] private int rewardedAdDailyLimit = 5;

    [Tooltip("Fixed reward granted when watching the opt-in Result Screen ad (Coins or Skill Charge only — NEVER Gems).")]
    [SerializeField] private RewardItem resultScreenAdReward = new RewardItem(RewardType.Coins, 30);

    [Tooltip("Fixed reward granted when watching the opt-in Shop ad (Coins or Skill Charge only — NEVER Gems).")]
    [SerializeField] private RewardItem shopAdReward = new RewardItem(RewardType.Coins, 30);

    [Tooltip("Optional minimum cooldown in seconds between two rewarded ad claims.")]
    [Min(0f)]
    [SerializeField] private float rewardedAdCooldownSeconds;

    [Header("4. Interstitial Ads Policy (GDD §8)")]
    [Tooltip("Minimum completed levels required before an interstitial ad may appear (GDD §8.1 = 2).")]
    [Min(1)]
    [SerializeField] private int minCompletedLevelsBetweenInterstitials = 2;

    [Tooltip("Maximum completed levels before an interstitial ad is eligible (GDD §8.1 = 3).")]
    [Min(1)]
    [SerializeField] private int maxCompletedLevelsBetweenInterstitials = 3;

    [Tooltip("Minimum seconds between interstitial impressions (0 lets 2-3 play cadence show cleanly even on short levels).")]
    [Min(0f)]
    [SerializeField] private float interstitialCooldownSeconds = 0f;

    [Tooltip("When true, both completed levels AND failed runs count toward the 2-3 play interstitial threshold.")]
    [SerializeField] private bool countFailedRunsTowardInterstitial = true;

    [Header("5. Campaign Economy Guardrails (Approved Decisions)")]
    [Tooltip("Disables PlayerLevel / EXP lock checks on Campaign worlds so all 30 levels unlock purely by level completion.")]
    [SerializeField] private bool bypassPlayerLevelAndExpWorldLocks = true;

    [Tooltip("Prevents in-level Coin pickups from granting direct wallet Coins on touch (stops Restart coin-farming exploit).")]
    [SerializeField] private bool grantWalletCoinsFromInLevelPickups;

    [Tooltip("Controls whether mid-gameplay Revive popup is active. Disabled in standard Campaign per GDD §7.1 & §3.1.")]
    [SerializeField] private bool enableMidGameplayRevive;

    [Tooltip("Controls whether post-level BlindBox (random Gem/Ad doubler) is active. Disabled per GDD §3.3 & §7.2.")]
    [SerializeField] private bool enablePostLevelBlindBox;

    [Tooltip("Controls legacy out-of-GDD reward widgets (Map FreePower ad, random PostGameAds popup, 4h CoinBonus, 7d DailyReward streak).")]
    [SerializeField] private bool enableLegacyExtraRewardSystems;

    public bool AllowPerLevelFirstClearOverride => allowPerLevelFirstClearOverride;
    public int MaxLoadoutSlots => Mathf.Clamp(maxLoadoutSlots, 1, 4);
    public int RewardedAdDailyLimit => Mathf.Max(1, rewardedAdDailyLimit);
    public RewardItem ResultScreenAdReward => SanitizeAdReward(resultScreenAdReward);
    public RewardItem ShopAdReward => SanitizeAdReward(shopAdReward);
    public float RewardedAdCooldownSeconds => Mathf.Max(0f, rewardedAdCooldownSeconds);
    public int MinCompletedLevelsBetweenInterstitials => Mathf.Max(1, Mathf.Min(minCompletedLevelsBetweenInterstitials, maxCompletedLevelsBetweenInterstitials));
    public int MaxCompletedLevelsBetweenInterstitials => Mathf.Max(MinCompletedLevelsBetweenInterstitials, Mathf.Max(minCompletedLevelsBetweenInterstitials, maxCompletedLevelsBetweenInterstitials));
    public float InterstitialCooldownSeconds => Mathf.Max(0f, interstitialCooldownSeconds);
    public bool CountFailedRunsTowardInterstitial => countFailedRunsTowardInterstitial;

    public bool BypassPlayerLevelAndExpWorldLocks => bypassPlayerLevelAndExpWorldLocks;
    public bool GrantWalletCoinsFromInLevelPickups => grantWalletCoinsFromInLevelPickups;
    public bool EnableMidGameplayRevive => enableMidGameplayRevive;
    public bool EnablePostLevelBlindBox => enablePostLevelBlindBox;
    public bool EnableLegacyExtraRewardSystems => enableLegacyExtraRewardSystems;

    public SectorEconomyRewardConfig GetSectorConfig(int sectorNumber)
    {
        if (sectorRewards == null || sectorRewards.Length == 0)
        {
            return new SectorEconomyRewardConfig { sectorNumber = Mathf.Max(1, sectorNumber) };
        }

        for (int i = 0; i < sectorRewards.Length; i++)
        {
            if (sectorRewards[i] != null && sectorRewards[i].sectorNumber == sectorNumber)
                return sectorRewards[i];
        }

        int fallbackIdx = Mathf.Clamp(sectorNumber - 1, 0, sectorRewards.Length - 1);
        return sectorRewards[fallbackIdx] ?? new SectorEconomyRewardConfig { sectorNumber = Mathf.Max(1, sectorNumber) };
    }

    public int GetFirstClearCoins(LevelData level, int sectorNumber)
    {
        if (allowPerLevelFirstClearOverride && level != null && level.baseCoinReward > 0)
            return Mathf.Max(0, level.baseCoinReward);

        return Mathf.Max(0, GetSectorConfig(sectorNumber).firstClearCoinReward);
    }

    public int GetReplayStarUnlockCoins(int sectorNumber, int starIndex)
    {
        SectorEconomyRewardConfig cfg = GetSectorConfig(sectorNumber);
        return starIndex switch
        {
            1 => Mathf.Max(0, cfg.star1UnlockCoinReward),
            2 => Mathf.Max(0, cfg.star2UnlockCoinReward),
            3 => Mathf.Max(0, cfg.star3UnlockCoinReward),
            _ => 0
        };
    }

    public bool TryGetSkillConfig(string skillId, out PowerSkillEconomyConfig config)
    {
        config = null;
        if (string.IsNullOrEmpty(skillId) || powerSkills == null)
            return false;

        for (int i = 0; i < powerSkills.Length; i++)
        {
            PowerSkillEconomyConfig entry = powerSkills[i];
            if (entry != null && string.Equals(entry.skillId, skillId, StringComparison.OrdinalIgnoreCase))
            {
                config = entry;
                return true;
            }
        }

        return false;
    }

    public bool TryGetSkillConfig(PowerUpType type, out PowerSkillEconomyConfig config)
    {
        config = null;
        if (powerSkills == null)
            return false;

        for (int i = 0; i < powerSkills.Length; i++)
        {
            PowerSkillEconomyConfig entry = powerSkills[i];
            if (entry != null && entry.skillType == type)
            {
                config = entry;
                return true;
            }
        }

        return false;
    }

    public bool IsSkillActiveInCampaign(string skillId)
    {
        if (TryGetSkillConfig(skillId, out PowerSkillEconomyConfig cfg))
            return IsSkillConfigUnlocked(cfg);

        return false;
    }

    public bool IsSkillActiveInCampaign(PowerUpType type)
    {
        if (TryGetSkillConfig(type, out PowerSkillEconomyConfig cfg))
            return IsSkillConfigUnlocked(cfg);

        return false;
    }

    private static bool IsSkillConfigUnlocked(PowerSkillEconomyConfig cfg)
    {
        if (cfg == null || !cfg.enabledInCampaign)
            return false;

        if (cfg.unlockedByDefault)
            return true;

        ProfileData profile = SaveManager.loadProfile();
        return profile != null && profile.HasSkill(cfg.skillId);
    }

    public int GetSkillInventoryCap(string skillId)
    {
        if (TryGetSkillConfig(skillId, out PowerSkillEconomyConfig cfg))
            return Mathf.Max(1, cfg.inventoryCap);

        if (string.Equals(skillId, PowerUpIds.Teleport, StringComparison.OrdinalIgnoreCase))
            return 8;
        if (string.Equals(skillId, PowerUpIds.Breaker, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(skillId, PowerUpIds.Freeze, StringComparison.OrdinalIgnoreCase))
            return 12;

        return 99;
    }

    public int GetInitialTutorialCharges(string skillId)
    {
        if (TryGetSkillConfig(skillId, out PowerSkillEconomyConfig cfg))
            return Mathf.Max(0, cfg.initialTutorialCharges);

        return 0;
    }

    public void ResetToBaselineDefaults()
    {
        allowPerLevelFirstClearOverride = false;
        maxLoadoutSlots = 4;
        rewardedAdDailyLimit = 5;
        resultScreenAdReward = new RewardItem(RewardType.Coins, 30);
        shopAdReward = new RewardItem(RewardType.Coins, 30);
        rewardedAdCooldownSeconds = 0f;
        minCompletedLevelsBetweenInterstitials = 2;
        maxCompletedLevelsBetweenInterstitials = 3;
        interstitialCooldownSeconds = 0f;
        countFailedRunsTowardInterstitial = true;
        powerSkills = new[]
        {
            new PowerSkillEconomyConfig { skillType = PowerUpType.Breaker, skillId = PowerUpIds.Breaker, enabledInCampaign = true, unlockedByDefault = true, inventoryCap = 12, initialTutorialCharges = 2 },
            new PowerSkillEconomyConfig { skillType = PowerUpType.Freeze, skillId = PowerUpIds.Freeze, enabledInCampaign = true, unlockedByDefault = true, inventoryCap = 12, initialTutorialCharges = 2 },
            new PowerSkillEconomyConfig { skillType = PowerUpType.Teleport, skillId = PowerUpIds.Teleport, enabledInCampaign = true, unlockedByDefault = true, inventoryCap = 8, initialTutorialCharges = 2 },
            new PowerSkillEconomyConfig { skillType = PowerUpType.Shield, skillId = PowerUpIds.Shield, enabledInCampaign = true, unlockedByDefault = false, inventoryCap = 10, initialTutorialCharges = 0 },
            new PowerSkillEconomyConfig { skillType = PowerUpType.Magnet, skillId = PowerUpIds.Magnet, enabledInCampaign = true, unlockedByDefault = false, inventoryCap = 10, initialTutorialCharges = 0 },
            new PowerSkillEconomyConfig { skillType = PowerUpType.Echo, skillId = PowerUpIds.Echo, enabledInCampaign = true, unlockedByDefault = false, inventoryCap = 8, initialTutorialCharges = 0 }
        };
        bypassPlayerLevelAndExpWorldLocks = true;
        grantWalletCoinsFromInLevelPickups = false;
        enableMidGameplayRevive = false;
        enablePostLevelBlindBox = false;
        enableLegacyExtraRewardSystems = false;
    }

    /// <summary>
    /// Enforces GDD §7.2 invariant: Rewarded Ads must NEVER grant Gems.
    /// </summary>
    private static RewardItem SanitizeAdReward(RewardItem candidate)
    {
        if (candidate.Type == RewardType.Gems
            || candidate.Type == RewardType.Cosmetic
            || candidate.Type == RewardType.Exp
            || candidate.Type == RewardType.SkillUnlock)
        {
            return new RewardItem(RewardType.Coins, Mathf.Max(1, candidate.Amount));
        }

        return candidate;
    }

    private void OnValidate()
    {
        maxLoadoutSlots = Mathf.Clamp(maxLoadoutSlots, 1, 4);
        rewardedAdDailyLimit = Mathf.Max(1, rewardedAdDailyLimit);
        minCompletedLevelsBetweenInterstitials = Mathf.Max(1, minCompletedLevelsBetweenInterstitials);
        maxCompletedLevelsBetweenInterstitials = Mathf.Max(minCompletedLevelsBetweenInterstitials, maxCompletedLevelsBetweenInterstitials);

        if (resultScreenAdReward.Type == RewardType.Gems)
        {
            Debug.LogWarning("[EconomyBalanceConfig] GDD §7.2 forbids granting Gems via Rewarded Ads. Reverting Result Screen ad reward to Coins.", this);
            resultScreenAdReward = new RewardItem(RewardType.Coins, Mathf.Max(1, resultScreenAdReward.Amount));
        }

        if (shopAdReward.Type == RewardType.Gems)
        {
            Debug.LogWarning("[EconomyBalanceConfig] GDD §7.2 forbids granting Gems via Rewarded Ads. Reverting Shop ad reward to Coins.", this);
            shopAdReward = new RewardItem(RewardType.Coins, Mathf.Max(1, shopAdReward.Amount));
        }
    }
}

