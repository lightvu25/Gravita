using System.Collections;
using UnityEngine;

/// <summary>
/// Dev-only auto-player for smoke-testing a level without manual input.
/// Attach to a GameObject in a test scene: when the level starts it queues
/// the configured route on the Player, then reports the outcome to the
/// Console. Remove or deactivate the component for normal play.
/// </summary>
public class AutoPlayTest : MonoBehaviour
{
    [Tooltip("Direction sequence to execute once the level starts.")]
    [SerializeField] private Vector2[] route =
    {
        Vector2.right, Vector2.up, Vector2.right, Vector2.down
    };

    [Tooltip("Minimum seconds to wait after a move before the next input.")]
    [SerializeField] private float stepDelay = 0.35f;

    private LevelFlowController flow;

    private void Start()
    {
        flow = FindAnyObjectByType<LevelFlowController>();
        if (flow == null)
        {
            Debug.LogError("[AutoPlayTest] No LevelFlowController in scene.");
            return;
        }

        flow.OnLevelStarted += HandleStarted;
        flow.OnLevelCompleted += HandleCompleted;
        flow.OnLevelFailed += HandleFailed;

        // Start() order between scripts is arbitrary — if the flow controller
        // already auto-started the level, the event fired before we subscribed.
        if (flow.CurrentState == LevelState.Playing)
            HandleStarted();
    }

    private void OnDestroy()
    {
        if (flow == null) return;
        flow.OnLevelStarted -= HandleStarted;
        flow.OnLevelCompleted -= HandleCompleted;
        flow.OnLevelFailed -= HandleFailed;
    }

    private void HandleStarted()
    {
        Debug.Log("[AutoPlayTest] Level started — running route.");
        StartCoroutine(RunRoute());
    }

    private void HandleCompleted()
    {
        Debug.Log("[AutoPlayTest] LEVEL COMPLETED ✓");
    }

    private void HandleFailed(string reason)
    {
        Debug.Log($"[AutoPlayTest] LEVEL FAILED: {reason}");
    }

    private IEnumerator RunRoute()
    {
        yield return new WaitForSeconds(0.25f);

        var player = PlayerMovement.Instance;
        if (player == null)
        {
            Debug.LogError("[AutoPlayTest] No PlayerMovement instance found.");
            yield break;
        }

        foreach (var dir in route)
        {
            player.QueueDirection(dir);
            yield return new WaitForSeconds(stepDelay);
            // Wait until the slide finishes (wall stop) before the next input.
            yield return new WaitUntil(() => !player.IsMoving);
            Debug.Log($"[AutoPlayTest] Slid {dir} → position {player.transform.position}");
        }

        Debug.Log("[AutoPlayTest] Route finished.");
    }
}
