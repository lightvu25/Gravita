using System.Collections.Generic;
using UnityEngine;

public enum MazePatternGroup
{
    Corridor     = 0,
    Intersection = 1,
    Loop         = 2,
    DeadEnd      = 3,
    Chamber      = 4,
}

/// <summary>
/// Authoring data for a single 5x5 shared-edge maze module (stride = 4).
/// Rows are stored top-to-bottom (row 0 = North, row 4 = South) using '#' (Wall) and '.' (Floor).
/// Ports exist exclusively at edge centres: N=(2,0), E=(4,2), S=(2,4), W=(0,2).
/// </summary>
[CreateAssetMenu(menuName = "Gravita/Maze/Maze Pattern", fileName = "MazePattern")]
public class MazePattern : ScriptableObject
{
    public const int ModuleSize = 5;
    public const int Stride = 4;
    public const int PortIndex = 2;

    [Header("Identity")]
    public string id = "C01_Straight";
    public string displayName = "Straight Corridor";
    public MazePatternGroup group = MazePatternGroup.Corridor;
    public int version = 1;

    [Header("Placement")]
    public MazeRotations allowedRotations = MazeRotations.All;
    [Min(0.01f)] public float baseWeight = 1f;

    [Header("5x5 ASCII Grid (Top-to-Bottom, '#' = Wall, '.' = Floor)")]
    public string[] rows =
    {
        "#####",
        "#####",
        ".....",
        "#####",
        "#####",
    };

    /// <summary>
    /// Returns the cell at rotated top-down coordinates (<paramref name="col"/>, <paramref name="row"/>)
    /// in [0..4] after <paramref name="quarterTurnsCW"/> clockwise 90-degree rotations.
    /// </summary>
    public MazeCell GetCell(int col, int row, int quarterTurnsCW = 0)
    {
        if (rows == null || rows.Length != ModuleSize || col < 0 || col >= ModuleSize || row < 0 || row >= ModuleSize)
            return MazeCell.Wall;

        int norm = ((quarterTurnsCW % 4) + 4) % 4;
        int origCol;
        int origRow;

        switch (norm)
        {
            case 0:
                origCol = col;
                origRow = row;
                break;
            case 1: // 90 deg CW: East(4,2) reads North(2,0)
                origCol = row;
                origRow = ModuleSize - 1 - col;
                break;
            case 2: // 180 deg CW
                origCol = ModuleSize - 1 - col;
                origRow = ModuleSize - 1 - row;
                break;
            default: // 270 deg CW: West(0,2) reads North(2,0)
                origCol = ModuleSize - 1 - row;
                origRow = col;
                break;
        }

        string line = rows[origRow];
        if (string.IsNullOrEmpty(line) || origCol >= line.Length)
            return MazeCell.Wall;

        return line[origCol] == '.' ? MazeCell.Floor : MazeCell.Wall;
    }

    /// <summary>
    /// Derives the open edge-centre port mask directly from the 5x5 cell array at the given CW rotation.
    /// </summary>
    public MazeSideMask GetPortMask(int quarterTurnsCW = 0)
    {
        MazeSideMask mask = MazeSideMask.None;
        if (GetCell(PortIndex, 0, quarterTurnsCW) == MazeCell.Floor)
            mask |= MazeSideMask.North;
        if (GetCell(ModuleSize - 1, PortIndex, quarterTurnsCW) == MazeCell.Floor)
            mask |= MazeSideMask.East;
        if (GetCell(PortIndex, ModuleSize - 1, quarterTurnsCW) == MazeCell.Floor)
            mask |= MazeSideMask.South;
        if (GetCell(0, PortIndex, quarterTurnsCW) == MazeCell.Floor)
            mask |= MazeSideMask.West;
        return mask;
    }

    public bool IsRotationAllowed(int quarterTurnsCW)
    {
        MazeRotations flag = MazeDirectionUtil.QuarterTurnsToFlag(quarterTurnsCW);
        return (allowedRotations & flag) != 0;
    }

