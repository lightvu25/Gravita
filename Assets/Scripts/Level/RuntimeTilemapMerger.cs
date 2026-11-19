using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Merges room/level Wall and Background tilemaps at runtime and applies corridor-safe exterior BFS
/// auto-fill plus outer padding without allocating runtime GameObjects.
/// </summary>
public class RuntimeTilemapMerger : MonoBehaviour
{
    public static RuntimeTilemapMerger Instance { get; private set; }

    public event Action OnMergeComplete;
    public bool IsMerging { get; private set; }

    [Header("Global Tilemaps")]
    public Tilemap globalWallTilemap;
    public Tilemap globalBackgroundTilemap;
    [SerializeField] private Tilemap globalGroundColliderTilemap;

    [Header("Auto-Fill Gaps")]
    public TileBase fillGroundRuleTile;
    public TileBase fillBackgroundRuleTile;
    [SerializeField] private TileBase fillGroundColliderTile;
    public int fillPadding = 15;

    private struct CollectedMergeData
    {
        public Dictionary<Vector3Int, TileBase> WallVisualTiles;
        public Dictionary<Vector3Int, TileBase> WallColliderTiles;
        public Dictionary<Vector3Int, TileBase> BackgroundTiles;
        public HashSet<Vector3Int> ProtectedMarkerCells;
        public HashSet<GameObject> LocalTilemapObjects;
        public TileBase FirstWallTile;
        public TileBase FirstBackgroundTile;
    }

    private void Awake()
    {
        Instance = this;
    }

    /// <summary>
    /// Synchronously processes a loaded campaign level layout: merges into global tilemaps when
    /// assigned, otherwise auto-fills the level's local Wall and Background tilemaps in-place.
    /// </summary>
    public void ProcessLevel(GameObject levelInstance)
    {
        if (levelInstance == null)
            return;

        if (globalWallTilemap == null || globalBackgroundTilemap == null)
        {
            AutoFillLocalLevelTilemaps(levelInstance, fillGroundRuleTile, fillBackgroundRuleTile, fillPadding);
            OnMergeComplete?.Invoke();
            return;
        }

        List<GameObject> singleLevel = new List<GameObject>(1) { levelInstance };
        bool useSeparatedGround = ShouldUseSeparatedGround(singleLevel);
        ConfigureGlobalGroundPhysics(useSeparatedGround);
        SyncGlobalRendererSettings(singleLevel);

        SimulationMode2D previousSimulationMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        IsMerging = true;
        bool mergeSucceeded = false;

        try
        {
            CollectedMergeData data = CreateCollectedData();
            CollectRoomTiles(levelInstance, useSeparatedGround, ref data);
            CommitMergeData(data, useSeparatedGround, singleLevel.Count);
            mergeSucceeded = true;
        }
        catch (Exception exception)
        {
            RollbackGlobalTilemaps();
            Debug.LogException(exception, this);
            Debug.LogError("[RuntimeTilemapMerger] Synchronous level merge failed. Local tilemaps left active.");
        }
        finally
        {
            IsMerging = false;
            Physics2D.simulationMode = previousSimulationMode;
        }

        if (mergeSucceeded)
            OnMergeComplete?.Invoke();
    }

    /// <summary>
    /// Synchronously merges a window of stitched Runner rooms, carving connecting portal throats
    /// (<paramref name="carvedFloorCells"/>) and sealing visited one-way airlocks (<paramref name="sealedWallCells"/>).
    /// Supports both full global tilemaps and partial/local fallback scenes cleanly.
    /// </summary>
    public void MergeActiveRoomsSynchronous(
        IReadOnlyList<GameObject> activeRooms,
        IReadOnlyCollection<Vector3Int> carvedFloorCells = null,
        IReadOnlyCollection<Vector3Int> sealedWallCells = null,
        TileBase overrideWallTile = null,
        TileBase overrideBackgroundTile = null)
    {
        if (activeRooms == null || activeRooms.Count == 0)
            return;

        if (overrideWallTile != null && fillGroundRuleTile == null)
            fillGroundRuleTile = overrideWallTile;
        if (overrideBackgroundTile != null && fillBackgroundRuleTile == null)
            fillBackgroundRuleTile = overrideBackgroundTile;

        if (globalWallTilemap == null)
        {
            ApplyStitchedMutationsToLocalRooms(activeRooms, carvedFloorCells, sealedWallCells, overrideWallTile, overrideBackgroundTile);
            Physics2D.SyncTransforms();
            OnMergeComplete?.Invoke();
            return;
        }

        var roomList = new List<GameObject>(activeRooms.Count);
        for (int i = 0; i < activeRooms.Count; i++)
        {
            if (activeRooms[i] != null)
                roomList.Add(activeRooms[i]);
        }
        if (roomList.Count == 0)
            return;

        bool hasGlobalBackground = globalBackgroundTilemap != null;
        bool useSeparatedGround = ShouldUseSeparatedGround(roomList);
        ConfigureGlobalGroundPhysics(useSeparatedGround);
        SyncGlobalRendererSettings(roomList);

        SimulationMode2D previousSimulationMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        IsMerging = true;
        bool mergeSucceeded = false;

        try
        {
            CollectedMergeData data = CreateCollectedData();
            for (int i = 0; i < roomList.Count; i++)
                CollectRoomTilesAllowingMissingGlobalBg(roomList[i], useSeparatedGround, hasGlobalBackground, ref data);

            ApplyPortalCarvingAndAirlock(ref data, carvedFloorCells, sealedWallCells, useSeparatedGround, overrideWallTile, overrideBackgroundTile);
            CommitRunnerMergeData(data, useSeparatedGround, hasGlobalBackground, carvedFloorCells, roomList);
            mergeSucceeded = true;
        }
        catch (Exception exception)
        {
            Debug.LogException(exception, this);
            Debug.LogError("[RuntimeTilemapMerger] Synchronous stitched room merge failed.");
        }
        finally
        {
            IsMerging = false;
            Physics2D.simulationMode = previousSimulationMode;
        }

        if (mergeSucceeded)
        {
            Physics2D.SyncTransforms();
            OnMergeComplete?.Invoke();
        }
    }

