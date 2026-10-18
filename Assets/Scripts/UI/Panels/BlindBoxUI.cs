using TMPro;
using System.Collections;
using DG.Tweening;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Post-completion mystery box flow.
///
/// Attach to an always-active UI object (the panel itself starts inactive).
/// On level completion an "Open Blind Box" button appears on the completion
/// panel. Pressing it opens the blind box panel; tapping the box rolls the
/// loot table (BlindBoxService), reveals the reward, then shows a "x2"
/// rewarded-ad button and an Exit button.
/// </summary>
public class BlindBoxUI : MonoBehaviour
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

    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (blindBoxPanel != null ? blindBoxPanel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
        if (blindBoxService == null)
            blindBoxService = BlindBoxService.Instance != null ? BlindBoxService.Instance : FindAnyObjectByType<BlindBoxService>();

        if (openBoxButton != null) openBoxButton.onClick.AddListener(ShowPanel);
        if (boxButton != null) boxButton.onClick.AddListener(OpenBox);
        if (doubleButton != null) doubleButton.onClick.AddListener(DoubleReward);
        if (exitButton != null) exitButton.onClick.AddListener(ClosePanel);

        if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
        if (openBoxButton != null) openBoxButton.gameObject.SetActive(false);
    }

    private void OnEnable()
    {
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

    // ─────────────────────────────────────────────────────────────
    //  Flow events
    // ─────────────────────────────────────────────────────────────

    private void HandleLevelCompleted()
    {
        blindBoxService?.ResetBox();
        if (openBoxButton != null) openBoxButton.gameObject.SetActive(true);
    }

    private void HandleLevelStarted()
    {
        blindBoxService?.ResetBox();
        if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
        if (openBoxButton != null) openBoxButton.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Box interaction
    // ─────────────────────────────────────────────────────────────

    /// <summary>Opens the blind box panel — also used by the Map screen's chest button.</summary>
    public void ShowPanel()
    {
        if (blindBoxPanel == null) return;

        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else blindBoxPanel.SetActive(true);

        if (blindBoxService != null && blindBoxService.IsOpened)
            SetOpenedVisuals();
        else
            SetClosedVisuals();
    }

    private void OpenBox()
    {
        if (blindBoxService == null || blindBoxService.IsOpened) return;

        if (boxButton != null) boxButton.interactable = false;
        StartCoroutine(RevealRoutine());
    }

    private IEnumerator RevealRoutine()
    {
        // Squash-pop the box while it "opens".
        if (boxButton != null)
        {
            boxButton.transform.DOKill();
            boxButton.transform.DOPunchScale(Vector3.one * 0.3f, revealDelay > 0f ? revealDelay : 0.3f, 6, 0.8f).SetUpdate(true);
        }

        if (revealDelay > 0f)
            yield return new WaitForSecondsRealtime(revealDelay);

        RewardBundle bundle = blindBoxService != null ? blindBoxService.Open() : null;

        if (rewardText != null)
        {
            rewardText.gameObject.SetActive(true);
            rewardText.text = bundle != null
                ? $"You got {blindBoxService.LastRewardLabel}!"
                : "Nothing inside...";
            rewardText.transform.DOKill();
            rewardText.transform.localScale = Vector3.one * 0.5f;
            rewardText.transform.DOScale(1f, 0.25f).SetEase(Ease.OutBack).SetUpdate(true);
        }

        if (doubleButton != null)
        {
            bool canDouble = bundle != null;
            doubleButton.gameObject.SetActive(canDouble);
            doubleButton.interactable = canDouble;
        }

        if (exitButton != null) exitButton.gameObject.SetActive(true);
    }

    /// <summary>Rebuilds the post-reveal state when the panel is re-opened after the box was opened.</summary>
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

        if (doubleButton != null) doubleButton.interactable = false;

        blindBoxService.DoubleWithAd(success =>
        {
            if (!isActiveAndEnabled) return;

            if (success)
            {
                if (rewardText != null)
                    rewardText.text = $"You got {blindBoxService.LastRewardLabel} x2!";
                if (doubleButton != null) doubleButton.gameObject.SetActive(false);
            }
            else if (doubleButton != null)
            {
                doubleButton.interactable = true;
            }
        });
    }

    private void ClosePanel()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(() => blindBoxPanel.SetActive(false));
        else if (blindBoxPanel != null) blindBoxPanel.SetActive(false);
        // The completion-panel button stays so the box can be revisited —
        // it hides for good once a new level starts (HandleLevelStarted).
    }

    private void SetClosedVisuals()
    {
        if (boxButton != null) boxButton.interactable = true;
        if (rewardText != null) rewardText.gameObject.SetActive(false);
        if (doubleButton != null) doubleButton.gameObject.SetActive(false);
        if (exitButton != null) exitButton.gameObject.SetActive(false);
    }
}
