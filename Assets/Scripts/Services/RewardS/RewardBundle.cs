using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bundles multiple RewardItem units into an atomic reward grant.
/// </summary>
[Serializable]
public class RewardBundle
{
    [SerializeField] private List<RewardItem> items = new List<RewardItem>();

    public IReadOnlyList<RewardItem> Items => items;

    public bool IsEmpty => items == null || items.Count == 0;

    public RewardBundle()
    {
        items = new List<RewardItem>();
    }

    public RewardBundle(IEnumerable<RewardItem> initialItems)
    {
        items = initialItems != null ? new List<RewardItem>(initialItems) : new List<RewardItem>();
    }

    public void Add(RewardItem item)
    {
        if (item.Amount <= 0) return;
        items.Add(item);
    }

    public void Add(RewardType type, int amount, string itemId = "")
    {
        if (amount <= 0) return;
        items.Add(new RewardItem(type, amount, itemId));
    }

    public int GetTotal(RewardType type)
    {
        int sum = 0;
        if (items == null) return 0;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Type == type)
                sum += items[i].Amount;
        }
        return sum;
    }

    public int CoinCount => GetTotal(RewardType.Coins);
    public int GemCount => GetTotal(RewardType.Gems);
}

