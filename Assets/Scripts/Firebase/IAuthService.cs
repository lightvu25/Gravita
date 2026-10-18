using System;
using System.Threading.Tasks;

public interface IAuthService
{
    AuthState State { get; }
    bool IsSignedIn { get; }
    string UserId { get; }
    string Email { get; }
    event Action<string> OnUserSignedIn;
    event Action OnStateChanged;
    Task<string> SignInAnonymouslyAsync();
    Task<AuthResult> SignInWithGoogleAsync();
    Task<AuthResult> SignInAsync(string email, string password);
    Task<AuthResult> RegisterAsync(string email, string password);
    void SignOut();
}
