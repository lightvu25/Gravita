using DG.Tweening;
using UnityEngine;

/// <summary>
/// Subtle squash &amp; stretch + Kinetic Runway tier/phase visual feedback for Subject G-0.
///
/// The deformation and phase tint are applied ONLY to the visual child Transform
/// (the SpriteRenderer object) — never to the Player root — so physics geometry stays untouched.
/// </summary>
public class PlayerSquashStretch : MonoBehaviour
{
    [Header("Visual Target")]
    [Tooltip("The visual child that gets scaled — NOT the Player root, so physics stay unaffected.")]
    [SerializeField] private Transform visualTransform;

    [Header("GDD Forms (pixels @ 32 PPU)")]
    [SerializeField] private float movingWidthPx = 20f;
    [SerializeField] private float movingHeightPx = 28f;
    [SerializeField] private float impactWidthPx = 28f;
    [SerializeField] private float impactHeightPx = 16f;

    [Header("Amounts (fraction of base scale — keep small for pixel-art)")]
    [SerializeField, Range(0f, 0.3f)] private float stretchAmount = 0.12f;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float squashDuration = 0.22f;
    [SerializeField, Min(0.05f)] private float stretchDuration = 0.18f;
    [SerializeField] private Ease returnEase = Ease.OutQuad;
    [SerializeField, Range(0f, 8f)] private float wallPressPx = 4f;

    [Header("Phase Launch Visuals")]
    [SerializeField] private Color phaseSpriteTint = new Color(0.72f, 0.55f, 1f, 0.55f);

    [Header("Safety")]
    [SerializeField, Range(0f, 0.6f)] private float maxDeformation = 0.45f;

    private PlayerMovement movement;
    private SpriteRenderer spriteRenderer;
    private Color baseSpriteColor = Color.white;
    private bool hasBaseColor;
    private Vector3 baseScale;
    private Vector3 baseLocalPosition;
    private Vector2 lastDirection = Vector2.right;
    private Sequence activeSequence;

    private void Awake()
    {
        if (visualTransform == null
            || visualTransform == transform
            || visualTransform.GetComponent<Collider2D>() != null)
        {
            var sr = GetComponentInChildren<SpriteRenderer>();
            visualTransform = sr != null && sr.transform != transform ? sr.transform : null;
        }

        if (visualTransform == null)
        {
            Debug.LogWarning("[PlayerSquashStretch] No safe visual child found (needs a SpriteRenderer that isn't the physics root) — component disabled.", this);
            enabled = false;
            return;
        }

        spriteRenderer = visualTransform.GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            baseSpriteColor = spriteRenderer.color;
            hasBaseColor = true;
        }

        baseScale = visualTransform.localScale;
        baseLocalPosition = visualTransform.localPosition;
    }

    private void OnEnable()
    {
        movement = GetComponentInParent<PlayerMovement>();
        if (movement == null) return;
        movement.OnMoveStarted += HandleMoveStarted;
        movement.OnMoveStopped += HandleMoveStopped;
        movement.OnVelocityTierChanged += HandleTierChanged;
        movement.OnPhasingChanged += HandlePhasingChanged;
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.OnMoveStarted -= HandleMoveStarted;
            movement.OnMoveStopped -= HandleMoveStopped;
            movement.OnVelocityTierChanged -= HandleTierChanged;
            movement.OnPhasingChanged -= HandlePhasingChanged;
        }
        KillActive();
        if (visualTransform != null)
        {
            visualTransform.localScale = baseScale;
            visualTransform.localPosition = baseLocalPosition;
        }
        if (spriteRenderer != null && hasBaseColor)
        {
            spriteRenderer.color = baseSpriteColor;
        }
    }

    private void HandleMoveStarted(Vector2 direction)
    {
        lastDirection = direction;
        Stretch(direction);
    }

    private void HandleMoveStopped()
    {
        bool pressWall = movement == null || movement.WasLastStopWallImpact;
        Squash(lastDirection, pressWall);
    }

    private void HandleTierChanged(VelocityTier tier)
    {
        if (tier == VelocityTier.Cruise || tier == VelocityTier.KineticRam)
        {
            float boost = tier == VelocityTier.KineticRam ? stretchAmount * 1.45f : stretchAmount * 1.15f;
            Play(Deform(lastDirection, +boost, -boost * 0.45f), stretchDuration * 0.85f);
        }
    }

    private void HandlePhasingChanged(bool phasing)
    {
        if (spriteRenderer == null || !hasBaseColor) return;
        spriteRenderer.color = phasing ? phaseSpriteTint : baseSpriteColor;
    }

    public void Squash() => Squash(lastDirection, true);

    public void Squash(Vector2 direction, bool pressIntoWall = true)
    {
        float flat = Mathf.Clamp(impactHeightPx / Mathf.Max(1f, movingHeightPx) - 1f, -maxDeformation, maxDeformation);
        float wide = Mathf.Clamp(impactWidthPx / Mathf.Max(1f, movingWidthPx) - 1f, -maxDeformation, maxDeformation);
        Play(Deform(direction, flat, wide), squashDuration, pressIntoWall ? direction : Vector2.zero);
    }

    public void Stretch() => Stretch(lastDirection);

    public void Stretch(Vector2 direction)
    {
        Play(Deform(direction, +stretchAmount, -stretchAmount * 0.5f), stretchDuration);
    }

    public void ResetScale()
    {
        if (visualTransform == null) return;
        KillActive();
        activeSequence = DOTween.Sequence()
            .Append(visualTransform.DOScale(baseScale, squashDuration * 0.5f).SetEase(returnEase))
            .Join(visualTransform.DOLocalMove(baseLocalPosition, squashDuration * 0.5f).SetEase(returnEase))
            .SetLink(gameObject);
    }

    private Vector3 Deform(Vector2 direction, float along, float across)
    {
        float a = Mathf.Clamp(along, -maxDeformation, maxDeformation);
        float c = Mathf.Clamp(across, -maxDeformation, maxDeformation);
        bool horizontal = Mathf.Abs(direction.x) >= Mathf.Abs(direction.y);

        float sx = horizontal ? a : c;
        float sy = horizontal ? c : a;
        return new Vector3(baseScale.x * (1f + sx), baseScale.y * (1f + sy), baseScale.z);
    }

    private void Play(Vector3 deformed, float duration, Vector2 pressDirection = default)
    {
        if (visualTransform == null) return;

        Vector3 pressed = pressDirection != Vector2.zero
            ? baseLocalPosition + (Vector3)(pressDirection.normalized * (wallPressPx / 32f))
            : baseLocalPosition;

        KillActive();
        activeSequence = DOTween.Sequence()
            .Append(visualTransform.DOScale(deformed, duration * 0.4f).SetEase(Ease.OutQuad))
            .Join(visualTransform.DOLocalMove(pressed, duration * 0.4f).SetEase(Ease.OutQuad))
            .Append(visualTransform.DOScale(baseScale, duration * 0.6f).SetEase(returnEase))
            .Join(visualTransform.DOLocalMove(baseLocalPosition, duration * 0.6f).SetEase(returnEase))
            .SetLink(gameObject);
    }

    private void KillActive()
    {
        if (activeSequence != null && activeSequence.IsActive())
            activeSequence.Kill();
        activeSequence = null;
    }
}
