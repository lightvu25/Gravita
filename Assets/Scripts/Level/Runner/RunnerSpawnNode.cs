using UnityEngine;

/// <summary>
/// Optional hand-authored spawn anchor placed inside Maze Runner room prefabs.
/// <see cref="RuntimeLevelPopulator"/> rolls eligible prefabs/events on these nodes first
/// before filling remaining room quotas on valid floor cells.
/// </summary>
public class RunnerSpawnNode : MonoBehaviour
{
    [Tooltip("Restricts which category can spawn here (Any selects across eligible categories).")]
    public RunnerSpawnCategory allowedCategory = RunnerSpawnCategory.Any;

    [Range(0f, 1f)]
    [Tooltip("Independent probability that this specific node attempts a spawn.")]
    public float activationChance = 1f;

    [Tooltip("If true and the room's Event check succeeded, this node skips activationChance roll.")]
    public bool forceWhenRoomEventTriggers = false;

    [Tooltip("Optional custom prefab candidates specific to this anchor (falls back to RunnerPopulationProfile pool when empty).")]
    public GameObject[] prefabOverrides;
}
