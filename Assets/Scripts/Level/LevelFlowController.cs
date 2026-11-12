using System;
using UnityEngine;
public enum HazardResponse
{
    FailLevel,
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

    [Header("Revive System")]
    [Tooltip("Maximum number of revives allowed per level run.")]
    [SerializeField, Min(0)] private int maxRevivesPerLevel = 3;

    [Tooltip("Base coin cost for the first coin revive (100 -> 200 -> 300). Increases only when reviving with coins.")]
    [SerializeField, Min(1)] private int baseReviveCoinCost = 100;

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

    /// <summary>Fires when a level fails permanently (after revives are exhausted or declined).</summary>
    public event Action<string> OnLevelFailed;

    /// <summary>Fires when the player revives and resumes the current run.</summary>
    public event Action OnPlayerRevived;

    /// <summary>Fires when the player requests to return to the map / level selection.</summary>
    public event Action OnReturnToMapRequested;

    /// <summary>Fires when the active level is restarted/reset via a restart control.</summary>
    public event Action OnLevelRestarted;

    // ─────────────────────────────────────────────────────────────
    //  Static Run Lifecycle Events (Used by MissionService)
    // ─────────────────────────────────────────────────────────────

    /// <summary>Fires whenever a new level run begins or is reset: (controller, runId).</summary>
    public static event Action<LevelFlowController, long> OnAnyRunStartedOrReset;

    /// <summary>
    /// Fires immediately when the player contacts an active hazard/enemy during Playing state,
    /// BEFORE Shield or Echo rescue is evaluated. Disqualifies "no-death" completion for the run.
    /// </summary>
    public static event Action<LevelFlowController, Hazard> OnAnyHazardContacted;

    /// <summary>Fires when a run fails, is abandoned to the map, or triggers an unshielded death respawn.</summary>
    public static event Action<LevelFlowController, long> OnAnyRunFailedOrAbandoned;

    /// <summary>Fires once per valid level completion: (controller, runId, completedWithoutHazardContact).</summary>
    public static event Action<LevelFlowController, long, bool> OnAnyRunCompletedValidly;

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private static long nextRunIdCounter;
    private long currentRunId;
    private bool hasTouchedHazardThisRun;
    private int movesUsedThisRun;
    private bool usedAnySkillThisRun;
    private PlayerMovement trackedPlayer;

    private int revivesUsedThisLevel;
    private int coinRevivesUsedThisLevel;
    private bool isAwaitingReviveDecision;

    private LevelState currentState = LevelState.Idle;
    private LevelRoot  currentRoot;

    public LevelState  CurrentState   => currentState;
    public LevelData   CurrentLevelData => levelData;
    public LevelRoot   CurrentRoot    => currentRoot;
    public long        CurrentRunId   => currentRunId;
    public bool        HasTouchedHazardThisRun => hasTouchedHazardThisRun;
    public int         MovesUsedThisRun => movesUsedThisRun;
    public bool        UsedAnySkillThisRun => usedAnySkillThisRun;

    public int MaxRevivesPerLevel => maxRevivesPerLevel;
    public int RevivesUsedThisLevel => revivesUsedThisLevel;
    public int RemainingRevives => Mathf.Max(0, maxRevivesPerLevel - revivesUsedThisLevel);
    public int CoinRevivesUsedThisLevel => coinRevivesUsedThisLevel;
    public int CurrentReviveCoinCost => baseReviveCoinCost * (coinRevivesUsedThisLevel + 1);
    public bool CanRevive => EconomyBalanceConfig.Active.EnableMidGameplayRevive && RemainingRevives > 0;
    public bool IsAwaitingReviveDecision => isAwaitingReviveDecision;

    /// <summary>
    /// Star count awarded by the most recent completion — the authoritative
    /// value passed to ProgressionManager. Read by result UIs instead of
    /// recomputing the rating.
    /// </summary>
    public int LastCompletedStars { get; private set; }

    /// <summary>Reward bundle granted by the most recent level clear (null until first completion).</summary>
    public RewardBundle LastClearReward { get; private set; }

    /// <summary>Structured Campaign Economy settlement DTO from the most recent level completion.</summary>
    public LevelEconomySettlement LastEconomySettlement { get; private set; }

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

        PowerUpController.OnAnyPowerUpUsed -= HandleAnyPowerUpUsed;
        PowerUpController.OnAnyPowerUpUsed += HandleAnyPowerUpUsed;

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

