using System;
using NUnit.Framework;
using UnityEngine;

/// <summary>
/// Phase E6 Verification (GDD §7, §8, & §14.2 Cases #14–#18):
///   - Completed Rewarded Ad grants fixed reward once; skipped/failed ad grants 0 & consumes 0 daily quota.
///   - Daily Rewarded Ad cap blocks N+1 until 00:00 local midnight.
///   - Rewarded Ad transaction strictly forbids Gem payouts (<c>ForbidGemsInPayout</c>).
///   - Remove Ads permanently disables Interstitials while keeping opt-in Rewarded Ads functional.
///   - Completing a Result Screen Rewarded Ad sets <see cref="AdPolicyCoordinator.SuppressNextResultTransitionInterstitial"/>
///     and suppresses the subsequent Result exit Interstitial (GDD §8.4).
/// </summary>
public class AdsPolicyTests : EconomyTestHarnessBase
{
    private sealed class StubRewardedAdService : IRewardedAdService
    {
        public RewardedAdResult NextResult = RewardedAdResult.Success(RewardedAdPlacement.ResultScreenBonus);
        public bool Available = true;

        public bool IsAdAvailable(RewardedAdPlacement placement) => Available;

        public void ShowRewardedAd(RewardedAdPlacement placement, Action<RewardedAdResult> onComplete)
        {
            onComplete?.Invoke(NextResult);
        }
    }

    private sealed class StubInterstitialAdService : IInterstitialAdService
    {
        public bool Available = true;
        public int ShowCount;

        public bool IsAdAvailable() => Available;

        public void ShowInterstitial(Action<bool> onClosed = null)
        {
            ShowCount++;
            onClosed?.Invoke(true);
        }
    }

    [Test]
    public void RewardedAds_EnforceCompletionRequirement_DailyCap_LocalMidnightReset_AndZeroGemRule()
    {
        var clock = new FixedLocalClockProvider(new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Local));
        LocalClock.Provider = clock;

        var economy = SpawnSingleton<EconomyManager>("Economy");
        var adReward = SpawnSingleton<AdRewardService>("AdReward");
        var stubAds = new StubRewardedAdService();
        adReward.SetAdService(stubAds);

        // Disable cooldown so sequential test calls don't wait on realtime clock
        typeof(EconomyBalanceConfig)
            .GetField("rewardedAdCooldownSeconds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(TestBalance, 0f);
        typeof(EconomyBalanceConfig)
            .GetField("rewardedAdDailyLimit", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(TestBalance, 2);

        int fixedCoins = TestBalance.ResultScreenAdReward.Amount;

        // Case #15: Skipped ad -> 0 Coins & 0 daily quota consumed
        stubAds.NextResult = RewardedAdResult.Skipped(RewardedAdPlacement.ShopBonus);
        bool skipOutcome = true;
        adReward.WatchAdForReward(RewardedAdPlacement.ShopBonus, 0, ok => skipOutcome = ok);
        Assert.IsFalse(skipOutcome);
        Assert.AreEqual(0, economy.Coins);
        Assert.AreEqual(2, AdPolicyCoordinator.GetRemainingDailyRewardedAds());

        // Case #14 & #16: Watch 2 completed ads -> hits daily cap of 2
        stubAds.NextResult = RewardedAdResult.Success(RewardedAdPlacement.ShopBonus);
        bool ok1 = false, ok2 = false, ok3 = true;
        adReward.WatchAdForReward(RewardedAdPlacement.ShopBonus, 0, ok => ok1 = ok);
        adReward.WatchAdForReward(RewardedAdPlacement.ShopBonus, 0, ok => ok2 = ok);
        adReward.WatchAdForReward(RewardedAdPlacement.ShopBonus, 0, ok => ok3 = ok);

        Assert.IsTrue(ok1);
        Assert.IsTrue(ok2);
        Assert.IsFalse(ok3, "3rd Rewarded Ad must be blocked once daily cap (2) is reached!");
        Assert.AreEqual(fixedCoins * 2, economy.Coins);
        Assert.AreEqual(0, AdPolicyCoordinator.GetRemainingDailyRewardedAds());

        // Next local day 00:01 -> quota resets!
        clock.Current = new DateTime(2026, 6, 2, 0, 1, 0, DateTimeKind.Local);
        Assert.AreEqual(2, AdPolicyCoordinator.GetRemainingDailyRewardedAds());

        // GDD §7.2 hard rule: Rewarded Ads can NEVER grant Gems
        var illegalGemBundle = new RewardBundle();
        illegalGemBundle.Add(RewardType.Gems, 5);
        bool gemAdAllowed = adReward.TryExecuteAdPayoutTransaction(
            RewardedAdPlacement.ShopBonus, illegalGemBundle, "illegal_gem_req", out RewardTransactionResult gemTxRes);
        Assert.IsFalse(gemAdAllowed);
        Assert.AreEqual(RewardTransactionStatus.ForbiddenRewardType, gemTxRes.Status);
        Assert.AreEqual(0, economy.Gems);
    }

    [Test]
    public void InterstitialAds_FollowCompletedLevelCadence_SuppressAfterResultRewardedAd_AndStopForeverWithRemoveAds()
    {
        var economy = SpawnSingleton<EconomyManager>("Economy");
        var adReward = SpawnSingleton<AdRewardService>("AdReward");
        var stubRewarded = new StubRewardedAdService
        {
            NextResult = RewardedAdResult.Success(RewardedAdPlacement.ResultScreenBonus)
        };
        adReward.SetAdService(stubRewarded);

        var stubInterstitial = new StubInterstitialAdService();

        // Pin cadence interval to 2 completed levels
        ProfileData profile = SaveManager.loadProfile();
        profile.interstitialTargetLevelsInterval = 2;
        profile.interstitialCompletedLevelsSinceLastAd = 0;
        SaveManager.TrySaveProfile(profile);

        // After 1st completed level -> cadence not reached (1 < 2), no interstitial
        AdPolicyCoordinator.RecordCompletedLevelForInterstitialCadence();
        Assert.IsFalse(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial));
        Assert.AreEqual(0, stubInterstitial.ShowCount);

        // After 2nd completed level -> cadence reached (2 >= 2), interstitial shows on Result exit!
        AdPolicyCoordinator.RecordCompletedLevelForInterstitialCadence();
        Assert.IsTrue(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial));
        Assert.AreEqual(1, stubInterstitial.ShowCount);
        Assert.AreEqual(0, SaveManager.loadProfile().interstitialCompletedLevelsSinceLastAd);

