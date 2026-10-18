using UnityEngine;
using System.Collections.Generic;

public class UIManager : MonoBehaviour
{
    public static UIManager Instance { get; private set; }

    [System.Serializable]
    public class UIPanelMapping
    {
        public UIPanelType panelType;
        public GameObject panelObject;
        public bool freezeTime;
    }

    [SerializeField] private List<UIPanelMapping> panelMappings;

    private readonly Dictionary<UIPanelType, IUIPanel> panelsDict = new();
    private UIPanelType currentActivePanel = UIPanelType.None;
    public UIPanelType CurrentActivePanel => currentActivePanel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;

        RegisterPanels();
    }

    private void RegisterPanels()
    {
        panelsDict.Clear();

        foreach (var mapping in panelMappings)
        {
            if (mapping.panelObject == null)
            {
                Debug.LogError($"[UIManager] Panel mapping '{mapping.panelType}' has no GameObject assigned — registration skipped.");
                continue;
            }

            if (!mapping.panelObject.scene.IsValid())
            {
                Debug.LogError($"[UIManager] Panel '{mapping.panelType}' → '{mapping.panelObject.name}' is not a scene object — registration skipped.");
                continue;
            }

            IUIPanel panel = mapping.panelObject.GetComponent<IUIPanel>();
            if (panel == null)
            {
                Debug.LogError($"[UIManager] Panel '{mapping.panelType}' → '{mapping.panelObject.name}' has no IUIPanel component — registration skipped.");
                continue;
            }

            if (panelsDict.ContainsKey(mapping.panelType))
            {
                Debug.LogError($"[UIManager] Duplicate mapping for '{mapping.panelType}' — '{mapping.panelObject.name}' overrides the earlier registration.");
            }

            panelsDict[mapping.panelType] = panel;
        }
    }

    public void OpenPanel(UIPanelType type)
    {
        if (type == UIPanelType.None)
        {
            Debug.LogWarning("[UIManager] Cannot open None panel.");
            return;
        }

        if (!panelsDict.TryGetValue(type, out IUIPanel panel))
        {
            Debug.LogError($"[UIManager] Panel '{type}' is not registered.");
            return;
        }

        if (currentActivePanel == type)
        {
            return;
        }

        CloseCurrentPanel();

        currentActivePanel = type;
        panel.Show();

        if (GetFreezeTime(type))
        {
            Time.timeScale = 0f;
        }
    }

    public void CloseCurrentPanel()
    {
        if (currentActivePanel == UIPanelType.None)
        {
            return;
        }

        UIPanelType closing = currentActivePanel;
        currentActivePanel = UIPanelType.None;
        Time.timeScale = 1f;

        if (panelsDict.TryGetValue(closing, out IUIPanel panel))
        {
            panel.Hide();
        }
    }

    public void ClosePanelIfOpen(UIPanelType type)
    {
        if (currentActivePanel == type)
        {
            CloseCurrentPanel();
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
        return currentActivePanel == type;
    }

    public bool IsAnyPanelOpen()
    {
        return currentActivePanel != UIPanelType.None;
    }

    private bool GetFreezeTime(UIPanelType type)
    {
        foreach (UIPanelMapping mapping in panelMappings)
        {
            if (mapping.panelType == type)
            {
                return mapping.freezeTime;
            }
        }

        return false;
    }
}
