using System.Collections;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-completion mystery box flow.
/// Gated behind <see cref="EconomyBalanceConfig.EnablePostLevelBlindBox"/> in Campaign mode.
/// </summary>
public class BlindBoxUI : MonoBehaviour, IUIPanel
{
    [Header("References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private BlindBoxService blindBoxService;

    [Header("Completion Panel")]
    [Tooltip("Button shown on the completion panel after finishing a level.")]
    [SerializeField] private Button openBoxButton;

    [Header("Blind Box Panel")]
    [SerializeField] private GameObject blindBoxPanel;
    [SerializeField] private Button boxButton;
    [SerializeField] private TMP_Text rewardText;
    [SerializeField] private Button doubleButton;
    [SerializeField] private Button exitButton;

    [Header("Reveal")]
    [Tooltip("Delay between tapping the box and revealing the reward.")]
    [SerializeField, Min(0f)] private float revealDelay = 0.5f;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private bool buttonsBound;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private bool HasDedicatedCompletionButton =>
        openBoxButton != null && openBoxButton.GetComponentInParent<MainMenuUI>(true) == null;

    private void Awake()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (blindBoxService == null)
            blindBoxService = BlindBoxService.Instance != null ? BlindBoxService.Instance : FindAnyObjectByType<BlindBoxService>();

        BindButtons();

        if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
        if (HasDedicatedCompletionButton) openBoxButton.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        BindButtons();
        if (flowController != null)
        {
            flowController.OnLevelCompleted += HandleLevelCompleted;
            flowController.OnLevelStarted += HandleLevelStarted;
        }
    }

    private void OnDisable()
    {
        if (flowController != null)
        {
            flowController.OnLevelCompleted -= HandleLevelCompleted;
            flowController.OnLevelStarted -= HandleLevelStarted;
        }
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (HasDedicatedCompletionButton) openBoxButton.onClick.AddListener(OnOpenBoxButtonClicked);
        if (boxButton != null) boxButton.onClick.AddListener(OpenBox);
        if (doubleButton != null) doubleButton.onClick.AddListener(DoubleReward);
        if (exitButton != null) exitButton.onClick.AddListener(OnExitButtonClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (openBoxButton != null) openBoxButton.onClick.RemoveListener(OnOpenBoxButtonClicked);
        if (boxButton != null) boxButton.onClick.RemoveListener(OpenBox);
        if (doubleButton != null) doubleButton.onClick.RemoveListener(DoubleReward);
        if (exitButton != null) exitButton.onClick.RemoveListener(OnExitButtonClicked);
    }

    public void Show() => ShowPanel();
    public void Hide() => ClosePanel();

    // ─────────────────────────────────────────────────────────────
    //  Flow events
    // ─────────────────────────────────────────────────────────────

    private void HandleLevelCompleted()
    {
        blindBoxService?.ResetBox();
        if (!EconomyBalanceConfig.Active.EnablePostLevelBlindBox)
        {
            if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
            if (HasDedicatedCompletionButton) openBoxButton.gameObject.SetActive(false);
            return;
        }

        if (HasDedicatedCompletionButton) openBoxButton.gameObject.SetActive(true);
    }

    private void HandleLevelStarted()
    {
        blindBoxService?.ResetBox();
        if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
        if (HasDedicatedCompletionButton) openBoxButton.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Box interaction
    // ─────────────────────────────────────────────────────────────

    private void OnOpenBoxButtonClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        ShowPanel();
    }

    /// <summary>Opens the blind box panel — also used by the Map screen's chest button.</summary>
    public void ShowPanel()
    {
        if (blindBoxService != null && !BlindBoxService.CanClaimDailyBox() && !blindBoxService.IsOpened)
        {
            ClosePanelThroughManagerOrSelf();
            return;
        }

        if (BlindBoxService.CanClaimDailyBox())
            blindBoxService?.ResetBox();

        GameObject targetPanel = blindBoxPanel != null ? blindBoxPanel : gameObject;
        if (!targetPanel.activeSelf)
            targetPanel.SetActive(true);
        if (!gameObject.activeSelf)
            gameObject.SetActive(true);

        BindButtons();
        UIAudio.Play(UISoundType.PanelOpen);
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else targetPanel.SetActive(true);

        if (blindBoxService != null && blindBoxService.IsOpened)
            SetOpenedVisuals();
        else
            SetClosedVisuals();
    }

    private void OpenBox()
    {
        if (blindBoxService == null || blindBoxService.IsOpened) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (boxButton != null) boxButton.interactable = false;
        StartCoroutine(RevealRoutine());
    }

    private IEnumerator RevealRoutine()
    {
        if (boxButton != null)
        {
            boxButton.transform.DOKill();
            boxButton.transform
                .DOPunchScale(Vector3.one * 0.3f, revealDelay > 0f ? revealDelay : 0.3f, 6, 0.8f)
                .SetUpdate(true)
                .SetLink(boxButton.gameObject);
        }

        if (revealDelay > 0f)
            yield return new WaitForSecondsRealtime(revealDelay);

        RewardBundle bundle = blindBoxService != null ? blindBoxService.Open(allowDirectMenuOpen: true) : null;
        UIAudio.Play(UISoundType.BlindBoxOpen);

        bool shownPopup = bundle != null && !bundle.IsEmpty &&
                          CoinUI.TryShowRewardPopup(bundle, alreadyGrantedBase: true, onClosedCallback: CloseBlindBoxAfterRewardPopup);

        if (rewardText != null)
        {
            rewardText.gameObject.SetActive(true);
            rewardText.text = bundle != null
                ? $"You got {blindBoxService.LastRewardLabel}!"
                : "Nothing inside...";
            rewardText.transform.DOKill();
            rewardText.transform.localScale = Vector3.one * 0.5f;
            rewardText.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true).SetLink(rewardText.gameObject);
        }

        if (doubleButton != null)
        {
            bool canDouble = !shownPopup && bundle != null && bundle.GemCount == 0;
            doubleButton.gameObject.SetActive(canDouble);
            doubleButton.interactable = canDouble;
        }

        if (exitButton != null) exitButton.gameObject.SetActive(true);
    }

    private void CloseBlindBoxAfterRewardPopup()
    {
        if (this == null) return;
        ClosePanelThroughManagerOrSelf();
    }

    private void ClosePanelThroughManagerOrSelf()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.BlindBox))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.BlindBox);
        else
            ClosePanel();
    }

    private void SetOpenedVisuals()
    {
        if (boxButton != null) boxButton.interactable = false;
        if (rewardText != null)
        {
            rewardText.gameObject.SetActive(true);
            rewardText.text = blindBoxService != null
                ? $"You got {blindBoxService.LastRewardLabel}!"
                : "";
        }
        if (doubleButton != null)
        {
            bool canDouble = blindBoxService != null && !blindBoxService.IsDoubled && !string.IsNullOrEmpty(blindBoxService.LastRewardLabel);
            doubleButton.gameObject.SetActive(canDouble);
            doubleButton.interactable = canDouble;
        }
        if (exitButton != null) exitButton.gameObject.SetActive(true);
    }

    private void DoubleReward()
    {
        if (blindBoxService == null || blindBoxService.IsDoubled) return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (doubleButton != null) doubleButton.interactable = false;

        blindBoxService.DoubleWithAd(success =>
        {
            if (!isActiveAndEnabled) return;

            if (success)
            {
                UIAudio.Play(UISoundType.RewardClaim);
                if (rewardText != null)
                    rewardText.text = $"You got {blindBoxService.LastRewardLabel} x2!";
                if (doubleButton != null) doubleButton.gameObject.SetActive(false);
            }
            else if (doubleButton != null)
            {
                UIAudio.Play(UISoundType.ErrorOrLocked);
                doubleButton.interactable = true;
            }
        });
    }

    private void OnExitButtonClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.BlindBox))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.BlindBox);
        else
            ClosePanel();
    }

    private void ClosePanel()
    {
        UIAudio.Play(UISoundType.PanelClose);
        GameObject targetPanel = blindBoxPanel != null ? blindBoxPanel : gameObject;
        if (PanelAnimator != null) PanelAnimator.PlayOut(() => targetPanel.SetActive(false));
        else targetPanel.SetActive(false);
    }

    private void SetClosedVisuals()
    {
        if (boxButton != null) boxButton.interactable = true;
        if (rewardText != null) rewardText.gameObject.SetActive(false);
        if (doubleButton != null) doubleButton.gameObject.SetActive(false);
        if (exitButton != null) exitButton.gameObject.SetActive(false);
    }
}