    public void ClearGlobalTilemaps()
    {
        if (globalWallTilemap != null)
            globalWallTilemap.ClearAllTiles();

        if (globalBackgroundTilemap != null)
            globalBackgroundTilemap.ClearAllTiles();

        if (globalGroundColliderTilemap != null)
            globalGroundColliderTilemap.ClearAllTiles();
    }

    /// <summary>
    /// Corridor-safe in-place auto-fill for a single level instance when global tilemaps are not wired.
    /// </summary>
    public static void AutoFillLocalLevelTilemaps(
        GameObject levelRootObject,
        TileBase fallbackWallTile = null,
        TileBase fallbackBackgroundTile = null,
        int padding = 15)
    {
        if (levelRootObject == null)
            return;

        FindLocalTilemaps(levelRootObject, out Tilemap wallTilemap, out Tilemap colliderTilemap, out Tilemap backgroundTilemap);
        if (wallTilemap == null && backgroundTilemap == null)
            return;

        Tilemap referenceTilemap = wallTilemap != null ? wallTilemap : backgroundTilemap;
        HashSet<Vector3Int> markerCells = CollectMarkerCells(levelRootObject, referenceTilemap);

        TileBase wallFill = fallbackWallTile != null ? fallbackWallTile : FindFirstNonNullTile(wallTilemap);
        TileBase bgFill = fallbackBackgroundTile != null ? fallbackBackgroundTile : FindFirstNonNullTile(backgroundTilemap);

        ApplyCorridorSafeAutoFill(
            wallTilemap,
            backgroundTilemap,
            colliderTilemap,
            wallFill,
            wallFill,
            bgFill,
            markerCells,
            padding);

        if (wallTilemap != null)
        {
            wallTilemap.RefreshAllTiles();
            RefreshTilemapPhysics(wallTilemap);
        }

        if (colliderTilemap != null && colliderTilemap != wallTilemap)
        {
            colliderTilemap.RefreshAllTiles();
            RefreshTilemapPhysics(colliderTilemap);
        }

        if (backgroundTilemap != null)
            backgroundTilemap.RefreshAllTiles();
    }

    public void MergeAllRooms(List<GameObject> spawnedRooms)
    {
        if (IsMerging)
        {
            Debug.LogWarning("[RuntimeTilemapMerger] A merge is already in progress.");
            return;
        }

        if (spawnedRooms == null || globalWallTilemap == null || globalBackgroundTilemap == null)
        {
            Debug.LogError("[RuntimeTilemapMerger] Merge aborted: spawned rooms and both global tilemaps are required.");
            return;
        }

        IsMerging = true;
        bool useSeparatedGround = ShouldUseSeparatedGround(spawnedRooms);
        ConfigureGlobalGroundPhysics(useSeparatedGround);
        SyncGlobalRendererSettings(spawnedRooms);

        SimulationMode2D previousSimulationMode = Physics2D.simulationMode;
        Physics2D.simulationMode = SimulationMode2D.Script;
        StartCoroutine(MergeRoutine(spawnedRooms, previousSimulationMode, useSeparatedGround));
    }

    private IEnumerator MergeRoutine(
        List<GameObject> spawnedRooms,
        SimulationMode2D previousSimulationMode,
        bool useSeparatedGround)
    {
        CollectedMergeData data = CreateCollectedData();
        bool mergeSucceeded = false;
        Exception mergeException = null;

        try
        {
            foreach (GameObject roomObj in spawnedRooms)
            {
                try
                {
                    if (roomObj != null)
                        CollectRoomTiles(roomObj, useSeparatedGround, ref data);
                }
                catch (Exception exception)
                {
                    mergeException = exception;
                    break;
                }

                yield return null;
            }

            if (mergeException == null)
            {
                try
                {
                    CommitMergeData(data, useSeparatedGround, spawnedRooms.Count);
                    mergeSucceeded = true;
                }
                catch (Exception exception)
                {
                    mergeException = exception;
                }
            }

            if (mergeException != null)
            {
                RollbackGlobalTilemaps();
                foreach (GameObject tilemapObject in data.LocalTilemapObjects)
                {
                    if (tilemapObject != null) tilemapObject.SetActive(true);
                }
                Debug.LogException(mergeException, this);
                Debug.LogError("[RuntimeTilemapMerger] Merge failed. Original room tilemaps were left visible.");
            }
        }
        finally
        {
            IsMerging = false;
            Physics2D.simulationMode = previousSimulationMode;
        }

        if (mergeSucceeded) OnMergeComplete?.Invoke();
    }

