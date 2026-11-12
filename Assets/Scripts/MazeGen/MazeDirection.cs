using System;
using UnityEngine;

/// <summary>
/// Cardinal grid directions used across the Gravita Maze Generator, Validator, and Preview tools.
/// Grid coordinates follow Unity Tilemap conventions: +X = East, +Y = North.
/// </summary>
public enum MazeDir
{
    North = 0,
    East  = 1,
    South = 2,
    West  = 3,
}

/// <summary>
/// 4-bit port mask describing which module edges expose an open centre port.
/// </summary>
[Flags]
public enum MazeSideMask
{
    None  = 0,
    North = 1 << 0, // 1
    East  = 1 << 1, // 2
    South = 1 << 2, // 4
    West  = 1 << 3, // 8
    All   = North | East | South | West, // 15
}

/// <summary>
/// Allowed clockwise quarter-turn rotations for a 5x5 <see cref="MazePattern"/>.
/// </summary>
[Flags]
public enum MazeRotations
{
    None = 0,
    R0   = 1 << 0,
    R90  = 1 << 1,
    R180 = 1 << 2,
    R270 = 1 << 3,
    All  = R0 | R90 | R180 | R270,
}

public static class MazeDirectionUtil
{
    public static readonly MazeDir[] AllDirections =
    {
        MazeDir.North,
        MazeDir.East,
        MazeDir.South,
        MazeDir.West,
    };

    public static Vector2Int ToStep(MazeDir dir) => dir switch
    {
        MazeDir.North => Vector2Int.up,
        MazeDir.East  => Vector2Int.right,
        MazeDir.South => Vector2Int.down,
        MazeDir.West  => Vector2Int.left,
        _             => Vector2Int.zero,
    };

    public static Vector2 ToWorldVector(MazeDir dir) => dir switch
    {
        MazeDir.North => Vector2.up,
        MazeDir.East  => Vector2.right,
        MazeDir.South => Vector2.down,
        MazeDir.West  => Vector2.left,
        _             => Vector2.zero,
    };

    public static MazeSideMask ToMask(MazeDir dir) => dir switch
    {
        MazeDir.North => MazeSideMask.North,
        MazeDir.East  => MazeSideMask.East,
        MazeDir.South => MazeSideMask.South,
        MazeDir.West  => MazeSideMask.West,
        _             => MazeSideMask.None,
    };

    public static MazeDir Opposite(MazeDir dir) => dir switch
    {
        MazeDir.North => MazeDir.South,
        MazeDir.East  => MazeDir.West,
        MazeDir.South => MazeDir.North,
        MazeDir.West  => MazeDir.East,
        _             => MazeDir.North,
    };

    public static MazeDir RotateClockwise(MazeDir dir, int quarterTurnsCW)
    {
        int norm = ((quarterTurnsCW % 4) + 4) % 4;
        return (MazeDir)(((int)dir + norm) % 4);
    }

    public static MazeSideMask RotateMaskClockwise(MazeSideMask mask, int quarterTurnsCW)
    {
        int norm = ((quarterTurnsCW % 4) + 4) % 4;
        if (norm == 0 || mask == MazeSideMask.None)
            return mask;

        int bits = (int)(mask & MazeSideMask.All);
        int rotated = ((bits << norm) | (bits >> (4 - norm))) & 0xF;
        return (MazeSideMask)rotated;
    }

    public static MazeRotations QuarterTurnsToFlag(int quarterTurnsCW)
    {
        int norm = ((quarterTurnsCW % 4) + 4) % 4;
        return norm switch
        {
            0 => MazeRotations.R0,
            1 => MazeRotations.R90,
            2 => MazeRotations.R180,
            3 => MazeRotations.R270,
            _ => MazeRotations.R0,
        };
    }

    public static int CountBits(MazeSideMask mask)
    {
        int v = (int)(mask & MazeSideMask.All);
        int count = 0;
        while (v != 0)
        {
            count += v & 1;
            v >>= 1;
        }
        return count;
    }
}

