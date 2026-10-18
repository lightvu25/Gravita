using UnityEngine;

/// <summary>
/// Instantiates and manages the lifecycle of a level layout.
///
/// Usage:
///   Assign a LevelData asset to the Inspector field (or call LoadLevel() at runtime).
///   The LevelFlowController drives this component — do not call LoadLevel() directly
///   from gameplay code outside of LevelFlowController.
/// </summary>
public class LevelLoader : MonoBehaviour
{
    [Header("Level To Load")]
    [Tooltip("The LevelData asset to load when the scene starts. Can be overridden at runtime by LevelFlowController.")]
    [SerializeField] private LevelData initialLevelData;

    [Header("Layout Parent")]
    [Tooltip("Optional parent transform for the instantiated layout. Leave null to parent to this GameObject.")]
    [SerializeField] private Transform layoutParent;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private LevelData   currentLevelData;
    private GameObject  currentLayoutInstance;
    private LevelRoot   currentLevelRoot;

    // ─────────────────────────────────────────────────────────────
    //  Public Accessors
    // ─────────────────────────────────────────────────────────────

    public LevelData   CurrentLevelData   => currentLevelData;
    public LevelRoot   CurrentLevelRoot   => currentLevelRoot;
    public bool        IsLevelLoaded      => currentLayoutInstance != null;

    // ─────────────────────────────────────────────────────────────
    //  API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Unloads any existing layout, then instantiates the layout prefab from the given LevelData.
    /// </summary>
    /// <returns>The LevelRoot of the newly instantiated layout, or null on failure.</returns>
    public LevelRoot LoadLevel(LevelData levelData)
    {
        if (levelData == null)
        {
            Debug.LogError("[LevelLoader] Cannot load level — LevelData is null.", this);
            return null;
        }

        if (levelData.LayoutPrefab == null)
        {
            Debug.LogError($"[LevelLoader] LevelData '{levelData.name}' has no Layout Prefab assigned.", this);
            return null;
        }

        UnloadCurrentLevel();

        Transform parent = layoutParent != null ? layoutParent : transform;
        currentLayoutInstance = Instantiate(levelData.LayoutPrefab, parent);
        currentLayoutInstance.name = $"Layout_{levelData.LevelId}";

        currentLevelRoot = currentLayoutInstance.GetComponent<LevelRoot>();
        if (currentLevelRoot == null)
        {
            Debug.LogError($"[LevelLoader] Layout Prefab '{levelData.LayoutPrefab.name}' is missing a LevelRoot component on its root GameObject.", this);
            if (Application.isPlaying)
                Destroy(currentLayoutInstance);
            else
                DestroyImmediate(currentLayoutInstance);
            currentLayoutInstance = null;
            return null;
        }

        currentLevelData = levelData;
        Debug.Log($"[LevelLoader] Loaded level '{levelData.LevelId}' — layout '{levelData.LayoutPrefab.name}'.");
        return currentLevelRoot;
    }

    /// <summary>
    /// Destroys the currently instantiated layout and clears all references.
    /// </summary>
    public void UnloadCurrentLevel()
    {
        if (currentLayoutInstance != null)
        {
            if (Application.isPlaying)
                Destroy(currentLayoutInstance);
            else
                DestroyImmediate(currentLayoutInstance);

            currentLayoutInstance = null;
            currentLevelRoot      = null;
            currentLevelData      = null;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Start()
    {
        // If LevelFlowController is present in the scene, it manages loading.
        // Avoid double-loading the initial level.
        if (FindAnyObjectByType<LevelFlowController>() != null)
            return;

        // Auto-load the initialLevelData if assigned in standalone testing mode.
        if (initialLevelData != null && !IsLevelLoaded)
        {
            LoadLevel(initialLevelData);
        }
    }
}
