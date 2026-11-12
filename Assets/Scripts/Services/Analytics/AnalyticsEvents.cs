/// <summary>
/// Centralized analytics event names including Gravita Economy GDD v1.0 §13 telemetry.
/// </summary>
public static class AnalyticsEvents
{
    // Session
    public const string SessionStart = "session_start";

    // Tutorial
    public const string TutorialStarted       = "tutorial_started";
    public const string TutorialStepCompleted = "tutorial_step_completed";
    public const string TutorialSkipped       = "tutorial_skipped";
    public const string TutorialCompleted     = "tutorial_completed";

    // Level
    public const string LevelStarted   = "level_started";
    public const string LevelCompleted = "level_completed";
    public const string LevelFailed    = "level_failed";
    public const string LevelRestarted = "level_restarted";
    public const string LevelAbandoned = "level_abandoned";

    // Progression & Campaign Economy (GDD §13)
    public const string LevelUnlocked         = "level_unlocked";
    public const string WorldOpened           = "world_opened";
    public const string LevelEconomySettled   = "level_economy_settled";
    public const string SectorCompleted       = "sector_completed";
    public const string CampaignCompleted     = "campaign_completed_stats";

    // Currencies (GDD §13.1)
    public const string CoinEarned = "coin_earned";
    public const string CoinSpent  = "coin_spent";
    public const string GemEarned  = "gem_earned";
    public const string GemSpent   = "gem_spent";

    // Power Skills (GDD §13.2)
    public const string SkillChargeChanged       = "skill_charge_changed";
    public const string SkillActivationAttempted = "skill_activation_attempted";

    // Reward
    public const string RewardShown   = "reward_shown";
    public const string RewardClaimed = "reward_claimed";
    public const string RewardDoubled = "reward_doubled";

    // Ads (GDD §13.3)
    public const string RewardedAdOffered      = "rewarded_ad_offered";
    public const string RewardedAdStarted      = "rewarded_ad_started";
    public const string RewardedAdCompleted    = "rewarded_ad_completed";
    public const string RewardedAdSkipped      = "rewarded_ad_skipped";
    public const string InterstitialShown      = "interstitial_shown";
    public const string InterstitialSuppressed = "interstitial_suppressed";

    // Shop & IAP (GDD §13.4)
    public const string ShopOpened          = "shop_opened";
    public const string ShopPurchaseSuccess = "shop_purchase_success";
    public const string ShopPurchaseFailed  = "shop_purchase_failed";
    public const string IapInitiated        = "iap_initiated";
    public const string IapCompleted        = "iap_completed";
    public const string IapFailed           = "iap_failed";
    public const string IapRestored         = "iap_restored";

    // Missions & Meta (GDD §13.5)
    public const string MissionRewardClaimed = "mission_reward_claimed";
    public const string DailyRewardClaimed   = "daily_reward_claimed";
    public const string CoinBonusCollected   = "coin_bonus_collected";
}
