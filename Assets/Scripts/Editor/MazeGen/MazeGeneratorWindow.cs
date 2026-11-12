using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Interactive Editor tool (<c>Tools/Gravita/Maze Generator</c>) implementing the
/// <c>Generate -&gt; Preview -&gt; Paint -&gt; Validate -&gt; Apply -&gt; Save</c> workflow
/// over <see cref="MazePreviewSession"/> and <see cref="MazeApplyService"/>.
/// </summary>
public sealed class MazeGeneratorWindow : EditorWindow
{
    private const float LeftPaneWidth = 348f;
    private const float CanvasPadding = 14f;
    private const float FooterHeight = 24f;

    private static readonly string[] BrushLabels =
    {
        "Wall (#)",
        "Floor (.)",
        "Spawn (S)",
        "Exit (E)",
    };

    private static readonly Color CanvasBgColor        = new Color(0.07f, 0.09f, 0.12f, 1f);
    private static readonly Color PerimeterWallColor   = new Color(0.10f, 0.12f, 0.17f, 1f);
    private static readonly Color InteriorWallColor    = new Color(0.14f, 0.17f, 0.23f, 1f);
    private static readonly Color UntraversedFloorColor = new Color(0.20f, 0.23f, 0.28f, 1f);
    private static readonly Color TraversedFloorColor  = new Color(0.25f, 0.31f, 0.39f, 1f);
    private static readonly Color SoftLockCellColor    = new Color(0.74f, 0.19f, 0.25f, 0.92f);
    private static readonly Color SpawnBadgeColor      = new Color(0.14f, 0.78f, 0.48f, 1f);
    private static readonly Color ExitBadgeColor       = new Color(0.96f, 0.72f, 0.18f, 1f);
    private static readonly Color StopDotColor         = new Color(0.55f, 0.88f, 1.00f, 0.85f);
    private static readonly Color DecisionStopColor    = new Color(0.95f, 0.98f, 1.00f, 0.95f);
    private static readonly Color SolutionLineColor    = new Color(0.20f, 0.88f, 1.00f, 0.95f);
    private static readonly Color StaleSolutionColor   = new Color(0.95f, 0.68f, 0.22f, 0.45f);
    private static readonly Color CellGridLineColor    = new Color(0.06f, 0.08f, 0.11f, 0.65f);
    private static readonly Color ModuleStrideColor    = new Color(0.38f, 0.62f, 0.88f, 0.34f);
    private static readonly Color HoverOutlineColor    = new Color(1.00f, 1.00f, 1.00f, 0.90f);
    private static readonly Color IssueFocusColor      = new Color(1.00f, 0.42f, 0.42f, 0.95f);

    [SerializeField] private MazePatternLibrary patternLibrary;
    [SerializeField] private MazeDifficultyProfile activeProfile;
    [SerializeField] private MazeMapData mapDataToLoad;

    [SerializeField] private int seed = 104729;
    [SerializeField] private bool useProfileDefaultSize = true;
    [SerializeField] private MazeSizePreset overridePreset = MazeSizePreset.Medium;
    [SerializeField] private int overrideSlotCols = 3;
    [SerializeField] private int overrideSlotRows = 5;

    [SerializeField] private MazePaintBrush selectedBrush = MazePaintBrush.Wall;
    [SerializeField] private bool showSolutionPath = true;
    [SerializeField] private bool showReachableStops = true;
    [SerializeField] private bool showSoftLockTraps = true;
    [SerializeField] private bool showModuleStrideGrid = true;

    [SerializeField] private LevelRoot sceneLevelRootForBind;
    [SerializeField] private MazeApplyTargets applyTargets = MazeApplyTargets.CreateDefault();
    [SerializeField] private MazeTemplateLevelRequest templateRequest = MazeTemplateLevelRequest.CreateDefault();

    private MazePreviewSession session;
    private Vector2 leftScrollPos;
    private Vector2 issuesScrollPos;
    private Vector2Int hoveredCell = new Vector2Int(-1, -1);
    private Vector2Int? focusedIssueCell;
    private string lastApplyFeedback;
    private bool lastApplyWasError;
    private string lastTemplateFeedback;
    private bool lastTemplateWasError;

    private readonly HashSet<Vector2Int> softLockLookup = new HashSet<Vector2Int>();
    private readonly Dictionary<Vector2Int, int> solutionStopStepLookup = new Dictionary<Vector2Int, int>();

    // Cached IMGUI styles (lazy-initialized in EnsureStyles during OnGUI)
    private bool stylesInitialized;
    private GUIStyle titleStyle;
    private GUIStyle sectionHeaderStyle;
    private GUIStyle cardStyle;
    private GUIStyle bannerStyle;
    private GUIStyle badgeLabelStyle;
    private GUIStyle stepLabelStyle;
    private GUIStyle footerLabelStyle;
    private GUIStyle miniMetricStyle;

    public MazePreviewSession Session => session ??= new MazePreviewSession();

    public MazePatternLibrary PatternLibrary
    {
        get => patternLibrary;
        set => patternLibrary = value;
    }

    public MazeDifficultyProfile ActiveProfile
    {
        get => activeProfile;
        set => activeProfile = value;
    }

    public MazePaintBrush SelectedBrush
    {
        get => selectedBrush;
        set => selectedBrush = value;
    }

    public MazeApplyTargets ApplyTargets
    {
        get => applyTargets;
        set => applyTargets = value;
    }

    public MazeTemplateLevelRequest TemplateRequest
    {
        get => templateRequest;
        set => templateRequest = value;
    }

    [MenuItem("Tools/Gravita/Maze Generator")]
    public static MazeGeneratorWindow ShowWindow()
    {
        var window = GetWindow<MazeGeneratorWindow>("Maze Generator");
        window.minSize = new Vector2(900f, 580f);
        window.Show();
        return window;
    }

    private void OnEnable()
    {
        session ??= new MazePreviewSession();
        wantsMouseMove = true;
        EnsureDefaultAssetsLoaded();
    }

