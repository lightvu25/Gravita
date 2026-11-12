using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(PlayerMovement))]
public class GameplayHapticsFeedback : MonoBehaviour
{
    private PlayerMovement movement;
    private bool isBound;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void OnEnable()
    {
        BindListeners();
    }

    private void OnDisable()
    {
        UnbindListeners();
    }

    internal void BindListeners()
    {
        if (isBound)
            return;

        if (movement == null)
            movement = GetComponent<PlayerMovement>();

        if (movement != null)
            movement.OnWallImpact += HandleWallImpact;

        MazeTile.OnAnyPlayerInteracted += HandleTileInteracted;
        isBound = true;
    }

    internal void UnbindListeners()
    {
        if (!isBound)
            return;

        if (movement != null)
            movement.OnWallImpact -= HandleWallImpact;

        MazeTile.OnAnyPlayerInteracted -= HandleTileInteracted;
        isBound = false;
    }

    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero)
            return;

        GameHaptics.Play(HapticEvent.WallImpact);
    }

    private void HandleTileInteracted(MazeTile tile, InteractionContext context)
    {
        if (tile == null)
            return;

        switch (tile)
        {
            case GravityBlock _ when context.Kind == InteractionKind.Brake:
            case BrakeTile _ when context.Kind == InteractionKind.Brake:
                GameHaptics.Play(HapticEvent.GravityAnchor);
                break;
        }
    }
}
