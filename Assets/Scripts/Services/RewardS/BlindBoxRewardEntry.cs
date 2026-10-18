using System;
using UnityEngine;

/// <summary>
/// One row of the blind box loot table. A rolled entry grants
/// Random.Range(minAmount, maxAmount) of <see cref="type"/>; for PowerUp
/// entries <see cref="itemId"/> carries the power-up identifier.
/// </summary>
[Serializable]
public class BlindBoxRewardEntry
{
    public RewardType type = RewardType.Coins;
    [Tooltip("Power-up id for RewardType.PowerUp entries (see PowerUpIds).")]
    public string itemId = "";
    [Min(1)] public int minAmount = 1;
    [Min(1)] public int maxAmount = 1;
    [Tooltip("Relative roll weight — higher means more common.")]
    [Min(0)] public int weight = 10;
    [Tooltip("Label shown on the reveal screen, e.g. 'Coins', 'Kinetic Shield'.")]
    public string displayName = "Coins";
}
