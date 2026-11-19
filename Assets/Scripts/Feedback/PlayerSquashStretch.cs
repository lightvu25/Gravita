using DG.Tweening;
using UnityEngine;

/// <summary>
/// Squash &amp; stretch, corner-whip, magnetic settle, respawn pop, and Kinetic Runway
/// tier/phase visual feedback for Subject G-0.
/// Applied strictly to the visual child Transform (never the physics root).
/// </summary>
public class PlayerSquashStretch : MonoBehaviour
{
    [Header("Visual Target")]
    [Tooltip("The visual child that gets scaled — NOT the Player root, so physics stay unaffected.")]
    [SerializeField] private Transform visualTransform;

    [Header("GDD Forms (pixels @ 32 PPU)")]
    [SerializeField] private float movingWidthPx = 20f;
    [SerializeField] private float movingHeightPx = 28f;
    [SerializeField] private float impactWidthPx = 30f;
    [SerializeField] private float impactHeightPx = 14f;

    [Header("Amounts (fraction of base scale — keep small for pixel-art)")]
    [SerializeField, Range(0f, 0.3f)] private float stretchAmount = 0.12f;

    [Header("Timing")]
    [SerializeField, Min(0.05f)] private float squashDuration = 0.18f;
    [SerializeField, Min(0.005f)] private float squashInDuration = 0.025f;
    [SerializeField, Min(0.05f)] private float stretchDuration = 0.18f;
    [SerializeField] private Ease returnEase = Ease.OutQuad;
    [SerializeField, Range(0f, 8f)] private float wallPressPx = 4f;

    [Header("Corner-Whip & Respawn Pop")]
    [SerializeField] private float cornerWhipDegrees = 10f;
    [SerializeField, Min(0.01f)] private float cornerWhipDuration = 0.10f;
    [SerializeField] private LevelFlowController flowController;

    [Header("Phase Launch Visuals")]
    [SerializeField] private Color phaseSpriteTint = new Color(0.72f, 0.55f, 1f, 0.55f);

    [Header("Safety")]
    [SerializeField, Range(0f, 0.6f)] private float maxDeformation = 0.50f;

    private PlayerMovement movement;
    private PlayerVisual playerVisual;
    private PlayerCosmeticApplier cosmeticApplier;
    private SpriteRenderer spriteRenderer;
    private Color baseSpriteColor = Color.white;
    private bool hasBaseColor;
    private bool isPhasingVisual;
    private bool pendingBrakeSettle;
    private Vector3 baseScale;
    private Vector3 baseLocalPosition;
    private Vector2 lastDirection = Vector2.right;
    private Sequence activeSequence;
    private Tween whipTween;

    private Quaternion CurrentBaseRotation => playerVisual != null ? playerVisual.BaseRotation : Quaternion.identity;
    private Vector3 CurrentBaseLocalPosition => playerVisual != null ? playerVisual.CurrentBaseLocalPosition : baseLocalPosition;

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

        playerVisual = GetComponent<PlayerVisual>() ?? GetComponentInParent<PlayerVisual>();
        cosmeticApplier = GetComponent<PlayerCosmeticApplier>() ?? GetComponentInParent<PlayerCosmeticApplier>();
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
        if (movement != null)
        {
            movement.OnMoveStarted += HandleMoveStarted;
            movement.OnMoveStopped += HandleMoveStopped;
            movement.OnVelocityTierChanged += HandleTierChanged;
            movement.OnPhasingChanged += HandlePhasingChanged;
            movement.OnRedirected += HandleRedirected;
        }

        if (playerVisual == null)
            playerVisual = GetComponent<PlayerVisual>() ?? GetComponentInParent<PlayerVisual>();
        if (playerVisual != null)
            playerVisual.OnBasePoseChanged += HandleBasePoseChanged;

        if (flowController == null)
            flowController = FindFirstObjectByType<LevelFlowController>();

        if (flowController != null)
        {
            flowController.OnLevelRestarted += HandleRespawnPop;
            flowController.OnPlayerRespawned += HandleRespawnPop;
        }

