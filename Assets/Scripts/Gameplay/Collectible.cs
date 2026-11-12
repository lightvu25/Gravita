using System;
using UnityEngine;

public enum CollectibleType
{
    Coin = 0,
    Star = 1
}

[RequireComponent(typeof(Collider2D))]
public class Collectible : MonoBehaviour, IPlayerInteractable
{
    public static event Action<Collectible> OnAnyCollectibleCollected;
    public static event Action<Collectible> OnAnyStarCollected;
    public static event Action<Collectible> OnAnyCoinCollected;
    public event Action<InteractionContext> OnPlayerInteracted;

    [Header("Collectible Settings")]
    [Tooltip("Determines whether this pickup grants Coins or counts as a level Star.")]
    [SerializeField] private CollectibleType collectibleType = CollectibleType.Coin;

    [Tooltip("Optional id used by objectives that target a specific pickup.")]
    [SerializeField] private string pickupId;

    [Tooltip("Coin amount or star count granted when collected.")]
    [SerializeField, Min(1)] private int value = 1;

    [Header("Visual Reference (Optional)")]
    [Tooltip("Optional SpriteRenderer reference for this collectible. Auto-resolved from this object or children if left empty.")]
    [SerializeField] private SpriteRenderer visualRenderer;

    [Header("Audio & Visual Effects (Optional)")]
    [Tooltip("Optional sound clip played via SoundManager when collected.")]
    [SerializeField] private AudioClip collectSfx;

    [Tooltip("Volume multiplier for the collection sound effect.")]
    [SerializeField, Range(0f, 1f)] private float collectSfxVolume = 1f;

    [Tooltip("Optional pooled particle prefab spawned at this pickup's position when collected.")]
    [SerializeField] private GameObject collectParticlePrefab;

    [Tooltip("Optional pooled animation prefab spawned at this pickup's position when collected.")]
    [SerializeField] private GameObject collectAnimationPrefab;

    [Tooltip("Optional Animator state name to play on the spawned animation prefab.")]
    [SerializeField] private string collectAnimationStateName;

    public CollectibleType Type => IsStar ? CollectibleType.Star : collectibleType;
    public bool IsStar => collectibleType == CollectibleType.Star;
    public bool IsCoin => !IsStar && collectibleType == CollectibleType.Coin;
    public string PickupId => pickupId;
    public int Value => Mathf.Max(1, value);
    public bool IsCollected { get; private set; }
    public Vector3 LastCollectWorldPosition { get; private set; }
    public SpriteRenderer VisualRenderer => visualRenderer;
    public bool HasCustomCollectSfx => collectSfx != null;
    public bool HasInteractionEffectPlayer => interactionEffectPlayer != null && interactionEffectPlayer.HasConfiguredEffects;
    public bool HasCustomParticleEffect => collectParticlePrefab != null || HasInteractionEffectPlayer;
    public bool HasCustomAnimationEffect => collectAnimationPrefab != null || HasInteractionEffectPlayer;

    private LevelRoot levelRoot;
    private Collider2D triggerCollider;
    private InteractionEffectPlayer interactionEffectPlayer;
    private Vector3 initialLocalPosition;
    private bool hasInitialPosition;

    protected void SetCollectibleType(CollectibleType type)
    {
        collectibleType = type;
    }

