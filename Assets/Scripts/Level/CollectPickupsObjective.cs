using System;
using UnityEngine;

/// <summary>
/// Objective: collect a required number of pickups in the layout.
///
/// requiredCount = 0 means "all matching collectibles in the layout".
/// </summary>
public class CollectPickupsObjective : ObjectiveBase
{
    private int requiredCount;
    private int currentCount;
    private bool targetStarsOnly;
    private bool ignoreStars;

    public override float Progress =>
        requiredCount > 0 ? Mathf.Clamp01((float)currentCount / requiredCount) : 0f;

    public int RequiredCount => requiredCount;
    public int CurrentCount => currentCount;

    public override void Initialize(ObjectiveDefinition definition, LevelRoot layout)
    {
        currentCount = 0;
        targetStarsOnly = definition != null
            && !string.IsNullOrEmpty(definition.description)
            && definition.description.IndexOf("star", StringComparison.OrdinalIgnoreCase) >= 0;
        ignoreStars = !targetStarsOnly && layout != null && layout.TotalCoinCount > 0;

        if (definition != null && definition.requiredCount > 0)
        {
            requiredCount = definition.requiredCount;
        }
        else if (layout != null)
        {
            requiredCount = targetStarsOnly
                ? layout.TotalStarCount
                : (ignoreStars ? layout.TotalCoinCount : layout.TotalCollectibles);
        }
        else
        {
            requiredCount = 0;
        }

        if (requiredCount <= 0)
            Debug.LogWarning("[CollectPickupsObjective] No required count configured and layout has no matching collectibles.");
    }

    public override void Reset()
    {
        currentCount = 0;
        ClearComplete();
    }

    /// <summary>
    /// Called by the ObjectiveSystem when LevelRoot reports a specific pickup.
    /// Filters Star vs Coin collectibles based on objective target.
    /// </summary>
    public void NotifyPickupCollected(Collectible collectible)
    {
        if (collectible != null)
        {
            if (targetStarsOnly && !collectible.IsStar)
                return;
            if (ignoreStars && collectible.IsStar)
                return;
        }

        NotifyPickupCollected();
    }

    /// <summary>
    /// Increments collected pickup count without type filtering.
    /// </summary>
    public void NotifyPickupCollected()
    {
        if (IsComplete) return;

        currentCount++;
        if (currentCount >= requiredCount)
            MarkComplete();
    }
}
