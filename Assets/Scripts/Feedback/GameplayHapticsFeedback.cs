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
        {
            movement.OnWallImpact += HandleWallImpact;
            movement.OnVelocityTierChanged += HandleVelocityTierChanged;
        }

        MazeTile.OnAnyPlayerInteracted += HandleTileInteracted;
        CrackedBlock.OnAnyBroken += HandleBlockBroken;
        CloseCallDetector.OnCloseCallTriggered += HandleCloseCall;
        Collectible.OnAnyStarCollected += HandleStarCollected;
        isBound = true;
    }

    internal void UnbindListeners()
    {
        if (!isBound)
            return;

        if (movement != null)
        {
            movement.OnWallImpact -= HandleWallImpact;
            movement.OnVelocityTierChanged -= HandleVelocityTierChanged;
        }

        MazeTile.OnAnyPlayerInteracted -= HandleTileInteracted;
        CrackedBlock.OnAnyBroken -= HandleBlockBroken;
        CloseCallDetector.OnCloseCallTriggered -= HandleCloseCall;
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        isBound = false;
    }

    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero)
            return;

        VelocityTier tier = (movement != null && movement.CurrentTier != VelocityTier.None)
            ? movement.CurrentTier
            : VelocityTier.Drift;

        HapticEvent evt = tier switch
        {
            VelocityTier.KineticRam => HapticEvent.KineticRamImpact,
            VelocityTier.Cruise     => HapticEvent.CruiseImpact,
            _                       => HapticEvent.WallImpact,
        };
        GameHaptics.Play(evt);
    }

    private void HandleVelocityTierChanged(VelocityTier tier)
    {
        if (tier == VelocityTier.KineticRam)
            GameHaptics.Play(HapticEvent.TierUp);
    }

    private void HandleBlockBroken(CrackedBlock _)
    {
        GameHaptics.Play(HapticEvent.BlockSmash);
    }

    private void HandleCloseCall(Vector3 _)
    {
        GameHaptics.Play(HapticEvent.CloseCall);
    }

    private void HandleStarCollected(Collectible _)
    {
        GameHaptics.Play(HapticEvent.StarCollect);
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

            case DeflectionWedge _ when context.Kind == InteractionKind.Deflect:
            case VectorPad _ when context.Kind == InteractionKind.Deflect:
                if (movement == null || !movement.IsPhasing)
                    GameHaptics.Play(HapticEvent.PadRedirect);
                break;

            case VectorLauncher _ when context.Kind == InteractionKind.Launch:
                GameHaptics.Play(HapticEvent.LaunchStart);
                break;

            case CircuitSwitch _ when context.Kind == InteractionKind.Switch:
                GameHaptics.Play(HapticEvent.SwitchToggle);
                break;

            case BrittleFloor _ when context.Kind == InteractionKind.Break:
            case FailingTile _ when context.Kind == InteractionKind.Break:
                GameHaptics.Play(HapticEvent.BlockSmash);
                break;

            case BrittleFloor _ when context.Kind == InteractionKind.Crack:
            case FailingTile _ when context.Kind == InteractionKind.Crack:
                GameHaptics.Play(HapticEvent.FloorCrack);
                break;
        }
    }
}
