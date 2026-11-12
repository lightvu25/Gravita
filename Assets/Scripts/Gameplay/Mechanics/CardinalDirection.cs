using UnityEngine;

/// <summary>
/// Inspector-friendly cardinal direction used by directional tiles and mechanics.
/// </summary>
public enum CardinalDirection
{
    Up    = 0,
    Right = 1,
    Down  = 2,
    Left  = 3,
}

public static class CardinalDirectionUtil
{
    public static Vector2 ToVector2(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.Up    => Vector2.up,
            CardinalDirection.Right => Vector2.right,
            CardinalDirection.Down  => Vector2.down,
            CardinalDirection.Left  => Vector2.left,
            _                       => Vector2.right,
        };
    }

    public static CardinalDirection FromVector2(Vector2 v)
    {
        if (Mathf.Abs(v.x) >= Mathf.Abs(v.y))
            return v.x >= 0f ? CardinalDirection.Right : CardinalDirection.Left;
        return v.y >= 0f ? CardinalDirection.Up : CardinalDirection.Down;
    }

    public static Vector2 SnapToCardinal(Vector2 v)
    {
        if (v.sqrMagnitude < 0.0001f) return Vector2.zero;
        return Mathf.Abs(v.x) >= Mathf.Abs(v.y)
            ? new Vector2(Mathf.Sign(v.x), 0f)
            : new Vector2(0f, Mathf.Sign(v.y));
    }

    public static float ToZAngleDegrees(CardinalDirection direction)
    {
        return direction switch
        {
            CardinalDirection.Right => 0f,
            CardinalDirection.Up    => 90f,
            CardinalDirection.Left  => 180f,
            CardinalDirection.Down  => 270f,
            _                       => 0f,
        };
    }
}

