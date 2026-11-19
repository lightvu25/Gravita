using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Runtime coordinator for a single room in Maze Runner mode.
/// Wraps the room's <see cref="LevelRoot"/>, <see cref="RunnerPortalNode"/>s (<c>StartNode</c> / <c>GoalNode</c>),
/// local Tilemaps, and pooled entity ownership for zero-leak window cleanup.
/// </summary>
public class RunnerRoomController : MonoBehaviour
{
    private struct SpawnedEntityRecord
    {
        public GameObject Instance;
        public bool IsPooled;
    }

    [Header("Room Layout References (Optional — auto-resolved on children)")]
    [SerializeField] private LevelRoot levelRoot;
    [SerializeField] private Grid roomGrid;
    [SerializeField] private Tilemap wallTilemap;
    [SerializeField] private Tilemap backgroundTilemap;
    [SerializeField] private RunnerPortalNode startNode;
    [SerializeField] private RunnerPortalNode goalNode;

    [Header("Logical Footprint (0 = auto-compute from Tilemaps)")]
    [SerializeField] private Vector2Int localGridOrigin = Vector2Int.zero;
    [SerializeField] private int logicalWidth = 0;
    [SerializeField] private int logicalHeight = 0;

    private readonly List<SpawnedEntityRecord> spawnedEntities = new List<SpawnedEntityRecord>(32);
    private bool initialized;

    public int RoomNumber { get; private set; }
    public LevelRoot LevelRoot => levelRoot;
    public Grid RoomGrid => roomGrid;
    public Tilemap WallTilemap => wallTilemap;
    public Tilemap BackgroundTilemap => backgroundTilemap;
    public RunnerPortalNode StartNode => startNode;
    public RunnerPortalNode GoalNode => goalNode;
    public Vector2Int LocalGridOrigin => localGridOrigin;
    public Vector2Int WorldGridOriginOffset => localGridOrigin + new Vector2Int(
        Mathf.RoundToInt(transform.position.x),
        Mathf.RoundToInt(transform.position.y));
    public int LogicalWidth => logicalWidth;
    public int LogicalHeight => logicalHeight;

    public Vector3Int WorldStartCell => startNode != null
        ? startNode.GridCell
        : new Vector3Int(Mathf.FloorToInt(transform.position.x), Mathf.FloorToInt(transform.position.y), 0);

    public Vector3Int WorldGoalCell => goalNode != null
        ? goalNode.GridCell
        : WorldStartCell;

    public Vector3 StartWorldPosition => startNode != null
        ? startNode.transform.position
        : (levelRoot != null ? levelRoot.PlayerSpawnPosition : transform.position);

    public Vector3 GoalWorldPosition => goalNode != null
        ? goalNode.transform.position
        : transform.position;

    public CardinalDirection ExitDirection => goalNode != null
        ? goalNode.PortalDirection
        : CardinalDirection.Up;

    private void Awake()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (initialized)
            return;
        initialized = true;

        if (levelRoot == null)
            levelRoot = GetComponent<LevelRoot>();

        if (roomGrid == null)
            roomGrid = GetComponentInChildren<Grid>(true);

