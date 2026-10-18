using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PauseMenuUI : MonoBehaviour, IUIPanel
{
    [Header("System References")]
    [SerializeField] private LevelFlowController flowController;

    [Header("Panel")]
    [SerializeField] private GameObject pausePanel;

    [Header("Title")]
    [SerializeField] private TMP_Text levelTitleText;

    [Header("Buttons")]
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button restartButton;
    [SerializeField] private Button quitToMenuButton;
    [SerializeField] private Button soundButton;
    [SerializeField] private Button musicButton;

    [Header("Image")]
    [SerializeField] private Image soundIcon;
    [SerializeField] private Image musicIcon;

    [Header("Sprites")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;
    [SerializeField] private Sprite musicOnSprite;
    [SerializeField] private Sprite musicOffSprite;

    [Header("Volume")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;
    public bool IsPaused => UIManager.Instance != null &&
                            UIManager.Instance.IsPanelOpen(UIPanelType.Pause);

    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator
            : (panelAnimator = (pausePanel != null ? pausePanel : gameObject).GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        flowController = FindAnyObjectByType<LevelFlowController>();
        resumeButton.onClick.AddListener(OnResumeClicked);
        restartButton.onClick.AddListener(OnRestartClicked);
        quitToMenuButton.onClick.AddListener(OnQuitClicked);
        soundButton.onClick.AddListener(OnSoundClicked);
        musicButton.onClick.AddListener(OnMusicClicked);
    }

    private void OnDestroy()
    {
        if (resumeButton != null)
            resumeButton.onClick.RemoveListener(OnResumeClicked);

        if (restartButton != null)
            restartButton.onClick.RemoveListener(OnRestartClicked);

        if (quitToMenuButton != null)
            quitToMenuButton.onClick.RemoveListener(OnQuitClicked);
    }

    public void Show()
    {
        if (flowController != null && flowController.CurrentLevelData != null && levelTitleText != null)
            levelTitleText.text = flowController.CurrentLevelData.DisplayName;

        RefreshAudioIcon();

        if (PanelAnimator != null)
            PanelAnimator.PlayIn();
        else if (pausePanel != null)
            pausePanel.SetActive(true);
    }

    public void Hide()
    {
        if (PanelAnimator != null) PanelAnimator.PlayOut(FinishHide);
        else FinishHide();
    }

    private void FinishHide()
    {
        if (pausePanel != null)
            pausePanel.SetActive(false);
    }

    private void OnResumeClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);
    }

    private void OnRestartClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);

        flowController?.RestartLevel();
    }

    private void OnQuitClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Pause);

        flowController?.ReturnToMap();
    }

    private void OnSettingsClicked()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Setting);
    }

    private void OnSoundClicked()
    {
        var sm = SoundManager.Instance;
        if (sm.GetSoundVolume() > 0)
        {
            soundVolume = sm.GetSoundVolume();
            sm.SetSoundVolume(0);
        }

        else
        {
            sm.SetSoundVolume(soundVolume);
        }

        sm.SaveVolume();
        RefreshAudioIcon();
    }

    private void OnMusicClicked()
    {
        var mm = MusicManager.Instance;
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

    private void RefreshAudioIcon()
    {
        var sm = SoundManager.Instance;
        var mm = MusicManager.Instance;

        if (soundIcon != null)
            soundIcon.sprite = (sm != null && sm.GetSoundVolume() > 0) ? soundOnSprite : soundOffSprite;
        if (musicIcon != null)
            musicIcon.sprite = (mm != null && mm.GetMusicVolume() > 0) ? musicOnSprite : musicOffSprite;
    }
}