    /// <summary>
    /// Validates exact 5x5 dimensions, legal '#' / '.' characters, border-wall integrity
    /// (ports allowed ONLY at edge centres), port-to-interior entry, and 4-connected floor topology.
    /// </summary>
    public bool ValidateDefinition(out List<string> errors)
    {
        errors = new List<string>();

        if (string.IsNullOrWhiteSpace(id))
            errors.Add("Pattern ID is empty.");

        if (allowedRotations == MazeRotations.None)
            errors.Add($"Pattern '{id}' allows zero rotations.");

        if (baseWeight <= 0f || float.IsNaN(baseWeight) || float.IsInfinity(baseWeight))
            errors.Add($"Pattern '{id}' has invalid baseWeight ({baseWeight}).");

        if (rows == null || rows.Length != ModuleSize)
        {
            errors.Add($"Pattern '{id}' must have exactly {ModuleSize} rows (found {(rows == null ? 0 : rows.Length)}).");
            return false;
        }

        var floorCells = new List<Vector2Int>();
        for (int r = 0; r < ModuleSize; r++)
        {
            string line = rows[r];
            if (line == null || line.Length != ModuleSize)
            {
                errors.Add($"Pattern '{id}' row {r} must have length {ModuleSize}.");
                return false;
            }

            for (int c = 0; c < ModuleSize; c++)
            {
                char ch = line[c];
                if (ch != '#' && ch != '.')
                {
                    errors.Add($"Pattern '{id}' contains illegal character '{ch}' at ({c},{r}).");
                    continue;
                }

                bool isBorder = r == 0 || r == ModuleSize - 1 || c == 0 || c == ModuleSize - 1;
                bool isPortCentre = (r == 0 && c == PortIndex)
                                 || (r == ModuleSize - 1 && c == PortIndex)
                                 || (c == 0 && r == PortIndex)
                                 || (c == ModuleSize - 1 && r == PortIndex);

                if (isBorder && !isPortCentre && ch != '#')
                {
                    errors.Add($"Pattern '{id}' has a non-centre open border cell at ({c},{r}); ports are restricted to edge centres.");
                }

                if (ch == '.')
                    floorCells.Add(new Vector2Int(c, r));
            }
        }

        if (floorCells.Count == 0)
        {
            errors.Add($"Pattern '{id}' contains zero floor cells.");
            return false;
        }

        MazeSideMask mask = GetPortMask(0);
        if (mask == MazeSideMask.None)
            errors.Add($"Pattern '{id}' has no open edge-centre ports.");

        // Every open port must connect immediately into its adjacent interior cell.
        if ((mask & MazeSideMask.North) != 0 && GetCell(PortIndex, 1, 0) != MazeCell.Floor)
            errors.Add($"Pattern '{id}' North port (2,0) is blocked at interior cell (2,1).");
        if ((mask & MazeSideMask.South) != 0 && GetCell(PortIndex, ModuleSize - 2, 0) != MazeCell.Floor)
            errors.Add($"Pattern '{id}' South port (2,4) is blocked at interior cell (2,3).");
        if ((mask & MazeSideMask.West) != 0 && GetCell(1, PortIndex, 0) != MazeCell.Floor)
            errors.Add($"Pattern '{id}' West port (0,2) is blocked at interior cell (1,2).");
        if ((mask & MazeSideMask.East) != 0 && GetCell(ModuleSize - 2, PortIndex, 0) != MazeCell.Floor)
            errors.Add($"Pattern '{id}' East port (4,2) is blocked at interior cell (3,2).");

        // Verify all floor cells form a single 4-connected component (rejects diagonal-only pinches).
        var floorSet = new HashSet<Vector2Int>(floorCells);
        var visited = new HashSet<Vector2Int>();
        var queue = new Queue<Vector2Int>();
        queue.Enqueue(floorCells[0]);
        visited.Add(floorCells[0]);

        Vector2Int[] dirs = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        while (queue.Count > 0)
        {
            Vector2Int cur = queue.Dequeue();
            for (int i = 0; i < dirs.Length; i++)
            {
                Vector2Int nxt = cur + dirs[i];
                if (floorSet.Contains(nxt) && visited.Add(nxt))
                    queue.Enqueue(nxt);
            }
        }

        if (visited.Count != floorCells.Count)
        {
            errors.Add($"Pattern '{id}' floor cells are not 4-connected ({visited.Count}/{floorCells.Count} connected).");
        }

        return errors.Count == 0;
    }
}

