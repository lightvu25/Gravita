using System;
using UnityEngine;

/// <summary>
/// Gameplay & Entity audio bridge (Layer 3 of the 4-layer audio architecture).
/// Place on a dedicated GameObject in GameScene (or on the Player/Level Manager) and wire
/// <see cref="SoundConfig"/> and scene controllers in the Inspector.
/// </summary>
public class EntityAudio : MonoBehaviour
{
    private const float CoinStreakWindowSeconds = 0.65f;
    private const int MaxCoinStreakSemitones = 12;
    private const float SemitoneRatio = 1.0594631f;
    private const float CoinArpeggioStepSeconds = 0.028f;
    private const float MaxCoinArpeggioDelaySeconds = 0.084f;
    private const int MaxRedirectChain = 8;

    private static int activeInstanceCount;
    public static bool HasActiveInstance => activeInstanceCount > 0;
    public static Func<float> UnscaledTimeProvider = () => Time.unscaledTime;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticState()
    {
        activeInstanceCount = 0;
        UnscaledTimeProvider = () => Time.unscaledTime;
    }

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

    private int redirectChain;
    private int coinStreak;
    private float lastCoinUnscaledTime;
    private bool hasCollectedCoin;
    private float nextCoinArpeggioTime;
    private bool hasScheduledArpeggio;

    private SoundConfig ActiveConfig =>
        soundConfig != null
            ? soundConfig
            : (SoundManager.Instance != null ? SoundManager.Instance.Config : null);

    private static float CurrentUnscaledTime => UnscaledTimeProvider != null ? UnscaledTimeProvider() : Time.unscaledTime;

    private void Awake()
    {
        if (playerMovement == null)
            playerMovement = PlayerMovement.Instance;
    }

    private void OnEnable()
    {
        activeInstanceCount++;
        if (playerMovement == null)
            playerMovement = PlayerMovement.Instance;

        SubscribeReferences();

        CrackedBlock.OnAnyBroken += HandleCrackedBlockBroken;
        LevelFlowController.OnAnyHazardContacted += HandleAnyHazardContacted;
        PowerUpController.OnAnyPowerUpUsed += HandleAnyPowerUpUsed;
        Collectible.OnAnyStarCollected += HandleStarCollected;
        Collectible.OnAnyCoinCollected += HandleCoinCollected;
        MazeTile.OnAnyPlayerInteracted += HandleTileInteracted;
    }

    private void OnDisable()
    {
        activeInstanceCount = Mathf.Max(0, activeInstanceCount - 1);
        UnsubscribeReferences();

        CrackedBlock.OnAnyBroken -= HandleCrackedBlockBroken;
        LevelFlowController.OnAnyHazardContacted -= HandleAnyHazardContacted;
        PowerUpController.OnAnyPowerUpUsed -= HandleAnyPowerUpUsed;
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        Collectible.OnAnyCoinCollected -= HandleCoinCollected;
        MazeTile.OnAnyPlayerInteracted -= HandleTileInteracted;
    }

    public void ResetStreakState()
    {
        redirectChain = 0;
        coinStreak = 0;
        lastCoinUnscaledTime = 0f;
        hasCollectedCoin = false;
        nextCoinArpeggioTime = 0f;
        hasScheduledArpeggio = false;
    }

    private void SubscribeReferences()
    {
        if (playerMovement != null && !playerSubscribed)
        {
            playerSubscribed = true;
            playerMovement.OnMoveStarted += HandlePlayerMoveStarted;
            playerMovement.OnMoveStopped += HandlePlayerMoveStopped;
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
            playerMovement.OnMoveStopped -= HandlePlayerMoveStopped;
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
        redirectChain = 0;
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.moveStart);
    }

    private void HandlePlayerMoveStopped()
    {
        redirectChain = 0;
    }

    private void HandlePlayerWallImpact(Vector2 impactNormal)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg == null)
            return;

        VelocityTier tier = (playerMovement != null && playerMovement.CurrentTier != VelocityTier.None)
            ? playerMovement.CurrentTier
            : VelocityTier.Drift;
        PlayEntry(cfg.GetWallImpactByTier(tier));
    }

    private void HandlePlayerRedirected(Vector2 newDirection)
    {
        if (playerMovement != null && playerMovement.IsPhasing)
            return;

        SoundConfig cfg = ActiveConfig;
        if (cfg == null)
            return;

        float pitch = 1f + 0.07f * Mathf.Min(redirectChain, MaxRedirectChain);
        redirectChain++;
        if (SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXWithPitch(cfg.wedgeRedirect, pitch);
    }

    private void HandleTileInteracted(MazeTile tile, InteractionContext context)
    {
        if ((tile is BrakeTile || tile is GravityBlock) && context.Kind == InteractionKind.Brake)
        {
            SoundConfig cfg = ActiveConfig;
            if (cfg != null)
                PlayEntry(cfg.brakeAnchor);
        }
    }

    private void HandleCrackedBlockBroken(CrackedBlock block)
    {
        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
            PlayEntry(cfg.crackedWallBreak);
    }

    private void HandleLevelStarted()
    {
        ResetStreakState();
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
        if (coin == null || SoundManager.Instance == null)
            return;

        float now = CurrentUnscaledTime;
        if (hasCollectedCoin && now - lastCoinUnscaledTime <= CoinStreakWindowSeconds)
            coinStreak = Mathf.Min(coinStreak + 1, MaxCoinStreakSemitones);
        else
            coinStreak = 0;

        hasCollectedCoin = true;
        lastCoinUnscaledTime = now;

        float pitch = Mathf.Pow(SemitoneRatio, coinStreak);
        float delay = hasScheduledArpeggio
            ? Mathf.Clamp(nextCoinArpeggioTime - now, 0f, MaxCoinArpeggioDelaySeconds)
            : 0f;
        nextCoinArpeggioTime = Mathf.Max(now, hasScheduledArpeggio ? nextCoinArpeggioTime : now) + CoinArpeggioStepSeconds;
        hasScheduledArpeggio = true;

        if (coin.HasCustomCollectSfx)
        {
            SoundManager.Instance.PlayClipWithPitch(
                coin.CollectSfx,
                pitch,
                coin.CollectSfxVolume,
                minInterval: 0.01f,
                bypassCooldown: true,
                scheduledDspDelay: delay);
            return;
        }

        SoundConfig cfg = ActiveConfig;
        if (cfg != null)
        {
            SoundManager.Instance.PlaySFXWithPitch(
                cfg.coinCollect,
                pitch,
                volumeScale: 1f,
                bypassCooldown: true,
                scheduledDspDelay: delay);
        }
    }

    private static void PlayEntry(SoundEntry entry)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(entry);
        }
    }
}
