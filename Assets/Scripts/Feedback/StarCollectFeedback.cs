using UnityEngine;

/// <summary>
/// Small camera "pop" when the player picks up a StarCollectible.
///
/// Listens to the static StarCollectible.OnAnyStarCollected event so it
/// needs no reference to the currently loaded LevelRoot — attach to the
/// Player (or any always-alive object) once and every star pickup shakes.
/// </summary>
public class StarCollectFeedback : MonoBehaviour
{
    [Header("Camera Shake")]
    [Tooltip("Impulse force for a star pickup — smaller than a wall hit.")]
    [SerializeField, Min(0f)] private float starShake = 0.06f;

    private void OnEnable()
    {
        StarCollectible.OnAnyStarCollected += HandleStarCollected;
    }

    private void OnDisable()
    {
        StarCollectible.OnAnyStarCollected -= HandleStarCollected;
    }

    private void HandleStarCollected(StarCollectible star)
    {
        if (starShake > 0f && CinemachineCameraShake2D.Instance != null)
            CinemachineCameraShake2D.Instance.ShakeCamera(starShake);
    }
}
