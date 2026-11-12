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
        items = new List<RewardItem>();
        if (initialItems == null) return;

        foreach (RewardItem item in initialItems)
        {
            if (item.Type == RewardType.Cosmetic && !string.IsNullOrEmpty(item.ItemId))
            {
                items.Add(new RewardItem(RewardType.Cosmetic, Mathf.Max(1, item.Amount), item.ItemId));
            }
            else if (item.Amount > 0)
            {
                items.Add(item);
            }
        }
    }

    public void Add(RewardItem item)
    {
        if (item.Type == RewardType.Cosmetic && !string.IsNullOrEmpty(item.ItemId))
        {
            items.Add(new RewardItem(RewardType.Cosmetic, Mathf.Max(1, item.Amount), item.ItemId));
            return;
        }

        if (item.Amount <= 0) return;
        items.Add(item);
    }

    public void Add(RewardType type, int amount, string itemId = "")
    {
        if (type == RewardType.Cosmetic && !string.IsNullOrEmpty(itemId))
        {
            items.Add(new RewardItem(RewardType.Cosmetic, Mathf.Max(1, amount), itemId));
            return;
        }

        if (amount <= 0) return;
        items.Add(new RewardItem(type, amount, itemId));
    }

    public bool HasType(RewardType type)
    {
        if (items == null) return false;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i].Type == type && (type == RewardType.Cosmetic || items[i].Amount > 0))
                return true;
        }
        return false;
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
    public int ExpCount => GetTotal(RewardType.Exp);
}
