using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject database containing all shop catalog items.
/// </summary>
[CreateAssetMenu(fileName = "ShopDatabase", menuName = "Game/Shop/Shop Database")]
public class ShopDatabase : ScriptableObject
{
    [SerializeField] private List<ShopItemDefinition> items = new List<ShopItemDefinition>();

    public IReadOnlyList<ShopItemDefinition> Items => items;

    public ShopItemDefinition GetItemById(string itemId)
    {
        if (string.IsNullOrEmpty(itemId) || items == null) return null;
        return items.Find(x => x != null && x.ItemId == itemId);
    }
}

