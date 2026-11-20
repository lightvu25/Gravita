using System;
using System.Threading.Tasks;
using Firebase;
using UnityEngine;

/// <summary>
/// Analytics-only Firebase entry point. Checks Firebase native dependencies,
/// activates <see cref="FirebaseAnalyticsProvider"/>, and mirrors GPGS PlayerId when signed in.
/// Account authentication and cloud saves are owned directly by <see cref="PlayGamesService"/>.
/// </summary>
public class FirebaseService : MonoBehaviour
{
    public enum Status
    {
        Uninitialized,
        Initializing,
        Ready,
        AuthFailed,
        Unavailable
    }

    public static FirebaseService Instance { get; private set; }

    public static string CurrentUserId => Instance != null ? Instance.UserId : null;

    public Status CurrentStatus { get; private set; } = Status.Uninitialized;
    public bool IsReady => CurrentStatus == Status.Ready;

    public IAuthService Auth => null;

    public ICloudSaveProvider CloudSave =>
        PlayGamesService.Instance != null ? PlayGamesService.Instance.CloudSave : null;

    public string UserId =>
        PlayGamesService.Instance != null && PlayGamesService.Instance.IsAuthenticated
            ? PlayGamesService.Instance.PlayerId
            : null;

    public event Action<Status> OnStatusChanged;

    private FirebaseApp app;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDestroy()
    {
        if (ReferenceEquals(Instance, this))
            Instance = null;
    }

    private void Start()
    {
        _ = InitializeAsync();
    }

    private async Task InitializeAsync()
    {
        SetStatus(Status.Initializing);

        try
        {
            DependencyStatus deps = await FirebaseApp.CheckAndFixDependenciesAsync();
            if (deps != DependencyStatus.Available)
            {
                Debug.LogWarning($"[FirebaseService] Dependencies unavailable ({deps}). Analytics fallback remains offline.");
                SetStatus(Status.Unavailable);
                return;
            }

            app = FirebaseApp.DefaultInstance;
            if (app == null)
            {
                Debug.LogWarning("[FirebaseService] FirebaseApp.DefaultInstance returned null. Analytics fallback remains offline.");
                SetStatus(Status.Unavailable);
                return;
            }

            if (Application.isPlaying)
                AnalyticsService.ConnectFirebaseAnalytics(UserId);

            SetStatus(Status.Ready);
            Debug.Log("[FirebaseService] Analytics ready.");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[FirebaseService] Analytics initialization failed: {e.Message}");
            SetStatus(Status.Unavailable);
        }
    }

    private void SetStatus(Status status)
    {
        if (CurrentStatus == status) return;
        CurrentStatus = status;
        OnStatusChanged?.Invoke(status);
    }
}
