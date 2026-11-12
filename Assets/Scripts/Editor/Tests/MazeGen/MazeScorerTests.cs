using NUnit.Framework;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Phase 3 unit tests for <see cref="MazeScorer"/>:
/// verifies GDD §6.3 / §9.3 quality weight normalization (default 40/35/25 and custom positive sums),
/// hard rejection of negative/zero/NaN/Infinity weights, zero score on invalid maps,
/// pure stop-graph diversity fallback, and independent <c>DifficultyFit</c> tier band separation.
/// </summary>
[TestFixture]
public class MazeScorerTests
{
    private static MazeGrid BuildSampleValidMaze()
    {
        // Reversible 9x9 layout with a 7-cell KineticRam runway, decision stops, and 1 navigable loop.
        string[] rows =
        {
            "#########", // gy=8
            "#S......#", // gy=7 (7-cell runway gx=1..7)
            "#.#####.#", // gy=6
            "#.#...#.#", // gy=5
            "#.#.#.#.#", // gy=4
            "#...#..E#", // gy=3
            "#####.###", // gy=2
            "#.....###", // gy=1
            "#########", // gy=0
        };
        return MazeGrid.FromAsciiLines(rows);
    }

    [Test]
    public void Scorer_DefaultAndUnnormalizedPositiveWeights_ProduceIdenticalNormalizedQualityScore()
    {
        MazeGrid grid = BuildSampleValidMaze();
        MazeValidationReport validation = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsTrue(validation.IsValid, validation.Issues.Count > 0 ? validation.Issues[0].Message : string.Empty);

        var profileDefault = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
        var profileScaled = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
        try
        {
            profileDefault.playabilityWeight = 40f;
            profileDefault.diversityWeight = 35f;
            profileDefault.layoutWeight = 25f;

            // 8 + 7 + 5 = 20 (same 40% / 35% / 25% ratio, sum != 100)
            profileScaled.playabilityWeight = 8f;
            profileScaled.diversityWeight = 7f;
            profileScaled.layoutWeight = 5f;

            MazeScoreBreakdown scoreA = MazeScorer.Evaluate(grid, validation, profileDefault);
            MazeScoreBreakdown scoreB = MazeScorer.Evaluate(grid, validation, profileScaled);

            Assert.IsTrue(scoreA.IsValid);
            Assert.IsFalse(scoreA.HasConfigError);
            Assert.AreEqual(0.40f, scoreA.NormalizedPlayabilityWeight, 0.0001f);
            Assert.AreEqual(0.35f, scoreA.NormalizedDiversityWeight, 0.0001f);
            Assert.AreEqual(0.25f, scoreA.NormalizedLayoutWeight, 0.0001f);

            float expectedWeighted = 0.40f * scoreA.PlayabilitySubscore
                                   + 0.35f * scoreA.DiversitySubscore
                                   + 0.25f * scoreA.LayoutSubscore;
            Assert.AreEqual(expectedWeighted, scoreA.QualityScore, 0.001f);
            Assert.AreEqual(scoreA.QualityScore, scoreB.QualityScore, 0.001f);
        }
        finally
        {
            Object.DestroyImmediate(profileDefault);
            Object.DestroyImmediate(profileScaled);
        }
    }

    [Test]
    public void Scorer_RejectsNegativeZeroNaNAndInfiniteWeightsWithConfigError()
    {
        MazeGrid grid = BuildSampleValidMaze();
        MazeValidationReport validation = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsTrue(validation.IsValid);

        var profile = ScriptableObject.CreateInstance<MazeDifficultyProfile>();
        try
        {
            (float p, float d, float l)[] invalidSets =
            {
                (-5f, 50f, 55f),
                (0f, 0f, 0f),
                (float.NaN, 35f, 25f),
                (40f, float.PositiveInfinity, 25f),
            };

            foreach (var (p, d, l) in invalidSets)
            {
                profile.playabilityWeight = p;
                profile.diversityWeight = d;
                profile.layoutWeight = l;

                MazeScoreBreakdown score = MazeScorer.Evaluate(grid, validation, profile);
                Assert.IsFalse(score.IsValid);
                Assert.IsTrue(score.HasConfigError);
                Assert.IsNotEmpty(score.ConfigError);
                Assert.AreEqual(0f, score.QualityScore);
            }
        }
        finally
        {
            Object.DestroyImmediate(profile);
        }
    }

    [Test]
    public void Scorer_InvalidOrSoftLockedValidationReport_ReturnsZeroScores()
    {
        string[] softLockMap =
        {
            "#######",
            "#.....#",
            "##.####",
            "##.####",
            "##S..E#",
            "#######",
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(softLockMap);
        MazeValidationReport invalidReport = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsFalse(invalidReport.IsValid);

        var profile = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>("Assets/Data/MazeGen/Profiles/Profile_Learn.asset");
        Assert.IsNotNull(profile);

        MazeScoreBreakdown score = MazeScorer.Evaluate(grid, invalidReport, profile);
        Assert.IsFalse(score.IsValid);
        Assert.IsFalse(score.HasConfigError);
        Assert.AreEqual(0f, score.QualityScore);
        Assert.AreEqual(0f, score.DifficultyFit);
    }

    [Test]
    public void Scorer_SeparatesIntrinsicQualityScoreFromTierDifficultyFit()
    {
        // Compact 5-move layout tailored to Profile_Learn (4..8 moves, 1..3 decisions, 0..2 loops)
        string[] learnSizedMap =
        {
            "#########", // gy=6
            "#S....###", // gy=5: (1,5)->East->(5,5) [1]
            "#####.###", // gy=4
            "#.....###", // gy=3: (5,5)->South->(5,3) [2] ->West->(1,3) [3]
            "#.#######", // gy=2
            "#......E#", // gy=1: (1,3)->South->(1,1) [4] ->East->(7,1) [5]
            "#########", // gy=0
        };

        MazeGrid grid = MazeGrid.FromAsciiLines(learnSizedMap);
        MazeValidationReport validation = MazeValidator.Validate(grid, requireExitOnStopCell: true);
        Assert.IsTrue(validation.IsValid);

        var learn = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>("Assets/Data/MazeGen/Profiles/Profile_Learn.asset");
        var master = AssetDatabase.LoadAssetAtPath<MazeDifficultyProfile>("Assets/Data/MazeGen/Profiles/Profile_Master.asset");
        Assert.IsNotNull(learn);
        Assert.IsNotNull(master);

        MazeScoreBreakdown learnScore = MazeScorer.Evaluate(grid, validation, learn);
        MazeScoreBreakdown masterScore = MazeScorer.Evaluate(grid, validation, master);

        // Intrinsic QualityScore uses the same 40/35/25 weights & minLongRunway=5 on both profiles
        Assert.AreEqual(learnScore.QualityScore, masterScore.QualityScore, 0.001f);

        // DifficultyFit must strongly favor Learn over Master for a 5-move linear/low-loop map
        Assert.Greater(learnScore.DifficultyFit, masterScore.DifficultyFit + 35f);
    }
}

