using System;

public enum AdResultStatus
{
    Completed,
    Skipped,
    Failed,
    Unavailable
}

/// <summary>
/// Encapsulates the completion state of a rewarded ad view.
/// </summary>
[Serializable]
public struct RewardedAdResult
{
    public AdResultStatus Status { get; }
    public RewardedAdPlacement Placement { get; }
    public string ErrorMessage { get; }

    public bool IsSuccess => Status == AdResultStatus.Completed;

    public RewardedAdResult(AdResultStatus status, RewardedAdPlacement placement, string errorMessage = "")
    {
        Status = status;
        Placement = placement;
        ErrorMessage = errorMessage ?? "";
    }

    public static RewardedAdResult Success(RewardedAdPlacement placement) => 
        new RewardedAdResult(AdResultStatus.Completed, placement);

    public static RewardedAdResult Skipped(RewardedAdPlacement placement) => 
        new RewardedAdResult(AdResultStatus.Skipped, placement);

    public static RewardedAdResult Failed(RewardedAdPlacement placement, string error) => 
        new RewardedAdResult(AdResultStatus.Failed, placement, error);

    public static RewardedAdResult Unavailable(RewardedAdPlacement placement) => 
        new RewardedAdResult(AdResultStatus.Unavailable, placement, "Ad not ready or fill unavailable.");
}

