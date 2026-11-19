using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime orchestrator for the continuous Tomb-of-the-Mask-style Maze Runner mode.
/// Dynamically generates/selects large rooms, stitches GoalNode -> StartNode corridor throats via
/// <see cref="RuntimeTilemapMerger"/>, seals visited One-Way Airlocks, populates each room via
/// <see cref="RuntimeLevelPopulator"/>, and settles coins/EXP/records atomically at run end.
/// </summary>
public class MazeRunnerService : MonoBehaviour
{
    public static MazeRunnerService Instance { get; private set; }

    private sealed class RoomWindowEntry
    {
        public RunnerRoomController Room;
        public readonly List<Vector3Int> EntryAirlockCells = new List<Vector3Int>(4);
        public readonly List<Vector3Int> IncomingThroatCells = new List<Vector3Int>(8);
        public readonly List<Vector3Int> OutgoingThroatCells = new List<Vector3Int>(8);
        public readonly List<Vector3Int> PermanentWallCells = new List<Vector3Int>(16);
        public bool AirlockSealed;
    }

    [Header("Configuration & Scene Dependencies")]
    [SerializeField] private RunnerModeConfig modeConfig;
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private RuntimeTilemapMerger tilemapMerger;
    [SerializeField] private RuntimeLevelPopulator levelPopulator;
    [SerializeField] private Transform roomsContainer;

    [Header("Debug / Determinism")]
    [Tooltip("Leave 0 for randomized runs; set non-zero to replay a deterministic Runner seed.")]
    [SerializeField] private int debugFixedSeed = 0;

    public event Action<RunnerSessionState> OnRunStarted;
    public event Action<int> OnRoomChanged;
    public event Action<RunnerRunSummary> OnRunEnded;

    public static event Action<RunnerSessionState> OnAnyRunStarted;
    public static event Action<int> OnAnyRoomChanged;
    public static event Action<RunnerRunSummary> OnAnyRunEnded;
    public static event Action<int> OnAnyInRunCoinsChanged;

    private readonly List<RoomWindowEntry> windowEntries = new List<RoomWindowEntry>(4);
    private readonly List<RunnerRoomController> activeRoomsView = new List<RunnerRoomController>(4);
    private readonly HashSet<Vector3Int> carvedFloorCells = new HashSet<Vector3Int>();
    private readonly HashSet<Vector3Int> sealedWallCells = new HashSet<Vector3Int>();
    private readonly List<GameObject> activeRoomObjectsScratch = new List<GameObject>(4);
    private readonly Dictionary<RunnerPortalNode, Action<RunnerPortalNode, PlayerMovement>> portalCallbacks =
        new Dictionary<RunnerPortalNode, Action<RunnerPortalNode, PlayerMovement>>(8);

    private RunnerRoomController currentRoom;
    private RunnerModeKind lastUsedMode = RunnerModeKind.Endless;
    private bool isInThroatTransit;
    private bool coinSubscribed;

