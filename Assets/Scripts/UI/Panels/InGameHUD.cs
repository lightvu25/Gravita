using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class InGameHUD : MonoBehaviour, IUIPanel
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("HUD Root")]
    [SerializeField] private GameObject hudRoot;

    [Header("Header")]
    [SerializeField] private Button menuButton;

    [Header("Currencies")]
    [SerializeField] private TMP_Text coinsText;
    [SerializeField] private Image[] starImages;

    [SerializeField] private Sprite starEmpty;
    [SerializeField] private Sprite starFilled;

    [Header("Run Info")]
    [SerializeField] private TMP_Text movesText;
    [SerializeField] private int moveLimit = 0;

    [Header("Power-up Counter")]
    [SerializeField] private Button shieldButton;
    [SerializeField] private TMP_Text shieldCountText;

    private int movesUsed;

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        if (menuButton != null)
            menuButton.onClick.AddListener(OnMenuClicked);

        if (shieldButton != null)
            shieldButton.onClick.AddListener(OnShieldClicked);
    }

    private void Start()
    {
        if (flowController != null)
        {
            flowController.OnLevelStarted += HandleLevelStarted;
            flowController.OnStateChanged += HandleStateChanged;
            HandleStateChanged(flowController.CurrentState);
        }

        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.OnMoveStopped += HandleMoveCompleted;

        if (EconomyManager.Instance != null)
            EconomyManager.Instance.OnCoinsChanged += UpdateCoins;

        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged += RefreshPowerUp;

        StarCollectible.OnAnyStarCollected += HandleStarCollected;

        RefreshHUD();
    }

    private void OnDestroy()
    {
        if (menuButton != null)
        {
            menuButton.onClick.RemoveListener(OnMenuClicked);
        }

        if (shieldButton != null)
        {
            shieldButton.onClick.RemoveListener(OnShieldClicked);
        }

        if (PlayerMovement.Instance != null)
        {
            PlayerMovement.Instance.OnMoveStopped -= HandleMoveCompleted;
        }

        StarCollectible.OnAnyStarCollected -= HandleStarCollected;

        if (flowController != null)
        {
            flowController.OnLevelStarted -= HandleLevelStarted;
            flowController.OnStateChanged -= HandleStateChanged;
        }

        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged -= UpdateCoins;
        }

        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged -= RefreshPowerUp;
        }
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
    }

    private void HandleStarCollected(StarCollectible star)
    {
        int count = (flowController != null && flowController.CurrentRoot != null) ? flowController.CurrentRoot.CollectedStarCount : 0;
        SetStars(count);
    }

    private void HandleMoveCompleted()
    {
        movesUsed++;
        RefreshMovesText();
    }

    private void RefreshMovesText()
    {
        if (movesText == null) return;
        movesText.text = moveLimit > 0
            ? Mathf.Max(0, moveLimit - movesUsed).ToString()
            : movesUsed.ToString();
    }

    // IUIPanel — HUD visibility is owned by LevelState (HandleStateChanged).
    // UIManager may "close" this panel when opening Pause/Settings on top, so
    // Hide() only takes effect when the game is not actually Playing.
    public void Show() => SetHudVisible(true);

    public void Hide()
    {
        if (flowController == null || flowController.CurrentState != LevelState.Playing)
            SetHudVisible(false);
    }

    private void SetHudVisible(bool visible)
    {
        GameObject target = hudRoot != null ? hudRoot : gameObject;

        if (target.activeSelf != visible)
        {
            target.SetActive(visible);
        }
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
        if (EconomyManager.Instance != null)
        {
            UpdateCoins(EconomyManager.Instance.Coins, 0);
        }

        RefreshMovesText();
        RefreshPowerUp();
        SetStars(0);
    }

    private void UpdateCoins(int amount, int delta) { if (coinsText != null) coinsText.text = amount.ToString(); }

    private void RefreshPowerUp()
    {
        int shieldCount = 0;

        if (ProgressionManager.Instance != null)
        {
            shieldCount =
                ProgressionManager.Instance.GetPowerUpCount(
                    PowerUpIds.KineticShield
                );
        }

        if (shieldCountText != null)
        {
            shieldCountText.text = shieldCount.ToString();
        }

        if (shieldButton != null)
        {
            shieldButton.gameObject.SetActive(shieldCount > 0);
            shieldButton.interactable = shieldCount > 0;
        }
    }

    private void OnShieldClicked()
    {
    }

    private void OnMenuClicked()
    {
        if (UIManager.Instance == null)
        {
            return;
        }

        UIManager.Instance.OpenPanel(UIPanelType.Pause);
    }

}
