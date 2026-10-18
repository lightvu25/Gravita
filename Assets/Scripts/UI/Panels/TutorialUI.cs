using TMPro;
using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// First-time onboarding overlay for the tutorial level.
///
/// Observes the real gameplay flow only — it never modifies objectives and
/// never blocks gameplay input. All visual elements should be
/// raycastTarget=false except the Skip button so swipes reach GameInput.
///
/// Steps:
///   0 — "Swipe or press a direction to move." (waits for the player to start sliding)
///   1 — "Reach the exit!" (waits for level completion)
///
/// Completion or Skip persists ProfileData.tutorialCompleted.
/// The overlay re-arms on restart as long as the flag is unset.
/// </summary>
public class TutorialUI : MonoBehaviour, IUIPanel
{
    [Header("Config")]
    [Tooltip("The level that hosts the tutorial (Level 1).")]
    [SerializeField] private LevelData tutorialLevel;

    [Header("UI References")]
    [SerializeField] private GameObject content;
    [SerializeField] private TMP_Text instructionText;
    [SerializeField] private TMP_Text stepText;
    [SerializeField] private Button skipButton;

    /// <summary>Fired when the tutorial overlay begins (step 0 shown).</summary>
    public event Action OnTutorialStarted;
    /// <summary>Fired when a tutorial step is satisfied (0 = moved, 1 = level completed).</summary>
    public event Action<int> OnTutorialStepCompleted;
    /// <summary>Fired when the player presses SKIP.</summary>
    public event Action OnTutorialSkipped;
    /// <summary>Fired when the tutorial finishes via normal level completion.</summary>
    public event Action OnTutorialCompleted;

    private LevelFlowController flowController;
    private int step = -1; // -1 = inactive
    private bool subscribed;

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        if (skipButton != null)
            skipButton.onClick.AddListener(OnSkipClicked);
    }

    private void Start()
    {
        Subscribe();
        HideOverlay();
    }

    // IUIPanel — tutorial visibility is driven by the gameplay flow
    // (TryBegin/SetStep), not by UIManager navigation. Show() intentionally
    // does nothing; Hide() dismisses the overlay content only so the
    // flow observer on this GameObject keeps running.
    public void Show() { }
    public void Hide() => HideOverlay();

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed || flowController == null) return;
        subscribed = true;
        flowController.OnLevelStarted += HandleLevelStarted;
        flowController.OnLevelCompleted += HandleLevelCompleted;
        flowController.OnStateChanged += HandleStateChanged;
        flowController.OnReturnToMapRequested += HandleReturnToMap;
    }

    private void Unsubscribe()
    {
        if (!subscribed || flowController == null) return;
        subscribed = false;
        flowController.OnLevelStarted -= HandleLevelStarted;
        flowController.OnLevelCompleted -= HandleLevelCompleted;
        flowController.OnStateChanged -= HandleStateChanged;
        flowController.OnReturnToMapRequested -= HandleReturnToMap;
    }

    private void Update()
    {
        if (step != 0) return;

        // Player performed their first slide — advance.
        if (PlayerMovement.Instance != null && PlayerMovement.Instance.IsMoving)
        {
            OnTutorialStepCompleted?.Invoke(0);
            SetStep(1);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Tutorial lifecycle
    // ─────────────────────────────────────────────────────────────

    private void TryBegin()
    {
        if (tutorialLevel == null || flowController == null) return;
        if (flowController.CurrentLevelData != tutorialLevel) return;

        if (IsTutorialCompleted())
        {
            EndTutorial();
            return;
        }

        SetStep(0);
    }

    private void SetStep(int newStep)
    {
        bool wasInactive = step < 0;
        step = newStep;

        if (content != null) content.SetActive(true);
        if (newStep == 0 && wasInactive) OnTutorialStarted?.Invoke();

        if (instructionText != null)
        {
            instructionText.text = newStep == 0
                ? "Swipe or press a direction key to move."
                : "Reach the exit!";
        }

        if (stepText != null)
            stepText.text = $"{newStep + 1} / 2";
    }

    private void CompleteAndHide(bool skipped)
    {
        if (skipped)
        {
            OnTutorialSkipped?.Invoke();
        }
        else
        {
            OnTutorialStepCompleted?.Invoke(1);
            OnTutorialCompleted?.Invoke();
        }
        MarkTutorialCompleted();
        EndTutorial();
    }

    private void EndTutorial()
    {
        step = -1;
        HideOverlay();
    }

    private void HideOverlay()
    {
        if (content != null) content.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Persistence
    // ─────────────────────────────────────────────────────────────

    private static bool IsTutorialCompleted()
    {
        var profile = SaveManager.loadProfile();
        return profile != null && profile.tutorialCompleted;
    }

    private static void MarkTutorialCompleted()
    {
        // Load fresh from disk — never write through a stale cached profile.
        var profile = SaveManager.loadProfile() ?? new ProfileData();
        profile.tutorialCompleted = true;
        SaveManager.saveProfile(profile);
    }

    // ─────────────────────────────────────────────────────────────
    //  Flow event handlers
    // ─────────────────────────────────────────────────────────────

    private void HandleLevelStarted() => TryBegin();

    private void HandleStateChanged(LevelState state)
    {
        if (state == LevelState.Playing)
        {
            // Covers RestartLevel — OnLevelStarted does not fire on reset.
            TryBegin();
        }
        else if (state == LevelState.Idle)
        {
            // Level unloaded / returned to map — leave flag untouched.
            EndTutorial();
        }
    }

    private void HandleLevelCompleted()
    {
        // Normal completion of the tutorial level ends onboarding.
        if (step >= 0)
            CompleteAndHide(false);
    }

    private void HandleReturnToMap() => EndTutorial();

    private void OnSkipClicked() => CompleteAndHide(true);
}