        MazeTile.OnAnyPlayerInteracted += HandleTileInteracted;
    }

    private void OnDisable()
    {
        if (movement != null)
        {
            movement.OnMoveStarted -= HandleMoveStarted;
            movement.OnMoveStopped -= HandleMoveStopped;
            movement.OnVelocityTierChanged -= HandleTierChanged;
            movement.OnPhasingChanged -= HandlePhasingChanged;
            movement.OnRedirected -= HandleRedirected;
        }

        if (playerVisual != null)
            playerVisual.OnBasePoseChanged -= HandleBasePoseChanged;

        if (flowController != null)
        {
            flowController.OnLevelRestarted -= HandleRespawnPop;
            flowController.OnPlayerRespawned -= HandleRespawnPop;
        }

        MazeTile.OnAnyPlayerInteracted -= HandleTileInteracted;
        pendingBrakeSettle = false;
        KillActive();
        KillWhip();

        if (visualTransform != null)
        {
            visualTransform.localScale = baseScale;
            visualTransform.localPosition = CurrentBaseLocalPosition;
            visualTransform.localRotation = CurrentBaseRotation;
        }

        isPhasingVisual = false;
        RestoreSpriteColor();
    }

    private void HandleBasePoseChanged()
    {
        KillWhip();
        if (visualTransform != null)
            visualTransform.localRotation = CurrentBaseRotation;
    }

    private void HandleTileInteracted(MazeTile tile, InteractionContext context)
    {
        if ((tile is BrakeTile || tile is GravityBlock) && context.Kind == InteractionKind.Brake)
            pendingBrakeSettle = true;
    }

    private void HandleMoveStarted(Vector2 direction)
    {
        pendingBrakeSettle = false;
        lastDirection = direction;
        Stretch(direction);
    }

    private void HandleRedirected(Vector2 newDirection)
    {
        if ((movement != null && movement.IsPhasing) || newDirection == Vector2.zero)
            return;

        Vector2 previous = lastDirection != Vector2.zero ? lastDirection.normalized : Vector2.right;
        Vector2 next = newDirection.normalized;
        float cross = previous.x * next.y - previous.y * next.x;
        float whipAngle = (cross >= 0f ? 1f : -1f) * cornerWhipDegrees;

        lastDirection = next;

        if (visualTransform != null && Mathf.Abs(cornerWhipDegrees) > 0.01f)
        {
            KillWhip();
            Quaternion targetRot = CurrentBaseRotation;
            visualTransform.localRotation = targetRot * Quaternion.Euler(0f, 0f, whipAngle);
            whipTween = visualTransform.DOLocalRotateQuaternion(targetRot, cornerWhipDuration)
                .SetEase(Ease.OutExpo)
                .SetUpdate(true)
                .SetLink(gameObject);
        }

        VelocityTier activeTier = movement != null ? movement.CurrentTier : VelocityTier.Drift;
        if (activeTier == VelocityTier.Cruise || activeTier == VelocityTier.KineticRam)
        {
            ApplyTierStretch(activeTier);
        }
        else
        {
            Stretch(next);
        }
    }

    private void HandleMoveStopped()
    {
        bool useBrakeSettle = pendingBrakeSettle;
        pendingBrakeSettle = false;

        if (movement == null || movement.WasLastStopWallImpact)
        {
            Squash(lastDirection, true);
            return;
        }

        if (useBrakeSettle)
        {
            Play(Deform(Vector2.up, -0.10f, 0.08f), 0.12f, Vector2.zero, Ease.OutBack, isSquash: true);
            return;
        }

        ResetScale();
    }

    private void HandleTierChanged(VelocityTier tier)
    {
        if (tier == VelocityTier.Cruise || tier == VelocityTier.KineticRam)
            ApplyTierStretch(tier);
    }

    private void ApplyTierStretch(VelocityTier tier)
    {
        float boost = tier == VelocityTier.KineticRam ? stretchAmount * 1.45f : stretchAmount * 1.15f;
        Play(Deform(lastDirection, +boost, -boost * 0.45f), stretchDuration * 0.85f);
    }

    private void HandlePhasingChanged(bool phasing)
    {
        pendingBrakeSettle = false;
        if (spriteRenderer == null || !hasBaseColor) return;

        if (phasing)
        {
            if (!isPhasingVisual)
                baseSpriteColor = spriteRenderer.color;
            isPhasingVisual = true;
            spriteRenderer.color = phaseSpriteTint;
        }
        else
        {
            isPhasingVisual = false;
            RestoreSpriteColor();
        }
    }

    private void HandleRespawnPop()
    {
        if (visualTransform == null)
            return;

        KillActive();
        KillWhip();
        visualTransform.localPosition = CurrentBaseLocalPosition;
        visualTransform.localRotation = CurrentBaseRotation;
        visualTransform.localScale = baseScale * 1.15f;

        activeSequence = DOTween.Sequence()
            .Append(visualTransform.DOScale(baseScale, 0.18f).SetEase(Ease.OutBack))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void RestoreSpriteColor()
    {
        if (cosmeticApplier != null)
        {
            cosmeticApplier.RefreshEquippedSkin();
            if (spriteRenderer != null)
            {
                baseSpriteColor = spriteRenderer.color;
                hasBaseColor = true;
            }
            return;
        }

        if (spriteRenderer != null && hasBaseColor)
            spriteRenderer.color = baseSpriteColor;
    }

    public void Squash() => Squash(lastDirection, true);

    public void Squash(Vector2 direction, bool pressIntoWall = true)
    {
        float flat = Mathf.Clamp(impactHeightPx / Mathf.Max(1f, movingHeightPx) - 1f, -maxDeformation, maxDeformation);
        float wide = Mathf.Clamp(impactWidthPx / Mathf.Max(1f, movingWidthPx) - 1f, -maxDeformation, maxDeformation);
        Play(
            Deform(direction, flat, wide),
            squashDuration,
            pressIntoWall ? direction : Vector2.zero,
            Ease.OutBack,
            isSquash: true);
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
            .Join(visualTransform.DOLocalMove(CurrentBaseLocalPosition, squashDuration * 0.5f).SetEase(returnEase))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private Vector3 Deform(Vector2 direction, float along, float across)
    {
        float a = Mathf.Clamp(along, -maxDeformation, maxDeformation);
        float c = Mathf.Clamp(across, -maxDeformation, maxDeformation);
        Vector3 localDir = Quaternion.Inverse(CurrentBaseRotation) * new Vector3(direction.x, direction.y, 0f);
        bool horizontalLocal = Mathf.Abs(localDir.x) >= Mathf.Abs(localDir.y);

        float sx = horizontalLocal ? a : c;
        float sy = horizontalLocal ? c : a;
        return new Vector3(baseScale.x * (1f + sx), baseScale.y * (1f + sy), baseScale.z);
    }

    private void Play(
        Vector3 deformed,
        float duration,
        Vector2 pressDirection = default,
        Ease? recoveryEase = null,
        bool isSquash = false)
    {
        if (visualTransform == null) return;

        Vector3 restingPos = CurrentBaseLocalPosition;
        Quaternion baseRot = CurrentBaseRotation;
        Vector3 pressed = restingPos;
        if (spriteRenderer != null && spriteRenderer.sprite != null)
        {
            Bounds b = spriteRenderer.sprite.bounds;
            float centerDxLocal = b.center.x * (baseScale.x - deformed.x);
            float centerDyLocal = b.center.y * (baseScale.y - deformed.y);

            if (pressDirection != Vector2.zero)
            {
                Vector2 dir = pressDirection.normalized;
                Vector3 localDir = Quaternion.Inverse(baseRot) * new Vector3(dir.x, dir.y, 0f);
                float pressWorld = wallPressPx / 32f;
                float dxLocal = localDir.x > 0.5f ? b.max.x * (baseScale.x - deformed.x)
                              : localDir.x < -0.5f ? b.min.x * (baseScale.x - deformed.x) : centerDxLocal;
                float dyLocal = localDir.y > 0.5f ? b.max.y * (baseScale.y - deformed.y)
                              : localDir.y < -0.5f ? b.min.y * (baseScale.y - deformed.y) : centerDyLocal;
                Vector3 localAnchorDelta = new Vector3(dxLocal, dyLocal, 0f);
                pressed += (baseRot * localAnchorDelta) + new Vector3(dir.x * pressWorld, dir.y * pressWorld, 0f);
            }
            else
            {
                Vector3 localAnchorDelta = new Vector3(centerDxLocal, centerDyLocal, 0f);
                pressed += baseRot * localAnchorDelta;
            }
        }
        else if (pressDirection != Vector2.zero)
        {
            pressed += (Vector3)(pressDirection.normalized * (wallPressPx / 32f));
        }

        float inTime = isSquash ? Mathf.Min(squashInDuration, duration * 0.5f) : duration * 0.4f;
        float outTime = Mathf.Max(0.01f, duration - inTime);
        Ease outEase = recoveryEase ?? returnEase;

        KillActive();
        activeSequence = DOTween.Sequence()
            .Append(visualTransform.DOScale(deformed, inTime).SetEase(Ease.OutQuad))
            .Join(visualTransform.DOLocalMove(pressed, inTime).SetEase(Ease.OutQuad))
            .Append(visualTransform.DOScale(baseScale, outTime).SetEase(outEase))
            .Join(visualTransform.DOLocalMove(restingPos, outTime).SetEase(outEase))
            .SetUpdate(true)
            .SetLink(gameObject);
    }

    private void KillActive()
    {
        if (activeSequence != null && activeSequence.IsActive())
            activeSequence.Kill();
        activeSequence = null;
    }

    private void KillWhip()
    {
        if (whipTween != null && whipTween.IsActive())
            whipTween.Kill();
        whipTween = null;
    }
}
