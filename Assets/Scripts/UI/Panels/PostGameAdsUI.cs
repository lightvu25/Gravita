using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Optional post-game ad prompt — gated behind <see cref="EconomyBalanceConfig.EnableLegacyExtraRewardSystems"/>.
/// </summary>
public class PostGameAdsUI : MonoBehaviour
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private AdRewardService adRewardService;

    [Header("Panel")]
    [SerializeField] private GameObject adOfferPanel;
    [SerializeField] private TMP_Text offerText;
    [SerializeField] private Button watchAdButton;
    [SerializeField] private Button dismissButton;

    [Header("Offer")]
    [Tooltip("Chance (0-1) the ad offer appears after a level ends.")]
    [SerializeField, Range(0f, 1f)] private float showChance = 0.4f;
    [Tooltip("Coin bonus granted for watching.")]
    [SerializeField, Min(0)] private int bonusCoins = 40;
    [Tooltip("Also offer after a failed run.")]
    [SerializeField] private bool showOnFailure = true;

    private UIPanelAnimator panelAnimator;
    private bool buttonsBound;

    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (adOfferPanel != null ? adOfferPanel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (adRewardService == null)
            adRewardService = AdRewardService.Instance != null ? AdRewardService.Instance : FindAnyObjectByType<AdRewardService>();

        BindButtons();
        if (adOfferPanel != null)
            adOfferPanel.SetActive(false);
    }

    private void OnEnable()
    {
        if (flowController != null)
        {
            flowController.OnLevelCompleted += HandleLevelEnded;
            flowController.OnLevelFailed += HandleLevelFailed;
            flowController.OnLevelStarted += HideOffer;
        }
    }

    private void OnDisable()
    {
        if (flowController != null)
        {
            flowController.OnLevelCompleted -= HandleLevelEnded;
            flowController.OnLevelFailed -= HandleLevelFailed;
            flowController.OnLevelStarted -= HideOffer;
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

        if (watchAdButton != null)
            watchAdButton.onClick.AddListener(OnWatchAd);
        if (dismissButton != null)
            dismissButton.onClick.AddListener(OnDismissClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (watchAdButton != null)
            watchAdButton.onClick.RemoveListener(OnWatchAd);
        if (dismissButton != null)
            dismissButton.onClick.RemoveListener(OnDismissClicked);
    }

    private void HandleLevelFailed(string reason)
    {
        if (showOnFailure) HandleLevelEnded();
    }

    private void HandleLevelEnded()
    {
        if (!EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems)
        {
            if (adOfferPanel != null) adOfferPanel.SetActive(false);
            return;
        }

        if (adOfferPanel == null) return;
        if (Random.value > showChance) return;
        if (adRewardService != null && !adRewardService.IsAdAvailable(RewardedAdPlacement.PostGameBonus)) return;

        if (offerText != null)
            offerText.text = $"+{bonusCoins} coins!";

        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else adOfferPanel.SetActive(true);
    }

    private void OnWatchAd()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (adRewardService == null)
        {
            HideOffer();
            return;
        }

        if (watchAdButton != null)
            watchAdButton.interactable = false;

        adRewardService.WatchAdForReward(RewardedAdPlacement.PostGameBonus, bonusCoins, _ =>
        {
            if (!isActiveAndEnabled) return;
            HideOffer();
        });
    }

    private void OnDismissClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        HideOffer();
    }

    private void HideOffer()
    {
        if (watchAdButton != null)
            watchAdButton.interactable = true;

        if (PanelAnimator != null)
            PanelAnimator.PlayOut(() => { if (adOfferPanel != null) adOfferPanel.SetActive(false); });
        else if (adOfferPanel != null)
            adOfferPanel.SetActive(false);
    }
}
