using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

/// <summary>
/// IAuthService backed by Firebase Authentication.
///
/// Supports email/password sign-in and registration plus anonymous guest
/// sign-in. Translates Firebase's numeric <see cref="AuthError"/> codes into
/// readable messages so the UI never has to know Firebase-specific details.
/// </summary>
public class FirebaseAuthService : IAuthService
{
    private readonly FirebaseAuth auth;

    public AuthState State { get; private set; } = AuthState.Loading;
    public bool IsSignedIn => auth != null && auth.CurrentUser != null;
    public string UserId => IsSignedIn ? auth.CurrentUser.UserId : null;
    public string Email => IsSignedIn ? auth.CurrentUser.Email : null;

    public event Action<string> OnUserSignedIn;
    public event Action OnStateChanged;

    public FirebaseAuthService(FirebaseAuth auth)
    {
        this.auth = auth;
        if (auth == null)
        {
            State = AuthState.Error;
            return;
        }

        // FirebaseAuth fires this whenever the signed-in user changes —
        // including silently at startup when it restores a saved session.
        auth.StateChanged += HandleAuthStateChanged;
        RefreshState();
    }

    // ---------------------------------------------------------------
    //  Sign-in operations
    // ---------------------------------------------------------------

    public async Task<string> SignInAnonymouslyAsync()
    {
        if (auth == null) return null;
        if (IsSignedIn) return UserId;

        try
        {
            Firebase.Auth.AuthResult result = await auth.SignInAnonymouslyAsync();
            string uid = result != null && result.User != null ? result.User.UserId : null;
            if (!string.IsNullOrEmpty(uid))
                OnUserSignedIn?.Invoke(uid);
            return uid;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Anonymous sign-in failed: {e.Message}");
            return null;
        }
    }

    public async Task<AuthResult> SignInWithGoogleAsync()
    {
        if (auth == null) return AuthResult.Fail("Firebase is not initialized yet — try again in a moment.");

        try
        {
            var providerData = new FederatedOAuthProviderData
            {
                ProviderId = "google.com"
            };
            var provider = new FederatedOAuthProvider(providerData);
            Firebase.Auth.AuthResult result = await auth.SignInWithProviderAsync(provider);
            string uid = result != null && result.User != null ? result.User.UserId : null;
            if (string.IsNullOrEmpty(uid))
                return AuthResult.Fail("Google sign-in returned no user.");

            OnUserSignedIn?.Invoke(uid);
            return AuthResult.Ok(uid);
        }
        catch (FirebaseException e)
        {
            return AuthResult.Fail(ToFriendlyMessage(e));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Google sign-in failed: {e}");
            return AuthResult.Fail("Google sign-in failed — please try again.");
        }
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        if (auth == null) return AuthResult.Fail("Firebase is not initialized yet — try again in a moment.");

        try
        {
            Firebase.Auth.AuthResult result = await auth.SignInWithEmailAndPasswordAsync(email, password);
            string uid = result != null && result.User != null ? result.User.UserId : null;
            return string.IsNullOrEmpty(uid)
                ? AuthResult.Fail("Sign-in returned no user.")
                : AuthResult.Ok(uid);
        }
        catch (FirebaseException e)
        {
            return AuthResult.Fail(ToFriendlyMessage(e));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Sign-in failed: {e}");
            return AuthResult.Fail("Sign-in failed — please try again.");
        }
    }

    public async Task<AuthResult> RegisterAsync(string email, string password)
    {
        if (auth == null) return AuthResult.Fail("Firebase is not initialized yet — try again in a moment.");

        try
        {
            Firebase.Auth.AuthResult result = await auth.CreateUserWithEmailAndPasswordAsync(email, password);
            string uid = result != null && result.User != null ? result.User.UserId : null;
            return string.IsNullOrEmpty(uid)
                ? AuthResult.Fail("Registration returned no user.")
                : AuthResult.Ok(uid);
        }
        catch (FirebaseException e)
        {
            return AuthResult.Fail(ToFriendlyMessage(e));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Registration failed: {e}");
            return AuthResult.Fail("Registration failed — please try again.");
        }
    }

    public void SignOut()
    {
        try
        {
            auth?.SignOut();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Sign-out failed: {e.Message}");
        }
    }

    // ---------------------------------------------------------------
    //  State tracking — the SDK tells us when the user changes, we
    //  translate that into our simple AuthState for the rest of the game.
    // ---------------------------------------------------------------

    private void HandleAuthStateChanged(object sender, EventArgs e)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        AuthState newState = auth == null ? AuthState.Error
            : IsSignedIn ? AuthState.SignedIn
            : AuthState.SignedOut;
        if (newState == State) return;

        State = newState;
        OnStateChanged?.Invoke();
    }

    /// <summary>
    /// Turns Firebase's numeric error codes into sentences a player can read.
    /// (AuthError)e.ErrorCode converts the raw int into the named enum.
    /// </summary>
    private static string ToFriendlyMessage(FirebaseException e)
    {
        switch ((AuthError)e.ErrorCode)
        {
            case AuthError.InvalidEmail:
                return "That doesn't look like a valid email address.";
            case AuthError.WrongPassword:
            case AuthError.InvalidCredential:
            case AuthError.Failure: // desktop SDK reports bad credentials as generic Failure
                return "Email or password is incorrect.";
            case AuthError.UserNotFound:
                return "No account exists for that email — try Register instead.";
            case AuthError.EmailAlreadyInUse:
                return "That email is already registered — try Login instead.";
            case AuthError.WeakPassword:
                return "Password is too weak — use at least 6 characters.";
            case AuthError.MissingPassword:
            case AuthError.MissingEmail:
                return "Email and password are required.";
            case AuthError.OperationNotAllowed:
                return "This sign-in method is disabled in the Firebase Console.";
            case AuthError.TooManyRequests:
                return "Too many attempts — wait a moment and try again.";
            case AuthError.NetworkRequestFailed:
                return "No connection — check your internet and try again.";
            case AuthError.WebContextCancelled:
                return "Sign-in was cancelled.";
            default:
                return $"Sign-in failed ({(AuthError)e.ErrorCode}).";
        }
    }
}
