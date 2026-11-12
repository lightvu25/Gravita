using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class BootstrapUI : MonoBehaviour
{
    [Header("Scene")]
    [SerializeField] private string nextSceneName = "GameScene";
    [SerializeField] private bool useLoadingScene = true;

    [Header("Input")]
    [SerializeField] private Button tapButton;
    [SerializeField, Min(0f)] private float inputDelay = 0.5f;

    [Header("Transition")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private bool continuing;
    private bool buttonBound;
    private float openedAt;

    private void Awake()
    {
        openedAt = Time.unscaledTime;
        if (tapButton != null && !buttonBound)
        {
            tapButton.onClick.AddListener(Continue);
            buttonBound = true;
        }
    }

    private void OnDestroy()
    {
        if (buttonBound && tapButton != null)
        {
            tapButton.onClick.RemoveListener(Continue);
            buttonBound = false;
        }
    }

    private void Update()
    {
        if (continuing) return;
        if (Time.unscaledTime - openedAt < inputDelay) return;

        bool tapped =
            (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) ||
            (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (tapped) Continue();
    }

    public void Continue()
    {
        if (continuing) return;
        continuing = true;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (panelAnimator != null)
            panelAnimator.PlayOut(LoadNextScene);
        else
            LoadNextScene();
    }

    private void LoadNextScene()
    {
        if (useLoadingScene)
            SceneLoader.Load(nextSceneName);
        else
            SceneManager.LoadScene(nextSceneName);
    }
}
