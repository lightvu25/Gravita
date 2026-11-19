/// <summary>
/// Semantic haptic feedback events used across Gravita UI and gameplay presentation.
/// Values are contiguous from 0 so <see cref="GameHaptics"/> can index cooldown tables without allocation.
/// </summary>
public enum HapticEvent
{
    /// <summary>Very light selection tick for confirmed UI button interactions.</summary>
    UISelect = 0,

    /// <summary>Light impact when the sliding player collides with a solid wall and stops.</summary>
    WallImpact = 1,

    /// <summary>Short crisp stop when a Gravity Block or Brake Tile halts the player at its cell center.</summary>
    GravityAnchor = 2,

    /// <summary>Light-to-medium pulse when a Vector Pad redirects the player's slide direction.</summary>
    PadRedirect = 3,

    /// <summary>Medium impact when a Vector Launcher initiates a launch.</summary>
    LaunchStart = 4,

    /// <summary>Light confirmation tick when a Circuit Switch changes state on entry.</summary>
    SwitchToggle = 5,

    /// <summary>Strong failure feedback triggered once per player death transition.</summary>
    Death = 6,

    /// <summary>Medium impact when sliding into a wall at Cruise tier.</summary>
    CruiseImpact = 7,

    /// <summary>Rigid impact when slamming a wall at KineticRam tier.</summary>
    KineticRamImpact = 8,

    /// <summary>Rigid impact when shattering a CrackedBlock or BrittleFloor / FailingTile.</summary>
    BlockSmash = 9,

    /// <summary>Subtle soft pulse when entering KineticRam tier.</summary>
    TierUp = 10,

    /// <summary>Light selection pulse when scraping past a lethal hazard in Close Call.</summary>
    CloseCall = 11,

    /// <summary>Medium confirmation pulse when picking up a Star.</summary>
    StarCollect = 12,

    /// <summary>Light selection crackle when arming a BrittleFloor or FailingTile.</summary>
    FloorCrack = 13,
}

