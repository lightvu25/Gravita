using System;
using System.Collections.Generic;
using UnityEngine;

public enum EventBannerActionType
{
    OpenEventDetailPopup = 0,
    JumpToPanel = 1,
    StartRunnerMode = 2,
}

public enum EventObjectiveKind
{
    MissionTypeStat = 0,
    RunnerBestRooms = 1,
    RunnerLifetimeRooms = 2,
}

[Serializable]
public struct GameEventTaskEntry
{
    public string taskId;
    public string title;
    public EventObjectiveKind objectiveKind;
    public MissionService.MissionType missionType;
    [Min(1)] public int target;
    public MissionRewardType rewardType;
    [Min(0)] public int rewardAmount;
    public string rewardPowerUpId;
    public string rewardCosmeticId;
    public Sprite rewardIcon;

    public void Sanitize()
    {
        taskId = taskId != null ? taskId.Trim() : string.Empty;
        title = title != null ? title : string.Empty;
        target = Mathf.Max(1, target);
        rewardAmount = rewardType == MissionRewardType.Cosmetic
            ? Mathf.Max(1, rewardAmount)
            : Mathf.Max(0, rewardAmount);
        if (string.IsNullOrWhiteSpace(rewardPowerUpId))
            rewardPowerUpId = PowerUpIds.Breaker;
        rewardCosmeticId ??= string.Empty;
    }
}

/// <summary>
/// Data-driven ScriptableObject defining a Live-Ops Event banner, countdown schedule,
/// detail view copy/CTA routing, and claimable event tasks.
/// </summary>
[CreateAssetMenu(fileName = "NewGameEvent", menuName = "Gravita/Events/Game Event Definition")]
public class GameEventDefinition : ScriptableObject
{
    [Header("Identity & Banner Display")]
    [SerializeField] private string eventId = "event_01";
    [SerializeField] private string title = "NEW LIVE EVENT";
    [TextArea(2, 3)]
    [SerializeField] private string bannerDescription = "Limited-time challenge event.";
    [TextArea(2, 5)]
    [SerializeField] private string detailRulesText = "Complete objectives before the timer ends to earn rewards.";
    [SerializeField] private Sprite bannerSprite;
    [SerializeField] private Sprite detailBannerSprite;

    [Header("Schedule")]
    [Tooltip("False = rolling window starting from player's first view; True = fixed yyyy-MM-dd start date.")]
    [SerializeField] private bool useFixedDateSchedule = false;
    [SerializeField] private string startDateIso = "2026-11-01";
    [Min(1)]
    [SerializeField] private int durationDays = 7;
    [SerializeField] private bool hideWhenExpired = false;

    [Header("Click & Detail CTA Actions")]
    [SerializeField] private EventBannerActionType bannerAction = EventBannerActionType.OpenEventDetailPopup;
    [SerializeField] private string ctaButtonLabel = "PLAY NOW";
    [SerializeField] private EventBannerActionType ctaActionType = EventBannerActionType.JumpToPanel;
    [SerializeField] private UIPanelType ctaTargetPanel = UIPanelType.Map;
    [SerializeField] private RunnerModeKind ctaRunnerMode = RunnerModeKind.Endless;

    [Header("Event Tasks")]
    [SerializeField] private List<GameEventTaskEntry> tasks = new List<GameEventTaskEntry>();

    public string EventId => !string.IsNullOrWhiteSpace(eventId) ? eventId : name;
    public string Title => !string.IsNullOrWhiteSpace(title) ? title : name;
    public string BannerDescription => bannerDescription ?? string.Empty;
    public string DetailRulesText => !string.IsNullOrWhiteSpace(detailRulesText) ? detailRulesText : BannerDescription;
    public Sprite BannerSprite => bannerSprite;
    public Sprite DetailBannerSprite => detailBannerSprite != null ? detailBannerSprite : bannerSprite;

    public bool UseFixedDateSchedule => useFixedDateSchedule;
    public string StartDateIso => startDateIso ?? string.Empty;
    public int DurationDays => Mathf.Max(1, durationDays);
    public bool HideWhenExpired => hideWhenExpired;

    public EventBannerActionType BannerAction => bannerAction;
    public string CtaButtonLabel => !string.IsNullOrWhiteSpace(ctaButtonLabel) ? ctaButtonLabel : "PLAY NOW";
    public EventBannerActionType CtaActionType => ctaActionType;
    public UIPanelType CtaTargetPanel => ctaTargetPanel;
    public RunnerModeKind CtaRunnerMode => ctaRunnerMode;
    public IReadOnlyList<GameEventTaskEntry> Tasks => tasks;

    public void Configure(
        string id,
        string displayTitle,
        string shortDescription,
        string rulesText,
        bool fixedDateSchedule,
        string isoStartDate,
        int daysDuration,
        bool hideExpired,
        EventBannerActionType bannerClickAction,
        string detailCtaLabel,
        EventBannerActionType detailCtaAction,
        UIPanelType detailCtaPanel,
        RunnerModeKind detailCtaRunnerMode,
        IEnumerable<GameEventTaskEntry> eventTasks,
        Sprite cardBanner = null,
        Sprite popupBanner = null)
    {
        eventId = id;
        title = displayTitle;
        bannerDescription = shortDescription;
        detailRulesText = rulesText;
        useFixedDateSchedule = fixedDateSchedule;
        startDateIso = isoStartDate ?? string.Empty;
        durationDays = Mathf.Max(1, daysDuration);
        hideWhenExpired = hideExpired;
        bannerAction = bannerClickAction;
        ctaButtonLabel = detailCtaLabel;
        ctaActionType = detailCtaAction;
        ctaTargetPanel = detailCtaPanel;
        ctaRunnerMode = detailCtaRunnerMode;
        if (cardBanner != null) bannerSprite = cardBanner;
        if (popupBanner != null) detailBannerSprite = popupBanner;

        tasks ??= new List<GameEventTaskEntry>();
        tasks.Clear();
        if (eventTasks != null)
        {
            foreach (GameEventTaskEntry entry in eventTasks)
                tasks.Add(entry);
        }

        Sanitize();
    }

    public void Sanitize()
    {
        eventId = eventId != null ? eventId.Trim() : string.Empty;
        durationDays = Mathf.Max(1, durationDays);
        tasks ??= new List<GameEventTaskEntry>();

        for (int i = 0; i < tasks.Count; i++)
        {
            GameEventTaskEntry t = tasks[i];
            t.Sanitize();
            tasks[i] = t;
        }
    }

    private void OnValidate()
    {
        Sanitize();
    }
}
