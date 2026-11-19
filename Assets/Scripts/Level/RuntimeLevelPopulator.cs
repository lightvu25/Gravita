using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public struct RunnerPopulationReport
{
    public int SeedUsed;
    public int RoomNumber;
    public bool RoomEventTriggered;
    public int SpawnNodesProcessed;
    public int BlocksSpawned;
    public int EnemiesSpawned;
    public int EventsSpawned;
    public int PickupsSpawned;
    public int RejectedForSolvability;
}

/// <summary>
/// Deterministic two-tier room populator for Maze Runner rooms.
/// Combines optional authored <see cref="RunnerSpawnNode"/> anchors with valid open floor placement,
/// respects per-room ranges + per-prefab/event chances &amp; counts, configures <see cref="SentinelDronePatrol"/> routes,
/// and verifies slide solvability via <see cref="MazeSlideGraph"/> before keeping blocking obstacles.
/// </summary>
public class RuntimeLevelPopulator : MonoBehaviour
{
    public static RuntimeLevelPopulator Instance { get; private set; }

    [Header("Default Population Config (ScriptableObject)")]
    [SerializeField] private RunnerPopulationProfile defaultProfile;

    public RunnerPopulationProfile DefaultProfile => defaultProfile;

    private struct RuntimePoolCandidate
    {
        public int SourceIndex;
        public RunnerSpawnEntry Entry;
        public int SpawnedCount;
    }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetDefaultProfile(RunnerPopulationProfile profile)
    {
        defaultProfile = profile;
    }

