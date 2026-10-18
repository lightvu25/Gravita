using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Observes existing game/service events and forwards them to the analytics
/// service. Contains no gameplay logic and owns no gameplay state — it only
/// translates reliable runtime events into TrackEvent calls.
///
/// Subscribes in Awake (not Start) so events fired by other components'
/// Start methods (e.g. auto-started levels) are not missed. Services are
/// resolved via FindAnyObjectByType rather than .Instance so subscription
/// does not depend on Awake ordering.
/// </summary>
public class AnalyticsEventObserver : MonoBehaviour
{
    private LevelFlowController flowController;
    private ProgressionManager progression;
    private ShopManager shopManager;
    private AdRewardService adRewardService;
    private DailyRewardService dailyRewardService;
    private CoinBonusService coinBonusService;
    private TutorialUI tutorialUI;

    private LevelState lastLevelState = LevelState.Idle;
    private bool subscribed;

    private void Awake()
    {
        flowController     = FindAnyObjectByType<LevelFlowController>();
        progression        = FindAnyObjectByType<ProgressionManager>();
        shopManager        = FindAnyObjectByType<ShopManager>();
        adRewardService    = FindAnyObjectByType<AdRewardService>();
        dailyRewardService = FindAnyObjectByType<DailyRewardService>();
        coinBonusService   = FindAnyObjectByType<CoinBonusService>();
        tutorialUI         = FindAnyObjectByType<TutorialUI>();

        Subscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        if (flowController != null)
        {
            flowController.OnLevelStarted         += HandleLevelStarted;
            flowController.OnLevelCompleted       += HandleLevelCompleted;
            flowController.OnLevelFailed          += HandleLevelFailed;
            flowController.OnLevelRestarted       += HandleLevelRestarted;
            flowController.OnStateChanged         += HandleLevelStateChanged;
            flowController.OnReturnToMapRequested += HandleReturnToMap;
            lastLevelState = flowController.CurrentState;
        }

        if (progression != null)
            progression.OnLevelUnlocked += HandleLevelUnlocked;

        if (shopManager != null)
            shopManager.OnItemPurchased += HandleItemPurchased;

        if (adRewardService != null)
            adRewardService.OnAdRewardGranted += HandleAdRewardGranted;

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed += HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed += HandleCoinBonusCollected;

        if (tutorialUI != null)
        {
            tutorialUI.OnTutorialStarted       += HandleTutorialStarted;
            tutorialUI.OnTutorialStepCompleted += HandleTutorialStepCompleted;
            tutorialUI.OnTutorialSkipped       += HandleTutorialSkipped;
            tutorialUI.OnTutorialCompleted     += HandleTutorialCompleted;
        }
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        if (flowController != null)
        {
            flowController.OnLevelStarted         -= HandleLevelStarted;
            flowController.OnLevelCompleted       -= HandleLevelCompleted;
            flowController.OnLevelFailed          -= HandleLevelFailed;
            flowController.OnLevelRestarted       -= HandleLevelRestarted;
            flowController.OnStateChanged         -= HandleLevelStateChanged;
            flowController.OnReturnToMapRequested -= HandleReturnToMap;
        }

        if (progression != null)
            progression.OnLevelUnlocked -= HandleLevelUnlocked;

        if (shopManager != null)
            shopManager.OnItemPurchased -= HandleItemPurchased;

        if (adRewardService != null)
            adRewardService.OnAdRewardGranted -= HandleAdRewardGranted;

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed -= HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed -= HandleCoinBonusCollected;

        if (tutorialUI != null)
        {
            tutorialUI.OnTutorialStarted       -= HandleTutorialStarted;
            tutorialUI.OnTutorialStepCompleted -= HandleTutorialStepCompleted;
            tutorialUI.OnTutorialSkipped       -= HandleTutorialSkipped;
            tutorialUI.OnTutorialCompleted     -= HandleTutorialCompleted;
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Level
    // ─────────────────────────────────────────────────────────────

    private void HandleLevelStarted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        AnalyticsService.Track(AnalyticsEvents.LevelStarted, LevelParams(level));
    }

    private void HandleLevelCompleted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        var p = LevelParams(level);

        // ProgressionManager.CompleteLevel has already run at this point,
        // so the persisted star value is the authoritative result.
        if (level != null && ProgressionManager.Instance != null)
            p["stars"] = ProgressionManager.Instance.GetLevelStars(level.LevelId);

        AnalyticsService.Track(AnalyticsEvents.LevelCompleted, p);
    }