    /// <summary>
    /// Auto-loads the default <see cref="MazePatternLibrary"/>, <c>Profile_Navigate</c> (or fallback profile),
    /// and default <c>WallTile</c>/<c>BackgroundTile</c> RuleTiles if unassigned, without creating or modifying assets.
    /// </summary>
    public void EnsureDefaultAssetsLoaded()
    {
        if (patternLibrary == null)
        {
            patternLibrary = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(MazePatternBootstrap.LibraryPath);
        }

        if (activeProfile == null)
        {
            activeProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
                $"{MazePatternBootstrap.ProfilesFolder}/Profile_Navigate.asset");

            if (activeProfile == null)
            {
                activeProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
                    $"{MazePatternBootstrap.ProfilesFolder}/Profile_Learn.asset");
            }

            if (activeProfile == null)
            {
                activeProfile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(
                    $"{MazePatternBootstrap.ProfilesFolder}/Profile_Master.asset");
            }
        }

        if (applyTargets.WallTile == null)
            applyTargets.WallTile = MazeApplyService.LoadDefaultWallTile();
        if (applyTargets.BackgroundTile == null)
            applyTargets.BackgroundTile = MazeApplyService.LoadDefaultBackgroundTile();
        if (templateRequest.TemplatePrefab == null)
            templateRequest.TemplatePrefab = MazeApplyService.LoadDefaultTemplatePrefab();
    }

    /// <summary>
    /// Generates a new preview into <see cref="Session"/> using the window's current settings.
    /// </summary>
    public MazeGenerationResult GenerateCurrentPreview()
    {
        focusedIssueCell = null;
        var request = new MazeGenerationRequest
        {
            Seed = seed,
            Profile = activeProfile,
            Library = patternLibrary,
            OverridePreset = useProfileDefaultSize ? (MazeSizePreset?)null : overridePreset,
            OverrideSlotCols = !useProfileDefaultSize && overridePreset == MazeSizePreset.Custom
                ? overrideSlotCols
                : (int?)null,
            OverrideSlotRows = !useProfileDefaultSize && overridePreset == MazeSizePreset.Custom
                ? overrideSlotRows
                : (int?)null,
        };

        MazeGenerationResult result = Session.GeneratePreview(in request);
        Repaint();
        return result;
    }

    /// <summary>
    /// Re-runs stop-state graph validation and quality/difficulty scoring on the current preview buffer.
    /// </summary>
    public MazeValidationReport ValidateCurrentPreview()
    {
        focusedIssueCell = null;
        MazeValidationReport report = Session.Revalidate(activeProfile);
        Repaint();
        return report;
    }

    private void OnGUI()
    {
        EnsureStyles();
        session ??= new MazePreviewSession();

        EditorGUILayout.BeginHorizontal();
        {
            DrawLeftControlPane();
            DrawRightPreviewPane();
        }
        EditorGUILayout.EndHorizontal();
    }

    private void DrawLeftControlPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(LeftPaneWidth), GUILayout.ExpandHeight(true));
        leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos);

        GUILayout.Space(4f);
        EditorGUILayout.LabelField("Gravita Maze Generator", titleStyle);
        EditorGUILayout.LabelField("5x5 Shared-Edge Stop-State Pipeline", EditorStyles.miniLabel);
        GUILayout.Space(6f);

        DrawStatusBanner();
        GUILayout.Space(6f);

        DrawGenerationSection();
        GUILayout.Space(6f);

        DrawBrushAndOverlaySection();
        GUILayout.Space(6f);

        DrawValidationAndMetricsSection();
        GUILayout.Space(6f);

        DrawApplyGateSection();
        GUILayout.Space(6f);

        DrawTemplateLevelSection();
        GUILayout.Space(8f);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    private void DrawStatusBanner()
    {
        Color prevBg = GUI.backgroundColor;
        GUI.backgroundColor = Session.Status switch
        {
            MazePreviewStatus.Valid   => new Color(0.16f, 0.62f, 0.36f, 1f),
            MazePreviewStatus.Stale   => new Color(0.82f, 0.56f, 0.14f, 1f),
            MazePreviewStatus.Invalid => new Color(0.78f, 0.22f, 0.24f, 1f),
            _                         => new Color(0.28f, 0.31f, 0.36f, 1f),
        };

        EditorGUILayout.BeginVertical(bannerStyle);
        string statusTag = Session.Status switch
        {
            MazePreviewStatus.Valid   => "STATUS: VALID (READY FOR GATE)",
            MazePreviewStatus.Stale   => "STATUS: STALE — RE-VALIDATE REQUIRED",
            MazePreviewStatus.Invalid => "STATUS: INVALID — HARD ERRORS",
            _                         => "STATUS: EMPTY PREVIEW",
        };
        EditorGUILayout.LabelField(statusTag, EditorStyles.boldLabel);
        if (!string.IsNullOrEmpty(Session.StatusBannerMessage))
            EditorGUILayout.LabelField(Session.StatusBannerMessage, EditorStyles.wordWrappedMiniLabel);
        EditorGUILayout.EndVertical();

        GUI.backgroundColor = prevBg;
    }

    private void DrawGenerationSection()
    {
        EditorGUILayout.BeginVertical(cardStyle);
        EditorGUILayout.LabelField("1. Generation & Source Assets", sectionHeaderStyle);

        patternLibrary = (MazePatternLibrary)EditorGUILayout.ObjectField(
            "Pattern Library",
            patternLibrary,
            typeof(MazePatternLibrary),
            allowSceneObjects: false);

        activeProfile = (MazeDifficultyProfile)EditorGUILayout.ObjectField(
            "Difficulty Profile",
            activeProfile,
            typeof(MazeDifficultyProfile),
            allowSceneObjects: false);

        // Quick 1-click profile tier switcher
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.PrefixLabel("Quick Tier");
        if (GUILayout.Button("Learn", EditorStyles.miniButtonLeft))
            SelectBuiltInProfile("Profile_Learn");
        if (GUILayout.Button("Navigate", EditorStyles.miniButtonMid))
            SelectBuiltInProfile("Profile_Navigate");
        if (GUILayout.Button("Master", EditorStyles.miniButtonRight))
            SelectBuiltInProfile("Profile_Master");
        EditorGUILayout.EndHorizontal();

        if (patternLibrary == null || activeProfile == null)
        {
            EditorGUILayout.HelpBox(
                "Assign both a MazePatternLibrary and a MazeDifficultyProfile, or click below to create/refresh the default base assets.",
                MessageType.Warning);
            if (GUILayout.Button("Create / Refresh Base Assets"))
            {
                MazePatternBootstrap.CreateOrRefreshBaseAssets(forceOverwrite: false);
                EnsureDefaultAssetsLoaded();
            }
        }

        GUILayout.Space(4f);
        useProfileDefaultSize = EditorGUILayout.Toggle("Use Profile Default Size", useProfileDefaultSize);
        if (!useProfileDefaultSize)
        {
            overridePreset = (MazeSizePreset)EditorGUILayout.EnumPopup("Override Size Preset", overridePreset);
            if (overridePreset == MazeSizePreset.Custom)
            {
                overrideSlotCols = EditorGUILayout.IntSlider("Slot Cols", overrideSlotCols, 2, 8);
                overrideSlotRows = EditorGUILayout.IntSlider("Slot Rows", overrideSlotRows, 2, 10);
            }
        }

        Vector2Int resolvedSlots = ResolveActiveSlotDimensions();
        int resolvedW = resolvedSlots.x * MazePattern.Stride + 1;
        int resolvedH = resolvedSlots.y * MazePattern.Stride + 1;
        EditorGUILayout.LabelField(
            $"Target Footprint: {resolvedSlots.x}x{resolvedSlots.y} slots -> {resolvedW}x{resolvedH} cells",
            miniMetricStyle);

        GUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();
        seed = EditorGUILayout.IntField("Seed", seed);
        if (GUILayout.Button("Random", GUILayout.Width(68f)))
        {
            seed = Environment.TickCount & 0x7FFFFFFF;
            if (patternLibrary != null && activeProfile != null)
                GenerateCurrentPreview();
        }
        EditorGUILayout.EndHorizontal();

        bool canGenerate = patternLibrary != null && activeProfile != null;
        using (new EditorGUI.DisabledScope(!canGenerate))
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.24f, 0.62f, 0.96f, 1f);
            if (GUILayout.Button("Generate Preview", GUILayout.Height(28f)))
            {
                GenerateCurrentPreview();
            }
            GUI.backgroundColor = prev;
        }

        GUILayout.Space(6f);
        EditorGUILayout.LabelField("Load Saved MazeMapData", EditorStyles.miniBoldLabel);
        EditorGUILayout.BeginHorizontal();
        mapDataToLoad = (MazeMapData)EditorGUILayout.ObjectField(
            mapDataToLoad,
            typeof(MazeMapData),
            allowSceneObjects: false);
        using (new EditorGUI.DisabledScope(mapDataToLoad == null))
        {
            if (GUILayout.Button("Load", GUILayout.Width(58f)))
            {
                focusedIssueCell = null;
                Session.LoadFromMapData(mapDataToLoad, activeProfile);
                seed = mapDataToLoad.seed;
                Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    private void DrawBrushAndOverlaySection()
    {
        EditorGUILayout.BeginVertical(cardStyle);
        EditorGUILayout.LabelField("2. Preview Brush & Overlays", sectionHeaderStyle);

        selectedBrush = (MazePaintBrush)GUILayout.Toolbar((int)selectedBrush, BrushLabels, GUILayout.Height(24f));

        Session.LockOuterBorder = EditorGUILayout.Toggle(
            new GUIContent("Lock Outer Perimeter", "Prevents carving open Floor/Spawn/Exit cells on the outer border."),
            Session.LockOuterBorder);

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!Session.CanUndo))
        {
            if (GUILayout.Button("Undo Brush"))
            {
                Session.UndoPreviewEdit(activeProfile);
                Repaint();
            }
        }
        using (new EditorGUI.DisabledScope(!Session.CanRedo))
        {
            if (GUILayout.Button("Redo Brush"))
            {
                Session.RedoPreviewEdit(activeProfile);
                Repaint();
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(4f);
        showSolutionPath = EditorGUILayout.ToggleLeft("Show Shortest Solution Path", showSolutionPath);
        showReachableStops = EditorGUILayout.ToggleLeft("Show Reachable Stop Nodes", showReachableStops);
        showSoftLockTraps = EditorGUILayout.ToggleLeft("Show Soft-Lock Trap Highlights", showSoftLockTraps);
        showModuleStrideGrid = EditorGUILayout.ToggleLeft("Show 4x4 Shared-Edge Module Grid", showModuleStrideGrid);

        EditorGUILayout.EndVertical();
    }

    private void DrawValidationAndMetricsSection()
    {
        EditorGUILayout.BeginVertical(cardStyle);
        EditorGUILayout.LabelField("3. Validation & Scoring", sectionHeaderStyle);

        using (new EditorGUI.DisabledScope(!Session.HasPreview))
        {
            Color prev = GUI.backgroundColor;
            if (Session.IsValidationStale)
                GUI.backgroundColor = new Color(0.95f, 0.68f, 0.20f, 1f);
            else if (Session.Status == MazePreviewStatus.Valid)
                GUI.backgroundColor = new Color(0.24f, 0.76f, 0.46f, 1f);

            string validateLabel = Session.IsValidationStale
                ? "Validate Preview (Stale Edits Pending)"
                : "Re-Validate Preview";

            if (GUILayout.Button(validateLabel, GUILayout.Height(26f)))
            {
                ValidateCurrentPreview();
            }
            GUI.backgroundColor = prev;
        }

        if (!string.IsNullOrEmpty(Session.GenerationFailure))
        {
            EditorGUILayout.HelpBox(Session.GenerationFailure, MessageType.Error);
        }
        else if (!string.IsNullOrEmpty(Session.GenerationWarning))
        {
            EditorGUILayout.HelpBox(Session.GenerationWarning, MessageType.Warning);
        }

        if (Session.PreviewGrid != null)
        {
            string valRev = Session.Validation != null ? Session.Validation.GridRevision.ToString() : "none";
            EditorGUILayout.LabelField(
                $"Seed: {Session.LastSeedUsed} | Attempts: {Session.LastAttemptsUsed} | Grid Rev: {Session.PreviewGrid.Revision} (Val Rev: {valRev})",
                miniMetricStyle);
        }

        if (Session.Score != null)
        {
            if (Session.Score.HasConfigError)
            {
                EditorGUILayout.HelpBox($"Profile Weight Error: {Session.Score.ConfigError}", MessageType.Error);
            }
            else if (Session.Score.IsValid)
            {
                GUILayout.Space(4f);
                DrawProgressBar(
                    Session.Score.QualityScore / 100f,
                    $"Quality Score: {Session.Score.QualityScore:F1} / 100"
                    + (activeProfile != null ? $" (Target {activeProfile.targetScore:F0})" : string.Empty));

                EditorGUILayout.LabelField(
                    $"  Playability {Session.Score.PlayabilitySubscore:F0} ({Session.Score.NormalizedPlayabilityWeight:P0}) | "
                    + $"Diversity {Session.Score.DiversitySubscore:F0} ({Session.Score.NormalizedDiversityWeight:P0}) | "
                    + $"Layout {Session.Score.LayoutSubscore:F0} ({Session.Score.NormalizedLayoutWeight:P0})",
                    miniMetricStyle);

                GUILayout.Space(2f);
                DrawProgressBar(
                    Session.Score.DifficultyFit / 100f,
                    $"Difficulty Fit: {Session.Score.DifficultyFit:F1} / 100");

                EditorGUILayout.LabelField(
                    $"  Moves {Session.Score.SolutionMovesFit:F0}% | Decisions {Session.Score.DecisionStopsFit:F0}% | "
                    + $"Loops {Session.Score.LoopsFit:F0}% | Caps {Session.Score.StructureCapsFit:F0}%",
                    miniMetricStyle);
            }
        }

        MazeSlideGraph graph = Session.Validation?.SlideGraph;
        if (graph != null)
        {
            GUILayout.Space(4f);
            string movesBand = activeProfile != null ? $" [{activeProfile.minSolutionMoves}..{activeProfile.maxSolutionMoves}]" : string.Empty;
            string decBand = activeProfile != null ? $" [{activeProfile.minDecisionStops}..{activeProfile.maxDecisionStops}]" : string.Empty;
            string loopBand = activeProfile != null ? $" [{activeProfile.minStopGraphLoops}..{activeProfile.maxStopGraphLoops}]" : string.Empty;

            EditorGUILayout.LabelField($"Solution Moves: {graph.SolutionMoves}{movesBand}", miniMetricStyle);
            EditorGUILayout.LabelField($"Decision Stops: {graph.DecisionStopsOnSolution}{decBand}", miniMetricStyle);
            EditorGUILayout.LabelField($"Stop-Graph Loops: {graph.StopGraphLoops}{loopBand}", miniMetricStyle);
            EditorGUILayout.LabelField(
                $"Reachable Stops: {graph.ReachableStops.Count} | Soft-Locks: {graph.SoftLockedStops.Count}",
                miniMetricStyle);
            EditorGUILayout.LabelField(
                $"Max Slide Runway: {graph.MaxSlideLength} cells | 1-Cell Ratio: {graph.OneCellSlideRatio:P0} | Coverage: {graph.FloorCoverage:P0}",
                miniMetricStyle);
        }

        if (Session.Validation != null && Session.Validation.Issues.Count > 0)
        {
            GUILayout.Space(6f);
            EditorGUILayout.LabelField($"Validation Issues ({Session.Validation.Issues.Count})", EditorStyles.miniBoldLabel);
            float listHeight = Mathf.Min(140f, 34f * Session.Validation.Issues.Count + 8f);
            issuesScrollPos = EditorGUILayout.BeginScrollView(issuesScrollPos, GUILayout.Height(listHeight));
            for (int i = 0; i < Session.Validation.Issues.Count; i++)
            {
                MazeValidationIssue issue = Session.Validation.Issues[i];
                MessageType msgType = issue.Severity switch
                {
                    MazeValidationSeverity.Error   => MessageType.Error,
                    MazeValidationSeverity.Warning => MessageType.Warning,
                    _                              => MessageType.Info,
                };
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.HelpBox($"[{issue.Code}] {issue.Message}", msgType);
                if (issue.Cell.HasValue && GUILayout.Button("Ping", GUILayout.Width(42f), GUILayout.Height(36f)))
                {
                    focusedIssueCell = issue.Cell.Value;
                    Repaint();
                }
                EditorGUILayout.EndHorizontal();
            }
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawApplyGateSection()
    {
        EditorGUILayout.BeginVertical(cardStyle);
        EditorGUILayout.LabelField("4. Apply to Scene Tilemap & Save Map Data", sectionHeaderStyle);

        if (!Session.CanApply)
        {
            string lockReason = Session.Status switch
            {
                MazePreviewStatus.Empty   => "Generate or load a maze preview first.",
                MazePreviewStatus.Stale   => "Preview was modified since last validation — click Validate Preview.",
                MazePreviewStatus.Invalid => "Preview has hard validation or profile configuration errors.",
                _                         => "Preview is not ready.",
            };
            EditorGUILayout.HelpBox($"Apply Gate LOCKED: {lockReason}", MessageType.None);
        }

        // Quick 1-click auto-bind from a scene/PrefabStage LevelRoot
        EditorGUILayout.BeginHorizontal();
        sceneLevelRootForBind = (LevelRoot)EditorGUILayout.ObjectField(
            "Scene LevelRoot",
            sceneLevelRootForBind,
            typeof(LevelRoot),
            allowSceneObjects: true);
        using (new EditorGUI.DisabledScope(sceneLevelRootForBind == null))
        {
            if (GUILayout.Button("Bind", GUILayout.Width(52f)))
            {
                if (MazeApplyService.TryBindTargetsFromLevelRoot(sceneLevelRootForBind, ref applyTargets, out string bindErr))
                {
                    lastApplyWasError = false;
                    lastApplyFeedback = $"Bound targets from '{sceneLevelRootForBind.name}'.";
                }
                else
                {
                    lastApplyWasError = true;
                    lastApplyFeedback = bindErr;
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        GUILayout.Space(2f);
        applyTargets.WallTilemap = (Tilemap)EditorGUILayout.ObjectField(
            "Wall Tilemap *",
            applyTargets.WallTilemap,
            typeof(Tilemap),
            allowSceneObjects: true);
        applyTargets.WallTile = (TileBase)EditorGUILayout.ObjectField(
            "Wall Tile *",
            applyTargets.WallTile,
            typeof(TileBase),
            allowSceneObjects: false);

        applyTargets.BackgroundTilemap = (Tilemap)EditorGUILayout.ObjectField(
            "Background Tilemap",
            applyTargets.BackgroundTilemap,
            typeof(Tilemap),
            allowSceneObjects: true);
        applyTargets.BackgroundTile = (TileBase)EditorGUILayout.ObjectField(
            "Background Tile",
            applyTargets.BackgroundTile,
            typeof(TileBase),
            allowSceneObjects: false);

        applyTargets.SpawnTransform = (Transform)EditorGUILayout.ObjectField(
            "Spawn Transform",
            applyTargets.SpawnTransform,
            typeof(Transform),
            allowSceneObjects: true);
        applyTargets.ExitTransform = (Transform)EditorGUILayout.ObjectField(
            "Exit Transform",
            applyTargets.ExitTransform,
            typeof(Transform),
            allowSceneObjects: true);

        applyTargets.CenterAroundOrigin = EditorGUILayout.Toggle("Center Grid Around (0,0)", applyTargets.CenterAroundOrigin);
        if (!applyTargets.CenterAroundOrigin)
            applyTargets.CellOriginOffset = EditorGUILayout.Vector2IntField("Cell Origin Offset", applyTargets.CellOriginOffset);

        applyTargets.ClearExistingTilesFirst = EditorGUILayout.Toggle("Clear Existing Tiles First", applyTargets.ClearExistingTilesFirst);
        applyTargets.FillBackgroundUnderWalls = EditorGUILayout.Toggle("Fill Background Under Walls", applyTargets.FillBackgroundUnderWalls);

        GUILayout.Space(4f);
        bool canExecuteApply = Session.CanApply && applyTargets.WallTilemap != null && applyTargets.WallTile != null;
        using (new EditorGUI.DisabledScope(!canExecuteApply))
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.22f, 0.78f, 0.48f, 1f);
            if (GUILayout.Button("Apply to Assigned Tilemap (Undoable)", GUILayout.Height(28f)))
            {
                MazeApplyResult applyRes = MazeApplyService.ApplyToTargets(Session, in applyTargets);
                lastApplyWasError = !applyRes.Success;
                lastApplyFeedback = applyRes.Success
                    ? $"Applied {applyRes.WallTilesPlaced} wall tiles & {applyRes.BackgroundTilesPlaced} bg tiles (Ctrl+Z to Undo)."
                    : applyRes.ErrorMessage;
            }
            GUI.backgroundColor = prev;
        }

        EditorGUILayout.BeginHorizontal();
        using (new EditorGUI.DisabledScope(!Session.CanApply))
        {
            if (GUILayout.Button("Quick Save MazeMapData"))
            {
                string profId = activeProfile != null ? activeProfile.profileId : "Custom";
                int libVer = patternLibrary != null ? patternLibrary.libraryVersion : 1;
                string defaultPath = MazeApplyService.BuildDefaultMapAssetPath(profId, Session.LastSeedUsed);
                if (MazeApplyService.SaveMapDataAsset(Session, defaultPath, profId, libVer, out MazeMapData saved, out string saveErr))
                {
                    EditorGUIUtility.PingObject(saved);
                    lastApplyWasError = false;
                    lastApplyFeedback = $"Saved '{defaultPath}'.";
                }
                else
                {
                    lastApplyWasError = true;
                    lastApplyFeedback = saveErr;
                }
            }

            if (GUILayout.Button("Save As...", GUILayout.Width(82f)))
            {
                string profId = activeProfile != null ? activeProfile.profileId : "Custom";
                int libVer = patternLibrary != null ? patternLibrary.libraryVersion : 1;
                MazeApplyService.EnsureFolderRecursive(MazeApplyService.DefaultMapsFolder);
                string chosenPath = EditorUtility.SaveFilePanelInProject(
                    "Save MazeMapData Asset",
                    $"Maze_{MazeApplyService.SanitizeFileName(profId)}_{Session.LastSeedUsed}",
                    "asset",
                    "Choose where to save this validated MazeMapData asset.",
                    MazeApplyService.DefaultMapsFolder);

                if (!string.IsNullOrEmpty(chosenPath))
                {
                    if (MazeApplyService.SaveMapDataAsset(Session, chosenPath, profId, libVer, out MazeMapData saved, out string saveErr))
                    {
                        EditorGUIUtility.PingObject(saved);
                        lastApplyWasError = false;
                        lastApplyFeedback = $"Saved '{chosenPath}'.";
                    }
                    else
                    {
                        lastApplyWasError = true;
                        lastApplyFeedback = saveErr;
                    }
                }
            }
        }
        EditorGUILayout.EndHorizontal();

        if (!string.IsNullOrEmpty(lastApplyFeedback))
        {
            EditorGUILayout.HelpBox(
                lastApplyFeedback,
                lastApplyWasError ? MessageType.Error : MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawTemplateLevelSection()
    {
        EditorGUILayout.BeginVertical(cardStyle);
        EditorGUILayout.LabelField("5. Create New Level From Template", sectionHeaderStyle);
        EditorGUILayout.LabelField(
            "Clones template prefab -> Assets/Prefabs/Levels/ + LevelData -> Assets/Data/Levels/ (never modifies CampaignDatabase).",
            miniMetricStyle);
        GUILayout.Space(2f);

        templateRequest.TemplatePrefab = (GameObject)EditorGUILayout.ObjectField(
            "Template Prefab *",
            templateRequest.TemplatePrefab,
            typeof(GameObject),
            allowSceneObjects: false);

        templateRequest.LevelAssetName = EditorGUILayout.TextField("New Level ID *", templateRequest.LevelAssetName);
        templateRequest.DisplayName = EditorGUILayout.TextField("Display Name", templateRequest.DisplayName);
        templateRequest.WorldIndex = Mathf.Max(1, EditorGUILayout.IntField("World Index", templateRequest.WorldIndex));

        templateRequest.CenterAroundOrigin = EditorGUILayout.Toggle("Center Grid Around (0,0)", templateRequest.CenterAroundOrigin);
        templateRequest.StripTemplateCollectiblesAndMechanics = EditorGUILayout.Toggle(
            new GUIContent("Strip Template Collectibles", "Removes template stars/coins/hazards so they do not overlap new walls."),
            templateRequest.StripTemplateCollectiblesAndMechanics);
        templateRequest.SaveCompanionMapData = EditorGUILayout.Toggle("Save Companion MazeMapData", templateRequest.SaveCompanionMapData);
        templateRequest.AllowOverwrite = EditorGUILayout.Toggle("Allow Overwrite Existing", templateRequest.AllowOverwrite);

        bool canCreate = Session.CanApply
                      && templateRequest.TemplatePrefab != null
                      && !string.IsNullOrWhiteSpace(templateRequest.LevelAssetName);

        GUILayout.Space(4f);
        using (new EditorGUI.DisabledScope(!canCreate))
        {
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = new Color(0.32f, 0.68f, 0.96f, 1f);
            if (GUILayout.Button("Create New Level From Template", GUILayout.Height(28f)))
            {
                MazeTemplateLevelRequest req = templateRequest;
                req.Profile = activeProfile;
                req.PatternLibraryVersion = patternLibrary != null ? patternLibrary.libraryVersion : 1;
                req.OverrideWallTile = applyTargets.WallTile;
                req.OverrideBackgroundTile = applyTargets.BackgroundTile;

                MazeTemplateLevelResult res = MazeApplyService.CreateLevelFromTemplate(Session, in req);
                lastTemplateWasError = !res.Success;
                if (res.Success)
                {
                    if (res.CreatedLevelData != null)
                        EditorGUIUtility.PingObject(res.CreatedLevelData);
                    lastTemplateFeedback =
                        $"Created '{res.PrefabAssetPath}' & '{res.LevelDataAssetPath}'. {res.ManualCampaignReminder}";
                }
                else
                {
                    lastTemplateFeedback = res.ErrorMessage;
                }
            }
            GUI.backgroundColor = prev;
        }

        if (!string.IsNullOrEmpty(lastTemplateFeedback))
        {
            EditorGUILayout.HelpBox(
                lastTemplateFeedback,
                lastTemplateWasError ? MessageType.Error : MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    private void DrawRightPreviewPane()
    {
        EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        Rect canvasRect = GUILayoutUtility.GetRect(
            200f,
            200f,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        EditorGUI.DrawRect(canvasRect, CanvasBgColor);

        if (Session.PreviewGrid == null)
        {
            GUI.Label(
                canvasRect,
                "No Maze Preview Loaded\nClick 'Generate Preview' on the left to build a 5x5 shared-edge maze.",
                badgeLabelStyle);
        }
        else
        {
            DrawMazeGridCanvas(canvasRect, Session.PreviewGrid);
        }

        Rect footerRect = GUILayoutUtility.GetRect(200f, FooterHeight, GUILayout.ExpandWidth(true));
        DrawCanvasFooter(footerRect);

        EditorGUILayout.EndVertical();
    }

    private void DrawMazeGridCanvas(Rect canvasRect, MazeGrid grid)
    {
        float availW = Mathf.Max(32f, canvasRect.width - CanvasPadding * 2f);
        float availH = Mathf.Max(32f, canvasRect.height - CanvasPadding * 2f);
        float cellSize = Mathf.Floor(Mathf.Min(availW / grid.Width, availH / grid.Height));
        cellSize = Mathf.Max(8f, cellSize);

        float boardW = cellSize * grid.Width;
        float boardH = cellSize * grid.Height;
        float originX = Mathf.Floor(canvasRect.x + (canvasRect.width - boardW) * 0.5f);
        float originY = Mathf.Floor(canvasRect.y + (canvasRect.height - boardH) * 0.5f);
        var boardRect = new Rect(originX, originY, boardW, boardH);

        HandleCanvasMouseInput(boardRect, grid, cellSize);

        MazeSlideGraph graph = Session.Validation?.SlideGraph;
        bool hasGraph = graph != null;

        softLockLookup.Clear();
        if (hasGraph && showSoftLockTraps)
        {
            for (int i = 0; i < graph.SoftLockedStops.Count; i++)
                softLockLookup.Add(graph.SoftLockedStops[i]);
        }

        // 1. Draw cells (row 0 at top corresponds to gy = Height - 1)
        for (int gy = 0; gy < grid.Height; gy++)
        {
            float cy = GridYToScreenY(originY, grid.Height, cellSize, gy);
            bool borderY = gy == 0 || gy == grid.Height - 1;

            for (int gx = 0; gx < grid.Width; gx++)
            {
                float cx = originX + gx * cellSize;
                var cellRect = new Rect(cx, cy, cellSize, cellSize);
                var coord = new Vector2Int(gx, gy);

                if (grid.GetCell(gx, gy) == MazeCell.Wall)
                {
                    bool isPerimeter = borderY || gx == 0 || gx == grid.Width - 1;
                    EditorGUI.DrawRect(cellRect, isPerimeter ? PerimeterWallColor : InteriorWallColor);
                }
                else
                {
                    bool traversed = !hasGraph || graph.IsCellTraversed(coord);
                    EditorGUI.DrawRect(cellRect, traversed ? TraversedFloorColor : UntraversedFloorColor);

                    if (softLockLookup.Contains(coord))
                    {
                        Rect trapRect = InsetRect(cellRect, Mathf.Max(1f, cellSize * 0.08f));
                        EditorGUI.DrawRect(trapRect, SoftLockCellColor);
                    }
                }
            }
        }

        // 2. Subtle cell grid lines & 4x4 shared-edge stride overlay
        for (int gx = 0; gx <= grid.Width; gx++)
        {
            float x = originX + gx * cellSize;
            EditorGUI.DrawRect(new Rect(x, originY, 1f, boardH), CellGridLineColor);
            if (showModuleStrideGrid && gx % MazePattern.Stride == 0)
                EditorGUI.DrawRect(new Rect(x, originY, 1.5f, boardH), ModuleStrideColor);
        }

        for (int gy = 0; gy <= grid.Height; gy++)
        {
            float y = originY + gy * cellSize;
            EditorGUI.DrawRect(new Rect(originX, y, boardW, 1f), CellGridLineColor);
            if (showModuleStrideGrid && (grid.Height - 1 - gy) % MazePattern.Stride == 0)
                EditorGUI.DrawRect(new Rect(originX, y, boardW, 1.5f), ModuleStrideColor);
        }

        // 3. Reachable stop markers
        if (hasGraph && showReachableStops)
        {
            for (int i = 0; i < graph.ReachableStops.Count; i++)
            {
                Vector2Int stop = graph.ReachableStops[i];
                if ((grid.Spawn.HasValue && stop == grid.Spawn.Value)
                    || (grid.Exit.HasValue && stop == grid.Exit.Value))
                {
                    continue;
                }

                bool isDecision = graph.OutgoingByStop.TryGetValue(stop, out List<MazeSlideEdge> edges)
                                  && edges.Count >= 2;
                float markerSize = Mathf.Max(3f, cellSize * (isDecision ? 0.30f : 0.20f));
                Vector2 center = GetCellScreenCenter(originX, originY, grid.Height, cellSize, stop);
                var dotRect = new Rect(
                    center.x - markerSize * 0.5f,
                    center.y - markerSize * 0.5f,
                    markerSize,
                    markerSize);
                EditorGUI.DrawRect(dotRect, isDecision ? DecisionStopColor : StopDotColor);
            }
        }

        // 4. Shortest solution polyline + stop order indices
        if (hasGraph && showSolutionPath && graph.SolutionStops.Count >= 2)
        {
            Color prevHandleColor = Handles.color;
            Handles.color = Session.IsValidationStale ? StaleSolutionColor : SolutionLineColor;

            for (int i = 0; i < graph.SolutionStops.Count - 1; i++)
            {
                Vector2 a = GetCellScreenCenter(originX, originY, grid.Height, cellSize, graph.SolutionStops[i]);
                Vector2 b = GetCellScreenCenter(originX, originY, grid.Height, cellSize, graph.SolutionStops[i + 1]);
                Handles.DrawAAPolyLine(3.2f, new Vector3(a.x, a.y, 0f), new Vector3(b.x, b.y, 0f));
            }

            Handles.color = prevHandleColor;

            if (cellSize >= 16f && !Session.IsValidationStale)
            {
                solutionStopStepLookup.Clear();
                for (int i = 1; i < graph.SolutionStops.Count - 1; i++)
                {
                    Vector2Int s = graph.SolutionStops[i];
                    if (!solutionStopStepLookup.ContainsKey(s))
                        solutionStopStepLookup[s] = i;
                }

                foreach (KeyValuePair<Vector2Int, int> kvp in solutionStopStepLookup)
                {
                    Rect cellRect = GetCellScreenRect(originX, originY, grid.Height, cellSize, kvp.Key);
                    GUI.Label(cellRect, kvp.Value.ToString(), stepLabelStyle);
                }
            }
        }

        // 5. Spawn ('S') & Exit ('E') badges
        if (grid.Spawn.HasValue && grid.InBounds(grid.Spawn.Value))
        {
            Rect spawnRect = InsetRect(
                GetCellScreenRect(originX, originY, grid.Height, cellSize, grid.Spawn.Value),
                Mathf.Max(1.5f, cellSize * 0.12f));
            EditorGUI.DrawRect(spawnRect, SpawnBadgeColor);
            if (cellSize >= 12f)
                GUI.Label(spawnRect, "S", badgeLabelStyle);
        }

        if (grid.Exit.HasValue && grid.InBounds(grid.Exit.Value))
        {
            Rect exitRect = InsetRect(
                GetCellScreenRect(originX, originY, grid.Height, cellSize, grid.Exit.Value),
                Mathf.Max(1.5f, cellSize * 0.12f));
            EditorGUI.DrawRect(exitRect, ExitBadgeColor);
            if (cellSize >= 12f)
                GUI.Label(exitRect, "E", badgeLabelStyle);
        }

        // 6. Focused validation issue & mouse hover outlines
        if (focusedIssueCell.HasValue && grid.InBounds(focusedIssueCell.Value))
        {
            DrawRectOutline(
                GetCellScreenRect(originX, originY, grid.Height, cellSize, focusedIssueCell.Value),
                IssueFocusColor,
                2f);
        }

        if (grid.InBounds(hoveredCell))
        {
            DrawRectOutline(
                GetCellScreenRect(originX, originY, grid.Height, cellSize, hoveredCell),
                HoverOutlineColor,
                1.5f);
        }
    }

    private void HandleCanvasMouseInput(Rect boardRect, MazeGrid grid, float cellSize)
    {
        Event evt = Event.current;
        if (evt == null)
            return;

        Vector2 mouse = evt.mousePosition;
        bool inside = boardRect.Contains(mouse);

        Vector2Int newHover = new Vector2Int(-1, -1);
        if (inside)
        {
            int gx = Mathf.Clamp(Mathf.FloorToInt((mouse.x - boardRect.x) / cellSize), 0, grid.Width - 1);
            int rowFromTop = Mathf.Clamp(Mathf.FloorToInt((mouse.y - boardRect.y) / cellSize), 0, grid.Height - 1);
            int gy = grid.Height - 1 - rowFromTop;
            newHover = new Vector2Int(gx, gy);
        }

        if (newHover != hoveredCell)
        {
            hoveredCell = newHover;
            if (evt.type == EventType.MouseMove)
                Repaint();
        }

        if (evt.button != 0)
            return;

        switch (evt.type)
        {
            case EventType.MouseDown:
                if (inside)
                {
                    GUI.FocusControl(null);
                    Session.BeginPaintStroke();
                    Session.PaintCell(hoveredCell.x, hoveredCell.y, selectedBrush);
                    evt.Use();
                    Repaint();
                }
                break;

            case EventType.MouseDrag:
                if (inside)
                {
                    if (Session.PaintCell(hoveredCell.x, hoveredCell.y, selectedBrush))
                        Repaint();
                    evt.Use();
                }
                break;

            case EventType.MouseUp:
            case EventType.Ignore:
                Session.EndPaintStroke();
                break;
        }
    }

    private void DrawCanvasFooter(Rect footerRect)
    {
        EditorGUI.DrawRect(footerRect, new Color(0.11f, 0.13f, 0.18f, 1f));
        Rect textRect = new Rect(footerRect.x + 8f, footerRect.y + 3f, footerRect.width - 16f, footerRect.height - 4f);

        MazeGrid grid = Session.PreviewGrid;
        if (grid != null && grid.InBounds(hoveredCell))
        {
            MazeCell cellType = grid.GetCell(hoveredCell);
            string marker = grid.Spawn == hoveredCell
                ? " [SPAWN]"
                : (grid.Exit == hoveredCell ? " [EXIT]" : string.Empty);

            string stopInfo = string.Empty;
            MazeSlideGraph g = Session.Validation?.SlideGraph;
            if (g != null && cellType == MazeCell.Floor)
            {
                if (softLockLookup.Contains(hoveredCell))
                    stopInfo = " | SOFT-LOCK STOP";
                else if (g.IsStopReachable(hoveredCell))
                    stopInfo = " | Reachable Stop";
                else if (g.IsCellTraversed(hoveredCell))
                    stopInfo = " | Pass-Through";
                else
                    stopInfo = " | Unreachable Floor";
            }

            string moduleInfo = string.Empty;
            if (Session.TryGetPlacedModuleAtCell(hoveredCell.x, hoveredCell.y, out MazePlacedModule mod))
            {
                moduleInfo = $" | Slot ({mod.SlotX},{mod.SlotY}): {mod.PatternId} R{mod.QuarterTurnsCW * 90}";
            }

            GUI.Label(
                textRect,
                $"Cell ({hoveredCell.x}, {hoveredCell.y}) | World ({hoveredCell.x + 0.5f:F1}, {hoveredCell.y + 0.5f:F1}) | {cellType}{marker}{stopInfo}{moduleInfo}",
                footerLabelStyle);
        }
        else
        {
            GUI.Label(
                textRect,
                "Legend: [S] Spawn  [E] Exit  [.] Reachable Stop  [Red] Soft-Lock Trap  [Cyan] Shortest Slide Solution  |  Click or drag to paint preview cells",
                footerLabelStyle);
        }
    }

    private void SelectBuiltInProfile(string fileNameWithoutExt)
    {
        string path = $"{MazePatternBootstrap.ProfilesFolder}/{fileNameWithoutExt}.asset";
        MazeDifficultyProfile loaded = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(path);
        if (loaded != null)
        {
            activeProfile = loaded;
            if (Session.HasPreview)
                Session.Revalidate(activeProfile);
            Repaint();
        }
    }

    private Vector2Int ResolveActiveSlotDimensions()
    {
        if (useProfileDefaultSize && activeProfile != null)
            return activeProfile.GetSlotDimensions();
        return MazeDifficultyProfile.ResolveSlotDimensions(overridePreset, overrideSlotCols, overrideSlotRows);
    }

    private static void DrawProgressBar(float normalizedValue, string label)
    {
        Rect rect = GUILayoutUtility.GetRect(18f, 18f, GUILayout.ExpandWidth(true));
        EditorGUI.ProgressBar(rect, Mathf.Clamp01(normalizedValue), label);
    }

    private static float GridYToScreenY(float originY, int gridHeight, float cellSize, int gy)
    {
        int rowFromTop = gridHeight - 1 - gy;
        return originY + rowFromTop * cellSize;
    }

    private static Rect GetCellScreenRect(float originX, float originY, int gridHeight, float cellSize, Vector2Int cell)
    {
        return new Rect(
            originX + cell.x * cellSize,
            GridYToScreenY(originY, gridHeight, cellSize, cell.y),
            cellSize,
            cellSize);
    }

    private static Vector2 GetCellScreenCenter(float originX, float originY, int gridHeight, float cellSize, Vector2Int cell)
    {
        return new Vector2(
            originX + (cell.x + 0.5f) * cellSize,
            GridYToScreenY(originY, gridHeight, cellSize, cell.y) + cellSize * 0.5f);
    }

    private static Rect InsetRect(Rect rect, float inset)
    {
        return new Rect(
            rect.x + inset,
            rect.y + inset,
            Mathf.Max(1f, rect.width - inset * 2f),
            Mathf.Max(1f, rect.height - inset * 2f));
    }

    private static void DrawRectOutline(Rect rect, Color color, float thickness)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - thickness, rect.width, thickness), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, thickness, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - thickness, rect.y, thickness, rect.height), color);
    }

    private void EnsureStyles()
    {
        if (stylesInitialized && titleStyle != null)
            return;

        titleStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 14,
        };

        sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11,
        };

        cardStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(8, 8, 8, 8),
        };

        bannerStyle = new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(10, 10, 6, 6),
        };

        badgeLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.08f, 0.10f, 0.13f, 1f) },
        };

        stepLabelStyle = new GUIStyle(EditorStyles.miniBoldLabel)
        {
            alignment = TextAnchor.MiddleCenter,
            normal = { textColor = new Color(0.92f, 0.98f, 1.00f, 0.95f) },
        };

        footerLabelStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            alignment = TextAnchor.MiddleLeft,
            normal = { textColor = new Color(0.80f, 0.86f, 0.94f, 1f) },
        };

        miniMetricStyle = new GUIStyle(EditorStyles.miniLabel)
        {
            wordWrap = true,
        };

        stylesInitialized = true;
    }
}

