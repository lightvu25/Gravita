using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// S6 Close Call Detector: rewards skilled near-misses when Subject G-0 crosses a
/// timed hazard within <see cref="closeCallWindowSeconds"/> of it becoming lethal,
/// or brushes directly past an active patrolling enemy without dying.
///
/// Purely visual/economy feedback — never modifies <c>Time.timeScale</c>.
/// </summary>
[RequireComponent(typeof(PlayerMovement))]
public class CloseCallDetector : MonoBehaviour
{
    public static event Action<Vector3> OnCloseCallTriggered;
    public static event Action<Vector3, Vector2> OnCloseCallTriggeredWithDirection;

    [Header("Near-Miss Thresholds")]
    [Tooltip("Crossing a warning laser/floor within this many seconds before ignition counts as a Close Call.")]
    [SerializeField, Min(0.02f)] private float closeCallWindowSeconds = 0.15f;

    [Tooltip("Passing within this distance of an active enemy hazard while sliding counts as a Close Call.")]
    [SerializeField, Min(0.2f)] private float enemyNearMissRadius = 1.08f;

    [Tooltip("Minimum real-time seconds before the same hazard can award another Close Call.")]
    [SerializeField, Min(0.2f)] private float perHazardCooldown = 1.5f;

    [Header("Reward & Feedback")]
    [SerializeField, Min(0)] private int bonusCoins = 5;
    [SerializeField, Min(0f)] private float cameraPulseForce = 0.12f;
    [SerializeField] private GameObject closeCallVfxPrefab;

    private readonly Collider2D[] scanBuffer = new Collider2D[16];
    private readonly Dictionary<EntityId, float> hazardCooldownUntil = new Dictionary<EntityId, float>();
    private PlayerMovement movement;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
    }

    private void OnDisable()
    {
        hazardCooldownUntil.Clear();
    }

    public static bool IsWithinCloseCallWindow(float timeUntilLethal, float windowSeconds = 0.15f)
    {
        return timeUntilLethal > 0f && timeUntilLethal <= Mathf.Max(0.001f, windowSeconds);
    }

    private void FixedUpdate()
    {
        if (movement == null || !movement.IsMoving || movement.IsPhasing || movement.InputLocked)
            return;

        Vector2 pos = transform.position;
        float now = Time.unscaledTime;

        int count = Physics2D.OverlapCircle(pos, enemyNearMissRadius, ContactFilter2D.noFilter, scanBuffer);
        for (int i = 0; i < count; i++)
        {
            Collider2D col = scanBuffer[i];
            if (col == null || col == movement.BodyCollider) continue;

            Hazard hazard = col.GetComponentInParent<Hazard>();
            if (hazard == null || hazard.IsBroken || hazard.IsFrozen) continue;

            EntityId id = hazard.GetEntityId();
            if (hazardCooldownUntil.TryGetValue(id, out float readyAt) && now < readyAt)
                continue;

            // Case 1: Timed hazard about to ignite right as the player crosses its cell.
            HazardActivation activation = hazard.GetComponent<HazardActivation>();
            if (activation != null
                && activation.CurrentPhase == HazardCyclePhase.Warning
                && IsWithinCloseCallWindow(activation.TimeUntilActive, closeCallWindowSeconds)
                && ((Vector2)hazard.transform.position - pos).sqrMagnitude <= (movement.CellSize * 0.55f) * (movement.CellSize * 0.55f))
            {
                Vector2 dir = movement.CurrentDirection != Vector2.zero
                    ? movement.CurrentDirection
                    : movement.LastMoveDirection;
                TriggerCloseCall(id, now, pos, dir);
                continue;
            }

            // Case 2: Brushing past an active enemy (Sentinel Drone) in an adjacent cell without colliding.
            if (hazard.IsEnemy && hazard.IsActiveHazard)
            {
                float dist = Vector2.Distance(pos, hazard.transform.position);
                if (dist > movement.CellSize * 0.72f && dist <= enemyNearMissRadius)
                {
                    Vector2 away = (pos - (Vector2)hazard.transform.position).normalized;
                    TriggerCloseCall(id, now, pos, away);
                }
            }
        }
    }

    private void TriggerCloseCall(EntityId hazardId, float now, Vector3 worldPos, Vector2 impulseDir)
    {
        hazardCooldownUntil[hazardId] = now + perHazardCooldown;

        if (bonusCoins > 0 && EconomyBalanceConfig.Active.EnableLegacyExtraRewardSystems && EconomyManager.Instance != null)
            EconomyManager.Instance.AddCoins(bonusCoins, "CloseCall");

        Vector2 resolvedDir = impulseDir.sqrMagnitude > 0.0001f ? impulseDir.normalized : Vector2.down;
        if (cameraPulseForce > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(resolvedDir, cameraPulseForce);

        if (closeCallVfxPrefab != null)
            VfxSpawner.Spawn(closeCallVfxPrefab, worldPos, Quaternion.identity);

        OnCloseCallTriggered?.Invoke(worldPos);
        OnCloseCallTriggeredWithDirection?.Invoke(worldPos, resolvedDir);
    }
}

