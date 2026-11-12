using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Manages permanent cosmetic ownership and active selection (v1.0: Player Skins for Subject G-0).
/// Purely visual — never alters movement physics, colliders, or velocity readability (GDD §6).
/// </summary>
public class CosmeticService : MonoBehaviour
{
    public const string DefaultSkinId = "";

    public static CosmeticService Instance { get; private set; }

    [SerializeField] private CosmeticDatabase database;

    /// <summary>Fired whenever cosmetic ownership or equipped skin changes.</summary>
    public static event Action OnCosmeticsChanged;

    public CosmeticDatabase Database => database;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (database == null)
            database = Resources.Load<CosmeticDatabase>("CosmeticDatabase_Main");
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    public void SetDatabase(CosmeticDatabase customDatabase)
    {
        if (Instance == null)
            Instance = this;

        database = customDatabase;
        OnCosmeticsChanged?.Invoke();
    }

    public static CosmeticDefinition ResolveDefinition(string cosmeticId)
    {
        if (string.IsNullOrEmpty(cosmeticId))
            return null;

        CosmeticDatabase db = Instance != null
            ? Instance.database
            : Resources.Load<CosmeticDatabase>("CosmeticDatabase_Main");

        return db != null ? db.GetById(cosmeticId) : null;
    }

    public static bool IsOwned(string cosmeticId, ProfileData profileOverride = null)
    {
        if (string.IsNullOrEmpty(cosmeticId))
            return true; // Default appearance is always owned

        CosmeticDefinition def = ResolveDefinition(cosmeticId);
        if (def != null && def.IsDefaultUnlocked)
            return true;

        ProfileData profile = profileOverride ?? SaveManager.loadProfile();
        return profile != null && profile.ownedCosmeticIds != null && profile.ownedCosmeticIds.Contains(cosmeticId);
    }

    public static string GetEquippedPlayerSkinId(ProfileData profileOverride = null)
    {
        ProfileData profile = profileOverride ?? SaveManager.loadProfile();
        if (profile == null || string.IsNullOrEmpty(profile.equippedPlayerSkinId))
            return DefaultSkinId;

        if (!IsOwned(profile.equippedPlayerSkinId, profile))
            return DefaultSkinId;

        return profile.equippedPlayerSkinId;
    }

    public static CosmeticDefinition GetEquippedPlayerSkinDefinition()
    {
        string equippedId = GetEquippedPlayerSkinId();
        return ResolveDefinition(equippedId);
    }

    /// <summary>
    /// Equips an owned PlayerSkin (pass empty string to restore the default Subject G-0 appearance).
    /// </summary>
    public static bool TryEquipPlayerSkin(string cosmeticId)
    {
        ProfileData profile = SaveManager.loadProfile();
        if (profile == null)
            return false;

        string normalized = cosmeticId ?? string.Empty;
        if (!string.IsNullOrEmpty(normalized) && !IsOwned(normalized, profile))
        {
            Debug.LogWarning($"[CosmeticService] Cannot equip unowned skin '{normalized}'.");
            return false;
        }

        string previous = profile.equippedPlayerSkinId;
        profile.equippedPlayerSkinId = normalized;

        if (!SaveManager.TrySaveProfile(profile))
        {
            profile.equippedPlayerSkinId = previous;
            return false;
        }

        OnCosmeticsChanged?.Invoke();
        return true;
    }

    internal static void NotifyCosmeticsUpdated()
    {
        OnCosmeticsChanged?.Invoke();
    }

    public static IReadOnlyList<string> GetOwnedCosmeticIds()
    {
        ProfileData profile = SaveManager.loadProfile();
        return profile != null && profile.ownedCosmeticIds != null
            ? profile.ownedCosmeticIds
            : Array.Empty<string>();
    }
}
