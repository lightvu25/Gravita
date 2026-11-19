using UnityEngine;
using System;
using System.Collections.Generic;

public enum PowerUpStatKind
{
    None = 0,
    DurationSeconds = 1,
    WallCells = 2,
    Radius = 3,
    CooldownSeconds = 4
}

public enum PowerUpStatsType
{
    None = 0,
    DurationSeconds = 1,
    WallCells = 2
}

public enum PowerUpActivationMode
{
    ManualActive = 0,
    AutoTrigger = 1,
    HybridAutoAndManual = 2
}

[Serializable]
public class PowerUpLevelStep
{
    [Min(0)] public int upgradeCostCoins;
    [Min(0f)] public float statValue;
    [Min(0f)] public float cooldownSeconds = 12f;
}

[CreateAssetMenu(fileName = "NewPowerUpData", menuName = "Gravita/Power-up Data")]
public class PowerUpData : ScriptableObject
{
    [Header("Identity & Display")]
    [Tooltip("Stable lowercase identifier used in code and save data (e.g. 'breaker', 'freeze', 'teleport').")]
    [SerializeField] private string id;

    [Header("Upgrade Levels (Up tab)")]
    [SerializeField] private PowerUpStatKind upgradeStat = PowerUpStatKind.None;
    [SerializeField] private string statUnitLabel = "sec";
    [SerializeField] private string statValueFormat = "0.00";
    [SerializeField] private List<PowerUpLevelStep> levelSteps = new List<PowerUpLevelStep>();

    [Tooltip("Player-facing name displayed in UI.")]
    [SerializeField] private string displayName;

    [Tooltip("Short gameplay description of what the power-up does.")]
    [TextArea(2, 4)]
    [SerializeField] private string description;

    [Tooltip("Icon displayed in UI. Leave empty until artwork is assigned.")]
    [SerializeField] private Sprite icon;

    [Tooltip("Enum value identifying the power-up type in code.")]
    [SerializeField] private PowerUpType powerUpType;

    [Header("Economy & Campaign Availability (GDD §5)")]
    [Tooltip("True for the 3 GDD baseline Power Skills (Breaker, Freeze, Teleport); false for dormant legacy power-ups.")]
    [SerializeField] private bool isCampaignActiveSkill = true;

    [Tooltip("Hard inventory cap for this skill (GDD §5.1: Breaker=12, Freeze=12, Teleport=8).")]
    [Min(1)]
    [SerializeField] private int inventoryCap = 12;

    [Tooltip("Initial charges granted once when unlocked in tutorial (GDD §5.2 — Pending Balance Tuning).")]
    [Min(0)]
    [SerializeField] private int initialTutorialCharges = 2;

    [Header("Effect Parameters")]
    [Tooltip("Effect duration in seconds. Use 0 for instant or single-hit effects (e.g. Shield, Breaker, Teleport).")]
    [Min(0f)]
    [SerializeField] private float duration;

    [Tooltip("Generic configurable magnitude (Shield: blocked damage count; Magnet: attraction radius; Breaker: max wall cells; Teleport: max wall cells).")]
    [Min(0f)]
    [SerializeField] private float effectValue;

    [Header("Type-Specific Configuration")]
    [Tooltip("Magnet: Speed (units/sec) at which coins within the attraction radius are pulled toward the player.")]
    [Min(0.1f)]
    [SerializeField] private float pullSpeed = 12f;

    [Tooltip("Breaker: Maximum consecutive grid cells / breakable walls in front of the player that can be broken.")]
    [Min(0)]
    [SerializeField] private int maxWallCells = 2;

    [Tooltip("Breaker: Maximum eligible traps within the inspected cells that can be destroyed.")]
    [Min(0)]
    [SerializeField] private int maxTrapsDestroyed = 1;

    [Tooltip("Teleport: Minimum consecutive wall cells required to teleport through.")]
    [Min(1)]
    [SerializeField] private int minWallTraversal = 1;

    [Tooltip("Teleport: Maximum consecutive wall cells the player can teleport through.")]
    [Min(1)]
    [SerializeField] private int maxWallTraversal = 3;

    [Header("Usage & Consumption")]
    [Tooltip("Maximum number of permitted uses per level/run. 0 means unlimited uses (subject to inventory/cooldown).")]
    [Min(0)]
    [SerializeField] private int usageLimit = 1;

    [Tooltip("Cooldown in seconds between uses. 0 means no cooldown.")]
    [Min(0f)]
    [SerializeField] private float cooldown;

