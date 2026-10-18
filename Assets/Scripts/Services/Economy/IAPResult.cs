using System;

public enum IAPResultStatus
{
    Success,
    Cancelled,
    Failed,
    Pending
}

/// <summary>
/// Encapsulates the verified result of an In-App Purchase transaction.
/// </summary>
[Serializable]
public struct IAPResult
{
    public string ProductId { get; }
    public string TransactionId { get; }
    public IAPResultStatus Status { get; }
    public string ErrorMessage { get; }

    public bool IsSuccess => Status == IAPResultStatus.Success;

    public IAPResult(string productId, string transactionId, IAPResultStatus status, string errorMessage = "")
    {
        ProductId = productId ?? "";
        TransactionId = transactionId ?? "";
        Status = status;
        ErrorMessage = errorMessage ?? "";
    }

    public static IAPResult Success(string productId, string transactionId = "") =>
        new IAPResult(productId, string.IsNullOrEmpty(transactionId) ? Guid.NewGuid().ToString() : transactionId, IAPResultStatus.Success);

    public static IAPResult Cancelled(string productId) =>
        new IAPResult(productId, "", IAPResultStatus.Cancelled, "User cancelled purchase.");

    public static IAPResult Failed(string productId, string error) =>
        new IAPResult(productId, "", IAPResultStatus.Failed, error);
}

