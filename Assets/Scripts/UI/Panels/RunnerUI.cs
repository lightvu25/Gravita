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

    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Optional Animation (Leave Empty for No Animation)")]
    [Tooltip("Only animates when explicitly assigned in the Inspector. Never auto-fetches GetComponent<UIPanelAnimator>().")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    public event Action OnRunnerClosed;
    public event Action OnStartRunRequested;

    private bool buttonsBound;
    private bool trackingActiveRunnerFloor;

    public const string BEST_DEPTH_PREF_KEY = "Runner_BestDepth";

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        BindButtons();
        SubscribeFlow();
    }

    private void OnDestroy()
    {
        UnbindButtons();
        UnsubscribeFlow();
    }

    private void SubscribeFlow()
    {
        if (flowController == null)
            return;

        flowController.OnLevelCompleted -= HandleRunnerFloorCompleted;
        flowController.OnLevelCompleted += HandleRunnerFloorCompleted;
        flowController.OnReturnToMapRequested -= HandleRunnerEnded;
        flowController.OnReturnToMapRequested += HandleRunnerEnded;
    }

    private void UnsubscribeFlow()
    {
        if (flowController == null)
            return;

        flowController.OnLevelCompleted -= HandleRunnerFloorCompleted;
        flowController.OnReturnToMapRequested -= HandleRunnerEnded;
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (startRunButton != null)
            startRunButton.onClick.AddListener(OnStartRunClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (startRunButton != null)
            startRunButton.onClick.RemoveListener(OnStartRunClicked);
    }

    public void Show() => OpenRunner();
    public void Hide() => CloseRunner();

    public void OpenRunner()
    {
        BindButtons();
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

    public void RefreshDisplay()
    {
        int bestDepth = PlayerPrefs.GetInt(BEST_DEPTH_PREF_KEY, 0);
        if (bestDepthText != null)
            bestDepthText.text = $"BEST DEPTH: FLOOR {bestDepth}";
    }

    private void OnStartRunClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        OnStartRunRequested?.Invoke();

        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        if (flowController != null && flowController.CurrentState == LevelState.Idle)
        {
            SubscribeFlow();
            LevelData floorLevel = ResolveNextRunnerLevel();
            if (floorLevel != null)
            {
                trackingActiveRunnerFloor = true;
                flowController.StartLevel(floorLevel);
            }
        }
    }

    private LevelData ResolveNextRunnerLevel()
    {
        if (ProgressionManager.Instance != null && ProgressionManager.Instance.Database != null)
        {
            CampaignDatabase db = ProgressionManager.Instance.Database;
            if (db.Worlds != null)
            {
                var allLevels = new List<LevelData>();
                for (int w = 0; w < db.Worlds.Count; w++)
                {
                    WorldData world = db.Worlds[w];
                    if (world == null || world.levels == null) continue;
                    foreach (LevelData lvl in world.levels)
                    {
                        if (lvl != null)
                            allLevels.Add(lvl);
                    }
                }

                if (allLevels.Count > 0)
                {
                    int currentDepth = Mathf.Max(0, PlayerPrefs.GetInt(BEST_DEPTH_PREF_KEY, 0));
                    return allLevels[currentDepth % allLevels.Count];
                }
            }
        }

        return flowController != null ? flowController.CurrentLevelData : null;
    }

    private void HandleRunnerFloorCompleted()
    {
        if (!trackingActiveRunnerFloor)
            return;

        int nextDepth = Mathf.Max(0, PlayerPrefs.GetInt(BEST_DEPTH_PREF_KEY, 0)) + 1;
        PlayerPrefs.SetInt(BEST_DEPTH_PREF_KEY, nextDepth);
        PlayerPrefs.Save();
        RefreshDisplay();
    }

    private void HandleRunnerEnded()
    {
        trackingActiveRunnerFloor = false;
    }
}
