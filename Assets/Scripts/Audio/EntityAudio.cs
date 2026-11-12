using UnityEngine;

/// <summary>
/// Gameplay & Entity audio bridge (Layer 3 of the 4-layer audio architecture).
/// Place on a dedicated GameObject in GameScene (or on the Player/Level Manager) and wire
/// <see cref="SoundConfig"/> and scene controllers in the Inspector.
/// </summary>
public class EntityAudio : MonoBehaviour
{
    [Header("Audio Config (ScriptableObject)")]
    [Tooltip("Sound catalog. If left empty, falls back to SoundManager.Instance.Config.")]
    [SerializeField] private SoundConfig soundConfig;

    [Header("Scene References (Inspector Wired)")]
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private LevelFlowController flowController;
    [SerializeField] private PowerUpController powerUpController;

    private bool playerSubscribed;
    private bool flowSubscribed;
    private bool powerUpSubscribed;

    private SoundConfig ActiveConfig =>
        soundConfig != null
            ? soundConfig
            : (SoundManager.Instance != null ? SoundManager.Instance.Config : null);

    private void OnEnable()
    {
        SubscribeReferences();

        CrackedBlock.OnAnyBroken += HandleCrackedBlockBroken;
        LevelFlowController.OnAnyHazardContacted += HandleAnyHazardContacted;
        PowerUpController.OnAnyPowerUpUsed += HandleAnyPowerUpUsed;
        Collectible.OnAnyStarCollected += HandleStarCollected;
        Collectible.OnAnyCoinCollected += HandleCoinCollected;
    }

    private void OnDisable()
    {
        UnsubscribeReferences();

        CrackedBlock.OnAnyBroken -= HandleCrackedBlockBroken;
        LevelFlowController.OnAnyHazardContacted -= HandleAnyHazardContacted;
        PowerUpController.OnAnyPowerUpUsed -= HandleAnyPowerUpUsed;
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        Collectible.OnAnyCoinCollected -= HandleCoinCollected;
    }

    private void SubscribeReferences()
    {
        if (playerMovement != null && !playerSubscribed)
        {
            playerSubscribed = true;
            playerMovement.OnMoveStarted += HandlePlayerMoveStarted;
            playerMovement.OnWallImpact += HandlePlayerWallImpact;
            playerMovement.OnRedirected += HandlePlayerRedirected;
        }

        if (flowController != null && !flowSubscribed)
        {
            flowSubscribed = true;
            flowController.OnLevelStarted += HandleLevelStarted;
            flowController.OnLevelCompleted += HandleLevelCompleted;
            flowController.OnLevelFailed += HandleLevelFailed;
        }

        if (powerUpController != null && !powerUpSubscribed)
        {
            powerUpSubscribed = true;
            powerUpController.OnPowerUpActivated += HandlePowerUpActivated;
        }
    }

    private void UnsubscribeReferences()
    {
        if (playerMovement != null && playerSubscribed)
        {
            playerSubscribed = false;
            playerMovement.OnMoveStarted -= HandlePlayerMoveStarted;
            playerMovement.OnWallImpact -= HandlePlayerWallImpact;
            playerMovement.OnRedirected -= HandlePlayerRedirected;
        }

        if (flowController != null && flowSubscribed)
        {
            flowSubscribed = false;
            flowController.OnLevelStarted -= HandleLevelStarted;
            flowController.OnLevelCompleted -= HandleLevelCompleted;
            flowController.OnLevelFailed -= HandleLevelFailed;
        }

        if (powerUpController != null && powerUpSubscribed)
        {
            powerUpSubscribed = false;
            powerUpController.OnPowerUpActivated -= HandlePowerUpActivated;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Gameplay Event Handlers
    // ─────────────────────────────────────────────────────────────

    private void HandlePlayerMoveStarted(Vector2 direction)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.moveStart);
    }

    private void HandlePlayerWallImpact(Vector2 impactNormal)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg == null)
            return;

        VelocityTier tier = playerMovement != null ? playerMovement.LastImpactTier : VelocityTier.Drift;
        PlayEntry(cfg.GetWallImpactByTier(tier));
    }

    private void HandlePlayerRedirected(Vector2 newDirection)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.wedgeRedirect);
    }

    private void HandleCrackedBlockBroken(CrackedBlock block)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.crackedWallBreak);
    }

    private void HandleLevelStarted()
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.levelStart);
    }

    private void HandleLevelCompleted()
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.goalReached);
    }

    private void HandleAnyHazardContacted(LevelFlowController controller, Hazard hazard)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.hazardHit);
    }

    private void HandleLevelFailed(string reason)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.levelFailed);
    }

    private void HandlePowerUpActivated(PowerUpType type)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.powerUpActivated);
    }

    private void HandleAnyPowerUpUsed(PowerUpType type)
    {
        if (powerUpSubscribed)
            return;

        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.powerUpActivated);
    }

    private void HandleStarCollected(Collectible star)
    {
        if (star == null || star.HasCustomCollectSfx)
            return;

        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.starCollect);
    }

    private void HandleCoinCollected(Collectible coin)
    {
        if (coin == null || coin.HasCustomCollectSfx)
            return;

        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.coinCollect);
    }

    private static void PlayEntry(SoundEntry entry)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(entry);
        }
    }
}
