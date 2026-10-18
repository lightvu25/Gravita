using System;
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Samples;
using UnityEngine;

/// <summary>
/// AdMob-backed <see cref="IRewardedAdService"/>. Waits for the one-time SDK
/// initialization owned by <see cref="GoogleMobileAdsController"/>, keeps one
/// rewarded ad loaded, and maps the full-screen callbacks onto
/// <see cref="RewardedAdResult"/>.
///
/// A placement is only reported as Completed when the SDK's earned-reward
/// callback fires — reward fulfillment itself stays in AdRewardService.
/// </summary>
public class GoogleMobileAdsRewardedService : MonoBehaviour, IRewardedAdService
{
    [Header("Ad Unit IDs")]
    [Tooltip("Android rewarded ad unit — Google's public test unit by default.")]
    [SerializeField] private string androidAdUnitId = "ca-app-pub-3940256099942544/5224354917";
#pragma warning disable CS0414 // field only referenced under UNITY_IOS
    [Tooltip("iOS rewarded ad unit — Google's public test unit by default.")]
    [SerializeField] private string iosAdUnitId = "ca-app-pub-3940256099942544/1712485313";
#pragma warning restore CS0414 // field only referenced under UNITY_IOS

    [Header("Reload")]
    [Tooltip("Seconds to wait before retrying after a failed load.")]
    [SerializeField, Min(1f)] private float loadRetryDelay = 10f;

    private string AdUnitId =>
#if UNITY_IOS
        iosAdUnitId;
#else
        androidAdUnitId;
#endif

    private RewardedAd rewardedAd;
    private bool loading;
    private bool showing;
    private bool rewardEarned;
    private Action<RewardedAdResult> pendingCallback;
    private RewardedAdPlacement pendingPlacement;

    private void OnEnable()
    {
        GoogleMobileAdsController.OnInitialized += HandleSdkInitialized;
        if (GoogleMobileAdsController.IsInitialized)
            LoadAd();
    }

    private void OnDisable()
    {
        GoogleMobileAdsController.OnInitialized -= HandleSdkInitialized;

        // Never leave AdRewardService's request tracking or a caller's button
        // hanging when the component is disabled mid-ad or the scene unloads.
        if (pendingCallback != null)
            Resolve(RewardedAdResult.Skipped(pendingPlacement));
    }

    private void OnDestroy() => CleanupAd();

    private void HandleSdkInitialized() => LoadAd();

    public bool IsAdAvailable(RewardedAdPlacement placement)
    {
        return isActiveAndEnabled && !showing
            && rewardedAd != null && rewardedAd.CanShowAd();
    }

    public void ShowRewardedAd(RewardedAdPlacement placement, Action<RewardedAdResult> onComplete)
    {
        if (!IsAdAvailable(placement))
        {
            onComplete?.Invoke(RewardedAdResult.Unavailable(placement));
            if (rewardedAd == null) LoadAd();
            return;
        }

        showing = true;
        rewardEarned = false;
        pendingPlacement = placement;
        pendingCallback = onComplete;
        GoogleMobileAdsController.AnyFullScreenAdShowing = true;

        rewardedAd.Show((Reward reward) =>
        {
            // The SDK only raises this when the user actually earned the reward.
            rewardEarned = true;
            Debug.Log($"[GoogleMobileAdsRewardedService] Reward earned: {reward.Amount} {reward.Type}");
        });
    }

    private void LoadAd()
    {
        if (rewardedAd != null || loading || !GoogleMobileAdsController.IsInitialized)
            return;

        loading = true;
        Debug.Log("[GoogleMobileAdsRewardedService] Loading rewarded ad...");

        RewardedAd.Load(AdUnitId, new AdRequest(), (RewardedAd ad, LoadAdError error) =>
        {
            loading = false;
            if (this == null) { ad?.Destroy(); return; }

            if (error != null || ad == null)
            {
                Debug.LogWarning($"[GoogleMobileAdsRewardedService] Load failed — " +
                    $"code={error?.GetCode()} msg={error?.GetMessage()}");
                if (isActiveAndEnabled) StartCoroutine(RetryRoutine());
                return;
            }

            rewardedAd = ad;
            RegisterHandlers(rewardedAd);
            Debug.Log("[GoogleMobileAdsRewardedService] Rewarded ad loaded.");
        });
    }

    private IEnumerator RetryRoutine()
    {
        yield return new WaitForSecondsRealtime(loadRetryDelay);
        LoadAd();
    }

    private void RegisterHandlers(RewardedAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log($"[GoogleMobileAdsRewardedService] Ad closed. rewardEarned={rewardEarned}");
            Resolve(rewardEarned
                ? RewardedAdResult.Success(pendingPlacement)
                : RewardedAdResult.Skipped(pendingPlacement));
            CleanupAd();
            LoadAd();
        };
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError($"[GoogleMobileAdsRewardedService] Show failed — " +
                $"{error.GetMessage()} (code {error.GetCode()})");
            Resolve(RewardedAdResult.Failed(pendingPlacement, error.GetMessage()));
            CleanupAd();
            LoadAd();
        };
        ad.OnAdClicked += () =>
            Debug.Log("[GoogleMobileAdsRewardedService] Ad clicked.");
        ad.OnAdImpressionRecorded += () =>
            Debug.Log("[GoogleMobileAdsRewardedService] Impression recorded.");
        ad.OnAdPaid += (AdValue value) =>
            Debug.Log($"[GoogleMobileAdsRewardedService] Paid: {value.Value} {value.CurrencyCode} ({value.Precision})");
    }

    /// <summary>
    /// Single resolution point — clears in-flight state before invoking so a
    /// callback that immediately re-requests can't hit the busy guard.
    /// </summary>
    private void Resolve(RewardedAdResult result)
    {
        Action<RewardedAdResult> callback = pendingCallback;
        pendingCallback = null;
        showing = false;
        rewardEarned = false;
        GoogleMobileAdsController.AnyFullScreenAdShowing = false;

        Debug.Log($"[GoogleMobileAdsRewardedService] {result.Placement} -> {result.Status}");
        callback?.Invoke(result);
    }

    private void CleanupAd()
    {
        rewardedAd?.Destroy();
        rewardedAd = null;
    }
}