    private bool ShouldUseSeparatedGround(List<GameObject> rooms)
    {
        bool allHaveCollider = AllRoomsHaveSeparatedGround(rooms);
        if (!allHaveCollider)
            return false;

        if (globalGroundColliderTilemap == null || globalGroundColliderTilemap == globalWallTilemap)
        {
            Debug.LogWarning(
                "[RuntimeTilemapMerger] Separated Wall collider tilemaps detected, but globalGroundColliderTilemap " +
                "is not assigned in Inspector. Falling back to combined visual/physics mode without spawning objects.");
            return false;
        }

        return true;
    }

    private static CollectedMergeData CreateCollectedData()
    {
        return new CollectedMergeData
        {
            WallVisualTiles = new Dictionary<Vector3Int, TileBase>(),
            WallColliderTiles = new Dictionary<Vector3Int, TileBase>(),
            BackgroundTiles = new Dictionary<Vector3Int, TileBase>(),
            ProtectedMarkerCells = new HashSet<Vector3Int>(),
            LocalTilemapObjects = new HashSet<GameObject>(),
            FirstWallTile = null,
            FirstBackgroundTile = null
        };
    }

    private void CommitMergeData(CollectedMergeData data, bool useSeparatedGround, int roomCount)
    {
        CommitRunnerMergeData(data, useSeparatedGround, hasGlobalBackground: true, carvedFloorCells: null, roomList: null);

        string mode = useSeparatedGround ? "separated visual/physics" : "combined visual/physics";
        Debug.Log(
            $"[RuntimeTilemapMerger] Merge complete ({mode}): " +
            $"{data.WallVisualTiles.Count} wall tiles, " +
            $"{data.WallColliderTiles.Count} collider tiles, " +
            $"and {data.BackgroundTiles.Count} background tiles from {roomCount} room(s).");
    }

    private void RollbackGlobalTilemaps()
    {
        ClearGlobalTilemaps();
    }

    private static void ValidateCollectedTiles(CollectedMergeData data, bool useSeparatedGround)
    {
        if (data.WallVisualTiles.Count == 0)
            throw new InvalidOperationException("No visible Wall tiles were collected from the level/rooms.");

        if (data.BackgroundTiles.Count == 0 && data.ProtectedMarkerCells.Count == 0)
            throw new InvalidOperationException("No Background tiles or marker footprint were collected.");

        if (useSeparatedGround && data.WallColliderTiles.Count == 0)
            throw new InvalidOperationException("Separated mode selected, but no Wall collider tiles were collected.");
    }

    private void CollectRoomTiles(GameObject roomObj, bool useSeparatedGround, ref CollectedMergeData data)
    {
        CollectRoomTilesInternal(roomObj, useSeparatedGround, collectBackgroundIntoGlobal: true, ref data);
    }

    private void CollectRoomTilesAllowingMissingGlobalBg(
        GameObject roomObj,
        bool useSeparatedGround,
        bool hasGlobalBackground,
        ref CollectedMergeData data)
    {
        CollectRoomTilesInternal(roomObj, useSeparatedGround, hasGlobalBackground, ref data);
    }

    private void CollectRoomTilesInternal(
        GameObject roomObj,
        bool useSeparatedGround,
        bool collectBackgroundIntoGlobal,
        ref CollectedMergeData data)
    {
        ResolveLayerIds(out int wallLayer, out int groundLayer, out int backgroundLayer);
        Tilemap[] localTilemaps = roomObj.GetComponentsInChildren<Tilemap>(true);
        Vector3Int zZero = Vector3Int.zero;

        foreach (Tilemap localTilemap in localTilemaps)
        {
            ClassifyTilemap(localTilemap, wallLayer, groundLayer, backgroundLayer, useSeparatedGround,
                out bool isWallVisual, out bool isWallCollider, out bool isBackground);

            if (!isWallVisual && !isWallCollider && !isBackground)
                continue;

            bool collectedGlobalLayer = false;

            foreach (Vector3Int localCell in localTilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = localTilemap.GetTile(localCell);
                if (tile == null) continue;

                Vector3 worldPosition = localTilemap.GetCellCenterWorld(localCell);

                if (isWallVisual)
                {
                    Vector3Int wallCell = globalWallTilemap.WorldToCell(worldPosition);
                    wallCell.z = 0;
                    data.WallVisualTiles[wallCell] = tile;
                    if (data.FirstWallTile == null) data.FirstWallTile = tile;
                    collectedGlobalLayer = true;
                }

                if (useSeparatedGround && isWallCollider && globalGroundColliderTilemap != null)
                {
                    Vector3Int colliderCell = globalGroundColliderTilemap.WorldToCell(worldPosition);
                    colliderCell.z = 0;
                    data.WallColliderTiles[colliderCell] = tile;
                    collectedGlobalLayer = true;
                }

                if (isBackground)
                {
                    if (data.FirstBackgroundTile == null) data.FirstBackgroundTile = tile;

                    if (collectBackgroundIntoGlobal && globalBackgroundTilemap != null)
                    {
                        Vector3Int bgCell = globalBackgroundTilemap.WorldToCell(worldPosition);
                        bgCell.z = 0;
                        data.BackgroundTiles[bgCell] = tile;
                        collectedGlobalLayer = true;
                    }
                    else
                    {
                        zZero = globalWallTilemap.WorldToCell(worldPosition);
                        zZero.z = 0;
                        data.ProtectedMarkerCells.Add(zZero);
                    }
                }
            }

            if (collectedGlobalLayer)
                data.LocalTilemapObjects.Add(localTilemap.gameObject);
        }

        foreach (Vector3Int markerCell in CollectMarkerCells(roomObj, globalWallTilemap))
            data.ProtectedMarkerCells.Add(markerCell);
    }

