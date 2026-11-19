using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a smooth, corner-aware LineRenderer trail behind the sliding player.
/// Presentation-only: reacts to <see cref="PlayerMovement"/> velocity tiers (S1 Kinetic Runway)
/// and Phase Launch state so speed and phase states are immediately readable on mobile.
/// Uses corner-only waypoints (2 vertices on straight slides) and continuous arc-length
/// tail retraction so textured speed-line trails shorten smoothly into the player direction
/// without UV swimming, vertex popping, or squash-and-stretch bounce jitter.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(LineRenderer))]
public sealed class PlayerLineTrail : MonoBehaviour
{
    [Header("References")]
    [Tooltip("LineRenderer used to draw the trail. Filled automatically when this component is added.")]
    [SerializeField] private LineRenderer lineRenderer;
    [Tooltip("Transform to follow. Leave empty to follow this GameObject.")]
    [SerializeField] private Transform followTarget;
    [Tooltip("Optional line material. When empty, a compatible unlit material is created at runtime.")]
    [SerializeField] private Material trailMaterial;

    [Header("Trail Shape")]
    [SerializeField, Min(0.01f)] private float trailWidth = 0.18f;
    [SerializeField, Min(0.01f)] private float pointSpacing = 0.08f;
    [SerializeField, Min(0.05f)] private float pointLifetime = 0.45f;
    [Tooltip("Maximum world-space length of the trail while sliding so it stays compact and continuously shortens behind the player.")]
    [SerializeField, Min(0.2f)] private float maxTrailLength = 2.25f;
    [Tooltip("How much faster the trail retracts into the player once movement stops.")]
    [SerializeField, Min(1f)] private float stopRetractSpeedMultiplier = 3.2f;
    [Tooltip("Width ratio at the player attachment point when using an untextured procedural trail.")]
    [SerializeField, Range(0.2f, 1f)] private float headWidthRatio = 0.65f;
    [SerializeField, Min(2)] private int maximumPoints = 48;
    [Tooltip("Large instantaneous movement clears the trail instead of drawing across the scene (ignored during Phase Launch).")]
    [SerializeField, Min(0.1f)] private float teleportResetDistance = 2f;
    [Tooltip("When true and a child SpriteRenderer exists, attaches the trail head to the steady player visual center (immune to impact squash bounce).")]
    [SerializeField] private bool centerOnPlayerSprite = true;
    [Tooltip("World-space offset from followTarget (used when centerOnPlayerSprite is false or no SpriteRenderer is found).")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, -0.45f, 0f);

    [Header("Trail Appearance (Tier 1 / Drift)")]
    [SerializeField] private Color tailColor = new Color(0.15f, 0.45f, 1f, 0f);
    [SerializeField] private Color headColor = new Color(0.35f, 0.95f, 1f, 0.9f);

    [Header("Kinetic Runway Readability (Tier 2 / Tier 3 / Phase)")]
    [SerializeField] private Color cruiseHeadColor = new Color(1f, 0.85f, 0.25f, 0.95f);
    [SerializeField] private Color kineticRamHeadColor = new Color(1f, 0.32f, 0.78f, 1f);
    [SerializeField] private Color phaseHeadColor = new Color(0.72f, 0.42f, 1f, 0.95f);
    [SerializeField, Min(1f)] private float cruiseWidthScale = 1.2f;
    [SerializeField, Min(1f)] private float kineticRamWidthScale = 1.45f;

    [SerializeField, Range(0, 12)] private int cornerVertices = 4;
    [SerializeField, Range(0, 12)] private int capVertices = 4;
    [Tooltip("Copies the player's SpriteRenderer sorting layer and renders one order behind it.")]
    [SerializeField] private bool matchPlayerSorting = true;

    // Corner-only waypoints from Tail (waypoints[0]) to the last turn before currentHeadPosition.
    // On a straight slide, waypoints contains only 1 element (the Tail), yielding a clean 2-vertex quad.
    private readonly List<Vector3> waypoints = new List<Vector3>(16);

    private PlayerMovement playerMovement;
    private SpriteRenderer cachedPlayerSprite;
    private Material runtimeMaterial;
    private Vector3 restingVisualLocalOffset;
    private bool hasRestingVisualOffset;
    private Vector3 currentHeadPosition;
    private Vector3 lastSegmentDirection;
    private float smoothedMoveSpeed = 10f;
    private bool hasInitialPosition;
    private bool wasMovingLastFrame;

    private void Reset()
    {
        lineRenderer = GetComponent<LineRenderer>();
        followTarget = transform;
        ApplyRendererSettings(false);
    }

    private void Awake()
    {
        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();
        if (followTarget == null)
            followTarget = transform;
        playerMovement = GetComponentInParent<PlayerMovement>();
        cachedPlayerSprite = GetComponentInChildren<SpriteRenderer>();
        CacheRestingVisualOffset();

        ApplyRendererSettings(true);
        ClearTrail();
    }

