using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class SettingUI : MonoBehaviour, IUIPanel
{
    [Header("Buttons")]
    [SerializeField] private Button soundButton;
    [SerializeField] private Button musicButton;
    [SerializeField] private Button closeButton;
    [SerializeField] private Button loginButton;
    [SerializeField] private Button notifyButton;
    [SerializeField] private Button logoutButton;

    [Header("Icons")]
    [SerializeField] private Image soundIcon;
    [SerializeField] private Image musicIcon;
    [SerializeField] private Image notifyIcon;

    [Header("Icon Sprites")]
    [SerializeField] private Sprite soundOnSprite;
    [SerializeField] private Sprite soundOffSprite;
    [SerializeField] private Sprite musicOnSprite;
    [SerializeField] private Sprite musicOffSprite;
    [SerializeField] private Sprite notifyOnSprite;
    [SerializeField] private Sprite notifyOffSprite;

    [Header("Sounds")]
    [SerializeField] private int soundVolume = 5;
    [SerializeField] private int musicVolume = 5;

    [Header("Level Display")]
    [SerializeField] private TMP_Text levelText;

    private const string NotifyPrefKey = "NotificationsEnabled";

    private UIPanelAnimator panelAnimator;
    private UIPanelAnimator PanelAnimator =>
        panelAnimator != null ? panelAnimator : (panelAnimator = GetComponent<UIPanelAnimator>());

    private void Awake()
    {
        if (soundButton != null) soundButton.onClick.AddListener(ToggleSound);
        if (musicButton != null) musicButton.onClick.AddListener(ToggleMusic);
        if (notifyButton != null) notifyButton.onClick.AddListener(ToggleNotify);
        if (closeButton != null) closeButton.onClick.AddListener(CloseSettings);
        if (loginButton != null) loginButton.onClick.AddListener(OpenLogin);
        if (logoutButton != null) logoutButton.onClick.AddListener(OnLogoutClicked);
    }

    private void OnEnable()
    {
        RefreshIcons();
        RefreshLevelText();
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

    private void ToggleSound()
    {
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
        }

        sm.SaveVolume();
        RefreshIcons();
    }

    private void ToggleMusic()
    {
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

    private void ToggleNotify()
    {
        PlayerPrefs.SetInt(NotifyPrefKey, IsNotifyOn() ? 0 : 1);
        PlayerPrefs.Save();
        RefreshIcons();
    }

    private static bool IsNotifyOn() => PlayerPrefs.GetInt(NotifyPrefKey, 1) != 0;

    private void CloseSettings()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Setting);
    }

    private void OpenLogin()
    {
        if (UIManager.Instance != null)
            UIManager.Instance.OpenPanel(UIPanelType.Login);
    }

    private void RefreshIcons()
    {
        var sm = SoundManager.Instance;
        var mm = MusicManager.Instance;

        if (soundIcon != null)
            soundIcon.sprite = (sm != null && sm.GetSoundVolume() > 0) ? soundOnSprite : soundOffSprite;

        if (musicIcon != null)
            musicIcon.sprite = (mm != null && mm.GetMusicVolume() > 0) ? musicOnSprite : musicOffSprite;

        if (notifyIcon != null)
            notifyIcon.sprite = IsNotifyOn() ? notifyOnSprite : notifyOffSprite;
    }

    private void RefreshLevelText()
    {
        var flow = FindAnyObjectByType<LevelFlowController>();
        var level = flow != null ? flow.CurrentLevelData : null;
        levelText.text = level != null ? $"LEVEL {level.DisplayName}" : "LEVEL ";
    }

    private void OnLogoutClicked()
    {
        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        auth?.SignOut();
        RefreshAccountButtons();
    }

    private void RefreshAccountButtons()
    {
        var auth = FirebaseService.Instance != null ? FirebaseService.Instance.Auth : null;
        bool signedIn = auth != null && auth.State == AuthState.SignedIn;

        if (loginButton != null) loginButton.gameObject.SetActive(!signedIn);
        if (logoutButton != null) logoutButton.gameObject.SetActive(signedIn);
    }
}
