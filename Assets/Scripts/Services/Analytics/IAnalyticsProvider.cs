using System.Collections.Generic;

/// <summary>
/// Backend abstraction for event delivery. The Mock implementation records
/// events locally; a production provider (Firebase, GA4, etc.) can be
/// swapped in via AnalyticsService.SetProvider without touching gameplay code.
/// </summary>
public interface IAnalyticsProvider
{
    void TrackEvent(string eventName, Dictionary<string, object> parameters);
}