    private void OnEnable()
    {
        if (playerMovement == null)
            playerMovement = GetComponentInParent<PlayerMovement>();
        if (cachedPlayerSprite == null)
            cachedPlayerSprite = GetComponentInChildren<SpriteRenderer>();
        CacheRestingVisualOffset();

        if (playerMovement != null)
        {
            playerMovement.OnVelocityTierChanged += HandleTierChanged;
            playerMovement.OnPhasingChanged += HandlePhasingChanged;
            playerMovement.OnRedirected += HandleRedirected;
        }

        ClearTrail();
        RefreshTierVisuals();
    }

    private void OnDisable()
    {
        if (playerMovement != null)
        {
            playerMovement.OnVelocityTierChanged -= HandleTierChanged;
            playerMovement.OnPhasingChanged -= HandlePhasingChanged;
            playerMovement.OnRedirected -= HandleRedirected;
        }

        ClearTrail();
    }

    private void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    private void OnValidate()
    {
        trailWidth = Mathf.Max(0.01f, trailWidth);
        pointSpacing = Mathf.Max(0.01f, pointSpacing);
        pointLifetime = Mathf.Max(0.05f, pointLifetime);
        maxTrailLength = Mathf.Max(0.2f, maxTrailLength);
        stopRetractSpeedMultiplier = Mathf.Max(1f, stopRetractSpeedMultiplier);
        headWidthRatio = Mathf.Clamp(headWidthRatio, 0.2f, 1f);
        maximumPoints = Mathf.Max(2, maximumPoints);
        teleportResetDistance = Mathf.Max(0.1f, teleportResetDistance);

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        ApplyRendererSettings(false);
    }

    private void CacheRestingVisualOffset()
    {
        if (cachedPlayerSprite != null)
        {
            Transform root = playerMovement != null ? playerMovement.transform : transform;
            restingVisualLocalOffset = cachedPlayerSprite.bounds.center - root.position;
            restingVisualLocalOffset.z = 0f;
            hasRestingVisualOffset = true;
        }
    }

    private void HandleTierChanged(VelocityTier _)
    {
        RefreshTierVisuals();
    }

    private void HandlePhasingChanged(bool phasing)
    {
        if (phasing && hasInitialPosition)
            PushCornerIfNeeded(GetStableSamplePosition());

        RefreshTierVisuals();
    }

    private void HandleRedirected(Vector2 newDirection)
    {
        if (!hasInitialPosition)
            return;

        Vector3 cornerPos = GetStableSamplePosition();
        PushCornerIfNeeded(cornerPos);
        if (newDirection.sqrMagnitude > 0.001f)
            lastSegmentDirection = new Vector3(newDirection.x, newDirection.y, 0f).normalized;
    }

    private void RefreshTierVisuals()
    {
        if (lineRenderer == null) return;

        bool phasing = playerMovement != null && playerMovement.IsPhasing;
        VelocityTier tier = playerMovement != null ? playerMovement.CurrentTier : VelocityTier.None;

        Color activeHead = headColor;
        float widthScale = 1f;

        if (phasing)
        {
            activeHead = phaseHeadColor;
            widthScale = kineticRamWidthScale;
        }
        else if (tier == VelocityTier.KineticRam)
        {
            activeHead = kineticRamHeadColor;
            widthScale = kineticRamWidthScale;
        }
        else if (tier == VelocityTier.Cruise)
        {
            activeHead = cruiseHeadColor;
            widthScale = cruiseWidthScale;
        }

        bool hasTrailTexture = lineRenderer.sharedMaterial != null && lineRenderer.sharedMaterial.mainTexture != null;
        Color activeTail = hasTrailTexture
            ? new Color(activeHead.r, activeHead.g, activeHead.b, activeHead.a)
            : new Color(activeHead.r * 0.5f, activeHead.g * 0.5f, activeHead.b * 0.8f, 0f);

        lineRenderer.startColor = activeTail;
        lineRenderer.endColor = activeHead;
        lineRenderer.widthMultiplier = trailWidth * widthScale;
    }

    /// <summary>
    /// Samples the player's visual center without inheriting transient squash-and-stretch
    /// localPosition/scale oscillations from <see cref="PlayerSquashStretch"/> on wall impact.
    /// </summary>
    private Vector3 GetStableSamplePosition()
    {
        if (centerOnPlayerSprite && cachedPlayerSprite != null)
        {
            if (playerMovement != null && hasRestingVisualOffset)
            {
                Vector3 rootPos = playerMovement.transform.position;
                return new Vector3(
                    rootPos.x + restingVisualLocalOffset.x,
                    rootPos.y + restingVisualLocalOffset.y,
                    followTarget != null ? followTarget.position.z : transform.position.z);
            }

            Vector3 center = cachedPlayerSprite.bounds.center;
            center.z = followTarget != null ? followTarget.position.z : transform.position.z;
            return center;
        }

        return followTarget.position + positionOffset;
    }

