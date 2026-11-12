using Unity.Cinemachine;
using UnityEngine;

/// <summary>
/// Self-contained camera shake helper. Owns a <see cref="CinemachineImpulseSource"/>
/// and exposes a simple ShakeCamera(force) API that gameplay code can call
/// (e.g. on pickup collection or hazard hits).
/// </summary>
[RequireComponent(typeof(CinemachineImpulseSource))]
public class CinemachineCameraShake2D : MonoBehaviour
{
    public static CinemachineCameraShake2D Instance { get; private set; }

    [Tooltip("Default shake intensity used by ShakeCamera().")]
    [SerializeField] private float defaultShakeForce = 0.2f;

    private CinemachineImpulseSource impulseSource;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        impulseSource = GetComponent<CinemachineImpulseSource>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    /// <summary>Shakes the camera with the configured default force.</summary>
    public void ShakeCamera() => ShakeCamera(defaultShakeForce);

    /// <summary>Shakes the camera with the given impulse force.</summary>
    public void ShakeCamera(float force)
    {
        impulseSource?.GenerateImpulse(force);
    }
}
