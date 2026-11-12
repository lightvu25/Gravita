using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(fileName = "AchievementDatabase", menuName = "Game/Achievement/Achievement Database")]
public class AchievementDatabase : ScriptableObject
{
    [Header("All Lifetime Achievements")]
    [SerializeField] private List<AchievementDefinition> achievements = new List<AchievementDefinition>();

    public IReadOnlyList<AchievementDefinition> Achievements => achievements;

    public void AddOrReplaceAchievement(AchievementDefinition definition)
    {
        if (definition == null || string.IsNullOrEmpty(definition.AchievementId))
            return;

        achievements ??= new List<AchievementDefinition>();
        for (int i = 0; i < achievements.Count; i++)
        {
            if (achievements[i] != null && achievements[i].AchievementId == definition.AchievementId)
            {
                achievements[i] = definition;
                return;
            }
        }

        achievements.Add(definition);
    }

    public void SetAchievements(IEnumerable<AchievementDefinition> definitions)
    {
        achievements ??= new List<AchievementDefinition>();
        achievements.Clear();
        if (definitions == null)
            return;

        foreach (AchievementDefinition def in definitions)
        {
            if (def != null)
                achievements.Add(def);
        }
    }

    private void OnValidate()
    {
        achievements ??= new List<AchievementDefinition>();
    }
}