    private void LateUpdate()
    {
        if (lineRenderer == null || followTarget == null) return;

        Vector3 sampledPosition = GetStableSamplePosition();

        if (!hasInitialPosition)
        {
            ResetAt(sampledPosition);
            return;
        }

        float dt = PlayerImpactFeedback.IsHitStopActive ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt <= 0f)
            return;

        Vector3 frameDelta = sampledPosition - currentHeadPosition;
        float frameDist = frameDelta.magnitude;

        bool isPhasing = playerMovement != null && playerMovement.IsPhasing;
        if (!isPhasing && frameDist >= teleportResetDistance)
        {
            ResetAt(sampledPosition);
            return;
        }

        bool isActivelyMoving = playerMovement != null
            ? playerMovement.IsMoving
            : frameDist > 0.0005f;

        if (isActivelyMoving)
        {
            if (!wasMovingLastFrame && waypoints.Count == 0)
            {
                waypoints.Add(currentHeadPosition);
            }

            if (frameDist > 0.0001f)
            {
                Vector3 stepDir = frameDelta / frameDist;

                // If direction changed significantly (e.g., 90-degree turn or reversal), lock the corner
                if (lastSegmentDirection.sqrMagnitude > 0.001f && Vector3.Dot(lastSegmentDirection, stepDir) < 0.98f)
                {
                    PushCornerIfNeeded(currentHeadPosition);
                }

                lastSegmentDirection = stepDir;
                currentHeadPosition = sampledPosition;

                float instSpeed = frameDist / dt;
                smoothedMoveSpeed = Mathf.Lerp(smoothedMoveSpeed, Mathf.Clamp(instSpeed, 4f, 56f), Mathf.Clamp01(dt * 12f));
            }
        }
        else if (wasMovingLastFrame)
        {
            // Snap once to the final resting position when stopping, then keep the head steady while retracting
            currentHeadPosition = sampledPosition;
        }

        wasMovingLastFrame = isActivelyMoving;

