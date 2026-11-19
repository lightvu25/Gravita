using System;

/// <summary>
/// Immutable end-of-run DTO emitted by <see cref="MazeRunnerService"/> for UI &amp; analytics.
/// </summary>
public readonly struct RunnerRunSummary
{
    public readonly RunnerModeKind Mode;
    public readonly int ClearedRooms;
    public readonly int TargetRooms;
    public readonly int InRunCoinsPickedUp;
    public readonly int TotalCoinsGranted;
    public readonly int TotalExpGranted;
    public readonly int BestRoomsRecord;
    public readonly bool IsNewBest;
    public readonly bool CompletedTarget;
    public readonly string EndReason;

    public RunnerRunSummary(
        RunnerModeKind mode,
        int clearedRooms,
        int targetRooms,
        int inRunCoinsPickedUp,
        int totalCoinsGranted,
        int totalExpGranted,
        int bestRoomsRecord,
        bool isNewBest,
        bool completedTarget,
        string endReason)
    {
        Mode = mode;
        ClearedRooms = clearedRooms;
        TargetRooms = targetRooms;
        InRunCoinsPickedUp = inRunCoinsPickedUp;
        TotalCoinsGranted = totalCoinsGranted;
        TotalExpGranted = totalExpGranted;
        BestRoomsRecord = bestRoomsRecord;
        IsNewBest = isNewBest;
        CompletedTarget = completedTarget;
        EndReason = endReason;
    }
}

/// <summary>
/// Pure C# run-state tracker for a single Maze Runner attempt (Endless / 10 Rooms / 20 Rooms).
/// Zero Unity scene dependency for deterministic EditMode testing.
/// </summary>
public sealed class RunnerSessionState
{
    public RunnerModeKind Mode { get; }
    public int MasterSeed { get; }
    public int TargetRoomCount { get; }

    public int CurrentRoomNumber { get; private set; }
    public int ClearedRooms { get; private set; }
    public int InRunCoinsPickedUp { get; private set; }
    public bool IsActive { get; private set; }
    public bool IsCompletedVictory { get; private set; }

    public RunnerSessionState(RunnerModeKind mode, int masterSeed)
    {
        Mode = mode;
        MasterSeed = masterSeed;
        TargetRoomCount = RunnerModeConfig.GetTargetRoomCount(mode);
        CurrentRoomNumber = 1;
        ClearedRooms = 0;
        InRunCoinsPickedUp = 0;
        IsActive = true;
        IsCompletedVictory = false;
    }

    public int GetRoomSeed(int roomNumber)
    {
        unchecked
        {
            uint h = (uint)MasterSeed;
            h = (h ^ (uint)Math.Max(1, roomNumber)) * 0x9E3779B9u;
            h ^= h >> 15;
            h *= 0x85EBCA6Bu;
            h ^= h >> 13;
            return (int)(h & 0x7FFFFFFFu);
        }
    }

    public void AddInRunCoins(int amount)
    {
        if (IsActive && amount > 0)
            InRunCoinsPickedUp += amount;
    }

    /// <summary>
    /// Marks the active room as cleared and advances to the next room.
    /// Returns true if a finite room-count mode (e.g. 10 or 20 rooms) just reached its goal.
    /// </summary>
    public bool RecordRoomCleared()
    {
        if (!IsActive)
            return false;

        ClearedRooms++;
        if (TargetRoomCount > 0 && ClearedRooms >= TargetRoomCount)
        {
            IsCompletedVictory = true;
            IsActive = false;
            return true;
        }

        CurrentRoomNumber = ClearedRooms + 1;
        return false;
    }

    public void EndRun()
    {
        IsActive = false;
    }
}
