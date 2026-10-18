using System;
using System.Collections.Generic;

/// <summary>
/// Immutable record of a single tracked event, kept by MockAnalyticsProvider
/// for inspection during development and testing.
/// </summary>
public class AnalyticsEventRecord
{
    public string EventName { get; }
    public IReadOnlyDictionary<string, object> Parameters { get; }
    public DateTime TimestampUtc { get; }

    public AnalyticsEventRecord(string eventName, Dictionary<string, object> parameters)
    {
        EventName = eventName;
        Parameters = parameters != null
            ? new Dictionary<string, object>(parameters)
            : new Dictionary<string, object>();
        TimestampUtc = DateTime.UtcNow;
    }
}

