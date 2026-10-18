using System;
using UnityEngine;

/// <summary>
/// Serializable definition of a single level objective.
/// Lives inside LevelData and drives the runtime ObjectiveSystem.
/// </summary>
[Serializable]
public class ObjectiveDefinition
{
    [Header("Objective Type")]
    [Tooltip("What condition must be satisfied to complete this objective.")]
    public ObjectiveType objectiveType;

    [Header("Quantity")]
    [Tooltip("Pickups required for CollectPickups objectives. 0 = every collectible in the layout.")]
    [Min(0)]
    public int requiredCount;

    [Header("Presentation")]
    [Tooltip("Optional HUD text. When empty a default description is generated from the type.")]
    [TextArea(1, 2)]
    public string description;
}
