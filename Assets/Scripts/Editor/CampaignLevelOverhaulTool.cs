using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Editor tool to repair Star.prefab, upgrade Level 1-20 prefabs with full Background tiles,
/// reachable Stars (3/level), reachable Coins (6-12/level), and safe wave mechanics, then
/// validate every level via GridSlide/MazeValidator and LevelDataValidator.
/// </summary>
public static class CampaignLevelOverhaulTool
{
    private const string StarPrefabPath = "Assets/Prefabs/Gameplay/Star.prefab";
    private const string CoinPrefabPath = "Assets/Prefabs/Gameplay/Coin.prefab";
    private const string CrackedPrefabPath = "Assets/Prefabs/Gameplay/Cracked.prefab";
    private const string BrittlePrefabPath = "Assets/Prefabs/Gameplay/Brittle.prefab";
    private const string FailingTilePrefabPath = "Assets/Prefabs/Gameplay/FailingTile.prefab";
    private const string CampaignDbPath = "Assets/Data/Campaign/CampaignDatabase.asset";

    private sealed class OverhaulAssets
    {
        public TileBase WallTile;
        public TileBase BackgroundTile;
        public GameObject StarPrefab;
        public GameObject CoinPrefab;
        public GameObject CrackedPrefab;
        public GameObject BrittlePrefab;
        public GameObject FailingTilePrefab;
        public MazePatternLibrary Library;
        public MazeDifficultyProfile LearnProfile;
        public MazeDifficultyProfile NavigateProfile;
        public MazeDifficultyProfile MasterProfile;
    }