        UpdateTrailShortening(dt, isActivelyMoving);
        RenderTrail();
    }

    private void PushCornerIfNeeded(Vector3 cornerPos)
    {
        if (waypoints.Count == 0)
        {
            waypoints.Add(cornerPos);
            return;
        }

        Vector3 lastWp = waypoints[waypoints.Count - 1];
        if ((cornerPos - lastWp).sqrMagnitude <= 0.0004f)
            return;

        // Collapse colinear intermediate waypoint if the previous segment was along the same line
        if (waypoints.Count >= 2)
        {
            Vector3 prevWp = waypoints[waypoints.Count - 2];
            Vector3 dirA = (lastWp - prevWp).normalized;
            Vector3 dirB = (cornerPos - lastWp).normalized;
            if (Vector3.Dot(dirA, dirB) > 0.99f)
            {
                waypoints[waypoints.Count - 1] = cornerPos;
                return;
            }
        }

        if (waypoints.Count >= maximumPoints)
            waypoints.RemoveAt(0);

        waypoints.Add(cornerPos);
    }

    private void UpdateTrailShortening(float dt, bool isActivelyMoving)
    {
        if (waypoints.Count == 0)
            return;

        float totalLength = ComputeTotalPathLength();
        if (totalLength <= 0.001f)
        {
            if (!isActivelyMoving)
            {
                waypoints.Clear();
                lastSegmentDirection = Vector3.zero;
            }
            return;
        }

        // Dynamic target length based on lifetime and maxTrailLength cap
        float targetMaxLength = Mathf.Min(maxTrailLength, smoothedMoveSpeed * pointLifetime);

        if (isActivelyMoving)
        {
            // When total path length exceeds targetMaxLength, smoothly advance the tail in the movement direction
            float excess = totalLength - targetMaxLength;
            if (excess > 0f)
            {
                // Smoothly close any excess plus steady chase so the tail never jumps
                float chaseStep = excess + smoothedMoveSpeed * dt * 0.15f;
                AdvanceTailByDistance(Mathf.Min(excess, chaseStep));
            }
        }
        else
        {
            // Stopped: smoothly retract the tail along the recorded path into the player
            float baseRetractSpeed = Mathf.Max(6f, smoothedMoveSpeed) * stopRetractSpeedMultiplier;
            // Combine constant retract speed with soft proportional damping near zero for a smooth finish
            float retractSpeed = Mathf.Lerp(baseRetractSpeed * 0.65f, baseRetractSpeed, Mathf.Clamp01(totalLength / Mathf.Max(0.5f, targetMaxLength)));
            float retractStep = retractSpeed * dt;

            AdvanceTailByDistance(retractStep);

            if (ComputeTotalPathLength() <= 0.015f)
            {
                waypoints.Clear();
                lastSegmentDirection = Vector3.zero;
            }
        }
    }

    private float ComputeTotalPathLength()
    {
        if (waypoints.Count == 0)
            return 0f;

        float length = 0f;
        for (int i = 0; i < waypoints.Count - 1; i++)
        {
            length += Vector3.Distance(waypoints[i], waypoints[i + 1]);
        }
        length += Vector3.Distance(waypoints[waypoints.Count - 1], currentHeadPosition);
        return length;
    }

    private void AdvanceTailByDistance(float distanceToAdvance)
    {
        float remaining = distanceToAdvance;

        while (remaining > 0f && waypoints.Count > 0)
        {
            Vector3 segStart = waypoints[0];
            Vector3 segEnd = waypoints.Count > 1 ? waypoints[1] : currentHeadPosition;
            float segLen = Vector3.Distance(segStart, segEnd);

            if (segLen <= 0.0001f)
            {
                if (waypoints.Count > 1)
                {
                    waypoints.RemoveAt(0);
                    continue;
                }

                waypoints[0] = currentHeadPosition;
                break;
            }

            if (remaining >= segLen)
            {
                remaining -= segLen;
                if (waypoints.Count > 1)
                {
                    waypoints.RemoveAt(0);
                }
                else
                {
                    waypoints[0] = currentHeadPosition;
                    break;
                }
            }
            else
            {
                float t = remaining / segLen;
                waypoints[0] = Vector3.Lerp(segStart, segEnd, t);
                break;
            }
        }
    }

    private void RenderTrail()
    {
        if (waypoints.Count == 0)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        float totalLength = ComputeTotalPathLength();
        if (totalLength <= 0.01f)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        int renderCount = waypoints.Count + 1;
        lineRenderer.positionCount = renderCount;
        for (int i = 0; i < waypoints.Count; i++)
        {
            lineRenderer.SetPosition(i, waypoints[i]);
        }
        lineRenderer.SetPosition(renderCount - 1, currentHeadPosition);
    }

    private void ResetAt(Vector3 position)
    {
        hasInitialPosition = true;
        currentHeadPosition = position;
        lastSegmentDirection = Vector3.zero;
        wasMovingLastFrame = false;
        waypoints.Clear();
        waypoints.Add(position);

        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    private void ClearTrail()
    {
        waypoints.Clear();
        lastSegmentDirection = Vector3.zero;
        hasInitialPosition = false;
        wasMovingLastFrame = false;

        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    private void ApplyRendererSettings(bool allowRuntimeMaterial)
    {
        if (lineRenderer == null) return;

        if (trailMaterial != null)
        {
            lineRenderer.sharedMaterial = trailMaterial;
        }
        else if (allowRuntimeMaterial && lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (shader == null)
                shader = Shader.Find("Sprites/Default");

            if (shader != null)
            {
                runtimeMaterial = new Material(shader)
                {
                    name = "Mind Player Trail (Runtime)"
                };
                lineRenderer.sharedMaterial = runtimeMaterial;
            }
        }

        bool hasTrailTexture = lineRenderer.sharedMaterial != null && lineRenderer.sharedMaterial.mainTexture != null;

        lineRenderer.useWorldSpace = true;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.widthMultiplier = trailWidth;

        // When using a speed-line sprite texture (e.g. Line Trail.png), keep a flat 1.0 width curve
        // so the parallel streak teeth stay straight instead of pinching inward or wobbling.
        if (hasTrailTexture)
        {
            lineRenderer.widthCurve = AnimationCurve.Linear(0f, 1f, 1f, 1f);
            lineRenderer.startColor = new Color(headColor.r, headColor.g, headColor.b, headColor.a);
            lineRenderer.endColor = headColor;
        }
        else
        {
            lineRenderer.widthCurve = new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.25f, 0.55f),
                new Keyframe(0.65f, 0.82f),
                new Keyframe(1f, headWidthRatio));
            lineRenderer.startColor = tailColor;
            lineRenderer.endColor = headColor;
        }

        lineRenderer.numCornerVertices = cornerVertices;
        lineRenderer.numCapVertices = hasTrailTexture ? 0 : capVertices;
        lineRenderer.generateLightingData = false;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

        if (matchPlayerSorting)
        {
            SpriteRenderer playerSprite = cachedPlayerSprite != null
                ? cachedPlayerSprite
                : GetComponentInChildren<SpriteRenderer>();
            if (playerSprite != null)
            {
                lineRenderer.sortingLayerID = playerSprite.sortingLayerID;
                lineRenderer.sortingOrder = playerSprite.sortingOrder - 1;
            }
        }
    }
}
