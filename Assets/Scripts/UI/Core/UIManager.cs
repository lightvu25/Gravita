using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [Serializable]
    public class UIPanelMapping
    {
        public UIPanelType panelType;
        public GameObject panelObject;
        public UILayer layer = UILayer.Popup;
        public bool freezeTime;
        [Tooltip("Check to hide the shared glitch backdrop when this panel opens (Screens always hide it).")]
        public bool disableGlitchBackdrop;
    }

    [SerializeField] private List<UIPanelMapping> panelMappings = new List<UIPanelMapping>();

    [Header("Shared Glitch Backdrop (Assign GlitchBackdrop Object Here)")]
    [Tooltip("Drag the shared full-screen GlitchBackdrop GameObject/RectTransform here.")]
    [SerializeField] private RectTransform glitchBackdrop;

    public RectTransform GlitchBackdrop => glitchBackdrop;

    private readonly Dictionary<UIPanelType, IUIPanel> panelsDict = new Dictionary<UIPanelType, IUIPanel>();
    private readonly Dictionary<UIPanelType, UIPanelMapping> mappingsDict = new Dictionary<UIPanelType, UIPanelMapping>();
    private readonly List<UIPanelType> popupStack = new List<UIPanelType>();

    private UIPanelType currentScreen = UIPanelType.None;

    /// <summary>Fired whenever the active <see cref="UILayer.Screen"/> changes.</summary>
    public event Action<UIPanelType> OnScreenChanged;

    /// <summary>Fired whenever the modal <see cref="UILayer.Popup"/> stack changes.</summary>
    public event Action OnPopupStackChanged;

    /// <summary>Currently active tab/content <see cref="UILayer.Screen"/>.</summary>
    public UIPanelType CurrentScreen => currentScreen;

    /// <summary>True when at least one modal <see cref="UILayer.Popup"/> is open.</summary>
    public bool IsAnyPopupOpen => popupStack.Count > 0;

    /// <summary>Topmost open <see cref="UILayer.Popup"/> if any, otherwise <see cref="CurrentScreen"/>.</summary>
    public UIPanelType CurrentActivePanel =>
        popupStack.Count > 0 ? popupStack[popupStack.Count - 1] : currentScreen;

    public bool IsPanelRegistered(UIPanelType type) => panelsDict.ContainsKey(type);

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RegisterPanels();
        if (glitchBackdrop != null)
            glitchBackdrop.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void RegisterPanels()
    {
        panelsDict.Clear();
        mappingsDict.Clear();

        foreach (var mapping in panelMappings)
        {
            if (mapping == null) continue;

            if (mapping.panelObject == null)
            {
                Debug.LogError($"[UIManager] Panel mapping '{mapping.panelType}' has no GameObject assigned — registration skipped.", this);
                continue;
            }

            if (!mapping.panelObject.scene.IsValid())
            {
                Debug.LogError($"[UIManager] Panel '{mapping.panelType}' → '{mapping.panelObject.name}' is not a scene object — registration skipped.", this);
                continue;
            }

            IUIPanel panel = mapping.panelObject.GetComponent<IUIPanel>();
            if (panel == null)
            {
                Debug.LogError($"[UIManager] Panel '{mapping.panelType}' → '{mapping.panelObject.name}' has no IUIPanel component — registration skipped.", this);
                continue;
            }

            if (panelsDict.ContainsKey(mapping.panelType))
            {
                Debug.LogError($"[UIManager] Duplicate mapping for '{mapping.panelType}' — '{mapping.panelObject.name}' overrides the earlier registration.", this);
            }

            // Enforce architectural rule: Main Menu tab screens and shells never use the glitch backdrop
            if (IsTabOrShellPanel(mapping.panelType))
            {
                mapping.layer = UILayer.Screen;
                mapping.disableGlitchBackdrop = true;
            }

            panelsDict[mapping.panelType] = panel;
            mappingsDict[mapping.panelType] = mapping;

            // Ensure non-shell overlays/popups that were accidentally left active in the scene start hidden
            if (mapping.panelType != UIPanelType.MainMenu
                && mapping.panelType != UIPanelType.Map
                && mapping.panelObject.activeSelf)
            {
                mapping.panelObject.SetActive(false);
            }
        }
    }

    /// <summary>
    /// Returns true if the panel type is one of the persistent Main Menu content tabs or a shell
    /// that swaps content without a modal glitch overlay.
    /// </summary>
    public static bool IsTabOrShellPanel(UIPanelType type)
    {
        switch (type)
        {
            case UIPanelType.MainMenu:
            case UIPanelType.GameplayHUD:
            case UIPanelType.Map:
            case UIPanelType.Runner:
            case UIPanelType.Powerups:
            case UIPanelType.Achievement:
                return true;
            default:
                return false;
        }
    }

    public void OpenPanel(UIPanelType type)
    {
        if (type == UIPanelType.None)
        {
            Debug.LogWarning("[UIManager] Cannot open None panel.", this);
            return;
        }

        if (!panelsDict.TryGetValue(type, out IUIPanel panel))
        {
            Debug.LogError($"[UIManager] Panel '{type}' is not registered.", this);
            return;
        }

        if (type == UIPanelType.MainMenu)
        {
            CloseAllPopups();
            panel.Show();
            UIAudio.NotifyPanelOpened(type);
            return;
        }

        if (type == UIPanelType.GameplayHUD)
        {
            CloseAllPopups();
            CloseActiveScreen();
            panel.Show();
            UIAudio.NotifyPanelOpened(type);
            return;
        }

        if (type == UIPanelType.Setting && popupStack.Contains(UIPanelType.Pause))
        {
            PushPopup(type);
            return;
        }

        UILayer effectiveLayer = GetEffectiveLayer(type);
        if (effectiveLayer == UILayer.Screen)
        {
            ShowScreen(type);
        }
        else
        {
            PushPopup(type);
        }
    }

    public void ShowScreen(UIPanelType type)
    {
        if (type == UIPanelType.None)
            return;

        if (!panelsDict.TryGetValue(type, out IUIPanel nextPanel))
        {
            Debug.LogError($"[UIManager] Screen '{type}' is not registered.", this);
            return;
        }

        CloseAllPopups();

        if (currentScreen == type)
        {
            if (mappingsDict.TryGetValue(type, out UIPanelMapping sameMapping)
                && sameMapping.panelObject != null
                && !sameMapping.panelObject.activeSelf)
            {
                nextPanel.Show();
            }
            UpdateGlitchBackdrop();
            OnScreenChanged?.Invoke(currentScreen);
            return;
        }

        if (currentScreen != UIPanelType.None)
        {
            UIPanelType prevScreen = currentScreen;
            currentScreen = UIPanelType.None;
            if (panelsDict.TryGetValue(prevScreen, out IUIPanel prevPanel))
            {
                prevPanel.Hide();
            }
            UIAudio.NotifyPanelClosed(prevScreen);
        }

        currentScreen = type;
        UpdateGlitchBackdrop();
        nextPanel.Show();
        UIAudio.NotifyPanelOpened(type);
        OnScreenChanged?.Invoke(currentScreen);
    }

    /// <summary>
    /// Closes the currently active <see cref="UILayer.Screen"/> without opening another Screen
    /// (for example when transitioning from Main Menu into Gameplay).
    /// </summary>
    public void CloseActiveScreen()
    {
        if (currentScreen == UIPanelType.None)
            return;

        UIPanelType closing = currentScreen;
        currentScreen = UIPanelType.None;

        if (panelsDict.TryGetValue(closing, out IUIPanel panel))
        {
            panel.Hide();
        }

        UIAudio.NotifyPanelClosed(closing);
    }

    /// <summary>
    /// Pushes a modal <see cref="UILayer.Popup"/> onto the popup stack without closing the underlying <see cref="CurrentScreen"/>.
    /// </summary>
    public void PushPopup(UIPanelType type)
    {
        if (type == UIPanelType.None)
            return;

        if (!panelsDict.TryGetValue(type, out IUIPanel panel))
        {
            Debug.LogError($"[UIManager] Popup '{type}' is not registered.", this);
            return;
        }

        if (popupStack.Count > 0 && popupStack[popupStack.Count - 1] == type)
        {
            UpdateGlitchBackdrop();
            return;
        }

        popupStack.Remove(type);
        popupStack.Add(type);

        if (mappingsDict.TryGetValue(type, out UIPanelMapping pushedMapping) && pushedMapping.panelObject != null)
        {
            pushedMapping.panelObject.transform.SetAsLastSibling();
        }

        UpdateGlitchBackdrop();
        panel.Show();
        UIAudio.NotifyPanelOpened(type);
        UpdateTimeScale();
        OnPopupStackChanged?.Invoke();
    }

    /// <summary>
    /// Pops and closes the topmost <see cref="UILayer.Popup"/> if one is open.
    /// </summary>
    public void CloseTopPopup()
    {
        if (popupStack.Count == 0)
        {
            UpdateGlitchBackdrop();
            return;
        }

        int topIndex = popupStack.Count - 1;
        UIPanelType closing = popupStack[topIndex];
        popupStack.RemoveAt(topIndex);

        if (panelsDict.TryGetValue(closing, out IUIPanel panel))
        {
            panel.Hide();
        }

        UIAudio.NotifyPanelClosed(closing);
        UpdateTimeScale();
        UpdateGlitchBackdrop();
        OnPopupStackChanged?.Invoke();
    }

    /// <summary>
    /// Closes all currently open <see cref="UILayer.Popup"/> panels in LIFO order.
    /// </summary>
    public void CloseAllPopups()
    {
        bool hadPopups = popupStack.Count > 0;
        while (popupStack.Count > 0)
        {
            int topIndex = popupStack.Count - 1;
            UIPanelType closing = popupStack[topIndex];
            popupStack.RemoveAt(topIndex);

            if (panelsDict.TryGetValue(closing, out IUIPanel panel))
            {
                panel.Hide();
            }

            UIAudio.NotifyPanelClosed(closing);
        }

        UpdateTimeScale();
        UpdateGlitchBackdrop();
        if (hadPopups)
            OnPopupStackChanged?.Invoke();
    }

    /// <summary>
    /// Closes the top Popup if any Popup is open; otherwise closes the active Screen.
    /// </summary>
    public void CloseCurrentPanel()
    {
        if (popupStack.Count > 0)
        {
            CloseTopPopup();
            return;
        }

        CloseActiveScreen();
        UpdateGlitchBackdrop();
    }

    /// <summary>
    /// Closes a specific panel if it is currently open as a Popup or as the active Screen.
    /// </summary>
    public void ClosePanelIfOpen(UIPanelType type)
    {
        int popupIdx = popupStack.LastIndexOf(type);
        if (popupIdx >= 0)
        {
            popupStack.RemoveAt(popupIdx);

            if (panelsDict.TryGetValue(type, out IUIPanel popupPanel))
            {
                popupPanel.Hide();
            }

            UIAudio.NotifyPanelClosed(type);
            UpdateTimeScale();
            UpdateGlitchBackdrop();
            OnPopupStackChanged?.Invoke();
            return;
        }

        if (currentScreen == type)
        {
            // If a non-Map Main Menu tab (e.g. Setting via its Close button) closes while in Main Menu,
            // return to the default Map tab so the Main Menu content area is not left blank.
            if (type != UIPanelType.Map && IsMainMenuVisible())
            {
                ShowScreen(UIPanelType.Map);
            }
            else
            {
                CloseActiveScreen();
            }
        }
    }

    public T GetPanel<T>(UIPanelType type) where T : class, IUIPanel
    {
        if (panelsDict.TryGetValue(type, out IUIPanel panel))
        {
            return panel as T;
        }

        return null;
    }

    public bool IsPanelOpen(UIPanelType type)
    {
        return currentScreen == type || popupStack.Contains(type);
    }

    public bool IsAnyPanelOpen()
    {
        return currentScreen != UIPanelType.None || popupStack.Count > 0;
    }

    private UILayer GetEffectiveLayer(UIPanelType type)
    {
        if (IsTabOrShellPanel(type))
            return UILayer.Screen;

        if (mappingsDict.TryGetValue(type, out UIPanelMapping mapping))
            return mapping.layer;

        return UILayer.Popup;
    }

    private bool IsMainMenuVisible()
    {
        return mappingsDict.TryGetValue(UIPanelType.MainMenu, out UIPanelMapping mm)
            && mm.panelObject != null
            && mm.panelObject.activeInHierarchy;
    }

    private void UpdateTimeScale()
    {
        bool shouldFreeze = false;
        for (int i = 0; i < popupStack.Count; i++)
        {
            if (GetFreezeTime(popupStack[i]))
            {
                shouldFreeze = true;
                break;
            }
        }

        Time.timeScale = shouldFreeze ? 0f : 1f;
    }

    private bool GetFreezeTime(UIPanelType type)
    {
        if (mappingsDict.TryGetValue(type, out UIPanelMapping mapping))
        {
            return mapping.freezeTime;
        }

        foreach (UIPanelMapping item in panelMappings)
        {
            if (item != null && item.panelType == type)
            {
                return item.freezeTime;
            }
        }

        return false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Shared Glitch Backdrop (Inspector Wired, Popup-Only)
    // ─────────────────────────────────────────────────────────────

    private bool ShouldShowGlitchBackdropForPopup(UIPanelType popupType)
    {
        if (popupType == UIPanelType.None || IsTabOrShellPanel(popupType))
            return false;

        return GetEffectiveLayer(popupType) == UILayer.Popup;
    }

    private void UpdateGlitchBackdrop()
    {
        if (popupStack.Count == 0)
        {
            HideGlitchBackdrop();
            return;
        }

        UIPanelType topPopup = popupStack[popupStack.Count - 1];
        if (!ShouldShowGlitchBackdropForPopup(topPopup))
        {
            HideGlitchBackdrop();
            return;
        }

        if (!mappingsDict.TryGetValue(topPopup, out UIPanelMapping mapping) || mapping.panelObject == null)
        {
            HideGlitchBackdrop();
            return;
        }

        if (glitchBackdrop == null)
            return;

        Transform panelTransform = mapping.panelObject.transform;
        Transform targetParent = panelTransform.parent != null ? panelTransform.parent : panelTransform;

        glitchBackdrop.SetParent(targetParent, false);
        StretchFullScreen(glitchBackdrop);

        int targetIndex = panelTransform.GetSiblingIndex();
        if (glitchBackdrop.GetSiblingIndex() < targetIndex)
        {
            glitchBackdrop.SetSiblingIndex(targetIndex - 1);
        }
        else
        {
            glitchBackdrop.SetSiblingIndex(targetIndex);
        }

        glitchBackdrop.gameObject.SetActive(true);
    }

    private void HideGlitchBackdrop()
    {
        if (glitchBackdrop != null && glitchBackdrop.gameObject.activeSelf)
        {
            glitchBackdrop.gameObject.SetActive(false);
        }
    }

    private static void StretchFullScreen(RectTransform rect)
    {
        if (rect == null) return;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        rect.localScale = Vector3.one;
    }
}