    private void FixedUpdate()
    {
        if (currentState != LevelState.Playing || currentRoot == null)
            return;

        if (cutsceneManager != null && cutsceneManager.IsSequenceRunning)
            return;

        currentRoot.TickClock(Time.fixedDeltaTime);
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

        currentRunId = ++nextRunIdCounter;
        hasTouchedHazardThisRun = false;
        movesUsedThisRun = 0;
        usedAnySkillThisRun = false;
        ResetReviveCounters();
        ProgressionManager.Instance?.LockLoadoutForActiveRun();

        SetState(LevelState.Playing);
        OnAnyRunStartedOrReset?.Invoke(this, currentRunId);
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

        UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);

        // Unsubscribe to prevent double-firing
        objectiveSystem.OnPrimaryObjectiveCompleted -= HandleObjectiveCompleted;

        // Reset all layout state (collectibles, exits)
        currentRoot?.ResetAll();

        // Put the player back at the spawn point
        RespawnPlayer();

        // Reset objective
        objectiveSystem.ResetObjective();
        objectiveSystem.OnPrimaryObjectiveCompleted += HandleObjectiveCompleted;

        currentRunId = ++nextRunIdCounter;
        hasTouchedHazardThisRun = false;
        movesUsedThisRun = 0;
        usedAnySkillThisRun = false;
        ResetReviveCounters();
        ProgressionManager.Instance?.LockLoadoutForActiveRun();

        SetState(LevelState.Playing);
        OnAnyRunStartedOrReset?.Invoke(this, currentRunId);
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
    /// Triggers player death flow (e.g. hazard touched).
    /// Runs the Death cutscene first, then offers the Revive popup if lives remain;
    /// otherwise finalizes level failure and opens the Result panel.
    /// </summary>
    public void FailLevel(string reason = "Level Failed")
    {
        if (currentState != LevelState.Playing) return;

        FreezePlayer();
        LastFailReason = reason;
        int lostShards = currentRoot != null ? currentRoot.CollectedCount : 0;

        if (CanRevive && UIManager.Instance != null)
        {
            isAwaitingReviveDecision = true;
            SetState(LevelState.AwaitingRevive);

            if (cutsceneManager != null)
                cutsceneManager.PlayDeath(lostShards, OpenReviveOrResultPanel);
            else
                OpenReviveOrResultPanel();

            Debug.Log($"[LevelFlowController] Player died in '{levelData?.LevelId}' ({reason}) — offering revive ({RemainingRevives}/{maxRevivesPerLevel} left, coin cost={CurrentReviveCoinCost}).");
            return;
        }

        FinalizeLevelFailure(reason);

        // Death sequence, then the result UI opens under the screen cover.
        if (cutsceneManager != null)
            cutsceneManager.PlayDeath(lostShards, OpenResultPanel);
        else
            OpenResultPanel();
    }

    /// <summary>
    /// Spends the current escalated coin cost to revive the player at the last safe resting cell.
    /// Increases the coin cost for the next coin revive (100 -> 200 -> 300).
    /// </summary>
    public bool TryReviveWithCoins()
    {
        if (!CanRevive || (!isAwaitingReviveDecision && currentState != LevelState.AwaitingRevive && currentState != LevelState.Failed))
            return false;

        int cost = CurrentReviveCoinCost;
        if (EconomyManager.Instance == null || !EconomyManager.Instance.SpendCoins(cost, "Revive"))
        {
            Debug.LogWarning($"[LevelFlowController] Cannot revive with coins — insufficient coins (need {cost}).");
            return false;
        }

        coinRevivesUsedThisLevel++;
        revivesUsedThisLevel++;
        Debug.Log($"[LevelFlowController] Player revived using {cost} coins. Remaining revives: {RemainingRevives}/{maxRevivesPerLevel}. Next coin cost: {CurrentReviveCoinCost}.");
        CompleteRevive();
        return true;
    }

    /// <summary>
    /// Revives the player after watching a Rewarded Ad.
    /// Consumes 1 revive life without increasing the coin inflation counter.
    /// </summary>
    public bool ReviveWithAd()
    {
        if (!CanRevive || (!isAwaitingReviveDecision && currentState != LevelState.AwaitingRevive && currentState != LevelState.Failed))
            return false;

        revivesUsedThisLevel++;
        Debug.Log($"[LevelFlowController] Player revived via Rewarded Ad. Remaining revives: {RemainingRevives}/{maxRevivesPerLevel}. Coin cost stays: {CurrentReviveCoinCost}.");
        CompleteRevive();
        return true;
    }

