using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// LoadingScene controller — asynchronously loads SceneLoader.TargetScene
/// and drives a progress bar / percent label.
///
/// All UI references are optional: with nothing assigned the component
/// still performs the async load. The bar fills against the real load
/// progress but never completes before <see cref="minDisplayTime"/>, so
/// fast loads don't flash the screen.
/// </summary>
public class LoadingScreen : MonoBehaviour
{
    [Header("UI References (all optional)")]
    [Tooltip("Fill area whose fillAmount is driven 0 → 1.")]
    [SerializeField] private Image progressFill;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private TMP_Text loadingLabel;
    [SerializeField] private GameObject tapToContinueGroup;

    [Header("Behaviour")]
    [Tooltip("Minimum seconds the loading screen stays visible.")]
    [SerializeField, Min(0f)] private float minDisplayTime = 1f;
    [Tooltip("How fast the fill eases toward the real progress.")]
    [SerializeField, Min(0.1f)] private float fillSpeed = 2f;
    [Tooltip("Show a tap prompt and wait for input once the scene is ready.")]
    [SerializeField] private bool requireTapToContinue;

    private AsyncOperation operation;
    private float shown;
    private bool ready;

    private void Start()
    {
        if (tapToContinueGroup != null) tapToContinueGroup.SetActive(false);
        if (loadingLabel != null) loadingLabel.text = "LOADING";
        ApplyProgress(0f);
        StartCoroutine(LoadRoutine());
    }

    private void Update()
    {
        if (!ready || operation == null) return;

        bool tapped =
            (Pointer.current != null && Pointer.current.press.wasPressedThisFrame) ||
            (Keyboard.current != null && Keyboard.current.anyKey.wasPressedThisFrame);

        if (tapped) Activate();
    }

    private IEnumerator LoadRoutine()
    {
        // One frame so the loading visuals render before the heavy work starts.
        yield return null;

        operation = SceneManager.LoadSceneAsync(SceneLoader.TargetScene);
        operation.allowSceneActivation = false;

        float elapsed = 0f;

        while (true)
        {
            elapsed += Time.unscaledDeltaTime;

            // Scene loads report 0 → 0.9 while loading; the last 10% is the
            // activation step gated by allowSceneActivation.
            float real = Mathf.Clamp01(operation.progress / 0.9f);
            float timed = Mathf.Clamp01(elapsed / Mathf.Max(0.01f, minDisplayTime));
            float target = Mathf.Min(real, timed);

            shown = Mathf.MoveTowards(shown, target, fillSpeed * Time.unscaledDeltaTime);
            ApplyProgress(shown);

            if (operation.progress >= 0.9f && elapsed >= minDisplayTime)
            {
                if (requireTapToContinue)
                {
                    ready = true;
                    if (tapToContinueGroup != null) tapToContinueGroup.SetActive(true);
                    yield break; // Update() waits for the tap
                }

                Activate();
                yield break;
            }

            yield return null;
        }
    }

    private void Activate()
    {
        if (operation != null) operation.allowSceneActivation = true;
    }

    private void ApplyProgress(float value)
    {
        if (progressFill != null) progressFill.fillAmount = value;
        if (progressText != null) progressText.text = $"{Mathf.RoundToInt(value * 100f)}%";
    }
}
