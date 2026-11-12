using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central catalog of available <see cref="CosmeticDefinition"/> assets.
/// </summary>
[CreateAssetMenu(fileName = "CosmeticDatabase_Main", menuName = "Gravita/Economy/Cosmetic Database")]
public class CosmeticDatabase : ScriptableObject
{
    [SerializeField] private List<CosmeticDefinition> cosmetics = new List<CosmeticDefinition>();

    public IReadOnlyList<CosmeticDefinition> Cosmetics => cosmetics;

    public CosmeticDefinition GetById(string cosmeticId)
    {
        if (string.IsNullOrEmpty(cosmeticId) || cosmetics == null)
            return null;

        for (int i = 0; i < cosmetics.Count; i++)
        {
            CosmeticDefinition def = cosmetics[i];
            if (def != null && def.CosmeticId == cosmeticId)
                return def;
        }

        return null;
    }

    public List<CosmeticDefinition> GetByCategory(CosmeticCategory category)
    {
        var result = new List<CosmeticDefinition>();
        if (cosmetics == null)
            return result;

        for (int i = 0; i < cosmetics.Count; i++)
        {
            CosmeticDefinition def = cosmetics[i];
            if (def != null && def.Category == category)
                result.Add(def);
        }

        return result;
    }

    public void SetCosmetics(IEnumerable<CosmeticDefinition> items)
    {
        cosmetics.Clear();
        if (items == null) return;

        foreach (CosmeticDefinition item in items)
        {
            if (item != null && !cosmetics.Contains(item))
                cosmetics.Add(item);
        }
    }
}

