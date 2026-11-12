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

    public List<ShopItemDefinition> GetItemsByTab(ShopTabCategory tab)
    {
        var result = new List<ShopItemDefinition>();
        if (items == null) return result;

        for (int i = 0; i < items.Count; i++)
        {
            ShopItemDefinition item = items[i];
            if (item == null) continue;
            if (tab == ShopTabCategory.All || item.ShopTab == tab)
                result.Add(item);
        }
        return result;
    }

    private void OnValidate()
    {
        items ??= new List<ShopItemDefinition>();
    }

    public void SetItems(IEnumerable<ShopItemDefinition> newItems)
    {
        items ??= new List<ShopItemDefinition>();
        items.Clear();
        if (newItems == null) return;

        foreach (var item in newItems)
        {
            if (item != null && !items.Contains(item))
                items.Add(item);
        }
    }
}