    public RunnerModeConfig ModeConfig => modeConfig;
    public RunnerSessionState ActiveSession { get; private set; }
    public bool IsRunActive => ActiveSession != null && ActiveSession.IsActive;
    public bool IsShowingRunnerResult { get; private set; }
    public bool HasLastRunSummary { get; private set; }
    public RunnerRunSummary LastRunSummary { get; private set; }
    public RunnerRoomController CurrentRoom => currentRoom;
    public IReadOnlyList<RunnerRoomController> ActiveRooms => activeRoomsView;
    public IReadOnlyCollection<Vector3Int> CarvedFloorCells => carvedFloorCells;
    public IReadOnlyCollection<Vector3Int> SealedWallCells => sealedWallCells;
    public bool IsInThroatTransit => isInThroatTransit;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        ResolveSceneDependencies();
        EnsureCoinSubscription();
    }

    private void OnDestroy()
    {
        RemoveCoinSubscription();
        CleanupRunWorld();

        if (Instance == this)
            Instance = null;
    }

    public void ConfigureForTesting(
        RunnerModeConfig config,
        LevelFlowController flow,
        RuntimeTilemapMerger merger,
        RuntimeLevelPopulator populator,
        Transform container = null,
        int fixedSeed = 0)
    {
        modeConfig = config;
        flowController = flow;
        tilemapMerger = merger;
        levelPopulator = populator;
        roomsContainer = container;
        debugFixedSeed = fixedSeed;
        EnsureCoinSubscription();
    }

    public bool StartRun(RunnerModeKind? overrideMode = null)
    {
        ResolveSceneDependencies();
        if (modeConfig == null)
        {
            Debug.LogError("[MazeRunnerService] Cannot start Maze Runner — RunnerModeConfig is not assigned.", this);
            return false;
        }

        if (levelPopulator != null && levelPopulator.DefaultProfile == null && modeConfig.defaultPopulationProfile != null)
            levelPopulator.SetDefaultProfile(modeConfig.defaultPopulationProfile);

        CleanupRunWorld();
        IsShowingRunnerResult = false;
        isInThroatTransit = false;

        RunnerModeKind resolvedMode = overrideMode ?? modeConfig.defaultMode;
        lastUsedMode = resolvedMode;
        int masterSeed = ResolveMasterSeed();
        ActiveSession = new RunnerSessionState(resolvedMode, masterSeed);

        int initialWindowTarget = Mathf.Max(1, 1 + modeConfig.lookaheadRooms);
        if (ActiveSession.TargetRoomCount > 0)
            initialWindowTarget = Mathf.Min(initialWindowTarget, ActiveSession.TargetRoomCount);

        for (int roomNumber = 1; roomNumber <= initialWindowTarget; roomNumber++)
        {
            if (!SpawnAndStitchRoom(roomNumber))
            {
                Debug.LogError($"[MazeRunnerService] Failed to build Runner Room {roomNumber}. Aborting run start.", this);
                CleanupRunWorld();
                ActiveSession = null;
                return false;
            }
        }

        if (windowEntries.Count == 0 || windowEntries[0].Room == null)
        {
            CleanupRunWorld();
            ActiveSession = null;
            return false;
        }

        currentRoom = windowEntries[0].Room;
        if (currentRoom.StartNode != null)
            currentRoom.StartNode.MarkConsumed();

        RebuildMergedTilemaps();

        if (flowController != null)
            flowController.StartRunnerMode(this, currentRoom);

        OnRunStarted?.Invoke(ActiveSession);
        OnAnyRunStarted?.Invoke(ActiveSession);
        OnRoomChanged?.Invoke(ActiveSession.CurrentRoomNumber);
        OnAnyRoomChanged?.Invoke(ActiveSession.CurrentRoomNumber);
        OnAnyInRunCoinsChanged?.Invoke(ActiveSession.InRunCoinsPickedUp);
        return true;
    }

    public bool RestartRun()
    {
        return StartRun(lastUsedMode);
    }

    public void AbandonRunToMenu()
    {
        isInThroatTransit = false;
        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(true);

        if (ActiveSession != null && ActiveSession.IsActive)
            ActiveSession.EndRun();
        ActiveSession = null;

        IsShowingRunnerResult = false;
        CleanupRunWorld();

        if (flowController != null)
        {
            flowController.ClearRunnerMode();
            flowController.ReturnToMap();
        }
    }

    public void ClearRunnerResultFlag()
    {
        IsShowingRunnerResult = false;
    }

    public void FinalizeRunOnDeath(string failReason)
    {
        isInThroatTransit = false;
        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(true);

        if (ActiveSession == null)
            return;

        ActiveSession.EndRun();
        RunnerRunSummary summary = SettleRunnerRun(completedTarget: false, string.IsNullOrEmpty(failReason) ? "Hazard" : failReason);
        IsShowingRunnerResult = true;
        HasLastRunSummary = true;
        LastRunSummary = summary;

        OnRunEnded?.Invoke(summary);
        OnAnyRunEnded?.Invoke(summary);
    }

    public void TickActiveRooms(float dt)
    {
        if (!IsRunActive)
            return;

        if (currentRoom != null && currentRoom.LevelRoot != null)
            currentRoom.LevelRoot.TickClock(dt);

        if (isInThroatTransit)
        {
            RunnerRoomController next = GetNextRoomAfter(currentRoom);
            if (next != null && next.LevelRoot != null && next != currentRoom)
                next.LevelRoot.TickClock(dt);
        }
    }

    public void HaltAllActiveRoomHazards()
    {
        if (currentRoom != null && currentRoom.LevelRoot != null)
            currentRoom.LevelRoot.HaltHazardsOnPlayerDeath();

        for (int i = 0; i < activeRoomsView.Count; i++)
        {
            RunnerRoomController room = activeRoomsView[i];
            if (room != null && room.LevelRoot != null && room != currentRoom)
                room.LevelRoot.HaltHazardsOnPlayerDeath();
        }
    }

    public void RestoreAllActiveRoomHazards()
    {
        if (currentRoom != null && currentRoom.LevelRoot != null)
            currentRoom.LevelRoot.RestoreHazardsAfterRevive();

        for (int i = 0; i < activeRoomsView.Count; i++)
        {
            RunnerRoomController room = activeRoomsView[i];
            if (room != null && room.LevelRoot != null && room != currentRoom)
                room.LevelRoot.RestoreHazardsAfterRevive();
        }
    }

    public void CleanupRunWorld()
    {
        isInThroatTransit = false;

        foreach (KeyValuePair<RunnerPortalNode, Action<RunnerPortalNode, PlayerMovement>> pair in portalCallbacks)
        {
            if (pair.Key != null)
                pair.Key.OnPlayerEnteredPortal -= pair.Value;
        }
        portalCallbacks.Clear();

        for (int i = windowEntries.Count - 1; i >= 0; i--)
        {
            DestroyWindowEntry(windowEntries[i]);
        }
        windowEntries.Clear();
        activeRoomsView.Clear();
        carvedFloorCells.Clear();
        sealedWallCells.Clear();
        currentRoom = null;

        if (tilemapMerger != null)
            tilemapMerger.ClearGlobalTilemaps();
    }

    // ─────────────────────────────────────────────────────────────
    //  Room Creation, Portal Selection & Continuous Stitching
    // ─────────────────────────────────────────────────────────────

    private bool SpawnAndStitchRoom(int roomNumber)
    {
        RoomWindowEntry prevEntry = windowEntries.Count > 0 ? windowEntries[windowEntries.Count - 1] : null;
        CardinalDirection requiredEntryDir = prevEntry != null && prevEntry.Room != null
            ? prevEntry.Room.ExitDirection
            : CardinalDirection.Up;

        int roomSeed = ActiveSession != null ? ActiveSession.GetRoomSeed(roomNumber) : ResolveMasterSeed();
        var rng = new System.Random(roomSeed);

        RunnerRoomController room = null;
        MazeGrid logicalGrid = null;
        RunnerPopulationProfile popOverride = null;

        bool preferProcedural = ShouldGenerateProcedural(roomNumber, requiredEntryDir, rng, out RunnerRoomTemplateEntry selectedTemplate);
        if (!preferProcedural && selectedTemplate.roomPrefab != null)
        {
            room = InstantiateRoomPrefab(selectedTemplate.roomPrefab, roomNumber);
            if (room != null)
            {
                logicalGrid = room.ExtractLogicalGridFromTilemap();
                popOverride = selectedTemplate.populationOverride;
            }
        }

        if (room == null)
        {
            if (!TryCreateProceduralRoom(roomNumber, roomSeed, requiredEntryDir, out room, out logicalGrid))
                return false;
        }

        room.InitializeForRoom(roomNumber, logicalGrid);
        var entry = new RoomWindowEntry { Room = room };

        if (prevEntry == null)
        {
            room.transform.position = Vector3.zero;
            SealInitialEntryBackWall(room, requiredEntryDir, entry);
        }
        else
        {
            AlignAndConnectRooms(prevEntry, entry, requiredEntryDir);
        }

        RunnerPopulationProfile effectiveProfile = ResolvePopulationProfile(popOverride);
        if (levelPopulator != null && effectiveProfile != null)
        {
            int popSeed = unchecked(roomSeed * 486187739 + roomNumber * 17);
            levelPopulator.PopulateRoom(room, logicalGrid, effectiveProfile, roomNumber, popSeed);
        }

        room.RefreshLevelRootCaches();
        SubscribeRoomPortals(room);

        windowEntries.Add(entry);
        activeRoomsView.Add(room);
        return true;
    }

    private bool ShouldGenerateProcedural(
        int roomNumber,
        CardinalDirection requiredEntryDir,
        System.Random rng,
        out RunnerRoomTemplateEntry chosen)
    {
        chosen = default;
        if (modeConfig == null || modeConfig.roomSourcePolicy == RunnerRoomSourcePolicy.ProceduralOnly)
            return true;

        var eligible = new List<RunnerRoomTemplateEntry>();
        float totalWeight = 0f;

        if (modeConfig.roomTemplates != null)
        {
            for (int i = 0; i < modeConfig.roomTemplates.Count; i++)
            {
                RunnerRoomTemplateEntry candidate = modeConfig.roomTemplates[i];
                if (!candidate.IsEligible(roomNumber))
                    continue;

                RunnerRoomController prefabRoom = candidate.roomPrefab.GetComponent<RunnerRoomController>();
                if (prefabRoom == null || prefabRoom.StartNode == null || prefabRoom.GoalNode == null)
                    continue;

                if (roomNumber > 1 && prefabRoom.StartNode.PortalDirection != requiredEntryDir)
                    continue;

                eligible.Add(candidate);
                totalWeight += candidate.weight;
            }
        }

        if (eligible.Count == 0)
            return true;

        if (modeConfig.roomSourcePolicy == RunnerRoomSourcePolicy.HybridPreferPoolThenProcedural &&
            rng.NextDouble() < modeConfig.proceduralMixChanceWhenHybrid)
        {
            return true;
        }

        float roll = (float)(rng.NextDouble() * totalWeight);
        float acc = 0f;
        for (int i = 0; i < eligible.Count; i++)
        {
            acc += eligible[i].weight;
            if (roll <= acc)
            {
                chosen = eligible[i];
                return false;
            }
        }

        chosen = eligible[eligible.Count - 1];
        return false;
    }

    private bool TryCreateProceduralRoom(
        int roomNumber,
        int roomSeed,
        CardinalDirection requiredEntryDir,
        out RunnerRoomController room,
        out MazeGrid finalGrid)
    {
        room = null;
        finalGrid = null;

        if (modeConfig.proceduralRoomShellPrefab == null)
        {
            Debug.LogError("[MazeRunnerService] Procedural room shell prefab is not assigned in RunnerModeConfig.", this);
            return false;
        }

        MazeDifficultyProfile difficulty = modeConfig.ResolveDifficultyProfile(roomNumber);
        MazePatternLibrary library = modeConfig.patternLibrary;
        if (difficulty == null || library == null)
        {
            Debug.LogError("[MazeRunnerService] Missing MazeDifficultyProfile or MazePatternLibrary on RunnerModeConfig.", this);
            return false;
        }

        const int maxSeedRetries = 8;
        for (int attempt = 0; attempt < maxSeedRetries; attempt++)
        {
            int attemptSeed = unchecked(roomSeed + attempt * 7919);
            var request = new MazeGenerationRequest
            {
                Seed = attemptSeed,
                Profile = difficulty,
                Library = library,
                OverridePreset = modeConfig.proceduralSizePreset,
                OverrideSlotCols = modeConfig.overrideSlotCols,
                OverrideSlotRows = modeConfig.overrideSlotRows,
            };

            MazeGenerationResult genResult = MazeGenerator.Generate(in request);
            if (!genResult.Success || genResult.Grid == null)
                continue;

            if (TryAssignBorderPortals(genResult.Grid, requiredEntryDir, attemptSeed, out MazeGrid borderGrid, out CardinalDirection exitDir))
            {
                room = InstantiateRoomPrefab(modeConfig.proceduralRoomShellPrefab, roomNumber);
                if (room == null)
                    return false;

                room.StampProceduralGrid(
                    borderGrid,
                    modeConfig.wallRuleTile,
                    modeConfig.backgroundRuleTile,
                    requiredEntryDir,
                    exitDir);
                finalGrid = borderGrid;
                return true;
            }
        }

        // Deterministic safety net: build from seed 0..n using any generated grid and pick best non-overlapping pair
        var fallbackRequest = new MazeGenerationRequest
        {
            Seed = roomSeed,
            Profile = difficulty,
            Library = library,
            OverridePreset = modeConfig.proceduralSizePreset,
            OverrideSlotCols = modeConfig.overrideSlotCols,
            OverrideSlotRows = modeConfig.overrideSlotRows,
        };
        MazeGenerationResult fallback = MazeGenerator.Generate(in fallbackRequest);
        if (fallback.Success && fallback.Grid != null &&
            TryAssignBorderPortalsWithEdgeSearch(fallback.Grid, requiredEntryDir, out MazeGrid emergencyGrid, out CardinalDirection emergencyExitDir))
        {
            room = InstantiateRoomPrefab(modeConfig.proceduralRoomShellPrefab, roomNumber);
            if (room == null)
                return false;

            room.StampProceduralGrid(
                emergencyGrid,
                modeConfig.wallRuleTile,
                modeConfig.backgroundRuleTile,
                requiredEntryDir,
                emergencyExitDir);
            finalGrid = emergencyGrid;
            return true;
        }

        return false;
    }

    private RunnerRoomController InstantiateRoomPrefab(GameObject prefab, int roomNumber)
    {
        if (prefab == null)
            return null;

        Transform parent = roomsContainer != null ? roomsContainer : transform;
        GameObject go = Instantiate(prefab, Vector3.zero, Quaternion.identity, parent);
        go.name = $"RunnerRoom_{roomNumber:D3}_{prefab.name}";

        RunnerRoomController controller = go.GetComponent<RunnerRoomController>();
        if (controller == null)
        {
            Debug.LogError($"[MazeRunnerService] Room prefab '{prefab.name}' lacks RunnerRoomController.", go);
            DestroyRoomGameObject(go);
            return null;
        }

        return controller;
    }

    private static readonly int[] PortraitPreferredRotations = { 0, 2, 1, 3 };

    private bool TryAssignBorderPortals(
        MazeGrid sourceGrid,
        CardinalDirection requiredEntryDir,
        int seed,
        out MazeGrid configuredGrid,
        out CardinalDirection chosenExitDir)
    {
        return TryAssignBorderPortalsInternal(
            sourceGrid,
            requiredEntryDir,
            maxWallDepth: 3,
            checkOriginalSpawnExit: true,
            seed: seed,
            out configuredGrid,
            out chosenExitDir);
    }

    private bool TryAssignBorderPortalsWithEdgeSearch(
        MazeGrid sourceGrid,
        CardinalDirection requiredEntryDir,
        out MazeGrid configuredGrid,
        out CardinalDirection chosenExitDir)
    {
        return TryAssignBorderPortalsInternal(
            sourceGrid,
            requiredEntryDir,
            maxWallDepth: 4,
            checkOriginalSpawnExit: false,
            seed: 0,
            out configuredGrid,
            out chosenExitDir);
    }

    private bool TryAssignBorderPortalsInternal(
        MazeGrid sourceGrid,
        CardinalDirection requiredEntryDir,
        int maxWallDepth,
        bool checkOriginalSpawnExit,
        int seed,
        out MazeGrid configuredGrid,
        out CardinalDirection chosenExitDir)
    {
        configuredGrid = null;
        chosenExitDir = CardinalDirection.Up;
        if (sourceGrid == null)
            return false;

        CardinalDirection entryWallOutward = Opposite(requiredEntryDir);
        CardinalDirection[] exitDirOrder = BuildAllowedExitDirectionOrder(requiredEntryDir);

        for (int dirIdx = 0; dirIdx < exitDirOrder.Length; dirIdx++)
        {
            CardinalDirection candidateExitDir = exitDirOrder[dirIdx];

            for (int r = 0; r < PortraitPreferredRotations.Length; r++)
            {
                int rot = PortraitPreferredRotations[r];
                MazeGrid rotated = rot == 0 ? sourceGrid.Clone() : RotateGrid90CW(sourceGrid, rot);

                if (checkOriginalSpawnExit && rotated.Spawn.HasValue && rotated.Exit.HasValue)
                {
                    Vector2Int sp = rotated.Spawn.Value;
                    Vector2Int ex = rotated.Exit.Value;
                    if (sp != ex &&
                        IsCleanBorderPortal(rotated, sp, entryWallOutward, maxWallDepth) &&
                        IsCleanBorderPortal(rotated, ex, candidateExitDir, maxWallDepth) &&
                        !WouldOverlapActiveRooms(rotated, sp, requiredEntryDir))
                    {
                        configuredGrid = rotated;
                        chosenExitDir = candidateExitDir;
                        return true;
                    }
                }

                if (TryFindCleanBorderPair(rotated, entryWallOutward, candidateExitDir, maxWallDepth, seed + rot, out Vector2Int foundSpawn, out Vector2Int foundExit) &&
                    !WouldOverlapActiveRooms(rotated, foundSpawn, requiredEntryDir))
                {
                    rotated.SetSpawn(foundSpawn);
                    rotated.SetExit(foundExit);
                    configuredGrid = rotated;
                    chosenExitDir = candidateExitDir;
                    return true;
                }
            }
        }

        return false;
    }

    private static bool TryFindCleanBorderPair(
        MazeGrid grid,
        CardinalDirection entryOutwardDir,
        CardinalDirection exitOutwardDir,
        int maxWallDepth,
        int seed,
        out Vector2Int chosenSpawn,
        out Vector2Int chosenExit)
    {
        chosenSpawn = default;
        chosenExit = default;

        var entryCandidates = new List<Vector2Int>(32);
        for (int x = 1; x < grid.Width - 1; x++)
        {
            for (int y = 1; y < grid.Height - 1; y++)
            {
                var cell = new Vector2Int(x, y);
                if (grid.IsFloor(cell) && IsCleanBorderPortal(grid, cell, entryOutwardDir, maxWallDepth))
                    entryCandidates.Add(cell);
            }
        }

        if (entryCandidates.Count == 0)
            return false;

        // Prioritize wall-adjacent stop cells near center for balanced flow
        int startIndex = Mathf.Abs(seed) % entryCandidates.Count;
        var bfsDepth = new Dictionary<Vector2Int, int>(64);
        var bestScore = int.MinValue;
        bool found = false;

        int maxCheck = Mathf.Min(entryCandidates.Count, 10);
        for (int i = 0; i < maxCheck; i++)
        {
            Vector2Int sp = entryCandidates[(startIndex + i) % entryCandidates.Count];
            List<Vector2Int> safeExits = MazeSlideGraph.GetZeroSoftLockExitCandidates(grid, sp, out MazeSlideGraph graph);
            if (safeExits.Count == 0)
                continue;

            ComputeShortestStopDepths(sp, graph, bfsDepth);

            for (int e = 0; e < safeExits.Count; e++)
            {
                Vector2Int ex = safeExits[e];
                if (ex == sp || !IsCleanBorderPortal(grid, ex, exitOutwardDir, maxWallDepth))
                    continue;

                if (!bfsDepth.TryGetValue(ex, out int moves) || moves < 3)
                    moves = 1;

                int span = Mathf.Abs(ex.x - sp.x) + Mathf.Abs(ex.y - sp.y);
                int score = moves * 10 + span;
                if (!found || score > bestScore)
                {
                    bestScore = score;
                    chosenSpawn = sp;
                    chosenExit = ex;
                    found = true;
                }
            }
        }

        return found;
    }

    private static bool IsCleanBorderPortal(
        MazeGrid grid,
        Vector2Int floorCell,
        CardinalDirection outwardDir,
        int maxWallDepth)
    {
        if (!grid.InBounds(floorCell) || !grid.IsFloor(floorCell))
            return false;

        Vector2Int step = ToCellStep(outwardDir);
        Vector2Int perp = new Vector2Int(-step.y, step.x);
        int depth = 0;
        Vector2Int cur = floorCell + step;

        while (grid.InBounds(cur))
        {
            if (!grid.IsWall(cur))
                return false;

            Vector2Int flankA = cur + perp;
            Vector2Int flankB = cur - perp;
            if ((grid.InBounds(flankA) && !grid.IsWall(flankA)) ||
                (grid.InBounds(flankB) && !grid.IsWall(flankB)))
            {
                return false;
            }

            depth++;
            cur += step;
        }

        return depth >= 1 && depth <= maxWallDepth;
    }

    private static CardinalDirection[] BuildAllowedExitDirectionOrder(CardinalDirection requiredEntryDir)
    {
        if (requiredEntryDir == CardinalDirection.Up)
            return new[] { CardinalDirection.Up, CardinalDirection.Right, CardinalDirection.Left };

        if (requiredEntryDir == CardinalDirection.Right)
            return new[] { CardinalDirection.Up, CardinalDirection.Right };

        if (requiredEntryDir == CardinalDirection.Left)
            return new[] { CardinalDirection.Up, CardinalDirection.Left };

        return new[] { CardinalDirection.Right, CardinalDirection.Left, CardinalDirection.Up };
    }

    private bool WouldOverlapActiveRooms(MazeGrid candidateGrid, Vector2Int candidateSpawn, CardinalDirection entryDir)
    {
        if (windowEntries.Count == 0)
            return false;

        RoomWindowEntry prevEntry = windowEntries[windowEntries.Count - 1];
        if (prevEntry.Room == null)
            return false;

        Vector2Int step = ToCellStep(entryDir);
        Vector3Int prevGoal3 = prevEntry.Room.WorldGoalCell;
        var prevGoal = new Vector2Int(prevGoal3.x, prevGoal3.y);
        Vector2Int prevExitBorder = FindLastRoomCellAlongDirection(prevEntry.Room, prevGoal, step);

        int gap = Mathf.Max(2, modeConfig != null ? modeConfig.throatLengthCells : 2);
        Vector2Int targetEntryBorder = prevExitBorder + step * (gap + 1);
        Vector2Int localEntryBorder = FindLastGridCellAlongDirection(candidateGrid, candidateSpawn, -step);
        Vector2Int worldOrigin = targetEntryBorder - localEntryBorder;

        var candidateRect = new RectInt(worldOrigin.x, worldOrigin.y, candidateGrid.Width, candidateGrid.Height);
        for (int i = 0; i < windowEntries.Count; i++)
        {
            RunnerRoomController existing = windowEntries[i].Room;
            if (existing == null) continue;
            if (RectsOverlap(candidateRect, existing.WorldCellBounds))
                return true;
        }

        return false;
    }

    private void SealInitialEntryBackWall(
        RunnerRoomController room,
        CardinalDirection entryDir,
        RoomWindowEntry entry)
    {
        Vector2Int step = ToCellStep(entryDir);
        Vector2Int startCell = new Vector2Int(room.WorldStartCell.x, room.WorldStartCell.y);
        Vector2Int cur = startCell - step;

        while (room.ContainsWorldCell(cur))
        {
            var wallCell = new Vector3Int(cur.x, cur.y, 0);
            carvedFloorCells.Remove(wallCell);
            sealedWallCells.Add(wallCell);
            entry.PermanentWallCells.Add(wallCell);
            cur -= step;
        }
    }

    private void AlignAndConnectRooms(
        RoomWindowEntry prevEntry,
        RoomWindowEntry nextEntry,
        CardinalDirection transitDir)
    {
        RunnerRoomController prevRoom = prevEntry.Room;
        RunnerRoomController nextRoom = nextEntry.Room;

        Vector2Int step = ToCellStep(transitDir);
        Vector2Int perp = new Vector2Int(-step.y, step.x);

        Vector2Int prevGoal = new Vector2Int(prevRoom.WorldGoalCell.x, prevRoom.WorldGoalCell.y);
        Vector2Int prevExitBorder = FindLastRoomCellAlongDirection(prevRoom, prevGoal, step);

        // 1. Align nextRoom so its entry outer border meets the far side of the throat gap
        nextRoom.transform.position = Vector3.zero;
        Vector2Int rawStart = new Vector2Int(nextRoom.WorldStartCell.x, nextRoom.WorldStartCell.y);
        Vector2Int rawEntryBorder = FindLastRoomCellAlongDirection(nextRoom, rawStart, -step);

        int throatGap = Mathf.Max(2, modeConfig != null ? modeConfig.throatLengthCells : 2);
        Vector2Int targetEntryBorder = prevExitBorder + step * (throatGap + 1);
        Vector2Int delta = targetEntryBorder - rawEntryBorder;
        nextRoom.transform.position = new Vector3(delta.x, delta.y, 0f);

        Vector2Int alignedStart = new Vector2Int(nextRoom.WorldStartCell.x, nextRoom.WorldStartCell.y);

        // 2. Carve exit border of prevRoom
        Vector2Int walk = prevGoal + step;
        while (prevRoom.ContainsWorldCell(walk))
        {
            AddThroatFloorCell(walk, perp, prevEntry.OutgoingThroatCells, nextEntry.PermanentWallCells);
            walk += step;
        }

        // 3. Carve external corridor throat + flank side walls
        for (int k = 1; k <= throatGap; k++)
        {
            Vector2Int gapCell = prevExitBorder + step * k;
            AddThroatFloorCell(gapCell, perp, prevEntry.OutgoingThroatCells, nextEntry.PermanentWallCells);
        }

        // 4. Carve nextRoom's entry border into StartNode (recorded for One-Way Airlock seal upon arrival)
        Vector2Int entryWalk = targetEntryBorder;
        while (entryWalk != alignedStart)
        {
            var floorCell3 = new Vector3Int(entryWalk.x, entryWalk.y, 0);
            carvedFloorCells.Add(floorCell3);
            nextEntry.IncomingThroatCells.Add(floorCell3);
            nextEntry.EntryAirlockCells.Add(floorCell3);
            AddFlankWalls(entryWalk, perp, nextEntry.PermanentWallCells);
            entryWalk += step;
        }
    }

    private void AddThroatFloorCell(
        Vector2Int cell,
        Vector2Int perp,
        List<Vector3Int> floorRecord,
        List<Vector3Int> wallRecord)
    {
        var floorCell3 = new Vector3Int(cell.x, cell.y, 0);
        carvedFloorCells.Add(floorCell3);
        floorRecord.Add(floorCell3);
        AddFlankWalls(cell, perp, wallRecord);
    }

    private void AddFlankWalls(Vector2Int centerCell, Vector2Int perp, List<Vector3Int> wallRecord)
    {
        Vector3Int flankA = new Vector3Int(centerCell.x + perp.x, centerCell.y + perp.y, 0);
        Vector3Int flankB = new Vector3Int(centerCell.x - perp.x, centerCell.y - perp.y, 0);

        if (!carvedFloorCells.Contains(flankA))
        {
            sealedWallCells.Add(flankA);
            wallRecord.Add(flankA);
        }

        if (!carvedFloorCells.Contains(flankB))
        {
            sealedWallCells.Add(flankB);
            wallRecord.Add(flankB);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Portal Event Flow & Sliding Window Maintenance
    // ─────────────────────────────────────────────────────────────

    private void SubscribeRoomPortals(RunnerRoomController room)
    {
        if (room == null) return;

        SubscribeSinglePortal(room, room.StartNode);
        SubscribeSinglePortal(room, room.GoalNode);
    }

    private void SubscribeSinglePortal(RunnerRoomController room, RunnerPortalNode portal)
    {
        if (portal == null || portalCallbacks.ContainsKey(portal))
            return;

        Action<RunnerPortalNode, PlayerMovement> cb = (node, player) => HandlePortalEntered(room, node, player);
        portalCallbacks[portal] = cb;
        portal.OnPlayerEnteredPortal += cb;
    }

    private void HandlePortalEntered(RunnerRoomController room, RunnerPortalNode portal, PlayerMovement player)
    {
        if (!IsRunActive || room == null || portal == null)
            return;

        if (portal.Role == RunnerPortalRole.Goal && room == currentRoom)
        {
            if (ActiveSession.TargetRoomCount > 0 && room.RoomNumber >= ActiveSession.TargetRoomCount)
            {
                ActiveSession.RecordRoomCleared();
                if (player != null)
                    player.Freeze();
                if (GameInput.Instance != null)
                    GameInput.Instance.SetInputsEnabled(true);

                RunnerRunSummary summary = SettleRunnerRun(completedTarget: true, "Target Cleared");
                IsShowingRunnerResult = true;
                HasLastRunSummary = true;
                LastRunSummary = summary;

                flowController?.CompleteRunnerMode(room.GoalWorldPosition);
                OnRunEnded?.Invoke(summary);
                OnAnyRunEnded?.Invoke(summary);
                return;
            }

            isInThroatTransit = true;
            if (GameInput.Instance != null)
                GameInput.Instance.SetInputsEnabled(false);

            if (player != null)
            {
                Vector2 exitVec = CardinalDirectionUtil.ToVector2(room.ExitDirection);
                if (Vector2.Dot(player.CurrentDirection, exitVec) <= 0.9f ||
                    Vector2.Distance(player.transform.position, room.GoalWorldPosition) > 0.05f)
                {
                    player.Redirect(exitVec, room.GoalWorldPosition);
                }
            }
            return;
        }

        RunnerRoomController expectedNext = GetNextRoomAfter(currentRoom);
        if (portal.Role == RunnerPortalRole.Start && expectedNext != null && room == expectedNext)
        {
            isInThroatTransit = false;
            if (GameInput.Instance != null)
                GameInput.Instance.SetInputsEnabled(true);

            if (player != null)
                player.HaltAt(room.StartWorldPosition, requireFreshInput: false);

            SealRoomEntryAirlock(room);

            ActiveSession.RecordRoomCleared();
            currentRoom = room;
            flowController?.UpdateActiveRunnerRoom(currentRoom);

            MaintainRoomWindow();
            RebuildMergedTilemaps();

            OnRoomChanged?.Invoke(ActiveSession.CurrentRoomNumber);
            OnAnyRoomChanged?.Invoke(ActiveSession.CurrentRoomNumber);
        }
    }

    private void SealRoomEntryAirlock(RunnerRoomController arrivedRoom)
    {
        RoomWindowEntry entry = FindWindowEntry(arrivedRoom);
        if (entry == null || entry.AirlockSealed)
            return;

        entry.AirlockSealed = true;
        for (int i = 0; i < entry.EntryAirlockCells.Count; i++)
        {
            Vector3Int cell = entry.EntryAirlockCells[i];
            carvedFloorCells.Remove(cell);
            sealedWallCells.Add(cell);
        }
    }

    private void MaintainRoomWindow()
    {
        if (ActiveSession == null || currentRoom == null || modeConfig == null)
            return;

        int maxLookaheadRoom = currentRoom.RoomNumber + modeConfig.lookaheadRooms;
        if (ActiveSession.TargetRoomCount > 0)
            maxLookaheadRoom = Mathf.Min(maxLookaheadRoom, ActiveSession.TargetRoomCount);

        int highestSpawned = windowEntries.Count > 0 ? windowEntries[windowEntries.Count - 1].Room.RoomNumber : currentRoom.RoomNumber;
        for (int r = highestSpawned + 1; r <= maxLookaheadRoom; r++)
        {
            SpawnAndStitchRoom(r);
        }

        int minRetainedRoom = currentRoom.RoomNumber - modeConfig.retainedPreviousRooms;
        for (int i = windowEntries.Count - 1; i >= 0; i--)
        {
            RoomWindowEntry entry = windowEntries[i];
            if (entry.Room == null || entry.Room.RoomNumber < minRetainedRoom)
            {
                DestroyWindowEntry(entry);
                windowEntries.RemoveAt(i);
            }
        }

        activeRoomsView.Clear();
        for (int i = 0; i < windowEntries.Count; i++)
        {
            if (windowEntries[i].Room != null)
                activeRoomsView.Add(windowEntries[i].Room);
        }
    }

    private void DestroyWindowEntry(RoomWindowEntry entry)
    {
        if (entry == null)
            return;

        for (int i = 0; i < entry.IncomingThroatCells.Count; i++)
            carvedFloorCells.Remove(entry.IncomingThroatCells[i]);
        for (int i = 0; i < entry.OutgoingThroatCells.Count; i++)
            carvedFloorCells.Remove(entry.OutgoingThroatCells[i]);
        for (int i = 0; i < entry.EntryAirlockCells.Count; i++)
            sealedWallCells.Remove(entry.EntryAirlockCells[i]);
        for (int i = 0; i < entry.PermanentWallCells.Count; i++)
            sealedWallCells.Remove(entry.PermanentWallCells[i]);

        if (entry.Room == null)
            return;

        UnsubscribePortal(entry.Room.StartNode);
        UnsubscribePortal(entry.Room.GoalNode);
        entry.Room.ReleaseRoomEntities();
        DestroyRoomGameObject(entry.Room.gameObject);
    }

    private void UnsubscribePortal(RunnerPortalNode portal)
    {
        if (portal != null && portalCallbacks.TryGetValue(portal, out Action<RunnerPortalNode, PlayerMovement> cb))
        {
            portal.OnPlayerEnteredPortal -= cb;
            portalCallbacks.Remove(portal);
        }
    }

    private void RebuildMergedTilemaps()
    {
        if (tilemapMerger == null)
            return;

        activeRoomObjectsScratch.Clear();
        for (int i = 0; i < windowEntries.Count; i++)
        {
            if (windowEntries[i].Room != null)
                activeRoomObjectsScratch.Add(windowEntries[i].Room.gameObject);
        }

        if (activeRoomObjectsScratch.Count == 0)
            return;

        TileBase overrideWall = modeConfig != null ? modeConfig.wallRuleTile : null;
        TileBase overrideBg = modeConfig != null ? modeConfig.backgroundRuleTile : null;
        tilemapMerger.MergeActiveRoomsSynchronous(
            activeRoomObjectsScratch,
            carvedFloorCells,
            sealedWallCells,
            overrideWall,
            overrideBg);
    }

    // ─────────────────────────────────────────────────────────────
    //  Economy & Coin Tracking
    // ─────────────────────────────────────────────────────────────

    private void EnsureCoinSubscription()
    {
        if (coinSubscribed)
            return;
        coinSubscribed = true;
        Collectible.OnAnyCoinCollected -= HandleInRunCoinCollected;
        Collectible.OnAnyCoinCollected += HandleInRunCoinCollected;
    }

    private void RemoveCoinSubscription()
    {
        if (!coinSubscribed)
            return;
        coinSubscribed = false;
        Collectible.OnAnyCoinCollected -= HandleInRunCoinCollected;
    }

    private void HandleInRunCoinCollected(Collectible coin)
    {
        if (!IsRunActive)
            return;

        int amount = coin != null ? Mathf.Max(1, coin.Value) : 1;
        ActiveSession.AddInRunCoins(amount);
        OnAnyInRunCoinsChanged?.Invoke(ActiveSession.InRunCoinsPickedUp);
    }

    private RunnerRunSummary SettleRunnerRun(bool completedTarget, string endReason)
    {
        RunnerModeKind mode = ActiveSession.Mode;
        int cleared = ActiveSession.ClearedRooms;
        int pickedCoins = ActiveSession.InRunCoinsPickedUp;
        bool grantsImmediatePickup = EconomyBalanceConfig.Active.GrantWalletCoinsFromInLevelPickups;
        int unsettledPickedCoins = grantsImmediatePickup ? 0 : pickedCoins;

        int payoutCoins = unsettledPickedCoins;
        int totalExp = 0;
        if (modeConfig != null)
        {
            modeConfig.CalculateCompletionRewards(mode, cleared, unsettledPickedCoins, completedTarget, out payoutCoins, out totalExp);
        }

        int totalCoinsSummary = grantsImmediatePickup ? payoutCoins + pickedCoins : payoutCoins;
        var payout = new RewardBundle();
        if (payoutCoins > 0)
            payout.Add(RewardType.Coins, payoutCoins);

        bool isNewBest = false;
        int bestRecord = cleared;

        var tx = new RewardTransaction
        {
            RequireUniqueRewardId = false,
            RejectIfSkillCapExceeded = false,
            Payout = payout,
            Reason = $"MazeRunner_{mode}",
            OnBeforeSaveMutateProfile = p =>
            {
                isNewBest = p.RecordRunnerRun(mode, cleared, completedTarget);
                bestRecord = p.GetBestRunnerRooms(mode);
            }
        };

        RewardTransactionResult result = RewardService.ExecuteTransaction(tx);
        if (!result.Succeeded)
            Debug.LogWarning($"[MazeRunnerService] Atomic run settlement warning ({result.Status}): {result.DiagnosticMessage}", this);

        if (totalExp > 0 && ProgressionManager.Instance != null)
            ProgressionManager.Instance.AddExp(totalExp, "MazeRunner");

        return new RunnerRunSummary(
            mode: mode,
            clearedRooms: cleared,
            targetRooms: ActiveSession.TargetRoomCount,
            inRunCoinsPickedUp: pickedCoins,
            totalCoinsGranted: totalCoinsSummary,
            totalExpGranted: totalExp,
            bestRoomsRecord: bestRecord,
            isNewBest: isNewBest,
            completedTarget: completedTarget,
            endReason: endReason);
    }

    // ─────────────────────────────────────────────────────────────
    //  Utilities
    // ─────────────────────────────────────────────────────────────

    private void ResolveSceneDependencies()
    {
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();
        if (tilemapMerger == null)
            tilemapMerger = FindAnyObjectByType<RuntimeTilemapMerger>();
        if (levelPopulator == null)
            levelPopulator = RuntimeLevelPopulator.Instance != null
                ? RuntimeLevelPopulator.Instance
                : FindAnyObjectByType<RuntimeLevelPopulator>();
    }

    private RunnerPopulationProfile ResolvePopulationProfile(RunnerPopulationProfile roomOverride)
    {
        if (roomOverride != null)
            return roomOverride;
        if (modeConfig != null && modeConfig.defaultPopulationProfile != null)
            return modeConfig.defaultPopulationProfile;
        return levelPopulator != null ? levelPopulator.DefaultProfile : null;
    }

    private int ResolveMasterSeed()
    {
        if (debugFixedSeed != 0)
            return debugFixedSeed & 0x7FFFFFFF;

        unchecked
        {
            int raw = Environment.TickCount ^ Guid.NewGuid().GetHashCode();
            return (raw & 0x7FFFFFFF) == 0 ? 1 : (raw & 0x7FFFFFFF);
        }
    }

    private RunnerRoomController GetNextRoomAfter(RunnerRoomController room)
    {
        if (room == null)
            return null;

        for (int i = 0; i < windowEntries.Count - 1; i++)
        {
            if (windowEntries[i].Room == room)
                return windowEntries[i + 1].Room;
        }
        return null;
    }

    private RoomWindowEntry FindWindowEntry(RunnerRoomController room)
    {
        for (int i = 0; i < windowEntries.Count; i++)
        {
            if (windowEntries[i].Room == room)
                return windowEntries[i];
        }
        return null;
    }

    private static Vector2Int FindLastRoomCellAlongDirection(RunnerRoomController room, Vector2Int start, Vector2Int step)
    {
        Vector2Int last = start;
        Vector2Int cur = start + step;
        while (room.ContainsWorldCell(cur))
        {
            last = cur;
            cur += step;
        }
        return last;
    }

    private static Vector2Int FindLastGridCellAlongDirection(MazeGrid grid, Vector2Int start, Vector2Int step)
    {
        Vector2Int last = start;
        Vector2Int cur = start + step;
        while (grid.InBounds(cur))
        {
            last = cur;
            cur += step;
        }
        return last;
    }

    private static bool RectsOverlap(RectInt a, RectInt b)
    {
        return a.xMin < b.xMax && a.xMax > b.xMin && a.yMin < b.yMax && a.yMax > b.yMin;
    }

    private static void ComputeShortestStopDepths(
        Vector2Int spawn,
        MazeSlideGraph graph,
        Dictionary<Vector2Int, int> depthMap)
    {
        depthMap.Clear();
        var bfs = new Queue<Vector2Int>(graph.ReachableStops.Count + 4);
        depthMap[spawn] = 0;
        bfs.Enqueue(spawn);

        while (bfs.Count > 0)
        {
            Vector2Int cur = bfs.Dequeue();
            int nextDepth = depthMap[cur] + 1;
            if (!graph.OutgoingByStop.TryGetValue(cur, out List<MazeSlideEdge> outEdges))
                continue;

            for (int i = 0; i < outEdges.Count; i++)
            {
                Vector2Int nextStop = outEdges[i].To;
                if (!depthMap.ContainsKey(nextStop))
                {
                    depthMap[nextStop] = nextDepth;
                    bfs.Enqueue(nextStop);
                }
            }
        }
    }

    private static MazeGrid RotateGrid90CW(MazeGrid src, int quarterTurnsCW)
    {
        int turns = ((quarterTurnsCW % 4) + 4) % 4;
        MazeGrid current = src.Clone();

        for (int t = 0; t < turns; t++)
        {
            int w = current.Width;
            int h = current.Height;
            var rotated = new MazeGrid(h, w, MazeCell.Wall);

            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                    rotated.SetCell(y, w - 1 - x, current.GetCell(x, y));
            }

            if (current.Spawn.HasValue)
            {
                Vector2Int s = current.Spawn.Value;
                rotated.SetSpawn(new Vector2Int(s.y, w - 1 - s.x));
            }

            if (current.Exit.HasValue)
            {
                Vector2Int e = current.Exit.Value;
                rotated.SetExit(new Vector2Int(e.y, w - 1 - e.x));
            }

            current = rotated;
        }

        return current;
    }

    private static Vector2Int ToCellStep(CardinalDirection dir)
    {
        return dir switch
        {
            CardinalDirection.Right => Vector2Int.right,
            CardinalDirection.Down  => Vector2Int.down,
            CardinalDirection.Left  => Vector2Int.left,
            _                       => Vector2Int.up,
        };
    }

    private static CardinalDirection Opposite(CardinalDirection dir)
    {
        return dir switch
        {
            CardinalDirection.Up    => CardinalDirection.Down,
            CardinalDirection.Right => CardinalDirection.Left,
            CardinalDirection.Down  => CardinalDirection.Up,
            _                       => CardinalDirection.Right,
        };
    }

    private static void DestroyRoomGameObject(GameObject go)
    {
        if (go == null)
            return;

        if (Application.isPlaying)
            Destroy(go);
        else
            DestroyImmediate(go);
    }
}