    private void ApplyPortalCarvingAndAirlock(
        ref CollectedMergeData data,
        IReadOnlyCollection<Vector3Int> carvedFloorCells,
        IReadOnlyCollection<Vector3Int> sealedWallCells,
        bool useSeparatedGround,
        TileBase overrideWallTile,
        TileBase overrideBackgroundTile)
    {
        TileBase bgFill = ResolveTile(overrideBackgroundTile, fillBackgroundRuleTile, data.FirstBackgroundTile);

        if (carvedFloorCells != null)
        {
            foreach (Vector3Int cell in carvedFloorCells)
            {
                Vector3Int c = new Vector3Int(cell.x, cell.y, 0);
                data.WallVisualTiles.Remove(c);
                data.WallColliderTiles.Remove(c);
                data.ProtectedMarkerCells.Add(c);

                if (globalBackgroundTilemap != null && bgFill != null && !data.BackgroundTiles.ContainsKey(c))
                    data.BackgroundTiles[c] = bgFill;
            }
        }

        if (sealedWallCells != null)
        {
            TileBase wallFill = ResolveTile(overrideWallTile, fillGroundRuleTile, data.FirstWallTile);
            TileBase colliderFill = fillGroundColliderTile != null ? fillGroundColliderTile : wallFill;

            foreach (Vector3Int cell in sealedWallCells)
            {
                Vector3Int c = new Vector3Int(cell.x, cell.y, 0);
                if (wallFill != null)
                    data.WallVisualTiles[c] = wallFill;

                if (useSeparatedGround && colliderFill != null)
                    data.WallColliderTiles[c] = colliderFill;

                if (globalBackgroundTilemap != null && bgFill != null && !data.BackgroundTiles.ContainsKey(c))
                    data.BackgroundTiles[c] = bgFill;
            }
        }
    }

    private void CommitRunnerMergeData(
        CollectedMergeData data,
        bool useSeparatedGround,
        bool hasGlobalBackground,
        IReadOnlyCollection<Vector3Int> carvedFloorCells,
        List<GameObject> roomList)
    {
        ValidateCollectedTiles(data, useSeparatedGround);

        globalWallTilemap.ClearAllTiles();
        if (hasGlobalBackground && globalBackgroundTilemap != null)
            globalBackgroundTilemap.ClearAllTiles();
        if (globalGroundColliderTilemap != null)
            globalGroundColliderTilemap.ClearAllTiles();

        ApplyTiles(globalWallTilemap, data.WallVisualTiles);
        if (hasGlobalBackground && globalBackgroundTilemap != null)
            ApplyTiles(globalBackgroundTilemap, data.BackgroundTiles);
        if (useSeparatedGround && globalGroundColliderTilemap != null)
            ApplyTiles(globalGroundColliderTilemap, data.WallColliderTiles);

        TileBase resolvedWallFill = ResolveTile(fillGroundRuleTile, data.FirstWallTile, null);
        TileBase resolvedColliderFill = fillGroundColliderTile != null ? fillGroundColliderTile : resolvedWallFill;
        TileBase resolvedBgFill = ResolveTile(fillBackgroundRuleTile, data.FirstBackgroundTile, null);

        ApplyCorridorSafeAutoFill(
            globalWallTilemap,
            hasGlobalBackground ? globalBackgroundTilemap : null,
            useSeparatedGround ? globalGroundColliderTilemap : null,
            resolvedWallFill,
            resolvedColliderFill,
            resolvedBgFill,
            data.ProtectedMarkerCells,
            fillPadding);

        if (!hasGlobalBackground && carvedFloorCells != null && carvedFloorCells.Count > 0 && resolvedBgFill != null)
            PaintCarvedBackgroundOntoLocalRooms(roomList, carvedFloorCells, resolvedBgFill);

        globalWallTilemap.RefreshAllTiles();
        if (hasGlobalBackground && globalBackgroundTilemap != null)
            globalBackgroundTilemap.RefreshAllTiles();

        Tilemap physicsTilemap = useSeparatedGround && globalGroundColliderTilemap != null
            ? globalGroundColliderTilemap
            : globalWallTilemap;
        if (physicsTilemap != null)
        {
            if (physicsTilemap != globalWallTilemap)
                physicsTilemap.RefreshAllTiles();
            RefreshTilemapPhysics(physicsTilemap);
        }

        foreach (GameObject tilemapObject in data.LocalTilemapObjects)
        {
            if (tilemapObject != null) tilemapObject.SetActive(false);
        }
    }

