using UnityEngine;
using UnityEngine.UI;

public class SettingUI : MonoBehaviour, IUIPanel
{
    [Header("Buttons")]
    [SerializeField] private Button soundButton;
    [SerializeField] private Button musicButton;
    [SerializeField] private Button hapticsButton;
    [SerializeField] private Button notifyButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button logoutButton;

    [Header("Icons")]
    [SerializeField] private Image soundIcon;
    [SerializeField] private Image musicIcon;
    [SerializeField] private Image hapticsIcon;
    [SerializeField] private Image notifyIcon;

    [Header("Icon Sprites")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;
    [SerializeField] private Sprite musicOnSprite;
    [SerializeField] private Sprite musicOffSprite;
    [SerializeField] private Sprite hapticsOnSprite;
    [SerializeField] private Sprite hapticsOffSprite;
    [SerializeField] private Sprite notifyOnSprite;
    [SerializeField] private Sprite notifyOffSprite;

    [Header("Volume")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;

    [Header("Optional Animation")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private const string NotifyPrefKey = "NotificationsEnabled";

    private bool buttonsBound;
    private bool authSubscribed;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private void Awake()
    {
        BindButtons();
    }

    private void OnEnable()
    {
        BindButtons();
        GameHaptics.OnEnabledChanged += HandleHapticsEnabledChanged;
        SubscribeAuth();
        RefreshIcons();
        RefreshAccountButtons();
    }

    private void OnDisable()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
        UnsubscribeAuth();
    }

    private void OnDestroy()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
        UnsubscribeAuth();
        UnbindButtons();
    }

    internal void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (soundButton != null) soundButton.onClick.AddListener(ToggleSound);
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (hapticsButton != null) hapticsButton.onClick.AddListener(ToggleHaptics);
        if (notifyButton != null) notifyButton.onClick.AddListener(ToggleNotify);
        if (closeButton != null) closeButton.onClick.AddListener(CloseSettings);
        if (loginButton != null) loginButton.onClick.AddListener(OpenLogin);
        if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClicked);
    }

    internal void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (soundButton != null) soundButton.onClick.RemoveListener(ToggleSound);
        if (musicButton != null) musicButton.onClick.RemoveListener(ToggleMusic);
        if (hapticsButton != null) hapticsButton.onClick.RemoveListener(ToggleHaptics);
        if (notifyButton != null) notifyButton.onClick.RemoveListener(ToggleNotify);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseSettings);
        if (loginButton != null) loginButton.onClick.RemoveListener(OpenLogin);
        if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);
    }

    private void SubscribeAuth()
    {
        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        if (!authSubscribed && auth != null)
        {
            auth.OnStateChanged += RefreshAccountButtons;
            authSubscribed = true;
        }
    }

    private void UnsubscribeAuth()
    {
        if (!authSubscribed) return;
        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        if (auth != null)
            auth.OnStateChanged -= RefreshAccountButtons;
        authSubscribed = false;
    }

    public void Show()
    {
        BindButtons();
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
        else FinishHide();
    }

    private void FinishHide() => gameObject.SetActive(false);

    private void ToggleSound()
    {
        GameHaptics.Play(HapticEvent.UISelect);

        var sm = SoundManager.Instance;
        if (sm == null) return;

        if (sm.GetSoundVolume() > 0)
        {
            soundVolume = sm.GetSoundVolume();
            sm.SetSoundVolume(0);
        }
        else
        {
            sm.SetSoundVolume(soundVolume);
            UIAudio.Play(UISoundType.ButtonClick);
        }

        sm.SaveVolume();
        RefreshIcons();
    }

    private void ToggleMusic()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        var mm = MusicManager.Instance;
        if (mm == null) return;

        if (mm.GetMusicVolume() > 0)
        {
            musicVolume = mm.GetMusicVolume();
            mm.SetMusicVolume(0);
        }
        else
        {
            mm.SetMusicVolume(musicVolume);
        }

        mm.SaveVolume();
        RefreshIcons();
    }

    private void ToggleHaptics()
    {
        GameHaptics.Enabled = !GameHaptics.Enabled;
        if (GameHaptics.Enabled)
            GameHaptics.Play(HapticEvent.UISelect);

        UIAudio.Play(UISoundType.ButtonClick);
        RefreshIcons();
    }

    private void ToggleNotify()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        PlayerPrefs.SetInt(NotifyPrefKey, IsNotifyOn() ? 0 : 1);
        PlayerPrefs.Save();
        RefreshIcons();
    }

    private static bool IsNotifyOn() => PlayerPrefs.GetInt(NotifyPrefKey, 1) != 0;

    private void CloseSettings()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Setting);
    }

    private void OpenLogin()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Login);
    }

    private void HandleHapticsEnabledChanged(bool _)
    {
        RefreshIcons();
    }

    private void RefreshIcons()
    {
        var sm = SoundManager.Instance;
        var mm = MusicManager.Instance;

        if (soundIcon != null)
            soundIcon.SetSpriteNative((sm != null && sm.GetSoundVolume() > 0) ? soundOnSprite : soundOffSprite);

        if (musicIcon != null)
            musicIcon.SetSpriteNative((mm != null && mm.GetMusicVolume() > 0) ? musicOnSprite : musicOffSprite);

        if (hapticsIcon != null)
        {
            Sprite targetSprite = GameHaptics.Enabled ? hapticsOnSprite : hapticsOffSprite;
            if (targetSprite != null)
                hapticsIcon.SetSpriteNative(targetSprite);
        }

        if (notifyIcon != null)
            notifyIcon.SetSpriteNative(IsNotifyOn() ? notifyOnSprite : notifyOffSprite);
    }

    private void OnLogoutClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        auth?.SignOut();
        RefreshAccountButtons();
    }

    private void RefreshAccountButtons()
    {
        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        bool signedInWithRealAccount = auth != null && auth.State == AuthState.SignedIn && !auth.IsAnonymous;

        if (loginButton != null) loginButton.gameObject.SetActive(!signedInWithRealAccount);
        if (logoutButton != null) logoutButton.gameObject.SetActive(signedInWithRealAccount);
    }
}
