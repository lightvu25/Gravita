using System;
using UnityEngine;

/// <summary>
/// Marker and coordinator for a maze Layout Prefab.
/// This component MUST be on the root GameObject of every Layout Prefab.
///
/// Responsibilities:
///   - Own the authoritative <see cref="LevelClock"/> for deterministic timed mechanics.
///   - Cache all gameplay elements in the layout (collectibles, hazards, exits,
///     maze tiles, cell interactions, switchables, clock listeners, cracked blocks,
///     spawn point) at Awake.
///   - Forward gameplay notifications (pickup collected, exit reached,
///     hazard hit) to listeners like the ObjectiveSystem and LevelFlowController.
///   - Coordinate a level-wide reset without knowing about LevelLoader or LevelData.
/// </summary>
public class LevelRoot : MonoBehaviour
{
    // ─────────────────────────────────────────────────────────────
    //  Events
    // ─────────────────────────────────────────────────────────────

    /// <summary>Fired when the player collects a Collectible.</summary>
    public event Action<Collectible> OnCollectibleCollected;

    /// <summary>Fired when the player reaches a LevelExit.</summary>
    public event Action<LevelExit> OnExitReached;

    /// <summary>Fired when the player touches a Hazard.</summary>
    public event Action<Hazard> OnHazardTriggered;

    // ─────────────────────────────────────────────────────────────
    //  Cached References (populated at Awake)
    // ─────────────────────────────────────────────────────────────

    private readonly LevelClock clock = new LevelClock();

    private Collectible[] collectibles;
    private Hazard[] hazards;
    private LevelExit[] exits;
    private MazeTile[] mazeTiles;
    private CrackedBlock[] crackedBlocks;
    private ICellInteraction[] cellInteractions;
    private ISwitchable[] switchables;
    private ILevelClockListener[] clockListeners;
    private PlayerSpawnPoint spawnPoint;
    private Grid layoutGrid;

    private int collectedCount;
    private int collectedStarCount;
    private int totalStarCount;
    private int collectedCoinCount;
    private int totalCoinCount;

    // ─────────────────────────────────────────────────────────────
    //  Public Accessors
    // ─────────────────────────────────────────────────────────────

    public LevelClock Clock => clock;
    public Collectible[] Collectibles => collectibles;
    public Hazard[] Hazards => hazards;
    public LevelExit[] Exits => exits;
    public MazeTile[] MazeTiles => mazeTiles;
    public CrackedBlock[] CrackedBlocks => crackedBlocks;
    public ICellInteraction[] CellInteractions => cellInteractions;
    public ISwitchable[] Switchables => switchables;
    public Grid LayoutGrid => layoutGrid;

    /// <summary>World position where the player (re)spawns.</summary>
    public Vector3 PlayerSpawnPosition => spawnPoint != null ? spawnPoint.Position : transform.position;

    /// <summary>Total collectibles placed in this layout.</summary>
    public int TotalCollectibles => collectibles != null ? collectibles.Length : 0;

    /// <summary>Collectibles picked up since the last reset.</summary>
    public int CollectedCount => collectedCount;

    /// <summary>Star collectibles picked up since the last reset — feeds the completion rating.</summary>
    public int CollectedStarCount => collectedStarCount;

    /// <summary>Star collectibles placed in this layout. 0 = level uses the objective-based rating.</summary>
    public int TotalStarCount => totalStarCount;

    /// <summary>Coin collectibles picked up since the last reset.</summary>
    public int CollectedCoinCount => collectedCoinCount;

    /// <summary>Total Coin collectibles placed in this layout.</summary>
    public int TotalCoinCount => totalCoinCount;

    // ─────────────────────────────────────────────────────────────
    //  Unity Lifecycle
    // ─────────────────────────────────────────────────────────────

    private void Awake()
    {
        EnsureInitialized();
    }

