using System;
using Solo.MOST_IN_ONE;
using UnityEngine;

/// <summary>
/// Production <see cref="IHapticsBackend"/> adapter wrapping <see cref="MOST_HapticFeedback"/> presets.
/// This is the sole class in the codebase coupled to <c>Solo.MOST_IN_ONE</c>.
/// Uses synchronous main-thread presets exclusively (never async patterns or curves).
/// </summary>
public sealed class MostHapticsBackend : IHapticsBackend
{
    private bool loggedExceptionOnce;

    public bool IsSupported
    {
        get
        {
            try
            {
                return MOST_HapticFeedback.IsSupported();
            }
            catch (Exception ex)
            {
                LogOnce("IsSupported check failed", ex);
                return false;
            }
        }
    }

    public void Play(HapticEvent hapticEvent)
    {
        try
        {
            MOST_HapticFeedback.Generate(MapPreset(hapticEvent));
        }
        catch (Exception ex)
        {
            LogOnce($"Playback failed for {hapticEvent}", ex);
        }
    }

    public static MOST_HapticFeedback.HapticTypes MapPreset(HapticEvent hapticEvent)
    {
        return hapticEvent switch
        {
            HapticEvent.UISelect      => MOST_HapticFeedback.HapticTypes.Selection,
            HapticEvent.WallImpact    => MOST_HapticFeedback.HapticTypes.LightImpact,
            HapticEvent.GravityAnchor => MOST_HapticFeedback.HapticTypes.RigidImpact,
            HapticEvent.PadRedirect   => MOST_HapticFeedback.HapticTypes.SoftImpact,
            HapticEvent.LaunchStart   => MOST_HapticFeedback.HapticTypes.MediumImpact,
            HapticEvent.SwitchToggle  => MOST_HapticFeedback.HapticTypes.Selection,
            HapticEvent.Death         => MOST_HapticFeedback.HapticTypes.Failure,
            _                         => MOST_HapticFeedback.HapticTypes.LightImpact,
        };
    }

    private void LogOnce(string context, Exception ex)
    {
        if (loggedExceptionOnce)
            return;

        loggedExceptionOnce = true;
        Debug.LogWarning($"[MostHapticsBackend] {context}: {ex.Message}");
    }
}

