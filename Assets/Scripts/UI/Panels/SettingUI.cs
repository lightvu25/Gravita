using System;
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

    [Header("Sliders")]
    [SerializeField] private Slider soundSlider;
    [SerializeField] private Slider musicSlider;

    [Header("Volume")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;

    [Header("Optional Animation")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private const int VOLUME_MAX = 10;
    private const string NotifyPrefKey = "NotificationsEnabled";

    private bool buttonsBound;
    private bool authSubscribed;
    private bool audioEventsSubscribed;

    private AudioChannelControl soundControl;
    private AudioChannelControl musicControl;

    private UIPanelAnimator PanelAnimator => panelAnimator;

    private void EnsureAudioControls()
    {
        soundControl ??= new AudioChannelControl(
            VOLUME_MAX,
            () => SoundManager.Instance != null,
            () => SoundManager.Instance.GetSoundVolume(),
            v => SoundManager.Instance.SetSoundVolume(v),
            () => SoundManager.Instance.GetPreviousSoundVolume(),
            v => SoundManager.Instance.SetPreviousSoundVolume(v),
            () => SoundManager.Instance.SaveVolume(),
            () => soundVolume,
            v => soundVolume = v,
            () => UIAudio.Play(UISoundType.ButtonClick));

        musicControl ??= new AudioChannelControl(
            VOLUME_MAX,
            () => MusicManager.Instance != null,
            () => MusicManager.Instance.GetMusicVolume(),
            v => MusicManager.Instance.SetMusicVolume(v),
            () => MusicManager.Instance.GetPreviousMusicVolume(),
            v => MusicManager.Instance.SetPreviousMusicVolume(v),
            () => MusicManager.Instance.SaveVolume(),
            () => musicVolume,
            v => musicVolume = v,
            () => UIAudio.Play(UISoundType.ButtonClick));

        soundControl.ConfigureWidgets(soundButton, soundSlider, soundIcon, soundOnSprite, soundOffSprite);
        musicControl.ConfigureWidgets(musicButton, musicSlider, musicIcon, musicOnSprite, musicOffSprite);
    }

    private void Awake()
    {
        BindButtons();
    }

    private void OnEnable()
    {
        BindButtons();
        GameHaptics.OnEnabledChanged += HandleHapticsEnabledChanged;
        SubscribeAudioEvents();
        SubscribeAuth();
        RefreshIcons();
        RefreshAccountButtons();
    }

    private void Update()
    {
        soundControl?.TickPointerRelease();
        musicControl?.TickPointerRelease();
    }

    private void OnDisable()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
        UnsubscribeAudioEvents();
        UnsubscribeAuth();
    }

    private void OnDestroy()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
        UnsubscribeAudioEvents();
        UnsubscribeAuth();
        UnbindButtons();
    }

    internal void BindButtons()
    {
        EnsureAudioControls();
        if (buttonsBound) return;
        buttonsBound = true;

        soundControl.Bind(ToggleSound);
        musicControl.Bind(ToggleMusic);
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

        soundControl?.Unbind(ToggleSound);
        musicControl?.Unbind(ToggleMusic);
        if (hapticsButton != null) hapticsButton.onClick.RemoveListener(ToggleHaptics);
        if (notifyButton != null) notifyButton.onClick.RemoveListener(ToggleNotify);
        if (closeButton != null) closeButton.onClick.RemoveListener(CloseSettings);
        if (loginButton != null) loginButton.onClick.RemoveListener(OpenLogin);
        if (logoutButton != null) logoutButton.onClick.RemoveListener(OnLogoutClicked);
    }

    private void SubscribeAudioEvents()
    {
        if (audioEventsSubscribed) return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.OnSoundVolumeChanged += HandleExternalAudioChanged;
        if (MusicManager.Instance != null)
            MusicManager.Instance.OnMusicVolumeChanged += HandleExternalAudioChanged;

        audioEventsSubscribed = SoundManager.Instance != null || MusicManager.Instance != null;
    }

    private void UnsubscribeAudioEvents()
    {
        if (!audioEventsSubscribed) return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.OnSoundVolumeChanged -= HandleExternalAudioChanged;
        if (MusicManager.Instance != null)
            MusicManager.Instance.OnMusicVolumeChanged -= HandleExternalAudioChanged;

        audioEventsSubscribed = false;
    }

    private void HandleExternalAudioChanged(object sender, EventArgs e)
    {
        RefreshIcons();
    }

    private void SubscribeAuth()
    {
        var pg = PlayGamesService.Instance;
        if (!authSubscribed && pg != null)
        {
            pg.OnAuthenticationChanged += HandlePlayGamesAuthChanged;
            authSubscribed = true;
        }
    }

    private void UnsubscribeAuth()
    {
        if (!authSubscribed) return;
        var pg = PlayGamesService.Instance;
        if (pg != null)
            pg.OnAuthenticationChanged -= HandlePlayGamesAuthChanged;
        authSubscribed = false;
    }

    private void HandlePlayGamesAuthChanged(bool isAuthenticated)
    {
        RefreshAccountButtons();
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
        EnsureAudioControls();
        soundControl.ToggleMute();
    }

    private void ToggleMusic()
    {
        EnsureAudioControls();
        musicControl.ToggleMute();
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

        if (PlayGamesService.Instance != null && PlayGamesService.Instance.IsSupported)
        {
            _ = SignInPlayGamesAsync();
            return;
        }

        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Login);
    }

    private async System.Threading.Tasks.Task SignInPlayGamesAsync()
    {
        await PlayGamesService.Instance.SignInAsync(interactive: true);
        if (this != null)
            RefreshAccountButtons();
    }

    private void HandleHapticsEnabledChanged(bool _)
    {
        RefreshIcons();
    }

    private void RefreshIcons()
    {
        EnsureAudioControls();
        soundControl.SyncUI();
        musicControl.SyncUI();

        if (hapticsIcon != null)
        {
            Sprite targetSprite = GameHaptics.Enabled ? hapticsOnSprite : hapticsOffSprite;
            if (targetSprite != null)
                hapticsIcon.sprite = targetSprite;
        }

        if (notifyIcon != null)
            notifyIcon.sprite = IsNotifyOn() ? notifyOnSprite : notifyOffSprite;
    }

    private void OnLogoutClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        RefreshAccountButtons();
    }

    private void RefreshAccountButtons()
    {
        bool signedIn = PlayGamesService.Instance != null && PlayGamesService.Instance.IsAuthenticated;

        if (loginButton != null) loginButton.gameObject.SetActive(!signedIn);
        if (logoutButton != null) logoutButton.gameObject.SetActive(false);
    }
}