    public void EnsureInitialized()
    {
        if (collectibles != null) return;

        collectibles = GetComponentsInChildren<Collectible>(includeInactive: true);
        totalStarCount = 0;
        totalCoinCount = 0;
        for (int i = 0; i < collectibles.Length; i++)
        {
            Collectible c = collectibles[i];
            if (c == null) continue;

            if (c.IsStar)
                totalStarCount += c.Value;
            else if (c.IsCoin)
                totalCoinCount++;
        }

        hazards = GetComponentsInChildren<Hazard>(includeInactive: true);
        exits = GetComponentsInChildren<LevelExit>(includeInactive: true);
        mazeTiles = GetComponentsInChildren<MazeTile>(includeInactive: true);
        crackedBlocks = GetComponentsInChildren<CrackedBlock>(includeInactive: true);
        cellInteractions = GetComponentsInChildren<ICellInteraction>(includeInactive: true);
        switchables = GetComponentsInChildren<ISwitchable>(includeInactive: true);
        clockListeners = GetComponentsInChildren<ILevelClockListener>(includeInactive: true);
        spawnPoint = GetComponentInChildren<PlayerSpawnPoint>(includeInactive: true);
        layoutGrid = GetComponentInChildren<Grid>(includeInactive: true);

        if (spawnPoint == null)
            Debug.LogWarning($"[LevelRoot] '{name}': No PlayerSpawnPoint found — the player will not be repositioned.", this);
        if (exits == null || exits.Length == 0)
            Debug.LogWarning($"[LevelRoot] '{name}': No LevelExit found — ReachExit objectives can never complete.", this);
    }

    /// <summary>
    /// Advances the deterministic <see cref="LevelClock"/> and notifies all active
    /// <see cref="ILevelClockListener"/> components in the layout.
    /// Driven exclusively by <see cref="LevelFlowController"/> while Playing.
    /// </summary>
    public void TickClock(float deltaTime)
    {
        if (deltaTime <= 0f) return;
        EnsureInitialized();

        clock.Tick(deltaTime);

        if (clockListeners == null) return;
        float t = clock.Time;
        for (int i = 0; i < clockListeners.Length; i++)
        {
            ILevelClockListener listener = clockListeners[i];
            if (listener == null) continue;
            if (listener is Behaviour b && !b.isActiveAndEnabled) continue;
            listener.OnLevelClockTick(t, deltaTime);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Notifications (called by gameplay elements)
    // ─────────────────────────────────────────────────────────────

    public void NotifyCollectibleCollected(Collectible collectible)
    {
        collectedCount++;
        if (collectible != null)
        {
            if (collectible.IsStar)
                collectedStarCount += collectible.Value;
            else if (collectible.IsCoin)
                collectedCoinCount++;
        }
        OnCollectibleCollected?.Invoke(collectible);
    }

    public void NotifyExitReached(LevelExit exit)
    {
        OnExitReached?.Invoke(exit);
    }

    public void NotifyHazardTriggered(Hazard hazard)
    {
        OnHazardTriggered?.Invoke(hazard);
    }

    // ─────────────────────────────────────────────────────────────
    //  Level-Wide Reset
    // ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Resets every gameplay element in this layout to its initial state.
    /// Called by LevelFlowController.ResetLevel().
    /// </summary>
    public void ResetAll()
    {
        EnsureInitialized();
        clock.Reset();

        collectedCount = 0;
        collectedStarCount = 0;
        collectedCoinCount = 0;

        foreach (var collectible in collectibles)
        {
            if (collectible != null)
                collectible.ResetCollectible();
        }

        if (switchables != null)
        {
            for (int i = 0; i < switchables.Length; i++)
                switchables[i]?.ResetSwitchState();
        }

        if (hazards != null)
        {
            foreach (var hazard in hazards)
            {
                if (hazard != null)
                    hazard.ResetHazard();
            }
        }

        foreach (var exit in exits)
        {
            if (exit != null)
                exit.ResetExit();
        }

        foreach (var tile in mazeTiles)
        {
            if (tile != null)
                tile.ResetTile();
        }

        foreach (var block in crackedBlocks)
        {
            if (block != null)
                block.ResetBlock();
        }

        if (clockListeners != null)
        {
            for (int i = 0; i < clockListeners.Length; i++)
                clockListeners[i]?.OnLevelClockReset();
        }

        Physics2D.SyncTransforms();
    }
}
