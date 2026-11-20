using System;
using System.Collections.Generic;
using System.Text;
using Firebase.Analytics;
using UnityEngine;

/// <summary>
/// Production <see cref="IAnalyticsProvider"/> backed by Firebase Analytics (GA4).
/// Buffers early startup events until Firebase dependencies finish initializing,
/// enforces GA4 naming/length constraints, and accepts injected delegates for EditMode tests.
/// </summary>
public class FirebaseAnalyticsProvider : IAnalyticsProvider
{
    public const int MaxBufferedEvents = 64;
    public const int MaxIdentifierLength = 40;
    public const int MaxParameterCount = 25;
    public const int MaxStringValueLength = 100;

    private readonly Action<string, Parameter[]> logWithParamsAction;
    private readonly Action<string> logWithoutParamsAction;
    private readonly Action<string> setUserIdAction;
    private readonly Action<bool> setCollectionEnabledAction;
    private readonly bool echoToConsoleInEditor;

    private readonly Queue<(string EventName, Dictionary<string, object> Parameters)> earlyBuffer =
        new Queue<(string EventName, Dictionary<string, object> Parameters)>(MaxBufferedEvents);

    public bool IsBackendReady { get; private set; }
    public string LastUserId { get; private set; }
    public int BufferedEventCount => earlyBuffer.Count;

    public FirebaseAnalyticsProvider(
        Action<string, Parameter[]> logWithParams = null,
        Action<string> logWithoutParams = null,
        Action<string> setUserId = null,
        Action<bool> setCollectionEnabled = null,
        bool echoToConsoleInEditor = true)
    {
        logWithParamsAction = logWithParams ?? ((name, parameters) => FirebaseAnalytics.LogEvent(name, parameters));
        logWithoutParamsAction = logWithoutParams ?? (name => FirebaseAnalytics.LogEvent(name));
        setUserIdAction = setUserId ?? (uid => FirebaseAnalytics.SetUserId(uid));
        setCollectionEnabledAction = setCollectionEnabled ?? (enabled => FirebaseAnalytics.SetAnalyticsCollectionEnabled(enabled));
        echoToConsoleInEditor = Application.isEditor && echoToConsoleInEditor;
    }

    public void TrackEvent(string eventName, Dictionary<string, object> parameters)
    {
        if (string.IsNullOrWhiteSpace(eventName))
            return;

        if (!IsBackendReady)
        {
            EnqueueBuffered(eventName, parameters);
            return;
        }

        DispatchToBackend(eventName, parameters);
    }

    public void MarkBackendReady(string initialUserId = null)
    {
        IsBackendReady = true;

        try
        {
            setCollectionEnabledAction?.Invoke(true);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAnalyticsProvider] SetAnalyticsCollectionEnabled failed: {e.Message}");
        }

        string resolvedUserId = !string.IsNullOrWhiteSpace(initialUserId) ? initialUserId.Trim() : LastUserId;
        if (!string.IsNullOrEmpty(resolvedUserId))
            SetUserId(resolvedUserId);