    public RunnerPopulationReport PopulateRoom(
        RunnerRoomController room,
        MazeGrid logicalGrid,
        RunnerPopulationProfile profileOverride,
        int roomNumber,
        int seed)
    {
        var report = new RunnerPopulationReport
        {
            SeedUsed = seed,
            RoomNumber = Mathf.Max(1, roomNumber),
        };

        RunnerPopulationProfile profile = profileOverride != null ? profileOverride : defaultProfile;
        if (room == null || profile == null)
            return report;

        room.EnsureInitialized();
        MazeGrid workGrid = logicalGrid != null ? logicalGrid.Clone() : room.ExtractLogicalGridFromTilemap();
        if (workGrid == null)
            return report;

        Vector2Int originWorld = room.WorldGridOriginOffset;
        int safeRadius = Mathf.Max(1, profile.safeRadiusAroundPortals);
        var rng = new System.Random(seed);

        bool requireExitStop = false;
        MazeSlideGraph baselineGraph = workGrid.Spawn.HasValue
            ? MazeSlideGraph.Build(workGrid, workGrid.Spawn, requireExitOnStopCell: true)
            : null;
        if (baselineGraph != null && baselineGraph.IsSolvable)
        {
            requireExitStop = true;
        }
        else if (workGrid.Spawn.HasValue)
        {
            baselineGraph = MazeSlideGraph.Build(workGrid, workGrid.Spawn, requireExitOnStopCell: false);
        }

        HashSet<Vector2Int> exactPortalCells = BuildPortalRadiusSet(workGrid, radius: 0);
        HashSet<Vector2Int> protectedSafeCells = BuildPortalRadiusSet(workGrid, safeRadius);
        var occupiedCells = new HashSet<Vector2Int>(exactPortalCells);

        // Tier 1 quotas
        int blockQuota = RollCategoryQuota(
            profile.enableBlocks,
            profile.roomBlockTriggerChance,
            profile.roomBlocksRange,
            bonusChance: 0f,
            extraCount: 0,
            rng,
            out _);

        float enemyBonusChance = (report.RoomNumber - 1) * profile.enemyChanceBonusPerRoom;
        int enemyExtraCount = Mathf.Min(
            profile.maxExtraEnemies,
            (report.RoomNumber - 1) / Mathf.Max(1, profile.extraEnemyEveryNRooms));
        int enemyQuota = RollCategoryQuota(
            profile.enableEnemies,
            profile.roomEnemyTriggerChance,
            profile.roomEnemiesRange,
            enemyBonusChance,
            enemyExtraCount,
            rng,
            out _);

        int eventQuota = RollCategoryQuota(
            profile.enableEvents,
            profile.roomEventTriggerChance,
            profile.roomEventsRange,
            bonusChance: 0f,
            extraCount: 0,
            rng,
            out bool eventTriggered);
        report.RoomEventTriggered = eventTriggered;

        int pickupQuota = profile.enablePickups
            ? rng.Next(profile.roomPickupsRange.x, profile.roomPickupsRange.y + 1)
            : 0;

        // Tier 2 rolled pools
        var pools = new Dictionary<RunnerSpawnCategory, List<RuntimePoolCandidate>>
        {
            [RunnerSpawnCategory.Block]  = BuildEligiblePool(profile.blockEntries, report.RoomNumber, rng),
            [RunnerSpawnCategory.Enemy]  = BuildEligiblePool(profile.enemyEntries, report.RoomNumber, rng),
            [RunnerSpawnCategory.Event]  = BuildEligiblePool(profile.eventEntries, report.RoomNumber, rng),
            [RunnerSpawnCategory.Pickup] = BuildEligiblePool(profile.pickupEntries, report.RoomNumber, rng),
        };

        var remainingQuotas = new Dictionary<RunnerSpawnCategory, int>
        {
            [RunnerSpawnCategory.Block]  = blockQuota,
            [RunnerSpawnCategory.Enemy]  = enemyQuota,
            [RunnerSpawnCategory.Event]  = eventQuota,
            [RunnerSpawnCategory.Pickup] = pickupQuota,
        };

        // Pass 1: Process hand-authored RunnerSpawnNode anchors (priority over random floor scattering)
        ProcessAuthoredSpawnNodes(
            room,
            workGrid,
            originWorld,
            profile,
            pools,
            remainingQuotas,
            occupiedCells,
            exactPortalCells,
            requireExitStop,
            rng,
            ref report);

        // Pass 2: Events on open floor cells outside portal safe-zone
        SpawnCategoryOnFloorCells(
            RunnerSpawnCategory.Event,
            room,
            workGrid,
            originWorld,
            profile,
            pools[RunnerSpawnCategory.Event],
            remainingQuotas[RunnerSpawnCategory.Event],
            occupiedCells,
            protectedSafeCells,
            preferTraversedCells: false,
            baselineGraph,
            requireExitStop,
            rng,
            ref report);

        // Pass 3: Blocks on open floor cells with MazeSlideGraph anti-softlock validation
        SpawnCategoryOnFloorCells(
            RunnerSpawnCategory.Block,
            room,
            workGrid,
            originWorld,
            profile,
            pools[RunnerSpawnCategory.Block],
            remainingQuotas[RunnerSpawnCategory.Block],
            occupiedCells,
            protectedSafeCells,
            preferTraversedCells: true,
            baselineGraph,
            requireExitStop,
            rng,
            ref report);

        // Recompute reachable slide graph after blocks so enemies & pickups align with final geometry
        MazeSlideGraph postBlockGraph = workGrid.Spawn.HasValue
            ? MazeSlideGraph.Build(workGrid, workGrid.Spawn, requireExitStop)
            : baselineGraph;

        // Pass 4: Enemies outside portal safe-zone (auto-configuring SentinelDronePatrol corridors)
        SpawnCategoryOnFloorCells(
            RunnerSpawnCategory.Enemy,
            room,
            workGrid,
            originWorld,
            profile,
            pools[RunnerSpawnCategory.Enemy],
            remainingQuotas[RunnerSpawnCategory.Enemy],
            occupiedCells,
            protectedSafeCells,
            preferTraversedCells: false,
            postBlockGraph,
            requireExitStop,
            rng,
            ref report);

        // Pass 5: Pickups along traversed slide cells
        SpawnCategoryOnFloorCells(
            RunnerSpawnCategory.Pickup,
            room,
            workGrid,
            originWorld,
            profile,
            pools[RunnerSpawnCategory.Pickup],
            remainingQuotas[RunnerSpawnCategory.Pickup],
            occupiedCells,
            exactPortalCells,
            preferTraversedCells: true,
            postBlockGraph,
            requireExitStop,
            rng,
            ref report);

        room.RefreshLevelRootCaches();
        return report;
    }

