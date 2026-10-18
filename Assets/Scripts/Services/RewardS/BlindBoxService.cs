using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Post-completion mystery box: rolls one weighted reward from a loot table
/// and grants it through the standard RewardService pipeline.
///
/// One box per level completion — the UI calls ResetBox() when a new
/// completion begins. DoubleWithAd() re-grants the rolled bundle after a
/// rewarded ad (the actual ad watch is delegated to AdRewardService).
/// </summary>
public class BlindBoxService : MonoBehaviour
{
    public static BlindBoxService Instance { get; private set; }

    [Header("Loot Table")]
    [SerializeField] private List<BlindBoxRewardEntry> rewardTable = new List<BlindBoxRewardEntry>
    {
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 15, maxAmount = 30, weight = 40, displayName = "Coins" },
        new BlindBoxRewardEntry { type = RewardType.Coins,   minAmount = 50, maxAmount = 80, weight = 15, displayName = "Coins" },
        new BlindBoxRewardEntry { type = RewardType.Gems,    minAmount = 1,  maxAmount = 2,  weight = 20, displayName = "Gems" },
        new BlindBoxRewardEntry { type = RewardType.Gems,    minAmount = 3,  maxAmount = 5,  weight = 5,  displayName = "Gems" },
        new BlindBoxRewardEntry { type = RewardType.PowerUp, itemId = PowerUpIds.KineticShield, minAmount = 1, maxAmount = 1, weight = 20, displayName = "Kinetic Shield" },
    };

    /// <summary>True once the current box has been opened.</summary>
    public bool IsOpened { get; private set; }

    /// <summary>True once the rolled reward has already been doubled via ad.</summary>
    public bool IsDoubled { get; private set; }

    /// <summary>Human-readable label of the last rolled reward.</summary>
    public string LastRewardLabel { get; private set; } = "";

    private RewardBundle lastBundle;

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

    /// <summary>Resets the box so the next completion can open a fresh one.</summary>
    public void ResetBox()
    {
        IsOpened = false;
        IsDoubled = false;
        lastBundle = null;
        LastRewardLabel = "";
    }

    /// <summary>
    /// Rolls the loot table and grants the reward. Returns the granted bundle,
    /// or null if the box was already opened or the table is empty.
    /// </summary>
    public RewardBundle Open()
    {
        if (IsOpened) return null;

        BlindBoxRewardEntry entry = RollEntry();
        if (entry == null)
        {
            Debug.LogWarning("[BlindBoxService] Loot table is empty or all weights are zero.");
            return null;
        }

        int amount = UnityEngine.Random.Range(entry.minAmount, entry.maxAmount + 1);

        var bundle = new RewardBundle();
        bundle.Add(entry.type, amount, entry.itemId);

        GrantBundle(bundle, "BlindBox");

        lastBundle = bundle;
        IsOpened = true;
        LastRewardLabel = amount > 1 ? $"{amount} {entry.displayName}" : entry.displayName;

        Debug.Log($"[BlindBoxService] Box opened — rolled '{LastRewardLabel}'.");
        return bundle;
    }

    /// <summary>
    /// Watches a rewarded ad; on completion grants the same bundle again.
    /// </summary>
    public void DoubleWithAd(Action<bool> onComplete = null)
    {
        if (!IsOpened || IsDoubled || lastBundle == null)
        {
            onComplete?.Invoke(false);
            return;
        }

        var adService = AdRewardService.Instance != null ? AdRewardService.Instance : FindAnyObjectByType<AdRewardService>();
        if (adService == null || !adService.IsAdAvailable(RewardedAdPlacement.BlindBoxDoubler))
        {
            Debug.LogWarning("[BlindBoxService] Cannot double — ad service unavailable.");
            onComplete?.Invoke(false);
            return;
        }

        adService.WatchAdForReward(RewardedAdPlacement.BlindBoxDoubler, 0, success =>
        {
            if (success)
            {
                IsDoubled = true;
                GrantBundle(lastBundle, "BlindBox_x2");
            }
            onComplete?.Invoke(success);
        });
    }

    private BlindBoxRewardEntry RollEntry()
    {
        int totalWeight = 0;
        for (int i = 0; i < rewardTable.Count; i++)
            totalWeight += Mathf.Max(0, rewardTable[i].weight);
        if (totalWeight <= 0) return null;

        int roll = UnityEngine.Random.Range(0, totalWeight);
        for (int i = 0; i < rewardTable.Count; i++)
        {
            roll -= Mathf.Max(0, rewardTable[i].weight);
            if (roll < 0)
                return rewardTable[i];
        }
        return rewardTable[rewardTable.Count - 1];
    }

    private void GrantBundle(RewardBundle bundle, string reason)
    {
        var rewardService = RewardService.Instance != null ? RewardService.Instance : FindAnyObjectByType<RewardService>();
        if (rewardService != null)
            rewardService.GrantRewardBundle(bundle, reason);
        else
            Debug.LogWarning("[BlindBoxService] RewardService unavailable — reward not granted.");
    }
}
