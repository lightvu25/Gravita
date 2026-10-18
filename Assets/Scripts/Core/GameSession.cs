using UnityEngine;

public class GameSession : MonoBehaviour
{
    public static GameSession Instance { get; private set; }

    [SerializeField] private string hubWorldSceneName = "GameScene";

    public ProfileData currentProfile;

    [HideInInspector] public LevelData pendingLevel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (Application.isPlaying)
        {
            DontDestroyOnLoad(gameObject);
        }

        Initialize();
    }
    
    private void Initialize()
    {
        currentProfile = SaveManager.loadProfile();
    }
}
