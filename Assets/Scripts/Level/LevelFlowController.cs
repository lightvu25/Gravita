using System;
using UnityEngine;

/// <summary>
/// Top-level coordinator for a maze level session.
///
/// Responsibilities:
///   - Drive LevelLoader to load/unload the level layout.
///   - Drive ObjectiveSystem to initialise and track objectives.
///   - Manage LevelState transitions (Idle → Loading → Playing → Completed/Failed).
///   - Spawn/reposition the player on level start and reset.
///   - Handle level reset (in-place — no scene reload).
///
/// This is the single entry point for level lifecycle management.
/// Nothing else should directly drive LevelLoader or ObjectiveSystem.
/// </summary>
/// <summary>How the level responds when the player touches a hazard.</summary>
public enum HazardResponse
{
    /// <summary>Classic fail state → retry screen.</summary>
    FailLevel,
    /// <summary>Arcade-style instant respawn on the level's start tile.</summary>
    InstantRespawn,
}

public class LevelFlowController : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Inspector References
    // ─────────────────────────────────────────────────────────────

    [Header("System References")]
    [Tooltip("The LevelLoader responsible for instantiating Layout Prefabs.")]
    [SerializeField] private LevelLoader levelLoader;

    [Tooltip("The objective system for tracking level completion.")]
    [SerializeField] private ObjectiveSystem objectiveSystem;

    [Header("Level To Load")]
    [Tooltip("The LevelData asset to load when the scene starts.")]
    [SerializeField] private LevelData levelData;

    [Tooltip("If true, automatically starts the assigned level on Start without waiting for menu selection.")]
    [SerializeField] private bool autoStartLevel = false;

    [Header("Hazards")]
    [Tooltip("What happens when the player touches a Hazard. InstantRespawn matches the GDD 'death → respawn on start tile' flow.")]
    [SerializeField] private HazardResponse hazardResponse = HazardResponse.FailLevel;

    [Header("Cutscenes")]
    [Tooltip("Runs the Opening/Death/Goal sequences around level lifecycle transitions. Optional — sequences are skipped when unassigned.")]
    [SerializeField] private CutsceneManager cutsceneManager;

    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    /// <summary>Fires every time the level state changes.</summary>
    public event Action<LevelState> OnStateChanged;

    /// <summary>Fires when the level is fully loaded and Playing begins.</summary>
    public event Action OnLevelStarted;

    /// <summary>Fires when the primary objective is completed.</summary>
    public event Action OnLevelCompleted;

    /// <summary>Fires when a level fails.</summary>
    public event Action<string> OnLevelFailed;

    /// <summary>Fires when the player requests to return to the map / level selection.</summary>
    public event Action OnReturnToMapRequested;

    /// <summary>Fires when the active level is restarted/reset via a restart control.</summary>
    public event Action OnLevelRestarted;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private LevelState currentState = LevelState.Idle;
    private LevelRoot  currentRoot;

    public LevelState  CurrentState   => currentState;
    public LevelData   CurrentLevelData => levelData;
    public LevelRoot   CurrentRoot    => currentRoot;

    /// <summary>
    /// Star count awarded by the most recent completion — the authoritative
    /// value passed to ProgressionManager. Read by result UIs instead of
    /// recomputing the rating.
    /// </summary>
    public int LastCompletedStars { get; private set; }

    /// <summary>Reward bundle granted by the most recent level clear (null until first completion).</summary>
    public RewardBundle LastClearReward { get; private set; }

    /// <summary>Failure reason passed to the most recent FailLevel call.</summary>
    public string LastFailReason { get; private set; }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        // Prefer inspector wiring; fall back to same-object then scene lookup.
        if (levelLoader == null)
            levelLoader = GetComponent<LevelLoader>();
        if (levelLoader == null)
            levelLoader = FindAnyObjectByType<LevelLoader>();

        if (objectiveSystem == null)
            objectiveSystem = GetComponent<ObjectiveSystem>();
        if (objectiveSystem == null)
            objectiveSystem = FindAnyObjectByType<ObjectiveSystem>();

        if (cutsceneManager == null)
            cutsceneManager = FindAnyObjectByType<CutsceneManager>();

        ValidateReferences();
    }

    private void Start()
    {
        // If the Main Menu passed a specific level via the global GameSession, use it.
        if (GameSession.Instance != null && GameSession.Instance.pendingLevel != null)
        {
            levelData = GameSession.Instance.pendingLevel;
            GameSession.Instance.pendingLevel = null; // Clear it after reading
            StartLevel(levelData);
            return;
        }

        if (autoStartLevel && levelData != null)
        {
            StartLevel(levelData);
        }
        else
        {
            currentState = LevelState.Idle;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Public API
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Loads and starts the given level.
    /// Can be called at runtime to load a different LevelData (Level Select).
    /// </summary>
    public void StartLevel(LevelData data)
    {
        if (data == null)
        {
            Debug.LogError("[LevelFlowController] Cannot start level — LevelData is null.", this);
            return;
        }

        levelData = data;
        SetState(LevelState.Loading);

        // Load the layout
        currentRoot = levelLoader.LoadLevel(levelData);
        if (currentRoot == null)
        {
            Debug.LogError("[LevelFlowController] Level layout failed to load.", this);
            SetState(LevelState.Idle);
            return;
        }

        HookupLevelRoot(currentRoot);

        // Position the player at the layout's spawn point
        RespawnPlayer();

        // Initialise objective system
        objectiveSystem.Initialize(levelData, currentRoot);
        objectiveSystem.OnPrimaryObjectiveCompleted -= HandleObjectiveCompleted;
        objectiveSystem.OnPrimaryObjectiveCompleted += HandleObjectiveCompleted;

        SetState(LevelState.Playing);
        OnLevelStarted?.Invoke();

        // Opening sequence — covers the level load, reveals, unlocks input.
        cutsceneManager?.PlayOpening();

        Debug.Log($"[LevelFlowController] Level '{levelData.LevelId}' started.");
    }

    /// <summary>
    /// Resets the current level to its initial state without reloading the scene.
    /// Restores all collectibles, hazards, and objective progress.
    /// </summary>
    public void ResetLevel()
    {
        if (currentState == LevelState.Idle || currentState == LevelState.Loading)
        {
            Debug.LogWarning("[LevelFlowController] ResetLevel called but no level is active.", this);
            return;
        }

        // Unsubscribe to prevent double-firing
        objectiveSystem.OnPrimaryObjectiveCompleted -= HandleObjectiveCompleted;

        // Reset all layout state (collectibles, exits)
        currentRoot?.ResetAll();

        // Put the player back at the spawn point
        RespawnPlayer();

        // Reset objective
        objectiveSystem.ResetObjective();
        objectiveSystem.OnPrimaryObjectiveCompleted += HandleObjectiveCompleted;

        SetState(LevelState.Playing);
        OnLevelRestarted?.Invoke();
        Debug.Log($"[LevelFlowController] Level '{levelData.LevelId}' reset.");
    }

    /// <summary>
    /// Convenience method matching standard restart terminology. Calls ResetLevel().
    /// </summary>
    public void RestartLevel()
    {
        ResetLevel();
    }

    /// <summary>
    /// Triggers level failure state (e.g. hazard touched).
    /// </summary>
    public void FailLevel(string reason = "Level Failed")
    {
        if (currentState != LevelState.Playing) return;

        FreezePlayer();
        LastFailReason = reason;
        SetState(LevelState.Failed);
        OnLevelFailed?.Invoke(reason);

        // Death sequence, then the result UI opens under the screen cover.
        int lostShards = currentRoot != null ? currentRoot.CollectedCount : 0;
        if (cutsceneManager != null)
            cutsceneManager.PlayDeath(lostShards, OpenResultPanel);
        else
            OpenResultPanel();

        Debug.Log($"[LevelFlowController] Level '{levelData?.LevelId}' FAILED: {reason}");
    }

    /// <summary>
    /// Requests return to campaign map or level selection.
    /// </summary>
    public void ReturnToMap()
    {
        levelLoader?.UnloadCurrentLevel();
        SetState(LevelState.Idle);
        OnReturnToMapRequested?.Invoke();
        // MainMenuScreen is inactive at boot — UIManager activation is the
        // only reliable way to bring the menu shell back up.
        UIManager.Instance?.OpenPanel(UIPanelType.MainMenu);
    }

    /// <summary>
    /// Loads the specified next LevelData, or automatically advances to the next campaign level.
    /// </summary>
    public void LoadNextLevel(LevelData nextData = null)
    {
        if (nextData == null && ProgressionManager.Instance != null && ProgressionManager.Instance.Database != null)
        {
            nextData = ProgressionManager.Instance.Database.GetNextLevel(levelData);
        }

        if (nextData != null)
        {
            StartLevel(nextData);
        }
        else
        {
            ReturnToMap();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Private
    // ─────────────────────────────────────────────────────────────

    private void HookupLevelRoot(LevelRoot root)
    {
        if (root == null) return;
        root.OnHazardTriggered -= HandleHazardTriggered;
        root.OnHazardTriggered += HandleHazardTriggered;
    }

    private void RespawnPlayer()
    {
        PlayerMovement player = PlayerMovement.Instance != null
            ? PlayerMovement.Instance
            : FindAnyObjectByType<PlayerMovement>();

        if (player == null) return;

        player.TeleportTo(currentRoot != null ? currentRoot.PlayerSpawnPosition : player.transform.position);
        player.Unfreeze();
    }

    private void FreezePlayer()
    {
        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.Freeze();
    }

    private void HandleHazardTriggered(Hazard hazard)
    {
        if (hazardResponse == HazardResponse.InstantRespawn)
        {
            // GDD instant-respawn: zero downtime, back on the start tile.
            RespawnPlayer();
            Debug.Log($"[LevelFlowController] Hazard hit — instant respawn ({hazard?.FailReason}).");
            return;
        }

        // A banked Kinetic Shield absorbs one hazard hit — respawn instead of failing.
        if (ProgressionManager.Instance != null && ProgressionManager.Instance.ConsumePowerUp(PowerUpIds.KineticShield))
        {
            RespawnPlayer();
            Debug.Log($"[LevelFlowController] Kinetic Shield consumed — hazard absorbed ({hazard?.FailReason}).");
            return;
        }

        FailLevel(hazard != null ? hazard.FailReason : "Hazard");
    }

    private void HandleObjectiveCompleted()
    {
        if (currentState != LevelState.Playing) return;

        FreezePlayer();
        SetState(LevelState.Completed);

        int optionalCompleted = 0;
        if (objectiveSystem != null && objectiveSystem.OptionalObjectives.Count > 0)
        {
            optionalCompleted = objectiveSystem.CompletedOptionalObjectiveCount;
        }

        // Star rating: when the layout places StarCollectibles they drive the
        // rating (1 for reaching the exit, +1 per star pickup). Levels without
        // star pickups keep the objective-based rating.
        int stars;
        if (currentRoot != null && currentRoot.TotalStarCount > 0)
        {
            stars = Mathf.Clamp(currentRoot.CollectedStarCount, 1, 3);
        }
        else
        {
            stars = 1 + optionalCompleted;
        }
        LastCompletedStars = stars;

        bool isFirstClear = false;
        // Record progression and stars
        if (ProgressionManager.Instance != null && levelData != null)
        {
            isFirstClear = !ProgressionManager.Instance.IsLevelCompleted(levelData);
            ProgressionManager.Instance.CompleteLevel(levelData, stars);
        }

        // Route level rewards through RewardCalculator -> RewardService -> EconomyManager
        var rewardService = RewardService.Instance != null ? RewardService.Instance : FindAnyObjectByType<RewardService>();
        if (rewardService != null && levelData != null)
        {
            var bundle = RewardCalculator.CalculateLevelRewards(levelData, stars, isFirstClear, optionalCompleted);
            rewardService.GrantRewardBundle(bundle, $"LevelClear_{levelData.LevelId}");
            LastClearReward = bundle;
        }

        OnLevelCompleted?.Invoke();

        // Goal sequence — player slides into the exit, then the result UI
        // opens under the screen cover. Rewards above already granted once.
        if (cutsceneManager != null)
            cutsceneManager.PlayGoal(ResolveGoalPosition(), OpenResultPanel);
        else
            OpenResultPanel();

        Debug.Log($"[LevelFlowController] Level '{levelData.LevelId}' COMPLETED!");
    }

    private void OpenResultPanel()
    {
        UIManager.Instance?.OpenPanel(UIPanelType.Result);
    }

    /// <summary>World position of the level exit for the Goal sequence slide-in.</summary>
    private Vector3 ResolveGoalPosition()
    {
        if (currentRoot != null && currentRoot.Exits != null && currentRoot.Exits.Length > 0
            && currentRoot.Exits[0] != null)
        {
            return currentRoot.Exits[0].transform.position;
        }

        return PlayerMovement.Instance != null
            ? PlayerMovement.Instance.transform.position
            : Vector3.zero;
    }

    private void SetState(LevelState newState)
    {
        if (currentState == newState) return;
        currentState = newState;
        OnStateChanged?.Invoke(currentState);
    }

    private void ValidateReferences()
    {
        if (levelLoader == null)
            Debug.LogError("[LevelFlowController] LevelLoader reference is not assigned.", this);

        if (objectiveSystem == null)
            Debug.LogError("[LevelFlowController] ObjectiveSystem reference is not assigned.", this);
    }

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle Cleanup
    // ─────────────────────────────────────────────────────────────

    private void OnDestroy()
    {
        if (objectiveSystem != null)
            objectiveSystem.OnPrimaryObjectiveCompleted -= HandleObjectiveCompleted;

        if (currentRoot != null)
            currentRoot.OnHazardTriggered -= HandleHazardTriggered;
    }
}
