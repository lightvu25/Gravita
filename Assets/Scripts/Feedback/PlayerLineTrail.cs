using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Draws a short, fading LineRenderer path behind the sliding player.
/// This component is presentation-only and observes Transform movement.
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
    [SerializeField, Min(0.01f)] private float pointSpacing = 0.12f;
    [SerializeField, Min(0.05f)] private float pointLifetime = 0.7f;
    [SerializeField, Min(2)] private int maximumPoints = 48;
    [Tooltip("Large instantaneous movement clears the trail instead of drawing across the scene.")]
    [SerializeField, Min(0.1f)] private float teleportResetDistance = 2f;
    [Tooltip("World-space offset from the followed Transform. The default places the trail near the player's feet.")]
    [SerializeField] private Vector3 positionOffset = new Vector3(0f, -0.45f, 0f);

    [Header("Trail Appearance")]
    [SerializeField] private Color tailColor = new Color(0.15f, 0.45f, 1f, 0f);
    [SerializeField] private Color headColor = new Color(0.35f, 0.95f, 1f, 0.9f);
    [SerializeField, Range(0, 12)] private int cornerVertices = 4;
    [SerializeField, Range(0, 12)] private int capVertices = 4;
    [Tooltip("Copies the player's SpriteRenderer sorting layer and renders one order behind it.")]
    [SerializeField] private bool matchPlayerSorting = true;

    private readonly List<Vector3> recordedPositions = new List<Vector3>(48);
    private readonly List<float> recordedTimes = new List<float>(48);

    private Material runtimeMaterial;
    private Vector3 lastRecordedPosition;
    private bool hasInitialPosition;

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

        ApplyRendererSettings(true);
        ClearTrail();
    }

    private void OnEnable()
    {
        ClearTrail();
    }

    private void OnDisable()
    {
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
        maximumPoints = Mathf.Max(2, maximumPoints);
        teleportResetDistance = Mathf.Max(0.1f, teleportResetDistance);

        if (lineRenderer == null)
            lineRenderer = GetComponent<LineRenderer>();

        ApplyRendererSettings(false);
    }

    private void LateUpdate()
    {
        if (lineRenderer == null || followTarget == null) return;

        Vector3 currentPosition = followTarget.position + positionOffset;
        float currentTime = Time.unscaledTime;

        if (!hasInitialPosition)
        {
            hasInitialPosition = true;
            lastRecordedPosition = currentPosition;
            AddPoint(currentPosition, currentTime);
            lineRenderer.positionCount = 0;
            return;
        }

        float movedDistance = Vector3.Distance(lastRecordedPosition, currentPosition);
        if (movedDistance >= teleportResetDistance)
        {
            ResetAt(currentPosition, currentTime);
            return;
        }

        while (movedDistance >= pointSpacing)
        {
            lastRecordedPosition = Vector3.MoveTowards(lastRecordedPosition, currentPosition, pointSpacing);
            AddPoint(lastRecordedPosition, currentTime);
            movedDistance = Vector3.Distance(lastRecordedPosition, currentPosition);
        }

        RemoveExpiredPoints(currentTime);
        RenderTrail(currentPosition);
    }

    private void AddPoint(Vector3 position, float time)
    {
        if (recordedPositions.Count >= maximumPoints)
        {
            recordedPositions.RemoveAt(0);
            recordedTimes.RemoveAt(0);
        }

        recordedPositions.Add(position);
        recordedTimes.Add(time);
    }

    private void RemoveExpiredPoints(float currentTime)
    {
        float oldestAllowedTime = currentTime - pointLifetime;
        int expiredCount = 0;

        while (expiredCount < recordedTimes.Count && recordedTimes[expiredCount] < oldestAllowedTime)
            expiredCount++;

        if (expiredCount <= 0) return;

        recordedPositions.RemoveRange(0, expiredCount);
        recordedTimes.RemoveRange(0, expiredCount);
    }

    private void RenderTrail(Vector3 currentPosition)
    {
        int recordedCount = recordedPositions.Count;
        bool needsCurrentHead = recordedCount == 0
            || (recordedPositions[recordedCount - 1] - currentPosition).sqrMagnitude > 0.000001f;
        int renderCount = recordedCount + (needsCurrentHead ? 1 : 0);

        if (renderCount < 2)
        {
            lineRenderer.positionCount = 0;
            return;
        }

        lineRenderer.positionCount = renderCount;
        for (int i = 0; i < recordedCount; i++)
            lineRenderer.SetPosition(i, recordedPositions[i]);

        if (needsCurrentHead)
            lineRenderer.SetPosition(renderCount - 1, currentPosition);
    }

    private void ResetAt(Vector3 currentPosition, float currentTime)
    {
        recordedPositions.Clear();
        recordedTimes.Clear();
        lastRecordedPosition = currentPosition;
        AddPoint(currentPosition, currentTime);
        lineRenderer.positionCount = 0;
    }

    private void ClearTrail()
    {
        recordedPositions.Clear();
        recordedTimes.Clear();
        hasInitialPosition = false;

        if (lineRenderer != null)
            lineRenderer.positionCount = 0;
    }

    private void ApplyRendererSettings(bool allowRuntimeMaterial)
    {
        if (lineRenderer == null) return;

        lineRenderer.useWorldSpace = true;
        lineRenderer.alignment = LineAlignment.View;
        lineRenderer.textureMode = LineTextureMode.Stretch;
        lineRenderer.widthMultiplier = trailWidth;
        lineRenderer.widthCurve = new AnimationCurve(
            new Keyframe(0f, 0f),
            new Keyframe(0.2f, 0.55f),
            new Keyframe(1f, 1f));
        lineRenderer.startColor = tailColor;
        lineRenderer.endColor = headColor;
        lineRenderer.numCornerVertices = cornerVertices;
        lineRenderer.numCapVertices = capVertices;
        lineRenderer.generateLightingData = false;
        lineRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lineRenderer.receiveShadows = false;

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

        if (matchPlayerSorting)
        {
            SpriteRenderer playerSprite = GetComponentInChildren<SpriteRenderer>();
            if (playerSprite != null)
            {
                lineRenderer.sortingLayerID = playerSprite.sortingLayerID;
                lineRenderer.sortingOrder = playerSprite.sortingOrder - 1;
            }
        }
    }
}
