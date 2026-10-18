using System;
using UnityEngine;

/// <summary>
/// Central dispatcher responsible for applying granted RewardBundles to the
/// player economy (Coins, Gems via EconomyManager).
/// </summary>
public class RewardService : MonoBehaviour
{
    public static RewardService Instance { get; private set; }

    /// <summary>Fired whenever a reward bundle is successfully applied.</summary>
    public event Action<RewardBundle> OnRewardGranted;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>
    /// Applies all reward items within the specified bundle to player systems.
    /// </summary>
    /// <param name="bundle">The reward bundle to grant.</param>
    /// <param name="reason">Context tag for analytics/audit logging.</param>
    public bool GrantRewardBundle(RewardBundle bundle, string reason = "Unspecified")
    {
        if (bundle == null || bundle.IsEmpty)
        {
            Debug.LogWarning($"[RewardService] Attempted to grant null or empty bundle. Reason: {reason}");
            return false;
        }

        var economy = EconomyManager.Instance ?? FindAnyObjectByType<EconomyManager>();

        foreach (var item in bundle.Items)
        {
            if (item.Amount <= 0) continue;

            switch (item.Type)
            {
                case RewardType.Coins:
                    if (economy != null)
                        economy.AddCoins(item.Amount, reason);
                    else
                        Debug.LogWarning($"[RewardService] EconomyManager unavailable to grant {item.Amount} Coins.");
                    break;

                case RewardType.Gems:
                    if (economy != null)
                        economy.AddGems(item.Amount, reason);
                    else
                        Debug.LogWarning($"[RewardService] EconomyManager unavailable to grant {item.Amount} Gems.");
                    break;

                case RewardType.PowerUp:
                    if (ProgressionManager.Instance != null)
                        ProgressionManager.Instance.AddPowerUp(item.ItemId, item.Amount);
                    else
                        Debug.LogWarning($"[RewardService] ProgressionManager unavailable to grant power-up '{item.ItemId}'.");
                    break;

                default:
                    Debug.LogWarning($"[RewardService] Reward type '{item.Type}' has no granting system.");
                    break;
            }
        }

        OnRewardGranted?.Invoke(bundle);
        Debug.Log($"[RewardService] Successfully granted reward bundle ({bundle.Items.Count} items). Reason: '{reason}'.");
        return true;
    }
}

