using System.Text;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Editor utility that batch-replaces all <see cref="TMP_Text"/> font assets across all project
/// scenes and prefabs with <c>Assets/Fonts/SDF/80s-retro-future.asset</c> and updates the TMP Settings default font.
/// </summary>
public static class TMPFontReplacer
{
    public const string DefaultTargetFontPath = "Assets/Fonts/SDF/80s-retro-future.asset";
    private const string TmpSettingsAssetPath = "Assets/TextMesh Pro/Resources/TMP Settings.asset";

    [MenuItem("Tools/UI/Replace All TMP Fonts (80s-retro-future) - Dry Run")]
    public static void DryRunMenu()
    {
        string report = ExecuteFontReplacement(DefaultTargetFontPath, dryRun: true);
        Debug.Log(report);
    }

    [MenuItem("Tools/UI/Replace All TMP Fonts (80s-retro-future) - Apply")]
    public static void ApplyMenu()
    {
        string report = ExecuteFontReplacement(DefaultTargetFontPath, dryRun: false);
        Debug.Log(report);
    }

    public static string ExecuteFontReplacement(string targetFontAssetPath = DefaultTargetFontPath, bool dryRun = false)
    {
        TMP_FontAsset targetFont = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(targetFontAssetPath);
        if (targetFont == null)
        {
            return $"[TMPFontReplacer] ERROR: Could not load TMP_FontAsset at '{targetFontAssetPath}'.";
        }

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"[TMPFontReplacer] Mode={(dryRun ? "DRY RUN" : "APPLY")} TargetFont='{targetFont.name}' ({targetFontAssetPath})");

        int totalPrefabsModified = 0;
        int totalPrefabTextsReplaced = 0;

        // 1. Process all Prefabs under Assets/Prefabs
        string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/Prefabs" });
        foreach (string guid in prefabGuids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(path)) continue;

            GameObject root = PrefabUtility.LoadPrefabContents(path);
            if (root == null) continue;

            int replacedInPrefab = 0;
            TMP_Text[] texts = root.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text txt in texts)
            {
                if (txt == null) continue;
                if (txt.font != targetFont || txt.fontSharedMaterial != targetFont.material)
                {
                    replacedInPrefab++;
                    if (!dryRun)
                    {
                        txt.font = targetFont;
                        txt.fontSharedMaterial = targetFont.material;
                        EditorUtility.SetDirty(txt);
                    }
                }
            }

            if (replacedInPrefab > 0)
            {
                totalPrefabsModified++;
                totalPrefabTextsReplaced += replacedInPrefab;
                sb.AppendLine($"  Prefab: {path} -> {replacedInPrefab} TMP_Text(s)");
                if (!dryRun)
                {
                    PrefabUtility.SaveAsPrefabAsset(root, path);
                }
            }

            PrefabUtility.UnloadPrefabContents(root);
        }

        // 2. Process all Scenes under Assets/Scenes
        int totalScenesModified = 0;
        int totalSceneTextsReplaced = 0;

        Scene activeSceneBefore = SceneManager.GetActiveScene();
        string originalScenePath = activeSceneBefore.path;
        if (!dryRun && activeSceneBefore.isDirty)
        {
            EditorSceneManager.SaveScene(activeSceneBefore);
        }

        string[] sceneGuids = AssetDatabase.FindAssets("t:Scene", new[] { "Assets/Scenes" });
        foreach (string guid in sceneGuids)
        {
            string scenePath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(scenePath)) continue;

            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            int replacedInScene = 0;

            foreach (GameObject rootObj in scene.GetRootGameObjects())
            {
                TMP_Text[] texts = rootObj.GetComponentsInChildren<TMP_Text>(true);
                foreach (TMP_Text txt in texts)
                {
                    if (txt == null) continue;
                    if (txt.font != targetFont || txt.fontSharedMaterial != targetFont.material)
                    {
                        replacedInScene++;
                        if (!dryRun)
                        {
                            Undo.RecordObject(txt, "Replace TMP Font");
                            txt.font = targetFont;
                            txt.fontSharedMaterial = targetFont.material;
                            EditorUtility.SetDirty(txt);
                        }
                    }
                }
            }

            if (replacedInScene > 0)
            {
                totalScenesModified++;
                totalSceneTextsReplaced += replacedInScene;
                sb.AppendLine($"  Scene: {scenePath} -> {replacedInScene} TMP_Text(s)");
                if (!dryRun)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
            }
        }

        if (!string.IsNullOrEmpty(originalScenePath) && SceneManager.GetActiveScene().path != originalScenePath)
        {
            EditorSceneManager.OpenScene(originalScenePath, OpenSceneMode.Single);
        }

        // 3. Update TMP Settings default font asset
        Object tmpSettingsObj = AssetDatabase.LoadAssetAtPath<Object>(TmpSettingsAssetPath);
        if (tmpSettingsObj != null)
        {
            SerializedObject so = new SerializedObject(tmpSettingsObj);
            SerializedProperty defaultFontProp = so.FindProperty("m_defaultFontAsset");
            if (defaultFontProp != null && defaultFontProp.objectReferenceValue != targetFont)
            {
                sb.AppendLine($"  TMP Settings: '{TmpSettingsAssetPath}' m_defaultFontAsset -> {targetFont.name}");
                if (!dryRun)
                {
                    defaultFontProp.objectReferenceValue = targetFont;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(tmpSettingsObj);
                }
            }
        }

        if (!dryRun)
        {
            AssetDatabase.SaveAssets();
        }

        sb.AppendLine($"[TMPFontReplacer] Summary: Prefabs={totalPrefabsModified} ({totalPrefabTextsReplaced} texts), Scenes={totalScenesModified} ({totalSceneTextsReplaced} texts).");
        return sb.ToString();
    }
}

