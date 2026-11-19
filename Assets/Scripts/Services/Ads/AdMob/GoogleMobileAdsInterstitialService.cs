using System;
using System.Collections;
using GoogleMobileAds.Api;
using GoogleMobileAds.Samples;
using UnityEngine;

/// <summary>
/// AdMob-backed <see cref="IInterstitialAdService"/> aligned with Gravita Economy GDD §8:
///   - Permanently disabled when <c>Remove Ads</c> is owned (<see cref="AdPolicyCoordinator.IsRemoveAdsActive"/>).
///   - Never triggers during active puzzle gameplay, deaths, restarts, or failed runs.
///   - Counts completed levels via <see cref="AdPolicyCoordinator.RecordCompletedLevelForInterstitialCadence"/>
///     and presents on Result Screen exit transitions every 2–3 completed levels (unless suppressed by a Result Rewarded Ad).
/// </summary>
public class GoogleMobileAdsInterstitialService : MonoBehaviour, IInterstitialAdService
{
    public static GoogleMobileAdsInterstitialService Instance { get; private set; }

    [Header("Ad Unit IDs")]
    [Tooltip("Android interstitial ad unit — Google's public test unit by default.")]
    [SerializeField] private string androidAdUnitId = "ca-app-pub-3940256099942544/1033173712";
#pragma warning disable CS0414
    [Tooltip("iOS interstitial ad unit — Google's public test unit by default.")]
    [SerializeField] private string iosAdUnitId = "ca-app-pub-3940256099942544/4411468910";
#pragma warning restore CS0414

    [Header("Trigger (Managed by AdPolicyCoordinator per GDD §8)")]
    [Tooltip("Legacy unconditional map-return trigger. Disabled per GDD §8.2–§8.3.")]
    [SerializeField] private bool showOnReturnToMap = false;
    [Tooltip("Tracks completed/failed play cadence toward the 2–3 run interstitial schedule.")]
    [SerializeField] private bool showAfterRandomLevelCount = true;
    [SerializeField] private bool bypassCooldownForLevelCounter = true;
    [SerializeField, Min(0f)] private float minIntervalSeconds = 0f;

    [Header("Reload")]
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
    private bool flowSubscribed;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void Start()
    {
        SubscribeFlowController();
    }

    private void OnEnable()
    {
        GoogleMobileAdsController.OnInitialized += HandleSdkInitialized;
        if (GoogleMobileAdsController.IsInitialized && !AdPolicyCoordinator.IsRemoveAdsActive())
            LoadAd();

        SubscribeFlowController();
    }

    private void OnDisable()
    {
        GoogleMobileAdsController.OnInitialized -= HandleSdkInitialized;
        UnsubscribeFlowController();

        if (pendingCallback != null)
            Resolve(false);
    }

    private void SubscribeFlowController()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();

        if (flowController != null && !flowSubscribed)
        {
            flowSubscribed = true;
            flowController.OnReturnToMapRequested += HandleReturnToMap;
            flowController.OnLevelCompleted += HandleLevelCompleted;
            flowController.OnLevelFailed += HandleLevelFailed;
        }
    }

    private void UnsubscribeFlowController()
    {
        if (flowController != null && flowSubscribed)
        {
            flowSubscribed = false;
            flowController.OnReturnToMapRequested -= HandleReturnToMap;
            flowController.OnLevelCompleted -= HandleLevelCompleted;
            flowController.OnLevelFailed -= HandleLevelFailed;
        }
    }

    private void HandleLevelCompleted()
    {
        if (!showAfterRandomLevelCount || AdPolicyCoordinator.IsRemoveAdsActive())
            return;

        // Only record completion cadence here; actual presentation happens on Result Screen exit
        // via AdPolicyCoordinator.TryShowPostResultTransitionInterstitial() so Goal cutscene & Result UI are never interrupted!
        AdPolicyCoordinator.RecordCompletedLevelForInterstitialCadence();
    }

    private void HandleLevelFailed(string reason)
    {
        if (!showAfterRandomLevelCount || AdPolicyCoordinator.IsRemoveAdsActive())
            return;

        AdPolicyCoordinator.RecordFailedRunForInterstitialCadence();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
        CleanupAd();
    }

    private void HandleSdkInitialized()
    {
        if (!AdPolicyCoordinator.IsRemoveAdsActive())
            LoadAd();
    }

    private void HandleReturnToMap()
    {
        if (showOnReturnToMap && EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems && !AdPolicyCoordinator.IsRemoveAdsActive())
        {
            TryShow();
        }
    }

    public bool IsAdAvailable()
    {
        if (AdPolicyCoordinator.IsRemoveAdsActive())
            return false;

        return isActiveAndEnabled && !showing
            && interstitialAd != null && interstitialAd.CanShowAd();
    }

    public bool TryShow()
    {
        if (AdPolicyCoordinator.IsRemoveAdsActive())
            return false;

        return TryShowInternal(ignoreCooldown: false);
    }

    internal bool TryShowFromCoordinator()
    {
        return TryShowInternal(ignoreCooldown: bypassCooldownForLevelCounter || EconomyBalanceConfig.Active.InterstitialCooldownSeconds <= 0f);
    }

    private bool TryShowInternal(bool ignoreCooldown)
    {
        if (AdPolicyCoordinator.IsRemoveAdsActive())
            return false;

        if (!isActiveAndEnabled || GoogleMobileAdsController.AnyFullScreenAdShowing)
            return false;

        float effectiveCooldown = Mathf.Max(minIntervalSeconds, EconomyBalanceConfig.Active.InterstitialCooldownSeconds);
        if (!ignoreCooldown && Time.unscaledTime - lastShowTime < effectiveCooldown)
            return false;

        if (!IsAdAvailable())
        {
            LoadAd();
            return false;
        }

        lastShowTime = Time.unscaledTime;
        ShowInterstitial();
        return true;
    }

    public void ShowInterstitial(Action<bool> onComplete = null)
    {
        if (AdPolicyCoordinator.IsRemoveAdsActive() || !IsAdAvailable() || GoogleMobileAdsController.AnyFullScreenAdShowing)
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
        if (AdPolicyCoordinator.IsRemoveAdsActive() || interstitialAd != null || loading || !GoogleMobileAdsController.IsInitialized)
            return;

        loading = true;
        Debug.Log("[GoogleMobileAdsInterstitialService] Loading interstitial...");

        InterstitialAd.Load(AdUnitId, new AdRequest(), (InterstitialAd ad, LoadAdError error) =>
        {
            loading = false;
            if (this == null || AdPolicyCoordinator.IsRemoveAdsActive())
            {
                ad?.Destroy();
                return;
            }

            if (error != null || ad == null)
            {
                Debug.LogWarning($"[GoogleMobileAdsInterstitialService] Load failed — code={error?.GetCode()} msg={error?.GetMessage()}");
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
            Debug.LogError($"[GoogleMobileAdsInterstitialService] Show failed — {error.GetMessage()} (code {error.GetCode()})");
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