    private static int RollCategoryQuota(
        bool enabled,
        float baseChance,
        Vector2Int range,
        float bonusChance,
        int extraCount,
        System.Random rng,
        out bool triggered)
    {
        triggered = false;
        if (!enabled)
            return 0;

        float effectiveChance = Mathf.Clamp01(baseChance + bonusChance);
        if (rng.NextDouble() > effectiveChance)
            return 0;

        triggered = true;
        int min = Mathf.Max(0, Mathf.Min(range.x, range.y));
        int max = Mathf.Max(min, Mathf.Max(range.x, range.y));
        return rng.Next(min, max + 1) + Mathf.Max(0, extraCount);
    }

    private static List<RuntimePoolCandidate> BuildEligiblePool(
        List<RunnerSpawnEntry> entries,
        int roomNumber,
        System.Random rng)
    {
        var result = new List<RuntimePoolCandidate>();
        if (entries == null)
            return result;

        for (int i = 0; i < entries.Count; i++)
        {
            RunnerSpawnEntry entry = entries[i];
            if (!entry.IsEligibleForRoom(roomNumber))
                continue;

            if (entry.spawnChance < 1f && rng.NextDouble() > entry.spawnChance)
                continue;

            result.Add(new RuntimePoolCandidate
            {
                SourceIndex = i,
                Entry = entry,
                SpawnedCount = 0,
            });
        }

        return result;
    }

    private void ProcessAuthoredSpawnNodes(
        RunnerRoomController room,
        MazeGrid workGrid,
        Vector2Int originWorld,
        RunnerPopulationProfile profile,
        Dictionary<RunnerSpawnCategory, List<RuntimePoolCandidate>> pools,
        Dictionary<RunnerSpawnCategory, int> remainingQuotas,
        HashSet<Vector2Int> occupiedCells,
        HashSet<Vector2Int> exactPortalCells,
        bool requireExitStop,
        System.Random rng,
        ref RunnerPopulationReport report)
    {
        RunnerSpawnNode[] nodes = room.GetComponentsInChildren<RunnerSpawnNode>(true);
        if (nodes == null || nodes.Length == 0)
            return;

        var shuffled = new List<RunnerSpawnNode>(nodes.Length);
        for (int i = 0; i < nodes.Length; i++)
        {
            if (nodes[i] != null && nodes[i].gameObject.activeInHierarchy)
                shuffled.Add(nodes[i]);
        }
        ShuffleList(shuffled, rng);

        for (int i = 0; i < shuffled.Count; i++)
        {
            RunnerSpawnNode node = shuffled[i];
            report.SpawnNodesProcessed++;

            bool forced = node.forceWhenRoomEventTriggers && report.RoomEventTriggered;
            if (!forced && rng.NextDouble() > Mathf.Clamp01(node.activationChance))
                continue;

            Vector2Int cell = new Vector2Int(
                Mathf.FloorToInt(node.transform.position.x) - originWorld.x,
                Mathf.FloorToInt(node.transform.position.y) - originWorld.y);

            if (!workGrid.InBounds(cell) || !workGrid.IsFloor(cell) || exactPortalCells.Contains(cell) || occupiedCells.Contains(cell))
                continue;

            RunnerSpawnCategory chosenCat = ResolveCategoryForNode(node.allowedCategory, remainingQuotas, pools, rng);
            if (chosenCat == RunnerSpawnCategory.Any)
                continue;

            if (node.prefabOverrides != null && node.prefabOverrides.Length > 0)
            {
                GameObject customPrefab = PickNonNullPrefab(node.prefabOverrides, rng);
                if (customPrefab != null && TrySpawnAtCell(
                    customPrefab,
                    chosenCat,
                    blocksSlideTraversal: chosenCat == RunnerSpawnCategory.Block,
                    room,
                    workGrid,
                    originWorld,
                    profile,
                    cell,
                    occupiedCells,
                    requireExitStop,
                    rng,
                    ref report))
                {
                    remainingQuotas[chosenCat] = Mathf.Max(0, remainingQuotas[chosenCat] - 1);
                }
                continue;
            }

            List<RuntimePoolCandidate> pool = pools[chosenCat];
            int idx = PickWeightedCandidateIndex(pool, preferUnderMinimum: true, rng);
            if (idx < 0)
                continue;

            RuntimePoolCandidate candidate = pool[idx];
            if (TrySpawnAtCell(
                candidate.Entry.prefab,
                chosenCat,
                candidate.Entry.blocksSlideTraversal,
                room,
                workGrid,
                originWorld,
                profile,
                cell,
                occupiedCells,
                requireExitStop,
                rng,
                ref report))
            {
                candidate.SpawnedCount++;
                pool[idx] = candidate;
                remainingQuotas[chosenCat] = Mathf.Max(0, remainingQuotas[chosenCat] - 1);
            }
        }
    }

