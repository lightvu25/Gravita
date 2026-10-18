using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Playables;
using TMPro;

/// <summary>
/// Scene-level cutscene coordinator. Owns the Opening, Death, and Goal
/// sequences (the Bootstrap sequence lives in BootstrapScene via BootstrapUI
/// + SceneLoader and intentionally never re-runs on level loads).
///
/// Each sequence: locks player input, plays the assigned
/// <see cref="PlayableDirector"/> (or optional Animator trigger / timed
/// fallback), coordinates the screen fade via <see cref="ScreenTransition"/>,
/// restores safe state, then invokes a single completion callback.
///
/// Only one sequence runs at a time — Goal and Death can never overlap.
/// Input is re-enabled and the death render-order restored from OnDisable so
/// an interrupted sequence (scene change) can never leave the player locked.
/// </summary>
public class CutsceneManager : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("Opening Cutscene")]
    [SerializeField] private PlayableDirector openingDirector;

    [Header("Death Cutscene")]
    [SerializeField] private PlayableDirector deathDirector;
    [SerializeField] private TMP_Text shardSubTitleText;

    [Header("Goal Cutscene")]
    [SerializeField] private PlayableDirector goalDirector;

    [Header("Player Animation (optional)")]
    [Tooltip("Animator on the player — auto-resolved if unassigned. Missing " +
             "references or empty trigger names fall back to timed waits.")]
    [SerializeField] private Animator playerAnimator;
    [SerializeField] private string enterTrigger = "Enter";
    [SerializeField] private string deathTrigger = "Death";
    [SerializeField] private string exitTrigger = "Exit";

    [Header("Transitions & Fallbacks")]
    [Tooltip("Screen-reveal duration for the Opening sequence.")]
    [SerializeField, Min(0f)] private float openingRevealDuration = 0.5f;
    [Tooltip("Seconds to wait when no opening director is assigned.")]
    [SerializeField, Min(0f)] private float openingFallbackDuration = 0.6f;
    [Tooltip("Seconds to wait when no death director is assigned.")]
    [SerializeField, Min(0f)] private float deathFallbackDuration = 0.8f;
    [Tooltip("Seconds to wait when no goal director is assigned.")]
    [SerializeField, Min(0f)] private float goalFallbackDuration = 1.5f;
    [Tooltip("Cover/reveal fade around the end-of-level transitions.")]
    [SerializeField, Min(0f)] private float endFadeDuration = 0.4f;

    public PlayableDirector GoalDirector => goalDirector;

    /// <summary>True while any sequence is running.</summary>
    public bool IsSequenceRunning => activeSequence != null;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private readonly HashSet<PlayableDirector> subscribedDirectors = new HashSet<PlayableDirector>();
    private readonly HashSet<PlayableDirector> activeDirectors = new HashSet<PlayableDirector>();
    private readonly List<CanvasSortingState> deathCanvasSortingStates = new List<CanvasSortingState>();
    private readonly List<RendererSortingState> playerRendererSortingStates = new List<RendererSortingState>();
    private bool deathRenderOrderApplied;
    private bool deathSequenceControlsRenderOrder;

    private Coroutine activeSequence;
    private bool inputLocked;

    private PlayerMovement player;
    private Rigidbody2D playerBody;

    private struct CanvasSortingState
    {
        public Canvas canvas;
        public bool overrideSorting;
        public int sortingLayerID;
        public int sortingOrder;
    }

    private struct RendererSortingState
    {
        public Renderer renderer;
        public int sortingLayerID;
        public int sortingOrder;
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void OnEnable()
    {
        SubscribeDirector(openingDirector);
        SubscribeDirector(deathDirector);
        SubscribeDirector(goalDirector);
    }

    private void OnDisable()
    {
        foreach (PlayableDirector director in subscribedDirectors)
        {
            if (director == null) continue;
            director.played -= HandleDirectorPlayed;
            director.stopped -= HandleDirectorStopped;
        }

        subscribedDirectors.Clear();
        activeDirectors.Clear();
        deathSequenceControlsRenderOrder = false;
        RestoreDeathRenderOrder();

        // Coroutines die silently on disable/scene unload — restore everything
        // they were holding so input can never stay locked or the screen black.
        Time.timeScale = 1f;
        UnlockPlayer(unfreezePlayer: false);
        activeSequence = null;
    }

    private void Start()
    {
        ConfigureDirector(openingDirector);
        ConfigureDirector(deathDirector);
        ConfigureDirector(goalDirector);
    }

    private void ConfigureDirector(PlayableDirector director)
    {
        if (director == null) return;

        director.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime;
        if (director.state == PlayState.Playing)
            HandleDirectorPlayed(director);
        if (director.state != PlayState.Playing && !director.playOnAwake)
            director.gameObject.SetActive(false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Sequence API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Opening: the screen starts covered (level loads underneath), the
    /// opening director or player Enter animation plays, then the screen is
    /// revealed and input unlocks.
    /// </summary>
    public void PlayOpening(Action onComplete = null)
    {
        if (!TryStartSequence(OpeningRoutine(onComplete)))
            onComplete?.Invoke(); // a sequence is already running — don't strand the caller
    }

    /// <summary>
    /// Death: locks input, plays the death director (or player Death
    /// animation / timed fallback), then covers the screen, fires onComplete
    /// (the caller opens the failure UI underneath), and reveals.
    /// </summary>
    public void PlayDeath(int lostShards, Action onComplete = null)
    {
        if (!TryStartSequence(DeathRoutine(lostShards, onComplete)))
            onComplete?.Invoke(); // a sequence is already running — don't strand the caller
    }

    /// <summary>
    /// Goal: locks input, slides the player into the exit, plays the goal
    /// director (or player Exit animation / timed fallback), then covers the
    /// screen, fires onComplete (the caller opens the result UI underneath),
    /// and reveals.
    /// </summary>
    public void PlayGoal(Vector3 goalPosition, Action onComplete = null)
    {
        if (!TryStartSequence(GoalRoutine(goalPosition, onComplete)))
            onComplete?.Invoke();
    }

    private bool TryStartSequence(IEnumerator routine)
    {
        if (activeSequence != null)
        {
            Debug.LogWarning("[CutsceneManager] A sequence is already running — request ignored.", this);
            return false;
        }

        activeSequence = StartCoroutine(routine);
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  Sequences
    // ─────────────────────────────────────────────────────────────

    private IEnumerator OpeningRoutine(Action onComplete)
    {
        LockPlayer();
        ResolvePlayer();

        ScreenTransition transition = ScreenTransition.GetOrCreate();
        transition.InstantCover(); // level loads under black — no first-frame flash

        if (openingDirector != null)
        {
            openingDirector.gameObject.SetActive(true);
            openingDirector.Play();
            transition.FadeIn(openingRevealDuration);
            yield return StartCoroutine(WaitForDirectorOrSkip(openingDirector));
            openingDirector.gameObject.SetActive(false);
            HandleDirectorStopped(openingDirector);
        }
        else
        {
            TryFireTrigger(enterTrigger, "opening");
            transition.FadeIn(openingRevealDuration);
            yield return new WaitForSecondsRealtime(openingFallbackDuration);
        }

        FinishSequence(unfreezePlayer: true);
        onComplete?.Invoke();
    }

    private IEnumerator DeathRoutine(int lostShards, Action onComplete)
    {
        LockPlayer();
        ResolvePlayer();

        if (shardSubTitleText != null && lostShards > 0)
            shardSubTitleText.text = $"You lost {lostShards} Shards";

        TryFireTrigger(deathTrigger, "death");

        if (deathDirector != null)
        {
            deathSequenceControlsRenderOrder = true;
            ApplyDeathRenderOrder();
            deathDirector.gameObject.SetActive(true);
            deathDirector.Stop();
            deathDirector.time = 0d;
            deathDirector.Evaluate();
            deathDirector.Play();
            yield return StartCoroutine(WaitForDirectorOrSkip(deathDirector));

            deathDirector.Stop();
            deathDirector.time = 0d;
            deathDirector.gameObject.SetActive(false);
            HandleDirectorStopped(deathDirector);
        }
        else
        {
            Debug.LogWarning("[CutsceneManager] No deathDirector assigned — using timed fallback.", this);
            yield return new WaitForSecondsRealtime(deathFallbackDuration);
        }

        deathSequenceControlsRenderOrder = false;
        RestoreDeathRenderOrder();
        Time.timeScale = 1f;

        // Cover → caller opens the failure UI underneath → reveal.
        // Timed waits (not tween callbacks) — a superseded fade can never
        // leave this sequence hanging.
        ScreenTransition transition = ScreenTransition.GetOrCreate();
        transition.FadeOut(endFadeDuration);
        yield return new WaitForSecondsRealtime(endFadeDuration);

        onComplete?.Invoke();

        transition.FadeIn(endFadeDuration);
        yield return new WaitForSecondsRealtime(endFadeDuration);
        FinishSequence(unfreezePlayer: false);
    }

    private IEnumerator GoalRoutine(Vector3 goalPosition, Action onComplete)
    {
        LockPlayer();
        ResolvePlayer();

        // Slide the player into the exit. This coroutine is the sole transform
        // owner during the sequence (PlayerMovement is frozen by inputLocked).
        if (playerBody != null)
        {
            float duration = 0.5f;
            float time = 0f;
            Vector2 startPos = playerBody.position;

            while (time < duration)
            {
                time += Time.unscaledDeltaTime;
                float t = Mathf.Clamp01(time / duration);
                playerBody.position = new Vector2(Mathf.Lerp(startPos.x, goalPosition.x, t), startPos.y);
                yield return null;
            }

            playerBody.position = new Vector2(goalPosition.x, startPos.y);
        }

        TryFireTrigger(exitTrigger, "goal");

        if (goalDirector != null)
        {
            goalDirector.gameObject.SetActive(true);
            goalDirector.Play();
            yield return StartCoroutine(WaitForDirectorOrSkip(goalDirector));
            goalDirector.gameObject.SetActive(false);
            HandleDirectorStopped(goalDirector);
        }
        else
        {
            yield return new WaitForSecondsRealtime(goalFallbackDuration);
        }

        // Cover → caller opens the result UI underneath → reveal.
        ScreenTransition transition = ScreenTransition.GetOrCreate();
        transition.FadeOut(endFadeDuration);
        yield return new WaitForSecondsRealtime(endFadeDuration);

        onComplete?.Invoke();

        transition.FadeIn(endFadeDuration);
        yield return new WaitForSecondsRealtime(endFadeDuration);
        FinishSequence(unfreezePlayer: false);
    }

    // ─────────────────────────────────────────────────────────────
    //  Sequence Helpers
    // ─────────────────────────────────────────────────────────────

    private void TryFireTrigger(string triggerName, string sequence)
    {
        if (playerAnimator == null) return;
        if (string.IsNullOrEmpty(triggerName)) return;

        if (!playerAnimator.isActiveAndEnabled)
        {
            Debug.LogWarning($"[CutsceneManager] Player Animator is disabled — skipping '{triggerName}' trigger for the {sequence} sequence.", this);
            return;
        }

        playerAnimator.SetTrigger(triggerName);
    }

    private void ResolvePlayer()
    {
        if (player == null)
            player = PlayerMovement.Instance != null
                ? PlayerMovement.Instance
                : FindAnyObjectByType<PlayerMovement>();

        if (player != null && playerBody == null)
            playerBody = player.GetComponent<Rigidbody2D>();

        if (player != null && playerAnimator == null)
            playerAnimator = player.GetComponentInChildren<Animator>();
    }

    private void LockPlayer()
    {
        inputLocked = true;
        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(false);
        ResolvePlayer();
        if (player != null)
            player.Freeze();
    }

    /// <summary>
    /// Restores global input. The player's own inputLocked flag is only cleared
    /// for the Opening sequence — Death/Goal leave it frozen because the level
    /// has ended (RespawnPlayer unfreezes on the next start).
    /// </summary>
    private void UnlockPlayer(bool unfreezePlayer)
    {
        if (!inputLocked) return;
        inputLocked = false;

        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(true);

        if (unfreezePlayer && player != null)
            player.Unfreeze();
    }

    private void FinishSequence(bool unfreezePlayer)
    {
        UnlockPlayer(unfreezePlayer);
        activeSequence = null;
    }

    // ─────────────────────────────────────────────────────────────
    //  Director Tracking & Death Render Order
    // ─────────────────────────────────────────────────────────────

    private void SubscribeDirector(PlayableDirector director)
    {
        if (director == null || !subscribedDirectors.Add(director)) return;

        director.played += HandleDirectorPlayed;
        director.stopped += HandleDirectorStopped;
    }

    private void HandleDirectorPlayed(PlayableDirector director)
    {
        if (director == null || !activeDirectors.Add(director)) return;
        if (director == deathDirector)
            ApplyDeathRenderOrder();
    }

    private void HandleDirectorStopped(PlayableDirector director)
    {
        if (director != null)
            activeDirectors.Remove(director);

        if (director == deathDirector && !deathSequenceControlsRenderOrder)
            RestoreDeathRenderOrder();
    }

    private void ApplyDeathRenderOrder()
    {
        if (deathRenderOrderApplied || deathDirector == null) return;

        SortingLayer[] sortingLayers = SortingLayer.layers;
        int highestSortingLayerID = sortingLayers != null && sortingLayers.Length > 0
            ? sortingLayers[sortingLayers.Length - 1].id
            : 0;

        Canvas[] deathCanvases = deathDirector.GetComponentsInChildren<Canvas>(true);
        for (int i = 0; i < deathCanvases.Length; i++)
        {
            Canvas canvas = deathCanvases[i];
            if (canvas == null) continue;

            deathCanvasSortingStates.Add(new CanvasSortingState
            {
                canvas = canvas,
                overrideSorting = canvas.overrideSorting,
                sortingLayerID = canvas.sortingLayerID,
                sortingOrder = canvas.sortingOrder
            });

            int relativeOrder = Mathf.Clamp(canvas.sortingOrder, -100, 100);
            canvas.overrideSorting = true;
            canvas.sortingLayerID = highestSortingLayerID;
            canvas.sortingOrder = 32000 + relativeOrder;
        }

        deathRenderOrderApplied = true;
    }

    private void RestoreDeathRenderOrder()
    {
        if (!deathRenderOrderApplied) return;

        for (int i = 0; i < deathCanvasSortingStates.Count; i++)
        {
            CanvasSortingState state = deathCanvasSortingStates[i];
            if (state.canvas == null) continue;

            state.canvas.overrideSorting = state.overrideSorting;
            state.canvas.sortingLayerID = state.sortingLayerID;
            state.canvas.sortingOrder = state.sortingOrder;
        }

        for (int i = 0; i < playerRendererSortingStates.Count; i++)
        {
            RendererSortingState state = playerRendererSortingStates[i];
            if (state.renderer == null) continue;

            state.renderer.sortingLayerID = state.sortingLayerID;
            state.renderer.sortingOrder = state.sortingOrder;
        }

        deathCanvasSortingStates.Clear();
        playerRendererSortingStates.Clear();
        deathRenderOrderApplied = false;
    }

    /// <summary>
    /// Waits for a director to finish; a double-tap fast-forwards it.
    /// </summary>
    private IEnumerator WaitForDirectorOrSkip(PlayableDirector director)
    {
        if (director == null) yield break;

        float lastInputTime = -10f;
        float doubleTapThreshold = 0.5f;

        // Wait a frame in case Play() hasn't updated the state yet
        yield return null;

        // Wait strictly until the director finishes playing
        while (director.state == PlayState.Playing)
        {
            if (Input.anyKeyDown)
            {
                if (Time.unscaledTime - lastInputTime < doubleTapThreshold)
                {
                    // Double tap detected! Fast-forward cutscene.
                    director.time = director.duration;
                    director.Evaluate();
                    director.Stop(); // Force state to end
                    break;
                }
                lastInputTime = Time.unscaledTime;
            }

            yield return null;
        }
    }
}