    private void ApplyStitchedMutationsToLocalRooms(
        IReadOnlyList<GameObject> activeRooms,
        IReadOnlyCollection<Vector3Int> carvedFloorCells,
        IReadOnlyCollection<Vector3Int> sealedWallCells,
        TileBase overrideWallTile,
        TileBase overrideBackgroundTile)
    {
        for (int i = 0; i < activeRooms.Count; i++)
        {
            GameObject roomObj = activeRooms[i];
            if (roomObj == null) continue;

            FindLocalTilemaps(roomObj, out Tilemap wallTilemap, out Tilemap colliderTilemap, out Tilemap backgroundTilemap);
            if (wallTilemap == null) continue;

            wallTilemap.CompressBounds();
            BoundsInt localBounds = wallTilemap.cellBounds;
            TileBase wallTile = ResolveTile(overrideWallTile, fillGroundRuleTile, FindFirstNonNullTile(wallTilemap));
            TileBase colliderTile = fillGroundColliderTile != null ? fillGroundColliderTile : wallTile;
            TileBase bgTile = ResolveTile(overrideBackgroundTile, fillBackgroundRuleTile, FindFirstNonNullTile(backgroundTilemap));

            if (carvedFloorCells != null)
            {
                foreach (Vector3Int cell in carvedFloorCells)
                {
                    Vector3 worldCenter = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                    Vector3Int localCell = wallTilemap.WorldToCell(worldCenter);
                    localCell.z = 0;
                    if (!wallTilemap.HasTile(localCell)) continue;

                    wallTilemap.SetTile(localCell, null);
                    if (colliderTilemap != null)
                    {
                        Vector3Int localCol = colliderTilemap.WorldToCell(worldCenter);
                        localCol.z = 0;
                        colliderTilemap.SetTile(localCol, null);
                    }

                    if (backgroundTilemap != null && bgTile != null)
                    {
                        Vector3Int localBg = backgroundTilemap.WorldToCell(worldCenter);
                        localBg.z = 0;
                        if (!backgroundTilemap.HasTile(localBg))
                            backgroundTilemap.SetTile(localBg, bgTile);
                    }
                }
            }

            if (sealedWallCells != null && wallTile != null)
            {
                foreach (Vector3Int cell in sealedWallCells)
                {
                    Vector3 worldCenter = new Vector3(cell.x + 0.5f, cell.y + 0.5f, 0f);
                    Vector3Int localCell = wallTilemap.WorldToCell(worldCenter);
                    localCell.z = 0;
                    if (!IsCellNearBounds(localCell, localBounds, 2)) continue;

                    wallTilemap.SetTile(localCell, wallTile);
                    if (colliderTilemap != null && colliderTile != null)
                    {
                        Vector3Int localCol = colliderTilemap.WorldToCell(worldCenter);
                        localCol.z = 0;
                        colliderTilemap.SetTile(localCol, colliderTile);
                    }

                    if (backgroundTilemap != null && bgTile != null)
                    {
                        Vector3Int localBg = backgroundTilemap.WorldToCell(worldCenter);
                        localBg.z = 0;
                        if (!backgroundTilemap.HasTile(localBg))
                            backgroundTilemap.SetTile(localBg, bgTile);
                    }
                }
            }

            wallTilemap.RefreshAllTiles();
            RefreshTilemapPhysics(colliderTilemap != null ? colliderTilemap : wallTilemap);
            if (backgroundTilemap != null) backgroundTilemap.RefreshAllTiles();
        }
    }

    private void PaintCarvedBackgroundOntoLocalRooms(
        List<GameObject> roomList,
        IReadOnlyCollection<Vector3Int> carvedFloorCells,
        TileBase bgTile)
    {
        Tilemap fallbackBg = null;
        for (int i = 0; i < roomList.Count; i++)
        {
            if (roomList[i] == null) continue;
            FindLocalTilemaps(roomList[i], out Tilemap _, out Tilemap _, out Tilemap bgTm);
            if (bgTm != null)
            {
                fallbackBg = bgTm;
                break;
            }
        }

        if (fallbackBg == null) return;

        foreach (Vector3Int cell in carvedFloorCells)
        {
            Vector3 worldCenter = globalWallTilemap.GetCellCenterWorld(new Vector3Int(cell.x, cell.y, 0));
            Vector3Int localCell = fallbackBg.WorldToCell(worldCenter);
            localCell.z = 0;
            if (!fallbackBg.HasTile(localCell))
                fallbackBg.SetTile(localCell, bgTile);
        }

        fallbackBg.RefreshAllTiles();
    }

    private static bool IsCellNearBounds(Vector3Int cell, BoundsInt bounds, int margin)
    {
        return cell.x >= bounds.xMin - margin && cell.x < bounds.xMax + margin &&
               cell.y >= bounds.yMin - margin && cell.y < bounds.yMax + margin;
    }

    private static TileBase ResolveTile(TileBase primary, TileBase secondary, TileBase fallback)
    {
        if (primary != null) return primary;
        if (secondary != null) return secondary;
        return fallback;
    }