    private void SpawnCategoryOnFloorCells(
        RunnerSpawnCategory category,
        RunnerRoomController room,
        MazeGrid workGrid,
        Vector2Int originWorld,
        RunnerPopulationProfile profile,
        List<RuntimePoolCandidate> pool,
        int countToSpawn,
        HashSet<Vector2Int> occupiedCells,
        HashSet<Vector2Int> forbiddenCells,
        bool preferTraversedCells,
        MazeSlideGraph slideGraph,
        bool requireExitStop,
        System.Random rng,
        ref RunnerPopulationReport report)
    {
        if (countToSpawn <= 0 || pool == null || pool.Count == 0)
            return;

        List<Vector2Int> candidates = CollectCandidateFloorCells(
            workGrid,
            occupiedCells,
            forbiddenCells,
            preferTraversedCells ? slideGraph : null,
            rng);

        int cursor = 0;
        for (int spawned = 0; spawned < countToSpawn && cursor < candidates.Count; )
        {
            int entryIndex = PickWeightedCandidateIndex(pool, preferUnderMinimum: true, rng);
            if (entryIndex < 0)
                break;

            RuntimePoolCandidate candidate = pool[entryIndex];
            Vector2Int cell = candidates[cursor++];

            if (occupiedCells.Contains(cell) || forbiddenCells.Contains(cell) || !workGrid.IsFloor(cell))
                continue;

            if (TrySpawnAtCell(
                candidate.Entry.prefab,
                category,
                candidate.Entry.blocksSlideTraversal,
                room,
                workGrid,
                originWorld,
                profile,
                cell,
                occupiedCells,
                requireExitStop,
                rng,
                ref report))
            {
                candidate.SpawnedCount++;
                pool[entryIndex] = candidate;
                spawned++;
            }
        }
    }

    private bool TrySpawnAtCell(
        GameObject prefab,
        RunnerSpawnCategory category,
        bool blocksSlideTraversal,
        RunnerRoomController room,
        MazeGrid workGrid,
        Vector2Int originWorld,
        RunnerPopulationProfile profile,
        Vector2Int cell,
        HashSet<Vector2Int> occupiedCells,
        bool requireExitStop,
        System.Random rng,
        ref RunnerPopulationReport report)
    {
        if (prefab == null || !workGrid.IsFloor(cell))
            return false;

        if (category == RunnerSpawnCategory.Block && blocksSlideTraversal && profile.enforceSlideSolvability)
        {
            workGrid.SetCell(cell.x, cell.y, MazeCell.Wall);
            bool stillValid = IsSlideGraphValid(workGrid, requireExitStop);
            if (!stillValid)
            {
                workGrid.SetCell(cell.x, cell.y, MazeCell.Floor);
                report.RejectedForSolvability++;
                return false;
            }
        }

        Vector3 worldPos = new Vector3(
            originWorld.x + cell.x + 0.5f,
            originWorld.y + cell.y + 0.5f,
            0f);

        ObjectPoolManager.PoolType poolType = ResolvePoolType(category);
        GameObject instance = room.SpawnTrackedEntity(prefab, worldPos, Quaternion.identity, poolType);
        if (instance == null)
        {
            if (category == RunnerSpawnCategory.Block && blocksSlideTraversal && profile.enforceSlideSolvability)
                workGrid.SetCell(cell.x, cell.y, MazeCell.Floor);
            return false;
        }

        if (category == RunnerSpawnCategory.Enemy)
        {
            SentinelDronePatrol drone = instance.GetComponent<SentinelDronePatrol>();
            if (drone != null)
            {
                Vector2Int[] offsets = ComputeUnobstructedDroneOffsets(workGrid, cell, profile.maxDronePatrolSpan, rng);
                drone.ConfigureRuntimeRoute(worldPos, offsets, DronePatrolMode.PingPong);
            }
        }

        occupiedCells.Add(cell);
        switch (category)
        {
            case RunnerSpawnCategory.Block:  report.BlocksSpawned++; break;
            case RunnerSpawnCategory.Enemy:  report.EnemiesSpawned++; break;
            case RunnerSpawnCategory.Event:  report.EventsSpawned++; break;
            case RunnerSpawnCategory.Pickup: report.PickupsSpawned++; break;
        }
        return true;
    }

