using TMPro;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class LoginUI : MonoBehaviour, IUIPanel
{
    [Header("Inputs")]
    [SerializeField] private InputField emailInput;
    [SerializeField] private InputField passwordInput;

    [Header("Buttons")]
    [SerializeField] private Button loginButton;
    [SerializeField] private Button registerButton;
    [SerializeField] private Button googleLoginButton;

    [Header("Labels")]
    [SerializeField] private TMP_Text statusText;

    private bool busy;
    private bool subscribed;
    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());
    private IAuthService Auth => FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;

    private void Awake()
    {
        loginButton.onClick.AddListener(OnLoginClicked);
        registerButton.onClick.AddListener(OnRegisterClicked);
        googleLoginButton.onClick.AddListener(OnGoogleLoginClicked);
    }

    private void OnEnable()
    {
        // The wait runs per-activation — UIManager's Hide() deactivates this
        // panel, which kills any in-flight coroutine, so it must restart.
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
    }

    private void OnDestroy()
    {
        var auth = Auth;
        if (subscribed && auth != null) auth.OnStateChanged -= HandleStateChanged;
    }

    public void Show()
    {
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
        ShowStatus("Connecting to Firebase…");
        SetInteractable(false);

        float waited = 0f;
        const float timeout = 30f;
        while (waited < timeout)
        {
            var auth = Auth;
            if (auth != null && auth.State != AuthState.Loading)
            {
                auth.OnStateChanged += HandleStateChanged;
                subscribed = true;
                RefreshView();
                yield break;
            }
            waited += Time.unscaledDeltaTime;
            yield return null;
        }

        ShowStatus("Firebase is unavailable — playing offline.");
    }

    private async void OnLoginClicked()
    {
        if (!ValidateInputs(out string email, out string password)) return;

        SetBusy(true, "Signing in…");
        AuthResult result = await Auth.SignInAsync(email, password);
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    private async void OnRegisterClicked()
    {
        if (!ValidateInputs(out string email, out string password)) return;

        SetBusy(true, "Creating account…");
        AuthResult result = await Auth.RegisterAsync(email, password);
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    private async void OnGoogleLoginClicked()
    {
        SetBusy(true, "Opening Google sign-in…");
        AuthResult result = await Auth.SignInWithGoogleAsync();
        SetBusy(false, null);

        if (!result.Success) ShowStatus(result.ErrorMessage);
    }

    // ---------------------------------------------------------------
    //  Input validation — cheap checks that never reach the network.
    // ---------------------------------------------------------------

    private bool ValidateInputs(out string email, out string password)
    {
        email = emailInput.text.Trim();
        password = passwordInput.text;

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

    // ---------------------------------------------------------------
    //  View state — driven by AuthState, not by button clicks.
    // ---------------------------------------------------------------

    private void HandleStateChanged()
    {
        RefreshView();
    }

    private void RefreshView()
    {
        var auth = Auth;
        if (auth == null)
        {
            ShowStatus("Firebase is unavailable — playing offline.");
            SetInteractable(false);
            return;
        }

        if (auth.State == AuthState.Loading)
        {
            ShowStatus("Connecting to Firebase…");
            SetInteractable(false);
            return;
        }
 
        if (auth.State == AuthState.SignedIn)
        {
            CloseSelf();
            return;
        }

        ShowStatus("");
        SetInteractable(true);
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
        SetInteractable(!value);
        if (message != null) ShowStatus(message);
    }

    private void SetInteractable(bool value)
    {
        if (busy && value) return;
        loginButton.interactable = value;
        registerButton.interactable = value;
        if (googleLoginButton != null) googleLoginButton.interactable = value;
        emailInput.interactable = value;
        passwordInput.interactable = value;
    }

    private void ShowStatus(string message)
    {
        if (statusText != null) statusText.text = message;
    }
}
