using UnityEngine.SceneManagement;

/// <summary>
/// Static scene-transition service. Call Load() to route through the
/// lightweight LoadingScene, which displays progress while the real
/// target loads asynchronously.
///
/// Statics persist across scene loads, so the target scene name survives
/// the transition without a persistent GameObject.
/// </summary>
public static class SceneLoader
{
    /// <summary>Name of the loading scene — must exist in Build Settings.</summary>
    public const string LoadingSceneName = "LoadingScene";

    /// <summary>Scene the LoadingScreen will load next.</summary>
    public static string TargetScene { get; private set; } = "GameScene";

    /// <summary>Route to <paramref name="sceneName"/> via the loading screen.</summary>
    public static void Load(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName))
        {
            UnityEngine.Debug.LogError("[SceneLoader] Load called with an empty scene name.");
            return;
        }

        TargetScene = sceneName;
        SceneManager.LoadScene(LoadingSceneName);
    }
}
