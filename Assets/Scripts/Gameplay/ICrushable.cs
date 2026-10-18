/// <summary>
/// A solid obstacle that may absorb a dash impact instead of stopping the
/// player. Checked by PlayerMovement when its collider blocks the slide:
/// if <see cref="TryCrush"/> returns true the player keeps moving.
/// </summary>
public interface ICrushable
{
    /// <summary>
    /// Returns true when the player smashed through (dash continues);
    /// false when the impact is treated as a normal wall stop.
    /// </summary>
    bool TryCrush(PlayerMovement player);
}