    private static bool IsSlideGraphValid(MazeGrid grid, bool requireExitStop)
    {
        if (grid == null || !grid.Spawn.HasValue || !grid.Exit.HasValue)
            return true;

        MazeSlideGraph graph = MazeSlideGraph.Build(grid, grid.Spawn, requireExitStop);
        if (!graph.IsSolvable)
            return false;

        return graph.SoftLockedStops.Count == 0;
    }

    private static Vector2Int[] ComputeUnobstructedDroneOffsets(
        MazeGrid grid,
        Vector2Int originCell,
        int maxSpan,
        System.Random rng)
    {
        Vector2Int[] cardinalAxes =
        {
            Vector2Int.right,
            Vector2Int.left,
            Vector2Int.up,
            Vector2Int.down,
        };

        Vector2Int bestDir = Vector2Int.zero;
        int bestLength = 0;
        int startIndex = rng.Next(0, cardinalAxes.Length);

        for (int i = 0; i < cardinalAxes.Length; i++)
        {
            Vector2Int dir = cardinalAxes[(startIndex + i) % cardinalAxes.Length];
            int clearCells = 0;
            for (int step = 1; step <= maxSpan; step++)
            {
                Vector2Int probe = originCell + dir * step;
                if (!grid.InBounds(probe) || !grid.IsFloor(probe))
                    break;
                clearCells++;
            }

            if (clearCells > bestLength)
            {
                bestLength = clearCells;
                bestDir = dir;
            }
        }

        if (bestLength <= 0 || bestDir == Vector2Int.zero)
            return new[] { Vector2Int.zero };

        return new[] { Vector2Int.zero, bestDir * bestLength };
    }

    private static List<Vector2Int> CollectCandidateFloorCells(
        MazeGrid grid,
        HashSet<Vector2Int> occupiedCells,
        HashSet<Vector2Int> forbiddenCells,
        MazeSlideGraph preferredGraph,
        System.Random rng)
    {
        var preferred = new List<Vector2Int>();
        var secondary = new List<Vector2Int>();

        for (int x = 0; x < grid.Width; x++)
        {
            for (int y = 0; y < grid.Height; y++)
            {
                var c = new Vector2Int(x, y);
                if (!grid.IsFloor(c) || occupiedCells.Contains(c) || forbiddenCells.Contains(c))
                    continue;

                if (preferredGraph != null && preferredGraph.IsCellTraversed(c))
                    preferred.Add(c);
                else
                    secondary.Add(c);
            }
        }

        ShuffleList(preferred, rng);
        ShuffleList(secondary, rng);
        preferred.AddRange(secondary);
        return preferred;
    }

    private static RunnerSpawnCategory ResolveCategoryForNode(
        RunnerSpawnCategory allowed,
        Dictionary<RunnerSpawnCategory, int> remainingQuotas,
        Dictionary<RunnerSpawnCategory, List<RuntimePoolCandidate>> pools,
        System.Random rng)
    {
        if (allowed != RunnerSpawnCategory.Any)
        {
            bool hasQuota = remainingQuotas.TryGetValue(allowed, out int q) && q > 0;
            bool hasPool = pools.TryGetValue(allowed, out List<RuntimePoolCandidate> list) && HasRemainingCapacity(list);
            return (hasQuota && hasPool) ? allowed : RunnerSpawnCategory.Any;
        }

        RunnerSpawnCategory[] order =
        {
            RunnerSpawnCategory.Event,
            RunnerSpawnCategory.Enemy,
            RunnerSpawnCategory.Block,
            RunnerSpawnCategory.Pickup,
        };

        var available = new List<RunnerSpawnCategory>(4);
        for (int i = 0; i < order.Length; i++)
        {
            RunnerSpawnCategory cat = order[i];
            if (remainingQuotas[cat] > 0 && HasRemainingCapacity(pools[cat]))
                available.Add(cat);
        }

        return available.Count > 0 ? available[rng.Next(0, available.Count)] : RunnerSpawnCategory.Any;
    }

