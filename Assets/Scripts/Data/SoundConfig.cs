using System;
using UnityEngine;

/// <summary>
/// Semantic UI sound categories configured in <see cref="SoundConfig"/> and triggered by <see cref="UIAudio"/>.
/// </summary>
public enum UISoundType
{
    None = 0,
    ButtonClick = 1,
    TabSwitch = 2,
    PanelOpen = 3,
    PanelClose = 4,
    RewardClaim = 5,
    PurchaseSuccess = 6,
    ErrorOrLocked = 7,
    BlindBoxOpen = 8,
    VictoryPopup = 9,
    DefeatPopup = 10,
    CurrencyCountTick = 11
}

/// <summary>
/// Configurable sound definition supporting multi-clip variation, volume scaling,
/// pitch randomization, and per-entry anti-spam cooldown.
/// </summary>
[Serializable]
public struct SoundEntry
{
    [Tooltip("One or more clips to choose from randomly when this sound is triggered.")]
    public AudioClip[] clips;

    [Range(0f, 1f)]
    [Tooltip("Playback volume multiplier (0..1).")]
    public float volume;

    [Tooltip("If true, applies random pitch variation around 1.0 using pitchVariance.")]
    public bool randomizePitch;

    [Range(0f, 0.5f)]
    [Tooltip("Pitch range offset around 1.0 (e.g., 0.08 -> [0.92, 1.08]).")]
    public float pitchVariance;

    [Min(0f)]
    [Tooltip("Minimum unscaled seconds between consecutive plays to prevent audio spam.")]
    public float minInterval;

    public bool HasClips
    {
        get
        {
            if (clips == null || clips.Length == 0)
                return false;

            for (int i = 0; i < clips.Length; i++)
            {
                if (clips[i] != null)
                    return true;
            }

            return false;
        }
    }

    public static SoundEntry CreateDefault(
        float defaultVolume = 0.85f,
        bool defaultRandomPitch = false,
        float defaultPitchVariance = 0.08f,
        float defaultMinInterval = 0.03f)
    {
        return new SoundEntry
        {
            clips = Array.Empty<AudioClip>(),
            volume = Mathf.Clamp01(defaultVolume),
            randomizePitch = defaultRandomPitch,
            pitchVariance = Mathf.Clamp(defaultPitchVariance, 0f, 0.5f),
            minInterval = Mathf.Max(0f, defaultMinInterval)
        };
    }

    public AudioClip GetRandomClip()
    {
        if (clips == null || clips.Length == 0)
            return null;

        if (clips.Length == 1)
            return clips[0];

        int startIndex = UnityEngine.Random.Range(0, clips.Length);
        for (int i = 0; i < clips.Length; i++)
        {
            AudioClip candidate = clips[(startIndex + i) % clips.Length];
            if (candidate != null)
                return candidate;
        }

        return null;
    }

    public float GetResolvedVolume()
    {
        return volume <= 0f ? 0f : Mathf.Clamp01(volume);
    }

    public float GetResolvedPitch()
    {
        if (!randomizePitch || pitchVariance <= 0.001f)
            return 1f;

        return AudioPitchUtility.GetRandomPitch(1f - pitchVariance, 1f + pitchVariance);
    }
}

/// <summary>
/// Data-driven catalog of Gameplay and UI sound entries.
/// Assign this asset to <see cref="SoundManager"/>, <see cref="EntityAudio"/>, and <see cref="UIAudio"/> in the Inspector.
/// </summary>
[CreateAssetMenu(fileName = "SoundConfig", menuName = "Game/Audio/Sound Config")]
public class SoundConfig : ScriptableObject
{
    [Header("Gameplay — Player Movement")]
    [Tooltip("Played when the player begins sliding/dashing.")]
    public SoundEntry moveStart = SoundEntry.CreateDefault(0.65f, true, 0.08f, 0.04f);

    [Tooltip("Played when the player stops against a wall at Drift (low) velocity.")]
    public SoundEntry wallImpactDrift = SoundEntry.CreateDefault(0.60f, true, 0.08f, 0.04f);

    [Tooltip("Played when the player stops against a wall at Cruise (medium) velocity.")]
    public SoundEntry wallImpactCruise = SoundEntry.CreateDefault(0.75f, true, 0.08f, 0.04f);

    [Tooltip("Played when the player slams into a wall at KineticRam (high) velocity.")]
    public SoundEntry wallImpactKinetic = SoundEntry.CreateDefault(0.90f, true, 0.06f, 0.04f);

    [Tooltip("Played when the player is deflected by an angled wedge.")]
    public SoundEntry wedgeRedirect = SoundEntry.CreateDefault(0.75f, true, 0.08f, 0.03f);

    [Header("Gameplay — World & Level Flow")]
    [Tooltip("Played when a CrackedBlock is smashed.")]
    public SoundEntry crackedWallBreak = SoundEntry.CreateDefault(0.90f, true, 0.08f, 0.03f);

    [Tooltip("Played when a level starts / player enters the level.")]
    public SoundEntry levelStart = SoundEntry.CreateDefault(0.75f, false, 0f, 0.10f);

    [Tooltip("Played when the player reaches the goal / completes the level.")]
    public SoundEntry goalReached = SoundEntry.CreateDefault(0.90f, false, 0f, 0.10f);

