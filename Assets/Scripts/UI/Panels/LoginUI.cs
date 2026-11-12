using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class LoginUI : MonoBehaviour, IUIPanel
{
    [Header("Inputs (TextMeshPro)")]
    [SerializeField] private TMP_InputField emailInput;
    [SerializeField] private TMP_InputField passwordInput;

    [Header("Buttons")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button googleLoginButton;
    [SerializeField] private Button closeButton;

    [Header("Labels")]
    [SerializeField] private TMP_Text statusText;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private bool busy;
    private bool buttonsBound;
    private bool subscribed;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private IAuthService Auth => FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;

    private void Awake()
    {
        BindButtons();
        EnsureInputsEditable();
    }

    private void OnEnable()
    {
        BindButtons();
        EnsureInputsEditable();
        StartCoroutine(WaitForAuth());
    }

    private void OnDisable()
    {
        var auth = Auth;
        if (subscribed && auth != null)
        {
            auth.OnStateChanged -= HandleStateChanged;
            subscribed = false;
        }
        busy = false;
    }

    private void OnDestroy()
    {
        var auth = Auth;
        if (subscribed && auth != null)
        {
            auth.OnStateChanged -= HandleStateChanged;
            subscribed = false;
        }

        UnbindButtons();
    }

    private void EnsureInputsEditable()
    {
        if (emailInput != null)
        {
            emailInput.interactable = true;
            emailInput.readOnly = false;
        }

        if (passwordInput != null)
        {
            passwordInput.interactable = true;
            passwordInput.readOnly = false;
        }
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (loginButton != null) loginButton.onClick.AddListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.AddListener(OnRegisterClicked);
        if (googleLoginButton != null) googleLoginButton.onClick.AddListener(OnGoogleLoginClicked);
        if (closeButton != null) closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (loginButton != null) loginButton.onClick.RemoveListener(OnLoginClicked);
        if (registerButton != null) registerButton.onClick.RemoveListener(OnRegisterClicked);
        if (googleLoginButton != null) googleLoginButton.onClick.RemoveListener(OnGoogleLoginClicked);
        if (closeButton != null) closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    public void Show()
    {
        BindButtons();
        EnsureInputsEditable();
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
        else FinishHide();
    }

    private void FinishHide() => gameObject.SetActive(false);

    private IEnumerator WaitForAuth()
    {
        EnsureInputsEditable();

        var initialAuth = Auth;
        if (initialAuth != null && initialAuth.State != AuthState.Loading)
        {
            if (!subscribed)
            {
                initialAuth.OnStateChanged += HandleStateChanged;
                subscribed = true;
            }
            RefreshView();
            yield break;
        }

        ShowStatus("Connecting to Firebase...");
        SetButtonsInteractable(false);

        float waited = 0f;
        const float timeout = 30f;
        while (waited < timeout)
        {
            var auth = Auth;
            if (auth != null && auth.State != AuthState.Loading)
            {
                if (!subscribed)
                {
                    auth.OnStateChanged += HandleStateChanged;
                    subscribed = true;
                }
                RefreshView();
                yield break;
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        ShowStatus("Firebase is unavailable - playing offline.");
        EnsureInputsEditable();
        SetButtonsInteractable(true);
    }

    private async void OnLoginClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (!ValidateInputs(out string email, out string password)) return;
        if (Auth == null)
        {
            ShowStatus("Firebase is unavailable.");
            return;
        }

        SetBusy(true, "Signing in...");
        AuthResult result = await Auth.SignInAsync(email, password);
        if (this == null) return;
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    private async void OnRegisterClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (!ValidateInputs(out string email, out string password)) return;
        if (Auth == null)
        {
            ShowStatus("Firebase is unavailable.");
            return;
        }

        SetBusy(true, "Creating account...");
        AuthResult result = await Auth.RegisterAsync(email, password);
        if (this == null) return;
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    private async void OnGoogleLoginClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (Auth == null)
        {
            ShowStatus("Firebase is unavailable.");
            return;
        }

        SetBusy(true, "Opening Google sign-in...");
        AuthResult result = await Auth.SignInWithGoogleAsync();
        if (this == null) return;
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        CloseSelf();
    }

    private bool ValidateInputs(out string email, out string password)
    {
        email = emailInput != null && emailInput.text != null ? emailInput.text.Trim() : string.Empty;
        password = passwordInput != null && passwordInput.text != null ? passwordInput.text : string.Empty;

        if (string.IsNullOrEmpty(email))
        {
            ShowStatus("Enter your email.");
            return false;
        }
        if (!email.Contains("@"))
        {
            ShowStatus("That doesn't look like an email address.");
            return false;
        }
        if (string.IsNullOrEmpty(password))
        {
            ShowStatus("Enter your password.");
            return false;
        }
        if (password.Length < 6)
        {
            ShowStatus("Password must be at least 6 characters.");
            return false;
        }
        return true;
    }

    private void HandleStateChanged()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        EnsureInputsEditable();

        var auth = Auth;
        if (auth == null)
        {
            ShowStatus("Firebase is unavailable - playing offline.");
            SetButtonsInteractable(true);
            return;
        }

        if (auth.State == AuthState.Loading)
        {
            ShowStatus("Connecting to Firebase...");
            SetButtonsInteractable(false);
            return;
        }

        // Only auto-close when signed in with a non-anonymous account (Email or Google)
        if (auth.State == AuthState.SignedIn && !auth.IsAnonymous)
        {
            CloseSelf();
            return;
        }

        ShowStatus(string.Empty);
        SetButtonsInteractable(true);
    }

    private void CloseSelf()
    {
        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Login))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Login);
        else
            gameObject.SetActive(false);
    }

    private void SetBusy(bool value, string message)
    {
        busy = value;
        if (emailInput != null) emailInput.interactable = !value;
        if (passwordInput != null) passwordInput.interactable = !value;
        SetButtonsInteractable(!value);
        if (message != null) ShowStatus(message);
    }

    private void SetButtonsInteractable(bool value)
    {
        if (busy && value) return;
        if (loginButton != null) loginButton.interactable = value;
        if (registerButton != null) registerButton.interactable = value;
        if (googleLoginButton != null) googleLoginButton.interactable = value;
    }

    private void ShowStatus(string message)
    {
        if (statusText != null) statusText.text = message ?? string.Empty;
    }
}
