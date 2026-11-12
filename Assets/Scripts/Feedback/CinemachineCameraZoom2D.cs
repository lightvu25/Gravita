using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Serialization;

public class CinemachineCameraZoom2D : MonoBehaviour
{
    public static CinemachineCameraZoom2D Instance { get; private set; }

    [FormerlySerializedAs("NORMAL_ORTHOGRAPHIC_SIZE")]
    [SerializeField] private float normalOrthographicSize;
    [SerializeField] private CinemachineCamera cinemachineCamera;
    [SerializeField] private float targetOrthographicSize;
    [SerializeField] private float zoomSpeed = 4f;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        if (cinemachineCamera == null)
            cinemachineCamera = GetComponent<CinemachineCamera>();
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Update()
    {
        if (cinemachineCamera == null)
            return;

        cinemachineCamera.Lens.OrthographicSize = Mathf.Lerp(
            cinemachineCamera.Lens.OrthographicSize,
            targetOrthographicSize,
            Time.deltaTime * zoomSpeed);
    }

    public void SetTargetOrthographicSize(float targetOrthographicSize)
    {
        this.targetOrthographicSize = targetOrthographicSize;
    }

    public void SetNormalOrthographicSize()
    {
        SetTargetOrthographicSize(normalOrthographicSize);
    }
}
