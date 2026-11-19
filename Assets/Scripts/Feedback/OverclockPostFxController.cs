using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Phase A URP Post-FX Overclock & Close-Call feedback controller.
/// Pulses runtime <see cref="Volume.profile"/> overrides (never <c>sharedProfile</c>)
/// alongside <see cref="CinemachineCameraZoom2D.PulseZoom"/> and <see cref="SoundConfig.closeCallChime"/>,
/// and restores baseline intensities cleanly in <see cref="OnDisable"/>.
/// </summary>
public class OverclockPostFxController : MonoBehaviour
{
    private const float CloseCallChainWindowSeconds = 2.0f;

    [Header("References")]
    [SerializeField] private Volume globalVolume;
    [SerializeField] private PlayerMovement playerMovement;
    [SerializeField] private SoundConfig soundConfig;

    [Header("Accessibility")]
    [SerializeField] private bool photosensitivitySafeMode = false;

    [Header("Close-Call Overclock Surge")]
    [SerializeField] private float closeCallBloomPeak = 12.2f;
    [SerializeField] private float safeModeBloomPeak = 9.8f;
    [SerializeField, Range(0f, 1f)] private float closeCallChromaticPeak = 0.62f;
    [SerializeField, Range(0f, 1f)] private float safeModeChromaticPeak = 0.26f;
    [SerializeField, Range(0f, 1f)] private float closeCallVignettePeak = 0.52f;
    [SerializeField, Range(0f, 1f)] private float safeModeVignettePeak = 0.42f;
    [SerializeField, Min(0.01f)] private float surgeInDuration = 0.045f;
    [SerializeField, Min(0.01f)] private float surgeOutDuration = 0.24f;

    [Header("Close-Call Audio & Camera")]
    [SerializeField] private float closeCallZoomDip = -0.28f;
    [SerializeField, Min(0.01f)] private float closeCallZoomInDuration = 0.05f;
    [SerializeField, Min(0.01f)] private float closeCallZoomOutDuration = 0.16f;

    private Bloom bloom;
    private ChromaticAberration chromaticAberration;
    private Vignette vignette;

    private float baseBloomIntensity = 9.0f;
    private float baseChromaticIntensity = 0.20f;
    private float baseVignetteIntensity = 0.40f;
    private bool baselinesCached;

    private float currentBloom;
    private float currentChromatic;
    private float currentVignette;

    private Sequence activeSequence;
    private int closeCallChain;
    private float lastCloseCallUnscaledTime;
    private bool hasCloseCall;

    private SoundConfig ActiveConfig =>
        soundConfig != null
            ? soundConfig
            : (SoundManager.Instance != null ? SoundManager.Instance.Config : null);

    private void Awake()
    {
        ResolveReferences();
        CacheVolumeBaselines();
    }

    private void OnEnable()
    {
        ResolveReferences();
        CacheVolumeBaselines();

        CloseCallDetector.OnCloseCallTriggered += HandleCloseCall;
        Collectible.OnAnyStarCollected += HandleStarCollected;
        if (playerMovement != null)
            playerMovement.OnWallImpact += HandleWallImpact;
    }

    private void OnDisable()
    {
        CloseCallDetector.OnCloseCallTriggered -= HandleCloseCall;
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        if (playerMovement != null)
            playerMovement.OnWallImpact -= HandleWallImpact;

        KillActiveSequence();
        RestoreBaselines();
    }

    private void ResolveReferences()
    {
        if (globalVolume == null)
            globalVolume = FindFirstObjectByType<Volume>();

        if (playerMovement == null)
            playerMovement = PlayerMovement.Instance != null
                ? PlayerMovement.Instance
                : FindFirstObjectByType<PlayerMovement>();
    }

    private void CacheVolumeBaselines()
    {
        if (baselinesCached || globalVolume == null)
            return;

        VolumeProfile profile = globalVolume.profile;
        if (profile == null)
            return;

        baselinesCached = true;

        if (profile.TryGet(out bloom) && bloom != null)
            baseBloomIntensity = bloom.intensity.value;
        if (profile.TryGet(out chromaticAberration) && chromaticAberration != null)
            baseChromaticIntensity = chromaticAberration.intensity.value;
        if (profile.TryGet(out vignette) && vignette != null)
            baseVignetteIntensity = vignette.intensity.value;

        currentBloom = baseBloomIntensity;
        currentChromatic = baseChromaticIntensity;
        currentVignette = baseVignetteIntensity;
    }

