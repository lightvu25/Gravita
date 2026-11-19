using System;
using System.Globalization;

/// <summary>
/// Injectable local clock abstraction enabling deterministic testing of 00:00 daily resets,
/// Monday 00:00 weekly resets, Rewarded Ad daily caps, and anti-clock-rollback guards (GDD §7.3 & §10.5).
/// </summary>
public interface ILocalClockProvider
{
    DateTime LocalNow { get; }
}

/// <summary>
/// Default production clock reading the device's local wall clock (<see cref="DateTime.Now"/>).
/// </summary>
public sealed class SystemLocalClockProvider : ILocalClockProvider
{
    public static readonly SystemLocalClockProvider Instance = new SystemLocalClockProvider();

    private SystemLocalClockProvider() { }

    public DateTime LocalNow => DateTime.Now;
}

/// <summary>
/// Mutable local clock used by automated NUnit EditMode tests.
/// </summary>
public sealed class FixedLocalClockProvider : ILocalClockProvider
{
    public DateTime Current { get; set; }

    public FixedLocalClockProvider(DateTime initialLocalTime)
    {
        Current = initialLocalTime;
    }

    public DateTime LocalNow => Current;
}

/// <summary>
/// Shared local calendar helper and monotonic high-water clock guard (GDD §10.5).
/// </summary>
public static class LocalClock
{
    private static readonly long MinorDriftToleranceTicks = TimeSpan.FromMinutes(5).Ticks;
    private static readonly long HighWaterPersistIntervalTicks = TimeSpan.FromMinutes(1).Ticks;
    private static ILocalClockProvider customProvider;
    private static long cachedDateTicks = -1;
    private static string cachedDateKey;
    private static long cachedMondayDateTicks = -1;
    private static string cachedMondayKey;

    public static ILocalClockProvider Provider
    {
        get => customProvider ?? SystemLocalClockProvider.Instance;
        set => customProvider = value;
    }

    public static DateTime Now => Provider.LocalNow;

    public static void ResetToSystemClock()
    {
        customProvider = null;
        NetworkTimeGuard.ResetSessionBaseline();
    }

    public static int DayOfWeekToOneBased(DayOfWeek dayOfWeek)
    {
        return dayOfWeek == DayOfWeek.Sunday ? 7 : (int)dayOfWeek;
    }

    public static DateTime GetMondayOfWeek(DateTime localDateTime)
    {
        DateTime dateOnly = localDateTime.Date;
        int dayIdx = DayOfWeekToOneBased(dateOnly.DayOfWeek);
        return dateOnly.AddDays(-(dayIdx - 1));
    }

    public static string FormatDateKey(DateTime localDateTime)
    {
        long dateTicks = localDateTime.Date.Ticks;
        if (dateTicks == cachedDateTicks && cachedDateKey != null)
            return cachedDateKey;

        cachedDateTicks = dateTicks;
        cachedDateKey = localDateTime.Date.ToString("yyyy_MM_dd", CultureInfo.InvariantCulture);
        return cachedDateKey;
    }

    public static string FormatMondayKey(DateTime localDateTime)
    {
        long dateTicks = localDateTime.Date.Ticks;
        if (dateTicks == cachedMondayDateTicks && cachedMondayKey != null)
            return cachedMondayKey;

        cachedMondayDateTicks = dateTicks;
        cachedMondayKey = FormatDateKey(GetMondayOfWeek(localDateTime));
        return cachedMondayKey;
    }

    /// <summary>
    /// Evaluates the current local clock against <see cref="ProfileData.highWaterLocalTicks"/>.
    /// If the device clock was rolled backward beyond 5 minutes tolerance, flags <paramref name="isClockRolledBack"/>
    /// and pins <paramref name="effectiveLocalNow"/> to the high-water timestamp so daily/weekly resets
    /// and ad caps cannot be exploited by rewinding system time (GDD §10.5).
    /// Returns true only when <see cref="ProfileData.highWaterLocalTicks"/> crossed a day boundary, was initialized,
    /// or advanced by at least 1 minute so read-only UI queries never spam synchronous disk writes every tick.
    /// </summary>
    public static bool EvaluateClockAndUpdateHighWater(
        ProfileData profile,
        DateTime rawLocalNow,
        out DateTime effectiveLocalNow,
        out bool isClockRolledBack)
    {
        if (profile == null)
        {
            effectiveLocalNow = rawLocalNow;
            isClockRolledBack = false;
            return false;
        }

        long rawTicks = rawLocalNow.Ticks;
        if (profile.highWaterLocalTicks > 0 && rawTicks < profile.highWaterLocalTicks - MinorDriftToleranceTicks)
        {
            isClockRolledBack = true;
            effectiveLocalNow = new DateTime(profile.highWaterLocalTicks);
            return false;
        }

        if (NetworkTimeGuard.EvaluateForwardTamper(rawLocalNow, customProvider != null, out DateTime clampedNow))
        {
            isClockRolledBack = true;
            effectiveLocalNow = profile.highWaterLocalTicks > clampedNow.Ticks
                ? new DateTime(profile.highWaterLocalTicks)
                : clampedNow;
            return false;
        }

        isClockRolledBack = false;
        effectiveLocalNow = rawLocalNow;
        if (rawTicks > profile.highWaterLocalTicks)
        {
            long previousTicks = profile.highWaterLocalTicks;
            profile.highWaterLocalTicks = rawTicks;

            if (previousTicks <= 0)
                return true;

            DateTime previousDate = new DateTime(previousTicks).Date;
            if (rawLocalNow.Date != previousDate)
                return true;

            return (rawTicks - previousTicks) >= HighWaterPersistIntervalTicks;
        }

        return false;
    }
}

