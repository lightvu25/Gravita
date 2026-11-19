using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Master ScriptableObject registry of Live-Ops events displayed inside <see cref="EventPanelUI"/>.
/// </summary>
[CreateAssetMenu(fileName = "EventDatabase", menuName = "Gravita/Events/Event Database")]
public class EventDatabase : ScriptableObject
{
    [Header("Live-Ops Events (Ordered by Display Priority)")]
    [SerializeField] private List<GameEventDefinition> events = new List<GameEventDefinition>();

    public IReadOnlyList<GameEventDefinition> Events => events;

    public void SetEvents(IEnumerable<GameEventDefinition> definitions)
    {
        events ??= new List<GameEventDefinition>();
        events.Clear();
        if (definitions == null)
            return;

        foreach (GameEventDefinition def in definitions)
        {
            if (def != null)
                events.Add(def);
        }
    }

    public void GetVisibleEvents(List<GameEventDefinition> results)
    {
        if (results == null)
            return;

        results.Clear();
        if (events == null)
            return;

        for (int i = 0; i < events.Count; i++)
        {
            GameEventDefinition def = events[i];
            if (def == null)
                continue;

            if (def.HideWhenExpired && !EventService.IsEventActive(def))
                continue;

            results.Add(def);
        }
    }

    private void OnValidate()
    {
        events ??= new List<GameEventDefinition>();
    }
}
