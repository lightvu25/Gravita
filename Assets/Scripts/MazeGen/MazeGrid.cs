using System;
using System.Collections.Generic;
using UnityEngine;

public enum MazeCell : byte
{
    Wall  = 0,
    Floor = 1,
}

/// <summary>
/// Pure in-memory discrete grid representation of a Gravita maze layout.
/// Coordinates use bottom-up Tilemap indexing: gx in [0..Width-1] (West->East),
/// gy in [0..Height-1] (South->North). ASCII serialization uses top-down row order
/// (row 0 = North / gy = Height - 1).
/// Every mutating operation increments <see cref="Revision"/> so stale validation
/// reports are deterministically detected before Apply.
/// </summary>
public sealed class MazeGrid
{
    public int Width { get; }
    public int Height { get; }

    public Vector2Int? Spawn { get; private set; }
    public Vector2Int? Exit { get; private set; }

    /// <summary>Monotonically incremented on any cell, Spawn, or Exit edit.</summary>
    public int Revision { get; private set; }

    /// <summary>Cached wall-check delegate so repeated <see cref="GridSlideSolver.SolveSlide"/> calls allocate zero garbage.</summary>
    public Func<Vector2Int, bool> IsWallPredicate { get; }

    private readonly MazeCell[,] cells;
    private readonly bool[,] stampedMask;

