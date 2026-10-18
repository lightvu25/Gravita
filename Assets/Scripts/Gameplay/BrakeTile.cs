using UnityEngine;

/// <summary>
/// Mud/Tar brake tile: absorbs all momentum and forces a dead stop at the
/// tile centre. Lets a level offer mid-corridor turns without a wall.
/// </summary>
public class BrakeTile : MazeTile
{
    protected override void OnPlayerEnter(PlayerMovement player)
    {
        player.HaltAt(CellCenter);
    }
}
