using System.Collections.Generic;

/// <summary>
/// Abstraction for the game's analytics surface. Gameplay and UI code
/// depends on this interface (via AnalyticsService.Instance), never on
/// a concrete provider.
/// </summary>
public interface IAnalyticsService
{
    void TrackEvent(string eventName);
    void TrackEvent(string eventName, Dictionary<string, object> parameters);
}

