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

    [Header("Volume")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;

    public bool IsPaused => UIManager.Instance != null &&
                            UIManager.Instance.IsPanelOpen(UIPanelType.Pause);

    private UIPanelAnimator panelAnimator;
    private bool buttonsBound;

    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (flowController == null)
            flowController = FindAnyObjectByType<LevelFlowController>();

        BindButtons();
    }

    private void OnEnable()
    {
        GameHaptics.OnEnabledChanged += HandleHapticsEnabledChanged;
        RefreshAudioIcon();
    }

    private void OnDisable()
    {
        GameHaptics.OnEnabledChanged -= HandleHapticsEnabledChanged;
    }

    private void OnDestroy()
    {
        UnbindButtons();
    }

    internal void BindButtons()
    {
        if (buttonsBound)
            return;

        if (resumeButton != null) resumeButton.onClick.AddListener(OnResumeClicked);
        if (restartButton != null) restartButton.onClick.AddListener(OnRestartClicked);
        if (quitToMenuButton != null) quitToMenuButton.onClick.AddListener(OnQuitClicked);
        if (soundButton != null) soundButton.onClick.AddListener(OnSoundClicked);
        if (musicButton != null) musicButton.onClick.AddListener(OnMusicClicked);
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

        if (soundButton != null)
            soundButton.onClick.RemoveListener(OnSoundClicked);

        if (musicButton != null)
            musicButton.onClick.RemoveListener(OnMusicClicked);

        if (hapticsButton != null)
            hapticsButton.onClick.RemoveListener(OnHapticsClicked);

        buttonsBound = false;
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
        RefreshAudioIcon();
    }

    private void OnMusicClicked()
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
        RefreshAudioIcon();
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
    }
}
