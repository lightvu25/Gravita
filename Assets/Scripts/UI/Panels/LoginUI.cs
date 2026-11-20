using System.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Account sign-in popup backed directly by Google Play Games Services (<see cref="PlayGamesService"/>).
/// Preserves legacy TMP input references for Scene/Prefab compatibility without touching Scene files.
/// </summary>
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
    private PlayGamesService subscribedPlayGames;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private void Awake()
    {
        BindButtons();
        EnsureInputsEditable();
    }

    private void OnEnable()
    {
        BindButtons();
        EnsureInputsEditable();
        SubscribePlayGames();
        RefreshView();
    }

    private void OnDisable()
    {
        UnsubscribePlayGames();
        busy = false;
    }

    private void OnDestroy()
    {
        UnsubscribePlayGames();
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

    private void SubscribePlayGames()
    {
        PlayGamesService pg = PlayGamesService.Instance;
        if (pg == null || ReferenceEquals(subscribedPlayGames, pg))
            return;

        UnsubscribePlayGames();
        subscribedPlayGames = pg;
        subscribedPlayGames.OnAuthenticationChanged += HandlePlayGamesAuthChanged;
    }

    private void UnsubscribePlayGames()
    {
        if (subscribedPlayGames == null)
            return;

        subscribedPlayGames.OnAuthenticationChanged -= HandlePlayGamesAuthChanged;
        subscribedPlayGames = null;
    }

    private void HandlePlayGamesAuthChanged(bool isAuthenticated)
    {
        RefreshView();
    }

    private async void OnLoginClicked()
    {
        await TriggerPlayGamesSignInAsync();
    }

    private async void OnRegisterClicked()
    {
        await TriggerPlayGamesSignInAsync();
    }

    private async void OnGoogleLoginClicked()
    {
        await TriggerPlayGamesSignInAsync();
    }

    private async Task TriggerPlayGamesSignInAsync()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        PlayGamesService pg = PlayGamesService.Instance;
        if (pg == null || !pg.IsSupported)
        {
            ShowStatus("Google Play Games sign-in is available on Android builds.");
            return;
        }

        SetBusy(true, "Signing in with Google Play Games...");
        AuthResult result = await pg.SignInAsync(interactive: true);
        if (this == null) return;
        SetBusy(false, null);

        if (result != null && result.Success)
            CloseSelf();
        else
            ShowStatus(result != null ? result.ErrorMessage : "Google Play Games sign-in failed.");
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        CloseSelf();
    }

    private void RefreshView()
    {
        EnsureInputsEditable();
        SetButtonsInteractable(true);

        PlayGamesService pg = PlayGamesService.Instance;
        if (pg != null && pg.IsAuthenticated)
        {
            CloseSelf();
            return;
        }

        ShowStatus(string.Empty);
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
        if (statusText == null) return;
        statusText.textWrappingMode = TextWrappingModes.Normal;
        statusText.text = message ?? string.Empty;
    }
}