    [MenuItem("Tools/Gravita/Overhaul Campaign Levels 1-20")]
    public static void OverhaulAllCampaignLevels()
    {
        if (!TryLoadAssets(out OverhaulAssets assets, out string loadError))
        {
            Debug.LogError($"[CampaignLevelOverhaulTool] Aborted: {loadError}");
            return;
        }

        int starMissingRemoved = RemoveMissingScriptsFromPrefab(StarPrefabPath);
        int overhaulCount = 0;
        int errorCount = 0;

        if (OverhaulLevel1(assets, out string l1Summary))
            overhaulCount++;
        else
            errorCount++;

        Debug.Log($"[CampaignLevelOverhaulTool] Level 001: {l1Summary}");

        for (int levelNum = 2; levelNum <= 20; levelNum++)
        {
            if (OverhaulProceduralLevel(levelNum, assets, out string summary))
            {
                overhaulCount++;
                Debug.Log($"[CampaignLevelOverhaulTool] Level {levelNum:D3}: {summary}");
            }
            else
            {
                errorCount++;
                Debug.LogError($"[CampaignLevelOverhaulTool] Level {levelNum:D3} FAILED: {summary}");
            }
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        int validatorErrors = ValidateCampaignDatabase();
        Debug.Log($"[CampaignLevelOverhaulTool] Completed. Star.prefab missing scripts removed={starMissingRemoved}, overhauled={overhaulCount}/20, buildErrors={errorCount}, validatorErrors={validatorErrors}.");
    }

    private static bool TryLoadAssets(out OverhaulAssets assets, out string error)
    {
        assets = new OverhaulAssets
        {
            WallTile = MazeApplyService.LoadDefaultWallTile(),
            BackgroundTile = MazeApplyService.LoadDefaultBackgroundTile(),
            StarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(StarPrefabPath),
            CoinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CoinPrefabPath),
            CrackedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(CrackedPrefabPath),
            BrittlePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BrittlePrefabPath),
            FailingTilePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(FailingTilePrefabPath),
            Library = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath),
            LearnProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"{MazePatternBootstrap.ProfilesFolder}/Profile_Learn.asset"),
            NavigateProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"{MazePatternBootstrap.ProfilesFolder}/Profile_Navigate.asset"),
            MasterProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"{MazePatternBootstrap.ProfilesFolder}/Profile_Master.asset"),
        };

        if (assets.WallTile == null) { error = "Missing default wall tile."; return false; }
        if (assets.BackgroundTile == null) { error = "Missing default background tile."; return false; }
        if (assets.StarPrefab == null) { error = $"Missing '{StarPrefabPath}'."; return false; }
        if (assets.CoinPrefab == null) { error = $"Missing '{CoinPrefabPath}'."; return false; }
        if (assets.Library == null) { error = $"Missing '{MazePatternBootstrap.LibraryPath}'."; return false; }
        if (assets.LearnProfile == null || assets.NavigateProfile == null || assets.MasterProfile == null)
        {
            error = "Missing one or more MazeDifficultyProfile assets (Learn/Navigate/Master).";
            return false;
        }

        error = null;
        return true;
    }

    private static int RemoveMissingScriptsFromPrefab(string prefabPath)
    {
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
            return 0;

        int removed = RemoveMissingScriptsRecursive(root);
        if (removed > 0)
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

        PrefabUtility.UnloadPrefabContents(root);
        return removed;
    }

    private static int RemoveMissingScriptsRecursive(GameObject root)
    {
        int removed = 0;
        Transform[] transforms = root.GetComponentsInChildren<Transform>(includeInactive: true);
        for (int i = 0; i < transforms.Length; i++)
        {
            if (transforms[i] != null)
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transforms[i].gameObject);
        }
        return removed;
    }

    private static bool OverhaulLevel1(OverhaulAssets assets, out string summary)
    {
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>("Assets/Data/Levels/Level_001.asset");
        if (data == null || data.layoutPrefab == null)
        {
            summary = "Level_001.asset or its layoutPrefab is missing.";
            return false;
        }

        string prefabPath = AssetDatabase.GetAssetPath(data.layoutPrefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            summary = $"Failed to load prefab '{prefabPath}'.";
            return false;
        }

        try
        {
            RemoveMissingScriptsRecursive(root);
            EnsureTilemapsConfigured(root, out Grid grid, out Tilemap wallTilemap, out Tilemap backgroundTilemap);
            if (wallTilemap == null || backgroundTilemap == null)
            {
                summary = "Missing WallTilemap or BackgroundTilemap on Level 1.";
                return false;
            }

            // Fill missing interior/under-wall background tiles within existing WallTilemap bounds without altering walls.
            BoundsInt bounds = wallTilemap.cellBounds;
            var missingBg = new List<Vector3Int>();
            for (int x = bounds.xMin; x < bounds.xMax; x++)
            {
                for (int y = bounds.yMin; y < bounds.yMax; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (!backgroundTilemap.HasTile(cell))
                        missingBg.Add(cell);
                }
            }

            if (missingBg.Count > 0)
            {
                Vector3Int[] positions = missingBg.ToArray();
                TileBase[] tiles = new TileBase[positions.Length];
                Array.Fill(tiles, assets.BackgroundTile);
                backgroundTilemap.SetTiles(positions, tiles);
                backgroundTilemap.RefreshAllTiles();
            }

            PlayerSpawnPoint spawn = root.GetComponentInChildren<PlayerSpawnPoint>(includeInactive: true);
            LevelExit exit = root.GetComponentInChildren<LevelExit>(includeInactive: true);
            MazeGrid extractedGrid = ExtractMazeGridFromTilemap(wallTilemap, spawn, exit);
            MazeSlideGraph graph = MazeSlideGraph.Build(extractedGrid, requireExitOnStopCell: false);

            // Preserve the 3 legacy Stars; remove any Coin collectibles before re-populating cleanly.
            var occupiedWorldCells = new HashSet<Vector3Int>();
            if (spawn != null) occupiedWorldCells.Add(wallTilemap.WorldToCell(spawn.transform.position));
            if (exit != null) occupiedWorldCells.Add(wallTilemap.WorldToCell(exit.transform.position));

            Collectible[] existingCollectibles = root.GetComponentsInChildren<Collectible>(includeInactive: true);
            for (int i = 0; i < existingCollectibles.Length; i++)
            {
                Collectible col = existingCollectibles[i];
                if (col == null) continue;
                if (col.IsCoin)
                    UnityEngine.Object.DestroyImmediate(col.gameObject);
                else
                    occupiedWorldCells.Add(wallTilemap.WorldToCell(col.transform.position));
            }

            DestroyNamedContainerIfPresent(root, "Collectibles");
            var collectiblesParent = new GameObject("Collectibles").transform;
            collectiblesParent.SetParent(root.transform, false);

            var candidateWorldCells = new List<Vector3Int>();
            foreach (Vector2Int gCell in graph.TraversedFloorCells)
            {
                var wCell = new Vector3Int(bounds.xMin + gCell.x, bounds.yMin + gCell.y, 0);
                if (!wallTilemap.HasTile(wCell) && !occupiedWorldCells.Contains(wCell))
                    candidateWorldCells.Add(wCell);
            }

            candidateWorldCells.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
            List<Vector3Int> coinCells = SelectSpacedCells(candidateWorldCells, 6, minManhattanGap: 1);
            for (int i = 0; i < coinCells.Count; i++)
                InstantiatePrefabCenteredAtCell(assets.CoinPrefab, collectiblesParent, grid, wallTilemap, coinCells[i], $"Coin_{i + 1:D2}", applyColliderOffset: true);

            EnsureStarOptionalObjective(data, requiredStars: 2);
            wallTilemap.CompressBounds();
            backgroundTilemap.CompressBounds();
            MazeApplyService.RefreshTilemapPhysics(wallTilemap);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            summary = $"bgAdded={missingBg.Count}, coins={coinCells.Count}, starsPreserved={occupiedWorldCells.Count - (spawn != null ? 1 : 0) - (exit != null ? 1 : 0)}";
            return true;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static bool OverhaulProceduralLevel(int levelNumber, OverhaulAssets assets, out string summary)
    {
        LevelData data = AssetDatabase.LoadAssetAtPath<LevelData>($"Assets/Data/Levels/Level_{levelNumber:D3}.asset");
        if (data == null || data.layoutPrefab == null)
        {
            summary = $"Level_{levelNumber:D3}.asset or layoutPrefab missing.";
            return false;
        }

        MazeDifficultyProfile profile = ResolveProfileForLevel(levelNumber, assets);
        if (!TryGenerateValidSession(levelNumber, profile, assets.Library, out MazePreviewSession session, out int usedSeed))
        {
            summary = $"Could not generate valid maze for profile '{profile.name}'.";
            return false;
        }

        MazeGrid mazeGrid = session.PreviewGrid;
        MazeSlideGraph graph = session.Validation.SlideGraph;
        Vector2Int origin = MazeApplyService.ResolveOriginOffset(mazeGrid, centerAroundOrigin: true, Vector2Int.zero);

        string prefabPath = AssetDatabase.GetAssetPath(data.layoutPrefab);
        GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
        if (root == null)
        {
            summary = $"Could not load prefab '{prefabPath}'.";
            return false;
        }

        try
        {
            RemoveMissingScriptsRecursive(root);
            ClearOldGameplayObjects(root);
            EnsureTilemapsConfigured(root, out Grid grid, out Tilemap wallTilemap, out Tilemap backgroundTilemap);

            PlayerSpawnPoint spawn = root.GetComponentInChildren<PlayerSpawnPoint>(includeInactive: true);
            LevelExit exit = root.GetComponentInChildren<LevelExit>(includeInactive: true);
            if (wallTilemap == null || backgroundTilemap == null || spawn == null || exit == null)
            {
                summary = "Missing required prefab children (WallTilemap/BackgroundTilemap/Spawn/Exit).";
                return false;
            }

            var targets = MazeApplyTargets.CreateDefault();
            targets.WallTilemap = wallTilemap;
            targets.WallTile = assets.WallTile;
            targets.BackgroundTilemap = backgroundTilemap;
            targets.BackgroundTile = assets.BackgroundTile;
            targets.SpawnTransform = spawn.transform;
            targets.ExitTransform = exit.transform;
            targets.CenterAroundOrigin = true;

            MazeApplyService.WriteGridToTilemaps(mazeGrid, in targets, origin, out _);
            RuntimeTilemapMerger.AutoFillLocalLevelTilemaps(root, assets.WallTile, assets.BackgroundTile, padding: 0);

            Vector2Int spawnGrid = mazeGrid.Spawn.Value;
            Vector2Int exitGrid = mazeGrid.Exit.Value;
            spawn.transform.position = MazeApplyService.CellCenterToWorld(grid, wallTilemap, GridToTileCell(spawnGrid, origin));
            exit.transform.position = MazeApplyService.CellCenterToWorld(grid, wallTilemap, GridToTileCell(exitGrid, origin));

            var solutionCells = CollectSolutionCells(graph);
            var usedFloorCells = new HashSet<Vector2Int> { spawnGrid, exitGrid };

            var collectiblesParent = new GameObject("Collectibles").transform;
            collectiblesParent.SetParent(root.transform, false);

            // 1. Place 3 Stars on provably reachable, non-softlocking stops/traversed cells.
            List<Vector2Int> starGridCells = SelectStarCells(graph, solutionCells, usedFloorCells, targetCount: 3);
            for (int i = 0; i < starGridCells.Count; i++)
            {
                usedFloorCells.Add(starGridCells[i]);
                InstantiatePrefabCenteredAtCell(
                    assets.StarPrefab,
                    collectiblesParent,
                    grid,
                    wallTilemap,
                    GridToTileCell(starGridCells[i], origin),
                    $"Star_{i + 1:D2}",
                    applyColliderOffset: false);
            }

            // 2. Place 6-12 Coins on remaining traversed floor cells.
            int coinTarget = Mathf.Clamp(6 + (levelNumber / 3), 6, 12);
            List<Vector2Int> coinCandidates = CollectAvailableFloorCandidates(graph, usedFloorCells);
            List<Vector2Int> coinGridCells = SelectSpacedCells(coinCandidates, coinTarget, minManhattanGap: 1);
            for (int i = 0; i < coinGridCells.Count; i++)
            {
                usedFloorCells.Add(coinGridCells[i]);
                InstantiatePrefabCenteredAtCell(
                    assets.CoinPrefab,
                    collectiblesParent,
                    grid,
                    wallTilemap,
                    GridToTileCell(coinGridCells[i], origin),
                    $"Coin_{i + 1:D2}",
                    applyColliderOffset: true);
            }

            // 3. Place Wave-appropriate mechanics strictly outside critical solution path / softlock risks.
            int mechanicsPlaced = PlaceWaveMechanics(
                levelNumber,
                assets,
                root.transform,
                grid,
                wallTilemap,
                mazeGrid,
                graph,
                origin,
                solutionCells,
                usedFloorCells);

            wallTilemap.CompressBounds();
            backgroundTilemap.CompressBounds();
            MazeApplyService.RefreshTilemapPhysics(wallTilemap);
            EnsureStarOptionalObjective(data, requiredStars: levelNumber == 2 ? 2 : 3);
            PrefabUtility.SaveAsPrefabAsset(root, prefabPath);

            summary = $"profile={profile.name}, seed={usedSeed}, grid={mazeGrid.Width}x{mazeGrid.Height}, stars={starGridCells.Count}, coins={coinGridCells.Count}, mechanics={mechanicsPlaced}";
            return starGridCells.Count >= 3 && coinGridCells.Count >= 5;
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    private static MazeDifficultyProfile ResolveProfileForLevel(int levelNumber, OverhaulAssets assets)
    {
        if (levelNumber % 5 == 0)
            return assets.MasterProfile;

        int waveSlot = (levelNumber - 1) % 5;
        return waveSlot <= 1 ? assets.LearnProfile : assets.NavigateProfile;
    }

    private static bool TryGenerateValidSession(
        int levelNumber,
        MazeDifficultyProfile profile,
        MazePatternLibrary library,
        out MazePreviewSession session,
        out int usedSeed)
    {
        session = new MazePreviewSession();
        int baseSeed = 101000 + levelNumber;
        for (int offset = 0; offset < 30; offset++)
        {
            int seed = baseSeed + offset * 37;
            MazeGenerationResult result = session.GeneratePreview(new MazeGenerationRequest
            {
                Seed = seed,
                Profile = profile,
                Library = library,
            });

            if (result.Success
                && session.CanApply
                && session.PreviewGrid != null
                && session.Validation?.SlideGraph != null
                && session.Validation.SlideGraph.IsSolvable
                && session.Validation.SlideGraph.TraversedFloorCells.Count >= 12)
            {
                usedSeed = seed;
                return true;
            }
        }

        usedSeed = baseSeed;
        return false;
    }

    private static void ClearOldGameplayObjects(GameObject root)
    {
        var toDestroy = new HashSet<GameObject>();
        foreach (Collectible c in root.GetComponentsInChildren<Collectible>(true))
            if (c != null) toDestroy.Add(c.gameObject);
        foreach (CrackedBlock cb in root.GetComponentsInChildren<CrackedBlock>(true))
            if (cb != null) toDestroy.Add(cb.gameObject);
        foreach (MazeTile mt in root.GetComponentsInChildren<MazeTile>(true))
            if (mt != null) toDestroy.Add(mt.gameObject);
        foreach (Hazard hz in root.GetComponentsInChildren<Hazard>(true))
            if (hz != null) toDestroy.Add(hz.gameObject);

        foreach (GameObject go in toDestroy)
        {
            if (go != null)
                UnityEngine.Object.DestroyImmediate(go);
        }

        DestroyNamedContainerIfPresent(root, "Collectibles");
        DestroyNamedContainerIfPresent(root, "Mechanics");
        DestroyNamedContainerIfPresent(root, "AutoCoins");
    }

    private static void DestroyNamedContainerIfPresent(GameObject root, string childName)
    {
        Transform t = root.transform.Find(childName);
        if (t != null)
            UnityEngine.Object.DestroyImmediate(t.gameObject);
    }

    private static void EnsureTilemapsConfigured(
        GameObject root,
        out Grid grid,
        out Tilemap wallTilemap,
        out Tilemap backgroundTilemap)
    {
        grid = root.GetComponentInChildren<Grid>(includeInactive: true);
        wallTilemap = null;
        backgroundTilemap = null;

        if (grid == null)
            return;

        int wallLayer = LayerMask.NameToLayer("Wall");
        int bgLayer = LayerMask.NameToLayer("Background");

        Tilemap[] tilemaps = grid.GetComponentsInChildren<Tilemap>(includeInactive: true);
        for (int i = 0; i < tilemaps.Length; i++)
        {
            Tilemap tm = tilemaps[i];
            if (tm == null) continue;

            if (wallTilemap == null && (tm.gameObject.layer == wallLayer || tm.name.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0))
                wallTilemap = tm;
            else if (backgroundTilemap == null && (tm.gameObject.layer == bgLayer || tm.name.IndexOf("Background", StringComparison.OrdinalIgnoreCase) >= 0))
                backgroundTilemap = tm;
        }

        if (wallTilemap != null)
        {
            if (wallLayer >= 0) wallTilemap.gameObject.layer = wallLayer;
            TilemapRenderer wallRenderer = wallTilemap.GetComponent<TilemapRenderer>();
            if (wallRenderer != null) wallRenderer.sortingOrder = 0;
        }

        if (backgroundTilemap == null)
        {
            var bgGo = new GameObject("BackgroundTilemap");
            bgGo.transform.SetParent(grid.transform, false);
            backgroundTilemap = bgGo.AddComponent<Tilemap>();
            bgGo.AddComponent<TilemapRenderer>();
        }

        if (bgLayer >= 0) backgroundTilemap.gameObject.layer = bgLayer;
        TilemapRenderer bgRenderer = backgroundTilemap.GetComponent<TilemapRenderer>();
        if (bgRenderer != null) bgRenderer.sortingOrder = -2;
    }

    private static MazeGrid ExtractMazeGridFromTilemap(Tilemap wallTilemap, PlayerSpawnPoint spawn, LevelExit exit)
    {
        BoundsInt bounds = wallTilemap.cellBounds;
        var grid = new MazeGrid(Mathf.Max(1, bounds.size.x), Mathf.Max(1, bounds.size.y));
        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                var cell = new Vector3Int(bounds.xMin + gx, bounds.yMin + gy, 0);
                grid.SetCell(gx, gy, wallTilemap.HasTile(cell) ? MazeCell.Wall : MazeCell.Floor);
            }
        }

        if (spawn != null)
        {
            Vector3Int s = wallTilemap.WorldToCell(spawn.transform.position);
            grid.SetSpawn(new Vector2Int(s.x - bounds.xMin, s.y - bounds.yMin));
        }
        if (exit != null)
        {
            Vector3Int e = wallTilemap.WorldToCell(exit.transform.position);
            grid.SetExit(new Vector2Int(e.x - bounds.xMin, e.y - bounds.yMin));
        }

        return grid;
    }

    private static HashSet<Vector2Int> CollectSolutionCells(MazeSlideGraph graph)
    {
        var cells = new HashSet<Vector2Int>(graph.SolutionStops);
        if (graph.SolutionStops.Count < 2)
            return cells;

        for (int i = 0; i < graph.SolutionStops.Count - 1 && i < graph.SolutionDirections.Count; i++)
        {
            Vector2Int from = graph.SolutionStops[i];
            Vector2Int to = graph.SolutionStops[i + 1];
            MazeDir dir = graph.SolutionDirections[i];
            if (!graph.OutgoingByStop.TryGetValue(from, out List<MazeSlideEdge> outgoing))
                continue;

            for (int e = 0; e < outgoing.Count; e++)
            {
                if (outgoing[e].Direction != dir || outgoing[e].To != to || outgoing[e].TraversedCells == null)
                    continue;

                for (int t = 0; t < outgoing[e].TraversedCells.Length; t++)
                    cells.Add(outgoing[e].TraversedCells[t]);
                break;
            }
        }

        return cells;
    }

    private static List<Vector2Int> SelectStarCells(
        MazeSlideGraph graph,
        HashSet<Vector2Int> solutionCells,
        HashSet<Vector2Int> excluded,
        int targetCount)
    {
        var chosen = new List<Vector2Int>(targetCount);
        var chosenSet = new HashSet<Vector2Int>(excluded);

        var detourStops = new List<Vector2Int>();
        var solutionStops = new List<Vector2Int>();
        foreach (Vector2Int stop in graph.ReachableStops)
        {
            if (chosenSet.Contains(stop)) continue;
            if (solutionCells.Contains(stop))
                solutionStops.Add(stop);
            else
                detourStops.Add(stop);
        }

        AddBestSpacedCandidates(detourStops, chosen, chosenSet, targetCount, minGap: 3);
        AddBestSpacedCandidates(solutionStops, chosen, chosenSet, targetCount, minGap: 2);

        if (chosen.Count < targetCount)
        {
            var fallback = CollectAvailableFloorCandidates(graph, chosenSet);
            AddBestSpacedCandidates(fallback, chosen, chosenSet, targetCount, minGap: 1);
        }

        return chosen;
    }

    private static List<Vector2Int> CollectAvailableFloorCandidates(MazeSlideGraph graph, HashSet<Vector2Int> excluded)
    {
        var list = new List<Vector2Int>();
        foreach (Vector2Int cell in graph.TraversedFloorCells)
        {
            if (!excluded.Contains(cell))
                list.Add(cell);
        }
        list.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));
        return list;
    }

    private static void AddBestSpacedCandidates(
        List<Vector2Int> pool,
        List<Vector2Int> chosen,
        HashSet<Vector2Int> chosenSet,
        int targetCount,
        int minGap)
    {
        if (pool.Count == 0 || chosen.Count >= targetCount)
            return;

        // Deterministic even spread across the sorted pool
        for (int pass = minGap; pass >= 1 && chosen.Count < targetCount; pass--)
        {
            for (int i = 0; i < pool.Count && chosen.Count < targetCount; i++)
            {
                Vector2Int c = pool[i];
                if (chosenSet.Contains(c)) continue;
                bool tooClose = false;
                for (int j = 0; j < chosen.Count; j++)
                {
                    if (Mathf.Abs(c.x - chosen[j].x) + Mathf.Abs(c.y - chosen[j].y) < pass)
                    {
                        tooClose = true;
                        break;
                    }
                }
                if (tooClose) continue;

                chosen.Add(c);
                chosenSet.Add(c);
            }
        }
    }

    private static List<T> SelectSpacedCells<T>(List<T> sortedCandidates, int targetCount, int minManhattanGap) where T : struct
    {
        var selected = new List<T>(targetCount);
        if (sortedCandidates.Count == 0 || targetCount <= 0)
            return selected;

        if (sortedCandidates.Count <= targetCount)
        {
            selected.AddRange(sortedCandidates);
            return selected;
        }

        float step = (float)sortedCandidates.Count / targetCount;
        var usedIndices = new HashSet<int>();
        for (int k = 0; k < targetCount; k++)
        {
            int idx = Mathf.Clamp(Mathf.FloorToInt(k * step + step * 0.5f), 0, sortedCandidates.Count - 1);
            while (usedIndices.Contains(idx) && idx + 1 < sortedCandidates.Count)
                idx++;

            if (usedIndices.Add(idx))
                selected.Add(sortedCandidates[idx]);
        }

        if (selected.Count < targetCount)
        {
            for (int i = 0; i < sortedCandidates.Count && selected.Count < targetCount; i++)
            {
                if (!usedIndices.Contains(i))
                    selected.Add(sortedCandidates[i]);
            }
        }

        return selected;
    }

    private static int PlaceWaveMechanics(
        int levelNumber,
        OverhaulAssets assets,
        Transform rootTransform,
        Grid grid,
        Tilemap wallTilemap,
        MazeGrid mazeGrid,
        MazeSlideGraph graph,
        Vector2Int origin,
        HashSet<Vector2Int> solutionCells,
        HashSet<Vector2Int> occupiedFloorCells)
    {
        if (levelNumber < 6)
            return 0;

        Transform mechanicsParent = new GameObject("Mechanics").transform;
        mechanicsParent.SetParent(rootTransform, false);
        int placed = 0;

        bool wantsCracked = (levelNumber >= 6 && levelNumber <= 10) || levelNumber >= 16;
        bool wantsBrittle = levelNumber >= 11;
        bool wantsFailing = levelNumber >= 16;

        if (wantsCracked && assets.CrackedPrefab != null)
        {
            int targetCracked = (levelNumber <= 7 || levelNumber == 16) ? 1 : 2;
            List<Vector2Int> crackedWalls = FindCrackedWallShortcutCells(mazeGrid, graph, targetCracked);
            for (int i = 0; i < crackedWalls.Count; i++)
            {
                Vector3Int tCell = GridToTileCell(crackedWalls[i], origin);
                wallTilemap.SetTile(tCell, null);
                InstantiatePrefabCenteredAtCell(assets.CrackedPrefab, mechanicsParent, grid, wallTilemap, tCell, $"CrackedWall_{i + 1:D2}", applyColliderOffset: true);
                placed++;
            }
        }

        var offSolutionFloor = new List<Vector2Int>();
        foreach (Vector2Int c in graph.TraversedFloorCells)
        {
            if (!solutionCells.Contains(c) && !occupiedFloorCells.Contains(c))
                offSolutionFloor.Add(c);
        }
        offSolutionFloor.Sort((a, b) => a.y != b.y ? a.y.CompareTo(b.y) : a.x.CompareTo(b.x));

        if (wantsBrittle && assets.BrittlePrefab != null && offSolutionFloor.Count > 0)
        {
            int targetBrittle = levelNumber >= 13 ? 2 : 1;
            var acceptedBrittle = new List<Vector2Int>(targetBrittle);
            try
            {
                for (int i = 0; i < offSolutionFloor.Count && acceptedBrittle.Count < targetBrittle; i += 2)
                {
                    Vector2Int cell = offSolutionFloor[i];
                    if (!IsSafePostCollapseCell(mazeGrid, graph, cell))
                        continue;

                    mazeGrid.SetCell(cell.x, cell.y, MazeCell.Wall);
                    acceptedBrittle.Add(cell);
                    occupiedFloorCells.Add(cell);
                    InstantiatePrefabCenteredAtCell(assets.BrittlePrefab, mechanicsParent, grid, wallTilemap, GridToTileCell(cell, origin), $"Brittle_{acceptedBrittle.Count:D2}", applyColliderOffset: true);
                    placed++;
                }
            }
            finally
            {
                for (int i = 0; i < acceptedBrittle.Count; i++)
                    mazeGrid.SetCell(acceptedBrittle[i].x, acceptedBrittle[i].y, MazeCell.Floor);
            }
        }

        if (wantsFailing && assets.FailingTilePrefab != null)
        {
            int targetFailing = levelNumber >= 18 ? 2 : 1;
            var acceptedFailing = new List<Vector2Int>(targetFailing);
            try
            {
                for (int i = offSolutionFloor.Count - 1; i >= 0 && acceptedFailing.Count < targetFailing; i--)
                {
                    Vector2Int cell = offSolutionFloor[i];
                    if (occupiedFloorCells.Contains(cell)) continue;
                    if (!IsSafePostCollapseCell(mazeGrid, graph, cell))
                        continue;

                    mazeGrid.SetCell(cell.x, cell.y, MazeCell.Wall);
                    acceptedFailing.Add(cell);
                    occupiedFloorCells.Add(cell);
                    InstantiatePrefabCenteredAtCell(assets.FailingTilePrefab, mechanicsParent, grid, wallTilemap, GridToTileCell(cell, origin), $"FailingTile_{acceptedFailing.Count:D2}", applyColliderOffset: true);
                    placed++;
                }
            }
            finally
            {
                for (int i = 0; i < acceptedFailing.Count; i++)
                    mazeGrid.SetCell(acceptedFailing[i].x, acceptedFailing[i].y, MazeCell.Floor);
            }
        }

        if (placed == 0)
            UnityEngine.Object.DestroyImmediate(mechanicsParent.gameObject);

        return placed;
    }

    private static bool IsSafePostCollapseCell(MazeGrid mazeGrid, MazeSlideGraph originalGraph, Vector2Int cell)
    {
        if (!mazeGrid.IsFloor(cell))
            return false;

        mazeGrid.SetCell(cell.x, cell.y, MazeCell.Wall);
        try
        {
            MazeValidationReport report = MazeValidator.Validate(mazeGrid, requireExitOnStopCell: false);
            if (!report.IsValid)
                return false;

            foreach (MazeSlideEdge edge in originalGraph.Edges)
            {
                if (edge.TraversedCells == null || !ArrayContainsCell(edge.TraversedCells, cell))
                    continue;

                if (edge.To == cell || !mazeGrid.IsFloor(edge.To))
                    return false;

                MazeSlideGraph landingGraph = MazeSlideGraph.Build(mazeGrid, overrideStart: edge.To, requireExitOnStopCell: false);
                if (!landingGraph.IsSolvable || landingGraph.SoftLockedStops.Count > 0)
                    return false;
            }

            return true;
        }
        finally
        {
            mazeGrid.SetCell(cell.x, cell.y, MazeCell.Floor);
        }
    }

    private static bool ArrayContainsCell(Vector2Int[] cells, Vector2Int target)
    {
        for (int i = 0; i < cells.Length; i++)
        {
            if (cells[i] == target)
                return true;
        }
        return false;
    }

    private static List<Vector2Int> FindCrackedWallShortcutCells(MazeGrid mazeGrid, MazeSlideGraph graph, int maxCount)
    {
        var results = new List<Vector2Int>(maxCount);
        var traversed = new HashSet<Vector2Int>(graph.TraversedFloorCells);

        try
        {
            for (int x = 1; x < mazeGrid.Width - 1 && results.Count < maxCount; x++)
            {
                for (int y = 1; y < mazeGrid.Height - 1 && results.Count < maxCount; y++)
                {
                    var c = new Vector2Int(x, y);
                    if (!mazeGrid.IsWall(c)) continue;

                    bool horizBridge = traversed.Contains(c + Vector2Int.left) && traversed.Contains(c + Vector2Int.right);
                    bool vertBridge = traversed.Contains(c + Vector2Int.up) && traversed.Contains(c + Vector2Int.down);
                    if (!horizBridge && !vertBridge) continue;

                    mazeGrid.SetCell(c.x, c.y, MazeCell.Floor);
                    MazeValidationReport smashedReport = MazeValidator.Validate(mazeGrid, requireExitOnStopCell: false);
                    if (smashedReport.IsValid)
                        results.Add(c);
                    else
                        mazeGrid.SetCell(c.x, c.y, MazeCell.Wall);
                }
            }
        }
        finally
        {
            for (int i = 0; i < results.Count; i++)
                mazeGrid.SetCell(results[i].x, results[i].y, MazeCell.Wall);
        }

        return results;
    }

    private static void InstantiatePrefabCenteredAtCell(
        GameObject prefab,
        Transform parent,
        Grid grid,
        Tilemap fallbackTilemap,
        Vector3Int cell,
        string objectName,
        bool applyColliderOffset)
    {
        if (prefab == null)
            return;

        GameObject instance = PrefabUtility.InstantiatePrefab(prefab, parent) as GameObject;
        if (instance == null)
            return;

        instance.name = objectName;
        Vector3 pos = MazeApplyService.CellCenterToWorld(grid, fallbackTilemap, cell, 0f);
        if (applyColliderOffset)
        {
            BoxCollider2D box = instance.GetComponent<BoxCollider2D>();
            if (box != null && Mathf.Abs(box.offset.y) > 0.25f)
            {
                bool hasChildHazard = instance.GetComponentInChildren<Hazard>(true) != null;
                float maxShiftY = hasChildHazard ? 0.30f : 0.485f;
                pos.y -= Mathf.Clamp(box.offset.y, -maxShiftY, maxShiftY);
                pos.x -= Mathf.Clamp(box.offset.x, -0.45f, 0.45f);
            }
        }

        instance.transform.position = pos;
    }

    private static Vector3Int GridToTileCell(Vector2Int gridCell, Vector2Int origin) =>
        new Vector3Int(gridCell.x + origin.x, gridCell.y + origin.y, 0);

    private static void EnsureStarOptionalObjective(LevelData data, int requiredStars)
    {
        if (data == null)
            return;

        int clamped = Mathf.Clamp(requiredStars, 1, 3);
        data.optionalObjectives = new[]
        {
            new ObjectiveDefinition
            {
                objectiveType = ObjectiveType.CollectPickups,
                requiredCount = clamped,
                description = "Collect bonus stars along the route",
            }
        };
        EditorUtility.SetDirty(data);
    }

    private static int ValidateCampaignDatabase()
    {
        CampaignDatabase db = AssetDatabase.LoadAssetAtPath<CampaignDatabase>(CampaignDbPath);
        if (db == null)
        {
            Debug.LogError($"[CampaignLevelOverhaulTool] Missing '{CampaignDbPath}'.");
            return 1;
        }

        int errors = 0;
        for (int w = 0; w < db.Worlds.Count; w++)
        {
            IReadOnlyList<LevelData> levels = db.Worlds[w].levels;
            if (levels == null) continue;
            for (int i = 0; i < levels.Count; i++)
            {
                LevelData levelData = levels[i];
                List<LevelDataValidator.ValidationCheck> checks = LevelDataValidator.Validate(levelData);
                for (int c = 0; c < checks.Count; c++)
                {
                    if (checks[c].Result == LevelDataValidator.CheckResult.Error)
                    {
                        errors++;
                        Debug.LogError($"[LevelDataValidator] {levelData.LevelId} [{checks[c].Label}]: {checks[c].Message}", levelData);
                    }
                }
            }
        }

        return errors;
    }
}
