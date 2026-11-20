using System;
using System.Threading.Tasks;
using Firebase;
using Firebase.Auth;
using UnityEngine;

/// <summary>
/// IAuthService backed by Firebase Authentication.
/// Supports email/password sign-in and registration, Google OAuth 2.0 sign-in (Mobile & Desktop/Editor),
/// and seamless upgrading of anonymous guest accounts.
/// </summary>
public class FirebaseAuthService : IAuthService
{
    private readonly FirebaseAuth auth;
    private bool lastKnownIsAnonymous;

    public AuthState State { get; private set; } = AuthState.Loading;
    public bool IsSignedIn => auth != null && auth.CurrentUser != null;
    public bool IsAnonymous => IsSignedIn && auth.CurrentUser.IsAnonymous;
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

        auth.StateChanged += HandleAuthStateChanged;
        RefreshState();
    }

    public async Task<string> SignInAnonymouslyAsync()
    {
        if (auth == null) return null;
        if (IsSignedIn) return UserId;

        try
        {
            Firebase.Auth.AuthResult result = await auth.SignInAnonymouslyAsync();
            string uid = result != null && result.User != null ? result.User.UserId : null;
            RefreshState();
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
        if (auth == null)
            return AuthResult.Fail("Firebase is unavailable — please try again shortly.");

        try
        {
            bool isMobileNative = !Application.isEditor
                && (Application.platform == RuntimePlatform.Android || Application.platform == RuntimePlatform.IPhonePlayer);

            if (isMobileNative)
            {
                return await SignInWithGoogleMobileProviderAsync();
            }

            return await SignInWithGoogleLoopbackCredentialAsync();
        }
        catch (FirebaseException e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Google Firebase sign-in failed ({(AuthError)e.ErrorCode}): {e.Message}");
            AuthError code = (AuthError)e.ErrorCode;
            if (code == AuthError.Failure || code == AuthError.InvalidCredential || code == AuthError.OperationNotAllowed)
            {
                return AuthResult.Fail(
                    "Google Sign-In failed: enable Google provider & add Android SHA-1 in Firebase Console, then update google-services.json.");
            }
            return AuthResult.Fail(ToFriendlyMessage(e));
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Google sign-in failed: {e}");
            return AuthResult.Fail("Google Sign-In failed — please check Firebase OAuth setup and try again.");
        }
    }

    private async Task<AuthResult> SignInWithGoogleMobileProviderAsync()
    {
        var providerData = new FederatedOAuthProviderData
        {
            ProviderId = "google.com",
            Scopes = new[] { "email", "profile" }
        };
        var provider = new FederatedOAuthProvider(providerData);

        Firebase.Auth.AuthResult result = null;
        if (IsAnonymous && auth.CurrentUser != null)
        {
            try
            {
                result = await auth.CurrentUser.LinkWithProviderAsync(provider);
            }
            catch (FirebaseException linkEx) when (IsCredentialAlreadyInUseError((AuthError)linkEx.ErrorCode))
            {
                result = await auth.SignInWithProviderAsync(provider);
            }
        }
        else
        {
            result = await auth.SignInWithProviderAsync(provider);
        }

        string uid = result != null && result.User != null ? result.User.UserId : null;
        if (string.IsNullOrEmpty(uid))
            return AuthResult.Fail("Google sign-in returned no user.");

        RefreshState();
        OnUserSignedIn?.Invoke(uid);
        return AuthResult.Ok(uid);
    }

    private async Task<AuthResult> SignInWithGoogleLoopbackCredentialAsync()
    {
        GoogleOAuthHelper.GoogleTokenResult tokenResult =
            await GoogleOAuthHelper.AcquireTokensViaLoopbackAsync(GoogleAuthConfig.Active);

        if (!tokenResult.Success)
            return AuthResult.Fail(tokenResult.ErrorMessage);

        Credential credential = GoogleAuthProvider.GetCredential(tokenResult.IdToken, tokenResult.AccessToken);
        return await SignInOrLinkCredentialAsync(credential, "Google sign-in returned no user.");
    }

    private async Task<AuthResult> SignInOrLinkCredentialAsync(Credential credential, string emptyUserMessage)
    {
        FirebaseUser signedInUser = null;

        if (IsAnonymous && auth.CurrentUser != null)
        {
            try
            {
                Firebase.Auth.AuthResult linkResult = await auth.CurrentUser.LinkWithCredentialAsync(credential);
                signedInUser = linkResult != null ? linkResult.User : auth.CurrentUser;
            }
            catch (FirebaseException linkEx) when (IsCredentialAlreadyInUseError((AuthError)linkEx.ErrorCode))
            {
                signedInUser = await auth.SignInWithCredentialAsync(credential);
            }
        }
        else
        {
            signedInUser = await auth.SignInWithCredentialAsync(credential);
        }

        string uid = signedInUser != null ? signedInUser.UserId : UserId;
        if (string.IsNullOrEmpty(uid))
            return AuthResult.Fail(emptyUserMessage);

        RefreshState();
        OnUserSignedIn?.Invoke(uid);
        return AuthResult.Ok(uid);
    }

    public async Task<AuthResult> SignInAsync(string email, string password)
    {
        if (auth == null)
            return AuthResult.Fail("Firebase is unavailable — please try again shortly.");

        try
        {
            Firebase.Auth.AuthResult result = await auth.SignInWithEmailAndPasswordAsync(email, password);
            string uid = result != null && result.User != null ? result.User.UserId : null;
            if (string.IsNullOrEmpty(uid))
                return AuthResult.Fail("Sign-in returned no user.");

            RefreshState();
            OnUserSignedIn?.Invoke(uid);
            return AuthResult.Ok(uid);
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
        if (auth == null)
            return AuthResult.Fail("Firebase is unavailable — please try again shortly.");

        try
        {
            Firebase.Auth.AuthResult result;
            if (IsAnonymous && auth.CurrentUser != null)
            {
                Credential credential = EmailAuthProvider.GetCredential(email, password);
                result = await auth.CurrentUser.LinkWithCredentialAsync(credential);
            }
            else
            {
                result = await auth.CreateUserWithEmailAndPasswordAsync(email, password);
            }

            string uid = result != null && result.User != null ? result.User.UserId : UserId;
            if (string.IsNullOrEmpty(uid))
                return AuthResult.Fail("Registration returned no user.");

            RefreshState();
            OnUserSignedIn?.Invoke(uid);
            return AuthResult.Ok(uid);
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
            RefreshState();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseAuthService] Sign-out failed: {e.Message}");
        }
    }

    private void HandleAuthStateChanged(object sender, EventArgs e)
    {
        RefreshState();
    }

    private void RefreshState()
    {
        AuthState newState = auth == null ? AuthState.Error
            : IsSignedIn ? AuthState.SignedIn
            : AuthState.SignedOut;
        bool currentAnonymous = IsAnonymous;

        if (newState == State && currentAnonymous == lastKnownIsAnonymous)
            return;

        State = newState;
        lastKnownIsAnonymous = currentAnonymous;
        OnStateChanged?.Invoke();
    }

    private static bool IsCredentialAlreadyInUseError(AuthError error)
    {
        return error == AuthError.CredentialAlreadyInUse
            || error == AuthError.EmailAlreadyInUse
            || error == AuthError.AccountExistsWithDifferentCredentials;
    }

    private static string ToFriendlyMessage(FirebaseException e)
    {
        switch ((AuthError)e.ErrorCode)
        {
            case AuthError.InvalidEmail:
                return "That doesn't look like a valid email address.";
            case AuthError.WrongPassword:
            case AuthError.InvalidCredential:
            case AuthError.Failure:
                return "Email or password is incorrect.";
            case AuthError.UserNotFound:
                return "No account exists for that email — try Register instead.";
            case AuthError.EmailAlreadyInUse:
            case AuthError.CredentialAlreadyInUse:
                return "That account is already registered — try Login instead.";
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
