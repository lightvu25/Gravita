using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class CampaignMapUI : MonoBehaviour, IUIPanel
{
    [Header("Panels")]
    [SerializeField] private GameObject worldMapPanel;
    [SerializeField] private GameObject levelSelectPanel;

    [Header("World Selection UI")]
    [SerializeField] private Transform worldButtonContainer;
    [SerializeField] private GameObject worldButtonPrefab;
    [SerializeField] private TMP_Text selectedWorldTitleText;

    [Header("Level Selection UI")]
    [SerializeField] private Transform levelGridContainer;
    [SerializeField] private GameObject levelNodePrefab;
    [SerializeField] private Button backToWorldsButton;

    [Header("Scene Transition")]
    [SerializeField] private string gameplaySceneName = "GameScene";

    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    public event Action OnMapClosed;

    private CampaignDatabase database;
    private int currentWorldIndex;
    private bool subscribedToFlow;
    private bool subscribedToProgression;
    private bool buttonsBound;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private readonly List<GameObject> spawnedWorldObjects = new List<GameObject>();
    private readonly List<LevelNodeWidget> spawnedLevelWidgets = new List<LevelNodeWidget>();

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        BindButtons();
    }

    private void OnEnable()
    {
        SubscribeToFlow();
        SubscribeToProgression();
        BindButtons();
        RefreshUI();
    }

    private void OnDisable()
    {
        UnsubscribeFromProgression();
    }

    private void Start()
    {
        SubscribeToFlow();
        SubscribeToProgression();
        BindButtons();

        if (ProgressionManager.Instance != null)
            database = ProgressionManager.Instance.Database;

        RefreshUI();
    }

    private void OnDestroy()
    {
        if (subscribedToFlow && flowController != null)
        {
            flowController.OnReturnToMapRequested -= HandleReturnToMap;
            flowController.OnLevelStarted -= HandleLevelStarted;
            subscribedToFlow = false;
        }

        UnsubscribeFromProgression();
        UnbindButtons();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (backToWorldsButton != null)
            backToWorldsButton.onClick.AddListener(OnBackToWorldsClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (backToWorldsButton != null)
            backToWorldsButton.onClick.RemoveListener(OnBackToWorldsClicked);
    }

    private void SubscribeToFlow()
    {
        if (subscribedToFlow) return;
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (flowController == null) return;

        flowController.OnReturnToMapRequested += HandleReturnToMap;
        flowController.OnLevelStarted += HandleLevelStarted;
        subscribedToFlow = true;
    }

    private void SubscribeToProgression()
    {
        if (subscribedToProgression || ProgressionManager.Instance == null) return;
        ProgressionManager.Instance.OnProgressionChanged += RefreshUI;
        subscribedToProgression = true;
    }

    private void UnsubscribeFromProgression()
    {
        if (!subscribedToProgression) return;
        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged -= RefreshUI;
        subscribedToProgression = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    public void OpenMap()
    {
        BindButtons();
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
        ShowWorldMap();
        RefreshUI();
    }

    public void CloseMap()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishCloseMap);
        else FinishCloseMap();
    }

    private void FinishCloseMap()
    {
        if (worldMapPanel != null) worldMapPanel.SetActive(false);
        if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
        gameObject.SetActive(false);
        OnMapClosed?.Invoke();
    }

    private void OnBackToWorldsClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        ShowWorldMap();
    }

    public void ShowWorldMap()
    {
        if (worldMapPanel != null) worldMapPanel.SetActive(true);
        if (levelSelectPanel != null) levelSelectPanel.SetActive(false);
    }

    public void SelectWorld(int worldIndex)
    {
        if (database == null || worldIndex < 0 || worldIndex >= database.Worlds.Count) return;

        var world = database.Worlds[worldIndex];
        if (ProgressionManager.Instance != null && !ProgressionManager.Instance.IsWorldUnlocked(world))
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
            string reqText = ProgressionManager.Instance.GetWorldUnlockRequirementText(world);
            Debug.LogWarning($"[CampaignMapUI] World '{world.worldName}' is locked! ({reqText})");
            return;
        }

        currentWorldIndex = worldIndex;
        if (worldMapPanel != null) worldMapPanel.SetActive(false);
        if (levelSelectPanel != null) levelSelectPanel.SetActive(true);

        AnalyticsService.Track(AnalyticsEvents.WorldOpened, new Dictionary<string, object>
        {
            ["world_index"] = worldIndex,
            ["world_name"]  = world.worldName
        });

        PopulateLevels(world);
    }

    // ─────────────────────────────────────────────────────────────
    //  UI Generation
    // ─────────────────────────────────────────────────────────────

    public void RefreshUI()
    {
        if (database == null && ProgressionManager.Instance != null)
            database = ProgressionManager.Instance.Database;

        if (database == null) return;

        if (worldButtonContainer != null && worldButtonPrefab != null)
        {
            ClearSpawnedObjects(spawnedWorldObjects);

            for (int i = 0; i < database.Worlds.Count; i++)
            {
                var world = database.Worlds[i];
                int index = i;
                GameObject worldObj = Instantiate(worldButtonPrefab, worldButtonContainer, false);
                worldObj.transform.localScale = Vector3.one;
                worldObj.SetActive(true);

                bool unlocked = ProgressionManager.Instance == null || ProgressionManager.Instance.IsWorldUnlocked(world);
                string reqText = !unlocked && ProgressionManager.Instance != null
                    ? ProgressionManager.Instance.GetWorldUnlockRequirementText(world)
                    : string.Empty;

                if (worldObj.TryGetComponent(out WorldButtonWidget worldWidget))
                {
                    worldWidget.Setup(world.worldName, unlocked, reqText, () => SelectWorld(index));
                    spawnedWorldObjects.Add(worldObj);
                }
                else if (worldObj.TryGetComponent(out Button btn))
                {
                    TMP_Text label = btn.GetComponentInChildren<TMP_Text>(true);
                    if (label != null)
                    {
                        label.text = unlocked
                            ? world.worldName
                            : (string.IsNullOrEmpty(reqText)
                                ? $"{world.worldName} (Locked)"
                                : $"{world.worldName}\n<size=65%>{reqText}</size>");
                    }

                    btn.interactable = unlocked;
                    btn.onClick.AddListener(() =>
                    {
                        GameHaptics.Play(HapticEvent.UISelect);
                        UIAudio.Play(UISoundType.ButtonClick);
                        SelectWorld(index);
                    });
                    spawnedWorldObjects.Add(worldObj);
                }
                else
                {
                    Debug.LogError("[CampaignMapUI] Assigned worldButtonPrefab is missing WorldButtonWidget or Button component.", this);
                    DestroyObjectSafely(worldObj);
                }
            }
        }

        RefreshSpawnedLevelWidgets();
    }

    public void RefreshSpawnedLevelWidgets()
    {
        for (int i = spawnedLevelWidgets.Count - 1; i >= 0; i--)
        {
            LevelNodeWidget widget = spawnedLevelWidgets[i];
            if (widget == null)
            {
                spawnedLevelWidgets.RemoveAt(i);
                continue;
            }
            widget.Refresh();
        }
    }

    private void PopulateLevels(WorldData world)
    {
        if (world == null) return;

        if (selectedWorldTitleText != null)
            selectedWorldTitleText.text = world.worldName;

        if (levelGridContainer == null || levelNodePrefab == null) return;

        for (int i = spawnedLevelWidgets.Count - 1; i >= 0; i--)
        {
            if (spawnedLevelWidgets[i] != null)
                DestroyObjectSafely(spawnedLevelWidgets[i].gameObject);
        }
        spawnedLevelWidgets.Clear();

        if (world.levels == null) return;

        foreach (var lvl in world.levels)
        {
            if (lvl == null) continue;

            GameObject nodeObj = Instantiate(levelNodePrefab, levelGridContainer, false);
            if (!nodeObj.TryGetComponent(out LevelNodeWidget widget))
            {
                Debug.LogError("[CampaignMapUI] Assigned levelNodePrefab is missing LevelNodeWidget component.", this);
                DestroyObjectSafely(nodeObj);
                return;
            }

            nodeObj.transform.localScale = Vector3.one;
            nodeObj.SetActive(true);
            widget.Setup(lvl, OnLevelNodeSelected);
            spawnedLevelWidgets.Add(widget);
        }
    }

    private static void ClearSpawnedObjects(List<GameObject> spawned)
    {
        for (int i = spawned.Count - 1; i >= 0; i--)
        {
            if (spawned[i] != null)
                DestroyObjectSafely(spawned[i]);
        }
        spawned.Clear();
    }

    private static void DestroyObjectSafely(GameObject obj)
    {
        if (obj == null) return;
        if (Application.isPlaying)
            Destroy(obj);
        else
            DestroyImmediate(obj);
    }

    private void OnLevelNodeSelected(LevelData level)
    {
        if (level == null) return;
        StartSelectedLevel(level);
    }

    private void StartSelectedLevel(LevelData level)
    {
        if (level == null) return;

        Debug.Log($"[CampaignMapUI] Selected level: {level.LevelId}");

        if (GameSession.Instance != null)
            GameSession.Instance.pendingLevel = level;

        if (flowController != null)
            flowController.StartLevel(level);
        else if (!string.IsNullOrEmpty(gameplaySceneName) && SceneManager.GetActiveScene().name != gameplaySceneName)
            SceneLoader.Load(gameplaySceneName);

        RequestClose();
    }

    // ─────────────────────────────────────────────────────────────
    //  IUIPanel / UIManager routing
    // ─────────────────────────────────────────────────────────────

    public void Show() => OpenMap();
    public void Hide() => CloseMap();

    private void HandleReturnToMap()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ShowScreen(UIPanelType.Map);
        else
            OpenMap();
    }

    private void HandleLevelStarted()
    {
        RequestClose();
    }

    private void RequestClose()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Map))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Map);
        else
            CloseMap();
    }
}
