using System;
using UnityEngine;

/// <summary>
/// Represents an individual reward unit (currency, tool, or cosmetic).
/// </summary>
[Serializable]
public struct RewardItem
{
    [SerializeField] private RewardType type;
    [SerializeField] private int amount;
    [SerializeField] private string itemId;

    public RewardType Type => type;
    public int Amount => amount;
    public string ItemId => itemId;

    public RewardItem(RewardType type, int amount, string itemId = "")
    {
        this.type = type;
        this.amount = Mathf.Max(0, amount);
        this.itemId = itemId ?? "";
    }

    public override string ToString()
    {
        return string.IsNullOrEmpty(itemId) 
            ? $"{amount} {type}" 
            : $"{amount} {type} ({itemId})";
    }
}

