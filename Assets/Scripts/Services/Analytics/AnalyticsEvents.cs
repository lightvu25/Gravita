/// <summary>
/// Centralized analytics event names. Only events with a reliable runtime
/// source are defined here — do not add speculative names.
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

    // Progression
    public const string LevelUnlocked = "level_unlocked";
    public const string WorldOpened   = "world_opened";

    // Reward
    public const string RewardShown   = "reward_shown";
    public const string RewardClaimed = "reward_claimed";
    public const string RewardDoubled = "reward_doubled";

    // Shop
    public const string ShopOpened          = "shop_opened";
    public const string ShopPurchaseSuccess = "shop_purchase_success";
    public const string ShopPurchaseFailed  = "shop_purchase_failed";

    // Meta
    public const string DailyRewardClaimed  = "daily_reward_claimed";
    public const string CoinBonusCollected   = "coin_bonus_collected";
}
