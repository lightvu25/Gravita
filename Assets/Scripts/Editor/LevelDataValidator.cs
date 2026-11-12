using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEditor;

/// <summary>
/// Custom Inspector for LevelData that provides a [Validate Level] button.
///
/// Runs a series of checks on the LevelData configuration and the referenced
/// Layout Prefab — including Gravita's grid mechanics (VectorLauncher landing safety,
/// CircuitSwitch / BreachSequencer ISwitchable wiring, SentinelDrone wall clearance,
/// same-cell mechanic conflicts, and spawn/anchor hazard overlap).
/// </summary>
[CustomEditor(typeof(LevelData))]
public class LevelDataValidator : UnityEditor.Editor
{
    // ─────────────────────────────────────────────────────────────
    //  Validation Result
    // ─────────────────────────────────────────────────────────────

    public enum CheckResult { Pass, Warning, Error }

    public struct ValidationCheck
    {
        public string      Label;
        public CheckResult Result;
        public string      Message;
    }

    // ─────────────────────────────────────────────────────────────
    //  State
    // ─────────────────────────────────────────────────────────────

    private List<ValidationCheck> lastResults;
    private bool hasValidated;

    // ─────────────────────────────────────────────────────────────
    //  Inspector GUI
    // ─────────────────────────────────────────────────────────────

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        EditorGUILayout.Space(10);
        EditorGUILayout.LabelField("Level Validation", EditorStyles.boldLabel);

        if (GUILayout.Button("✓  Validate Level", GUILayout.Height(30)))
        {
            lastResults = Validate((LevelData)target);
            hasValidated = true;
        }

        if (!hasValidated) return;

        EditorGUILayout.Space(5);