    [Tooltip("Played when the player contacts a hazard or trap.")]
    public SoundEntry hazardHit = SoundEntry.CreateDefault(0.85f, true, 0.06f, 0.05f);

    [Tooltip("Played when the level fails.")]
    public SoundEntry levelFailed = SoundEntry.CreateDefault(0.80f, false, 0f, 0.10f);

    [Header("Gameplay — Power-Ups & Pickups")]
    [Tooltip("Played when any power-up is activated.")]
    public SoundEntry powerUpActivated = SoundEntry.CreateDefault(0.85f, true, 0.06f, 0.04f);

    [Tooltip("Fallback sound when a Coin collectible has no custom collectSfx assigned.")]
    public SoundEntry coinCollect = SoundEntry.CreateDefault(0.85f, true, 0.08f, 0.02f);

    [Tooltip("Fallback sound when a Star collectible has no custom collectSfx assigned.")]
    public SoundEntry starCollect = SoundEntry.CreateDefault(0.85f, true, 0.08f, 0.02f);

    [Header("UI — Interactive & Feedback")]
    public SoundEntry buttonClick = SoundEntry.CreateDefault(0.75f, true, 0.05f, 0.03f);
    public SoundEntry tabSwitch = SoundEntry.CreateDefault(0.75f, true, 0.05f, 0.03f);
    public SoundEntry panelOpen = SoundEntry.CreateDefault(0.70f, false, 0f, 0.05f);
    public SoundEntry panelClose = SoundEntry.CreateDefault(0.65f, false, 0f, 0.05f);
    public SoundEntry rewardClaim = SoundEntry.CreateDefault(0.85f, false, 0f, 0.05f);
    public SoundEntry purchaseSuccess = SoundEntry.CreateDefault(0.85f, false, 0f, 0.05f);
    public SoundEntry errorOrLocked = SoundEntry.CreateDefault(0.80f, false, 0f, 0.05f);
    public SoundEntry blindBoxOpen = SoundEntry.CreateDefault(0.85f, false, 0f, 0.05f);
    public SoundEntry victoryPopup = SoundEntry.CreateDefault(0.85f, false, 0f, 0.10f);
    public SoundEntry defeatPopup = SoundEntry.CreateDefault(0.85f, false, 0f, 0.10f);
    [Tooltip("Tick sound played repeatedly while Coin/Gem counters animate from current value to the new total.")]
    public SoundEntry currencyCountTick = SoundEntry.CreateDefault(0.65f, true, 0.06f, 0.045f);

    /// <summary>
    /// Resolves the appropriate wall impact <see cref="SoundEntry"/> for the given <see cref="VelocityTier"/>,
    /// falling back to adjacent tiers if a tier has no clips assigned.
    /// </summary>
    public SoundEntry GetWallImpactByTier(VelocityTier tier)
    {
        switch (tier)
        {
            case VelocityTier.KineticRam:
                if (wallImpactKinetic.HasClips) return wallImpactKinetic;
                if (wallImpactCruise.HasClips) return wallImpactCruise;
                return wallImpactDrift;

            case VelocityTier.Cruise:
                if (wallImpactCruise.HasClips) return wallImpactCruise;
                if (wallImpactDrift.HasClips) return wallImpactDrift;
                return wallImpactKinetic;

            default:
                if (wallImpactDrift.HasClips) return wallImpactDrift;
                if (wallImpactCruise.HasClips) return wallImpactCruise;
                return wallImpactKinetic;
        }
    }

    /// <summary>
    /// Resolves a semantic <see cref="UISoundType"/> to its configured <see cref="SoundEntry"/>.
    /// </summary>
    public bool TryGetUISound(UISoundType soundType, out SoundEntry entry)
    {
        switch (soundType)
        {
            case UISoundType.ButtonClick:
                entry = buttonClick;
                return entry.HasClips;

            case UISoundType.TabSwitch:
                entry = tabSwitch.HasClips ? tabSwitch : buttonClick;
                return entry.HasClips;

            case UISoundType.PanelOpen:
                entry = panelOpen;
                return entry.HasClips;

            case UISoundType.PanelClose:
                entry = panelClose;
                return entry.HasClips;

            case UISoundType.RewardClaim:
                entry = rewardClaim;
                return entry.HasClips;

            case UISoundType.PurchaseSuccess:
                entry = purchaseSuccess.HasClips ? purchaseSuccess : rewardClaim;
                return entry.HasClips;

            case UISoundType.ErrorOrLocked:
                entry = errorOrLocked;
                return entry.HasClips;

            case UISoundType.BlindBoxOpen:
                entry = blindBoxOpen.HasClips ? blindBoxOpen : rewardClaim;
                return entry.HasClips;

            case UISoundType.VictoryPopup:
                entry = victoryPopup.HasClips ? victoryPopup : rewardClaim;
                return entry.HasClips;

            case UISoundType.DefeatPopup:
                entry = defeatPopup.HasClips ? defeatPopup : errorOrLocked;
                return entry.HasClips;

            case UISoundType.CurrencyCountTick:
                entry = currencyCountTick.HasClips ? currencyCountTick : coinCollect;
                return entry.HasClips;

            default:
                entry = default;
                return false;
        }
    }
}
