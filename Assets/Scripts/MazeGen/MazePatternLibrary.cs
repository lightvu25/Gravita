using System.Collections.Generic;
using UnityEngine;

public readonly struct MazePatternPlacement
{
    public readonly MazePattern Pattern;
    public readonly int QuarterTurnsCW;
    public readonly MazeSideMask PortMask;

    public MazePatternPlacement(MazePattern pattern, int quarterTurnsCW, MazeSideMask portMask)
    {
        Pattern = pattern;
        QuarterTurnsCW = quarterTurnsCW;
        PortMask = portMask;
    }
}

/// <summary>
/// Registry of <see cref="MazePattern"/> modules used by the Maze Generator.
/// Provides side-mask candidate lookup across allowed rotations and full library integrity validation.
/// </summary>
[CreateAssetMenu(menuName = "Gravita/Maze/Maze Pattern Library", fileName = "DefaultMazePatternLibrary")]
public class MazePatternLibrary : ScriptableObject
{
    public int libraryVersion = 1;
    public List<MazePattern> patterns = new List<MazePattern>();

    public List<MazePatternPlacement> GetCandidates(
        MazeSideMask requiredPorts,
        HashSet<MazePatternGroup> allowedGroups = null)
    {
        var results = new List<MazePatternPlacement>();
        if (patterns == null) return results;

        for (int i = 0; i < patterns.Count; i++)
        {
            MazePattern pattern = patterns[i];
            if (pattern == null) continue;
            if (allowedGroups != null && !allowedGroups.Contains(pattern.group))
                continue;

            // Avoid duplicate symmetrical placements when rotated grids are identical
            var seenRotations = new HashSet<string>();

            for (int rot = 0; rot < 4; rot++)
            {
                if (!pattern.IsRotationAllowed(rot)) continue;
                MazeSideMask portMask = pattern.GetPortMask(rot);
                if (portMask != requiredPorts) continue;

                string signature = BuildRotatedSignature(pattern, rot);
                if (!seenRotations.Add(signature))
                    continue;

                results.Add(new MazePatternPlacement(pattern, rot, portMask));
            }
        }

        return results;
    }

    public bool ValidateLibrary(out List<string> errors)
    {
        errors = new List<string>();
        if (patterns == null || patterns.Count == 0)
        {
            errors.Add("Pattern library contains no patterns.");
            return false;
        }

        var ids = new HashSet<string>();
        for (int i = 0; i < patterns.Count; i++)
        {
            MazePattern p = patterns[i];
            if (p == null)
            {
                errors.Add($"Pattern slot [{i}] is null.");
                continue;
            }

            if (!ids.Add(p.id))
                errors.Add($"Duplicate pattern ID '{p.id}' in library.");

            if (!p.ValidateDefinition(out List<string> patternErrors))
                errors.AddRange(patternErrors);
        }

        // Verify all 15 non-empty 4-bit port masks (1..15) have at least one valid placement.
        for (int maskBits = 1; maskBits <= 15; maskBits++)
        {
            var mask = (MazeSideMask)maskBits;
            var candidates = GetCandidates(mask);
            if (candidates.Count == 0)
                errors.Add($"Pattern library has no candidate covering port mask '{mask}' ({maskBits}).");
        }

        return errors.Count == 0;
    }

    private static string BuildRotatedSignature(MazePattern pattern, int quarterTurnsCW)
    {
        char[] buf = new char[MazePattern.ModuleSize * MazePattern.ModuleSize];
        int idx = 0;
        for (int r = 0; r < MazePattern.ModuleSize; r++)
        {
            for (int c = 0; c < MazePattern.ModuleSize; c++)
            {
                buf[idx++] = pattern.GetCell(c, r, quarterTurnsCW) == MazeCell.Floor ? '.' : '#';
            }
        }
        return new string(buf);
    }
}

