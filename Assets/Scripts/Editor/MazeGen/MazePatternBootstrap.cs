using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Creates or refreshes the 11 audited 5x5 base <see cref="MazePattern"/> assets,
/// <see cref="MazePatternLibrary"/>, and the 3 initial <see cref="MazeDifficultyProfile"/> assets
/// under <c>Assets/Data/MazeGen/</c>.
/// </summary>
public static class MazePatternBootstrap
{
    public const string RootFolder         = "Assets/Data/MazeGen";
    public const string PatternsFolder     = "Assets/Data/MazeGen/Patterns";
    public const string ProfilesFolder     = "Assets/Data/MazeGen/Profiles";
    public const string MapsFolder         = "Assets/Data/MazeGen/Maps";
    public const string LibraryPath        = "Assets/Data/MazeGen/DefaultMazePatternLibrary.asset";

    public const string RefMapLearnPath    = "Assets/Data/MazeGen/Maps/RefMap_Learn.asset";
    public const string RefMapNavigatePath = "Assets/Data/MazeGen/Maps/RefMap_Navigate.asset";
    public const string RefMapMasterPath   = "Assets/Data/MazeGen/Maps/RefMap_Master.asset";

    public const int RefSeedLearn          = 1009;
    public const int RefSeedNavigate       = 1039;
    public const int RefSeedMaster         = 1001;

    private readonly struct PatternSpec
    {
        public readonly string Id;
        public readonly string DisplayName;
        public readonly MazePatternGroup Group;
        public readonly MazeRotations Rotations;
        public readonly float Weight;
        public readonly string[] Rows;

        public PatternSpec(
            string id,
            string displayName,
            MazePatternGroup group,
            MazeRotations rotations,
            float weight,
            string[] rows)
        {
            Id = id;
            DisplayName = displayName;
            Group = group;
            Rotations = rotations;
            Weight = weight;
            Rows = rows;
        }
    }

    private static readonly PatternSpec[] BaseSpecs =
    {
        new PatternSpec(
            "C01_Straight",
            "Straight Corridor",
            MazePatternGroup.Corridor,
            MazeRotations.R0 | MazeRotations.R90,
            1.00f,
            new[]
            {
                "#####",
                "#####",
                ".....",
                "#####",
                "#####",
            }),

        new PatternSpec(
            "C02_Corner",
            "90-Degree Corner",
            MazePatternGroup.Corridor,
            MazeRotations.All,
            1.20f,
            new[]
            {
                "#####",
                "#####",
                "...##",
                "##.##",
                "##.##",
            }),

        new PatternSpec(
            "C03_Chicane",
            "Chicane Stop Jog",
            MazePatternGroup.Corridor,
            MazeRotations.All,
            1.15f,
            new[]
            {
                "#####",
                "#...#",
                "..#..",
                "#####",
                "#####",
            }),

        new PatternSpec(
            "I01_TJunction",
            "Standard T-Junction",
            MazePatternGroup.Intersection,
            MazeRotations.All,
            1.00f,
            new[]
            {
                "##.##",
                "##.##",
                ".....",
                "#####",
                "#####",
            }),

        new PatternSpec(
            "I02_TBaffled",
            "Baffled Catcher T-Junction",
            MazePatternGroup.Intersection,
            MazeRotations.All,
            1.35f,
            new[]
            {
                "##.##",
                "#..##",
                "..#..",
                "#...#",
                "#####",
            }),

        new PatternSpec(
            "I03_Cross",
            "4-Way Crossover",
            MazePatternGroup.Intersection,
            MazeRotations.R0,
            0.35f,
            new[]
            {
                "##.##",
                "##.##",
                ".....",
                "##.##",
                "##.##",
            }),

        new PatternSpec(
            "L01_RingHub",
            "Rotary Ring Hub",
            MazePatternGroup.Loop,
            MazeRotations.All,
            1.40f,
            new[]
            {
                "##.##",
                "#..##",
                "..#..",
                "#...#",
                "##.##",
            }),

        new PatternSpec(
            "D01_DeadEnd",
            "Terminal Pocket",
            MazePatternGroup.DeadEnd,
            MazeRotations.All,
            1.00f,
            new[]
            {
                "#####",
                "#####",
                "...##",
                "#####",
                "#####",
            }),

        new PatternSpec(
            "D02_DeadEndHook",
            "Hooked Terminal Alcove",
            MazePatternGroup.DeadEnd,
            MazeRotations.All,
            1.15f,
            new[]
            {
                "#####",
                "#..##",
                "..###",
                "#####",
                "#####",
            }),

        new PatternSpec(
            "R01_PillarChamber",
            "Pillar Chamber (Corner 2-Port)",
            MazePatternGroup.Chamber,
            MazeRotations.All,
            1.10f,
            new[]
            {
                "#####",
                "#...#",
                "....#",
                "#..##",
                "##.##",
            }),

        new PatternSpec(
            "R02_Chamber3Port",
            "Baffled Chamber (3-Port Mirror)",
            MazePatternGroup.Chamber,
            MazeRotations.All,
            1.15f,
            new[]
            {
                "##.##",
                "##..#",
                "..#..",
                "#...#",
                "#####",
            }),
    };

