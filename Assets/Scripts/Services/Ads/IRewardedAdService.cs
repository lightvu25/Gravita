using System;

/// <summary>
/// Architectural contract decoupling game logic from commercial ad SDKs (IronSource, Unity Ads, AppLovin, AdMob).
/// </summary>
public interface IRewardedAdService
{
    /// <summary>
    /// Queries if an ad is ready to show for the given placement.
    /// </summary>
    bool IsAdAvailable(RewardedAdPlacement placement);

    /// <summary>
    /// Requests display of a rewarded ad for the given placement.
    /// Guaranteed to invoke onComplete with the result.
    /// </summary>
    void ShowRewardedAd(RewardedAdPlacement placement, Action<RewardedAdResult> onComplete);
}

