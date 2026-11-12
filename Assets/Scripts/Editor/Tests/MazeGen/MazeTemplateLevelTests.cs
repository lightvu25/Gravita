using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Phase 5b EditMode tests for <see cref="MazeApplyService.CreateLevelFromTemplate"/>:
/// verifies isolated cloning from <c>Level 1.prefab</c>, gameplay-extra stripping, exact <c>.5</c>
/// Spawn/Exit snapping, <see cref="LevelDataValidator"/> compliance, GUID-preserving overwrite,
/// and strict non-mutation of <c>Level 1.prefab</c>, <c>Level_001.asset</c>, and <c>CampaignDatabase</c>.
/// </summary>
[TestFixture]
public class MazeTemplateLevelTests
{
    private const string TempFolder = "Assets/Data/MazeGen/Maps/__TempPhase5b";

    [TearDown]
    public void TearDown()
    {
        if (AssetDatabase.IsValidFolder(TempFolder))
            AssetDatabase.DeleteAsset(TempFolder);
    }

    private static MazePatternLibrary LoadLibrary() =>
        AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);

    private static MazeDifficultyProfile LoadProfile(string name) =>
        AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>($"{MazePatternBootstrap.ProfilesFolder}/Profile_{name}.asset");

    [Test]
    public void CreateLevelFromTemplate_ClonesTemplate_PassesLevelDataValidator_StripsExtras_AndLeavesCampaignAndLevel1Untouched()
    {
        GameObject templatePrefab = MazeApplyService.LoadDefaultTemplatePrefab();
        Assert.IsNotNull(templatePrefab, "Expected default template prefab at Assets/Prefabs/Levels/Level 1.prefab.");

        // Snapshot Level 1.prefab before running CreateLevelFromTemplate (Q7: never modify Level 1)
        PlayerSpawnPoint origSpawn = templatePrefab.GetComponentInChildren<PlayerSpawnPoint>(true);
        LevelExit origExit = templatePrefab.GetComponentInChildren<LevelExit>(true);
        int origCollectibleCount = templatePrefab.GetComponentsInChildren<Collectible>(true).Length;
        Vector3 origSpawnPos = origSpawn != null ? origSpawn.transform.position : Vector3.zero;
        Vector3 origExitPos = origExit != null ? origExit.transform.position : Vector3.zero;
        Assert.Greater(origCollectibleCount, 0, "Level 1.prefab should contain stars/collectibles before stripping.");

        // Snapshot all CampaignDatabase assets before running CreateLevelFromTemplate (Q4: never auto-modify CampaignDatabase)
        string[] campaignGuids = AssetDatabase.FindAssets("t:CampaignDatabase");
        var campaignJsonBefore = new Dictionary<string, string>();
        for (int i = 0; i < campaignGuids.Length; i++)
        {
            string path = AssetDatabase.GUIDToAssetPath(campaignGuids[i]);
            CampaignDatabase db = AssetDatabase.LoadAssetAtPath<CampaignDatabase>(path);
            if (db != null)
                campaignJsonBefore[path] = EditorJsonUtility.ToJson(db);
        }

        var session = new MazePreviewSession();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");
        MazeGenerationResult gen = session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 515253,
            Profile = navigate,
            Library = LoadLibrary(),
        });
        Assert.IsTrue(gen.Success, gen.FailureReason);
        Assert.IsTrue(session.CanApply);

        MazeTemplateLevelRequest req = MazeTemplateLevelRequest.CreateDefault();
        req.TemplatePrefab = templatePrefab;
        req.LevelAssetName = "Level_GenTest_01";
        req.DisplayName = "Generated Test Sector 01";
        req.WorldIndex = 2;
        req.Profile = navigate;
        req.PatternLibraryVersion = 1;
        req.CenterAroundOrigin = true;
        req.StripTemplateCollectiblesAndMechanics = true;
        req.SaveCompanionMapData = true;
        req.AllowOverwrite = false;
        req.CustomPrefabFolder = TempFolder;
        req.CustomLevelDataFolder = TempFolder;
        req.CustomMapDataFolder = TempFolder;

        MazeTemplateLevelResult res = MazeApplyService.CreateLevelFromTemplate(session, in req);
        Assert.IsTrue(res.Success, res.ErrorMessage);
        Assert.IsNotNull(res.CreatedPrefab);
        Assert.IsNotNull(res.CreatedLevelData);
        Assert.IsNotNull(res.CreatedMapData);
        Assert.IsNotEmpty(res.ManualCampaignReminder);

        // 1. Verify cloned prefab structure & stripped template collectibles
        Assert.IsNotNull(res.CreatedPrefab.GetComponent<LevelRoot>());
        Assert.AreEqual(
            0,
            res.CreatedPrefab.GetComponentsInChildren<Collectible>(true).Length,
            "Template collectibles/stars must be stripped when StripTemplateCollectiblesAndMechanics is true.");

        PlayerSpawnPoint newSpawn = res.CreatedPrefab.GetComponentInChildren<PlayerSpawnPoint>(true);
        LevelExit newExit = res.CreatedPrefab.GetComponentInChildren<LevelExit>(true);
        Assert.IsNotNull(newSpawn);
        Assert.IsNotNull(newExit);

        MazeGrid grid = session.PreviewGrid;
        Vector2Int origin = MazeApplyService.ResolveOriginOffset(grid, centerAroundOrigin: true, Vector2Int.zero);
        Vector2Int sp = grid.Spawn.Value;
        Vector2Int ex = grid.Exit.Value;
        Assert.AreEqual(new Vector3(origin.x + sp.x + 0.5f, origin.y + sp.y + 0.5f, 0f), newSpawn.transform.position);
        Assert.AreEqual(new Vector3(origin.x + ex.x + 0.5f, origin.y + ex.y + 0.5f, 0f), newExit.transform.position);

        // Verify WallTilemap & BackgroundTilemap cell contents in the saved prefab
        int wallLayer = LayerMask.NameToLayer("Wall");
        Tilemap savedWallTm = null;
        foreach (Tilemap tm in res.CreatedPrefab.GetComponentsInChildren<Tilemap>(true))
        {
            if (tm.gameObject.layer == wallLayer)
            {
                savedWallTm = tm;
                break;
            }
        }
        Assert.IsNotNull(savedWallTm);
        for (int gx = 0; gx < grid.Width; gx++)
        {
            for (int gy = 0; gy < grid.Height; gy++)
            {
                var c3 = new Vector3Int(origin.x + gx, origin.y + gy, 0);
                Assert.AreEqual(grid.GetCell(gx, gy) == MazeCell.Wall, savedWallTm.HasTile(c3));
            }
        }

        // 2. Verify created LevelData passes LevelDataValidator with 0 Errors and Pass on Grid Alignment & Maze Topology
        Assert.AreSame(res.CreatedPrefab, res.CreatedLevelData.LayoutPrefab);
        Assert.AreEqual("Level_GenTest_01", res.CreatedLevelData.LevelId);
        Assert.AreEqual(LevelDifficulty.Medium, res.CreatedLevelData.difficulty);
        Assert.AreEqual(ObjectiveType.ReachExit, res.CreatedLevelData.PrimaryObjective.objectiveType);

        List<LevelDataValidator.ValidationCheck> checks = LevelDataValidator.Validate(res.CreatedLevelData);
        bool sawGridAlignmentPass = false;
        bool sawMazeTopologyPass = false;
        foreach (LevelDataValidator.ValidationCheck check in checks)
        {
            Assert.AreNotEqual(
                LevelDataValidator.CheckResult.Error,
                check.Result,
                $"LevelDataValidator reported Error on [{check.Label}]: {check.Message}");

            if (check.Label == "Grid Center Alignment")
            {
                Assert.AreEqual(LevelDataValidator.CheckResult.Pass, check.Result, check.Message);
                sawGridAlignmentPass = true;
            }
            else if (check.Label == "Maze Slide Topology")
            {
                Assert.AreEqual(LevelDataValidator.CheckResult.Pass, check.Result, check.Message);
                sawMazeTopologyPass = true;
            }
        }
        Assert.IsTrue(sawGridAlignmentPass, "Expected Grid Center Alignment check to run and Pass.");
        Assert.IsTrue(sawMazeTopologyPass, "Expected Maze Slide Topology check to run and Pass.");

        // 3. Verify Level 1.prefab was NOT modified on disk (Q7)
        GameObject reloadedTemplate = MazeApplyService.LoadDefaultTemplatePrefab();
        Assert.AreEqual(origCollectibleCount, reloadedTemplate.GetComponentsInChildren<Collectible>(true).Length);
        Assert.AreEqual(origSpawnPos, reloadedTemplate.GetComponentInChildren<PlayerSpawnPoint>(true).transform.position);
        Assert.AreEqual(origExitPos, reloadedTemplate.GetComponentInChildren<LevelExit>(true).transform.position);

        // 4. Verify CampaignDatabase was NOT modified (Q4)
        foreach (KeyValuePair<string, string> kvp in campaignJsonBefore)
        {
            CampaignDatabase dbAfter = AssetDatabase.LoadAssetAtPath<CampaignDatabase>(kvp.Key);
            Assert.AreEqual(kvp.Value, EditorJsonUtility.ToJson(dbAfter), $"CampaignDatabase '{kvp.Key}' must never be auto-modified.");
        }
    }

    [Test]
    public void CreateLevelFromTemplate_EnforcesSafetyGates_StaleSession_OverwriteControl_AndProtectedLegacyAssets()
    {
        GameObject templatePrefab = MazeApplyService.LoadDefaultTemplatePrefab();
        Assert.IsNotNull(templatePrefab);

        var session = new MazePreviewSession();
        MazeDifficultyProfile learn = LoadProfile("Learn");
        session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 606060,
            Profile = learn,
            Library = LoadLibrary(),
        });
        Assert.IsTrue(session.CanApply);

        // 1. Attempting to overwrite TemplatePrefab itself or Level_001.asset MUST fail even with AllowOverwrite = true
        MazeTemplateLevelRequest overwriteTemplateReq = MazeTemplateLevelRequest.CreateDefault();
        overwriteTemplateReq.TemplatePrefab = templatePrefab;
        overwriteTemplateReq.LevelAssetName = "Level 1";
        overwriteTemplateReq.AllowOverwrite = true;
        MazeTemplateLevelResult overwriteTemplateRes = MazeApplyService.CreateLevelFromTemplate(session, in overwriteTemplateReq);
        Assert.IsFalse(overwriteTemplateRes.Success, "Must reject overwriting Level 1.prefab.");

        MazeTemplateLevelRequest overwriteLegacyDataReq = MazeTemplateLevelRequest.CreateDefault();
        overwriteLegacyDataReq.TemplatePrefab = templatePrefab;
        overwriteLegacyDataReq.LevelAssetName = "Level_001";
        overwriteLegacyDataReq.AllowOverwrite = true;
        MazeTemplateLevelResult overwriteLegacyDataRes = MazeApplyService.CreateLevelFromTemplate(session, in overwriteLegacyDataReq);
        Assert.IsFalse(overwriteLegacyDataRes.Success, "Must reject overwriting Level_001.asset.");

        // 2. Create initial level in TempFolder -> succeeds; second call with AllowOverwrite = false -> fails; AllowOverwrite = true -> updates in-place keeping GUIDs
        MazeTemplateLevelRequest req = MazeTemplateLevelRequest.CreateDefault();
        req.TemplatePrefab = templatePrefab;
        req.LevelAssetName = "Level_OverwriteTest";
        req.Profile = learn;
        req.CustomPrefabFolder = TempFolder;
        req.CustomLevelDataFolder = TempFolder;
        req.CustomMapDataFolder = TempFolder;
        req.AllowOverwrite = false;

        MazeTemplateLevelResult first = MazeApplyService.CreateLevelFromTemplate(session, in req);
        Assert.IsTrue(first.Success, first.ErrorMessage);
        string levelDataGuidBefore = AssetDatabase.AssetPathToGUID(first.LevelDataAssetPath);
        string mapDataGuidBefore = AssetDatabase.AssetPathToGUID(first.MapDataAssetPath);

        MazeTemplateLevelResult duplicateBlocked = MazeApplyService.CreateLevelFromTemplate(session, in req);
        Assert.IsFalse(duplicateBlocked.Success, "Duplicate path must fail when AllowOverwrite is false.");

        // Generate a new seed and overwrite with AllowOverwrite = true
        session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = 707070,
            Profile = learn,
            Library = LoadLibrary(),
        });
        req.AllowOverwrite = true;
        MazeTemplateLevelResult overwritten = MazeApplyService.CreateLevelFromTemplate(session, in req);
        Assert.IsTrue(overwritten.Success, overwritten.ErrorMessage);
        Assert.AreEqual(levelDataGuidBefore, AssetDatabase.AssetPathToGUID(overwritten.LevelDataAssetPath));
        Assert.AreEqual(mapDataGuidBefore, AssetDatabase.AssetPathToGUID(overwritten.MapDataAssetPath));
        Assert.AreEqual(707070, overwritten.CreatedMapData.seed);

        // 3. Stale session -> rejected
        Vector2Int sp = session.PreviewGrid.Spawn.Value;
        session.BeginPaintStroke();
        session.PaintCell(sp.x, sp.y, MazePaintBrush.Wall);
        session.EndPaintStroke();
        Assert.IsTrue(session.IsValidationStale);

        MazeTemplateLevelResult staleRes = MazeApplyService.CreateLevelFromTemplate(session, in req);
        Assert.IsFalse(staleRes.Success, "Stale session must be rejected by CreateLevelFromTemplate.");
    }
}