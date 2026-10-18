using System;

/// <summary>
/// Architectural contract decoupling game logic from commercial interstitial ad SDKs.
/// Interstitials never grant rewards — they are intentionally separate from
/// IRewardedAdService.
/// </summary>
public interface IInterstitialAdService
{
    /// <summary>
    /// Queries if an interstitial is loaded and ready to show.
    /// </summary>
    bool IsAdAvailable();

    /// <summary>
    /// Requests display of a loaded interstitial.
    /// Guaranteed to invoke onComplete — true when the ad was presented and
    /// closed, false when unavailable or the presentation failed.
    /// </summary>
    void ShowInterstitial(Action<bool> onComplete = null);
}
