using UnityEngine;

/// <summary>
/// Configures smooth frame pacing before any scene loads:
/// unlocks native 60-120 Hz targetFrameRate on mobile (instead of Unity's 30 FPS default)
/// and enables display-synchronized VSync on desktop platforms.
/// </summary>
public static class FramePacingBootstrap
{
    private const int MinMobileFrameRate = 60;
    private const int MaxMobileFrameRate = 120;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void InitializeOnLoad()
    {
        if (!Application.isPlaying)
            return;

        ApplyRuntimeFramePacing();
    }

    public static void ApplyRuntimeFramePacing()
    {
        double refreshHz = Screen.currentResolution.refreshRateRatio.value;
        ResolveFramePacing(Application.isMobilePlatform, refreshHz, out int vSyncCount, out int targetFrameRate);
        QualitySettings.vSyncCount = vSyncCount;
        Application.targetFrameRate = targetFrameRate;
    }

    public static void ResolveFramePacing(
        bool isMobilePlatform,
        double screenRefreshHz,
        out int vSyncCount,
        out int targetFrameRate)
    {
        if (isMobilePlatform)
        {
            vSyncCount = 0;
            int resolvedHz = (!double.IsNaN(screenRefreshHz) && !double.IsInfinity(screenRefreshHz) && screenRefreshHz > 0d)
                ? Mathf.RoundToInt((float)screenRefreshHz)
                : MinMobileFrameRate;
            targetFrameRate = Mathf.Clamp(resolvedHz, MinMobileFrameRate, MaxMobileFrameRate);
            return;
        }

        vSyncCount = 1;
        targetFrameRate = -1;
    }
}
