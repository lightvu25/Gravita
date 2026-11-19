using DG.Tweening;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

[DefaultExecutionOrder(-10)]
public class CinemachineCameraZoom2D : MonoBehaviour
{
    private const float FallbackOrthoSize = 5.5f;

    public static CinemachineCameraZoom2D Instance { get; private set; }

    [FormerlySerializedAs("NORMAL_ORTHOGRAPHIC_SIZE")]
    [SerializeField] private float normalOrthographicSize;
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private float targetOrthographicSize;
    [SerializeField] private float zoomSpeed = 9f;

    [Header("Pixel-Perfect Override Guard")]
    [SerializeField] private Behaviour cinemachinePixelPerfect;
    [SerializeField] private Behaviour urpPixelPerfectCamera;
    [SerializeField] private bool disablePixelPerfectOverrides = true;

    private float currentBaseSize;
    private float pulseOffset;
    private Sequence pulseSequence;

    public float NormalOrthographicSize => normalOrthographicSize > 0.5f ? normalOrthographicSize : FallbackOrthoSize;
    public float TargetOrthographicSize => targetOrthographicSize;
    public float CurrentPulseOffset => pulseOffset;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (cinemachineCamera == null)
            cinemachineCamera = GetComponent<CinemachineCamera>() ?? GetComponentInParent<CinemachineCamera>();

        if (disablePixelPerfectOverrides)
            ApplySmoothTrackingOverrides();

        if (normalOrthographicSize <= 0.5f)
        {
            normalOrthographicSize = (cinemachineCamera != null && cinemachineCamera.Lens.OrthographicSize > 0.5f)
                ? cinemachineCamera.Lens.OrthographicSize
                : FallbackOrthoSize;
        }

        currentBaseSize = normalOrthographicSize;
        if (targetOrthographicSize <= 0.5f)
            targetOrthographicSize = currentBaseSize;
    }

    private void OnDisable()
    {
        KillPulseSequence();
    }

    private void OnDestroy()
    {
        KillPulseSequence();
        if (Instance == this)
            Instance = null;
    }

    private void LateUpdate()
    {
        if (cinemachineCamera == null)
            return;

        float dt = PlayerImpactFeedback.IsHitStopActive ? Time.unscaledDeltaTime : Time.deltaTime;
        if (dt > 0f)
        {
            float effectiveSpeed = zoomSpeed > 0f ? zoomSpeed : 9f;
            float alpha = 1f - Mathf.Exp(-effectiveSpeed * dt);
            currentBaseSize = Mathf.Lerp(currentBaseSize, targetOrthographicSize, alpha);
        }

        cinemachineCamera.Lens.OrthographicSize = Mathf.Max(1f, currentBaseSize + pulseOffset);
    }

    /// <summary>
    /// Disables pixel-snapping components that quantize camera motion/zoom (including on parent CinemachineCamera)
    /// and aligns <see cref="CinemachineBrain"/> to <c>LateUpdate</c> so camera follows interpolated physics smoothly.
    /// </summary>
    public void ApplySmoothTrackingOverrides(Camera targetCamera = null)
    {
        if (cinemachineCamera == null)
            cinemachineCamera = GetComponent<CinemachineCamera>() ?? GetComponentInParent<CinemachineCamera>();

        if (cinemachinePixelPerfect == null)
        {
            cinemachinePixelPerfect = GetComponent("CinemachinePixelPerfect") as Behaviour;
            if (cinemachinePixelPerfect == null && cinemachineCamera != null)
                cinemachinePixelPerfect = cinemachineCamera.GetComponent("CinemachinePixelPerfect") as Behaviour;
            if (cinemachinePixelPerfect == null && transform.parent != null)
                cinemachinePixelPerfect = transform.parent.GetComponent("CinemachinePixelPerfect") as Behaviour;
        }

        Camera cam = targetCamera != null ? targetCamera : Camera.main;
        if (urpPixelPerfectCamera == null && cam != null)
            urpPixelPerfectCamera = cam.GetComponent("PixelPerfectCamera") as Behaviour;

        if (cinemachinePixelPerfect != null)
            cinemachinePixelPerfect.enabled = false;
        if (urpPixelPerfectCamera != null)
            urpPixelPerfectCamera.enabled = false;

        CinemachineBrain brain = cam != null ? cam.GetComponent<CinemachineBrain>() : null;
        if (brain == null)
            brain = FindAnyObjectByType<CinemachineBrain>();

        if (brain != null && brain.UpdateMethod != CinemachineBrain.UpdateMethods.LateUpdate)
            brain.UpdateMethod = CinemachineBrain.UpdateMethods.LateUpdate;
    }

    public void SetTargetOrthographicSize(float targetOrthographicSize)
    {
        this.targetOrthographicSize = Mathf.Max(1f, targetOrthographicSize);
    }

    public void SetNormalOrthographicSize()
    {
        SetTargetOrthographicSize(NormalOrthographicSize);
    }

    public void PulseZoom(float orthoDelta, float inDuration, float outDuration)
    {
        pulseSequence?.Kill();
        pulseSequence = DOTween.Sequence()
            .Append(DOTween.To(() => pulseOffset, x => pulseOffset = x, orthoDelta, Mathf.Max(0.01f, inDuration)).SetEase(Ease.OutQuad))
            .Append(DOTween.To(() => pulseOffset, x => pulseOffset = x, 0f, Mathf.Max(0.01f, outDuration)).SetEase(Ease.OutSine))
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnKill(() => pulseOffset = 0f);
    }

    private void KillPulseSequence()
    {
        if (pulseSequence != null && pulseSequence.IsActive())
            pulseSequence.Kill();
        pulseSequence = null;
        pulseOffset = 0f;
    }
}
