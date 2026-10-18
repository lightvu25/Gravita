using System;
using System.Threading.Tasks;
using UnityEngine;

/// <summary>
/// Dev-only end-to-end proof for the Firebase layer — auto-spawned at
/// startup alongside FirebaseService (independent of scene content).
///
/// Waits for FirebaseService to reach a terminal state, then uploads and
/// downloads a minimal probe payload through ICloudSaveProvider and reports
/// PASS/FAIL to the Console. Set AUTO_RUN to false to disable.
/// </summary>
public class FirebaseSmokeTest : MonoBehaviour
{
    /// <summary>Master switch — set to false to stop auto-running the test.</summary>
    public static bool AutoRun = true;

    private const float TimeoutSeconds = 45f;

    [SerializeField] private string slot = "smoke_test";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Spawn()
    {
        if (!AutoRun) return;
        var go = new GameObject(nameof(FirebaseSmokeTest));
        go.AddComponent<FirebaseSmokeTest>();
        DontDestroyOnLoad(go);
    }

    [Serializable]
    private class SmokePayload
    {
        public int schemaVersion;
        public string note;
        public long utcTicks;
    }

    private async void Start()
    {
        await RunAsync();
    }

    private async Task<bool> RunAsync()
    {
        var status = await WaitForTerminalStatus();
        if (status != FirebaseService.Status.Ready)
        {
            Debug.LogWarning($"[FirebaseSmokeTest] Firebase not ready ({status}) — cloud test skipped, game unaffected.");
            return false;
        }

        ICloudSaveProvider cloud = FirebaseService.Instance.CloudSave;
        string uid = FirebaseService.CurrentUserId;
        Debug.Log($"[FirebaseSmokeTest] Signed in anonymously — uid={uid}");

        var payload = new SmokePayload
        {
            schemaVersion = 0,
            note = "firebase smoke test",
            utcTicks = DateTime.UtcNow.Ticks
        };
        string json = JsonUtility.ToJson(payload);

        bool uploaded = await cloud.UploadAsync(slot, json);
        if (!uploaded)
        {
            Debug.LogError("[FirebaseSmokeTest] FAIL — upload returned false.");
            return false;
        }

        string downloaded = await cloud.DownloadAsync(slot);
        if (downloaded == json)
        {
            Debug.Log($"[FirebaseSmokeTest] PASS — uploaded and downloaded '{slot}' for uid={uid}.");
            return true;
        }

        Debug.LogError($"[FirebaseSmokeTest] FAIL — downloaded payload mismatch.\n sent: {json}\n got: {downloaded ?? "<null>"}");
        return false;
    }

    private static async Task<FirebaseService.Status> WaitForTerminalStatus()
    {
        float waited = 0f;
        while (waited < TimeoutSeconds)
        {
            var service = FirebaseService.Instance;
            if (service != null &&
                service.CurrentStatus != FirebaseService.Status.Uninitialized &&
                service.CurrentStatus != FirebaseService.Status.Initializing)
            {
                return service.CurrentStatus;
            }

            await Task.Delay(250);
            waited += 0.25f;
        }

        return FirebaseService.Status.Unavailable;
    }
}
