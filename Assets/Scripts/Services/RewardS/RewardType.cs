/// <summary>
/// Types of rewards grantable through the reward pipeline.
/// </summary>
public enum RewardType
{
    Coins,
    Gems,
    /// <summary>Consumable boost stored in the profile power-up inventory (see RewardItem.ItemId).</summary>
    PowerUp,
    /// <summary>Player experience points contributing toward Player Level progression.</summary>
    Exp,
    /// <summary>Permanent non-gameplay visual cosmetic (e.g. PlayerSkin; see RewardItem.ItemId).</summary>
    Cosmetic,
    /// <summary>Unlocks a modular Power Core by ID (see RewardItem.ItemId).</summary>
    SkillUnlock
}
