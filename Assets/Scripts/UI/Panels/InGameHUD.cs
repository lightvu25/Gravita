using System.Collections.Generic;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InGameHUD : MonoBehaviour, IUIPanel
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private PowerUpController powerUpController;

    [Header("Header")]
    [SerializeField] private Button menuButton;
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private RectTransform coinIconTransform;
    [SerializeField] private Image[] starImages;
    [SerializeField] private Sprite starEmpty;
    [SerializeField] private Sprite starFilled;

    [Header("Run Info")]
    [SerializeField] private TMP_Text movesText;
    [SerializeField] private int moveLimit = 0;

    [Header("Power-Up Bar")]
    [SerializeField] private Transform powerContainer;
    [SerializeField] private GameObject powerButtonPrefab;

    private readonly List<PowerUpSlotWidget> spawnedPowerButtons = new List<PowerUpSlotWidget>();
    private readonly List<PowerUpType> displayedPowerTypes = new List<PowerUpType>();
    private readonly List<PowerUpType> desiredPowerTypesBuffer = new List<PowerUpType>(4);

    private int movesUsed;
    private bool buttonsBound;

    private void Awake()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (powerUpController == null)
            powerUpController = PowerUpController.Instance != null
                ? PowerUpController.Instance
                : FindAnyObjectByType<PowerUpController>();

        BindButtons();
    }

    private void Update()
    {
        if (spawnedPowerButtons.Count == 0 || powerUpController == null)
            return;

        if (flowController == null || flowController.CurrentState != LevelState.Playing)
            return;

        RefreshSpawnedPowerStatesOnly();
    }

    private void Start()
    {
        if (flowController != null)
        {
            flowController.OnLevelStarted += HandleLevelStarted;
            flowController.OnLevelRestarted += HandleLevelStarted;
            flowController.OnStateChanged += HandleStateChanged;
            HandleStateChanged(flowController.CurrentState);
        }

        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.OnMoveStopped += HandleMoveCompleted;

        if (EconomyManager.Instance != null)
            EconomyManager.Instance.OnCoinsChanged += UpdateCoins;

        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged += RefreshPowerUp;
            ProgressionManager.Instance.OnLoadoutChanged += RefreshPowerUp;
        }

        if (powerUpController != null)
            powerUpController.OnPowerUpStateChanged += RefreshPowerUp;

        Collectible.OnAnyStarCollected += HandleStarCollected;
        Collectible.OnAnyCoinCollected += HandleInLevelCoinCollected;
        MazeRunnerService.OnAnyRunStarted += HandleRunnerRunStarted;
        MazeRunnerService.OnAnyRoomChanged += HandleRunnerRoomChanged;
        MazeRunnerService.OnAnyRunEnded += HandleRunnerRunEnded;
        MazeRunnerService.OnAnyInRunCoinsChanged += HandleRunnerCoinsChanged;

        RefreshHUD();
    }

    private void OnDestroy()
    {
        UnbindButtons();

        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.OnMoveStopped -= HandleMoveCompleted;

        Collectible.OnAnyStarCollected -= HandleStarCollected;
        Collectible.OnAnyCoinCollected -= HandleInLevelCoinCollected;
        MazeRunnerService.OnAnyRunStarted -= HandleRunnerRunStarted;
        MazeRunnerService.OnAnyRoomChanged -= HandleRunnerRoomChanged;
        MazeRunnerService.OnAnyRunEnded -= HandleRunnerRunEnded;
        MazeRunnerService.OnAnyInRunCoinsChanged -= HandleRunnerCoinsChanged;

        if (flowController != null)
        {
            flowController.OnLevelStarted -= HandleLevelStarted;
            flowController.OnLevelRestarted -= HandleLevelStarted;
            flowController.OnStateChanged -= HandleStateChanged;
        }

        if (EconomyManager.Instance != null)
            EconomyManager.Instance.OnCoinsChanged -= UpdateCoins;

        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged -= RefreshPowerUp;
            ProgressionManager.Instance.OnLoadoutChanged -= RefreshPowerUp;
        }

        if (powerUpController != null)
            powerUpController.OnPowerUpStateChanged -= RefreshPowerUp;
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (menuButton != null)
            menuButton.onClick.AddListener(OnMenuClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (menuButton != null)
            menuButton.onClick.RemoveListener(OnMenuClicked);
    }

    private void HandleLevelStarted()
    {
        SetHudVisible(true);
        movesUsed = 0;
        RefreshStarImagesVisibility();
        RefreshMovesText();
        SetStars(0);
        RefreshHUD();
    }

    private void HandleStateChanged(LevelState state)
    {
        bool show = state == LevelState.Playing;
        SetHudVisible(show);
        RefreshStarImagesVisibility();
        RefreshMovesText();
        RefreshPowerUp();
    }

    private void HandleStarCollected(Collectible _)
    {
        int count = (flowController != null && flowController.CurrentRoot != null)
            ? flowController.CurrentRoot.CollectedStarCount
            : 0;
        SetStars(count);
    }

    private void HandleMoveCompleted()
    {
        movesUsed++;
        RefreshMovesText();
        RefreshPowerUp();
    }

    private void RefreshMovesText()
    {
        if (movesText == null) return;

        MazeRunnerService activeRunner = ResolveActiveRunnerService();
        if (activeRunner != null && activeRunner.IsRunActive && activeRunner.ActiveSession != null)
        {
            RunnerSessionState session = activeRunner.ActiveSession;
            movesText.text = session.TargetRoomCount > 0
                ? $"ROOM {session.CurrentRoomNumber}/{session.TargetRoomCount}"
                : $"ROOM {session.CurrentRoomNumber}";
            return;
        }

        movesText.text = moveLimit > 0
            ? Mathf.Max(0, moveLimit - movesUsed).ToString()
            : movesUsed.ToString();
    }

    public void Show()
    {
        SetHudVisible(true);
        RefreshHUD();
    }

    public void Hide()
    {
        if (flowController == null || flowController.CurrentState != LevelState.Playing)
            SetHudVisible(false);
    }

    private void SetHudVisible(bool visible)
    {
        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);
    }

    public void SetStars(int earnedStars)
    {
        earnedStars = Mathf.Clamp(earnedStars, 0, 3);
        if (starImages == null) return;

        for (int i = 0; i < starImages.Length; i++)
        {
            if (starImages[i] == null) continue;
            starImages[i].sprite = i < earnedStars ? starFilled : starEmpty;
        }
    }

    public void RefreshHUD()
    {
        RefreshStarImagesVisibility();
        RefreshCoinsDisplay(animate: false);
        RefreshMovesText();
        RefreshPowerUp();
        SetStars(0);
    }

    private void UpdateCoins(int newBalance, int delta)
    {
        int totalDisplay = newBalance + GetPendingRunnerCoins();
        CurrencyUI.Set(coinsText, totalDisplay, delta != 0);
    }

    private void RefreshCoinsDisplay(bool animate)
    {
        int walletCoins = EconomyManager.Instance != null ? EconomyManager.Instance.Coins : 0;
        CurrencyUI.Set(coinsText, walletCoins + GetPendingRunnerCoins(), animate);
    }

    private MazeRunnerService ResolveActiveRunnerService()
    {
        if (MazeRunnerService.Instance != null && MazeRunnerService.Instance.IsRunActive)
            return MazeRunnerService.Instance;

        if (flowController != null && flowController.ActiveRunnerService != null && flowController.ActiveRunnerService.IsRunActive)
            return flowController.ActiveRunnerService;

        return null;
    }

    private int GetPendingRunnerCoins()
    {
        if (EconomyBalanceConfig.Active.GrantWalletCoinsFromInLevelPickups)
            return 0;

        MazeRunnerService activeRunner = ResolveActiveRunnerService();
        RunnerSessionState session = activeRunner != null ? activeRunner.ActiveSession : null;
        return session != null ? session.InRunCoinsPickedUp : 0;
    }

    private bool IsRunnerModeActive()
    {
        return ResolveActiveRunnerService() != null;
    }

    private void RefreshStarImagesVisibility()
    {
        if (starImages == null) return;

        bool showStars = !IsRunnerModeActive();
        for (int i = 0; i < starImages.Length; i++)
        {
            if (starImages[i] != null && starImages[i].gameObject.activeSelf != showStars)
                starImages[i].gameObject.SetActive(showStars);
        }
    }

    private void HandleRunnerRunStarted(RunnerSessionState _)
    {
        RefreshStarImagesVisibility();
        RefreshMovesText();
        RefreshCoinsDisplay(animate: false);
    }

    private void HandleRunnerRoomChanged(int _)
    {
        RefreshMovesText();
    }

    private void HandleRunnerRunEnded(RunnerRunSummary _)
    {
        RefreshStarImagesVisibility();
        RefreshCoinsDisplay(animate: false);
    }

    private void HandleRunnerCoinsChanged(int _)
    {
        RefreshCoinsDisplay(animate: true);
    }

    private void HandleInLevelCoinCollected(Collectible coin)
    {
        if (coin == null || coinIconTransform == null)
            return;

        coinIconTransform.DOKill();
        coinIconTransform.localScale = Vector3.one;
        coinIconTransform.DOPunchScale(Vector3.one * 0.12f, 0.14f, 1, 0.5f)
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void BuildDesiredPowerTypes(List<PowerUpType> results)
    {
        results.Clear();
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;

        if (ProgressionManager.Instance != null)
        {
            IReadOnlyList<string> loadout = ProgressionManager.Instance.GetLoadoutSlots();
            for (int i = 0; i < loadout.Count; i++)
            {
                string skillId = loadout[i];
                if (string.IsNullOrEmpty(skillId)) continue;

                if (PowerUpController.TryGetPowerUpType(skillId, out PowerUpType type)
                    && balance.IsSkillActiveInCampaign(type)
                    && !results.Contains(type))
                {
                    results.Add(type);
                }
            }
            return;
        }

        IReadOnlyList<PowerUpType> allTypes = PowerUpController.GetSupportedPowerUpTypes();
        for (int i = 0; i < allTypes.Count; i++)
        {
            PowerUpType type = allTypes[i];
            if (balance.IsSkillActiveInCampaign(type))
                results.Add(type);
        }
    }

    private bool NeedsPowerBarRebuild(List<PowerUpType> desired)
    {
        if (desired.Count != displayedPowerTypes.Count || desired.Count != spawnedPowerButtons.Count)
            return true;

        for (int i = 0; i < desired.Count; i++)
        {
            if (displayedPowerTypes[i] != desired[i] || spawnedPowerButtons[i] == null)
                return true;
        }
        return false;
    }

    private void RefreshPowerUp()
    {
        if (powerContainer == null || powerButtonPrefab == null)
            return;

        if (powerUpController == null)
            powerUpController = PowerUpController.Instance != null
                ? PowerUpController.Instance
                : FindAnyObjectByType<PowerUpController>();

        BuildDesiredPowerTypes(desiredPowerTypesBuffer);

        if (NeedsPowerBarRebuild(desiredPowerTypesBuffer))
        {
            for (int i = spawnedPowerButtons.Count - 1; i >= 0; i--)
            {
                PowerUpSlotWidget existing = spawnedPowerButtons[i];
                if (existing == null) continue;
                if (Application.isPlaying)
                    Destroy(existing.gameObject);
                else
                    DestroyImmediate(existing.gameObject);
            }

            spawnedPowerButtons.Clear();
            displayedPowerTypes.Clear();

            for (int i = 0; i < desiredPowerTypesBuffer.Count; i++)
            {
                PowerUpType type = desiredPowerTypesBuffer[i];
                GetPowerUpDisplayState(
                    type,
                    out Sprite icon,
                    out int count,
                    out bool canClick,
                    out bool isActiveEffect,
                    out bool showChargeCount,
                    out bool isReadyState,
                    out float cooldownNorm,
                    out PowerUpActivationMode mode);

                GameObject btnObj = Instantiate(powerButtonPrefab, powerContainer, false);
                if (!btnObj.TryGetComponent(out PowerUpSlotWidget widget))
                {
                    Debug.LogError("[InGameHUD] Assigned powerButtonPrefab is missing PowerUpSlotWidget component.", this);
                    if (Application.isPlaying)
                        Destroy(btnObj);
                    else
                        DestroyImmediate(btnObj);
                    break;
                }

                btnObj.name = $"PowerButton_{type}";
                btnObj.transform.localScale = Vector3.one;
                btnObj.SetActive(true);
                widget.Setup(type, icon, count, canClick, isActiveEffect, OnPowerButtonClicked, showChargeCount, isReadyState, cooldownNorm, mode);

                spawnedPowerButtons.Add(widget);
                displayedPowerTypes.Add(type);
            }
            return;
        }

        RefreshSpawnedPowerStatesOnly();
    }

    private void RefreshSpawnedPowerStatesOnly()
    {
        for (int i = 0; i < spawnedPowerButtons.Count; i++)
        {
            PowerUpSlotWidget widget = spawnedPowerButtons[i];
            if (widget == null) continue;

            PowerUpType type = displayedPowerTypes[i];
            GetPowerUpDisplayState(
                type,
                out Sprite icon,
                out int count,
                out bool canClick,
                out bool isActiveEffect,
                out bool showChargeCount,
                out bool isReadyState,
                out float cooldownNorm,
                out PowerUpActivationMode mode);
            widget.Refresh(icon, count, canClick, isActiveEffect, showChargeCount, isReadyState, cooldownNorm, mode);
        }
    }

    private void GetPowerUpDisplayState(
        PowerUpType type,
        out Sprite icon,
        out int count,
        out bool canClick,
        out bool isActiveEffect,
        out bool showChargeCount,
        out bool isReadyState,
        out float cooldownNorm,
        out PowerUpActivationMode activationMode)
    {
        PowerUpData data = powerUpController != null ? powerUpController.GetPowerUpData(type) : null;
        icon = data != null ? data.Icon : null;
        bool isModular = powerUpController != null && powerUpController.IsModularSkill(type);
        showChargeCount = !isModular;
        activationMode = powerUpController != null
            ? powerUpController.GetActivationMode(type)
            : (data != null ? data.ActivationMode : PowerUpActivationMode.ManualActive);

        if (powerUpController != null)
        {
            count = powerUpController.GetOwnedCount(type);
            isActiveEffect = powerUpController.IsEffectActive(type);
            cooldownNorm = powerUpController.GetCooldownNormalized(type);
            isReadyState = !isActiveEffect && cooldownNorm <= 0.0001f && powerUpController.GetCooldownRemaining(type) <= 0f;
            canClick = powerUpController.CanManuallyActivate(type);
        }
        else
        {
            count = ProgressionManager.Instance != null
                ? ProgressionManager.Instance.GetPowerUpCount(PowerUpController.GetPowerUpId(type))
                : 0;
            canClick = count > 0 && activationMode != PowerUpActivationMode.AutoTrigger;
            isActiveEffect = false;
            cooldownNorm = 0f;
            isReadyState = !isModular || count >= 0;
        }
    }

    private void OnPowerButtonClicked(PowerUpType type)
    {
        bool activated = powerUpController != null
            && powerUpController.TryActivateFromUI(type);
        UIAudio.Play(activated ? UISoundType.ButtonClick : UISoundType.ErrorOrLocked);
        RefreshPowerUp();

        for (int i = 0; i < spawnedPowerButtons.Count; i++)
        {
            PowerUpSlotWidget widget = spawnedPowerButtons[i];
            if (widget != null && widget.PowerUpType == type)
            {
                widget.PlayActivationFeedback(activated);
                break;
            }
        }
    }

    public bool TryActivatePowerUp(PowerUpType type)
    {
        if (powerUpController == null)
        {
            Debug.LogWarning($"[InGameHUD] Cannot activate {type} — PowerUpController is not assigned.", this);
            return false;
        }

        bool activated = powerUpController.TryActivate(type);
        UIAudio.Play(activated ? UISoundType.ButtonClick : UISoundType.ErrorOrLocked);

        RefreshPowerUp();
        return activated;
    }

    private void OnMenuClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        UIManager.Instance?.OpenPanel(UIPanelType.Pause);
    }
}
