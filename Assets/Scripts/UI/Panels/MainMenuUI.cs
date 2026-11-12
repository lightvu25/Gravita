using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour, IUIPanel
{
    private enum Tab { Map, Runner, Powerups, Achievement, Multiplayer }

    [Header("System References")]
    [SerializeField] private RunnerUI runnerUI;
    [SerializeField] private LevelFlowController flowController;

    [Header("Header")]
    [Tooltip("Displays the current number level of the player.")]
    [SerializeField] private TMP_Text levelText;
    [Tooltip("Optional Slider for the player's EXP progression bar (0..1).")]
    [SerializeField] private Slider expProgressSlider;
    [Tooltip("Optional Filled Image for the player's EXP progression bar.")]
    [SerializeField] private Image expFillImage;
    [Tooltip("Optional text displaying current EXP / required EXP (e.g. '50 / 100').")]
    [SerializeField] private TMP_Text expText;
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;

    [Header("Tab Bar")]
    [SerializeField] private Button mapTabButton;
    [FormerlySerializedAs("shopTabButton")]
    [SerializeField] private Button runnerTabButton;
    [SerializeField] private Button powerupsTabButton;
    [SerializeField] private Button achievementTabButton;
    [SerializeField] private Button multiplayerTabButton;

    [SerializeField, Min(1f)] private float selectedTabScale = 1.3f;
    [SerializeField] private float tabScaleTweenDuration = 0.2f;

    [Header("Side Buttons")]
    [SerializeField] private Button settingButton;
    [SerializeField] private Button missionButton;
    [SerializeField] private Button dailyRewardButton;
    [FormerlySerializedAs("visitorButton")]
    [SerializeField] private Button bonusButton;
    [SerializeField] private Button blindBoxButton;
    [SerializeField] private Button shopButton;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private Tab currentTab = Tab.Map;
    private bool buttonsBound;
    private bool economySubscribed;
    private bool progressionSubscribed;
    private bool uiManagerSubscribed;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        BindButtons();

        if (runnerUI != null)
            runnerUI.OnRunnerClosed += HandleRunnerClosed;

        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged += UpdateCoins;
            EconomyManager.Instance.OnGemsChanged += UpdateGems;
            economySubscribed = true;
        }

        if (flowController != null)
            flowController.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        UnbindButtons();

        if (runnerUI != null)
            runnerUI.OnRunnerClosed -= HandleRunnerClosed;

        if (economySubscribed && EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged -= UpdateCoins;
            EconomyManager.Instance.OnGemsChanged -= UpdateGems;
            economySubscribed = false;
        }

        if (flowController != null)
            flowController.OnStateChanged -= HandleStateChanged;

        if (progressionSubscribed && ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged -= RefreshHeader;
            ProgressionManager.Instance.OnPlayerExpChanged -= HandleExpChanged;
            progressionSubscribed = false;
        }

        if (uiManagerSubscribed && UIManager.Instance != null)
        {
            UIManager.Instance.OnScreenChanged -= HandleScreenChanged;
            UIManager.Instance.OnPopupStackChanged -= HandlePopupStackChanged;
            uiManagerSubscribed = false;
        }
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (mapTabButton != null) mapTabButton.onClick.AddListener(OnMapTabClicked);
        if (runnerTabButton != null) runnerTabButton.onClick.AddListener(OnRunnerTabClicked);
        if (powerupsTabButton != null) powerupsTabButton.onClick.AddListener(OnPowerupsTabClicked);
        if (achievementTabButton != null) achievementTabButton.onClick.AddListener(OnAchievementTabClicked);
        if (multiplayerTabButton != null) multiplayerTabButton.onClick.AddListener(OnMultiplayerTabClicked);
        if (settingButton != null) settingButton.onClick.AddListener(OnSettingClicked);
        if (missionButton != null) missionButton.onClick.AddListener(OnMissionClicked);
        if (dailyRewardButton != null) dailyRewardButton.onClick.AddListener(OnDailyRewardClicked);
        if (bonusButton != null) bonusButton.onClick.AddListener(OnBonusClicked);
        if (blindBoxButton != null) blindBoxButton.onClick.AddListener(OnBlindBoxClicked);
        if (shopButton != null) shopButton.onClick.AddListener(OnShopClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (mapTabButton != null) mapTabButton.onClick.RemoveListener(OnMapTabClicked);
        if (runnerTabButton != null) runnerTabButton.onClick.RemoveListener(OnRunnerTabClicked);
        if (powerupsTabButton != null) powerupsTabButton.onClick.RemoveListener(OnPowerupsTabClicked);
        if (achievementTabButton != null) achievementTabButton.onClick.RemoveListener(OnAchievementTabClicked);
        if (multiplayerTabButton != null) multiplayerTabButton.onClick.RemoveListener(OnMultiplayerTabClicked);
        if (settingButton != null) settingButton.onClick.RemoveListener(OnSettingClicked);
        if (missionButton != null) missionButton.onClick.RemoveListener(OnMissionClicked);
        if (dailyRewardButton != null) dailyRewardButton.onClick.RemoveListener(OnDailyRewardClicked);
        if (bonusButton != null) bonusButton.onClick.RemoveListener(OnBonusClicked);
        if (blindBoxButton != null) blindBoxButton.onClick.RemoveListener(OnBlindBoxClicked);
        if (shopButton != null) shopButton.onClick.RemoveListener(OnShopClicked);
    }

    public void Show()
    {
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);

        if (flowController == null || flowController.CurrentState == LevelState.Idle)
            SelectTab(currentTab);
        else
            RefreshHeader();
    }

    public void Hide()
    {
        if (flowController != null && flowController.CurrentState != LevelState.Idle)
        {
            if (UIManager.Instance != null)
            {
                UIManager.Instance.CloseAllPopups();
                UIManager.Instance.CloseActiveScreen();
            }

            if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
            else FinishHide();
        }
    }

    private void FinishHide() => gameObject.SetActive(false);

    private void Start()
    {
        bool showMenu = flowController == null || flowController.CurrentState == LevelState.Idle;
        if (!economySubscribed && EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged += UpdateCoins;
            EconomyManager.Instance.OnGemsChanged += UpdateGems;
            economySubscribed = true;
        }
        if (!progressionSubscribed && ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged += RefreshHeader;
            ProgressionManager.Instance.OnPlayerExpChanged += HandleExpChanged;
            progressionSubscribed = true;
        }
        if (!uiManagerSubscribed && UIManager.Instance != null)
        {
            UIManager.Instance.OnScreenChanged += HandleScreenChanged;
            UIManager.Instance.OnPopupStackChanged += HandlePopupStackChanged;
            uiManagerSubscribed = true;
        }

        gameObject.SetActive(showMenu);
        if (showMenu)
            SelectTab(currentTab);

        UpdateSideButtonsVisibility();
        RefreshHeader();
    }

    // ─────────────────────────────────────────────────────────────
    //  Tab selection (Screen Layer)
    // ─────────────────────────────────────────────────────────────

    private void OnMapTabClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        SelectTab(Tab.Map, playSound: true);
    }

    private void OnRunnerTabClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        SelectTab(Tab.Runner, playSound: true);
    }

    private void OnPowerupsTabClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        SelectTab(Tab.Powerups, playSound: true);
    }

    private void OnAchievementTabClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        SelectTab(Tab.Achievement, playSound: true);
    }

    private void OnMultiplayerTabClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        SelectTab(Tab.Multiplayer, playSound: true);
    }

    private void SelectTab(Tab tab, bool playSound = false)
    {
        currentTab = tab;

        if (playSound)
            UIAudio.Play(UISoundType.TabSwitch);

        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        if (UIManager.Instance != null)
        {
            if (!uiManagerSubscribed)
            {
                UIManager.Instance.OnScreenChanged += HandleScreenChanged;
                UIManager.Instance.OnPopupStackChanged += HandlePopupStackChanged;
                uiManagerSubscribed = true;
            }

            UIPanelType targetPanel = TabToPanelType(tab);
            if (targetPanel != UIPanelType.None && UIManager.Instance.IsPanelRegistered(targetPanel))
            {
                UIManager.Instance.OpenPanel(targetPanel);
            }
        }

        UpdateTabVisuals();
        UpdateSideButtonsVisibility();
        RefreshHeader();
    }

    private void HandleScreenChanged(UIPanelType screenType)
    {
        if (TryPanelTypeToTab(screenType, out Tab mappedTab) && currentTab != mappedTab)
        {
            currentTab = mappedTab;
            UpdateTabVisuals();
        }

        UpdateSideButtonsVisibility();
    }

    private void HandlePopupStackChanged()
    {
        if (UIManager.Instance != null && !UIManager.Instance.IsAnyPopupOpen)
        {
            if (!TryPanelTypeToTab(UIManager.Instance.CurrentScreen, out Tab activeScreenTab))
                activeScreenTab = Tab.Map;

            if (currentTab != activeScreenTab)
            {
                currentTab = activeScreenTab;
                UpdateTabVisuals();
            }
        }

        UpdateSideButtonsVisibility();
    }

    private void UpdateSideButtonsVisibility()
    {
        bool showSideButtons = UIManager.Instance == null || !UIManager.Instance.IsAnyPopupOpen;

        if (missionButton != null && missionButton != settingButton)
            missionButton.gameObject.SetActive(showSideButtons);

        if (dailyRewardButton != null)
            dailyRewardButton.gameObject.SetActive(showSideButtons);

        if (shopButton != null)
            shopButton.gameObject.SetActive(showSideButtons);

        if (bonusButton != null && bonusButton != settingButton)
            bonusButton.gameObject.SetActive(showSideButtons);

        if (blindBoxButton != null && blindBoxButton != settingButton)
            blindBoxButton.gameObject.SetActive(showSideButtons);
    }

    private static UIPanelType TabToPanelType(Tab tab)
    {
        switch (tab)
        {
            case Tab.Map: return UIPanelType.Map;
            case Tab.Runner: return UIPanelType.Runner;
            case Tab.Powerups: return UIPanelType.Powerups;
            case Tab.Achievement: return UIPanelType.Achievement;
            default: return UIPanelType.None;
            case Tab.Multiplayer: return UIPanelType.Multiplayer;
        }
    }

    private static bool TryPanelTypeToTab(UIPanelType type, out Tab tab)
    {
        switch (type)
        {
            case UIPanelType.Map:
                tab = Tab.Map;
                return true;
            case UIPanelType.Runner:
                tab = Tab.Runner;
                return true;
            case UIPanelType.Powerups:
                tab = Tab.Powerups;
                return true;
            case UIPanelType.Achievement:
                tab = Tab.Achievement;
                return true;
            case UIPanelType.Multiplayer:
                tab = Tab.Multiplayer;
                return true;
            default:
                tab = Tab.Map;
                return false;
        }
    }

    private void UpdateTabVisuals()
    {
        ScaleTab(mapTabButton, currentTab == Tab.Map);
        ScaleTab(runnerTabButton, currentTab == Tab.Runner);
        ScaleTab(powerupsTabButton, currentTab == Tab.Powerups);
        ScaleTab(achievementTabButton, currentTab == Tab.Achievement);
        ScaleTab(multiplayerTabButton, currentTab == Tab.Multiplayer);
    }

    private void ScaleTab(Button button, bool selected)
    {
        if (button == null) return;
        float s = selected ? selectedTabScale : 1f;
        button.transform.DOKill();
        button.transform
            .DOScale(s, tabScaleTweenDuration)
            .SetEase(selected ? Ease.OutBack : Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(button.gameObject);
    }

    // ─────────────────────────────────────────────────────────────
    //  Header (Player Level & EXP Progression Bar)
    // ─────────────────────────────────────────────────────────────

    private void HandleExpChanged(int totalExp, int delta) => RefreshHeader();

    private void RefreshHeader()
    {
        if (EconomyManager.Instance != null)
        {
            UpdateCoins(EconomyManager.Instance.Coins, 0);
            UpdateGems(EconomyManager.Instance.Gems, 0);
        }

        int playerLevel = 1;
        int currentExp = 0;
        int expToNext = PlayerExpConfig.DefaultBaseExpPerLevel;
        float progress = 0f;

        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.EvaluatePlayerExpState(
                out playerLevel,
                out currentExp,
                out expToNext,
                out progress);
        }

        if (levelText != null)
            levelText.text = playerLevel.ToString();

        if (expProgressSlider != null)
        {
            expProgressSlider.minValue = 0f;
            expProgressSlider.maxValue = 1f;
            expProgressSlider.value = progress;
        }

        if (expFillImage != null)
            expFillImage.fillAmount = progress;

        if (expText != null)
            expText.text = $"{currentExp}/{expToNext}";
    }

    private void UpdateCoins(int newBalance, int delta)
    {
        if (coinsText != null) coinsText.text = newBalance.ToString();
    }

    private void UpdateGems(int newBalance, int delta)
    {
        if (gemsText != null) gemsText.text = newBalance.ToString();
    }

    // ─────────────────────────────────────────────────────────────
    //  Side buttons (Popup Layer) + state transitions
    // ─────────────────────────────────────────────────────────────

    private void OnSettingClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Setting);
    }

    private void OnMissionClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
        {
            UIManager.Instance.PushPopup(UIPanelType.Mission);
            MissionPanelUI missionPanel = UIManager.Instance.GetPanel<MissionPanelUI>(UIPanelType.Mission);
            if (missionPanel != null)
                missionPanel.SelectCategory(MissionCategory.Weekly, playSound: false);
        }
    }

    private void OnDailyRewardClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
        {
            UIManager.Instance.PushPopup(UIPanelType.Mission);
            MissionPanelUI missionPanel = UIManager.Instance.GetPanel<MissionPanelUI>(UIPanelType.Mission);
            if (missionPanel != null)
                missionPanel.SelectCategory(MissionCategory.Weekly, playSound: false);
        }
    }

    private void OnBonusClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
            UIManager.Instance.PushPopup(UIPanelType.CoinBonus);
    }

    private void OnBlindBoxClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
            UIManager.Instance.PushPopup(UIPanelType.BlindBox);
    }

    private void OnShopClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null)
            UIManager.Instance.PushPopup(UIPanelType.Shop);
    }

    private void HandleRunnerClosed()
    {
        if (currentTab == Tab.Runner && (flowController == null || flowController.CurrentState == LevelState.Idle))
            SelectTab(Tab.Map);
    }

    private void HandleStateChanged(LevelState state)
    {
        if (state == LevelState.Playing)
        {
            if (UIManager.Instance != null)
            {
                if (UIManager.Instance.IsPanelRegistered(UIPanelType.GameplayHUD))
                {
                    UIManager.Instance.OpenPanel(UIPanelType.GameplayHUD);
                }
                else
                {
                    UIManager.Instance.CloseAllPopups();
                    UIManager.Instance.CloseActiveScreen();
                }
            }
            gameObject.SetActive(false);
        }
        else if (state == LevelState.Idle)
        {
            if (UIManager.Instance != null && UIManager.Instance.IsPanelRegistered(UIPanelType.GameplayHUD))
            {
                UIManager.Instance.GetPanel<InGameHUD>(UIPanelType.GameplayHUD)?.Hide();
            }
            gameObject.SetActive(true);
            SelectTab(currentTab == Tab.Runner ? Tab.Map : currentTab);
        }
    }
}
