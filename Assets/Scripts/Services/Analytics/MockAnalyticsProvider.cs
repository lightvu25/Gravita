using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Local mock analytics provider for Phase 25. Records every event in memory
/// and logs it to the console so the stream can be inspected during
/// development. No network calls, no external SDK.
/// </summary>
public class MockAnalyticsProvider : IAnalyticsProvider
{
    private readonly List<AnalyticsEventRecord> events = new List<AnalyticsEventRecord>();

    /// <summary>When true, TrackEvent throws — used to verify the service's failure-safety contract.</summary>
    public bool SimulateFailures;

    /// <summary>All events recorded this session, in order.</summary>
    public IReadOnlyList<AnalyticsEventRecord> Events => events;

    public void TrackEvent(string eventName, Dictionary<string, object> parameters)
    {
        if (SimulateFailures)
            throw new System.InvalidOperationException("MockAnalyticsProvider simulated failure.");

        events.Add(new AnalyticsEventRecord(eventName, parameters));
        Debug.Log($"[Analytics] {eventName}{FormatParameters(parameters)}");
    }

    public void Clear()
    {
        events.Clear();
    }

    private static string FormatParameters(Dictionary<string, object> parameters)
    {
        if (parameters == null || parameters.Count == 0) return "";

        var parts = new List<string>();
        foreach (var kvp in parameters)
            parts.Add($"{kvp.Key}={kvp.Value}");

        return " {" + string.Join(", ", parts) + "}";
    }
}