    [Tooltip("Whether one unit of this power-up is consumed from the player's inventory upon use.")]
    [SerializeField] private bool isConsumable = true;

    [Header("Modular Core System (Gravita)")]
    [SerializeField] private PowerUpActivationMode activationMode = PowerUpActivationMode.ManualActive;
    [SerializeField] private bool unlockedByDefault = true;
    [Tooltip("Coin discount granted per owned shard when upgrading (Choice A).")]
    [Min(0)]
    [SerializeField] private int coinDiscountPerShard = 25;
    [Tooltip("Time scale multiplier applied to level clock & hazards during Time Dilation.")]
    [Range(0.1f, 1f)]
    [SerializeField] private float timeDilationScale = 0.3f;
    [Tooltip("Fallback base cooldown in seconds for modular usage when levelSteps do not specify one.")]
    [Min(0f)]
    [SerializeField] private float baseCooldownSeconds = 12f;

    public string Id => !string.IsNullOrWhiteSpace(id) ? id : name;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : name;
    public string Description => description ?? string.Empty;
    public Sprite Icon => icon;
    public PowerUpType PowerUpType => powerUpType;
    public PowerUpActivationMode ActivationMode => activationMode;
    public bool UnlockedByDefault => unlockedByDefault;
    public int CoinDiscountPerShard => Mathf.Max(0, coinDiscountPerShard);
    public float TimeDilationScale => Mathf.Clamp(timeDilationScale, 0.1f, 1f);
    public float BaseCooldownSeconds => Mathf.Max(0f, baseCooldownSeconds);
    public bool IsCampaignActiveSkill => isCampaignActiveSkill && EconomyBalanceConfig.Active.IsSkillActiveInCampaign(powerUpType);
    public int InventoryCap => EconomyBalanceConfig.Active.TryGetSkillConfig(powerUpType, out PowerSkillEconomyConfig cfg)
        ? cfg.inventoryCap
        : Mathf.Max(1, inventoryCap);
    public int InitialTutorialCharges => EconomyBalanceConfig.Active.TryGetSkillConfig(powerUpType, out PowerSkillEconomyConfig cfg)
        ? cfg.initialTutorialCharges
        : Mathf.Max(0, initialTutorialCharges);
    public float Duration => duration;
    public float EffectValue => effectValue;
    public float PullSpeed => pullSpeed > 0f ? pullSpeed : 12f;
    public int BlockedDamageCount => effectValue > 0f ? Mathf.RoundToInt(effectValue) : 1;
    public float AttractionRadius => effectValue > 0f ? effectValue : 3.5f;
    public int MaxWallCells => maxWallCells > 0 ? maxWallCells : (effectValue > 0f ? Mathf.RoundToInt(effectValue) : 2);
    public int MaxTrapsDestroyed => Mathf.Max(0, maxTrapsDestroyed);
    public int MinWallTraversal => Mathf.Max(1, minWallTraversal);
    public int MaxWallTraversal => Mathf.Max(MinWallTraversal, maxWallTraversal > 0 ? maxWallTraversal : Mathf.RoundToInt(effectValue));
    public int UsageLimit => usageLimit;
    public float Cooldown => cooldown;
    public bool IsConsumable => isConsumable;

    public void Initialize(
        string id,
        string displayName,
        string description,
        PowerUpType powerUpType,
        float duration,
        float effectValue,
        int usageLimit = 1,
        float cooldown = 0f,
        bool isConsumable = true,
        Sprite icon = null,
        float pullSpeed = 12f,
        int maxWallCells = 2,
        int maxTrapsDestroyed = 1,
        int minWallTraversal = 1,
        int maxWallTraversal = 3,
        bool campaignActive = true,
        int cap = 12,
        int tutorialCharges = 2)
    {
        this.id = id;
        this.displayName = displayName;
        this.description = description;
        this.powerUpType = powerUpType;
        this.duration = Mathf.Max(0f, duration);
        this.effectValue = Mathf.Max(0f, effectValue);
        this.usageLimit = Mathf.Max(0, usageLimit);
        this.cooldown = Mathf.Max(0f, cooldown);
        this.isConsumable = isConsumable;
        this.icon = icon;
        this.pullSpeed = Mathf.Max(0.1f, pullSpeed);
        this.maxWallCells = Mathf.Max(0, maxWallCells);
        this.maxTrapsDestroyed = Mathf.Max(0, maxTrapsDestroyed);
        this.minWallTraversal = Mathf.Max(1, minWallTraversal);
        this.maxWallTraversal = Mathf.Max(this.minWallTraversal, maxWallTraversal);
        this.isCampaignActiveSkill = campaignActive;
        this.inventoryCap = Mathf.Max(1, cap);
        this.initialTutorialCharges = Mathf.Max(0, tutorialCharges);
    }

