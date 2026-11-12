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
    [SerializeField] private Button worldButtonPrefab;
    [SerializeField] private TMP_Text selectedWorldTitleText;

    [Header("Level Selection UI")]
    [SerializeField] private Transform levelGridContainer;
    [SerializeField] private LevelNodeWidget levelNodePrefab;
    [SerializeField] private Button backToWorldsButton;

    [Header("Pre-Level 4-Slot Loadout Confirmation (Optional — GDD §5.5)")]
    [SerializeField] private GameObject preLevelLoadoutPopup;
    [SerializeField] private TMP_Text preLevelLoadoutSummaryText;
    [SerializeField] private Button confirmStartLevelButton;
    [SerializeField] private Button editLoadoutButton;
    [SerializeField] private Button cancelPreLevelButton;

    [Header("Scene Transition")]
    [SerializeField] private string gameplaySceneName = "GameScene";

    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    public event Action OnMapClosed;

    private CampaignDatabase database;
    private int currentWorldIndex;
    private LevelData pendingSelectedLevel;
    private bool subscribedToFlow;
    private bool subscribedToProgression;
    private bool buttonsBound;
    private UIPanelAnimator panelAnimator;

    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private readonly List<LevelNodeWidget> spawnedLevelWidgets = new List<LevelNodeWidget>();

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (worldButtonPrefab != null && worldButtonPrefab.gameObject.scene.IsValid())
            worldButtonPrefab.gameObject.SetActive(false);

        if (levelNodePrefab != null && levelNodePrefab.gameObject.scene.IsValid())
            levelNodePrefab.gameObject.SetActive(false);

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

        if (preLevelLoadoutPopup != null)
            preLevelLoadoutPopup.SetActive(false);

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
        if (confirmStartLevelButton != null)
            confirmStartLevelButton.onClick.AddListener(OnConfirmPreLevelStart);
        if (editLoadoutButton != null)
            editLoadoutButton.onClick.AddListener(OnEditLoadoutFromPreLevel);
        if (cancelPreLevelButton != null)
            cancelPreLevelButton.onClick.AddListener(OnCancelPreLevelClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (backToWorldsButton != null)
            backToWorldsButton.onClick.RemoveListener(OnBackToWorldsClicked);
        if (confirmStartLevelButton != null)
            confirmStartLevelButton.onClick.RemoveListener(OnConfirmPreLevelStart);
        if (editLoadoutButton != null)
            editLoadoutButton.onClick.RemoveListener(OnEditLoadoutFromPreLevel);
        if (cancelPreLevelButton != null)
            cancelPreLevelButton.onClick.RemoveListener(OnCancelPreLevelClicked);
    }

    private void SubscribeToFlow()
    {
        if (subscribedToFlow) return;
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (flowController == null) return;

        flowController.OnReturnToMapRequested += HandleReturnToMap;
        flowController.OnLevelStarted += HandleLevelStarted;
        subscribedToFlow = true;
    }

    private void SubscribeToProgression()
    {
        if (subscribedToProgression || ProgressionManager.Instance == null) return;
        ProgressionManager.Instance.OnProgressionChanged += RefreshUI;
        ProgressionManager.Instance.OnLoadoutChanged += RefreshPreLevelSummary;
        subscribedToProgression = true;
    }

    private void UnsubscribeFromProgression()
    {
        if (!subscribedToProgression) return;
        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged -= RefreshUI;
            ProgressionManager.Instance.OnLoadoutChanged -= RefreshPreLevelSummary;
        }
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
        ClosePreLevelPopup();
        ShowWorldMap();
        RefreshUI();
    }

    public void CloseMap()
    {
        ClosePreLevelPopup();
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
        ClosePreLevelPopup();
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
            if (worldButtonPrefab.gameObject.scene.IsValid())
                worldButtonPrefab.gameObject.SetActive(false);

            for (int i = worldButtonContainer.childCount - 1; i >= 0; i--)
            {
                var child = worldButtonContainer.GetChild(i).gameObject;
                if (child != worldButtonPrefab.gameObject)
                    Destroy(child);
            }

            for (int i = 0; i < database.Worlds.Count; i++)
            {
                var world = database.Worlds[i];
                int index = i;
                Button btn = Instantiate(worldButtonPrefab, worldButtonContainer);
                btn.gameObject.SetActive(true);

                TMP_Text label = btn.GetComponentInChildren<TMP_Text>();
                bool unlocked = ProgressionManager.Instance == null || ProgressionManager.Instance.IsWorldUnlocked(world);

                if (label != null)
                {
                    if (unlocked)
                    {
                        label.text = world.worldName;
                    }
                    else
                    {
                        string reqText = ProgressionManager.Instance != null
                            ? ProgressionManager.Instance.GetWorldUnlockRequirementText(world)
                            : "Locked";
                        label.text = string.IsNullOrEmpty(reqText)
                            ? $"{world.worldName} (Locked)"
                            : $"{world.worldName}\n<size=65%>{reqText}</size>";
                    }
                }

                btn.interactable = unlocked;
                btn.onClick.AddListener(() =>
                {
                    GameHaptics.Play(HapticEvent.UISelect);
                    UIAudio.Play(UISoundType.ButtonClick);
                    SelectWorld(index);
                });
            }
        }

        RefreshSpawnedLevelWidgets();
        RefreshPreLevelSummary();
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

        if (levelNodePrefab.gameObject.scene.IsValid())
            levelNodePrefab.gameObject.SetActive(false);

        for (int i = levelGridContainer.childCount - 1; i >= 0; i--)
        {
            var child = levelGridContainer.GetChild(i).gameObject;
            if (child != levelNodePrefab.gameObject)
                Destroy(child);
        }
        spawnedLevelWidgets.Clear();

        if (world.levels == null) return;

        foreach (var lvl in world.levels)
        {
            if (lvl == null) continue;

            LevelNodeWidget widget = Instantiate(levelNodePrefab, levelGridContainer);
            widget.gameObject.SetActive(true);
            widget.Setup(lvl, OnLevelNodeSelected);
            spawnedLevelWidgets.Add(widget);
        }
    }

    private void OnLevelNodeSelected(LevelData level)
    {
        if (level == null) return;

        if (ProgressionManager.Instance != null)
            _ = ProgressionManager.Instance.GetLoadoutSlots();

        if (preLevelLoadoutPopup != null)
        {
            pendingSelectedLevel = level;
            RefreshPreLevelSummary();
            preLevelLoadoutPopup.SetActive(true);
            UIAudio.Play(UISoundType.PanelOpen);
            return;
        }

        StartSelectedLevel(level);
    }

    private void RefreshPreLevelSummary()
    {
        if (preLevelLoadoutSummaryText == null)
            return;

        ProgressionManager pm = ProgressionManager.Instance;
        IReadOnlyList<string> slots = pm != null ? pm.GetLoadoutSlots() : Array.Empty<string>();
        var slotTexts = new List<string>(ProgressionManager.MaxLoadoutSlotCount);
        for (int i = 0; i < ProgressionManager.MaxLoadoutSlotCount; i++)
        {
            string skillId = i < slots.Count ? slots[i] : string.Empty;
            if (string.IsNullOrEmpty(skillId))
            {
                slotTexts.Add($"[{i + 1}: Empty]");
            }
            else
            {
                int count = pm != null ? pm.GetPowerUpCount(skillId) : 0;
                string displayName = PowerUpController.Instance != null &&
                                     PowerUpController.TryGetPowerUpType(skillId, out PowerUpType skillType)
                    ? PowerUpController.Instance.ResolveDisplayName(skillType)
                    : PowerUpController.GetFallbackDisplayName(skillId);
                slotTexts.Add($"[{i + 1}: {displayName} ({count})]");
            }
        }

        string levelTitle = pendingSelectedLevel != null ? pendingSelectedLevel.LevelId : string.Empty;
        preLevelLoadoutSummaryText.text = string.IsNullOrEmpty(levelTitle)
            ? string.Join("  ", slotTexts)
            : $"{levelTitle}\n{string.Join("  ", slotTexts)}";
    }

    private void OnConfirmPreLevelStart()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        LevelData toStart = pendingSelectedLevel;
        ClosePreLevelPopup();
        if (toStart != null)
            StartSelectedLevel(toStart);
    }

    private void OnEditLoadoutFromPreLevel()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        ClosePreLevelPopup();
        UIManager.Instance?.OpenPanel(UIPanelType.Powerups);
    }

    private void OnCancelPreLevelClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        ClosePreLevelPopup();
    }

    private void ClosePreLevelPopup()
    {
        pendingSelectedLevel = null;
        if (preLevelLoadoutPopup != null)
            preLevelLoadoutPopup.SetActive(false);
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