    private static void ApplyCorridorSafeAutoFill(
        Tilemap wallTilemap,
        Tilemap backgroundTilemap,
        Tilemap colliderTilemap,
        TileBase wallFillTile,
        TileBase colliderFillTile,
        TileBase bgFillTile,
        HashSet<Vector3Int> markerCells,
        int padding)
    {
        HashSet<Vector3Int> wallCells = new HashSet<Vector3Int>();
        HashSet<Vector3Int> existingBgCells = new HashSet<Vector3Int>();
        bool hasCoreBounds = false;
        int coreMinX = 0, coreMaxX = 0, coreMinY = 0, coreMaxY = 0;

        void EncapsulateCell(Vector3Int cell)
        {
            if (!hasCoreBounds)
            {
                hasCoreBounds = true;
                coreMinX = coreMaxX = cell.x;
                coreMinY = coreMaxY = cell.y;
                return;
            }

            if (cell.x < coreMinX) coreMinX = cell.x;
            if (cell.x > coreMaxX) coreMaxX = cell.x;
            if (cell.y < coreMinY) coreMinY = cell.y;
            if (cell.y > coreMaxY) coreMaxY = cell.y;
        }

        if (wallTilemap != null)
        {
            foreach (Vector3Int p in wallTilemap.cellBounds.allPositionsWithin)
            {
                if (!wallTilemap.HasTile(p)) continue;
                Vector3Int c = new Vector3Int(p.x, p.y, 0);
                wallCells.Add(c);
                EncapsulateCell(c);
            }
        }

        if (backgroundTilemap != null)
        {
            foreach (Vector3Int p in backgroundTilemap.cellBounds.allPositionsWithin)
            {
                if (!backgroundTilemap.HasTile(p)) continue;
                Vector3Int c = new Vector3Int(p.x, p.y, 0);
                existingBgCells.Add(c);
                EncapsulateCell(c);
            }
        }

        if (markerCells != null)
        {
            foreach (Vector3Int m in markerCells)
                EncapsulateCell(new Vector3Int(m.x, m.y, 0));
        }

        if (!hasCoreBounds)
            return;

        int safePad = Mathf.Max(0, padding);
        int bfsPad = Mathf.Max(1, safePad);
        int bfsMinX = coreMinX - bfsPad;
        int bfsMaxX = coreMaxX + bfsPad;
        int bfsMinY = coreMinY - bfsPad;
        int bfsMaxY = coreMaxY + bfsPad;

        // 1. Protect any explicit non-wall floor/marker cells (never overwrite corridors with walls)
        HashSet<Vector3Int> protectedFloorCells = new HashSet<Vector3Int>();
        foreach (Vector3Int bgCell in existingBgCells)
        {
            if (!wallCells.Contains(bgCell))
                protectedFloorCells.Add(bgCell);
        }

        if (markerCells != null)
        {
            foreach (Vector3Int m in markerCells)
            {
                Vector3Int c = new Vector3Int(m.x, m.y, 0);
                if (!wallCells.Contains(c))
                    protectedFloorCells.Add(c);
            }
        }

        // 2. Flood-fill from the outer perimeter inward: enclosed non-wall cells cannot reach perimeter,
        //    so even missing-background interior maze pockets remain protected as traversable corridors.
        HashSet<Vector3Int> exteriorReachable = FloodFillExterior(bfsMinX, bfsMaxX, bfsMinY, bfsMaxY, wallCells);
        for (int x = bfsMinX; x <= bfsMaxX; x++)
        {
            for (int y = bfsMinY; y <= bfsMaxY; y++)
            {
                Vector3Int c = new Vector3Int(x, y, 0);
                if (!wallCells.Contains(c) && !exteriorReachable.Contains(c))
                    protectedFloorCells.Add(c);
            }
        }

        int fillMinX = coreMinX - safePad;
        int fillMaxX = coreMaxX + safePad;
        int fillMinY = coreMinY - safePad;
        int fillMaxY = coreMaxY + safePad;

        List<Vector3Int> wallFillPositions = wallTilemap != null && wallFillTile != null ? new List<Vector3Int>() : null;
        List<Vector3Int> bgFillPositions = backgroundTilemap != null && bgFillTile != null ? new List<Vector3Int>() : null;

        for (int x = fillMinX; x <= fillMaxX; x++)
        {
            for (int y = fillMinY; y <= fillMaxY; y++)
            {
                Vector3Int c = new Vector3Int(x, y, 0);
                if (wallFillPositions != null && !wallCells.Contains(c) && !protectedFloorCells.Contains(c))
                    wallFillPositions.Add(c);

                if (bgFillPositions != null && !existingBgCells.Contains(c))
                    bgFillPositions.Add(c);
            }
        }

        if (wallFillPositions != null && wallFillPositions.Count > 0)
        {
            Vector3Int[] posArray = wallFillPositions.ToArray();
            TileBase[] tiles = new TileBase[posArray.Length];
            Array.Fill(tiles, wallFillTile);
            wallTilemap.SetTiles(posArray, tiles);

            if (colliderTilemap != null && colliderTilemap != wallTilemap && colliderFillTile != null)
            {
                TileBase[] colTiles = new TileBase[posArray.Length];
                Array.Fill(colTiles, colliderFillTile);
                colliderTilemap.SetTiles(posArray, colTiles);
            }
        }

        if (bgFillPositions != null && bgFillPositions.Count > 0)
        {
            Vector3Int[] bgArray = bgFillPositions.ToArray();
            TileBase[] bgTiles = new TileBase[bgArray.Length];
            Array.Fill(bgTiles, bgFillTile);
            backgroundTilemap.SetTiles(bgArray, bgTiles);
        }
    }

