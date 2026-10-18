using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Scene bootstrapper. Resets Time.timeScale and re-enables player input
/// whenever a new scene finishes loading.
///
/// Pause/panel navigation belongs to UIManager; level lifecycle belongs to
/// LevelFlowController — this class intentionally does neither.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void OnEnable()
    {
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private void OnDisable()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        if (this != Instance) return;

        // Ensure time is unpaused before initializing a new scene —
        // prevents the player staying frozen after a paused transition.
        Time.timeScale = 1f;

        StartCoroutine(InitializeSceneRoutine());
    }

    private IEnumerator InitializeSceneRoutine()
    {
        yield return null;

        if (GameInput.Instance != null)
            GameInput.Instance.SetInputsEnabled(true);
    }
}
