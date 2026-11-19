using System;
using UnityEngine;

/// <summary>
/// Detects forward system-clock jumps within an active session by comparing wall-clock progression
/// against monotonic uptime (<see cref="Time.realtimeSinceStartupAsDouble"/>) or synced server UTC (L4).
/// </summary>
public static class NetworkTimeGuard
{
    private static readonly TimeSpan ForwardJumpTolerance = TimeSpan.FromMinutes(15);

    private static bool hasSessionBaseline;
    private static long baselineLocalTicks;
    private static double baselineMonotonicSeconds;

    private static bool hasTrustedServerUtc;
    private static long serverUtcOffsetTicks;

    public static Func<double> MonotonicSecondsProvider = () => Time.realtimeSinceStartupAsDouble;
    public static bool EnforceOnCustomClockForTesting { get; set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    public static void ResetSessionBaseline()
    {
        hasSessionBaseline = false;
        baselineLocalTicks = 0;
        baselineMonotonicSeconds = 0d;
        hasTrustedServerUtc = false;
        serverUtcOffsetTicks = 0;
        EnforceOnCustomClockForTesting = false;
        MonotonicSecondsProvider = () => Time.realtimeSinceStartupAsDouble;
    }

    /// <summary>Records an authoritative server UTC timestamp so local wall-clock forward skew can be clamped.</summary>
    public static void SyncTrustedUtc(DateTime serverUtcNow)
    {
        DateTime utc = serverUtcNow.Kind == DateTimeKind.Utc ? serverUtcNow : serverUtcNow.ToUniversalTime();
        serverUtcOffsetTicks = utc.Ticks - DateTime.UtcNow.Ticks;
        hasTrustedServerUtc = true;
    }

    public static bool EvaluateForwardTamper(
        DateTime rawLocalNow,
        bool usingCustomClockProvider,
        out DateTime clampedLocalNow)
    {
        if (usingCustomClockProvider && !EnforceOnCustomClockForTesting)
        {
            clampedLocalNow = rawLocalNow;
            return false;
        }

        double monotonicNow = MonotonicSecondsProvider != null ? MonotonicSecondsProvider() : Time.realtimeSinceStartupAsDouble;

        if (!hasSessionBaseline)
        {
            hasSessionBaseline = true;
            baselineLocalTicks = rawLocalNow.Ticks;
            baselineMonotonicSeconds = monotonicNow;

            if (hasTrustedServerUtc && Math.Abs(serverUtcOffsetTicks) > ForwardJumpTolerance.Ticks)
            {
                DateTime trustedLocal = DateTime.UtcNow.AddTicks(serverUtcOffsetTicks).ToLocalTime();
                if (rawLocalNow.Ticks > trustedLocal.Ticks + ForwardJumpTolerance.Ticks)
                {
                    clampedLocalNow = trustedLocal;
                    return true;
                }
            }

            clampedLocalNow = rawLocalNow;
            return false;
        }

        double elapsedSeconds = Math.Max(0d, monotonicNow - baselineMonotonicSeconds);
        long expectedTicks = baselineLocalTicks + TimeSpan.FromSeconds(elapsedSeconds).Ticks;
        long maxAllowedTicks = expectedTicks + ForwardJumpTolerance.Ticks;

        if (rawLocalNow.Ticks > maxAllowedTicks)
        {
            clampedLocalNow = new DateTime(expectedTicks, rawLocalNow.Kind);
            return true;
        }

        clampedLocalNow = rawLocalNow;
        return false;
    }
}