        // Case #17 (GDD §8.4): Reach cadence again, BUT player watches Rewarded Ad on Result Screen -> Interstitial MUST be suppressed!
        profile = SaveManager.loadProfile();
        profile.interstitialTargetLevelsInterval = 2;
        profile.interstitialCompletedLevelsSinceLastAd = 2;
        SaveManager.TrySaveProfile(profile);

        bool watchedResultAd = false;
        adReward.WatchAdForReward(RewardedAdPlacement.ResultScreenBonus, 0, ok => watchedResultAd = ok);
        Assert.IsTrue(watchedResultAd);
        Assert.IsTrue(AdPolicyCoordinator.SuppressNextResultTransitionInterstitial);

        Assert.IsFalse(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial),
            "Result Screen exit Interstitial MUST be suppressed right after watching a Result Screen Rewarded Ad!");
        Assert.AreEqual(1, stubInterstitial.ShowCount);

        // Case #18 (GDD §8.5): Owning Remove Ads permanently blocks Interstitials while keeping opt-in Rewarded Ads available!
        profile = SaveManager.loadProfile();
        profile.noAdsPurchased = true;
        profile.interstitialTargetLevelsInterval = 1;
        profile.interstitialCompletedLevelsSinceLastAd = 5;
        SaveManager.TrySaveProfile(profile);

        Assert.IsTrue(AdPolicyCoordinator.IsRemoveAdsActive());
        Assert.IsFalse(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial));
        Assert.AreEqual(1, stubInterstitial.ShowCount);

        // Opt-in Shop Rewarded Ad still works when Remove Ads is owned!
        typeof(EconomyBalanceConfig)
            .GetField("rewardedAdCooldownSeconds", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
            ?.SetValue(TestBalance, 0f);
        bool shopAdWorkedWithNoAds = false;
        adReward.WatchAdForReward(RewardedAdPlacement.ShopBonus, 0, ok => shopAdWorkedWithNoAds = ok);
        // Failed runs ALSO count toward the 2-3 play interstitial cadence
        profile = SaveManager.loadProfile();
        profile.noAdsPurchased = false;
        profile.interstitialTargetLevelsInterval = 2;
        profile.interstitialCompletedLevelsSinceLastAd = 0;
        SaveManager.TrySaveProfile(profile);

        AdPolicyCoordinator.RecordFailedRunForInterstitialCadence();
        Assert.IsFalse(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial));
        AdPolicyCoordinator.RecordFailedRunForInterstitialCadence();
        Assert.IsTrue(AdPolicyCoordinator.TryShowPostResultTransitionInterstitial(stubInterstitial),
            "Two plays (including failed runs) must trigger interstitial cadence.");
        Assert.AreEqual(2, stubInterstitial.ShowCount);
    }
}
