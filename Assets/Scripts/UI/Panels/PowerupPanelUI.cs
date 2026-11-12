using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PowerupPanelUI : MonoBehaviour, IUIPanel
{
    private static readonly string[] DefaultCampaignSkillIds =
    {
        PowerUpIds.Breaker,
        PowerUpIds.Freeze,
        PowerUpIds.Teleport
    };

    public enum PowerupTab { Equip, Up }

    [Header("Skill Data")]
    [SerializeField] private PowerUpData[] powerUpAssets;

    [Header("Tabs (Equip / Up)")]
    [SerializeField] private Button equipTabButton;
    [SerializeField] private Button upTabButton;
    [SerializeField] private GameObject equipRoot;        // EquipUI
    [SerializeField] private GameObject upRoot;           // UpUI
    [SerializeField, Min(1f)] private float selectedTabScale = 1.2f;
    [SerializeField, Min(0.01f)] private float tabScaleTweenDuration = 0.18f;

    [Header("Equip Tab")]
    [SerializeField] private Transform rowContainer;      // non-scrolling
    [SerializeField] private PowerupRowWidget rowPrefab;
    [SerializeField] private PowerupLoadoutSlotWidget[] slotWidgets;   // 4 loadout slots
    [SerializeField] private RectTransform dragLayer;     // top-most RectTransform of the panel

    [Header("Up Tab")]
    [SerializeField] private Transform upgradeContainer;
    [SerializeField] private PowerupUpgradeCardWidget upgradeCardPrefab;

    [Header("Optional Animation")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private readonly List<PowerupRowWidget> spawnedRows = new List<PowerupRowWidget>();
    private readonly List<PowerupUpgradeCardWidget> spawnedUpgradeCards = new List<PowerupUpgradeCardWidget>();

    private PowerupTab activeTab = PowerupTab.Equip;
    public PowerupTab ActiveTab => activeTab;

    private UIPanelAnimator PanelAnimator => panelAnimator;
    private ProgressionManager PM => ProgressionManager.Instance;
    private bool IsLocked() => PM == null || PM.IsLoadoutLocked;

    private PowerupDragGhost ghost;
    private PowerupDragGhost Ghost => ghost ?? (ghost = new PowerupDragGhost(dragLayer != null ? dragLayer : (RectTransform)transform));

    private string dragSkillId;
    private int dragFromSlot = -1;
    private bool dragDropHandled;

    public void Show()
    {
        SelectTab(PowerupTab.Equip, playSound: false);
        if (PanelAnimator != null) PanelAnimator.PlayIn();
        else gameObject.SetActive(true);
    }

    public void Hide()
    {
        if (ghost != null) ghost.Hide();
        if (PanelAnimator != null) PanelAnimator.PlayOut(() => gameObject.SetActive(false));
        else gameObject.SetActive(false);
    }

    private void Awake()
    {
        if (rowPrefab != null && rowPrefab.gameObject.scene.IsValid())
            rowPrefab.gameObject.SetActive(false);

        if (upgradeCardPrefab != null && upgradeCardPrefab.gameObject.scene.IsValid())
            upgradeCardPrefab.gameObject.SetActive(false);

        BindTabs();
        BindSlots();
    }

    private void OnEnable()
    {
        BindTabs();
        BindSlots();
        SelectTab(PowerupTab.Equip, playSound: false);

        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged += RefreshAll;
            ProgressionManager.Instance.OnLoadoutChanged += RefreshAll;
            ProgressionManager.Instance.OnPowerUpUpgraded += HandlePowerUpUpgraded;
        }
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged += HandleCoinsChanged;
        }
    }

    private void OnDisable()
    {
        if (ghost != null) ghost.Hide();
        if (ProgressionManager.Instance != null)
        {
            ProgressionManager.Instance.OnProgressionChanged -= RefreshAll;
            ProgressionManager.Instance.OnLoadoutChanged -= RefreshAll;
            ProgressionManager.Instance.OnPowerUpUpgraded -= HandlePowerUpUpgraded;
        }
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged -= HandleCoinsChanged;
        }
    }

    private void OnDestroy()
    {
        if (equipTabButton != null) equipTabButton.onClick.RemoveListener(OnEquipTabClicked);
        if (upTabButton != null) upTabButton.onClick.RemoveListener(OnUpTabClicked);
        if (ghost != null) { ghost.Destroy(); ghost = null; }
    }

    private void HandlePowerUpUpgraded(string skillId, int newLevel) => RefreshAll();
    private void HandleCoinsChanged(int newBalance, int delta) => RefreshAll();

    private void BindTabs()
    {
        if (equipTabButton != null)
        {
            equipTabButton.onClick.RemoveListener(OnEquipTabClicked);
            equipTabButton.onClick.AddListener(OnEquipTabClicked);
        }
        if (upTabButton != null)
        {
            upTabButton.onClick.RemoveListener(OnUpTabClicked);
            upTabButton.onClick.AddListener(OnUpTabClicked);
        }
    }

    private void OnEquipTabClicked() => SelectTab(PowerupTab.Equip);
    private void OnUpTabClicked() => SelectTab(PowerupTab.Up);

    public void SelectTab(PowerupTab tab, bool playSound = true)
    {
        activeTab = tab;
        if (playSound)
            UIAudio.Play(UISoundType.TabSwitch);

        if (equipRoot != null) equipRoot.SetActive(tab == PowerupTab.Equip);
        if (upRoot != null) upRoot.SetActive(tab == PowerupTab.Up);

        AnimateTabButton(equipTabButton, tab == PowerupTab.Equip, instant: !playSound);
        AnimateTabButton(upTabButton, tab == PowerupTab.Up, instant: !playSound);

        RefreshAll();
    }

    private void AnimateTabButton(Button btn, bool selected, bool instant)
    {
        if (btn == null) return;
        Transform tr = btn.transform;
        tr.DOKill();
        Vector3 target = selected ? Vector3.one * selectedTabScale : Vector3.one;
        if (instant || !Application.isPlaying)
        {
            tr.localScale = target;
        }
        else
        {
            tr.DOScale(target, tabScaleTweenDuration)
              .SetEase(Ease.OutQuad)
              .SetUpdate(true)
              .SetLink(btn.gameObject);
        }
    }

    private void BindSlots()
    {
        if (slotWidgets == null) return;
        for (int i = 0; i < slotWidgets.Length; i++)
        {
            PowerupLoadoutSlotWidget widget = slotWidgets[i];
            if (widget == null) continue;
            int idx = i;
            widget.Bind(
                idx,
                IsLocked,
                slotIndex => TryUnequipSlot(slotIndex),
                HandleSlotBeginDrag,
                HandleDrag,
                (_, e) => HandleEndDrag(e),
                HandleDrop);
        }
    }

    public void RefreshList() => RefreshAll();

    public void RefreshAll()
    {
        RefreshSlots();
        if (activeTab == PowerupTab.Equip)
            RebuildRows();
        else
            RebuildUpgradeCards();
    }

    private void RefreshSlots()
    {
        if (slotWidgets == null) return;
        IReadOnlyList<string> slots = PM != null ? PM.GetLoadoutSlots() : Array.Empty<string>();
        for (int i = 0; i < slotWidgets.Length; i++)
        {
            PowerupLoadoutSlotWidget widget = slotWidgets[i];
            if (widget == null) continue;
            string skillId = i < slots.Count ? slots[i] : string.Empty;
            Sprite icon = null;
            if (!string.IsNullOrEmpty(skillId))
                ResolveSkillMetadata(skillId, out _, out icon);
            widget.Refresh(skillId, icon);
        }
    }

    private void RebuildRows()
    {
        if (rowContainer == null || rowPrefab == null)
            return;

        if (rowPrefab.gameObject.scene.IsValid())
            rowPrefab.gameObject.SetActive(false);

        for (int i = spawnedRows.Count - 1; i >= 0; i--)
        {
            PowerupRowWidget row = spawnedRows[i];
            if (row != null && row != rowPrefab)
                Destroy(row.gameObject);
        }
        spawnedRows.Clear();

        List<string> activeSkillIds = CollectActiveCampaignSkillIds();
        ProgressionManager pm = PM;
        bool locked = IsLocked();

        for (int i = 0; i < activeSkillIds.Count; i++)
        {
            string skillId = activeSkillIds[i];
            if (string.IsNullOrEmpty(skillId))
                continue;

            ResolveSkillMetadata(skillId, out string _, out Sprite icon);

            int count = pm != null ? pm.GetPowerUpCount(skillId) : 0;
            int cap = pm != null
                ? pm.GetPowerUpCap(skillId)
                : EconomyBalanceConfig.Active.GetSkillInventoryCap(skillId);
            bool equipped = pm != null && pm.IsSkillEquippedInLoadout(skillId);

            PowerupRowWidget row = Instantiate(rowPrefab, rowContainer);
            row.gameObject.SetActive(true);

            string capturedId = skillId;
            row.Setup(
                capturedId,
                icon,
                count,
                cap,
                equipped,
                locked,
                () => TryEquipToFirstEmptySlot(capturedId),
                IsLocked,
                HandleRowBeginDrag,
                HandleDrag,
                (_, e) => HandleEndDrag(e));

            spawnedRows.Add(row);
        }
    }

    private void RebuildUpgradeCards()
    {
        if (upgradeContainer == null || upgradeCardPrefab == null)
            return;

        if (upgradeCardPrefab.gameObject.scene.IsValid())
            upgradeCardPrefab.gameObject.SetActive(false);

        for (int i = spawnedUpgradeCards.Count - 1; i >= 0; i--)
        {
            PowerupUpgradeCardWidget card = spawnedUpgradeCards[i];
            if (card != null && card != upgradeCardPrefab)
                Destroy(card.gameObject);
        }
        spawnedUpgradeCards.Clear();

        List<string> activeSkillIds = CollectActiveCampaignSkillIds();
        ProgressionManager pm = PM;
        int coins = EconomyManager.Instance != null ? EconomyManager.Instance.Coins : 0;

        for (int i = 0; i < activeSkillIds.Count; i++)
        {
            string skillId = activeSkillIds[i];
            if (string.IsNullOrEmpty(skillId))
                continue;

            PowerUpData data = FindPowerUpData(skillId);
            ResolveSkillMetadata(skillId, out string displayName, out Sprite icon);
            int level = pm != null ? pm.GetPowerUpLevel(skillId) : 1;

            PowerupUpgradeCardWidget card = Instantiate(upgradeCardPrefab, upgradeContainer);
            card.gameObject.SetActive(true);

            string capturedId = skillId;
            card.Setup(data, icon, displayName, level, coins, () => OnUpgradeClicked(capturedId, data));
            spawnedUpgradeCards.Add(card);
        }
    }

    private void OnUpgradeClicked(string id, PowerUpData data)
    {
        if (PM != null && PM.TryUpgradePowerUp(id, data, out string _))
            UIAudio.Play(UISoundType.PurchaseSuccess);
        else
            UIAudio.Play(UISoundType.ErrorOrLocked);

        RefreshAll();
    }

    // ─────────────────────────────────────────────────────────────
    //  Public Equip Logic (Tap, Drag, Swap, Unequip)
    // ─────────────────────────────────────────────────────────────

    public bool TryEquipToFirstEmptySlot(string id)
    {
        if (IsLocked() || string.IsNullOrEmpty(id))
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
            return false;
        }
        IReadOnlyList<string> slots = PM != null ? PM.GetLoadoutSlots() : Array.Empty<string>();
        for (int i = 0; i < slots.Count; i++)
        {
            if (string.Equals(slots[i], id, StringComparison.OrdinalIgnoreCase))
                return TryUnequipSlot(i);
        }
        for (int i = 0; i < ProgressionManager.MaxLoadoutSlotCount; i++)
        {
            if (i >= slots.Count || string.IsNullOrEmpty(slots[i]))
                return Assign(i, id);
        }
        UIAudio.Play(UISoundType.ErrorOrLocked);
        return false;
    }

    public bool TryEquipToSlot(string id, int slot) => !IsLocked() && Assign(slot, id);
    public bool TryUnequipSlot(int slot) => !IsLocked() && Assign(slot, string.Empty);

    public bool TrySwapSlots(int a, int b)
    {
        if (IsLocked() || a == b || a < 0 || b < 0 ||
            a >= ProgressionManager.MaxLoadoutSlotCount || b >= ProgressionManager.MaxLoadoutSlotCount)
            return false;

        IReadOnlyList<string> s = PM != null ? PM.GetLoadoutSlots() : Array.Empty<string>();
        string sa = a < s.Count ? s[a] : string.Empty;
        string sb = b < s.Count ? s[b] : string.Empty;
        var next = new List<string>(s);
        while (next.Count < ProgressionManager.MaxLoadoutSlotCount)
            next.Add(string.Empty);
        next[a] = sb;
        next[b] = sa;
        bool ok = PM != null && PM.TrySetLoadout(next);
        if (ok) UIAudio.Play(UISoundType.ButtonClick);
        RefreshAll();
        return ok;
    }

    private bool Assign(int slot, string id)
    {
        bool ok = PM != null && PM.TryAssignLoadoutSlot(slot, id);
        UIAudio.Play(ok ? UISoundType.ButtonClick : UISoundType.ErrorOrLocked);
        RefreshAll();
        return ok;
    }

    // ─────────────────────────────────────────────────────────────
    //  Drag & Drop Handling
    // ─────────────────────────────────────────────────────────────

    private void HandleRowBeginDrag(PowerupRowWidget row, PointerEventData e)
    {
        dragSkillId = row.SkillId;
        dragFromSlot = -1;
        dragDropHandled = false;
        Ghost.Show(row.Icon, e.position, e.pressEventCamera);
    }

    private void HandleSlotBeginDrag(PowerupLoadoutSlotWidget slot, PointerEventData e)
    {
        dragSkillId = slot.SkillId;
        dragFromSlot = slot.SlotIndex;
        dragDropHandled = false;
        Ghost.Show(slot.Icon, e.position, e.pressEventCamera);
    }

    private void HandleDrag(PointerEventData e) => Ghost.Move(e.position, e.pressEventCamera);

    private void HandleDrop(int targetSlot, PointerEventData e)
    {
        if (string.IsNullOrEmpty(dragSkillId)) return;
        if (dragFromSlot >= 0)
            TrySwapSlots(dragFromSlot, targetSlot);
        else
            TryEquipToSlot(dragSkillId, targetSlot);
        dragDropHandled = true;
    }

    private void HandleEndDrag(PointerEventData e)
    {
        Ghost.Hide();
        if (dragFromSlot >= 0 && !dragDropHandled)
            TryUnequipSlot(dragFromSlot);
        dragSkillId = null;
        dragFromSlot = -1;
        dragDropHandled = false;
    }

    // ─────────────────────────────────────────────────────────────
    //  Skill Resolution Helpers
    // ─────────────────────────────────────────────────────────────

    private List<string> CollectActiveCampaignSkillIds()
    {
        EconomyBalanceConfig balance = EconomyBalanceConfig.Active;
        var list = new List<string>(4);
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (powerUpAssets != null)
        {
            for (int i = 0; i < powerUpAssets.Length; i++)
            {
                PowerUpData data = powerUpAssets[i];
                if (data == null || !data.IsCampaignActiveSkill)
                    continue;

                string skillId = PowerUpController.GetPowerUpId(data.PowerUpType);
                if (!string.IsNullOrEmpty(skillId) && added.Add(skillId))
                    list.Add(skillId);
            }
        }

        for (int i = 0; i < DefaultCampaignSkillIds.Length; i++)
        {
            string skillId = DefaultCampaignSkillIds[i];
            if (balance.IsSkillActiveInCampaign(skillId) && added.Add(skillId))
                list.Add(skillId);
        }

        return list;
    }

    private void ResolveSkillMetadata(string skillId, out string displayName, out Sprite icon)
    {
        PowerUpData data = FindPowerUpData(skillId);
        if (data != null)
        {
            displayName = data.DisplayName;
            icon = data.Icon;
            return;
        }

        displayName = PowerUpController.GetFallbackDisplayName(skillId);
        icon = null;
    }

    private PowerUpData FindPowerUpData(string skillId)
    {
        if (string.IsNullOrEmpty(skillId))
            return null;

        if (powerUpAssets != null)
        {
            for (int i = 0; i < powerUpAssets.Length; i++)
            {
                PowerUpData asset = powerUpAssets[i];
                if (asset == null)
                    continue;

                string assetSkillId = PowerUpController.GetPowerUpId(asset.PowerUpType);
                if (string.Equals(asset.Id, skillId, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(assetSkillId, skillId, StringComparison.OrdinalIgnoreCase))
                {
                    return asset;
                }
            }
        }

        return PowerUpController.Instance != null
            ? PowerUpController.Instance.GetPowerUpData(skillId)
            : null;
    }
}
