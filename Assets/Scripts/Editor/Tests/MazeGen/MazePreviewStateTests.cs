using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Phase 4 EditMode tests for <see cref="MazePreviewSession"/> and <see cref="MazeGeneratorWindow"/>:
/// verifies preview generation, brush painting, stale validation gating (<c>CanApply == false</c>),
/// outer-perimeter locking, brush stroke Undo/Redo (including branching revision collision immunity),
/// <see cref="MazeMapData"/> round-trip loading, and zero scene dirtiness.
/// </summary>
[TestFixture]
public class MazePreviewStateTests
{
    private static MazePatternLibrary LoadLibrary()
    {
        var lib = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        Assert.IsNotNull(lib, "Expected DefaultMazePatternLibrary.asset to exist.");
        return lib;
    }

    private static MazeDifficultyProfile LoadProfile(string name)
    {
        var profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
            $"{MazePatternBootstrap.ProfilesFolder}/Profile_{name}.asset");
        Assert.IsNotNull(profile, $"Expected Profile_{name}.asset to exist.");
        return profile;
    }

    [Test]
    public void GeneratePreview_PopulatesValidState_WithoutSceneDirtiness()
    {
        bool wasDirtyBefore = SceneManager.GetActiveScene().isDirty;

        var session = new MazePreviewSession();
        Assert.AreEqual(MazePreviewStatus.Empty, session.Status);
        Assert.IsFalse(session.HasPreview);
        Assert.IsFalse(session.CanApply);

        MazeDifficultyProfile navigate = LoadProfile("Navigate");
        var request = new MazeGenerationRequest
        {
            Seed = 104729,
            Profile = navigate,
            Library = LoadLibrary(),
        };

        MazeGenerationResult result = session.GeneratePreview(in request);
        Assert.IsTrue(result.Success, result.FailureReason);
        Assert.AreEqual(MazePreviewStatus.Valid, session.Status);
        Assert.IsTrue(session.HasPreview);
        Assert.IsFalse(session.IsValidationStale);
        Assert.IsTrue(session.CanApply);
        Assert.AreEqual(15, session.PlacedModules.Count, "3x5 Navigate map should record 15 placed modules.");
        Assert.IsTrue(session.TryGetPlacedModuleAtCell(2, 2, out MazePlacedModule slot00));
        Assert.AreEqual(0, slot00.SlotX);
        Assert.AreEqual(0, slot00.SlotY);
        Assert.IsNotEmpty(slot00.PatternId);

        Assert.AreEqual(wasDirtyBefore, SceneManager.GetActiveScene().isDirty, "GeneratePreview must never dirty the open scene.");
    }

    [Test]
    public void PaintCell_ImmediatelyMarksValidationStale_AndLocksCanApply()
    {
        var session = new MazePreviewSession();
        MazeDifficultyProfile learn = LoadProfile("Learn");
        var request = new MazeGenerationRequest
        {
            Seed = 424242,
            Profile = learn,
            Library = LoadLibrary(),
        };

        session.GeneratePreview(in request);
        Assert.IsTrue(session.CanApply);

        int revBefore = session.PreviewGrid.Revision;
        Vector2Int spawnCell = session.PreviewGrid.Spawn.Value;

        // Painting Wall over Spawn clears Spawn, bumps Revision, marks Stale, and locks CanApply
        session.BeginPaintStroke();
        bool changed = session.PaintCell(spawnCell.x, spawnCell.y, MazePaintBrush.Wall);
        session.EndPaintStroke();

        Assert.IsTrue(changed);
        Assert.Greater(session.PreviewGrid.Revision, revBefore);
        Assert.IsNull(session.PreviewGrid.Spawn, "Painting Wall over Spawn must clear the Spawn marker.");
        Assert.IsTrue(session.IsValidationStale);
        Assert.AreEqual(MazePreviewStatus.Stale, session.Status);
        Assert.IsFalse(session.CanApply, "CanApply must immediately lock whenever validation is stale.");
        Assert.IsNull(session.CreateValidatedMapData("Learn", 1), "CreateValidatedMapData must refuse stale previews.");

        // Revalidating without Spawn transitions Stale -> Invalid (MissingSpawn) and keeps CanApply locked
        MazeValidationReport invalidReport = session.Revalidate(learn);
        Assert.IsFalse(session.IsValidationStale);
        Assert.IsFalse(invalidReport.IsValid);
        Assert.IsTrue(invalidReport.HasIssue(MazeValidationCode.MissingSpawn));
        Assert.AreEqual(MazePreviewStatus.Invalid, session.Status);
        Assert.IsFalse(session.CanApply);

        // Restoring Spawn with the Spawn brush marks Stale again until Revalidate is clicked
        session.BeginPaintStroke();
        Assert.IsTrue(session.PaintCell(spawnCell.x, spawnCell.y, MazePaintBrush.Spawn));
        session.EndPaintStroke();
        Assert.AreEqual(MazePreviewStatus.Stale, session.Status);
        Assert.IsFalse(session.CanApply);

        MazeValidationReport validReport = session.Revalidate(learn);
        Assert.IsTrue(validReport.IsValid);
        Assert.AreEqual(MazePreviewStatus.Valid, session.Status);
        Assert.IsTrue(session.CanApply);
    }

    [Test]
    public void LockOuterBorder_BlocksPerimeterCarving_AndUnlockFlagsOpenBoundary()
    {
        var session = new MazePreviewSession();
        MazeDifficultyProfile learn = LoadProfile("Learn");
        var request = new MazeGenerationRequest
        {
            Seed = 777001,
            Profile = learn,
            Library = LoadLibrary(),
        };

        session.GeneratePreview(in request);
        Assert.IsTrue(session.LockOuterBorder, "Outer perimeter lock should be enabled by default.");

        int revBefore = session.PreviewGrid.Revision;
        Assert.IsFalse(session.PaintCell(0, 2, MazePaintBrush.Floor));
        Assert.IsFalse(session.PaintCell(0, 2, MazePaintBrush.Spawn));
        Assert.IsFalse(session.PaintCell(0, 2, MazePaintBrush.Exit));
        Assert.AreEqual(revBefore, session.PreviewGrid.Revision);
        Assert.IsTrue(session.CanApply);

        // Unlocking perimeter allows carving Floor on the border, which Revalidate catches as OpenBoundary
        session.LockOuterBorder = false;
        session.BeginPaintStroke();
        Assert.IsTrue(session.PaintCell(0, 2, MazePaintBrush.Floor));
        session.EndPaintStroke();

        Assert.AreEqual(MazePreviewStatus.Stale, session.Status);
        Assert.IsFalse(session.CanApply);

        MazeValidationReport report = session.Revalidate(learn);
        Assert.IsFalse(report.IsValid);
        Assert.IsTrue(report.HasIssue(MazeValidationCode.OpenBoundary));
        Assert.AreEqual(MazePreviewStatus.Invalid, session.Status);
        Assert.IsFalse(session.CanApply);
    }

    [Test]
    public void UndoAndRedo_AcrossRevalidateAndBranchingEdits_NeverSuffersRevisionCollision()
    {
        var session = new MazePreviewSession();
        MazeDifficultyProfile navigate = LoadProfile("Navigate");
        var request = new MazeGenerationRequest
        {
            Seed = 900101,
            Profile = navigate,
            Library = LoadLibrary(),
        };

        session.GeneratePreview(in request);
        Assert.IsTrue(session.CanApply);
        string[] originalAscii = session.PreviewGrid.ToAsciiLines();

        // Find two distinct interior Wall cells to test branching edits at the exact same Revision number
        Vector2Int wallA = new Vector2Int(-1, -1);
        Vector2Int wallB = new Vector2Int(-1, -1);
        for (int gx = 1; gx < session.PreviewGrid.Width - 1; gx++)
        {
            for (int gy = 1; gy < session.PreviewGrid.Height - 1; gy++)
            {
                if (session.PreviewGrid.GetCell(gx, gy) == MazeCell.Wall)
                {
                    if (wallA.x < 0) wallA = new Vector2Int(gx, gy);
                    else if (wallB.x < 0) { wallB = new Vector2Int(gx, gy); break; }
                }
            }
            if (wallB.x >= 0) break;
        }
        Assert.GreaterOrEqual(wallB.x, 0);

        // Edit Branch A -> Revalidate (stamping Validation at Revision = R0 + 1)
        session.BeginPaintStroke();
        Assert.IsTrue(session.PaintCell(wallA.x, wallA.y, MazePaintBrush.Floor));
        session.EndPaintStroke();
        session.Revalidate(navigate);
        Assert.IsFalse(session.IsValidationStale);

        // Undo back to R0 -> must restore exact original grid and Valid status
        Assert.IsTrue(session.UndoPreviewEdit(navigate));
        CollectionAssert.AreEqual(originalAscii, session.PreviewGrid.ToAsciiLines());
        Assert.IsFalse(session.IsValidationStale);
        Assert.IsTrue(session.CanApply);
        Assert.IsFalse(session.HasManualEdits);

        // Redo -> restores Branch A with its freshly validated state, then Undo again
        Assert.IsTrue(session.RedoPreviewEdit(navigate));
        Assert.AreEqual(MazeCell.Floor, session.PreviewGrid.GetCell(wallA));
        Assert.IsFalse(session.IsValidationStale);
        Assert.IsTrue(session.UndoPreviewEdit(navigate));

        // Now paint Branch B (also reaches Revision = R0 + 1!) -> MUST be Stale, NOT collide with Branch A's validation!
        session.BeginPaintStroke();
        Assert.IsTrue(session.PaintCell(wallB.x, wallB.y, MazePaintBrush.Floor));
        session.EndPaintStroke();

        Assert.IsTrue(session.IsValidationStale, "Branching edit after Undo must never collide with discarded branch's validation revision.");
        Assert.AreEqual(MazePreviewStatus.Stale, session.Status);
        Assert.IsFalse(session.CanApply);
        Assert.IsFalse(session.CanRedo, "New edit must clear Redo stack.");
    }

    [Test]
    public void LoadFromMapData_AndWindowLifecycle_InitializeCleanlyWithoutSceneMutation()
    {
        bool wasDirtyBefore = SceneManager.GetActiveScene().isDirty;
        MazeDifficultyProfile master = LoadProfile("Master");

        var sourceSession = new MazePreviewSession();
        var request = new MazeGenerationRequest
        {
            Seed = 515151,
            Profile = master,
            Library = LoadLibrary(),
        };
        sourceSession.GeneratePreview(in request);
        Assert.IsTrue(sourceSession.CanApply);

        MazeMapData tempMap = sourceSession.CreateValidatedMapData(master.profileId, 1);
        Assert.IsNotNull(tempMap);

        MazeGeneratorWindow window = null;
        try
        {
            var targetSession = new MazePreviewSession();
            Assert.IsTrue(targetSession.LoadFromMapData(tempMap, master));
            Assert.AreEqual(MazePreviewStatus.Valid, targetSession.Status);
            Assert.IsTrue(targetSession.CanApply);
            CollectionAssert.AreEqual(
                sourceSession.PreviewGrid.ToAsciiLines(),
                targetSession.PreviewGrid.ToAsciiLines());

            window = ScriptableObject.CreateInstance<MazeGeneratorWindow>();
            window.EnsureDefaultAssetsLoaded();
            Assert.IsNotNull(window.PatternLibrary);
            Assert.IsNotNull(window.ActiveProfile);

            MazeGenerationResult winResult = window.GenerateCurrentPreview();
            Assert.IsTrue(winResult.Success);
            Assert.IsTrue(window.Session.CanApply);

            Vector2Int exitCell = window.Session.PreviewGrid.Exit.Value;
            window.Session.BeginPaintStroke();
            window.Session.PaintCell(exitCell.x, exitCell.y, MazePaintBrush.Wall);
            window.Session.EndPaintStroke();
            Assert.IsFalse(window.Session.CanApply);

            MazeValidationReport report = window.ValidateCurrentPreview();
            Assert.IsFalse(report.IsValid);
            Assert.IsTrue(report.HasIssue(MazeValidationCode.MissingExit));
        }
        finally
        {
            if (window != null)
                Object.DestroyImmediate(window);
            if (tempMap != null)
                Object.DestroyImmediate(tempMap);
        }

        Assert.AreEqual(wasDirtyBefore, SceneManager.GetActiveScene().isDirty, "Window & session preview lifecycle must not dirty the scene.");
    }
}

