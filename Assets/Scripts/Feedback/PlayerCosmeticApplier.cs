using UnityEngine;

/// <summary>
/// Runtime visual-only applier for Subject G-0 Player Skins (GDD §6 & Approved Decision 5).
/// Strictly modifies visual presentation (<see cref="SpriteRenderer"/> / <see cref="Animator"/>)
/// and NEVER touches <see cref="Collider2D"/>, <see cref="Rigidbody2D"/>, movement speed,
/// or velocity-tier gameplay indicators.
/// </summary>
[DisallowMultipleComponent]
public class PlayerCosmeticApplier : MonoBehaviour
{
    [SerializeField] private SpriteRenderer targetRenderer;
    [SerializeField] private Animator targetAnimator;

    private Sprite defaultSprite;
    private Color defaultColor = Color.white;
    private RuntimeAnimatorController defaultAnimatorController;
    private bool capturedDefaults;
    private bool isSubscribed;

    public static PlayerCosmeticApplier EnsureAttached(PlayerMovement player)
    {
        if (player == null)
            return null;

        PlayerCosmeticApplier existing = player.GetComponent<PlayerCosmeticApplier>();
        if (existing != null)
        {
            existing.SubscribeIfNeeded();
            existing.RefreshEquippedSkin();
            return existing;
        }

        PlayerCosmeticApplier added = player.gameObject.AddComponent<PlayerCosmeticApplier>();
        added.SubscribeIfNeeded();
        added.RefreshEquippedSkin();
        return added;
    }

    private void Awake()
    {
        ResolveTargetsAndCaptureDefaults();
    }

    private void OnEnable()
    {
        SubscribeIfNeeded();
        RefreshEquippedSkin();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void SubscribeIfNeeded()
    {
        if (isSubscribed)
            return;

        CosmeticService.OnCosmeticsChanged -= HandleCosmeticsChanged;
        CosmeticService.OnCosmeticsChanged += HandleCosmeticsChanged;
        isSubscribed = true;
    }

    private void Unsubscribe()
    {
        if (!isSubscribed)
            return;

        CosmeticService.OnCosmeticsChanged -= HandleCosmeticsChanged;
        isSubscribed = false;
    }

    private void HandleCosmeticsChanged()
    {
        RefreshEquippedSkin();
    }

    public void RefreshEquippedSkin()
    {
        ResolveTargetsAndCaptureDefaults();
        if (targetRenderer == null)
            return;

        CosmeticDefinition skin = CosmeticService.GetEquippedPlayerSkinDefinition();
        if (skin == null || skin.Category != CosmeticCategory.PlayerSkin)
        {
            // Restore original Subject G-0 visuals when default skin is selected
            if (capturedDefaults)
            {
                targetRenderer.sprite = defaultSprite;
                targetRenderer.color = defaultColor;
                if (targetAnimator != null)
                    targetAnimator.runtimeAnimatorController = defaultAnimatorController;
            }
            return;
        }

        if (skin.PlayerSprite != null)
            targetRenderer.sprite = skin.PlayerSprite;
        else if (capturedDefaults)
            targetRenderer.sprite = defaultSprite;

        targetRenderer.color = skin.PlayerTint;

        if (targetAnimator != null)
        {
            targetAnimator.runtimeAnimatorController = skin.AnimatorOverride != null
                ? skin.AnimatorOverride
                : defaultAnimatorController;
        }
    }

    private void ResolveTargetsAndCaptureDefaults()
    {
        if (targetRenderer == null)
        {
            targetRenderer = GetComponent<SpriteRenderer>();
            if (targetRenderer == null)
                targetRenderer = GetComponentInChildren<SpriteRenderer>();
        }

        if (targetAnimator == null)
        {
            targetAnimator = GetComponent<Animator>();
            if (targetAnimator == null)
                targetAnimator = GetComponentInChildren<Animator>();
        }

        if (!capturedDefaults && targetRenderer != null)
        {
            defaultSprite = targetRenderer.sprite;
            defaultColor = targetRenderer.color;
            if (targetAnimator != null)
                defaultAnimatorController = targetAnimator.runtimeAnimatorController;
            capturedDefaults = true;
        }
    }
}
