using UnityEngine;

/// <summary>
/// Phase A Kinetic Runway feedback bridge:
/// drives 33ms-per-tile ascending synth ticks, Cruise/KineticRam audio cues,
/// dynamic base orthographic camera zoom (5.50 -> 5.72 -> 6.02), and high-tier impact zoom dips.
/// </summary>
public class KineticRunwayFeedback : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private SoundConfig soundConfig;

    [Header("Tile Escalation Audio")]
    [SerializeField, Range(0.5f, 1.5f)] private float baseTilePitch = 0.94f;
    [SerializeField, Min(0f)] private float pitchPerTile = 0.045f;
    [SerializeField, Range(0.8f, 2f)] private float maxTilePitch = 1.44f;
    [SerializeField, Range(0f, 1f)] private float driftVolumeScale = 0.50f;
    [SerializeField, Range(0f, 1f)] private float cruiseVolumeScale = 0.78f;
    [SerializeField, Range(0f, 1f)] private float kineticVolumeScale = 1.00f;

    [Header("Tier Camera Zoom")]
    [SerializeField, Min(0f)] private float cruiseOrthoOffset = 0.22f;
    [SerializeField, Min(0f)] private float kineticOrthoOffset = 0.52f;
    [SerializeField] private float impactZoomDip = -0.18f;
    [SerializeField, Min(0.01f)] private float impactZoomInDuration = 0.045f;
    [SerializeField, Min(0.01f)] private float impactZoomOutDuration = 0.14f;

    private bool subscribed;

    private SoundConfig ActiveConfig =>
        soundConfig != null
            ? soundConfig
            : (SoundManager.Instance != null ? SoundManager.Instance.Config : null);

    private void Awake()
    {
        ResolvePlayer();
    }

    private void OnEnable()
    {
        ResolvePlayer();
        BindPlayer();
        CrackedBlock.OnAnyBroken += HandleBlockBroken;
    }

    private void OnDisable()
    {
        UnbindPlayer();
        CrackedBlock.OnAnyBroken -= HandleBlockBroken;
    }

    private void ResolvePlayer()
    {
        if (playerMovement != null)
            return;

        playerMovement = PlayerMovement.Instance != null
            ? PlayerMovement.Instance
            : (GetComponent<PlayerMovement>() ?? FindFirstObjectByType<PlayerMovement>());
    }

    private void BindPlayer()
    {
        if (subscribed || playerMovement == null)
            return;

        subscribed = true;
        playerMovement.OnTileCrossed += HandleTileCrossed;
        playerMovement.OnVelocityTierChanged += HandleVelocityTierChanged;
        playerMovement.OnMoveStopped += HandleMoveStopped;
        playerMovement.OnWallImpact += HandleWallImpact;
    }

    private void UnbindPlayer()
    {
        if (!subscribed || playerMovement == null)
            return;

        subscribed = false;
        playerMovement.OnTileCrossed -= HandleTileCrossed;
        playerMovement.OnVelocityTierChanged -= HandleVelocityTierChanged;
        playerMovement.OnMoveStopped -= HandleMoveStopped;
        playerMovement.OnWallImpact -= HandleWallImpact;
    }

    private void HandleTileCrossed(int tilesTraveled)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg == null || SoundManager.Instance == null)
            return;

        float pitch = Mathf.Clamp(baseTilePitch + pitchPerTile * tilesTraveled, baseTilePitch, maxTilePitch);
        VelocityTier tier = playerMovement != null ? playerMovement.CurrentTier : VelocityTierUtil.FromTiles(tilesTraveled);
        float volScale = tier switch
        {
            VelocityTier.KineticRam => kineticVolumeScale,
            VelocityTier.Cruise     => cruiseVolumeScale,
            _                       => driftVolumeScale,
        };

        SoundManager.Instance.PlaySFXWithPitch(cfg.tileTick, pitch, volScale);
    }

    private void HandleVelocityTierChanged(VelocityTier tier)
    {
        CinemachineCameraZoom2D zoom = CinemachineCameraZoom2D.Instance;
        if (zoom != null)
        {
            switch (tier)
            {
                case VelocityTier.Cruise:
                    zoom.SetTargetOrthographicSize(zoom.NormalOrthographicSize + cruiseOrthoOffset);
                    break;
                case VelocityTier.KineticRam:
                    zoom.SetTargetOrthographicSize(zoom.NormalOrthographicSize + kineticOrthoOffset);
                    break;
                default:
                    zoom.SetNormalOrthographicSize();
                    break;
            }
        }

        SoundConfig cfg = ActiveConfig;
        if (cfg == null || SoundManager.Instance == null)
            return;

        if (tier == VelocityTier.Cruise)
            SoundManager.Instance.PlaySFX(cfg.tierUpCruise);
        else if (tier == VelocityTier.KineticRam)
            SoundManager.Instance.PlaySFX(cfg.tierUpKinetic);
    }

    private void HandleMoveStopped()
    {
        if (CinemachineCameraZoom2D.Instance != null)
            CinemachineCameraZoom2D.Instance.SetNormalOrthographicSize();
    }

    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero || CinemachineCameraZoom2D.Instance == null)
            return;

        VelocityTier tier = (playerMovement != null && playerMovement.CurrentTier != VelocityTier.None)
            ? playerMovement.CurrentTier
            : VelocityTier.Drift;

        if (tier == VelocityTier.Cruise || tier == VelocityTier.KineticRam)
            CinemachineCameraZoom2D.Instance.PulseZoom(impactZoomDip, impactZoomInDuration, impactZoomOutDuration);
    }

    private void HandleBlockBroken(CrackedBlock _)
    {
        if (CinemachineCameraZoom2D.Instance != null)
            CinemachineCameraZoom2D.Instance.PulseZoom(impactZoomDip, impactZoomInDuration, impactZoomOutDuration);
    }
}