        foreach (var check in lastResults)
        {
            Color original = GUI.color;

            switch (check.Result)
            {
                case CheckResult.Pass:    GUI.color = new Color(0.5f, 1f, 0.5f); break;
                case CheckResult.Warning: GUI.color = new Color(1f, 0.9f, 0.3f); break;
                case CheckResult.Error:   GUI.color = new Color(1f, 0.4f, 0.4f); break;
            }

            string icon = check.Result switch
            {
                CheckResult.Pass    => "✓",
                CheckResult.Warning => "⚠",
                CheckResult.Error   => "✗",
                _ => "?"
            };

            string text = string.IsNullOrEmpty(check.Message)
                ? $"{icon}  {check.Label}"
                : $"{icon}  {check.Label}: {check.Message}";

            EditorGUILayout.HelpBox(text, check.Result == CheckResult.Error
                ? MessageType.Error
                : check.Result == CheckResult.Warning
                    ? MessageType.Warning
                    : MessageType.Info);

            GUI.color = original;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Validation Logic
    // ─────────────────────────────────────────────────────────────

    public static List<ValidationCheck> Validate(LevelData data)
    {
        var results = new List<ValidationCheck>();
        if (data == null)
        {
            results.Add(new ValidationCheck
            {
                Label   = "LevelData",
                Result  = CheckResult.Error,
                Message = "LevelData reference is null.",
            });
            return results;
        }

        // 1 — Level ID
        bool hasId = !string.IsNullOrWhiteSpace(data.LevelId);
        results.Add(new ValidationCheck
        {
            Label   = "Level ID",
            Result  = hasId ? CheckResult.Pass : CheckResult.Error,
            Message = hasId ? data.LevelId : "Level ID is empty.",
        });

        // 2 — Layout Prefab
        bool hasPrefab = data.LayoutPrefab != null;
        results.Add(new ValidationCheck
        {
            Label   = "Layout Prefab",
            Result  = hasPrefab ? CheckResult.Pass : CheckResult.Error,
            Message = hasPrefab ? data.LayoutPrefab.name : "No Layout Prefab assigned.",
        });

        if (!hasPrefab)
        {
            results.Add(new ValidationCheck
            {
                Label   = "Layout Contents",
                Result  = CheckResult.Warning,
                Message = "Skipped — Layout Prefab must be assigned first.",
            });
            results.AddRange(ValidateObjective(data, null));
            return results;
        }

        GameObject prefab = data.LayoutPrefab;

        // 3 — LevelRoot on prefab
        LevelRoot root = prefab.GetComponent<LevelRoot>();
        bool hasRoot = root != null;
        results.Add(new ValidationCheck
        {
            Label   = "LevelRoot Component",
            Result  = hasRoot ? CheckResult.Pass : CheckResult.Error,
            Message = hasRoot ? "Found on prefab root." : "Layout Prefab root is missing a LevelRoot component.",
        });

        // 4 — Player spawn point
        var spawn = prefab.GetComponentInChildren<PlayerSpawnPoint>(true);
        results.Add(new ValidationCheck
        {
            Label   = "Player Spawn Point",
            Result  = spawn != null ? CheckResult.Pass : CheckResult.Warning,
            Message = spawn != null ? $"Found '{spawn.name}'." : "No PlayerSpawnPoint found — the player will spawn at the layout root.",
        });

        // 5 — Level exit
        var exits = prefab.GetComponentsInChildren<LevelExit>(true);
        results.Add(new ValidationCheck
        {
            Label   = "Level Exit(s)",
            Result  = exits.Length > 0 ? CheckResult.Pass : CheckResult.Warning,
            Message = exits.Length > 0 ? $"{exits.Length} found." : "No LevelExit found — ReachExit objectives can never complete.",
        });

        // 6 — Collectibles
        var collectibles = prefab.GetComponentsInChildren<Collectible>(true);
        int coinCount = 0;
        int starCount = 0;
        for (int i = 0; i < collectibles.Length; i++)
        {
            if (collectibles[i] == null) continue;
            if (collectibles[i].IsStar) starCount++;
            else if (collectibles[i].IsCoin) coinCount++;
        }
        results.Add(new ValidationCheck
        {
            Label   = "Collectible(s)",
            Result  = collectibles.Length > 0 ? CheckResult.Pass : CheckResult.Warning,
            Message = collectibles.Length > 0
                ? $"{collectibles.Length} found ({coinCount} Coin(s), {starCount} Star(s))."
                : "No collectibles in layout.",
        });

        // 7 — Hazards
        var hazards = prefab.GetComponentsInChildren<Hazard>(true);
        if (hazards.Length > 0)
        {
            results.Add(new ValidationCheck
            {
                Label   = "Hazard(s)",
                Result  = CheckResult.Pass,
                Message = $"{hazards.Length} found in layout.",
            });
        }

        // 8 — EXP Reward configuration
        bool hasExp = data.baseExpReward > 0 || data.replayExpReward > 0;
        results.Add(new ValidationCheck
        {
            Label   = "EXP Rewards",
            Result  = hasExp ? CheckResult.Pass : CheckResult.Warning,
            Message = hasExp
                ? $"First clear: {data.baseExpReward} (+{data.expBonusPerStar}/star), Replay: {data.replayExpReward}."
                : "Both baseExpReward and replayExpReward are 0.",
        });

        // 9 — Objective checks
        results.AddRange(ValidateObjective(data, collectibles));

        // 10 — Gravita Mechanics Checks
        results.AddRange(ValidateGravitaMechanics(prefab, spawn, hazards));

        // 11 — Read-only Grid Alignment & Pure Maze Slide Checks (Q7: reports off-grid Spawn/Exit without modifying assets)
        results.AddRange(ValidateGridAlignmentAndMazeTopology(prefab, spawn, exits));

        return results;
    }

    private static IEnumerable<ValidationCheck> ValidateObjective(LevelData data, Collectible[] collectibles)
    {
        var results = new List<ValidationCheck>();

        bool hasPrimary = data.PrimaryObjective != null;
        results.Add(new ValidationCheck
        {
            Label   = "Primary Objective",
            Result  = hasPrimary ? CheckResult.Pass : CheckResult.Error,
            Message = hasPrimary ? $"Type: {data.PrimaryObjective.objectiveType}" : "No primary objective configured.",
        });

        if (!hasPrimary) return results;

        var obj = data.PrimaryObjective;

        bool isSupported = obj.objectiveType == ObjectiveType.ReachExit
                        || obj.objectiveType == ObjectiveType.CollectPickups;
        results.Add(new ValidationCheck
        {
            Label   = "Objective Type",
            Result  = isSupported ? CheckResult.Pass : CheckResult.Warning,
            Message = isSupported ? $"{obj.objectiveType} (supported)." : $"{obj.objectiveType} is not yet implemented.",
        });

        if (obj.objectiveType == ObjectiveType.CollectPickups)
        {
            int available = collectibles != null ? collectibles.Length : 0;
            bool countOk = obj.requiredCount == 0 || obj.requiredCount <= available || collectibles == null;
            results.Add(new ValidationCheck
            {
                Label   = "Required Pickup Count",
                Result  = countOk ? CheckResult.Pass : CheckResult.Error,
                Message = obj.requiredCount == 0
                    ? "0 = collect every pickup in the layout."
                    : collectibles == null
                        ? $"{obj.requiredCount} required (layout not checked)."
                        : $"{obj.requiredCount} required, {available} available in layout.",
            });
        }

        return results;
    }

    // ─────────────────────────────────────────────────────────────
    //  Gravita Mechanics Validation (Prefab-Safe in EditMode)
    // ─────────────────────────────────────────────────────────────

    private static IEnumerable<ValidationCheck> ValidateGravitaMechanics(
        GameObject prefab,
        PlayerSpawnPoint spawn,
        Hazard[] hazards)
    {
        var results = new List<ValidationCheck>();
        Grid grid = prefab.GetComponentInChildren<Grid>(true);
        int wallLayer = LayerMask.NameToLayer("Wall");

        // Collect Wall-layer tilemaps and solid Wall-layer colliders in the prefab
        var wallTilemaps = new List<Tilemap>();
        foreach (var tm in prefab.GetComponentsInChildren<Tilemap>(true))
        {
            if (tm != null && tm.gameObject.layer == wallLayer)
                wallTilemaps.Add(tm);
        }

        var solidWallColliders = new List<BoxCollider2D>();
        foreach (var col in prefab.GetComponentsInChildren<BoxCollider2D>(true))
        {
            if (col == null || col.isTrigger || col.gameObject.layer != wallLayer) continue;
            solidWallColliders.Add(col);
        }

        // A — Same-cell conflicting movement mechanics
        var interactions = prefab.GetComponentsInChildren<ICellInteraction>(true);
        if (interactions.Length > 0)
        {
            var movementOwnerByCell = new Dictionary<Vector2Int, MonoBehaviour>();
            var conflicts = new List<string>();

            foreach (var ic in interactions)
            {
                if (!(ic is MonoBehaviour mb) || mb == null) continue;
                // Only movement-claiming priorities (GravityBlock..Brake) conflict with each other
                if (ic.Priority < CellInteractionPriority.GravityBlock || ic.Priority >= CellInteractionPriority.FailingTile)
                    continue;

                Vector2Int cellKey = WorldToCellKey(grid, mb.transform.position);
                if (movementOwnerByCell.TryGetValue(cellKey, out MonoBehaviour existing))
                {
                    conflicts.Add($"'{existing.name}' and '{mb.name}' overlap at cell {cellKey}");
                }
                else
                {
                    movementOwnerByCell[cellKey] = mb;
                }
            }

            results.Add(new ValidationCheck
            {
                Label   = "Cell Mechanic Conflicts",
                Result  = conflicts.Count == 0 ? CheckResult.Pass : CheckResult.Error,
                Message = conflicts.Count == 0
                    ? $"{interactions.Length} cell interaction(s) checked — no conflicting movement tiles."
                    : string.Join("; ", conflicts),
            });
        }

        // B — CircuitSwitch target wiring
        var switches = prefab.GetComponentsInChildren<CircuitSwitch>(true);
        if (switches.Length > 0)
        {
            var switchIssues = new List<string>();
            foreach (var sw in switches)
            {
                var targets = sw.RawTargets;
                if (targets == null || targets.Length == 0)
                {
                    switchIssues.Add($"'{sw.name}' has no targets assigned");
                    continue;
                }

                for (int i = 0; i < targets.Length; i++)
                {
                    if (targets[i] == null)
                        switchIssues.Add($"'{sw.name}' target[{i}] is null");
                    else if (!(targets[i] is ISwitchable))
                        switchIssues.Add($"'{sw.name}' target '{targets[i].name}' does not implement ISwitchable");
                }
            }

            results.Add(new ValidationCheck
            {
                Label   = "Circuit Switch Wiring",
                Result  = switchIssues.Count == 0 ? CheckResult.Pass : CheckResult.Error,
                Message = switchIssues.Count == 0
                    ? $"{switches.Length} CircuitSwitch(es) wired to valid ISwitchable targets."
                    : string.Join("; ", switchIssues),
            });
        }

        // C — VectorLauncher landing safety
        var launchers = prefab.GetComponentsInChildren<VectorLauncher>(true);
        if (launchers.Length > 0)
        {
            var launcherIssues = new List<string>();
            foreach (var launcher in launchers)
            {
                Vector2 landing = launcher.GetResolvedLandingPosition();
                if (Vector2.Distance(landing, launcher.transform.position) < 0.25f)
                {
                    launcherIssues.Add($"'{launcher.name}' landing position overlaps its own cell");
                    continue;
                }

                if (IsPositionBlockedByUnbreakablePrefabWall(landing, grid, wallTilemaps, solidWallColliders))
                {
                    launcherIssues.Add($"'{launcher.name}' landing at {landing} is inside an unbreakable Wall");
                }
            }

            results.Add(new ValidationCheck
            {
                Label   = "Vector Launcher Landings",
                Result  = launcherIssues.Count == 0 ? CheckResult.Pass : CheckResult.Error,
                Message = launcherIssues.Count == 0
                    ? $"{launchers.Length} VectorLauncher(s) have clear landing positions."
                    : string.Join("; ", launcherIssues),
            });
        }

        // D — SentinelDronePatrol route wall clearance
        var drones = prefab.GetComponentsInChildren<SentinelDronePatrol>(true);
        if (drones.Length > 0)
        {
            var droneIssues = new List<string>();
            var waypoints = new List<Vector2>(8);
            foreach (var drone in drones)
            {
                drone.BuildWaypoints(drone.transform.position, waypoints);
                int segCount = drone.PatrolMode == DronePatrolMode.Loop ? waypoints.Count : waypoints.Count - 1;
                for (int s = 0; s < segCount; s++)
                {
                    Vector2 a = waypoints[s];
                    Vector2 b = waypoints[(s + 1) % waypoints.Count];
                    int steps = Mathf.Max(2, Mathf.CeilToInt(Vector2.Distance(a, b) * 2f));
                    for (int k = 0; k <= steps; k++)
                    {
                        Vector2 sample = Vector2.Lerp(a, b, k / (float)steps);
                        if (IsPositionBlockedByUnbreakablePrefabWall(sample, grid, wallTilemaps, solidWallColliders, ignoreGates: true))
                        {
                            droneIssues.Add($"'{drone.name}' segment #{s} intersects a Wall near {sample}");
                            break;
                        }
                    }
                }
            }

            results.Add(new ValidationCheck
            {
                Label   = "Sentinel Drone Routes",
                Result  = droneIssues.Count == 0 ? CheckResult.Pass : CheckResult.Warning,
                Message = droneIssues.Count == 0
                    ? $"{drones.Length} SentinelDrone route(s) clear of static walls."
                    : string.Join("; ", droneIssues),
            });
        }

        // E — Safe Resting Cells (Spawn & GravityBlock) vs Active/Timed Hazards (D6: no revive grace)
        var gravityBlocks = prefab.GetComponentsInChildren<GravityBlock>(true);
        if (hazards != null && hazards.Length > 0 && (spawn != null || gravityBlocks.Length > 0))
        {
            var hazardCells = new HashSet<Vector2Int>();
            foreach (var h in hazards)
            {
                if (h == null || !h.gameObject.activeSelf) continue; // Ignore inactive CollapsedPitHazard children
                hazardCells.Add(WorldToCellKey(grid, h.transform.position));
            }

            var unsafeAnchors = new List<string>();
            if (spawn != null && hazardCells.Contains(WorldToCellKey(grid, spawn.transform.position)))
                unsafeAnchors.Add($"PlayerSpawn '{spawn.name}' shares a cell with a Hazard");

            foreach (var gb in gravityBlocks)
            {
                if (gb != null && hazardCells.Contains(WorldToCellKey(grid, gb.transform.position)))
                    unsafeAnchors.Add($"GravityBlock '{gb.name}' shares a cell with a Hazard");
            }

            if (unsafeAnchors.Count > 0)
            {
                results.Add(new ValidationCheck
                {
                    Label   = "Safe Anchor Cells",
                    Result  = CheckResult.Warning,
                    Message = string.Join("; ", unsafeAnchors),
                });
            }
        }

        return results;
    }

    private static Vector2Int WorldToCellKey(Grid grid, Vector3 worldPos)
    {
        if (grid != null)
        {
            Vector3Int c = grid.WorldToCell(worldPos);
            return new Vector2Int(c.x, c.y);
        }
        return new Vector2Int(Mathf.FloorToInt(worldPos.x), Mathf.FloorToInt(worldPos.y));
    }

    private static bool IsPositionBlockedByUnbreakablePrefabWall(
        Vector2 worldPos,
        Grid grid,
        List<Tilemap> wallTilemaps,
        List<BoxCollider2D> solidWallColliders,
        bool ignoreGates = false)
    {
        if (grid != null)
        {
            Vector3Int cell = grid.WorldToCell(worldPos);
            for (int i = 0; i < wallTilemaps.Count; i++)
            {
                if (wallTilemaps[i].HasTile(cell))
                    return true;
            }
        }

        for (int i = 0; i < solidWallColliders.Count; i++)
        {
            BoxCollider2D col = solidWallColliders[i];
            if (col == null || !col.gameObject.activeInHierarchy) continue;
            if (col.GetComponentInParent<CrackedBlock>() != null) continue;

            EnergyGate gate = col.GetComponentInParent<EnergyGate>();
            if (gate != null && ignoreGates) continue;

            Vector2 center = (Vector2)col.transform.TransformPoint(col.offset);
            Vector2 half = Vector2.Scale(col.size, col.transform.lossyScale) * 0.45f;
            if (Mathf.Abs(worldPos.x - center.x) <= half.x && Mathf.Abs(worldPos.y - center.y) <= half.y)
                return true;
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Read-Only Grid Alignment & Pure Maze Slide Topology (Q7)
    // ─────────────────────────────────────────────────────────────

    private static IEnumerable<ValidationCheck> ValidateGridAlignmentAndMazeTopology(
        GameObject prefab,
        PlayerSpawnPoint spawn,
        LevelExit[] exits)
    {
        var results = new List<ValidationCheck>();
        Grid grid = prefab.GetComponentInChildren<Grid>(true);
        if (grid == null)
            return results;

        // 1. Check .5 cell-center alignment of Spawn and Exit(s) (non-destructive warning for legacy levels like Level_001)
        var offGridMessages = new List<string>();

        if (spawn != null)
        {
            Vector3 spPos = spawn.transform.position;
            Vector3 spCenter = grid.GetCellCenterWorld(grid.WorldToCell(spPos));
            if (Vector2.Distance(spPos, spCenter) > 0.05f)
            {
                offGridMessages.Add(
                    $"PlayerSpawn '{spawn.name}' at ({spPos.x:F2}, {spPos.y:F2}) is off-grid from cell center ({spCenter.x:F2}, {spCenter.y:F2})");
            }
        }

        if (exits != null)
        {
            for (int i = 0; i < exits.Length; i++)
            {
                if (exits[i] == null) continue;
                Vector3 exPos = exits[i].transform.position;
                Vector3 exCenter = grid.GetCellCenterWorld(grid.WorldToCell(exPos));
                if (Vector2.Distance(exPos, exCenter) > 0.05f)
                {
                    offGridMessages.Add(
                        $"LevelExit '{exits[i].name}' at ({exPos.x:F2}, {exPos.y:F2}) is off-grid from cell center ({exCenter.x:F2}, {exCenter.y:F2})");
                }
            }
        }

        results.Add(new ValidationCheck
        {
            Label  = "Grid Center Alignment",
            Result = offGridMessages.Count == 0 ? CheckResult.Pass : CheckResult.Warning,
            Message = offGridMessages.Count == 0
                ? "Spawn and Exit(s) are aligned to exact grid cell centres."
                : string.Join("; ", offGridMessages),
        });

        // 2. Pure Maze Slide Graph check (only when layout has a WallTilemap, Spawn, Exit, and no dynamic movement/gate mechanics)
        if (spawn == null || exits == null || exits.Length == 0 || exits[0] == null)
            return results;

        bool hasDynamicMechanics = prefab.GetComponentsInChildren<ICellInteraction>(true).Length > 0
                                || prefab.GetComponentsInChildren<EnergyGate>(true).Length > 0
                                || prefab.GetComponentsInChildren<CrackedBlock>(true).Length > 0;
        if (hasDynamicMechanics)
        {
            results.Add(new ValidationCheck
            {
                Label   = "Maze Slide Topology",
                Result  = CheckResult.Pass,
                Message = "Skipped pure-maze graph check — layout contains dynamic Gravita mechanics.",
            });
            return results;
        }

        int wallLayer = LayerMask.NameToLayer("Wall");
        Tilemap wallTilemap = null;
        foreach (var tm in prefab.GetComponentsInChildren<Tilemap>(true))
        {
            if (tm != null && tm.gameObject.layer == wallLayer)
            {
                wallTilemap = tm;
                break;
            }
        }

        if (wallTilemap == null || !TryExtractReadOnlyMazeGrid(prefab, grid, wallTilemap, spawn, exits[0], out MazeGrid extracted))
            return results;

        // Hand-authored levels allow pass-through exits (Q3) and may intentionally use open doorways
        // bounded by external colliders; report diagnostic findings on LevelData as Warning, never Error.
        MazeValidationReport report = MazeValidator.Validate(extracted, requireExitOnStopCell: false);

        if (report.IsValid)
        {
            int moves = report.SlideGraph != null ? report.SlideGraph.SolutionMoves : 0;
            results.Add(new ValidationCheck
            {
                Label   = "Maze Slide Topology",
                Result  = CheckResult.Pass,
                Message = $"Solvable in {moves} move(s), 0 reachable soft-locks ({extracted.Width}x{extracted.Height} grid).",
            });
        }
        else
        {
            var msgs = new List<string>(report.Issues.Count);
            for (int i = 0; i < report.Issues.Count; i++)
            {
                if (report.Issues[i].Severity == MazeValidationSeverity.Error)
                    msgs.Add(report.Issues[i].Message);
            }

            results.Add(new ValidationCheck
            {
                Label   = "Maze Slide Topology",
                Result  = CheckResult.Warning,
                Message = string.Join("; ", msgs),
            });
        }

        return results;
    }

    private static bool TryExtractReadOnlyMazeGrid(
        GameObject prefab,
        Grid grid,
        Tilemap wallTilemap,
        PlayerSpawnPoint spawn,
        LevelExit exit,
        out MazeGrid mazeGrid)
    {
        mazeGrid = null;
        BoundsInt raw = wallTilemap.cellBounds;
        int minX = int.MaxValue, minY = int.MaxValue;
        int maxX = int.MinValue, maxY = int.MinValue;
        bool anyTile = false;

        for (int y = raw.yMin; y < raw.yMax; y++)
        {
            for (int x = raw.xMin; x < raw.xMax; x++)
            {
                if (wallTilemap.HasTile(new Vector3Int(x, y, 0)))
                {
                    anyTile = true;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }
        }

        if (!anyTile) return false;

        int width = maxX - minX + 1;
        int height = maxY - minY + 1;
        if (width < 3 || height < 3 || width > 256 || height > 256)
            return false;

        mazeGrid = new MazeGrid(width, height, MazeCell.Floor);
        for (int gx = 0; gx < width; gx++)
        {
            for (int gy = 0; gy < height; gy++)
            {
                var cell3 = new Vector3Int(minX + gx, minY + gy, 0);
                if (wallTilemap.HasTile(cell3))
                    mazeGrid.SetCell(gx, gy, MazeCell.Wall);
            }
        }

        // Also stamp any static Wall-layer BoxCollider2D (e.g. SolidBlock)
        int wallLayer = LayerMask.NameToLayer("Wall");
        foreach (var col in prefab.GetComponentsInChildren<BoxCollider2D>(true))
        {
            if (col == null || col.isTrigger || col.gameObject.layer != wallLayer) continue;
            if (col.GetComponent<TilemapCollider2D>() != null) continue;
            Vector3Int bc = grid.WorldToCell(col.transform.TransformPoint(col.offset));
            int gx = bc.x - minX;
            int gy = bc.y - minY;
            if (mazeGrid.InBounds(gx, gy))
                mazeGrid.SetCell(gx, gy, MazeCell.Wall);
        }

        Vector3Int spCell = grid.WorldToCell(spawn.transform.position);
        Vector3Int exCell = grid.WorldToCell(exit.transform.position);
        mazeGrid.SetSpawn(new Vector2Int(spCell.x - minX, spCell.y - minY));
        mazeGrid.SetExit(new Vector2Int(exCell.x - minX, exCell.y - minY));
        return true;
    }
}
