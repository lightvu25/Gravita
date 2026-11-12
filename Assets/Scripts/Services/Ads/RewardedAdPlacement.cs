/// <summary>
/// Contextual placements for opt-in rewarded ads.
/// In standard Campaign (GDD §7.1), only Result Screen and Shop placements are active;
/// mid-gameplay and random popup placements remain dormant unless explicitly enabled in <see cref="EconomyBalanceConfig"/>.
/// </summary>
public enum RewardedAdPlacement
{
    SecondChance = 0,
    LevelClearCoinDoubler = 1,
    CoinBonusMultiplier = 2,
    BlindBoxDoubler = 3,
    PostGameBonus = 4,
    FreePower = 5,
    ResultScreenBonus = 6,
    ShopBonus = 7
}
