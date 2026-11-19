using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

[RequireComponent(typeof(Button))]
public class PowerupLoadoutSlotWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private Image iconImage;        // power icon (also shows '+' fallback when emptyRoot is null)
    [SerializeField] private TMP_Text countText;     // optional count label on the loadout slot
    [SerializeField] private GameObject emptyRoot;   // optional dedicated "+" child object (never self)
    [SerializeField] private Sprite emptyPlusSprite; // optional "+" sprite shown on iconImage when empty

    public int SlotIndex { get; private set; }
    public string SkillId { get; private set; } = string.Empty;
    public Sprite Icon { get; private set; }
    public TMP_Text CountText => countText;

    private Button button;
    private Sprite cachedEmptySprite;
    private bool cacheInitialized;
    private Func<bool> isLocked;
    private Action<int> onTap;
    private Action<PowerupLoadoutSlotWidget, PointerEventData> onBeginDrag;
    private Action<PointerEventData> onDrag;
    private Action<PowerupLoadoutSlotWidget, PointerEventData> onEndDrag;
    private Action<int, PointerEventData> onDrop;   // slot index that received the drop

    private void Awake()
    {
        InitializeEmptyStateCache();
    }

    private void InitializeEmptyStateCache()
    {
        if (cacheInitialized) return;
        cacheInitialized = true;

        if (emptyRoot == gameObject)
            emptyRoot = null;

        if (cachedEmptySprite == null && iconImage != null && iconImage.sprite != null)
            cachedEmptySprite = iconImage.sprite;
    }

    public void Bind(int index, Func<bool> locked, Action<int> tap,
        Action<PowerupLoadoutSlotWidget, PointerEventData> begin, Action<PointerEventData> drag,
        Action<PowerupLoadoutSlotWidget, PointerEventData> end, Action<int, PointerEventData> drop)
    {
        InitializeEmptyStateCache();
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

    public void Refresh(string skillId, Sprite icon, int count = 0)
    {
        InitializeEmptyStateCache();
        SkillId = skillId ?? string.Empty;
        Icon = icon;
        bool filled = !string.IsNullOrEmpty(SkillId);
        bool hasSeparateEmptyRoot = emptyRoot != null && emptyRoot != gameObject;

        if (filled)
        {
            if (iconImage != null)
            {
                iconImage.sprite = icon;
                iconImage.enabled = icon != null;
            }
            if (hasSeparateEmptyRoot)
                emptyRoot.SetActive(false);

            if (countText != null)
            {
                countText.text = Mathf.Max(0, count).ToString();
                countText.gameObject.SetActive(true);
            }
            return;
        }

        if (countText != null)
        {
            countText.text = string.Empty;
            countText.gameObject.SetActive(false);
        }

        if (hasSeparateEmptyRoot)
        {
            emptyRoot.SetActive(true);
            if (iconImage != null && iconImage.gameObject != emptyRoot)
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }
        else if (iconImage != null)
        {
            Sprite plus = emptyPlusSprite != null ? emptyPlusSprite : cachedEmptySprite;
            if (plus != null)
            {
                iconImage.sprite = plus;
                iconImage.enabled = true;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.enabled = false;
            }
        }
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
