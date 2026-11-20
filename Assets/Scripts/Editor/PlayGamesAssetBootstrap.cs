using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates or validates <c>Assets/Resources/PlayGamesConfig.asset</c> cleanly inside the Assets folder
/// without modifying any Scene or UI Prefab.
/// </summary>
public static class PlayGamesAssetBootstrap
{
    public const string ConfigAssetPath = "Assets/Resources/PlayGamesConfig.asset";

    [MenuItem("Tools/Gravita/Create Or Refresh PlayGamesConfig Asset")]
    public static void EnsureDefaultAssetMenu()
    {
        EnsureDefaultAsset();
    }

    public static PlayGamesConfig EnsureDefaultAsset()
    {
        string resourcesDir = Path.GetDirectoryName(ConfigAssetPath);
        if (!string.IsNullOrEmpty(resourcesDir) && !Directory.Exists(resourcesDir))
            Directory.CreateDirectory(resourcesDir);

        PlayGamesConfig existing = AssetDatabase.LoadAssetAtPath<PlayGamesConfig>(ConfigAssetPath);
        if (existing != null)
        {
            existing.PopulateDefaultKeysIfEmpty();
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssets();
            SafeUnityPackageImporter.EnsureAndroidDefineSymbol("GOOGLE_PLAY_GAMES");
            Debug.Log($"[PlayGamesAssetBootstrap] Refreshed existing config at '{ConfigAssetPath}'.");
            return existing;
        }

        PlayGamesConfig created = ScriptableObject.CreateInstance<PlayGamesConfig>();
        created.PopulateDefaultKeysIfEmpty();
        AssetDatabase.CreateAsset(created, ConfigAssetPath);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        SafeUnityPackageImporter.EnsureAndroidDefineSymbol("GOOGLE_PLAY_GAMES");
        Debug.Log($"[PlayGamesAssetBootstrap] Created default config at '{ConfigAssetPath}'.");
        return created;
    }
}
