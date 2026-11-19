using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class RunnerUI : MonoBehaviour, IUIPanel
{
    [Header("Controls")]
    [SerializeField] private Button startRunButton;

    [Header("Labels")]
    [SerializeField] private TMP_Text bestDepthText;
    [SerializeField] private TMP_Text bestDepthScoreText;

    [Header("Mode Selection (Endless active now; 10/20 unlockable when ready)")]
    [SerializeField] private Button endlessModeButton;
    [SerializeField] private Button rooms10ModeButton;
    [SerializeField] private Button rooms20ModeButton;
    [SerializeField] private bool enable10And20RoomModes = false;

    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private MazeRunnerService runnerService;

    [Header("Optional Animation (Leave Empty for No Animation)")]
    [Tooltip("Only animates when explicitly assigned in the Inspector. Never auto-fetches GetComponent<UIPanelAnimator>().")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    public event Action OnRunnerClosed;
    public event Action OnStartRunRequested;

    private bool buttonsBound;
    private RunnerModeKind selectedMode = RunnerModeKind.Endless;

    public const string BEST_DEPTH_PREF_KEY = "Runner_BestDepth";

    private void Awake()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (runnerService == null)
            runnerService = MazeRunnerService.Instance != null
                ? MazeRunnerService.Instance
                : FindAnyObjectByType<MazeRunnerService>();

        BindButtons();
        SubscribeEvents();
    }

    private void OnDestroy()
    {
        UnbindButtons();
        UnsubscribeEvents();
    }

    private void SubscribeEvents()
    {
        MazeRunnerService.OnAnyRunEnded -= HandleRunnerRunEnded;
        MazeRunnerService.OnAnyRunEnded += HandleRunnerRunEnded;
    }

    private void UnsubscribeEvents()
    {
        MazeRunnerService.OnAnyRunEnded -= HandleRunnerRunEnded;
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (startRunButton != null)
            startRunButton.onClick.AddListener(OnStartRunClicked);
        if (endlessModeButton != null)
            endlessModeButton.onClick.AddListener(SelectEndlessMode);
        if (rooms10ModeButton != null)
            rooms10ModeButton.onClick.AddListener(Select10RoomsMode);
        if (rooms20ModeButton != null)
            rooms20ModeButton.onClick.AddListener(Select20RoomsMode);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (startRunButton != null)
            startRunButton.onClick.RemoveListener(OnStartRunClicked);
        if (endlessModeButton != null)
            endlessModeButton.onClick.RemoveListener(SelectEndlessMode);
        if (rooms10ModeButton != null)
            rooms10ModeButton.onClick.RemoveListener(Select10RoomsMode);
        if (rooms20ModeButton != null)
            rooms20ModeButton.onClick.RemoveListener(Select20RoomsMode);
    }

    public void Show() => OpenRunner();
    public void Hide() => CloseRunner();

    public void OpenRunner()
    {
        BindButtons();
        RefreshModeButtons();
        RefreshDisplay();

        if (panelAnimator != null)
            panelAnimator.PlayIn();
        else
            gameObject.SetActive(true);
    }

    public void CloseRunner()
    {
        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(FinishCloseRunner);
        else
            FinishCloseRunner();
    }

    private void FinishCloseRunner()
    {
        gameObject.SetActive(false);
        OnRunnerClosed?.Invoke();
    }

    public void SetMode(RunnerModeKind mode)
    {
        if (!enable10And20RoomModes && mode != RunnerModeKind.Endless)
            return;

        selectedMode = mode;
        RefreshModeButtons();
        RefreshDisplay();
    }

    public void RefreshDisplay()
    {
        if (!enable10And20RoomModes)
            selectedMode = RunnerModeKind.Endless;

        ProfileData profile = LoadAndMigrateBestProfile();
        int bestRooms = profile != null
            ? profile.GetBestRunnerRooms(selectedMode)
            : Mathf.Max(0, PlayerPrefs.GetInt(BEST_DEPTH_PREF_KEY, 0));

        if (bestDepthText != null)
        {
            bestDepthText.text = selectedMode switch
            {
                RunnerModeKind.Rooms10 => $"10 ROOMS CLEARS: {bestRooms}",
                RunnerModeKind.Rooms20 => $"20 ROOMS CLEARS: {bestRooms}",
                _                      => $"BEST: {bestRooms} ROOMS",
            };
        }

        if (bestDepthScoreText != null)
            bestDepthScoreText.text = bestRooms.ToString();
    }

    private static ProfileData LoadAndMigrateBestProfile()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
            return null;

        int legacyBest = Mathf.Max(0, PlayerPrefs.GetInt(BEST_DEPTH_PREF_KEY, 0));
        if (legacyBest > profile.bestRunnerEndlessRooms)
        {
            profile.bestRunnerEndlessRooms = legacyBest;
            SaveManager.TrySaveProfile(profile);
        }

        return profile;
    }

    private void RefreshModeButtons()
    {
        if (rooms10ModeButton != null)
            rooms10ModeButton.gameObject.SetActive(enable10And20RoomModes);
        if (rooms20ModeButton != null)
            rooms20ModeButton.gameObject.SetActive(enable10And20RoomModes);

        if (endlessModeButton != null)
            endlessModeButton.interactable = selectedMode != RunnerModeKind.Endless;
        if (rooms10ModeButton != null)
            rooms10ModeButton.interactable = enable10And20RoomModes && selectedMode != RunnerModeKind.Rooms10;
        if (rooms20ModeButton != null)
            rooms20ModeButton.interactable = enable10And20RoomModes && selectedMode != RunnerModeKind.Rooms20;
    }

    private void SelectEndlessMode() => SetMode(RunnerModeKind.Endless);
    private void Select10RoomsMode() => SetMode(RunnerModeKind.Rooms10);
    private void Select20RoomsMode() => SetMode(RunnerModeKind.Rooms20);

    private void OnStartRunClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        OnStartRunRequested?.Invoke();

        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (runnerService == null)
            runnerService = MazeRunnerService.Instance != null
                ? MazeRunnerService.Instance
                : FindAnyObjectByType<MazeRunnerService>();

        if (runnerService == null)
        {
            Debug.LogError("[RunnerUI] Cannot start Maze Runner — MazeRunnerService reference is missing in scene.", this);
            return;
        }

        if (flowController != null && flowController.CurrentState != LevelState.Idle)
            return;

        if (runnerService.StartRun(selectedMode))
        {
            if (UIManager.Instance != null)
                UIManager.Instance.ClosePanelIfOpen(UIPanelType.Runner);
            else
                CloseRunner();
        }
    }

    private void HandleRunnerRunEnded(RunnerRunSummary _)
    {
        RefreshDisplay();
    }
}