    protected virtual void Awake()
    {
        initialLocalPosition = transform.localPosition;
        hasInitialPosition = true;

        levelRoot = GetComponentInParent<LevelRoot>();

        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<SpriteRenderer>();

        interactionEffectPlayer = GetComponent<InteractionEffectPlayer>();
        triggerCollider = GetComponent<Collider2D>();
        if (triggerCollider != null && !triggerCollider.isTrigger)
        {
            Debug.LogWarning($"[Collectible] '{name}': Collider2D must be a trigger. Setting isTrigger = true.", this);
            triggerCollider.isTrigger = true;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (IsCollected) return;
        if (!IsPlayer(other)) return;

        Collect();
    }

    private static bool IsPlayer(Collider2D other)
    {
        if (other == null) return false;
        return other.GetComponentInParent<PlayerMovement>() != null || other.CompareTag("Player");
    }

    public Vector3 ResolveVisualWorldCenter()
    {
        if (visualRenderer == null)
            visualRenderer = GetComponentInChildren<SpriteRenderer>();

        if (visualRenderer != null && visualRenderer.enabled && visualRenderer.gameObject.activeInHierarchy)
            return visualRenderer.bounds.center;

        if (triggerCollider != null && triggerCollider.enabled && triggerCollider.gameObject.activeInHierarchy)
            return triggerCollider.bounds.center;

        if (visualRenderer != null)
            return visualRenderer.transform.position;

        return transform.position;
    }

    public void Collect()
    {
        if (IsCollected) return;
        IsCollected = true;

        Vector3 collectPosition = ResolveVisualWorldCenter();
        LastCollectWorldPosition = collectPosition;

        if (triggerCollider != null)
            triggerCollider.enabled = false;

        if (IsCoin && EconomyBalanceConfig.Active.GrantWalletCoinsFromInLevelPickups)
            GrantCoinReward(Value);

        // Notify per-prefab InteractionEffectPlayer before deactivating the GameObject
        OnPlayerInteracted?.Invoke(new InteractionContext(InteractionKind.Collect, collectPosition, Vector2.zero));
        PlayCollectFeedback(collectPosition);

        gameObject.SetActive(false);

        if (levelRoot == null)
            levelRoot = GetComponentInParent<LevelRoot>();

        levelRoot?.NotifyCollectibleCollected(this);
        OnAnyCollectibleCollected?.Invoke(this);

        if (IsStar)
            OnAnyStarCollected?.Invoke(this);
        else if (IsCoin)
            OnAnyCoinCollected?.Invoke(this);

        OnCollected();
    }

    private void GrantCoinReward(int coinAmount)
    {
        if (coinAmount <= 0) return;

        var rewardService = RewardService.Instance != null
            ? RewardService.Instance
            : FindAnyObjectByType<RewardService>();

        if (rewardService != null)
        {
            var bundle = new RewardBundle();
            bundle.Add(RewardType.Coins, coinAmount);
            rewardService.GrantRewardBundle(bundle, "CoinPickup");
            return;
        }

        var economy = EconomyManager.Instance != null
            ? EconomyManager.Instance
            : FindAnyObjectByType<EconomyManager>();

        if (economy != null)
        {
            economy.AddCoins(coinAmount, "CoinPickup");
        }
        else
        {
            Debug.LogWarning($"[Collectible] '{name}': Neither RewardService nor EconomyManager is available to grant {coinAmount} coin(s).", this);
        }
    }

    private void PlayCollectFeedback(Vector3 position)
    {
        if (collectSfx != null && SoundManager.Instance != null)
        {
            SoundManager.Instance.PlaySFX(collectSfx, collectSfxVolume);
        }

        // If an InteractionEffectPlayer on this prefab has configured effects, it already spawned them via OnPlayerInteracted.
        if (HasInteractionEffectPlayer)
            return;

        if (collectParticlePrefab != null)
        {
            ParticleSpawner.SpawnEffect(collectParticlePrefab, position, Quaternion.identity);
        }

        if (collectAnimationPrefab != null)
        {
            PrefabAnimationSpawner.SpawnEffect(
                collectAnimationPrefab,
                position,
                Quaternion.identity,
                null,
                collectAnimationStateName);
        }
    }

    /// <summary>Hook invoked after this pickup is collected — override for special pickups.</summary>
    protected virtual void OnCollected() { }

    /// <summary>Restores the pickup for a level reset.</summary>
    public void ResetCollectible()
    {
        IsCollected = false;
        if (hasInitialPosition)
            transform.localPosition = initialLocalPosition;
        if (triggerCollider != null)
            triggerCollider.enabled = true;
        gameObject.SetActive(true);
    }
}