    /// <summary>
    /// Called when the player dismisses the Revive popup ("No Thanks").
    /// Transitions immediately to the Failed result screen.
    /// </summary>
    public void DeclineRevive()
    {
        if (!isAwaitingReviveDecision && currentState == LevelState.Failed)
        {
            OpenResultPanel();
            return;
        }

        isAwaitingReviveDecision = false;
        UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);
        FinalizeLevelFailure(LastFailReason ?? "Level Failed");
        OpenResultPanel();
    }

    /// <summary>
    /// Requests return to campaign map or level selection.
    /// </summary>
    public void ReturnToMap()
    {
        bool wasPlaying = currentState == LevelState.Playing || isAwaitingReviveDecision;
        isAwaitingReviveDecision = false;
        ProgressionManager.Instance?.UnlockLoadoutAfterRun();
        UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);
        levelLoader?.UnloadCurrentLevel();
        SetState(LevelState.Idle);
        if (wasPlaying)
        {
            OnAnyRunFailedOrAbandoned?.Invoke(this, currentRunId);
        }
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

    private void ResetReviveCounters()
    {
        revivesUsedThisLevel = 0;
        coinRevivesUsedThisLevel = 0;
        isAwaitingReviveDecision = false;
    }

    private void OpenReviveOrResultPanel()
    {
        if (CanRevive && UIManager.Instance != null)
        {
            UIManager.Instance.PushPopup(UIPanelType.Revive);
            if (UIManager.Instance.IsPanelOpen(UIPanelType.Revive))
                return;
        }

        FinalizeLevelFailure(LastFailReason ?? "Level Failed");
        OpenResultPanel();
    }

    private void FinalizeLevelFailure(string reason)
    {
        isAwaitingReviveDecision = false;
        LastFailReason = reason;
        ProgressionManager.Instance?.UnlockLoadoutAfterRun();
        IncrementProfileDeaths();
        SetState(LevelState.Failed);
        OnAnyRunFailedOrAbandoned?.Invoke(this, currentRunId);
        OnLevelFailed?.Invoke(reason);
        Debug.Log($"[LevelFlowController] Level '{levelData?.LevelId}' FAILED: {reason}");
    }

    private void CompleteRevive()
    {
        isAwaitingReviveDecision = false;
        UIManager.Instance?.ClosePanelIfOpen(UIPanelType.Revive);

        PlayerMovement player = PlayerMovement.Instance != null
            ? PlayerMovement.Instance
            : FindAnyObjectByType<PlayerMovement>();

        if (player != null)
        {
            Vector3 safePos = ResolveSafeRevivePosition(player);
            player.ResetDirectionHistory();
            player.TeleportTo(safePos);
            player.Unfreeze();
            Physics2D.SyncTransforms();

            Animator anim = player.GetComponentInChildren<Animator>();
            if (anim != null && anim.isActiveAndEnabled)
            {
                anim.Rebind();
                anim.Update(0f);
            }
        }

        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(true);

        SetState(LevelState.Playing);
        OnPlayerRevived?.Invoke();
    }

    private Vector3 ResolveSafeRevivePosition(PlayerMovement player)
    {
        Vector3 fallbackSpawn = currentRoot != null ? currentRoot.PlayerSpawnPosition : player.transform.position;
        Vector3 targetPos = player != null ? player.LastSafePosition : fallbackSpawn;

        // If a BrittleFloor at the resting cell shattered during the fatal slide, restore that tile so the player isn't trapped.
        Collider2D[] overlaps = Physics2D.OverlapCircleAll(targetPos, 0.25f);
        for (int i = 0; i < overlaps.Length; i++)
        {
            if (overlaps[i] == null) continue;
            BrittleFloor brittle = overlaps[i].GetComponentInParent<BrittleFloor>();
            if (brittle != null && brittle.IsShattered)
            {
                brittle.ResetTile();
            }

            FailingTile failing = overlaps[i].GetComponentInParent<FailingTile>();
            if (failing != null && failing.IsArmed)
            {
                failing.ResetTile();
            }
        }

        return targetPos;
    }

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

        BindPlayerTracking(player);
        PlayerCosmeticApplier.EnsureAttached(player);

        player.ResetDirectionHistory();
        player.TeleportTo(currentRoot != null ? currentRoot.PlayerSpawnPosition : player.transform.position);
        player.Unfreeze();
    }

    private void BindPlayerTracking(PlayerMovement player)
    {
        if (trackedPlayer == player)
            return;

        if (trackedPlayer != null)
            trackedPlayer.OnMoveStarted -= HandlePlayerMoveStarted;

        trackedPlayer = player;
        if (trackedPlayer != null)
            trackedPlayer.OnMoveStarted += HandlePlayerMoveStarted;
    }

    private void HandlePlayerMoveStarted(Vector2 _)
    {
        if (currentState == LevelState.Playing)
            movesUsedThisRun++;
    }

    private void HandleAnyPowerUpUsed(PowerUpType _)
    {
        if (currentState == LevelState.Playing)
            usedAnySkillThisRun = true;
    }

    private static void IncrementProfileDeaths()
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile == null) return;
        profile.deaths = Mathf.Max(0, profile.deaths + 1);
        SaveManager.TrySaveProfile(profile);
    }

    private void FreezePlayer()
    {
        if (PlayerMovement.Instance != null)
            PlayerMovement.Instance.Freeze();
    }

    private void HandleHazardTriggered(Hazard hazard)
    {
        if (currentState != LevelState.Playing)
            return;

        if (PlayerMovement.Instance != null && PlayerMovement.Instance.IsPhasing)
            return;

        if (hazard != null && !hazard.IsActiveHazard)
            return;

        // Immediately disqualify "no-death" condition upon any contact with an active hazard/enemy,
        // even if Shield or Echo subsequently prevents actual player death.
        hasTouchedHazardThisRun = true;
        OnAnyHazardContacted?.Invoke(this, hazard);

        PowerUpController powerUps = PowerUpController.Instance != null
            ? PowerUpController.Instance
            : FindAnyObjectByType<PowerUpController>();

        // 1. Active Shield cancels one instance of hazard damage and consumes the protection.
        if (powerUps != null && powerUps.TryConsumeShieldHit(hazard))
        {
            return;
        }

        // 2. Active Echo automatically rescues the player by returning them to the stored Echo position.
        if (powerUps != null && powerUps.TryTriggerEchoRescue(hazard))
        {
            return;
        }

        // Clean up any other temporary power-up effects on player death.
        powerUps?.ClearAllActiveEffects(resetLevelUsageCounts: false);

        if (hazardResponse == HazardResponse.InstantRespawn)
        {
            // GDD instant-respawn: zero downtime, back on the start tile.
            IncrementProfileDeaths();
            OnAnyRunFailedOrAbandoned?.Invoke(this, currentRunId);
            RespawnPlayer();
            Debug.Log($"[LevelFlowController] Hazard hit — instant respawn ({hazard?.FailReason}).");
            return;
        }

        FailLevel(hazard != null ? hazard.FailReason : "Hazard");
    }

    private void HandleObjectiveCompleted()
    {
        if (currentState != LevelState.Playing) return;

        FreezePlayer();
        ProgressionManager.Instance?.UnlockLoadoutAfterRun();
        SetState(LevelState.Completed);

        int optionalCompleted = 0;
        if (objectiveSystem != null && objectiveSystem.OptionalObjectives.Count > 0)
        {
            optionalCompleted = objectiveSystem.CompletedOptionalObjectiveCount;
        }

        // Star rating: when the layout places Star collectibles they drive the
        // rating (0-3 based on collected stars, no artificial floor per Decision 3).
        // Levels without star pickups keep the objective-based rating.
        int stars;
        if (currentRoot != null && currentRoot.TotalStarCount > 0)
        {
            stars = Mathf.Clamp(currentRoot.CollectedStarCount, 0, 3);
        }
        else
        {
            stars = 1 + optionalCompleted;
        }
        LastCompletedStars = stars;

        // Atomic Campaign Economy settlement (GDD §4: First Clear, Replay Star Upgrade, Sector Completion, Best Moves)
        if (levelData != null && !string.IsNullOrEmpty(levelData.LevelId))
        {
            LastEconomySettlement = CampaignEconomyService.SettleLevelCompletion(
                levelData,
                stars,
                movesUsedThisRun,
                usedAnySkillThisRun);
            LastClearReward = LastEconomySettlement.GrantedBundle;
        }
        else
        {
            LastEconomySettlement = default;
            LastClearReward = new RewardBundle();
        }

        bool completedWithoutHazardContact = !hasTouchedHazardThisRun;
        OnAnyRunCompletedValidly?.Invoke(this, currentRunId, completedWithoutHazardContact);
        OnLevelCompleted?.Invoke();

        // Goal sequence — player slides into the exit, then the result UI
        // opens under the screen cover. Rewards above already granted once.
        if (cutsceneManager != null)
            cutsceneManager.PlayGoal(ResolveGoalPosition(), OpenResultPanel);
        else
            OpenResultPanel();

        Debug.Log($"[LevelFlowController] Level '{levelData?.LevelId}' COMPLETED!");
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
        ProgressionManager.Instance?.UnlockLoadoutAfterRun();
        PowerUpController.OnAnyPowerUpUsed -= HandleAnyPowerUpUsed;

        if (trackedPlayer != null)
        {
            trackedPlayer.OnMoveStarted -= HandlePlayerMoveStarted;
            trackedPlayer = null;
        }

        if (objectiveSystem != null)
            objectiveSystem.OnPrimaryObjectiveCompleted -= HandleObjectiveCompleted;

        if (currentRoot != null)
            currentRoot.OnHazardTriggered -= HandleHazardTriggered;
    }
}
