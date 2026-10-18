using UnityEngine;

/// <summary>
/// Objective: collect a required number of pickups in the layout.
///
/// requiredCount = 0 means "all collectibles in the layout".
/// </summary>
public class CollectPickupsObjective : ObjectiveBase
{
    private int requiredCount;
    private int currentCount;

    public override float Progress =>
        requiredCount > 0 ? Mathf.Clamp01((float)currentCount / requiredCount) : 0f;

    public int RequiredCount => requiredCount;
    public int CurrentCount => currentCount;

    public override void Initialize(ObjectiveDefinition definition, LevelRoot layout)
    {
        currentCount = 0;

        // 0 = collect every pickup placed in the layout.
        requiredCount = definition != null && definition.requiredCount > 0
            ? definition.requiredCount
            : (layout != null ? layout.TotalCollectibles : 0);

        if (requiredCount <= 0)
            Debug.LogWarning("[CollectPickupsObjective] No required count configured and layout has no collectibles.");
    }

    public override void Reset()
    {
        currentCount = 0;
        ClearComplete();
    }

    /// <summary>
    /// Called by the ObjectiveSystem when LevelRoot reports a pickup.
    /// </summary>
    public void NotifyPickupCollected()
    {
        if (IsComplete) return;

        currentCount++;
        if (currentCount >= requiredCount)
            MarkComplete();
    }
}
