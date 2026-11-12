using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime coordinator for the six Gravita power-ups:
/// Shield, Magnet, Breaker, Echo, Teleport, and Freeze.
///
/// Integrates directly with <see cref="PlayerMovement"/>, <see cref="LevelFlowController"/>,
/// <see cref="LevelRoot"/>, <see cref="Collectible"/>, <see cref="CrackedBlock"/>,
/// <see cref="Hazard"/>, and <see cref="ProgressionManager"/>.
/// All runtime timers and state live on this component — never inside ScriptableObjects.
/// </summary>
public class PowerUpController : MonoBehaviour
{
    public static PowerUpController Instance { get; private set; }

    [Header("System References")]
    [Tooltip("Reference to PlayerMovement. Auto-resolved if left empty.")]
    [SerializeField] private PlayerMovement playerMovement;

    [Tooltip("Reference to LevelFlowController. Auto-resolved if left empty.")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Power-up Data Assets")]
    [SerializeField] private PowerUpData shieldData;
    [SerializeField] private PowerUpData magnetData;
    [SerializeField] private PowerUpData breakerData;
    [SerializeField] private PowerUpData echoData;
    [SerializeField] private PowerUpData teleportData;
    [SerializeField] private PowerUpData freezeData;

    [Header("Echo Visual Settings")]
    [Tooltip("Optional custom prefab spawned for the stationary Echo marker. If unassigned, a translucent copy of the player sprite is generated automatically.")]
    [SerializeField] private GameObject echoVisualPrefab;

    [Tooltip("Tint applied to the auto-generated stationary Echo visual.")]
    [SerializeField] private Color echoTintColor = new Color(0.35f, 0.92f, 1f, 0.65f);

    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    /// <summary>Fired when a power-up is successfully activated (or when Echo returns).</summary>
    public event Action<PowerUpType> OnPowerUpActivated;

    /// <summary>
    /// Static event fired once per genuine power-up activation (when a power-up is consumed/used).
    /// Does not fire on the second-press manual Echo return.
    /// </summary>
    public static event Action<PowerUpType> OnAnyPowerUpUsed;

    /// <summary>Fired whenever any power-up runtime state, active flag, or usage count changes.</summary>
    public event Action OnPowerUpStateChanged;

    /// <summary>
    /// Static analytics & telemetry event fired on every skill activation attempt (GDD §13):
    /// (skillType, succeeded, reasonCode).
    /// </summary>
    public static event Action<PowerUpType, bool, string> OnSkillActivationAttempted;

    // ─────────────────────────────────────────────────────────────
    //  Runtime State (Never stored in ScriptableObjects)
    // ─────────────────────────────────────────────────────────────

    private readonly Dictionary<PowerUpType, int> usesThisLevel = new Dictionary<PowerUpType, int>();
    private readonly Dictionary<PowerUpType, float> cooldownRemaining = new Dictionary<PowerUpType, float>();

    // Shield runtime state
    private int activeShieldHits;

    // Magnet runtime state
    private bool isMagnetActive;
    private float magnetRemainingTime;

    // Echo runtime state
    private bool isEchoActive;
    private float echoRemainingTime;
    private Vector3 storedEchoPosition;
    private GameObject activeEchoObject;

    // Freeze runtime state
    private bool isFreezeActive;
    private float freezeRemainingTime;
    private readonly List<IFreezable> activeFrozenTargets = new List<IFreezable>();

    // Physics query buffers
    private readonly Collider2D[] cellOverlapBuffer = new Collider2D[16];
    private readonly RaycastHit2D[] boundsRayHits = new RaycastHit2D[8];

    // ─────────────────────────────────────────────────────────────
    //  Public Read-Only State Properties
    // ─────────────────────────────────────────────────────────────

    public bool IsShieldActive => activeShieldHits > 0;
    public int ActiveShieldHits => activeShieldHits;

    public bool IsMagnetActive => isMagnetActive;
    public float MagnetRemainingTime => magnetRemainingTime;

    public bool IsEchoActive => isEchoActive;
    public float EchoRemainingTime => echoRemainingTime;
    public Vector3 StoredEchoPosition => storedEchoPosition;

    public bool IsFreezeActive => isFreezeActive;
    public float FreezeRemainingTime => freezeRemainingTime;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;

        ResolveReferences();
        ResetUsageDictionaries();
    }

    private void OnEnable()
    {
        ResolveReferences();
        SubscribeFlowEvents();
    }

    private void Start()
    {
        ResolveReferences();
        SubscribeFlowEvents();
    }

    private void OnDisable()
    {
        UnsubscribeFlowEvents();
        ClearAllActiveEffects(resetLevelUsageCounts: false);
    }

    private void OnDestroy()
    {
        UnsubscribeFlowEvents();
        ClearAllActiveEffects(resetLevelUsageCounts: false);

        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        UpdateCooldowns(Time.deltaTime);

        if (!IsGameplayActive())
            return;

        if (isMagnetActive)
            UpdateMagnet(Time.deltaTime);

        if (isEchoActive)
            UpdateEcho(Time.deltaTime);

        if (isFreezeActive)
            UpdateFreeze(Time.deltaTime);
    }

    // ─────────────────────────────────────────────────────────────
    //  Initialization & Event Wiring
    // ─────────────────────────────────────────────────────────────

    private bool flowEventsSubscribed;

    private void ResolveReferences()
    {
        if (playerMovement == null)
            playerMovement = GetComponent<PlayerMovement>();
        if (playerMovement == null)
            playerMovement = PlayerMovement.Instance != null
                ? PlayerMovement.Instance
                : FindAnyObjectByType<PlayerMovement>();

        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();
    }

    private void SubscribeFlowEvents()
    {
        if (flowEventsSubscribed || flowController == null) return;

        flowController.OnLevelStarted += HandleLevelStartedOrRestarted;
        flowController.OnLevelRestarted += HandleLevelStartedOrRestarted;
        flowController.OnStateChanged += HandleLevelStateChanged;
        flowController.OnLevelFailed += HandleLevelFailed;
        flowController.OnLevelCompleted += HandleLevelCompleted;
        flowController.OnReturnToMapRequested += HandleReturnToMap;
        flowEventsSubscribed = true;
    }

