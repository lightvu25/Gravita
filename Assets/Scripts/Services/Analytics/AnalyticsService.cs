using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central analytics service. Scene singleton that owns an IAnalyticsProvider
/// (MockAnalyticsProvider for Phase 25) and exposes the IAnalyticsService
/// abstraction to the rest of the game.
///
/// Safety contract: analytics must NEVER break gameplay. Every call is
/// null-safe and wrapped so a throwing provider cannot propagate.
///
/// session_start fires exactly once per application/Play-Mode session —
/// the static guard survives scene reloads but resets on domain reload.
/// </summary>
public class AnalyticsService : MonoBehaviour, IAnalyticsService
{
    /// <summary>Analytics abstraction for callers that prefer the interface.</summary>
    public static IAnalyticsService Instance { get; private set; }

    /// <summary>The active provider — exposed for the observer/tests to inspect.</summary>
    public IAnalyticsProvider Provider => provider;

    private IAnalyticsProvider provider;
    private static bool sessionStarted;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        Instance = null;
        sessionStarted = false;
    }

    public static void ResetForTesting() => ResetStaticState();

    private void Awake()
    {
        if (Instance != null && !ReferenceEquals(Instance, this))
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (provider == null)
            provider = new MockAnalyticsProvider();

        if (!sessionStarted)
        {
            sessionStarted = true;
            Track(AnalyticsEvents.SessionStart);
        }
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    /// <summary>
    /// Swaps the event delivery backend. Production providers plug in here
    /// without any gameplay code changes.
    /// </summary>
    public void SetProvider(IAnalyticsProvider newProvider)
    {
        provider = newProvider ?? new MockAnalyticsProvider();
    }

    /// <summary>
    /// Connects the live <see cref="FirebaseAnalyticsProvider"/>, replaying any events recorded
    /// by the startup <see cref="MockAnalyticsProvider"/> before Firebase dependencies resolved.
    /// </summary>
    public static void ConnectFirebaseAnalytics(
        string initialUserId = null,
        FirebaseAnalyticsProvider customProvider = null)
    {
        if (!(Instance is AnalyticsService svc))
            return;

        if (customProvider == null && svc.provider is FirebaseAnalyticsProvider existingFb)
        {
            existingFb.MarkBackendReady(initialUserId);
            return;
        }

        FirebaseAnalyticsProvider fbProvider = customProvider ?? new FirebaseAnalyticsProvider();
        if (svc.provider is MockAnalyticsProvider mock)
        {
            IReadOnlyList<AnalyticsEventRecord> buffered = mock.Events;
            for (int i = 0; i < buffered.Count; i++)
            {
                AnalyticsEventRecord rec = buffered[i];
                if (rec != null)
                    fbProvider.TrackEvent(rec.EventName, FirebaseAnalyticsProvider.CloneDictionary(rec.Parameters));
            }
        }

        svc.SetProvider(fbProvider);
        fbProvider.MarkBackendReady(initialUserId);
    }

    /// <summary>
    /// Updates the analytics user identifier whenever authentication transitions.
    /// </summary>
    public static void UpdateUserId(string userId)
    {
        if (Instance is AnalyticsService svc && svc.provider is FirebaseAnalyticsProvider fbProvider)
            fbProvider.SetUserId(userId);
    }

    /// <summary>
    /// Static convenience façade — safe to call from anywhere even when no
    /// AnalyticsService exists in the scene. Never throws.
    /// </summary>
    public static void Track(string eventName, Dictionary<string, object> parameters = null)
    {
        var service = Instance;
        if (service == null) return;

        try
        {
            service.TrackEvent(eventName, parameters);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AnalyticsService] Suppressed analytics failure for '{eventName}': {e.Message}");
        }
    }

    public void TrackEvent(string eventName)
    {
        TrackEvent(eventName, null);
    }

    public void TrackEvent(string eventName, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrEmpty(eventName)) return;

        var p = provider;
        if (p == null) return;

        try
        {
            p.TrackEvent(eventName, parameters);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[AnalyticsService] Provider threw for '{eventName}': {e.Message}");
        }
    }
}

