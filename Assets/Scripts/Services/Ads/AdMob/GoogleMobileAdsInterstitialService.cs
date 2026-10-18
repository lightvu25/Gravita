using System;
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Samples;
using UnityEngine;

/// <summary>
/// AdMob-backed <see cref="IInterstitialAdService"/>. Waits for the one-time
/// SDK initialization owned by <see cref="GoogleMobileAdsController"/>, keeps
/// one interstitial loaded, and reloads after every close or failure.
///
/// Interstitials never grant rewards. The built-in trigger shows one when the
/// game returns to the map (a safe transition point — never during active
/// gameplay), throttled by <see cref="minIntervalSeconds"/>.
/// </summary>
public class GoogleMobileAdsInterstitialService : MonoBehaviour, IInterstitialAdService
{
    public static GoogleMobileAdsInterstitialService Instance { get; private set; }

    [Header("Ad Unit IDs")]
    [Tooltip("Android interstitial ad unit — Google's public test unit by default.")]
    [SerializeField] private string androidAdUnitId = "ca-app-pub-3940256099942544/1033173712";
#pragma warning disable CS0414 // field only referenced under UNITY_IOS
    [Tooltip("iOS interstitial ad unit — Google's public test unit by default.")]
    [SerializeField] private string iosAdUnitId = "ca-app-pub-3940256099942544/4411468910";
#pragma warning restore CS0414 // field only referenced under UNITY_IOS

    [Header("Trigger")]
    [Tooltip("Attempt an interstitial whenever the game returns to the map.")]
    [SerializeField] private bool showOnReturnToMap = true;
    [Tooltip("Minimum seconds between shown interstitials.")]
    [SerializeField, Min(0f)] private float minIntervalSeconds = 120f;

    [Header("Reload")]
    [Tooltip("Seconds to wait before retrying after a failed load.")]
    [SerializeField, Min(1f)] private float loadRetryDelay = 10f;

    private string AdUnitId =>
#if UNITY_IOS
        iosAdUnitId;
#else
        androidAdUnitId;
#endif

    private InterstitialAd interstitialAd;
    private bool loading;
    private bool showing;
    private float lastShowTime = float.NegativeInfinity;
    private Action<bool> pendingCallback;
    private LevelFlowController flowController;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnEnable()
    {
        GoogleMobileAdsController.OnInitialized += HandleSdkInitialized;
        if (GoogleMobileAdsController.IsInitialized)
            LoadAd();

        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (flowController != null)
            flowController.OnReturnToMapRequested += HandleReturnToMap;
    }

    private void OnDisable()
    {
        GoogleMobileAdsController.OnInitialized -= HandleSdkInitialized;
        if (flowController != null)
            flowController.OnReturnToMapRequested -= HandleReturnToMap;

        if (pendingCallback != null)
            Resolve(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        CleanupAd();
    }

    private void HandleSdkInitialized() => LoadAd();

    private void HandleReturnToMap()
    {
        if (showOnReturnToMap) TryShow();
    }

    public bool IsAdAvailable()
    {
        return isActiveAndEnabled && !showing
            && interstitialAd != null && interstitialAd.CanShowAd();
    }

    /// <summary>
    /// Shows an interstitial if one is loaded, the cooldown has elapsed, and no
    /// other full-screen ad is up. Returns false without side effects otherwise.
    /// </summary>
    public bool TryShow()
    {
        if (!isActiveAndEnabled || GoogleMobileAdsController.AnyFullScreenAdShowing)
            return false;
        if (Time.unscaledTime - lastShowTime < minIntervalSeconds)
            return false;
        if (!IsAdAvailable())
        {
            LoadAd();   // nothing ready — prime the load for the next transition
            return false;
        }

        lastShowTime = Time.unscaledTime;
        ShowInterstitial();
        return true;
    }

    public void ShowInterstitial(Action<bool> onComplete = null)
    {
        if (!IsAdAvailable() || GoogleMobileAdsController.AnyFullScreenAdShowing)
        {
            onComplete?.Invoke(false);
            return;
        }

        showing = true;
        pendingCallback = onComplete;
        GoogleMobileAdsController.AnyFullScreenAdShowing = true;
        interstitialAd.Show();
    }

    private void LoadAd()
    {
        if (interstitialAd != null || loading || !GoogleMobileAdsController.IsInitialized)
            return;

        loading = true;
        Debug.Log("[GoogleMobileAdsInterstitialService] Loading interstitial...");

        InterstitialAd.Load(AdUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
        {
            loading = false;
            if (this == null) { ad?.Destroy(); return; }

            if (error != null || ad == null)
            {
                Debug.LogWarning($"[GoogleMobileAdsInterstitialService] Load failed — " +
                    $"code={error?.GetCode()} msg={error?.GetMessage()}");
                if (isActiveAndEnabled) StartCoroutine(RetryRoutine());
                return;
            }

            interstitialAd = ad;
            RegisterHandlers(interstitialAd);
            Debug.Log("[GoogleMobileAdsInterstitialService] Interstitial loaded.");
        });
    }

    private IEnumerator RetryRoutine()
    {
        yield return new WaitForSecondsRealtime(loadRetryDelay);
        LoadAd();
    }

    private void RegisterHandlers(InterstitialAd ad)
    {
        ad.OnAdFullScreenContentClosed += () =>
        {
            Debug.Log("[GoogleMobileAdsInterstitialService] Ad closed.");
            Resolve(true);
            CleanupAd();
            LoadAd();
        };
        ad.OnAdFullScreenContentFailed += (AdError error) =>
        {
            Debug.LogError($"[GoogleMobileAdsInterstitialService] Show failed — " +
                $"{error.GetMessage()} (code {error.GetCode()})");
            Resolve(false);
            CleanupAd();
            LoadAd();
        };
        ad.OnAdClicked += () =>
            Debug.Log("[GoogleMobileAdsInterstitialService] Ad clicked.");
        ad.OnAdImpressionRecorded += () =>
            Debug.Log("[GoogleMobileAdsInterstitialService] Impression recorded.");
        ad.OnAdPaid += (AdValue value) =>
            Debug.Log($"[GoogleMobileAdsInterstitialService] Paid: {value.Value} {value.CurrencyCode} ({value.Precision})");
    }

    private void Resolve(bool shown)
    {
        Action<bool> callback = pendingCallback;
        pendingCallback = null;
        showing = false;
        GoogleMobileAdsController.AnyFullScreenAdShowing = false;
        callback?.Invoke(shown);
    }

    private void CleanupAd()
    {
        interstitialAd?.Destroy();
        interstitialAd = null;
    }
}
