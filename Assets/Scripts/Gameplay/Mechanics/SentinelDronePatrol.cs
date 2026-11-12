using System.Collections.Generic;
using UnityEngine;

public enum DronePatrolMode
{
    PingPong = 0,
    Loop     = 1,
}

/// <summary>
/// Sentinel Drone Patrol: deterministic grid-aligned security unit driven by <see cref="LevelClock"/>.
///
/// Rules:
///   - Position is a pure function of local patrol time (pauses cleanly under EMP Pulse / Freeze,
///     and resets to t = 0 on level restart).
///   - Supports either grid cell offsets from start cell OR explicit Transform waypoints.
///   - Validates route segments against the Wall layer and draws route gizmos in Scene View.
/// </summary>
[RequireComponent(typeof(Hazard))]
public class SentinelDronePatrol : MonoBehaviour, ILevelClockListener
{
    [Header("Route Configuration")]
    [SerializeField] private DronePatrolMode patrolMode = DronePatrolMode.PingPong;

    [Tooltip("Optional explicit Transform waypoints. If empty, Waypoint Cell Offsets are used relative to initial position.")]
    [SerializeField] private Transform[] waypointTransforms;

    [Tooltip("Grid cell offsets relative to initial placement when Waypoint Transforms is empty.")]
    [SerializeField] private Vector2Int[] waypointCellOffsets =
    {
        Vector2Int.zero,
        new Vector2Int(3, 0),
    };

    [Header("Timing")]
    [SerializeField, Min(0.1f)] private float cellsPerSecond = 3f;
    [SerializeField, Min(0f)] private float waitPerWaypoint = 0.25f;
    [SerializeField, Min(0f)] private float startDelay = 0f;
    [SerializeField, Min(0.1f)] private float cellSize = 1f;

    public DronePatrolMode PatrolMode => patrolMode;
    public float LocalPatrolTime => localPatrolTime;

    private readonly List<Vector2> resolvedWaypoints = new List<Vector2>(8);
    private Hazard hazard;
    private Rigidbody2D rb;
    private Vector2 initialWorldOrigin;
    private bool originCaptured;
    private float localPatrolTime;

    private void Awake()
    {
        hazard = GetComponent<Hazard>();
        rb = GetComponent<Rigidbody2D>();
        if (rb != null)
        {
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.freezeRotation = true;
        }

        CaptureOriginAndBuildWaypoints();

        int wallMask = LayerMask.GetMask("Wall");
        if (wallMask != 0 && HasBlockedSegment(resolvedWaypoints, patrolMode, wallMask, out int seg))
        {
            Debug.LogWarning($"[SentinelDronePatrol] '{name}': Patrol segment #{seg} intersects a Wall collider.", this);
        }
    }

    private void CaptureOriginAndBuildWaypoints()
    {
        if (!originCaptured)
        {
            initialWorldOrigin = transform.position;
            originCaptured = true;
        }

        BuildWaypoints(initialWorldOrigin, resolvedWaypoints);
    }

    public void BuildWaypoints(Vector2 origin, List<Vector2> output)
    {
        output.Clear();

        if (waypointTransforms != null && waypointTransforms.Length > 0)
        {
            for (int i = 0; i < waypointTransforms.Length; i++)
            {
                if (waypointTransforms[i] != null)
                    output.Add(waypointTransforms[i].position);
            }
        }

        if (output.Count == 0 && waypointCellOffsets != null && waypointCellOffsets.Length > 0)
        {
            for (int i = 0; i < waypointCellOffsets.Length; i++)
            {
                output.Add(origin + (Vector2)waypointCellOffsets[i] * cellSize);
            }
        }

        if (output.Count == 0)
            output.Add(origin);
    }