    private static HashSet<Vector3Int> FloodFillExterior(
        int minX,
        int maxX,
        int minY,
        int maxY,
        HashSet<Vector3Int> wallCells)
    {
        HashSet<Vector3Int> visited = new HashSet<Vector3Int>();
        Queue<Vector3Int> queue = new Queue<Vector3Int>();

        void TrySeed(int x, int y)
        {
            Vector3Int p = new Vector3Int(x, y, 0);
            if (!wallCells.Contains(p) && visited.Add(p))
                queue.Enqueue(p);
        }

        for (int x = minX; x <= maxX; x++)
        {
            TrySeed(x, minY);
            TrySeed(x, maxY);
        }

        for (int y = minY + 1; y < maxY; y++)
        {
            TrySeed(minX, y);
            TrySeed(maxX, y);
        }

        void TryEnqueueNeighbor(int nx, int ny)
        {
            if (nx < minX || nx > maxX || ny < minY || ny > maxY)
                return;

            var next = new Vector3Int(nx, ny, 0);
            if (!wallCells.Contains(next) && visited.Add(next))
                queue.Enqueue(next);
        }

        while (queue.Count > 0)
        {
            Vector3Int cur = queue.Dequeue();
            TryEnqueueNeighbor(cur.x + 1, cur.y);
            TryEnqueueNeighbor(cur.x - 1, cur.y);
            TryEnqueueNeighbor(cur.x, cur.y + 1);
            TryEnqueueNeighbor(cur.x, cur.y - 1);
        }

        return visited;
    }

    private static HashSet<Vector3Int> CollectMarkerCells(GameObject rootObject, Tilemap referenceTilemap)
    {
        HashSet<Vector3Int> markers = new HashSet<Vector3Int>();
        if (rootObject == null || referenceTilemap == null)
            return markers;

        void AddMarker(Vector3 worldPos)
        {
            Vector3Int c = referenceTilemap.WorldToCell(worldPos);
            c.z = 0;
            markers.Add(c);
        }

        foreach (PlayerSpawnPoint sp in rootObject.GetComponentsInChildren<PlayerSpawnPoint>(true))
            if (sp != null) AddMarker(sp.transform.position);
        foreach (LevelExit exit in rootObject.GetComponentsInChildren<LevelExit>(true))
            if (exit != null) AddMarker(exit.transform.position);
        foreach (Collectible col in rootObject.GetComponentsInChildren<Collectible>(true))
            if (col != null) AddMarker(col.transform.position);
        foreach (Hazard haz in rootObject.GetComponentsInChildren<Hazard>(true))
            if (haz != null && haz.gameObject.activeInHierarchy) AddMarker(haz.transform.position);

        return markers;
    }

    private static void FindLocalTilemaps(
        GameObject rootObject,
        out Tilemap wallTilemap,
        out Tilemap colliderTilemap,
        out Tilemap backgroundTilemap)
    {
        wallTilemap = null;
        colliderTilemap = null;
        backgroundTilemap = null;

        ResolveLayerIds(out int wallLayer, out int groundLayer, out int backgroundLayer);
        Tilemap[] tilemaps = rootObject.GetComponentsInChildren<Tilemap>(true);

        foreach (Tilemap tm in tilemaps)
        {
            ClassifyTilemap(tm, wallLayer, groundLayer, backgroundLayer, useSeparatedGround: true,
                out bool isWallVisual, out bool isWallCollider, out bool isBackground);

            if (wallTilemap == null && isWallVisual) wallTilemap = tm;
            if (colliderTilemap == null && isWallCollider) colliderTilemap = tm;
            if (backgroundTilemap == null && isBackground) backgroundTilemap = tm;
        }
    }

    private static TileBase FindFirstNonNullTile(Tilemap tilemap)
    {
        if (tilemap == null) return null;
        foreach (Vector3Int p in tilemap.cellBounds.allPositionsWithin)
        {
            TileBase t = tilemap.GetTile(p);
            if (t != null) return t;
        }
        return null;
    }

    private void ConfigureGlobalGroundPhysics(bool useSeparatedGround)
    {
        TilemapRenderer visualRenderer = globalWallTilemap.GetComponent<TilemapRenderer>();
        if (visualRenderer != null) visualRenderer.enabled = true;

        TilemapCollider2D visualTilemapCollider = globalWallTilemap.GetComponent<TilemapCollider2D>();
        if (visualTilemapCollider != null) visualTilemapCollider.enabled = !useSeparatedGround;

        CompositeCollider2D visualComposite = globalWallTilemap.GetComponent<CompositeCollider2D>();
        if (visualComposite != null) visualComposite.enabled = !useSeparatedGround;

        Rigidbody2D visualBody = globalWallTilemap.GetComponent<Rigidbody2D>();
        if (visualBody != null) visualBody.simulated = !useSeparatedGround;

        if (globalGroundColliderTilemap != null && globalGroundColliderTilemap != globalWallTilemap)
            globalGroundColliderTilemap.gameObject.SetActive(useSeparatedGround);
    }

    private void SyncGlobalRendererSettings(List<GameObject> spawnedRooms)
    {
        Tilemap wallSource = null;
        Tilemap backgroundSource = null;

        ResolveLayerIds(out int wallLayer, out int groundLayer, out int backgroundLayer);

        foreach (GameObject roomObject in spawnedRooms)
        {
            if (roomObject == null) continue;

            Tilemap[] tilemaps = roomObject.GetComponentsInChildren<Tilemap>(true);
            foreach (Tilemap tilemap in tilemaps)
            {
                ClassifyTilemap(tilemap, wallLayer, groundLayer, backgroundLayer, useSeparatedGround: false,
                    out bool isWallVisual, out bool _, out bool isBackground);

                if (wallSource == null && isWallVisual)
                    wallSource = tilemap;

                if (backgroundSource == null && isBackground)
                    backgroundSource = tilemap;

                if (wallSource != null && backgroundSource != null) break;
            }

            if (wallSource != null && backgroundSource != null) break;
        }

        CopyRendererAppearance(wallSource, globalWallTilemap);
        CopyRendererAppearance(backgroundSource, globalBackgroundTilemap);
    }

