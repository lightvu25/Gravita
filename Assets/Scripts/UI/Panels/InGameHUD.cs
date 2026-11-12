using System.Collections.Generic;
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
    [SerializeField] private Image[] starImages;
    [SerializeField] private Sprite starEmpty;
    [SerializeField] private Sprite starFilled;

    [Header("Run Info")]
    [SerializeField] private TMP_Text movesText;
    [SerializeField] private int moveLimit = 0;

    [Header("Power-Up Bar")]
    [SerializeField] private Transform powerContainer;
    [SerializeField] private PowerUpSlotWidget powerButtonPrefab;

    private readonly List<PowerUpSlotWidget> spawnedPowerButtons = new List<PowerUpSlotWidget>();
    private readonly List<PowerUpType> displayedPowerTypes = new List<PowerUpType>();
    private readonly List<PowerUpType> desiredPowerTypesBuffer = new List<PowerUpType>(4);

    private int movesUsed;
    private bool buttonsBound;

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (powerUpController == null)
            powerUpController = FindAnyObjectByType<PowerUpController>();

        if (powerButtonPrefab != null && powerButtonPrefab.gameObject.scene.IsValid())
            powerButtonPrefab.gameObject.SetActive(false);

        BindButtons();
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

        RefreshHUD();
    }

    private void OnDestroy()
    {
        UnbindButtons();

        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.OnMoveStopped -= HandleMoveCompleted;

        Collectible.OnAnyStarCollected -= HandleStarCollected;

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
        RefreshMovesText();
        SetStars(0);
        RefreshHUD();
    }

    private void HandleStateChanged(LevelState state)
    {
        bool show = state == LevelState.Playing;
        SetHudVisible(show);
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
            starImages[i].SetSpriteNative(i < earnedStars ? starFilled : starEmpty);
        }
    }

    public void RefreshHUD()
    {
        if (EconomyManager.Instance != null)
            UpdateCoins(EconomyManager.Instance.Coins, 0);

        RefreshMovesText();
        RefreshPowerUp();
        SetStars(0);
    }

    private void UpdateCoins(int newBalance, int delta)
    {
        CurrencyUI.Set(coinsText, newBalance, delta != 0);
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

        GameObject templateObj = powerButtonPrefab.gameObject;
        if (templateObj.scene.IsValid() && templateObj.activeSelf)
            templateObj.SetActive(false);

        BuildDesiredPowerTypes(desiredPowerTypesBuffer);

        if (NeedsPowerBarRebuild(desiredPowerTypesBuffer))
        {
            for (int i = powerContainer.childCount - 1; i >= 0; i--)
            {
                GameObject child = powerContainer.GetChild(i).gameObject;
                if (child == templateObj) continue;
                Destroy(child);
            }

            spawnedPowerButtons.Clear();
            displayedPowerTypes.Clear();

            for (int i = 0; i < desiredPowerTypesBuffer.Count; i++)
            {
                PowerUpType type = desiredPowerTypesBuffer[i];
                GetPowerUpDisplayState(type, out Sprite icon, out int count, out bool canActivate, out bool isActiveEffect);

                PowerUpSlotWidget widget = Instantiate(powerButtonPrefab, powerContainer);
                widget.name = $"PowerButton_{type}";
                widget.gameObject.SetActive(true);
                widget.Setup(type, icon, count, canActivate, isActiveEffect, OnPowerButtonClicked);

                spawnedPowerButtons.Add(widget);
                displayedPowerTypes.Add(type);
            }
            return;
        }

        for (int i = 0; i < spawnedPowerButtons.Count; i++)
        {
            PowerUpSlotWidget widget = spawnedPowerButtons[i];
            if (widget == null) continue;

            PowerUpType type = displayedPowerTypes[i];
            GetPowerUpDisplayState(type, out Sprite icon, out int count, out bool canActivate, out bool isActiveEffect);
            widget.Refresh(icon, count, canActivate, isActiveEffect);
        }
    }

    private void GetPowerUpDisplayState(
        PowerUpType type,
        out Sprite icon,
        out int count,
        out bool canActivate,
        out bool isActiveEffect)
    {
        PowerUpData data = powerUpController != null ? powerUpController.GetPowerUpData(type) : null;
        icon = data != null ? data.Icon : null;

        if (powerUpController != null)
        {
            count = powerUpController.GetOwnedCount(type);
            canActivate = powerUpController.CanActivate(type);
            isActiveEffect = powerUpController.IsEffectActive(type);
        }
        else
        {
            count = ProgressionManager.Instance != null
                ? ProgressionManager.Instance.GetPowerUpCount(PowerUpController.GetPowerUpId(type))
                : 0;
            canActivate = count > 0;
            isActiveEffect = false;
        }
    }

    private void OnPowerButtonClicked(PowerUpType type)
    {
        bool activated = TryActivatePowerUp(type);
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
