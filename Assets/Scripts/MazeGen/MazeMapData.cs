using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Persisted asset storing a generated or preview-edited Gravita maze, its generation provenance,
/// and its validated shortest stop-state solution path.
/// </summary>
[CreateAssetMenu(menuName = "Gravita/Maze/Maze Map Data", fileName = "MazeMapData")]
public class MazeMapData : ScriptableObject
{
    public const int CurrentGeneratorVersion = 1;

    [Header("Dimensions & Provenance")]
    public int width;
    public int height;
    public int seed;
    public string profileId;
    public MazeSizePreset sizePreset;
    public int patternLibraryVersion = 1;
    public int generatorVersion = CurrentGeneratorVersion;

    [Header("Validated Metrics")]
    public float qualityScore;
    public float difficultyFit;
    public int solutionMoves;
    public int decisionStops;
    public int stopGraphLoops;

    [Header("Key Coordinates (Bottom-Up Tilemap Cell Coordinates)")]
    public Vector2Int spawn;
    public Vector2Int exit;

    [Header("Top-to-Bottom ASCII Grid ('#' = Wall, '.' = Floor, 'S' = Spawn, 'E' = Exit)")]
    public string[] rows;

    [Header("Shortest Verified Stop Sequence (Spawn -> ... -> Exit)")]
    public List<Vector2Int> solutionStops = new List<Vector2Int>();
    public List<MazeDir> solutionDirections = new List<MazeDir>();

    public MazeGrid ToMazeGrid()
    {
        MazeGrid grid = MazeGrid.FromAsciiLines(rows);
        grid.SetSpawn(spawn);
        grid.SetExit(exit);
        return grid;
    }

    public void PopulateFromGrid(
        MazeGrid grid,
        int generationSeed,
        string sourceProfileId,
        MazeSizePreset preset,
        int libVersion,
        float score,
        float fit,
        IReadOnlyList<Vector2Int> stops = null,
        IReadOnlyList<MazeDir> directions = null,
        int decisionCount = 0,
        int loopCount = 0)
    {
        width = grid.Width;
        height = grid.Height;
        seed = generationSeed;
        profileId = sourceProfileId ?? string.Empty;
        sizePreset = preset;
        patternLibraryVersion = libVersion;
        generatorVersion = CurrentGeneratorVersion;
        qualityScore = score;
        difficultyFit = fit;
        spawn = grid.Spawn ?? Vector2Int.zero;
        exit = grid.Exit ?? Vector2Int.zero;
        rows = grid.ToAsciiLines(includeMarkers: true);

        solutionStops.Clear();
        if (stops != null)
        {
            for (int i = 0; i < stops.Count; i++)
                solutionStops.Add(stops[i]);
        }

        solutionDirections.Clear();
        if (directions != null)
        {
            for (int i = 0; i < directions.Count; i++)
                solutionDirections.Add(directions[i]);
        }

        solutionMoves = solutionDirections.Count;
        decisionStops = decisionCount;
        stopGraphLoops = loopCount;
    }
}