    [MenuItem("Tools/Gravita/Maze/Create Or Refresh Base Assets")]
    public static void CreateOrRefreshBaseAssetsMenu()
    {
        CreateOrRefreshBaseAssets(forceOverwrite: false);
    }

    [MenuItem("Tools/Gravita/Maze/Force Reset Base Assets")]
    public static void ForceResetBaseAssetsMenu()
    {
        CreateOrRefreshBaseAssets(forceOverwrite: true);
    }

    [MenuItem("Tools/Gravita/Maze/Create Or Refresh Reference Maps")]
    public static void CreateOrRefreshReferenceMapsMenu()
    {
        CreateOrRefreshReferenceMaps();
    }

    /// <summary>
    /// Generates and saves (or updates in-place preserving GUIDs) the 3 deterministic verified reference
    /// <see cref="MazeMapData"/> fixtures (<c>RefMap_Learn</c>, <c>RefMap_Navigate</c>, <c>RefMap_Master</c>)
    /// under <c>Assets/Data/MazeGen/Maps/</c>.
    /// </summary>
    public static void CreateOrRefreshReferenceMaps()
    {
        MazePatternLibrary library = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(LibraryPath);
        if (library == null)
        {
            CreateOrRefreshBaseAssets(forceOverwrite: false);
            library = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(LibraryPath);
        }

        EnsureFolder(MapsFolder);

        GenerateAndSaveReferenceMap(library, "Learn", RefSeedLearn, RefMapLearnPath);
        GenerateAndSaveReferenceMap(library, "Navigate", RefSeedNavigate, RefMapNavigatePath);
        GenerateAndSaveReferenceMap(library, "Master", RefSeedMaster, RefMapMasterPath);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void GenerateAndSaveReferenceMap(
        MazePatternLibrary library,
        string tierName,
        int seed,
        string outputAssetPath)
    {
        string profilePath = $"{ProfilesFolder}/Profile_{tierName}.asset";
        MazeDifficultyProfile profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(profilePath);
        if (profile == null)
        {
            Debug.LogError($"[MazePatternBootstrap] Missing profile at '{profilePath}'.");
            return;
        }

        var session = new MazePreviewSession();
        MazeGenerationResult gen = session.GeneratePreview(new MazeGenerationRequest
        {
            Seed = seed,
            Profile = profile,
            Library = library,
        });

        if (!gen.Success || !session.CanApply)
        {
            Debug.LogError($"[MazePatternBootstrap] Failed to generate reference map '{outputAssetPath}': {gen.FailureReason}");
            return;
        }

        if (!MazeApplyService.SaveMapDataAsset(
                session,
                outputAssetPath,
                profile.profileId,
                library.libraryVersion,
                out _,
                out string saveErr))
        {
            Debug.LogError($"[MazePatternBootstrap] Failed to save reference map '{outputAssetPath}': {saveErr}");
        }
    }

    /// <summary>
    /// Creates or refreshes the 11 base patterns, library, and 3 difficulty profiles.
    /// When <paramref name="forceOverwrite"/> is false, any existing pattern asset whose <c>version</c>
    /// was bumped above <c>1</c> by a designer is preserved untouched.
    /// </summary>
    public static void CreateOrRefreshBaseAssets(bool forceOverwrite = false)
    {
        EnsureFolder("Assets/Data");
        EnsureFolder(RootFolder);
        EnsureFolder(PatternsFolder);
        EnsureFolder(ProfilesFolder);

        var createdPatterns = new List<MazePattern>(BaseSpecs.Length);
        for (int i = 0; i < BaseSpecs.Length; i++)
        {
            PatternSpec spec = BaseSpecs[i];
            string path = $"{PatternsFolder}/{spec.Id}.asset";
            MazePattern asset = AssetDatabase.LoadAssetAtPath<MazePattern>(path);
            bool isNew = asset == null;
            if (isNew)
            {
                asset = ScriptableObject.CreateInstance<MazePattern>();
                AssetDatabase.CreateAsset(asset, path);
            }

            if (isNew || forceOverwrite || asset.version <= 1)
            {
                asset.id = spec.Id;
                asset.displayName = spec.DisplayName;
                asset.group = spec.Group;
                asset.version = 1;
                asset.allowedRotations = spec.Rotations;
                asset.baseWeight = spec.Weight;
                asset.rows = (string[])spec.Rows.Clone();
                EditorUtility.SetDirty(asset);
            }

            createdPatterns.Add(asset);
        }

        MazePatternLibrary library = AssetDatabase.LoadAssetAtPath<MazePatternLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<MazePatternLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        library.libraryVersion = 1;
        library.patterns = createdPatterns;
        EditorUtility.SetDirty(library);

        CreateOrUpdateProfile(
            path: $"{ProfilesFolder}/Profile_Learn.asset",
            profileId: "Learn",
            tier: MazeDifficultyTier.Learn,
            preset: MazeSizePreset.Small,
            minMoves: 4, maxMoves: 8,
            minDecisions: 1, maxDecisions: 3,
            minLoops: 0, maxLoops: 2,
            braidRatio: 0.10f,
            maxDeadEnds: 2,
            maxChambers: 0,
            groups: new[]
            {
                MazePatternGroup.Corridor,
                MazePatternGroup.Intersection,
                MazePatternGroup.DeadEnd,
            });

        CreateOrUpdateProfile(
            path: $"{ProfilesFolder}/Profile_Navigate.asset",
            profileId: "Navigate",
            tier: MazeDifficultyTier.Navigate,
            preset: MazeSizePreset.Medium,
            minMoves: 8, maxMoves: 14,
            minDecisions: 3, maxDecisions: 6,
            minLoops: 1, maxLoops: 4,
            braidRatio: 0.30f,
            maxDeadEnds: 3,
            maxChambers: 1,
            groups: new[]
            {
                MazePatternGroup.Corridor,
                MazePatternGroup.Intersection,
                MazePatternGroup.Loop,
                MazePatternGroup.DeadEnd,
                MazePatternGroup.Chamber,
            });

        CreateOrUpdateProfile(
            path: $"{ProfilesFolder}/Profile_Master.asset",
            profileId: "Master",
            tier: MazeDifficultyTier.Master,
            preset: MazeSizePreset.Large,
            minMoves: 12, maxMoves: 22,
            minDecisions: 5, maxDecisions: 10,
            minLoops: 2, maxLoops: 8,
            braidRatio: 0.45f,
            maxDeadEnds: 5,
            maxChambers: 2,
            groups: new[]
            {
                MazePatternGroup.Corridor,
                MazePatternGroup.Intersection,
                MazePatternGroup.Loop,
                MazePatternGroup.DeadEnd,
                MazePatternGroup.Chamber,
            });

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    private static void CreateOrUpdateProfile(
        string path,
        string profileId,
        MazeDifficultyTier tier,
        MazeSizePreset preset,
        int minMoves,
        int maxMoves,
        int minDecisions,
        int maxDecisions,
        int minLoops,
        int maxLoops,
        float braidRatio,
        int maxDeadEnds,
        int maxChambers,
        MazePatternGroup[] groups)
    {
        MazeDifficultyProfile p = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>(path);
        if (p == null)
        {
            p = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
            AssetDatabase.CreateAsset(p, path);
        }

        Vector2Int slots = MazeDifficultyProfile.ResolveSlotDimensions(preset);
        p.profileId = profileId;
        p.tier = tier;
        p.defaultPreset = preset;
        p.customSlotCols = slots.x;
        p.customSlotRows = slots.y;
        p.minSolutionMoves = minMoves;
        p.maxSolutionMoves = maxMoves;
        p.minDecisionStops = minDecisions;
        p.maxDecisionStops = maxDecisions;
        p.minStopGraphLoops = minLoops;
        p.maxStopGraphLoops = maxLoops;
        p.braidRatio = braidRatio;
        p.maxDeadEnds = maxDeadEnds;
        p.maxChambers = maxChambers;
        p.minLongRunway = 5;
        p.requireExitOnStopCell = true;
        p.allowedGroups = new List<MazePatternGroup>(groups);
        p.maxAttempts = 50;
        p.targetScore = 80f;
        p.playabilityWeight = 40f;
        p.diversityWeight = 35f;
        p.layoutWeight = 25f;

        EditorUtility.SetDirty(p);
    }

    private static void EnsureFolder(string assetFolderPath)
    {
        if (AssetDatabase.IsValidFolder(assetFolderPath))
            return;

        int slash = assetFolderPath.LastIndexOf('/');
        string parent = assetFolderPath.Substring(0, slash);
        string folderName = assetFolderPath.Substring(slash + 1);
        if (!AssetDatabase.IsValidFolder(parent))
            EnsureFolder(parent);

        AssetDatabase.CreateFolder(parent, folderName);
    }
}
