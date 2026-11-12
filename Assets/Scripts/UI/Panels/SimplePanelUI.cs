using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal IUIPanel for panels that need no dedicated script — Show/Hide
/// simply toggle the panel GameObject. Optionally binds a close button to
/// route dismissal cleanly through <see cref="UIManager"/>.
/// </summary>
public class SimplePanelUI : MonoBehaviour, IUIPanel
{
    [Tooltip("Optional override — defaults to this GameObject.")]
    [SerializeField] private GameObject target;

    [Header("Optional Close Routing")]
    [SerializeField] private UIPanelType panelType = UIPanelType.None;
    [SerializeField] private Button closeButton;

    private bool buttonBound;

    private void Awake() => BindCloseButton();

    private void OnEnable() => BindCloseButton();

    private void OnDestroy()
    {
        if (buttonBound && closeButton != null)
        {
            closeButton.onClick.RemoveListener(HandleCloseClicked);
            buttonBound = false;
        }
    }

    private void BindCloseButton()
    {
        if (buttonBound || closeButton == null)
            return;

        buttonBound = true;
        closeButton.onClick.AddListener(HandleCloseClicked);
    }

    private void HandleCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (panelType != UIPanelType.None && UIManager.Instance != null && UIManager.Instance.IsPanelOpen(panelType))
            UIManager.Instance.ClosePanelIfOpen(panelType);
        else
            Hide();
    }

    public void Show()
    {
        BindCloseButton();
        (target != null ? target : gameObject).SetActive(true);
    }

    public void Hide() => (target != null ? target : gameObject).SetActive(false);
}