    private void UnsubscribeFlowEvents()
    {
        if (!flowEventsSubscribed || flowController == null) return;

        flowController.OnLevelStarted -= HandleLevelStartedOrRestarted;
        flowController.OnLevelRestarted -= HandleLevelStartedOrRestarted;
        flowController.OnStateChanged -= HandleLevelStateChanged;
        flowController.OnLevelFailed -= HandleLevelFailed;
        flowController.OnLevelCompleted -= HandleLevelCompleted;
        flowController.OnReturnToMapRequested -= HandleReturnToMap;
        flowEventsSubscribed = false;
    }

    private void HandleLevelStartedOrRestarted()
    {
        ClearAllActiveEffects(resetLevelUsageCounts: true);
        if (playerMovement != null)
            playerMovement.ResetDirectionHistory();
    }

    private void HandleLevelStateChanged(LevelState newState)
    {
        if (newState != LevelState.Playing)
        {
            ClearAllActiveEffects(resetLevelUsageCounts: false);
        }
    }

    private void HandleLevelFailed(string _)
    {
        ClearAllActiveEffects(resetLevelUsageCounts: false);
    }

    private void HandleLevelCompleted()
    {
        ClearAllActiveEffects(resetLevelUsageCounts: false);
    }

    private void HandleReturnToMap()
    {
        ClearAllActiveEffects(resetLevelUsageCounts: true);
    }

    private void ResetUsageDictionaries()
    {
        foreach (PowerUpType type in Enum.GetValues(typeof(PowerUpType)))
        {
            usesThisLevel[type] = 0;
            cooldownRemaining[type] = 0f;
        }
    }

    /// <summary>
    /// Cleans up all active power-up timers, visuals, and temporary states.
    /// Safe to call on level reset, death, completion, or scene unload.
    /// </summary>
    public void ClearAllActiveEffects(bool resetLevelUsageCounts)
    {
        activeShieldHits = 0;

        isMagnetActive = false;
        magnetRemainingTime = 0f;

        CleanupEchoState();
        EndFreezeEffect();

        if (resetLevelUsageCounts)
            ResetUsageDictionaries();

        OnPowerUpStateChanged?.Invoke();
    }

    // ─────────────────────────────────────────────────────────────
    //  Data & Inventory Queries
    // ─────────────────────────────────────────────────────────────

    public PowerUpData GetPowerUpData(PowerUpType type)
    {
        return type switch
        {
            PowerUpType.Shield   => shieldData,
            PowerUpType.Magnet   => magnetData,
            PowerUpType.Breaker  => breakerData,
            PowerUpType.Echo     => echoData,
            PowerUpType.Teleport => teleportData,
            PowerUpType.Freeze   => freezeData,
            _                    => null
        };
    }

    public PowerUpData GetPowerUpData(string skillId)
    {
        return TryGetPowerUpType(skillId, out PowerUpType type) ? GetPowerUpData(type) : null;
    }

    public bool IsEffectActive(PowerUpType type)
    {
        return type switch
        {
            PowerUpType.Shield => IsShieldActive,
            PowerUpType.Magnet => IsMagnetActive,
            PowerUpType.Echo   => IsEchoActive,
            PowerUpType.Freeze => IsFreezeActive,
            _                  => false
        };
    }

    public static string GetPowerUpId(PowerUpType type)
    {
        return type switch
        {
            PowerUpType.Shield   => PowerUpIds.Shield,
            PowerUpType.Magnet   => PowerUpIds.Magnet,
            PowerUpType.Breaker  => PowerUpIds.Breaker,
            PowerUpType.Echo     => PowerUpIds.Echo,
            PowerUpType.Teleport => PowerUpIds.Teleport,
            PowerUpType.Freeze   => PowerUpIds.Freeze,
            _                    => string.Empty
        };
    }

