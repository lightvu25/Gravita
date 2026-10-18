public class AuthResult
{
    /// <summary>True when the operation completed and a user is signed in.</summary>
    public bool Success { get; }

    /// <summary>Firebase UID of the user — null when <see cref="Success"/> is false.</summary>
    public string UserId { get; }

    /// <summary>Human-readable reason for failure — safe to show directly in the UI.</summary>
    public string ErrorMessage { get; }

    private AuthResult(bool success, string userId, string errorMessage)
    {
        Success = success;
        UserId = userId;
        ErrorMessage = errorMessage;
    }

    /// <summary>Creates a successful result carrying the signed-in user's id.</summary>
    public static AuthResult Ok(string userId) => new AuthResult(true, userId, null);

    /// <summary>Creates a failed result carrying a message the UI can display.</summary>
    public static AuthResult Fail(string errorMessage) => new AuthResult(false, null, errorMessage);
}
