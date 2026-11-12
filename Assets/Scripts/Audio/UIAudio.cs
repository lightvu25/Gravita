using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI Audio trigger component (Layer 4 of the 4-layer audio architecture).
/// Attach to a UI Button, Tab, or Panel Prefab and configure the desired <see cref="UISoundType"/> in the Inspector.
/// Also provides static helper calls that route directly through <see cref="SoundManager"/>.
/// </summary>
public class UIAudio : MonoBehaviour
{
    [Header("Audio Config (ScriptableObject)")]
    [Tooltip("Optional override SoundConfig. If empty, uses SoundManager.Instance.Config.")]
    [SerializeField] private SoundConfig soundConfig;

    [Header("Button Click Trigger")]
    [Tooltip("Button to listen to. If left empty, attempts GetComponent<Button>() on this GameObject.")]
    [SerializeField] private Button targetButton;
    [SerializeField] private bool bindButtonOnAwake = true;
    [SerializeField] private UISoundType clickSoundType = UISoundType.ButtonClick;

    [Header("Panel Lifecycle Trigger")]
    [SerializeField] private bool playOnEnable;
    [SerializeField] private UISoundType openSoundType = UISoundType.PanelOpen;
    [SerializeField] private bool playOnDisable;
    [SerializeField] private UISoundType closeSoundType = UISoundType.PanelClose;
    [SerializeField] private bool duckMusicWhileEnabled;

    [Header("Optional Reward Service Subscriptions")]
    [SerializeField] private DailyRewardService dailyRewardService;
    [SerializeField] private CoinBonusService coinBonusService;

    private void Awake()
    {
        if (bindButtonOnAwake && targetButton == null)
        {
            targetButton = GetComponent<Button>();
        }
    }

    private void OnEnable()
    {
        if (targetButton != null)
        {
            targetButton.onClick.RemoveListener(HandleButtonClicked);
            targetButton.onClick.AddListener(HandleButtonClicked);
        }

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed += HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed += HandleCoinBonusClaimed;

        if (Time.timeSinceLevelLoad >= 0.1f)
        {
            if (playOnEnable && openSoundType != UISoundType.None)
            {
                PlaySound(openSoundType);
            }

            if (duckMusicWhileEnabled && MusicManager.Instance != null)
            {
                MusicManager.Instance.DuckMusic(true);
            }
        }
    }

    private void OnDisable()
    {
        if (targetButton != null)
        {
            targetButton.onClick.RemoveListener(HandleButtonClicked);
        }

        if (dailyRewardService != null)
            dailyRewardService.OnDailyRewardClaimed -= HandleDailyRewardClaimed;

        if (coinBonusService != null)
            coinBonusService.OnCoinBonusClaimed -= HandleCoinBonusClaimed;

        if (Time.timeSinceLevelLoad >= 0.1f)
        {
            if (playOnDisable && closeSoundType != UISoundType.None)
            {
                PlaySound(closeSoundType);
            }

            if (duckMusicWhileEnabled && MusicManager.Instance != null)
            {
                MusicManager.Instance.DuckMusic(false);
            }
        }
    }

    private void HandleButtonClicked()
    {
        PlaySound(clickSoundType);
    }

    private void HandleDailyRewardClaimed(int day, RewardBundle bundle)
    {
        PlaySound(UISoundType.RewardClaim);
    }

    private void HandleCoinBonusClaimed(int amount)
    {
        PlaySound(UISoundType.RewardClaim);
    }

    /// <summary>
    /// Instance method (can also be wired via UnityEvent in the Inspector).
    /// </summary>
    public void PlayConfiguredClickSound()
    {
        PlaySound(clickSoundType);
    }

    /// <summary>
    /// Plays the specified <see cref="UISoundType"/> using this component's <see cref="soundConfig"/> or <see cref="SoundManager.Config"/>.
    /// </summary>
    public void PlaySound(UISoundType soundType)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUISound(soundType, soundConfig);
        }
    }

    // ─────────────────────────────────────────────────────────────
    //  Static Semantic Helpers (Routes via SoundManager.Instance)
    // ─────────────────────────────────────────────────────────────

    public static void Play(UISoundType soundType)
    {
        if (SoundManager.Instance != null)
        {
            SoundManager.Instance.PlayUISound(soundType);
        }
    }

    public static void NotifyPanelOpened(UIPanelType type)
    {
        if (Time.timeSinceLevelLoad < 0.15f)
            return;

        if (type == UIPanelType.None
            || type == UIPanelType.MainMenu
            || type == UIPanelType.Map
            || type == UIPanelType.GameplayHUD
            || type == UIPanelType.Result)
        {
            return;
        }

        Play(UISoundType.PanelOpen);
    }

    public static void NotifyPanelClosed(UIPanelType type)
    {
        if (Time.timeSinceLevelLoad < 0.15f)
            return;

        if (type == UIPanelType.None
            || type == UIPanelType.MainMenu
            || type == UIPanelType.Map
            || type == UIPanelType.GameplayHUD)
        {
            return;
        }

        Play(UISoundType.PanelClose);
    }
}