        ResolveTilemaps();
        ResolvePortalNodes();
        DisableStoryExitInsideRunner();
        ComputeGridExtentsIfUnset();
    }

    public RectInt WorldCellBounds => new RectInt(
        WorldGridOriginOffset.x,
        WorldGridOriginOffset.y,
        Mathf.Max(1, logicalWidth),
        Mathf.Max(1, logicalHeight));

    public void BindInspectorReferences(
        LevelRoot root,
        Grid grid,
        Tilemap wall,
        Tilemap background,
        RunnerPortalNode start,
        RunnerPortalNode goal)
    {
        levelRoot = root;
        roomGrid = grid;
        wallTilemap = wall;
        backgroundTilemap = background;
        startNode = start;
        goalNode = goal;
        initialized = false;
    }

    public bool ContainsWorldCell(Vector2Int worldCell)
    {
        Vector2Int origin = WorldGridOriginOffset;
        return worldCell.x >= origin.x && worldCell.x < origin.x + logicalWidth &&
               worldCell.y >= origin.y && worldCell.y < origin.y + logicalHeight;
    }

    public void StampProceduralGrid(
        MazeGrid grid,
        TileBase wallTile,
        TileBase backgroundTile,
        CardinalDirection entryDirection,
        CardinalDirection exitDirection)
    {
        if (grid == null)
        {
            Debug.LogError($"[RunnerRoomController] Cannot stamp null MazeGrid on '{name}'.", this);
            return;
        }

        EnsureInitialized();
        localGridOrigin = Vector2Int.zero;
        logicalWidth = grid.Width;
        logicalHeight = grid.Height;

        int totalCells = grid.Width * grid.Height;
        var wallPositions = new List<Vector3Int>(totalCells);
        var bgPositions = new Vector3Int[totalCells];
        int bgIndex = 0;

        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                var cell = new Vector3Int(gx, gy, 0);
                bgPositions[bgIndex++] = cell;
                if (grid.IsWall(new Vector2Int(gx, gy)))
                    wallPositions.Add(cell);
            }
        }

        if (wallTilemap != null)
        {
            if (!wallTilemap.gameObject.activeSelf)
                wallTilemap.gameObject.SetActive(true);
            wallTilemap.ClearAllTiles();

            if (wallTile != null && wallPositions.Count > 0)
            {
                var wallArray = new TileBase[wallPositions.Count];
                System.Array.Fill(wallArray, wallTile);
                wallTilemap.SetTiles(wallPositions.ToArray(), wallArray);
            }
            wallTilemap.RefreshAllTiles();
        }

        if (backgroundTilemap != null)
        {
            if (!backgroundTilemap.gameObject.activeSelf)
                backgroundTilemap.gameObject.SetActive(true);
            backgroundTilemap.ClearAllTiles();

            if (backgroundTile != null && bgPositions.Length > 0)
            {
                var bgArray = new TileBase[bgPositions.Length];
                System.Array.Fill(bgArray, backgroundTile);
                backgroundTilemap.SetTiles(bgPositions, bgArray);
            }
            backgroundTilemap.RefreshAllTiles();
        }

        if (startNode != null && grid.Spawn.HasValue)
        {
            startNode.transform.localPosition = new Vector3(grid.Spawn.Value.x + 0.5f, grid.Spawn.Value.y + 0.5f, 0f);
            startNode.Configure(RunnerPortalRole.Start, entryDirection);
        }

        if (goalNode != null && grid.Exit.HasValue)
        {
            goalNode.transform.localPosition = new Vector3(grid.Exit.Value.x + 0.5f, grid.Exit.Value.y + 0.5f, 0f);
            goalNode.Configure(RunnerPortalRole.Goal, exitDirection);
        }
    }

    public void InitializeForRoom(int roomNumber, MazeGrid proceduralGrid = null, Vector2Int? customOrigin = null)
    {
        RoomNumber = Mathf.Max(1, roomNumber);
        initialized = false;
        EnsureInitialized();

        if (customOrigin.HasValue)
            localGridOrigin = customOrigin.Value;

        if (proceduralGrid != null)
        {
            logicalWidth = proceduralGrid.Width;
            logicalHeight = proceduralGrid.Height;
        }
        else
        {
            ComputeGridExtentsIfUnset();
        }

        if (startNode != null)
            startNode.ResetPortal();
        if (goalNode != null)
            goalNode.ResetPortal();
    }

    public void DisableStoryExitInsideRunner()
    {
        LevelExit[] exits = GetComponentsInChildren<LevelExit>(true);
        for (int i = 0; i < exits.Length; i++)
        {
            if (exits[i] == null) continue;
            exits[i].enabled = false;
            if (exits[i].gameObject != (goalNode != null ? goalNode.gameObject : null))
            {
                Collider2D col = exits[i].GetComponent<Collider2D>();
                if (col != null) col.enabled = false;
            }
        }
    }

    public MazeGrid ExtractLogicalGridFromTilemap()
    {
        EnsureInitialized();
        ComputeGridExtentsIfUnset();

        int w = Mathf.Max(5, logicalWidth);
        int h = Mathf.Max(5, logicalHeight);
        var grid = new MazeGrid(w, h, MazeCell.Floor);

        if (wallTilemap != null)
        {
            for (int gx = 0; gx < w; gx++)
            {
                for (int gy = 0; gy < h; gy++)
                {
                    var cellPos = new Vector3Int(localGridOrigin.x + gx, localGridOrigin.y + gy, 0);
                    grid.SetCell(gx, gy, wallTilemap.HasTile(cellPos) ? MazeCell.Wall : MazeCell.Floor);
                }
            }
        }

        Vector2Int originWorld = WorldGridOriginOffset;
        if (startNode != null)
        {
            Vector2Int sp = new Vector2Int(WorldStartCell.x - originWorld.x, WorldStartCell.y - originWorld.y);
            if (grid.InBounds(sp))
            {
                grid.SetCell(sp.x, sp.y, MazeCell.Floor);
                grid.SetSpawn(sp);
            }
        }

        if (goalNode != null)
        {
            Vector2Int ex = new Vector2Int(WorldGoalCell.x - originWorld.x, WorldGoalCell.y - originWorld.y);
            if (grid.InBounds(ex))
            {
                grid.SetCell(ex.x, ex.y, MazeCell.Floor);
                grid.SetExit(ex);
            }
        }

        return grid;
    }

    public GameObject SpawnTrackedEntity(
        GameObject prefab,
        Vector3 worldPosition,
        Quaternion rotation,
        ObjectPoolManager.PoolType poolType = ObjectPoolManager.PoolType.GameObject)
    {
        if (prefab == null)
            return null;

        GameObject instance;
        bool isPooled;

        if (Application.isPlaying)
        {
            instance = ObjectPoolManager.SpawnObject(prefab, worldPosition, rotation, transform, poolType);
            isPooled = instance != null;
        }
        else
        {
            instance = Instantiate(prefab, worldPosition, rotation, transform);
            isPooled = false;
        }

        if (instance == null)
            return null;

        instance.name = prefab.name;

        Collectible collectible = instance.GetComponent<Collectible>();
        if (collectible != null)
            collectible.PrepareForRuntimeSpawn(worldPosition);

        Hazard hazard = instance.GetComponent<Hazard>();
        if (hazard != null)
            hazard.PrepareForRuntimeSpawn(worldPosition, levelRoot);

        CrackedBlock cracked = instance.GetComponent<CrackedBlock>();
        if (cracked != null)
            cracked.ResetBlock();

        spawnedEntities.Add(new SpawnedEntityRecord
        {
            Instance = instance,
            IsPooled = isPooled,
        });

        return instance;
    }

    public void RefreshLevelRootCaches()
    {
        if (levelRoot != null)
            levelRoot.RebuildCachedElements();
    }

    public void ReleaseRoomEntities()
    {
        for (int i = spawnedEntities.Count - 1; i >= 0; i--)
        {
            SpawnedEntityRecord rec = spawnedEntities[i];
            if (rec.Instance == null)
                continue;

            if (rec.IsPooled && Application.isPlaying)
            {
                ObjectPoolManager.ReturnObjectToPool(rec.Instance);
            }
            else if (Application.isPlaying)
            {
                Destroy(rec.Instance);
            }
            else
            {
                DestroyImmediate(rec.Instance);
            }
        }
        spawnedEntities.Clear();
    }

    private void OnDestroy()
    {
        ReleaseRoomEntities();
    }

    private void ResolveTilemaps()
    {
        if (wallTilemap != null && backgroundTilemap != null)
            return;

        int wallLayer = LayerMask.NameToLayer("Wall");
        int bgLayer = LayerMask.NameToLayer("Background");
        Tilemap[] tilemaps = GetComponentsInChildren<Tilemap>(true);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            Tilemap tm = tilemaps[i];
            if (tm == null) continue;

            if (wallTilemap == null && ((wallLayer >= 0 && tm.gameObject.layer == wallLayer) ||
                tm.gameObject.name.IndexOf("Wall", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                wallTilemap = tm;
                continue;
            }

            if (backgroundTilemap == null && ((bgLayer >= 0 && tm.gameObject.layer == bgLayer) ||
                tm.gameObject.name.IndexOf("Background", System.StringComparison.OrdinalIgnoreCase) >= 0))
            {
                backgroundTilemap = tm;
            }
        }
    }

    private void ResolvePortalNodes()
    {
        RunnerPortalNode[] nodes = GetComponentsInChildren<RunnerPortalNode>(true);
        for (int i = 0; i < nodes.Length; i++)
        {
            RunnerPortalNode n = nodes[i];
            if (n == null) continue;
            if (n.Role == RunnerPortalRole.Start && startNode == null)
                startNode = n;
            else if (n.Role == RunnerPortalRole.Goal && goalNode == null)
                goalNode = n;
        }

        if (startNode == null || goalNode == null)
        {
            Debug.LogError(
                $"[RunnerRoomController] Room '{name}' is missing StartNode or GoalNode RunnerPortalNode.",
                this);
        }
    }

    private void ComputeGridExtentsIfUnset()
    {
        if (logicalWidth > 0 && logicalHeight > 0)
            return;

        Tilemap source = wallTilemap != null ? wallTilemap : backgroundTilemap;
        if (source == null)
            return;

        source.CompressBounds();
        BoundsInt bounds = source.cellBounds;
        if (bounds.size.x > 0 && bounds.size.y > 0)
        {
            localGridOrigin = new Vector2Int(bounds.xMin, bounds.yMin);
            logicalWidth = bounds.size.x;
            logicalHeight = bounds.size.y;
        }
    }
}
