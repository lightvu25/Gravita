using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class PowerupLoadoutSlotWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image iconImage;      // power icon (hidden when empty)
    [SerializeField] private GameObject emptyRoot; // the "+" image

    public int SlotIndex { get; private set; }
    public string SkillId { get; private set; } = string.Empty;
    public Sprite Icon { get; private set; }

    private Button button;
    private Func<bool> isLocked;
    private Action<int> onTap;
    private Action<PowerupLoadoutSlotWidget, PointerEventData> onBeginDrag;
    private Action<PointerEventData> onDrag;
    private Action<PowerupLoadoutSlotWidget, PointerEventData> onEndDrag;
    private Action<int, PointerEventData> onDrop;   // slot index that received the drop

    public void Bind(int index, Func<bool> locked, Action<int> tap,
        Action<PowerupLoadoutSlotWidget, PointerEventData> begin, Action<PointerEventData> drag,
        Action<PowerupLoadoutSlotWidget, PointerEventData> end, Action<int, PointerEventData> drop)
    {
        SlotIndex = index;
        isLocked = locked;
        onTap = tap;
        onBeginDrag = begin;
        onDrag = drag;
        onEndDrag = end;
        onDrop = drop;
        if (button == null) button = GetComponent<Button>();
        button.onClick.RemoveListener(HandleTap);
        button.onClick.AddListener(HandleTap);
    }

    public void Refresh(string skillId, Sprite icon)
    {
        SkillId = skillId ?? string.Empty;
        Icon = icon;
        bool filled = !string.IsNullOrEmpty(SkillId);
        if (iconImage != null)
        {
            iconImage.SetSpriteNative(filled ? icon : null);
            iconImage.enabled = filled && icon != null;
        }
        if (emptyRoot != null)
            emptyRoot.SetActive(!filled);
    }

    private void OnDestroy()
    {
        if (button != null)
            button.onClick.RemoveListener(HandleTap);
    }

    private void HandleTap()
    {
        if (!string.IsNullOrEmpty(SkillId))
            onTap?.Invoke(SlotIndex);
    }

    public void OnBeginDrag(PointerEventData e)
    {
        if (string.IsNullOrEmpty(SkillId) || (isLocked != null && isLocked()))
        {
            e.pointerDrag = null;
            return;
        }
        onBeginDrag?.Invoke(this, e);
    }

    public void OnDrag(PointerEventData e) => onDrag?.Invoke(e);
    public void OnEndDrag(PointerEventData e) => onEndDrag?.Invoke(this, e);
    public void OnDrop(PointerEventData e) => onDrop?.Invoke(SlotIndex, e);
}

/// <summary>
/// Alias class for Hand-Coding Guide compatibility.
/// </summary>
public class PowerupSlotWidget : PowerupLoadoutSlotWidget { }
