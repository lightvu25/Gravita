using UnityEngine;

/// <summary>
/// Legacy pure calculation helper retained for compatibility; aligned with
/// <see cref="EconomyBalanceConfig"/> and <see cref="CampaignEconomyService"/> (GDD §4):
///   - First clear grants Sector-configured Coins once (zero direct star bonus Coins/Gems).
///   - Unimproved replay grants 0 Coins and 0 Gems (replay star upgrades are settled via <see cref="CampaignEconomyService"/>).
/// </summary>
public static class RewardCalculator
{
    public const int FirstClearBaseCoins = 50;
    public const int OptionalObjectiveBonusCoins = 0;
    public const int ReplayBaseCoins = 0;

    public const int FirstClearBaseExp = 50;
    public const int BonusExpPerStar = 15;
    public const int ReplayBaseExp = 20;

    public const int ThreeStarBonusGems = 0;

    /// <summary>
    /// Calculates the baseline first-clear reward bundle for a puzzle level (GDD §4.1–§4.2).
    /// Note: Authoritative Campaign settlement (including replay star upgrades and sector milestones)
    /// is executed atomically by <see cref="CampaignEconomyService.SettleLevelCompletion"/>.
    /// </summary>
    public static RewardBundle CalculateLevelRewards(
        LevelData levelData, 
        int starsEarned, 
        bool isFirstClear, 
        int optionalObjectivesCompleted)
    {
        var bundle = new RewardBundle();

        if (isFirstClear)
        {
            int sectorNumber = levelData != null ? Mathf.Max(1, levelData.World) : 1;
            int totalCoins = EconomyBalanceConfig.Active.GetFirstClearCoins(levelData, sectorNumber);
            if (totalCoins > 0)
            {
                bundle.Add(RewardType.Coins, totalCoins);
            }
        }

        return bundle;
    }
}