    private static void CopyRendererAppearance(Tilemap source, Tilemap destination)
    {
        if (source == null || destination == null) return;

        TilemapRenderer sourceRenderer = source.GetComponent<TilemapRenderer>();
        TilemapRenderer destinationRenderer = destination.GetComponent<TilemapRenderer>();
        if (sourceRenderer != null && destinationRenderer != null)
        {
            destinationRenderer.sharedMaterial = sourceRenderer.sharedMaterial;
            destinationRenderer.sortingLayerID = sourceRenderer.sortingLayerID;
            destinationRenderer.sortingOrder = sourceRenderer.sortingOrder;
        }

        destination.color = source.color;
        destination.tileAnchor = source.tileAnchor;
    }

    private static void ApplyTiles(Tilemap tilemap, Dictionary<Vector3Int, TileBase> tiles)
    {
        if (tilemap == null || tiles == null || tiles.Count == 0) return;

        Vector3Int[] positions = new Vector3Int[tiles.Count];
        TileBase[] tileAssets = new TileBase[tiles.Count];
        int index = 0;

        foreach (KeyValuePair<Vector3Int, TileBase> entry in tiles)
        {
            positions[index] = entry.Key;
            tileAssets[index] = entry.Value;
            index++;
        }

        tilemap.SetTiles(positions, tileAssets);
    }

    private static void RefreshTilemapPhysics(Tilemap tilemap)
    {
        if (tilemap == null) return;

        TilemapCollider2D tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
        if (tilemapCollider != null) tilemapCollider.ProcessTilemapChanges();

        CompositeCollider2D composite = tilemap.GetComponent<CompositeCollider2D>();
        if (composite != null) composite.GenerateGeometry();
    }

    private static bool AllRoomsHaveSeparatedGround(List<GameObject> spawnedRooms)
    {
        bool foundWallVisual = false;

        foreach (GameObject roomObject in spawnedRooms)
        {
            if (roomObject == null) continue;

            bool roomHasVisual = false;
            bool roomHasCollider = false;
            Tilemap[] roomTilemaps = roomObject.GetComponentsInChildren<Tilemap>(true);

            foreach (Tilemap tilemap in roomTilemaps)
            {
                string tilemapName = tilemap.gameObject.name;
                roomHasVisual |= IsWallVisualName(tilemapName);
                roomHasCollider |= IsWallColliderName(tilemapName);
            }

            if (roomHasVisual)
            {
                foundWallVisual = true;
                if (!roomHasCollider) return false;
            }
        }

        return foundWallVisual;
    }

    private static void ResolveLayerIds(out int wallLayer, out int groundLayer, out int backgroundLayer)
    {
        wallLayer = LayerMask.NameToLayer("Wall");
        groundLayer = LayerMask.NameToLayer("Ground");
        backgroundLayer = LayerMask.NameToLayer("Background");
    }

    private static void ClassifyTilemap(
        Tilemap tilemap,
        int wallLayer,
        int groundLayer,
        int backgroundLayer,
        bool useSeparatedGround,
        out bool isWallVisual,
        out bool isWallCollider,
        out bool isBackground)
    {
        isWallVisual = false;
        isWallCollider = false;
        isBackground = false;
        if (tilemap == null) return;

        string tilemapName = tilemap.gameObject.name;
        int layer = tilemap.gameObject.layer;

        isWallCollider = IsWallColliderName(tilemapName);
        isBackground = (backgroundLayer >= 0 && layer == backgroundLayer) || IsBackgroundName(tilemapName);
        isWallVisual = !isBackground && !isWallCollider &&
                       (IsWallVisualName(tilemapName) ||
                        (!useSeparatedGround &&
                         ((wallLayer >= 0 && layer == wallLayer) || (groundLayer >= 0 && layer == groundLayer))));
    }

    private static bool IsWallVisualName(string objectName)
    {
        string normalized = NormalizeTilemapName(objectName);
        return normalized == "wall" ||
               normalized == "walltilemap" ||
               normalized == "tilemapwall" ||
               normalized == "ground" ||
               normalized == "tilemapground";
    }

    private static bool IsWallColliderName(string objectName)
    {
        string normalized = NormalizeTilemapName(objectName);
        return normalized == "wallcollider" ||
               normalized == "tilemapwallcollider" ||
               normalized == "groundcollider" ||
               normalized == "tilemapgroundcollider";
    }

    private static bool IsBackgroundName(string objectName)
    {
        string normalized = NormalizeTilemapName(objectName);
        return normalized == "background" ||
               normalized == "backgroundtilemap" ||
               normalized == "tilemapbackground";
    }

    private static string NormalizeTilemapName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return string.Empty;

        char[] buffer = new char[objectName.Length];
        int length = 0;
        foreach (char character in objectName)
        {
            if (char.IsLetterOrDigit(character))
                buffer[length++] = char.ToLowerInvariant(character);
        }

        return new string(buffer, 0, length);
    }
}
