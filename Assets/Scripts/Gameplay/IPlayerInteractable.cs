using System;
using UnityEngine;

/// <summary>
/// Semantic category of a player interaction with a world object.
/// </summary>
public enum InteractionKind
{
    /// <summary>Matches any interaction when used as a filter in <see cref="InteractionEffectPlayer"/>.</summary>
    Any = 0,
    /// <summary>Pickup collected (Star, Coin, etc.).</summary>
    Collect = 1,
    /// <summary>Object smashed or shattered (CrackedBlock, BrittleFloor, breakable Hazard).</summary>
    Break = 2,
    /// <summary>Object cracked or stepped on without breaking.</summary>
    Crack = 3,
    /// <summary>Player entered a tile or trigger zone.</summary>
    Enter = 4,
    /// <summary>Player redirected by a DeflectionWedge.</summary>
    Deflect = 5,
    /// <summary>Player halted by a BrakeTile.</summary>
    Brake = 6,
    /// <summary>Player collided with a Hazard or blunt obstacle face.</summary>
    Hit = 7,
    /// <summary>Player reached the LevelExit.</summary>
    Exit = 8,
    /// <summary>Player activated a CircuitSwitch.</summary>
    Switch = 9,
    /// <summary>Player launched via a VectorLauncher or VectorPad.</summary>
    Launch = 10,
}

/// <summary>
/// Immutable payload describing a player interaction with an <see cref="IPlayerInteractable"/> world entity.
/// </summary>
public readonly struct InteractionContext
{
    public readonly InteractionKind Kind;
    public readonly Vector3 VisualCenter;
    public readonly Vector2 Direction;

    public InteractionContext(InteractionKind kind, Vector3 visualCenter, Vector2 direction = default)
    {
        Kind = kind;
        VisualCenter = visualCenter;
        Direction = direction;
    }
}

/// <summary>
/// Domain contract implemented by gameplay objects that the player can interact with
/// (Star, Coin, CrackedBlock, BrittleFloor, BrakeTile, DeflectionWedge, Hazard, LevelExit).
/// Presentation components such as <see cref="InteractionEffectPlayer"/> subscribe to
/// <see cref="OnPlayerInteracted"/> to spawn pooled VFX without coupling gameplay logic to visuals.
/// </summary>
public interface IPlayerInteractable
{
    event Action<InteractionContext> OnPlayerInteracted;
}