    private static bool HasRemainingCapacity(List<RuntimePoolCandidate> pool)
    {
        if (pool == null) return false;
        for (int i = 0; i < pool.Count; i++)
        {
            if (pool[i].SpawnedCount < pool[i].Entry.ClampedMaxCount)
                return true;
        }
        return false;
    }

    private static int PickWeightedCandidateIndex(
        List<RuntimePoolCandidate> pool,
        bool preferUnderMinimum,
        System.Random rng)
    {
        if (pool == null || pool.Count == 0)
            return -1;

        if (preferUnderMinimum)
        {
            float minWeightSum = 0f;
            for (int i = 0; i < pool.Count; i++)
            {
                RuntimePoolCandidate c = pool[i];
                if (c.SpawnedCount < c.Entry.ClampedMinCount && c.SpawnedCount < c.Entry.ClampedMaxCount)
                    minWeightSum += c.Entry.weight;
            }

            if (minWeightSum > 0f)
                return SelectWeightedFromList(pool, minWeightSum, requireUnderMin: true, rng);
        }

        float totalWeight = 0f;
        for (int i = 0; i < pool.Count; i++)
        {
            RuntimePoolCandidate c = pool[i];
            if (c.SpawnedCount < c.Entry.ClampedMaxCount)
                totalWeight += c.Entry.weight;
        }

        if (totalWeight <= 0f)
            return -1;

        return SelectWeightedFromList(pool, totalWeight, requireUnderMin: false, rng);
    }

    private static int SelectWeightedFromList(
        List<RuntimePoolCandidate> pool,
        float totalWeight,
        bool requireUnderMin,
        System.Random rng)
    {
        double roll = rng.NextDouble() * totalWeight;
        float cumulative = 0f;
        int lastValid = -1;

        for (int i = 0; i < pool.Count; i++)
        {
            RuntimePoolCandidate c = pool[i];
            if (c.SpawnedCount >= c.Entry.ClampedMaxCount)
                continue;
            if (requireUnderMin && c.SpawnedCount >= c.Entry.ClampedMinCount)
                continue;

            lastValid = i;
            cumulative += c.Entry.weight;
            if (roll <= cumulative)
                return i;
        }

        return lastValid;
    }

    private static HashSet<Vector2Int> BuildPortalRadiusSet(MazeGrid grid, int radius)
    {
        var set = new HashSet<Vector2Int>();
        void AddAround(Vector2Int? center)
        {
            if (!center.HasValue) return;
            Vector2Int c = center.Value;
            for (int dx = -radius; dx <= radius; dx++)
            {
                for (int dy = -radius; dy <= radius; dy++)
                    set.Add(new Vector2Int(c.x + dx, c.y + dy));
            }
        }

        AddAround(grid.Spawn);
        AddAround(grid.Exit);
        return set;
    }

    private static GameObject PickNonNullPrefab(GameObject[] candidates, System.Random rng)
    {
        var valid = new List<GameObject>(candidates.Length);
        for (int i = 0; i < candidates.Length; i++)
        {
            if (candidates[i] != null)
                valid.Add(candidates[i]);
        }
        return valid.Count > 0 ? valid[rng.Next(0, valid.Count)] : null;
    }

    private static void ShuffleList<T>(List<T> list, System.Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }

    private static ObjectPoolManager.PoolType ResolvePoolType(RunnerSpawnCategory category)
    {
        return category switch
        {
            RunnerSpawnCategory.Enemy  => ObjectPoolManager.PoolType.Enemy,
            RunnerSpawnCategory.Pickup => ObjectPoolManager.PoolType.Loot,
            _                          => ObjectPoolManager.PoolType.GameObject,
        };
    }
}