        while (earlyBuffer.Count > 0)
        {
            var buffered = earlyBuffer.Dequeue();
            DispatchToBackend(buffered.EventName, buffered.Parameters);
        }
    }

    public void SetUserId(string userId)
    {
        LastUserId = string.IsNullOrWhiteSpace(userId) ? null : userId.Trim();
        if (!IsBackendReady)
            return;

        try
        {
            setUserIdAction?.Invoke(LastUserId);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAnalyticsProvider] SetUserId failed: {e.Message}");
        }
    }

    private void EnqueueBuffered(string eventName, Dictionary<string, object> parameters)
    {
        while (earlyBuffer.Count >= MaxBufferedEvents)
            earlyBuffer.Dequeue();

        earlyBuffer.Enqueue((eventName, CloneDictionary(parameters)));
    }

    private void DispatchToBackend(string rawEventName, Dictionary<string, object> rawParameters)
    {
        string safeName = SanitizeEventName(rawEventName);
        Parameter[] fbParams = BuildParameters(rawParameters);

        if (echoToConsoleInEditor)
            Debug.Log($"[FirebaseAnalytics] {safeName} (params={fbParams.Length})");

        try
        {
            if (fbParams.Length == 0)
                logWithoutParamsAction?.Invoke(safeName);
            else
                logWithParamsAction?.Invoke(safeName, fbParams);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAnalyticsProvider] LogEvent('{safeName}') failed: {e.Message}");
        }
    }

    public static string SanitizeEventName(string raw) =>
        SanitizeIdentifier(raw, "e_", remapSessionStart: true);

    public static string SanitizeParamKey(string raw) =>
        SanitizeIdentifier(raw, "p_", remapSessionStart: false);

    private static string SanitizeIdentifier(string raw, string digitPrefix, bool remapSessionStart)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "unknown";

        string lower = raw.Trim().ToLowerInvariant();
        var sb = new StringBuilder(lower.Length);
        bool lastWasUnderscore = false;

        for (int i = 0; i < lower.Length; i++)
        {
            char c = lower[i];
            bool isAlphaNum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (isAlphaNum)
            {
                sb.Append(c);
                lastWasUnderscore = false;
            }
            else if (!lastWasUnderscore && sb.Length > 0)
            {
                sb.Append('_');
                lastWasUnderscore = true;
            }
        }

        string cleaned = sb.ToString().Trim('_');
        if (string.IsNullOrEmpty(cleaned))
            return "unknown";

        if (remapSessionStart && cleaned == "session_start")
            return "game_session_start";

        if (cleaned.StartsWith("firebase_", StringComparison.Ordinal) ||
            cleaned.StartsWith("google_", StringComparison.Ordinal) ||
            cleaned.StartsWith("ga_", StringComparison.Ordinal))
        {
            cleaned = "app_" + cleaned;
        }
        else if (cleaned[0] >= '0' && cleaned[0] <= '9')
        {
            cleaned = digitPrefix + cleaned;
        }

        if (cleaned.Length > MaxIdentifierLength)
            cleaned = cleaned.Substring(0, MaxIdentifierLength);

        return cleaned;
    }

    public static Parameter[] BuildParameters(Dictionary<string, object> raw)
    {
        if (raw == null || raw.Count == 0)
            return Array.Empty<Parameter>();

        var result = new List<Parameter>(Mathf.Min(raw.Count, MaxParameterCount));
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);

        foreach (var kvp in raw)
        {
            if (result.Count >= MaxParameterCount)
                break;

            if (string.IsNullOrWhiteSpace(kvp.Key))
                continue;

            string safeKey = SanitizeParamKey(kvp.Key);
            if (!seenKeys.Add(safeKey))
                continue;

            result.Add(CreateParameter(safeKey, kvp.Value));
        }

        return result.ToArray();
    }

    private static Parameter CreateParameter(string key, object value)
    {
        switch (value)
        {
            case null:
                return new Parameter(key, string.Empty);
            case bool b:
                return new Parameter(key, b ? 1L : 0L);
            case byte by:
                return new Parameter(key, (long)by);
            case sbyte sb:
                return new Parameter(key, (long)sb);
            case short s:
                return new Parameter(key, (long)s);
            case ushort us:
                return new Parameter(key, (long)us);
            case int i:
                return new Parameter(key, (long)i);
            case uint ui:
                return new Parameter(key, (long)ui);
            case long l:
                return new Parameter(key, l);
            case float f:
                return new Parameter(key, float.IsNaN(f) || float.IsInfinity(f) ? 0d : (double)f);
            case double d:
                return new Parameter(key, double.IsNaN(d) || double.IsInfinity(d) ? 0d : d);
            case decimal dec:
                return new Parameter(key, (double)dec);
            case Enum en:
                return new Parameter(key, TruncateValue(en.ToString()));
            default:
                return new Parameter(key, TruncateValue(value.ToString()));
        }
    }

    private static string TruncateValue(string raw)
    {
        if (string.IsNullOrEmpty(raw))
            return string.Empty;

        return raw.Length <= MaxStringValueLength ? raw : raw.Substring(0, MaxStringValueLength);
    }

    internal static Dictionary<string, object> CloneDictionary(IReadOnlyDictionary<string, object> source)
    {
        if (source == null || source.Count == 0)
            return null;

        var copy = new Dictionary<string, object>(source.Count);
        foreach (var kvp in source)
            copy[kvp.Key] = kvp.Value;
        return copy;
    }
}
