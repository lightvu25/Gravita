using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class MainMenuUI : MonoBehaviour, IUIPanel
{
    private enum Tab { Map, Shop, Powerups, Missions, Settings }

    [Header("System References")]
    [SerializeField] private CampaignMapUI campaignMapUI;
    [SerializeField] private ShopUI shopUI;
    [SerializeField] private DailyRewardUI dailyRewardUI;
    [FormerlySerializedAs("visitorTipUI")]
    [SerializeField] private CoinBonusUI coinBonusUI;
    [SerializeField] private LevelFlowController flowController;

    [Header("Tap To Play")]
    [Tooltip("Full-screen intro panel — any tap dismisses it into the main menu.")]
    [SerializeField] private GameObject tapToPlayPanel;
    [SerializeField] private Button tapToPlayButton;
    [Tooltip("Tap-to-play art slot — assign your title/play image.")]
    [SerializeField] private Image tapToPlayImage;

    [Header("Main Menu Root")]
    [SerializeField] private GameObject mainMenuPanel;

    [Header("Header")]
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private TMP_Text gemsText;
    [Tooltip("Icon slots — assign coin/shard/level art in the scene.")]
    [SerializeField] private Image coinIconImage;
    [SerializeField] private Image gemIconImage;
    [SerializeField] private Image levelIconImage;

    [Header("Tab Bar")]
    [SerializeField] private Button mapTabButton;
    [SerializeField] private Button shopTabButton;
    [SerializeField] private Button powerupsTabButton;
    [SerializeField] private Button missionsTabButton;
    [SerializeField] private Button settingsTabButton;
    [Tooltip("Tab icon slots — the selected tab's icon scales up.")]
    [SerializeField] private Image mapTabIcon;
    [SerializeField] private Image shopTabIcon;
    [SerializeField] private Image powerupsTabIcon;
    [SerializeField] private Image missionsTabIcon;
    [SerializeField] private Image settingsTabIcon;
    [SerializeField, Min(1f)] private float selectedTabScale = 1.3f;

    [Header("Side Buttons (Map Screen)")]
    [SerializeField] private Button dailyRewardButton;
    [FormerlySerializedAs("visitorButton")]
    [SerializeField] private Button bonusButton;

    private Tab currentTab = Tab.Map;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (campaignMapUI == null)
            campaignMapUI = FindAnyObjectByType<CampaignMapUI>(FindObjectsInactive.Include);
        if (shopUI == null)
            shopUI = FindAnyObjectByType<ShopUI>(FindObjectsInactive.Include);
        if (dailyRewardUI == null)
            dailyRewardUI = FindAnyObjectByType<DailyRewardUI>(FindObjectsInactive.Include);
        if (coinBonusUI == null)
            coinBonusUI = FindAnyObjectByType<CoinBonusUI>(FindObjectsInactive.Include);

        if (tapToPlayButton != null) tapToPlayButton.onClick.AddListener(OnTapToPlay);
        if (mapTabButton != null) mapTabButton.onClick.AddListener(() => SelectTab(Tab.Map));
        if (shopTabButton != null) shopTabButton.onClick.AddListener(() => SelectTab(Tab.Shop));
        if (powerupsTabButton != null) powerupsTabButton.onClick.AddListener(() => SelectTab(Tab.Powerups));
        if (missionsTabButton != null) missionsTabButton.onClick.AddListener(() => SelectTab(Tab.Missions));
        if (settingsTabButton != null) settingsTabButton.onClick.AddListener(() => SelectTab(Tab.Settings));
        if (dailyRewardButton != null) dailyRewardButton.onClick.AddListener(OnDailyRewardClicked);
        if (bonusButton != null) bonusButton.onClick.AddListener(OnBonusClicked);

        if (campaignMapUI != null) campaignMapUI.OnMapClosed += HandleMapClosed;
        if (shopUI != null) shopUI.OnShopClosed += HandleShopClosed;
        if (dailyRewardUI != null) dailyRewardUI.OnDailyRewardClosed += HandleSubPanelClosed;
        if (coinBonusUI != null) coinBonusUI.OnCoinBonusClosed += HandleSubPanelClosed;

        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged += UpdateCoins;
            EconomyManager.Instance.OnGemsChanged += UpdateGems;
        }

        if (flowController != null)
            flowController.OnStateChanged += HandleStateChanged;
    }

    private void OnDestroy()
    {
        if (campaignMapUI != null) campaignMapUI.OnMapClosed -= HandleMapClosed;
        if (shopUI != null) shopUI.OnShopClosed -= HandleShopClosed;
        if (dailyRewardUI != null) dailyRewardUI.OnDailyRewardClosed -= HandleSubPanelClosed;
        if (coinBonusUI != null) coinBonusUI.OnCoinBonusClosed -= HandleSubPanelClosed;

        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged -= UpdateCoins;
            EconomyManager.Instance.OnGemsChanged -= UpdateGems;
        }

        if (flowController != null)
            flowController.OnStateChanged -= HandleStateChanged;
    }

    // IUIPanel — the menu shell is lifecycle-driven (Start / OnStateChanged).
    // Hide() only deactivates when the game has left Idle: while the player is
    // in the menu, other panels (Map/Shop/…) open on top of this shell, which
    // must stay visible behind them.
    public void Show()
    {
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (flowController == null || flowController.CurrentState != LevelState.Idle)
        {
            if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
            else FinishHide();
        }
    }

    private void FinishHide() => gameObject.SetActive(false);

    private void Start()
    {
        bool showMenu = flowController == null || flowController.CurrentState == LevelState.Idle;
        if (tapToPlayPanel != null) tapToPlayPanel.SetActive(showMenu);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(false);

        RefreshHeader();
    }

    // ─────────────────────────────────────────────────────────────
    //  Tap to play / tab selection
    // ─────────────────────────────────────────────────────────────

    private void OnTapToPlay()
    {
        if (tapToPlayPanel != null) tapToPlayPanel.SetActive(false);
        if (mainMenuPanel != null) mainMenuPanel.SetActive(true);
        SelectTab(Tab.Map); // Map auto-displays first, per TotM flow.
    }

    private void SelectTab(Tab tab)
    {
        currentTab = tab;

        if (mainMenuPanel != null && !mainMenuPanel.activeSelf)
            mainMenuPanel.SetActive(true);

        // UIManager owns single-active-panel switching — opening the new
        // tab's panel closes the previous one.
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(TabToPanelType(tab));

        UpdateTabVisuals();
        RefreshHeader();
    }

    private static UIPanelType TabToPanelType(Tab tab)
    {
        switch (tab)
        {
            case Tab.Map: return UIPanelType.Map;
            case Tab.Shop: return UIPanelType.Shop;
            case Tab.Powerups: return UIPanelType.Powerups;
            case Tab.Missions: return UIPanelType.Mission;
            default: return UIPanelType.Setting;
        }
    }

    private void UpdateTabVisuals()
    {
        ScaleTab(mapTabButton, currentTab == Tab.Map);
        ScaleTab(shopTabButton, currentTab == Tab.Shop);
        ScaleTab(powerupsTabButton, currentTab == Tab.Powerups);
        ScaleTab(missionsTabButton, currentTab == Tab.Missions);
        ScaleTab(settingsTabButton, currentTab == Tab.Settings);
    }

    private void ScaleTab(Button button, bool selected)
    {
        if (button == null) return;
        float s = selected ? selectedTabScale : 1f;
        button.transform.localScale = new Vector3(s, s, 1f);
    }

    // ─────────────────────────────────────────────────────────────
    //  Header
    // ─────────────────────────────────────────────────────────────

    private void RefreshHeader()
    {
        if (EconomyManager.Instance != null)
        {
            UpdateCoins(EconomyManager.Instance.Coins, 0);
            UpdateGems(EconomyManager.Instance.Gems, 0);
        }

        if (levelText != null && ProgressionManager.Instance != null && ProgressionManager.Instance.Database != null)
        {
            levelText.text = FindNextUncompletedLevel()?.DisplayName ?? "";
        }
    }

    /// <summary>First campaign level the player has not completed yet, or null when all are done.</summary>
    private static LevelData FindNextUncompletedLevel()
    {
        var db = ProgressionManager.Instance.Database;
        foreach (var world in db.Worlds)
        {
            if (world.levels == null) continue;
            foreach (var lvl in world.levels)
            {
                if (lvl != null && !ProgressionManager.Instance.IsLevelCompleted(lvl))
                    return lvl;
            }
        }
        return null;
    }

    private void UpdateCoins(int amount, int delta) { if (coinsText != null) coinsText.text = amount.ToString(); }
    private void UpdateGems(int amount, int delta) { if (gemsText != null) gemsText.text = amount.ToString(); }

    // ─────────────────────────────────────────────────────────────
    //  Side buttons + sub-panel close handling
    // ─────────────────────────────────────────────────────────────

    private void OnDailyRewardClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.DailyReward);
    }

    private void OnBonusClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.CoinBonus);
    }

    private void HandleSubPanelClosed()
    {
        // A popup closed (daily reward / coin bonus / shop): reopen the tab
        // that was underneath so the menu never lands on an empty screen.
        if (mainMenuPanel != null && (flowController == null || flowController.CurrentState == LevelState.Idle))
            SelectTab(currentTab);
    }

    private void HandleShopClosed()
    {
        // Shop is a tab — closing it returns focus to the Map tab.
        if (currentTab == Tab.Shop)
            SelectTab(Tab.Map);
        else
            HandleSubPanelClosed();
    }

    private void HandleMapClosed()
    {
        // Map only "closes" when a level starts or another tab takes over;
        // on Idle with no level pending, keep the menu on the Map tab.
        if (flowController != null && flowController.CurrentState == LevelState.Idle && currentTab == Tab.Map)
            SelectTab(Tab.Map);
    }

    // The whole menu shell exists only while Idle — during gameplay the HUD
    // owns the screen; result overlays handle Completed/Failed themselves.
    private void HandleStateChanged(LevelState state)
    {
        if (state == LevelState.Playing)
        {
            if (mainMenuPanel != null) mainMenuPanel.SetActive(false);
            if (tapToPlayPanel != null) tapToPlayPanel.SetActive(false);
        }
        else if (state == LevelState.Idle && mainMenuPanel != null && tapToPlayPanel != null && !tapToPlayPanel.activeSelf)
        {
            mainMenuPanel.SetActive(true);
        }
    }
}
