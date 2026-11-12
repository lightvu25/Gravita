/// <summary>
/// Supported cosmetic customization categories in Gravita.
/// v1.0 baseline exposes <see cref="PlayerSkin"/> exclusively; additional visual-only
/// categories remain reserved for future expansion without breaking serialized IDs.
/// </summary>
public enum CosmeticCategory
{
    PlayerSkin = 0,
    Trail = 1,
    DeathEffect = 2,
    VictoryEffect = 3,
    UITheme = 4,
    TileRoomAppearance = 5
}

