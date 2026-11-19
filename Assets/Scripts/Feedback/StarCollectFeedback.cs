using UnityEngine;

/// <summary>
/// Feedback when the player picks up a StarCollectible or general Collectible.
///
/// Listens to static pickup events so it needs no direct reference to the
/// currently loaded LevelRoot — attach to the Player (or any always-alive
/// object) once and every pickup triggers its configured feedback.
/// </summary>
public class StarCollectFeedback : MonoBehaviour
{
    [Header("Camera Shake")]
    [Tooltip("Impulse force for a star pickup — smaller than a wall hit.")]
    [SerializeField, Min(0f)] private float starShake = 0.06f;

    [Header("Star Pickup Effects (Optional)")]
    [Tooltip("Optional pooled particle prefab spawned when a StarCollectible is collected.")]
    [SerializeField] private GameObject starParticlePrefab;

    [Tooltip("Optional pooled animation prefab spawned when a StarCollectible is collected.")]
    [SerializeField] private GameObject starAnimationPrefab;

    [Header("General Collectible Effects (Optional)")]
    [Tooltip("Optional pooled particle prefab spawned when a regular Collectible (coin/dot) is collected.")]
    [SerializeField] private GameObject collectibleParticlePrefab;

    [Tooltip("Optional pooled animation prefab spawned when a regular Collectible (coin/dot) is collected.")]
    [SerializeField] private GameObject collectibleAnimationPrefab;

    private void Start()
    {
        if (!Application.isPlaying) return;

        VfxSpawner.Prewarm(starParticlePrefab, 2);
        VfxSpawner.Prewarm(starAnimationPrefab, 2);
        VfxSpawner.Prewarm(collectibleParticlePrefab, 2);
        VfxSpawner.Prewarm(collectibleAnimationPrefab, 2);
    }

    private void OnEnable()
    {
        Collectible.OnAnyStarCollected += HandleStarCollected;
        Collectible.OnAnyCollectibleCollected += HandleCollectibleCollected;
    }

    private void OnDisable()
    {
        Collectible.OnAnyStarCollected -= HandleStarCollected;
        Collectible.OnAnyCollectibleCollected -= HandleCollectibleCollected;
    }

    private void HandleStarCollected(Collectible star)
    {
        if (star == null) return;

        bool hasPerPrefabShake = star.TryGetComponent<InteractionEffectPlayer>(out var iep) && iep.HasConfiguredShake;
        if (!hasPerPrefabShake && starShake > 0f && CinemachineCameraShake2D.Instance != null)
        {
            Vector2 travelDir = PlayerMovement.Instance != null && PlayerMovement.Instance.LastMoveDirection != Vector2.zero
                ? PlayerMovement.Instance.LastMoveDirection
                : Vector2.down;
            CinemachineCameraShake2D.Instance.ShakeCamera(travelDir, starShake);
        }

        Vector3 pos = star.LastCollectWorldPosition;

        if (!star.HasCustomParticleEffect && starParticlePrefab != null)
            ParticleSpawner.SpawnEffect(starParticlePrefab, pos, Quaternion.identity);

        if (!star.HasCustomAnimationEffect && starAnimationPrefab != null)
            PrefabAnimationSpawner.SpawnEffect(starAnimationPrefab, pos, Quaternion.identity);
    }

    private void HandleCollectibleCollected(Collectible collectible)
    {
        if (collectible == null || collectible.IsStar) return;
        Vector3 pos = collectible.LastCollectWorldPosition;

        if (!collectible.HasCustomParticleEffect && collectibleParticlePrefab != null)
            ParticleSpawner.SpawnEffect(collectibleParticlePrefab, pos, Quaternion.identity);

        if (!collectible.HasCustomAnimationEffect && collectibleAnimationPrefab != null)
            PrefabAnimationSpawner.SpawnEffect(collectibleAnimationPrefab, pos, Quaternion.identity);
    }
}
