using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Random post-game ad prompt — after a level ends (win or fail), rolls
/// <see cref="showChance"/>; on success shows a small offer panel: watch a
/// rewarded ad for a coin bonus, or dismiss. Keep the panel inactive in the
/// scene; attach this component to an always-active object.
/// </summary>
public class PostGameAdsUI : MonoBehaviour
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private AdRewardService adRewardService;

    [Header("Panel")]
    [SerializeField] private GameObject adOfferPanel;
    [Tooltip("Offer art slot — assign your ad/bonus image.")]
    [SerializeField] private Image offerImage;
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
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (adOfferPanel != null ? adOfferPanel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (adRewardService == null)
            adRewardService = AdRewardService.Instance != null ? AdRewardService.Instance : FindAnyObjectByType<AdRewardService>();

        if (watchAdButton != null) watchAdButton.onClick.AddListener(OnWatchAd);
        if (dismissButton != null) dismissButton.onClick.AddListener(HideOffer);
        if (adOfferPanel != null) adOfferPanel.SetActive(false);
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

    private void HandleLevelFailed(string reason)
    {
        if (showOnFailure) HandleLevelEnded();
    }

    private void HandleLevelEnded()
    {
        if (adOfferPanel == null) return;
        if (UnityEngine.Random.value > showChance) return;
        if (adRewardService != null && !adRewardService.IsAdAvailable(RewardedAdPlacement.PostGameBonus)) return;

        if (offerText != null)
            offerText.text = $"+{bonusCoins} coins!";

        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else adOfferPanel.SetActive(true);
    }

    private void OnWatchAd()
    {
        if (adRewardService == null) { HideOffer(); return; }

        if (watchAdButton != null) watchAdButton.interactable = false;

        adRewardService.WatchAdForReward(RewardedAdPlacement.PostGameBonus, bonusCoins, success =>
        {
            if (!isActiveAndEnabled) return;
            HideOffer();
        });
    }

    private void HideOffer()
    {
        if (watchAdButton != null) watchAdButton.interactable = true;
        if (PanelAnimator != null) PanelAnimator.PlayOut(() => adOfferPanel.SetActive(false));
        else if (adOfferPanel != null) adOfferPanel.SetActive(false);
    }
}