    public MazeGrid(int width, int height, MazeCell initialFill = MazeCell.Wall)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));

        Width = width;
        Height = height;
        cells = new MazeCell[width, height];
        stampedMask = new bool[width, height];
        IsWallPredicate = IsWall;

        if (initialFill != MazeCell.Wall)
        {
            for (int x = 0; x < width; x++)
                for (int y = 0; y < height; y++)
                    cells[x, y] = initialFill;
        }
    }

    public MazeGrid Clone()
    {
        var copy = new MazeGrid(Width, Height);
        Array.Copy(cells, copy.cells, cells.Length);
        Array.Copy(stampedMask, copy.stampedMask, stampedMask.Length);
        copy.Spawn = Spawn;
        copy.Exit = Exit;
        copy.Revision = Revision;
        return copy;
    }

    public bool InBounds(int gx, int gy) => gx >= 0 && gx < Width && gy >= 0 && gy < Height;
    public bool InBounds(Vector2Int cell) => InBounds(cell.x, cell.y);

    public MazeCell GetCell(int gx, int gy)
    {
        if (!InBounds(gx, gy)) return MazeCell.Wall;
        return cells[gx, gy];
    }

    public MazeCell GetCell(Vector2Int cell) => GetCell(cell.x, cell.y);

    public bool IsWall(Vector2Int cell) => GetCell(cell.x, cell.y) == MazeCell.Wall;
    public bool IsFloor(Vector2Int cell) => InBounds(cell.x, cell.y) && cells[cell.x, cell.y] == MazeCell.Floor;

    public bool SetCell(int gx, int gy, MazeCell value)
    {
        if (!InBounds(gx, gy)) return false;
        if (cells[gx, gy] == value) return false;

        cells[gx, gy] = value;

        // Clearing floor under Spawn or Exit clears that marker so invalid hidden state cannot persist silently.
        if (value == MazeCell.Wall)
        {
            if (Spawn.HasValue && Spawn.Value.x == gx && Spawn.Value.y == gy)
                Spawn = null;
            if (Exit.HasValue && Exit.Value.x == gx && Exit.Value.y == gy)
                Exit = null;
        }

        Revision++;
        return true;
    }

    public bool SetSpawn(Vector2Int? spawn)
    {
        if (Spawn == spawn) return false;
        Spawn = spawn;
        Revision++;
        return true;
    }

    public bool SetExit(Vector2Int? exit)
    {
        if (Exit == exit) return false;
        Exit = exit;
        Revision++;
        return true;
    }

    public int CountFloorCells()
    {
        int count = 0;
        for (int x = 0; x < Width; x++)
            for (int y = 0; y < Height; y++)
                if (cells[x, y] == MazeCell.Floor)
                    count++;
        return count;
    }

    /// <summary>
    /// Verifies whether a 5x5 <see cref="MazePattern"/> rotated by <paramref name="quarterTurnsCW"/>
    /// can be stamped at module slot (<paramref name="slotX"/>, <paramref name="slotY"/>) with stride 4
    /// without contradicting any previously stamped shared-border cell.
    /// </summary>
    public bool CanStampPattern(MazePattern pattern, int quarterTurnsCW, int slotX, int slotY, out string mismatchReason)
    {
        mismatchReason = null;
        if (pattern == null)
        {
            mismatchReason = "Pattern is null.";
            return false;
        }

        int baseX = slotX * MazePattern.Stride;
        int baseY = slotY * MazePattern.Stride;

        if (!InBounds(baseX, baseY) || !InBounds(baseX + MazePattern.ModuleSize - 1, baseY + MazePattern.ModuleSize - 1))
        {
            mismatchReason = $"Slot ({slotX},{slotY}) exceeds grid bounds ({Width}x{Height}).";
            return false;
        }

        for (int row = 0; row < MazePattern.ModuleSize; row++)
        {
            int gy = baseY + (MazePattern.ModuleSize - 1 - row);
            for (int col = 0; col < MazePattern.ModuleSize; col++)
            {
                int gx = baseX + col;
                MazeCell incoming = pattern.GetCell(col, row, quarterTurnsCW);
                if (stampedMask[gx, gy] && cells[gx, gy] != incoming)
                {
                    mismatchReason = $"Shared border conflict at cell ({gx},{gy}): existing={cells[gx, gy]}, incoming={incoming}.";
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Stamps a 5x5 <see cref="MazePattern"/> onto module slot (<paramref name="slotX"/>, <paramref name="slotY"/>)
    /// using shared-edge stride 4. Returns false without partial mutation if any shared border cell conflicts.
    /// </summary>
    public bool TryStampPattern(MazePattern pattern, int quarterTurnsCW, int slotX, int slotY, out string error)
    {
        if (!CanStampPattern(pattern, quarterTurnsCW, slotX, slotY, out error))
            return false;

        int baseX = slotX * MazePattern.Stride;
        int baseY = slotY * MazePattern.Stride;

        for (int row = 0; row < MazePattern.ModuleSize; row++)
        {
            int gy = baseY + (MazePattern.ModuleSize - 1 - row);
            for (int col = 0; col < MazePattern.ModuleSize; col++)
            {
                int gx = baseX + col;
                cells[gx, gy] = pattern.GetCell(col, row, quarterTurnsCW);
                stampedMask[gx, gy] = true;
            }
        }

        Revision++;
        return true;
    }

    /// <summary>
    /// Serializes the grid into top-down ASCII rows ('#' = Wall, '.' = Floor, 'S' = Spawn, 'E' = Exit).
    /// </summary>
    public string[] ToAsciiLines(bool includeMarkers = true)
    {
        var lines = new string[Height];
        var rowChars = new char[Width];

        for (int r = 0; r < Height; r++)
        {
            int gy = Height - 1 - r;
            for (int gx = 0; gx < Width; gx++)
            {
                char ch = cells[gx, gy] == MazeCell.Floor ? '.' : '#';
                if (includeMarkers)
                {
                    if (Spawn.HasValue && Spawn.Value.x == gx && Spawn.Value.y == gy)
                        ch = 'S';
                    else if (Exit.HasValue && Exit.Value.x == gx && Exit.Value.y == gy)
                        ch = 'E';
                }
                rowChars[gx] = ch;
            }
            lines[r] = new string(rowChars);
        }

        return lines;
    }

    /// <summary>
    /// Parses top-down ASCII rows ('#' = Wall, '.' = Floor, 'S' = Spawn on Floor, 'E' = Exit on Floor).
    /// </summary>
    public static MazeGrid FromAsciiLines(IReadOnlyList<string> rowsTopToBottom)
    {
        if (rowsTopToBottom == null || rowsTopToBottom.Count == 0)
            throw new ArgumentException("ASCII row array cannot be null or empty.", nameof(rowsTopToBottom));

        int height = rowsTopToBottom.Count;
        int width = rowsTopToBottom[0] != null ? rowsTopToBottom[0].Length : 0;
        if (width <= 0)
            throw new ArgumentException("ASCII rows must have non-zero width.", nameof(rowsTopToBottom));

        var grid = new MazeGrid(width, height);
        for (int r = 0; r < height; r++)
        {
            string line = rowsTopToBottom[r] ?? string.Empty;
            if (line.Length != width)
                throw new ArgumentException($"Row {r} has length {line.Length}, expected {width}.", nameof(rowsTopToBottom));

            int gy = height - 1 - r;
            for (int gx = 0; gx < width; gx++)
            {
                char c = line[gx];
                switch (c)
                {
                    case '#':
                        grid.cells[gx, gy] = MazeCell.Wall;
                        break;
                    case '.':
                        grid.cells[gx, gy] = MazeCell.Floor;
                        break;
                    case 'S':
                        grid.cells[gx, gy] = MazeCell.Floor;
                        grid.Spawn = new Vector2Int(gx, gy);
                        break;
                    case 'E':
                        grid.cells[gx, gy] = MazeCell.Floor;
                        grid.Exit = new Vector2Int(gx, gy);
                        break;
                    default:
                        throw new ArgumentException($"Unsupported ASCII maze character '{c}' at ({gx},{gy}).", nameof(rowsTopToBottom));
                }
            }
        }

        return grid;
    }
}

