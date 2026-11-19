using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour, IUIPanel
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Title")]
    [SerializeField] private TMP_Text levelTitleText;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitToMenuButton;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button musicButton;
    [SerializeField] private Button hapticsButton;

    [Header("Image")]
    [SerializeField] private Image soundIcon;
    [SerializeField] private Image musicIcon;
    [SerializeField] private Image hapticsIcon;

    [Header("Sprites")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;
    [SerializeField] private Sprite musicOnSprite;
    [SerializeField] private Sprite musicOffSprite;
    [SerializeField] private Sprite hapticsOnSprite;
    [SerializeField] private Sprite hapticsOffSprite;

    [Header("Sliders")]
    [SerializeField] private Slider soundSlider;
    [SerializeField] private Slider musicSlider;

    [Header("Volume")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;

    [Header("Optional Animation")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    public bool IsPaused => UIManager.Instance != null &&
                            UIManager.Instance.IsPanelOpen(UIPanelType.Pause);

    private const int VOLUME_MAX = 10;

    private bool buttonsBound;
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
        if (flowController == null)
            flowController = LevelFlowController.ResolveInstance();

        BindButtons();
    }

    private void OnEnable()
    {
        BindButtons();
        GameHaptics.OnEnabledChanged += HandleHapticsEnabledChanged;
        SubscribeAudioEvents();
        RefreshAudioIcon();
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
    }

    private void OnDestroy()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
        UnsubscribeAudioEvents();
        UnbindButtons();
    }

    internal void BindButtons()
    {
        EnsureAudioControls();
        if (buttonsBound)
            return;

        if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
        if (quitToMenuButton != null) quitToMenuButton.onClick.AddListener(OnQuitClicked);
        soundControl.Bind(OnSoundClicked);
        musicControl.Bind(OnMusicClicked);
        if (hapticsButton != null) hapticsButton.onClick.AddListener(OnHapticsClicked);
        buttonsBound = true;
    }

    internal void UnbindButtons()
    {
        if (!buttonsBound)
            return;

        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(OnResumeClicked);

        if (restartButton != null)
            restartButton.onClick.RemoveListener(OnRestartClicked);

        if (quitToMenuButton != null)
            quitToMenuButton.onClick.RemoveListener(OnQuitClicked);

        soundControl?.Unbind(OnSoundClicked);
        musicControl?.Unbind(OnMusicClicked);

        if (hapticsButton != null)
            hapticsButton.onClick.RemoveListener(OnHapticsClicked);

        buttonsBound = false;
    }

    private void SubscribeAudioEvents()
    {
        if (audioEventsSubscribed)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.OnSoundVolumeChanged += HandleExternalAudioChanged;
        if (MusicManager.Instance != null)
            MusicManager.Instance.OnMusicVolumeChanged += HandleExternalAudioChanged;

        audioEventsSubscribed = SoundManager.Instance != null || MusicManager.Instance != null;
    }

    private void UnsubscribeAudioEvents()
    {
        if (!audioEventsSubscribed)
            return;

        if (SoundManager.Instance != null)
            SoundManager.Instance.OnSoundVolumeChanged -= HandleExternalAudioChanged;
        if (MusicManager.Instance != null)
            MusicManager.Instance.OnMusicVolumeChanged -= HandleExternalAudioChanged;

        audioEventsSubscribed = false;
    }

    private void HandleExternalAudioChanged(object sender, EventArgs e)
    {
        RefreshAudioIcon();
    }

    public void Show()
    {
        if (flowController != null && flowController.CurrentLevelData != null && levelTitleText != null)
            levelTitleText.text = flowController.CurrentLevelData.DisplayName;

        RefreshAudioIcon();

        if (PanelAnimator != null)
            PanelAnimator.PlayIn();
        else
            gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
        else FinishHide();
    }

    private void FinishHide()
    {
        gameObject.SetActive(false);
    }

    private void OnResumeClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);
    }

    private void OnRestartClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);

        flowController?.RestartLevel();
    }

    private void OnQuitClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);

        flowController?.ReturnToMap();
    }

    private void OnSoundClicked()
    {
        EnsureAudioControls();
        soundControl.ToggleMute();
    }

    private void OnMusicClicked()
    {
        EnsureAudioControls();
        musicControl.ToggleMute();
    }

    private void OnHapticsClicked()
    {
        GameHaptics.Enabled = !GameHaptics.Enabled;
        if (GameHaptics.Enabled)
            GameHaptics.Play(HapticEvent.UISelect);

        UIAudio.Play(UISoundType.ButtonClick);
        RefreshAudioIcon();
    }

    private void HandleHapticsEnabledChanged(bool _)
    {
        RefreshAudioIcon();
    }

    private void RefreshAudioIcon()
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
    }
}