    public void ConfigureEconomyPolicy(bool campaignActive, int cap, int tutorialCharges)
    {
        isCampaignActiveSkill = campaignActive;
        inventoryCap = Mathf.Max(1, cap);
        initialTutorialCharges = Mathf.Max(0, tutorialCharges);
    }

    public void ConfigureModularCore(
        PowerUpActivationMode mode,
        bool defaultUnlocked,
        int shardDiscountCoins = 25,
        float dilationScale = 0.3f,
        float cooldownSec = 12f)
    {
        activationMode = mode;
        unlockedByDefault = defaultUnlocked;
        coinDiscountPerShard = Mathf.Max(0, shardDiscountCoins);
        timeDilationScale = Mathf.Clamp(dilationScale, 0.1f, 1f);
        baseCooldownSeconds = Mathf.Max(0f, cooldownSec);
        isConsumable = false;
        usageLimit = 0;
    }

    private void OnValidate()
    {
        duration = Mathf.Max(0f, duration);
        effectValue = Mathf.Max(0f, effectValue);
        pullSpeed = Mathf.Max(0.1f, pullSpeed);
        maxWallCells = Mathf.Max(0, maxWallCells);
        maxTrapsDestroyed = Mathf.Max(0, maxTrapsDestroyed);
        minWallTraversal = Mathf.Max(1, minWallTraversal);
        maxWallTraversal = Mathf.Max(minWallTraversal, maxWallTraversal);
        usageLimit = Mathf.Max(0, usageLimit);
        cooldown = Mathf.Max(0f, cooldown);
        inventoryCap = Mathf.Max(1, inventoryCap);
        initialTutorialCharges = Mathf.Max(0, initialTutorialCharges);
        coinDiscountPerShard = Mathf.Max(0, coinDiscountPerShard);
        timeDilationScale = Mathf.Clamp(timeDilationScale, 0.1f, 1f);
        baseCooldownSeconds = Mathf.Max(0f, baseCooldownSeconds);
        if (levelSteps != null && levelSteps.Count > 0 && levelSteps[0] != null)
            levelSteps[0].upgradeCostCoins = 0;
    }

    public override string ToString() => $"[PowerUpData] {DisplayName} ({Id}, {PowerUpType})";

    public PowerUpStatKind UpgradeStat => upgradeStat;
    public string StatUnitLabel => statUnitLabel ?? string.Empty;
    public int MaxLevel => levelSteps != null && levelSteps.Count > 0 ? levelSteps.Count : 1;

    public float GetStatAtLevel(int level, float legacyFallback)
    {
        if (levelSteps == null || levelSteps.Count == 0) return legacyFallback;
        return levelSteps[Mathf.Clamp(level, 1, levelSteps.Count) - 1].statValue;
    }

    public float GetCooldownAtLevel(int level)
    {
        if (levelSteps != null && levelSteps.Count > 0)
        {
            float stepCooldown = levelSteps[Mathf.Clamp(level, 1, levelSteps.Count) - 1].cooldownSeconds;
            if (stepCooldown > 0f)
                return stepCooldown;
        }
        return baseCooldownSeconds > 0f ? baseCooldownSeconds : (cooldown > 0f ? cooldown : 12f);
    }

    public string FormatStat(int level, float legacyFallback) =>
        GetStatAtLevel(level, legacyFallback).ToString(string.IsNullOrEmpty(statValueFormat) ? "0.##" : statValueFormat);

    /// Coins needed to go from fromLevel to fromLevel+1. -1 when already max.
    public int GetUpgradeCost(int fromLevel)
    {
        if (fromLevel >= MaxLevel || levelSteps == null || fromLevel < 1) return -1;
        return Mathf.Max(0, levelSteps[fromLevel].upgradeCostCoins);
    }

    public float GetDuration(int level)      => GetStatAtLevel(level, Duration);
    public int   GetMaxWallCells(int level)  => Mathf.Max(1, Mathf.RoundToInt(GetStatAtLevel(level, MaxWallCells)));
    public int   GetMaxWallTraversal(int level) =>
        Mathf.Max(MinWallTraversal, Mathf.RoundToInt(GetStatAtLevel(level, MaxWallTraversal)));
}