    private void RestoreBaselines()
    {
        if (!baselinesCached)
            return;

        currentBloom = baseBloomIntensity;
        currentChromatic = baseChromaticIntensity;
        currentVignette = baseVignetteIntensity;
        ApplyCurrentValues();
    }

    private void ApplyCurrentValues()
    {
        if (bloom != null)
        {
            bloom.intensity.overrideState = true;
            bloom.intensity.value = currentBloom;
        }
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.overrideState = true;
            chromaticAberration.intensity.value = currentChromatic;
        }
        if (vignette != null)
        {
            vignette.intensity.overrideState = true;
            vignette.intensity.value = currentVignette;
        }
    }

    private void HandleCloseCall(Vector3 _)
    {
        float now = Time.unscaledTime;
        if (hasCloseCall && now - lastCloseCallUnscaledTime <= CloseCallChainWindowSeconds)
            closeCallChain = Mathf.Min(closeCallChain + 1, 6);
        else
            closeCallChain = 0;

        hasCloseCall = true;
        lastCloseCallUnscaledTime = now;

        SoundConfig cfg = ActiveConfig;
        if (cfg != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFXWithPitch(cfg.closeCallChime, 1.04f + 0.05f * closeCallChain);

        if (CinemachineCameraZoom2D.Instance != null)
            CinemachineCameraZoom2D.Instance.PulseZoom(closeCallZoomDip, closeCallZoomInDuration, closeCallZoomOutDuration);

        float targetBloom = photosensitivitySafeMode ? safeModeBloomPeak : closeCallBloomPeak;
        float targetCa = photosensitivitySafeMode ? safeModeChromaticPeak : closeCallChromaticPeak;
        float targetVig = photosensitivitySafeMode ? safeModeVignettePeak : closeCallVignettePeak;

        PulseOverrides(targetBloom, targetCa, targetVig, surgeInDuration, surgeOutDuration);
    }

    private void HandleStarCollected(Collectible _)
    {
        if (CinemachineCameraZoom2D.Instance != null)
            CinemachineCameraZoom2D.Instance.PulseZoom(-0.14f, 0.04f, 0.12f);

        float bloomPeak = baseBloomIntensity + (photosensitivitySafeMode ? 0.4f : 1.2f);
        PulseOverrides(bloomPeak, currentChromatic, currentVignette, 0.04f, 0.14f);
    }

    private void HandleWallImpact(Vector2 normal)
    {
        if (normal == Vector2.zero || playerMovement == null || playerMovement.CurrentTier != VelocityTier.KineticRam)
            return;

        float vigPeak = photosensitivitySafeMode ? Mathf.Max(baseVignetteIntensity, baseVignetteIntensity + 0.02f) : 0.50f;
        PulseOverrides(currentBloom, currentChromatic, vigPeak, 0.035f, 0.15f);
    }

    private void PulseOverrides(
        float bloomPeak,
        float caPeak,
        float vignettePeak,
        float inDuration,
        float outDuration)
    {
        CacheVolumeBaselines();
        if (!baselinesCached)
            return;

        KillActiveSequence();

        activeSequence = DOTween.Sequence()
            .Append(DOTween.To(() => currentBloom, x => { currentBloom = x; ApplyCurrentValues(); }, bloomPeak, inDuration).SetEase(Ease.OutQuad))
            .Join(DOTween.To(() => currentChromatic, x => { currentChromatic = x; ApplyCurrentValues(); }, caPeak, inDuration).SetEase(Ease.OutQuad))
            .Join(DOTween.To(() => currentVignette, x => { currentVignette = x; ApplyCurrentValues(); }, vignettePeak, inDuration).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => currentBloom, x => { currentBloom = x; ApplyCurrentValues(); }, baseBloomIntensity, outDuration).SetEase(Ease.InOutSine))
            .Join(DOTween.To(() => currentChromatic, x => { currentChromatic = x; ApplyCurrentValues(); }, baseChromaticIntensity, outDuration).SetEase(Ease.InOutSine))
            .Join(DOTween.To(() => currentVignette, x => { currentVignette = x; ApplyCurrentValues(); }, baseVignetteIntensity, outDuration).SetEase(Ease.InOutSine))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void KillActiveSequence()
    {
        if (activeSequence != null && activeSequence.IsActive())
            activeSequence.Kill();
        activeSequence = null;
    }
}
