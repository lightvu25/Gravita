using UnityEngine;

/// <summary>
/// Data definition for a permanent, purely visual cosmetic item (v1.0: Player Skin for Subject G-0).
/// Strictly respects GDD §6.3 Gameplay Clarity Rules: cosmetics never alter collider hitbox,
/// slide speed, Velocity Tier colors, or Phase Launch readability.
/// </summary>
[CreateAssetMenu(fileName = "NewCosmeticDefinition", menuName = "Gravita/Economy/Cosmetic Definition")]
public class CosmeticDefinition : ScriptableObject
{
    [Header("Identity")]
    [Tooltip("Stable unique identifier persisted in ProfileData (e.g. 'skin_default', 'skin_cobalt').")]
    [SerializeField] private string cosmeticId;

    [SerializeField] private string displayName;

    [TextArea(2, 4)]
    [SerializeField] private string description;

    [SerializeField] private CosmeticCategory category = CosmeticCategory.PlayerSkin;

    [Tooltip("If true, every player owns this cosmetic automatically without purchasing.")]
    [SerializeField] private bool isDefaultUnlocked;

    [Header("UI Presentation")]
    [Tooltip("Preview icon shown on Shop and Skin Selection cards.")]
    [SerializeField] private Sprite icon;

    [Header("Player Skin Visuals (CosmeticCategory.PlayerSkin)")]
    [Tooltip("Optional sprite applied to Subject G-0's SpriteRenderer. Leave null to keep the prefab's default sprite.")]
    [SerializeField] private Sprite playerSprite;

    [Tooltip("Optional tint applied to Subject G-0's SpriteRenderer. Defaults to pure white (no tint).")]
    [SerializeField] private Color playerTint = Color.white;

    [Tooltip("Optional Animator Controller override for animated skins. Never modifies Rigidbody2D or Collider2D.")]
    [SerializeField] private RuntimeAnimatorController animatorOverride;

    public string CosmeticId => !string.IsNullOrWhiteSpace(cosmeticId) ? cosmeticId : name;
    public string DisplayName => !string.IsNullOrWhiteSpace(displayName) ? displayName : name;
    public string Description => description ?? string.Empty;
    public CosmeticCategory Category => category;
    public bool IsDefaultUnlocked => isDefaultUnlocked;
    public Sprite Icon => icon != null ? icon : playerSprite;
    public Sprite PlayerSprite => playerSprite;
    public Color PlayerTint => playerTint;
    public RuntimeAnimatorController AnimatorOverride => animatorOverride;

    public void Configure(
        string id,
        string nameLabel,
        string desc,
        CosmeticCategory cosmeticCategory = CosmeticCategory.PlayerSkin,
        bool defaultUnlocked = false,
        Sprite previewIcon = null,
        Sprite skinSprite = null,
        Color? tint = null,
        RuntimeAnimatorController animOverride = null)
    {
        cosmeticId = id;
        displayName = nameLabel;
        description = desc;
        category = cosmeticCategory;
        isDefaultUnlocked = defaultUnlocked;
        icon = previewIcon;
        playerSprite = skinSprite;
        playerTint = tint ?? Color.white;
        animatorOverride = animOverride;
    }
}