    /// <summary>
    /// Pure deterministic evaluator mapping <paramref name="patrolTime"/> to an exact world-space position
    /// along <paramref name="waypoints"/>.
    /// </summary>
    public static Vector2 EvaluatePatrolPosition(
        IReadOnlyList<Vector2> waypoints,
        DronePatrolMode mode,
        float speedUnitsPerSecond,
        float waitSeconds,
        float delaySeconds,
        float patrolTime)
    {
        if (waypoints == null || waypoints.Count == 0)
            return Vector2.zero;

        if (waypoints.Count == 1 || patrolTime <= delaySeconds)
            return waypoints[0];

        float safeSpeed = Mathf.Max(0.01f, speedUnitsPerSecond);
        float safeWait = Mathf.Max(0f, waitSeconds);
        float activeTime = patrolTime - Mathf.Max(0f, delaySeconds);

        int count = waypoints.Count;
        int segmentCount = mode == DronePatrolMode.Loop ? count : (count - 1) * 2;
        if (segmentCount <= 0)
            return waypoints[0];

        float totalCycleDuration = 0f;
        for (int s = 0; s < segmentCount; s++)
        {
            GetSegmentIndices(s, count, mode, out int fromIdx, out int toIdx);
            float dist = Vector2.Distance(waypoints[fromIdx], waypoints[toIdx]);
            totalCycleDuration += safeWait + (dist / safeSpeed);
        }

        if (totalCycleDuration <= 0.0001f)
            return waypoints[0];

        float t = Mathf.Repeat(activeTime, totalCycleDuration);

        for (int s = 0; s < segmentCount; s++)
        {
            GetSegmentIndices(s, count, mode, out int fromIdx, out int toIdx);
            Vector2 a = waypoints[fromIdx];
            Vector2 b = waypoints[toIdx];

            if (t < safeWait)
                return a;
            t -= safeWait;

            float moveDuration = Vector2.Distance(a, b) / safeSpeed;
            if (t <= moveDuration || s == segmentCount - 1)
            {
                float alpha = moveDuration > 0.0001f ? Mathf.Clamp01(t / moveDuration) : 1f;
                return Vector2.Lerp(a, b, alpha);
            }
            t -= moveDuration;
        }

        return waypoints[0];
    }

    private static void GetSegmentIndices(int segmentIndex, int waypointCount, DronePatrolMode mode, out int fromIdx, out int toIdx)
    {
        if (mode == DronePatrolMode.Loop)
        {
            fromIdx = segmentIndex % waypointCount;
            toIdx = (segmentIndex + 1) % waypointCount;
            return;
        }

        int forwardCount = waypointCount - 1;
        if (segmentIndex < forwardCount)
        {
            fromIdx = segmentIndex;
            toIdx = segmentIndex + 1;
        }
        else
        {
            int backOffset = segmentIndex - forwardCount;
            fromIdx = waypointCount - 1 - backOffset;
            toIdx = fromIdx - 1;
        }
    }

    public static bool HasBlockedSegment(IReadOnlyList<Vector2> waypoints, DronePatrolMode mode, int wallLayerMask, out int blockedSegment)
    {
        blockedSegment = -1;
        if (waypoints == null || waypoints.Count < 2 || wallLayerMask == 0)
            return false;

        int segs = mode == DronePatrolMode.Loop ? waypoints.Count : waypoints.Count - 1;
        for (int i = 0; i < segs; i++)
        {
            Vector2 a = waypoints[i];
            Vector2 b = waypoints[(i + 1) % waypoints.Count];
            RaycastHit2D hit = Physics2D.Linecast(a, b, wallLayerMask);
            if (hit.collider != null && !hit.collider.isTrigger)
            {
                blockedSegment = i;
                return true;
            }
        }
        return false;
    }

    public void OnLevelClockTick(float levelTime, float deltaTime)
    {
        if (hazard != null && (hazard.IsFrozen || hazard.IsBroken))
            return;

        CaptureOriginAndBuildWaypoints();
        localPatrolTime += deltaTime;

        float speed = cellsPerSecond * cellSize;
        Vector2 targetPos = EvaluatePatrolPosition(resolvedWaypoints, patrolMode, speed, waitPerWaypoint, startDelay, localPatrolTime);

        if (rb != null)
            rb.MovePosition(targetPos);
        else
            transform.position = new Vector3(targetPos.x, targetPos.y, transform.position.z);

        hazard?.CheckOverlappingPlayer();
    }

    public void OnLevelClockReset()
    {
        localPatrolTime = 0f;
        CaptureOriginAndBuildWaypoints();
        Vector2 startPos = resolvedWaypoints[0];
        if (rb != null)
            rb.position = startPos;
        transform.position = new Vector3(startPos.x, startPos.y, transform.position.z);
    }

#if UNITY_EDITOR
    private void OnDrawGizmos()
    {
        Vector2 origin = Application.isPlaying && originCaptured ? initialWorldOrigin : (Vector2)transform.position;
        var preview = new List<Vector2>(8);
        BuildWaypoints(origin, preview);
        if (preview.Count == 0) return;

        Gizmos.color = new Color(1f, 0.28f, 0.35f, 0.85f);
        for (int i = 0; i < preview.Count; i++)
        {
            Gizmos.DrawWireSphere(preview[i], 0.16f);
            if (i + 1 < preview.Count)
                Gizmos.DrawLine(preview[i], preview[i + 1]);
            else if (patrolMode == DronePatrolMode.Loop && preview.Count > 2)
                Gizmos.DrawLine(preview[i], preview[0]);
        }
    }
#endif
}