    private void HandleLevelFailed(string reason)
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        var p = LevelParams(level);
        p["reason"] = reason;
        AnalyticsService.Track(AnalyticsEvents.LevelFailed, p);
    }

    private void HandleLevelRestarted()
    {
        var level = flowController != null ? flowController.CurrentLevelData : null;
        AnalyticsService.Track(AnalyticsEvents.LevelRestarted, LevelParams(level));
    }

    private void HandleLevelStateChanged(LevelState state)
    {
        // ReturnToMap sets Idle BEFORE firing OnReturnToMapRequested, so Idle
        // must not overwrite the remembered state — it holds the state being
        // exited, which is what distinguishes abandon from normal completion.
        if (state != LevelState.Idle)
            lastLevelState = state;
    }

    private void HandleReturnToMap()
    {
        // Returning to map while a level was Playing = abandon.
        // Returning after Completed (Continue/Map on the reward overlay) is
        // normal flow, not abandonment.
        if (lastLevelState == LevelState.Playing)
        {
            var level = flowController != null ? flowController.CurrentLevelData : null;
            AnalyticsService.Track(AnalyticsEvents.LevelAbandoned, LevelParams(level));
        }
        lastLevelState = LevelState.Idle;
    }

    private void HandleLevelUnlocked(LevelData level)
    {
        AnalyticsService.Track(AnalyticsEvents.LevelUnlocked, LevelParams(level));
    }

    // ─────────────────────────────────────────────────────────────
    //  Shop / Reward / Meta
    // ─────────────────────────────────────────────────────────────

    private void HandleItemPurchased(ShopItemDefinition item)
    {
        if (item == null) return;
        AnalyticsService.Track(AnalyticsEvents.ShopPurchaseSuccess, new Dictionary<string, object>
        {
            ["item_id"]     = item.ItemId,
            ["cost_type"]   = item.CostType.ToString(),
            ["cost_amount"] = item.CostAmount
        });
    }

    private void HandleAdRewardGranted(RewardedAdPlacement placement, int contextValue)
    {
        // Only fires after the rewarded flow actually completed and granted
        // its benefit — never logged for failed/cancelled mock ads.
        AnalyticsService.Track(AnalyticsEvents.RewardDoubled, new Dictionary<string, object>
        {
            ["placement"] = placement.ToString(),
            ["coins"]     = contextValue
        });
    }

    private void HandleDailyRewardClaimed(int streakDay, RewardBundle bundle)
    {
        AnalyticsService.Track(AnalyticsEvents.DailyRewardClaimed, new Dictionary<string, object>
        {
            ["streak_day"] = streakDay,
            ["coins"]      = bundle != null ? bundle.CoinCount : 0
        });
    }

    private void HandleCoinBonusCollected(int amount)
    {
        AnalyticsService.Track(AnalyticsEvents.CoinBonusCollected, new Dictionary<string, object>
        {
            ["coins"] = amount
        });
    }

    // ─────────────────────────────────────────────────────────────
    //  Tutorial
    // ─────────────────────────────────────────────────────────────

    private void HandleTutorialStarted()
    {
        AnalyticsService.Track(AnalyticsEvents.TutorialStarted);
    }

    private void HandleTutorialStepCompleted(int stepIndex)
    {
        AnalyticsService.Track(AnalyticsEvents.TutorialStepCompleted, new Dictionary<string, object>
        {
            ["step"] = stepIndex
        });
    }

    private void HandleTutorialSkipped()
    {
        AnalyticsService.Track(AnalyticsEvents.TutorialSkipped);
    }

    private void HandleTutorialCompleted()
    {
        AnalyticsService.Track(AnalyticsEvents.TutorialCompleted);
    }

    // ─────────────────────────────────────────────────────────────
    //  Helpers
    // ─────────────────────────────────────────────────────────────

    private static Dictionary<string, object> LevelParams(LevelData level)
    {
        var p = new Dictionary<string, object>();
        if (level != null)
        {
            p["level_id"]    = level.LevelId;
            p["world_index"] = level.World;
        }
        return p;
    }
}