    public static bool TryGetPowerUpType(string skillId, out PowerUpType type)
    {
        if (!string.IsNullOrEmpty(skillId))
        {
            if (string.Equals(skillId, PowerUpIds.Breaker, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Breaker;
                return true;
            }
            if (string.Equals(skillId, PowerUpIds.Freeze, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Freeze;
                return true;
            }
            if (string.Equals(skillId, PowerUpIds.Teleport, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Teleport;
                return true;
            }
            if (string.Equals(skillId, PowerUpIds.Shield, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(skillId, PowerUpIds.KineticShield, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Shield;
                return true;
            }
            if (string.Equals(skillId, PowerUpIds.Magnet, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Magnet;
                return true;
            }
            if (string.Equals(skillId, PowerUpIds.Echo, StringComparison.OrdinalIgnoreCase))
            {
                type = PowerUpType.Echo;
                return true;
            }
        }

        type = default;
        return false;
    }

    public static string GetFallbackDisplayName(PowerUpType type) => type.ToString();

    public static string GetFallbackDisplayName(string skillId)
    {
        return TryGetPowerUpType(skillId, out PowerUpType type)
            ? GetFallbackDisplayName(type)
            : (skillId ?? string.Empty);
    }

    public string ResolveDisplayName(PowerUpType type)
    {
        PowerUpData data = GetPowerUpData(type);
        return data != null && !string.IsNullOrWhiteSpace(data.DisplayName)
            ? data.DisplayName
            : GetFallbackDisplayName(type);
    }

    private static readonly PowerUpType[] DefaultSupportedPowerUpTypes =
        (PowerUpType[])Enum.GetValues(typeof(PowerUpType));

    /// <summary>
    /// Returns the authoritative list of supported <see cref="PowerUpType"/> values in the game.
    /// </summary>
    public static IReadOnlyList<PowerUpType> GetSupportedPowerUpTypes()
    {
        return DefaultSupportedPowerUpTypes;
    }

    /// <summary>
    /// Returns the number of charges the player currently owns in <see cref="ProgressionManager"/>.
    /// </summary>
    public int GetOwnedCount(PowerUpType type)
    {
        if (ProgressionManager.Instance == null) return 0;

        PowerUpData data = GetPowerUpData(type);
        string id = data != null && !string.IsNullOrEmpty(data.Id) ? data.Id : GetPowerUpId(type);
        return ProgressionManager.Instance.GetPowerUpCount(id);
    }

    public int GetRemainingLevelUses(PowerUpType type)
    {
        PowerUpData data = GetPowerUpData(type);
        int limit = data != null ? data.UsageLimit : 1;
        if (limit <= 0) return int.MaxValue;

        int used = usesThisLevel.TryGetValue(type, out int u) ? u : 0;
        return Mathf.Max(0, limit - used);
    }

    public float GetCooldownRemaining(PowerUpType type)
    {
        return cooldownRemaining.TryGetValue(type, out float cd) ? Mathf.Max(0f, cd) : 0f;
    }

    /// <summary>
    /// Returns whether the power-up button can currently be pressed by the player.
    /// Note: For Echo, returns true while an Echo is active so the second press can trigger manual return.
    /// </summary>
    public bool CanActivate(PowerUpType type)
    {
        return EvaluateActivationEligibility(type, out _);
    }

    private bool EvaluateActivationEligibility(PowerUpType type, out string blockReason)
    {
        ResolveReferences();

        if (!IsGameplayActive())
        {
            blockReason = "GameplayInactive";
            return false;
        }

        if (playerMovement == null || !playerMovement.gameObject.activeInHierarchy || playerMovement.InputLocked)
        {
            blockReason = "InputLocked";
            return false;
        }

        // Second press on Echo returns the player to the stored Echo position without consuming another charge
        if (type == PowerUpType.Echo && isEchoActive)
        {
            blockReason = string.Empty;
            return true;
        }

        PowerUpData data = GetPowerUpData(type);
        string skillId = data != null && !string.IsNullOrEmpty(data.Id) ? data.Id : GetPowerUpId(type);

        // Enforce locked 4-slot Loadout during active Campaign gameplay (GDD §5.5)
        if (ProgressionManager.Instance != null && ProgressionManager.Instance.IsLoadoutLocked)
        {
            if (!ProgressionManager.Instance.IsSkillEquippedInLoadout(skillId))
            {
                blockReason = "NotInLoadout";
                return false;
            }
        }

        if (GetCooldownRemaining(type) > 0f)
        {
            blockReason = "OnCooldown";
            return false;
        }

        if (GetRemainingLevelUses(type) <= 0)
        {
            blockReason = "LevelUsageLimitReached";
            return false;
        }

        bool isConsumable = data == null || data.IsConsumable;
        if (isConsumable && ProgressionManager.Instance != null && GetOwnedCount(type) <= 0)
        {
            blockReason = "ZeroCharges";
            return false;
        }

        bool availableState = type switch
        {
            PowerUpType.Shield => activeShieldHits <= 0,
            PowerUpType.Magnet => !isMagnetActive,
            PowerUpType.Freeze => !isFreezeActive,
            _                  => true
        };

        blockReason = availableState ? string.Empty : "AlreadyActive";
        return availableState;
    }

    // ─────────────────────────────────────────────────────────────
    //  Main Activation Entry Point
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to activate the specified power-up from a single button press.
    /// If preconditions or target checks fail, returns false without consuming a charge.
    /// </summary>
    public bool TryActivate(PowerUpType type)
    {
        ResolveReferences();

        // Special handling for Echo second button press: return to stored Echo position
        if (type == PowerUpType.Echo && isEchoActive)
        {
            if (!IsGameplayActive() || playerMovement == null || playerMovement.InputLocked)
                return false;

            bool returned = TryReturnToEcho(isAutomaticRescue: false);
            if (returned)
            {
                OnPowerUpActivated?.Invoke(PowerUpType.Echo);
                OnPowerUpStateChanged?.Invoke();
            }
            return returned;
        }

        if (!EvaluateActivationEligibility(type, out string blockReason))
        {
            OnSkillActivationAttempted?.Invoke(type, false, blockReason);
            return false;
        }

        bool activated = type switch
        {
            PowerUpType.Shield   => ActivateShield(),
            PowerUpType.Magnet   => ActivateMagnet(),
            PowerUpType.Breaker  => ActivateBreaker(),
            PowerUpType.Echo     => ActivateEcho(),
            PowerUpType.Teleport => ActivateTeleport(),
            PowerUpType.Freeze   => ActivateFreeze(),
            _                    => false
        };

        if (!activated)
        {
            OnSkillActivationAttempted?.Invoke(type, false, "PreconditionFailed");
            return false;
        }

        // Record usage & consume inventory ONLY after activation succeeds
        usesThisLevel[type] = (usesThisLevel.TryGetValue(type, out int used) ? used : 0) + 1;

        PowerUpData data = GetPowerUpData(type);
        if (data != null && data.Cooldown > 0f)
            cooldownRemaining[type] = data.Cooldown;

        bool isConsumable = data == null || data.IsConsumable;
        if (isConsumable && ProgressionManager.Instance != null)
        {
            ConsumeOwnedPowerUp(type);
        }

        OnSkillActivationAttempted?.Invoke(type, true, "Success");
        OnPowerUpActivated?.Invoke(type);
        OnAnyPowerUpUsed?.Invoke(type);
        OnPowerUpStateChanged?.Invoke();
        return true;
    }

    private void ConsumeOwnedPowerUp(PowerUpType type)
    {
        if (ProgressionManager.Instance == null) return;

        PowerUpData data = GetPowerUpData(type);
        string id = data != null && !string.IsNullOrEmpty(data.Id) ? data.Id : GetPowerUpId(type);
        ProgressionManager.Instance.ConsumePowerUp(id);
    }

    // ─────────────────────────────────────────────────────────────
    //  A. Shield Implementation
    // ─────────────────────────────────────────────────────────────

    private bool ActivateShield()
    {
        if (activeShieldHits > 0)
            return false;

        int hitsToBlock = shieldData != null ? shieldData.BlockedDamageCount : 1;
        activeShieldHits = Mathf.Max(1, hitsToBlock);
        if (playerMovement != null)
            SpawnRadialPulseVFX(GetPlayerCenter(playerMovement), new Color(0.25f, 0.92f, 1f, 0.85f), 2.4f, 0.35f);
        Debug.Log($"[PowerUpController] Shield activated (blocks {activeShieldHits} damage instance).");
        return true;
    }

    /// <summary>
    /// Checked by <see cref="LevelFlowController"/> when the player contacts a lethal <see cref="Hazard"/>.
    /// If Shield is active, cancels the damage instance and consumes the protection.
    /// </summary>
    public bool TryConsumeShieldHit(Hazard hazard = null)
    {
        if (activeShieldHits <= 0)
            return false;

        activeShieldHits--;
        Debug.Log($"[PowerUpController] Shield blocked damage from '{(hazard != null ? hazard.name : "Hazard")}'. Remaining shield hits: {activeShieldHits}.");
        OnPowerUpStateChanged?.Invoke();
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  B. Magnet Implementation (Duration: 6s)
    // ─────────────────────────────────────────────────────────────

    private float GetMagnetDuration()
    {
        return magnetData != null && magnetData.Duration > 0f ? magnetData.Duration : 6f;
    }

    private float GetMagnetRadius()
    {
        return magnetData != null && magnetData.AttractionRadius > 0f ? magnetData.AttractionRadius : 3f;
    }

    private float GetMagnetPullSpeed()
    {
        return magnetData != null && magnetData.PullSpeed > 0f ? magnetData.PullSpeed : 12f;
    }

    private bool ActivateMagnet()
    {
        if (isMagnetActive)
            return false;

        isMagnetActive = true;
        magnetRemainingTime = GetMagnetDuration();
        if (playerMovement != null)
            SpawnRadialPulseVFX(GetPlayerCenter(playerMovement), new Color(1f, 0.85f, 0.2f, 0.85f), GetMagnetRadius() * 1.4f, 0.4f);
        Debug.Log($"[PowerUpController] Magnet activated for {magnetRemainingTime:F1}s (radius: {GetMagnetRadius():F1}).");
        return true;
    }

    private void UpdateMagnet(float deltaTime)
    {
        if (!isMagnetActive) return;

        magnetRemainingTime -= deltaTime;

        if (playerMovement != null && playerMovement.gameObject.activeInHierarchy)
        {
            Vector3 playerPos = GetPlayerCenter(playerMovement);
            float radius = GetMagnetRadius();
            float radiusSqr = radius * radius;
            float pullStep = GetMagnetPullSpeed() * deltaTime;
            const float collectThresholdSqr = 0.22f * 0.22f;

            Collectible[] collectibles = GetLevelCollectibles();
            if (collectibles != null)
            {
                for (int i = 0; i < collectibles.Length; i++)
                {
                    Collectible coin = collectibles[i];
                    if (coin == null || !coin.IsCoin || coin.IsCollected || !coin.gameObject.activeInHierarchy)
                        continue;

                    Vector3 coinPos = coin.transform.position;
                    float distSqr = (coinPos - playerPos).sqrMagnitude;
                    if (distSqr > radiusSqr)
                        continue;

                    Vector3 nextPos = Vector3.MoveTowards(coinPos, playerPos, pullStep);
                    coin.transform.position = nextPos;

                    if ((nextPos - playerPos).sqrMagnitude <= collectThresholdSqr)
                    {
                        coin.Collect();
                    }
                }
            }
        }

        if (magnetRemainingTime <= 0f)
        {
            isMagnetActive = false;
            magnetRemainingTime = 0f;
            Debug.Log("[PowerUpController] Magnet ended.");
            OnPowerUpStateChanged?.Invoke();
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  C. Breaker Implementation (Up to 2 wall cells, at most 1 trap)
    // ─────────────────────────────────────────────────────────────

    private int CurrentLevel(string skillId) =>
        ProgressionManager.Instance != null ? ProgressionManager.Instance.GetPowerUpLevel(skillId) : 1;

    private int GetBreakerMaxWallCells() =>
        breakerData != null ? breakerData.GetMaxWallCells(CurrentLevel(PowerUpIds.Breaker)) : 2;

    private int GetBreakerMaxTraps()
    {
        return breakerData != null ? Mathf.Max(0, breakerData.MaxTrapsDestroyed) : 1;
    }

    private bool ActivateBreaker()
    {
        if (playerMovement == null)
            return false;

        int maxCells = GetBreakerMaxWallCells();
        int maxWalls = maxCells;
        int maxTraps = GetBreakerMaxTraps();

        Vector2 primaryDir = GetEstablishedDirection(playerMovement);
        int wallsBroken = 0;
        int trapsBroken = 0;
        Vector2 resolvedDir = primaryDir;

        if (primaryDir != Vector2.zero)
        {
            TryBreakAlongDirection(primaryDir, maxCells, maxWalls, maxTraps, ref wallsBroken, ref trapsBroken);
        }

        // Fallback: if player has not moved yet or no breakable target was in the primary facing direction,
        // scan the remaining cardinal directions so adjacent CrackedBlocks/traps can still be shattered.
        if (wallsBroken == 0 && trapsBroken == 0)
        {
            Vector2[] cardinalDirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
            for (int d = 0; d < cardinalDirs.Length; d++)
            {
                if (cardinalDirs[d] == primaryDir) continue;
                TryBreakAlongDirection(cardinalDirs[d], maxCells, maxWalls, maxTraps, ref wallsBroken, ref trapsBroken);
                if (wallsBroken > 0 || trapsBroken > 0)
                {
                    resolvedDir = cardinalDirs[d];
                    break;
                }
            }
        }

        Vector2 vfxDir = resolvedDir != Vector2.zero ? resolvedDir : Vector2.up;
        if (wallsBroken == 0 && trapsBroken == 0)
        {
            SpawnDirectionalScanOrBurstVFX(vfxDir, maxCells, succeeded: false);
            Debug.Log("[PowerUpController] Breaker failed — no eligible breakable wall or trap in range.");
            return false;
        }

        SpawnDirectionalScanOrBurstVFX(vfxDir, maxCells, succeeded: true);
        Physics2D.SyncTransforms();
        Debug.Log($"[PowerUpController] Breaker succeeded — broke {wallsBroken} wall(s) and {trapsBroken} trap(s).");
        return true;
    }

    private void TryBreakAlongDirection(
        Vector2 dir,
        int maxCells,
        int maxWalls,
        int maxTraps,
        ref int wallsBroken,
        ref int trapsBroken)
    {
        float cellSize = Mathf.Max(0.1f, playerMovement.CellSize);
        Vector2 boxSize = Vector2.one * (cellSize * 0.75f);

        for (int step = 1; step <= maxCells; step++)
        {
            Vector2 cellCenter = GetCellCenterInFront(playerMovement, dir, step);

            int hitCount = Physics2D.OverlapBox(cellCenter, boxSize, 0f, ContactFilter2D.noFilter, cellOverlapBuffer);
            for (int i = 0; i < hitCount; i++)
            {
                Collider2D col = cellOverlapBuffer[i];
                if (col == null) continue;

                // 1. Check for breakable wall (CrackedBlock)
                if (wallsBroken < maxWalls)
                {
                    CrackedBlock cracked = col.GetComponentInParent<CrackedBlock>();
                    if (cracked != null && !cracked.IsBroken)
                    {
                        cracked.Break();
                        wallsBroken++;
                        continue;
                    }
                }

                // 2. Check for eligible trap (Hazard that is a trap, not an enemy)
                if (trapsBroken < maxTraps)
                {
                    Hazard hazard = col.GetComponentInParent<Hazard>();
                    if (hazard != null && hazard.CanBeBrokenByBreaker)
                    {
                        if (hazard.BreakTrap())
                        {
                            trapsBroken++;
                        }
                    }
                }
            }
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  D. Echo Implementation (Duration: 5s, stationary copy + rescue)
    // ─────────────────────────────────────────────────────────────

    private float GetEchoDuration()
    {
        return echoData != null && echoData.Duration > 0f ? echoData.Duration : 5f;
    }

    private bool ActivateEcho()
    {
        if (isEchoActive || playerMovement == null)
            return false;

        Vector3 spawnPos = GetValidEchoSpawnPosition(playerMovement);
        if (!IsValidPlayerDestination(spawnPos, ignoreHazards: false))
        {
            Debug.Log("[PowerUpController] Echo failed — current player position is not a valid safe cell.");
            return false;
        }

        storedEchoPosition = spawnPos;
        isEchoActive = true;
        echoRemainingTime = GetEchoDuration();

        SpawnEchoVisual(storedEchoPosition);
        Debug.Log($"[PowerUpController] Echo placed at {storedEchoPosition} for {echoRemainingTime:F1}s.");
        return true;
    }

    private void UpdateEcho(float deltaTime)
    {
        if (!isEchoActive) return;

        echoRemainingTime -= deltaTime;
        if (echoRemainingTime <= 0f)
        {
            CleanupEchoState();
            Debug.Log("[PowerUpController] Echo expired after 5s.");
            OnPowerUpStateChanged?.Invoke();
        }
    }

    /// <summary>
    /// Triggered by <see cref="LevelFlowController"/> when the player hits a lethal <see cref="Hazard"/>
    /// while an Echo is active. Automatically returns the player to the stored Echo position and cancels death.
    /// </summary>
    public bool TryTriggerEchoRescue(Hazard hazard = null)
    {
        if (!isEchoActive)
            return false;

        bool rescued = TryReturnToEcho(isAutomaticRescue: true);
        if (rescued)
        {
            Debug.Log($"[PowerUpController] Echo automatic rescue triggered on '{(hazard != null ? hazard.name : "Hazard")}'. Returned to {storedEchoPosition}.");
            OnPowerUpStateChanged?.Invoke();
        }
        return rescued;
    }

    private bool TryReturnToEcho(bool isAutomaticRescue)
    {
        if (!isEchoActive || playerMovement == null)
            return false;

        Vector3 targetPos = storedEchoPosition;

        // Immediately clear Echo state first to prevent duplicate rescue/return events
        CleanupEchoState();

        if (!TryResolveSafeEchoDestination(targetPos, out Vector3 resolvedDestination))
        {
            Debug.LogWarning("[PowerUpController] Stored Echo destination became invalid and no safe adjacent cell was found.");
            return false;
        }

        playerMovement.TeleportTo(resolvedDestination);
        playerMovement.Unfreeze();
        Physics2D.SyncTransforms();

        Debug.Log($"[PowerUpController] Player returned to Echo position {resolvedDestination} (Rescue: {isAutomaticRescue}).");
        return true;
    }

    private bool TryResolveSafeEchoDestination(Vector3 desiredPos, out Vector3 safePos)
    {
        if (IsValidPlayerDestination(desiredPos, ignoreHazards: false))
        {
            safePos = desiredPos;
            return true;
        }

        // If the exact stored cell became blocked (e.g. a BrittleFloor shattered after placing Echo),
        // deterministically check the 4 orthogonal neighbor cells (Up, Right, Down, Left).
        float cellSize = playerMovement != null ? Mathf.Max(0.1f, playerMovement.CellSize) : 1f;
        Vector2[] cardinalDirs = { Vector2.up, Vector2.right, Vector2.down, Vector2.left };

        for (int i = 0; i < cardinalDirs.Length; i++)
        {
            Vector3 candidate = desiredPos + (Vector3)(cardinalDirs[i] * cellSize);
            if (IsValidPlayerDestination(candidate, ignoreHazards: false) && IsInsideLevelBounds(candidate))
            {
                safePos = candidate;
                return true;
            }
        }

        safePos = desiredPos;
        return false;
    }

    private void SpawnEchoVisual(Vector3 worldPosition)
    {
        DestroyEchoVisual();

        Transform parent = flowController != null && flowController.CurrentRoot != null
            ? flowController.CurrentRoot.transform
            : null;

        if (echoVisualPrefab != null)
        {
            activeEchoObject = Instantiate(echoVisualPrefab, worldPosition, Quaternion.identity, parent);
            activeEchoObject.name = "PlayerEcho_Marker";
            return;
        }

        activeEchoObject = new GameObject("PlayerEcho_Marker");
        if (parent != null)
            activeEchoObject.transform.SetParent(parent, worldPositionStays: true);
        activeEchoObject.transform.position = worldPosition;

        SpriteRenderer playerSprite = playerMovement != null
            ? playerMovement.GetComponentInChildren<SpriteRenderer>()
            : null;

        if (playerSprite != null)
        {
            GameObject visualChild = new GameObject("Visual");
            visualChild.transform.SetParent(activeEchoObject.transform, worldPositionStays: false);
            visualChild.transform.localPosition = playerSprite.transform.localPosition;
            visualChild.transform.localRotation = playerSprite.transform.localRotation;
            visualChild.transform.localScale = playerSprite.transform.localScale;

            SpriteRenderer echoRenderer = visualChild.AddComponent<SpriteRenderer>();
            echoRenderer.sprite = playerSprite.sprite;
            echoRenderer.color = echoTintColor;
            echoRenderer.sortingLayerID = playerSprite.sortingLayerID;
            echoRenderer.sortingOrder = playerSprite.sortingOrder - 1;
            echoRenderer.flipX = playerSprite.flipX;
            echoRenderer.flipY = playerSprite.flipY;
        }
    }

    private void DestroyEchoVisual()
    {
        if (activeEchoObject != null)
        {
            Destroy(activeEchoObject);
            activeEchoObject = null;
        }
    }

    private void CleanupEchoState()
    {
        isEchoActive = false;
        echoRemainingTime = 0f;
        DestroyEchoVisual();
    }

    // ─────────────────────────────────────────────────────────────
    //  E. Teleport Implementation (1 to 3 consecutive wall cells)
    // ─────────────────────────────────────────────────────────────

    private int GetMinWallTraversal()
    {
        return teleportData != null ? Mathf.Max(1, teleportData.MinWallTraversal) : 1;
    }

    private int GetMaxWallTraversal() =>
        teleportData != null
            ? Mathf.Max(GetMinWallTraversal(), teleportData.GetMaxWallTraversal(CurrentLevel(PowerUpIds.Teleport)))
            : 3;

    /// <summary>
    /// Deterministically teleports the player across 1 to 3 consecutive wall cells
    /// along the player's most recently established movement direction, landing on
    /// the first valid, in-bounds playable cell immediately on the other side of the
    /// traversed wall segment.
    /// </summary>
    private bool ActivateTeleport()
    {
        if (playerMovement == null)
            return false;

        Vector2 dir = GetEstablishedDirection(playerMovement);
        if (dir == Vector2.zero)
        {
            Debug.Log("[PowerUpController] Teleport failed — player has no established movement direction.");
            return false;
        }

        int minWalls = GetMinWallTraversal();
        int maxWalls = GetMaxWallTraversal();

        // Count consecutive wall cells starting immediately in front of the player (step = 1..maxWalls).
        // Stop at the first non-wall cell so we deterministically select the cell immediately beyond
        // the contiguous wall segment.
        int consecutiveWallCells = 0;
        for (int step = 1; step <= maxWalls; step++)
        {
            Vector2 cellCenter = GetCellCenterInFront(playerMovement, dir, step);

            // Do not teleport through active enemies or traps
            if (HasActiveHazardAtCell(cellCenter))
            {
                Debug.Log("[PowerUpController] Teleport failed — cannot teleport through an active trap or enemy.");
                return false;
            }

            if (IsWallCell(cellCenter))
            {
                consecutiveWallCells++;
            }
            else
            {
                break;
            }
        }

        if (consecutiveWallCells < minWalls)
        {
            SpawnDirectionalScanOrBurstVFX(dir, minWalls, succeeded: false);
            Debug.Log($"[PowerUpController] Teleport failed — requires at least {minWalls} wall cell in front (found {consecutiveWallCells}).");
            return false;
        }

        int landingStep = consecutiveWallCells + 1;
        Vector2 destinationCenter = GetCellCenterInFront(playerMovement, dir, landingStep);
        Vector3 destinationPos = new Vector3(destinationCenter.x, destinationCenter.y, playerMovement.transform.position.z);

        // Validate destination: must not be inside a wall, blocked by an obstacle/hazard, or outside level bounds
        if (!IsValidPlayerDestination(destinationPos, ignoreHazards: false) ||
            HasBluntWedgeAtCell(destinationCenter, dir) ||
            !IsInsideLevelBounds(destinationPos))
        {
            SpawnDirectionalScanOrBurstVFX(dir, landingStep, succeeded: false);
            Debug.Log($"[PowerUpController] Teleport failed — destination cell at step {landingStep} ({destinationPos}) is blocked or outside level bounds.");
            return false;
        }

        Vector3 departurePos = GetPlayerCenter(playerMovement);
        playerMovement.TeleportTo(destinationPos);
        SpawnRadialPulseVFX(departurePos, new Color(0.75f, 0.45f, 1f, 0.85f), 1.8f, 0.28f);
        SpawnRadialPulseVFX(destinationPos, new Color(0.35f, 0.95f, 1f, 0.95f), 2.2f, 0.32f);
        Physics2D.SyncTransforms();
        Debug.Log($"[PowerUpController] Teleport succeeded — crossed {consecutiveWallCells} wall cell(s) to {destinationPos}.");
        return true;
    }

    // ─────────────────────────────────────────────────────────────
    //  F. Freeze Implementation (Duration: 4s)
    // ─────────────────────────────────────────────────────────────

    private float GetFreezeDuration()
    {
        if (freezeData == null) return 4f;
        float d = freezeData.GetDuration(CurrentLevel(PowerUpIds.Freeze));
        return d > 0f ? d : 4f;
    }

    private bool ActivateFreeze()
    {
        if (isFreezeActive)
            return false;

        isFreezeActive = true;
        freezeRemainingTime = GetFreezeDuration();
        activeFrozenTargets.Clear();

        FreezeAllEligibleTargetsInLevel();
        if (playerMovement != null)
            SpawnRadialPulseVFX(GetPlayerCenter(playerMovement), new Color(0.35f, 0.92f, 1f, 0.9f), 5.5f, 0.45f);
        Debug.Log($"[PowerUpController] Freeze activated for {freezeRemainingTime:F1}s ({activeFrozenTargets.Count} target(s) frozen).");
        return true;
    }

    private void FreezeAllEligibleTargetsInLevel()
    {
        Transform searchRoot = flowController != null && flowController.CurrentRoot != null
            ? flowController.CurrentRoot.transform
            : null;

        if (searchRoot != null)
        {
            IFreezable[] freezables = searchRoot.GetComponentsInChildren<IFreezable>(includeInactive: false);
            for (int i = 0; i < freezables.Length; i++)
            {
                RegisterAndFreezeIfActive(freezables[i]);
            }
        }
        else
        {
            Hazard[] hazards = FindObjectsByType<Hazard>();
            for (int i = 0; i < hazards.Length; i++)
            {
                RegisterAndFreezeIfActive(hazards[i]);
            }
        }
    }

    /// <summary>
    /// Freezes a newly enabled/spawned <see cref="IFreezable"/> target if Freeze is currently running.
    /// </summary>
    public void RegisterAndFreezeIfActive(IFreezable target)
    {
        if (!isFreezeActive || target == null || !target.SupportsFreeze)
            return;

        if (!activeFrozenTargets.Contains(target))
            activeFrozenTargets.Add(target);

        target.FreezeTarget();
    }

    private void UpdateFreeze(float deltaTime)
    {
        if (!isFreezeActive) return;

        freezeRemainingTime -= deltaTime;
        if (freezeRemainingTime <= 0f)
        {
            EndFreezeEffect();
            Debug.Log("[PowerUpController] Freeze ended — targets restored.");
            OnPowerUpStateChanged?.Invoke();
        }
    }

    private void EndFreezeEffect()
    {
        if (!isFreezeActive && activeFrozenTargets.Count == 0)
            return;

        isFreezeActive = false;
        freezeRemainingTime = 0f;

        for (int i = 0; i < activeFrozenTargets.Count; i++)
        {
            if (activeFrozenTargets[i] != null)
                activeFrozenTargets[i].UnfreezeTarget();
        }
        activeFrozenTargets.Clear();
    }

    // ─────────────────────────────────────────────────────────────
    //  Grid & Spatial Helper Methods
    // ─────────────────────────────────────────────────────────────

    private bool IsGameplayActive()
    {
        if (flowController != null)
            return flowController.CurrentState == LevelState.Playing;

        return true;
    }

    private void UpdateCooldowns(float deltaTime)
    {
        if (deltaTime <= 0f) return;

        bool anyExpired = false;
        PowerUpType[] types = (PowerUpType[])Enum.GetValues(typeof(PowerUpType));
        for (int i = 0; i < types.Length; i++)
        {
            PowerUpType t = types[i];
            if (cooldownRemaining.TryGetValue(t, out float cd) && cd > 0f)
            {
                cd = Mathf.Max(0f, cd - deltaTime);
                cooldownRemaining[t] = cd;
                if (cd <= 0f)
                    anyExpired = true;
            }
        }

        if (anyExpired)
            OnPowerUpStateChanged?.Invoke();
    }

    private static Vector2 GetEstablishedDirection(PlayerMovement player)
    {
        if (player == null) return Vector2.zero;
        Vector2 dir = player.CurrentDirection != Vector2.zero
            ? player.CurrentDirection
            : player.LastMoveDirection;

        if (dir == Vector2.zero)
            return Vector2.zero;

        return Mathf.Abs(dir.x) >= Mathf.Abs(dir.y)
            ? new Vector2(Mathf.Sign(dir.x), 0f)
            : new Vector2(0f, Mathf.Sign(dir.y));
    }

    private static Vector3 GetPlayerCenter(PlayerMovement player)
    {
        if (player == null) return Vector3.zero;
        return player.BodyCollider != null
            ? player.BodyCollider.bounds.center
            : player.transform.position;
    }

    private Vector3 GetValidEchoSpawnPosition(PlayerMovement player)
    {
        Vector3 playerPos = player.transform.position;
        Grid grid = GetActiveGrid();
        if (grid != null)
        {
            Vector3Int cell = grid.WorldToCell(GetPlayerCenter(player));
            Vector3 cellCenter = grid.GetCellCenterWorld(cell);
            cellCenter.z = playerPos.z;
            if (IsValidPlayerDestination(cellCenter, ignoreHazards: false))
                return cellCenter;
        }
        return playerPos;
    }

    private Vector2 GetCellCenterInFront(PlayerMovement player, Vector2 cardinalDir, int cellSteps)
    {
        float cellSize = Mathf.Max(0.1f, player.CellSize);
        Vector3 playerCenter = GetPlayerCenter(player);
        Grid grid = GetActiveGrid();

        if (grid != null)
        {
            // Sample slightly inside the player's cell along -cardinalDir if resting on a boundary
            Vector3 samplePos = playerCenter - (Vector3)(cardinalDir * 0.05f);
            Vector3Int baseCell = grid.WorldToCell(samplePos);
            Vector3Int offset = new Vector3Int(
                Mathf.RoundToInt(cardinalDir.x) * cellSteps,
                Mathf.RoundToInt(cardinalDir.y) * cellSteps,
                0);
            Vector3 worldCenter = grid.GetCellCenterWorld(baseCell + offset);
            return new Vector2(worldCenter.x, worldCenter.y);
        }

        return (Vector2)playerCenter + cardinalDir * (cellSteps * cellSize);
    }

    private bool IsWallCell(Vector2 cellCenter)
    {
        if (playerMovement == null) return false;

        float cellSize = Mathf.Max(0.1f, playerMovement.CellSize);
        Vector2 probeSize = Vector2.one * (cellSize * 0.7f);
        var wallFilter = new ContactFilter2D();
        wallFilter.SetLayerMask(playerMovement.WallLayers);
        wallFilter.useTriggers = false;
        int hitCount = Physics2D.OverlapBox(cellCenter, probeSize, 0f, wallFilter, cellOverlapBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = cellOverlapBuffer[i];
            if (col == null || !col.enabled || col.isTrigger) continue;
            if (playerMovement.BodyCollider != null && col == playerMovement.BodyCollider) continue;
            return true;
        }

        return false;
    }

    private bool HasActiveHazardAtCell(Vector2 cellCenter)
    {
        float cellSize = playerMovement != null ? Mathf.Max(0.1f, playerMovement.CellSize) : 1f;
        Vector2 probeSize = Vector2.one * (cellSize * 0.7f);
        int hitCount = Physics2D.OverlapBox(cellCenter, probeSize, 0f, ContactFilter2D.noFilter, cellOverlapBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = cellOverlapBuffer[i];
            if (col == null || !col.enabled) continue;
            Hazard hazard = col.GetComponentInParent<Hazard>();
            if (hazard != null && hazard.IsActiveHazard)
                return true;
        }

        return false;
    }

    private bool HasBluntWedgeAtCell(Vector2 cellCenter, Vector2 incomingDirection)
    {
        float cellSize = playerMovement != null ? Mathf.Max(0.1f, playerMovement.CellSize) : 1f;
        Vector2 probeSize = Vector2.one * (cellSize * 0.7f);
        int hitCount = Physics2D.OverlapBox(cellCenter, probeSize, 0f, ContactFilter2D.noFilter, cellOverlapBuffer);

        for (int i = 0; i < hitCount; i++)
        {
            Collider2D col = cellOverlapBuffer[i];
            if (col == null || !col.enabled) continue;
            DeflectionWedge wedge = col.GetComponentInParent<DeflectionWedge>();
            if (wedge != null && wedge.GetDeflection(incomingDirection) == Vector2.zero)
                return true;
        }

        return false;
    }

    private bool IsValidPlayerDestination(Vector3 worldPos, bool ignoreHazards)
    {
        Vector2 pos2D = new Vector2(worldPos.x, worldPos.y);
        if (IsWallCell(pos2D))
            return false;

        if (!ignoreHazards && HasActiveHazardAtCell(pos2D))
            return false;

        return true;
    }

    /// <summary>
    /// Verifies that <paramref name="worldPos"/> lies inside the playable level bounds
    /// (not outside the outer perimeter walls of the maze).
    /// </summary>
    private bool IsInsideLevelBounds(Vector3 worldPos)
    {
        LevelRoot root = flowController != null ? flowController.CurrentRoot : null;
        if (root != null)
        {
            // 1. If the layout has a non-wall background/floor Tilemap, verify the cell has a floor tile
            Tilemap[] tilemaps = root.GetComponentsInChildren<Tilemap>(includeInactive: false);
            for (int i = 0; i < tilemaps.Length; i++)
            {
                Tilemap tm = tilemaps[i];
                if (tm == null) continue;
                // Skip wall tilemaps that sit on the player's wallLayers
                if (playerMovement != null && ((1 << tm.gameObject.layer) & playerMovement.WallLayers.value) != 0)
                    continue;

                Vector3Int cell = tm.WorldToCell(worldPos);
                if (!tm.HasTile(cell))
                    return false;
            }
        }

        // 2. Raycast in all 4 cardinal directions against WallLayers:
        // Any valid interior maze cell is bounded by perimeter walls in all 4 directions.
        if (playerMovement != null)
        {
            const float maxProbeDistance = 100f;
            Vector2 origin = new Vector2(worldPos.x, worldPos.y);
            Vector2[] dirs = { Vector2.up, Vector2.down, Vector2.left, Vector2.right };

            for (int d = 0; d < dirs.Length; d++)
            {
                int hitCount = Physics2D.RaycastNonAlloc(origin, dirs[d], boundsRayHits, maxProbeDistance, playerMovement.WallLayers);
                bool hitSolidWall = false;
                for (int i = 0; i < hitCount; i++)
                {
                    Collider2D col = boundsRayHits[i].collider;
                    if (col != null && col.enabled && !col.isTrigger && col != playerMovement.BodyCollider)
                    {
                        hitSolidWall = true;
                        break;
                    }
                }

                if (!hitSolidWall)
                    return false;
            }
        }

        return true;
    }

    private Grid GetActiveGrid()
    {
        if (flowController != null && flowController.CurrentRoot != null && flowController.CurrentRoot.LayoutGrid != null)
            return flowController.CurrentRoot.LayoutGrid;

        return null;
    }

    private Collectible[] GetLevelCollectibles()
    {
        if (flowController != null && flowController.CurrentRoot != null && flowController.CurrentRoot.Collectibles != null)
            return flowController.CurrentRoot.Collectibles;

        return FindObjectsByType<Collectible>();
    }

    // ─────────────────────────────────────────────────────────────
    //  In-World Skill Feedback VFX (EMP Ring & Directional Cell Scan)
    // ─────────────────────────────────────────────────────────────

    private static Sprite cachedRingSprite;
    private static Sprite cachedBoxSprite;

    private static Sprite GetOrCreateRingSprite()
    {
        if (cachedRingSprite != null) return cachedRingSprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                bool isRing = dist >= 11.5f && dist <= 15.2f;
                tex.SetPixel(x, y, isRing ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedRingSprite;
    }

    private static Sprite GetOrCreateBoxSprite()
    {
        if (cachedBoxSprite != null) return cachedBoxSprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isBorder = x < 3 || x >= size - 3 || y < 3 || y >= size - 3;
                Color c = isBorder ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        cachedBoxSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedBoxSprite;
    }

    private void SpawnRadialPulseVFX(Vector3 centerPos, Color color, float targetScale, float duration)
    {
        if (!Application.isPlaying) return;

        var go = new GameObject("SkillPulseVFX");
        go.transform.position = new Vector3(centerPos.x, centerPos.y, 0f);
        go.transform.localScale = Vector3.one * 0.35f;

        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = GetOrCreateRingSprite();
        sr.color = color;
        sr.sortingOrder = 25;

        Sequence seq = DOTween.Sequence()
            .SetUpdate(true)
            .SetLink(go);
        seq.Join(go.transform.DOScale(Vector3.one * targetScale, duration).SetEase(Ease.OutCubic));
        seq.Join(sr.DOFade(0f, duration).SetEase(Ease.InQuad));
        seq.OnComplete(() => Destroy(go));
    }

    private void SpawnDirectionalScanOrBurstVFX(Vector2 dir, int cellCount, bool succeeded)
    {
        if (!Application.isPlaying || playerMovement == null) return;

        Color vfxColor = succeeded
            ? new Color(1f, 0.86f, 0.25f, 0.9f)
            : new Color(1f, 0.28f, 0.32f, 0.85f);

        for (int step = 1; step <= Mathf.Max(1, cellCount); step++)
        {
            Vector2 cellCenter = GetCellCenterInFront(playerMovement, dir, step);
            var go = new GameObject(succeeded ? $"BreakerBurst_{step}" : $"SkillInvalidScan_{step}");
            go.transform.position = new Vector3(cellCenter.x, cellCenter.y, 0f);
            go.transform.localScale = Vector3.one * (succeeded ? 0.5f : 0.85f);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = GetOrCreateBoxSprite();
            sr.color = vfxColor;
            sr.sortingOrder = 25;

            Sequence seq = DOTween.Sequence()
                .SetUpdate(true)
                .SetLink(go);
            if (succeeded)
            {
                seq.Join(go.transform.DOScale(Vector3.one * 1.25f, 0.28f).SetEase(Ease.OutBack));
                seq.Join(sr.DOFade(0f, 0.28f).SetEase(Ease.InQuad));
            }
            else
            {
                seq.Join(go.transform.DOShakePosition(0.24f, new Vector3(0.08f, 0.08f, 0f), 20, 90f, false, true));
                seq.Join(sr.DOFade(0f, 0.28f).SetEase(Ease.InQuad));
            }
            seq.OnComplete(() => Destroy(go));
        }
    }
}
