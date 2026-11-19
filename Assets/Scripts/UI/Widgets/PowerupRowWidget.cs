using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PowerupRowWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private GameObject equippedMarker;   // optional, you place it
    [SerializeField] private Button tapButton;            // falls back to Button on this object

    public string SkillId { get; private set; }
    public Sprite Icon { get; private set; }
    public TMP_Text CountText => countText;

    private Action onTap;
    private Func<bool> isLocked;
    private Action<PowerupRowWidget, PointerEventData> onBeginDrag;
    private Action<PointerEventData> onDrag;
    private Action<PowerupRowWidget, PointerEventData> onEndDrag;
    private Button bound;
    private ScrollRect parentScrollRect;
    private bool routingDragToScroll;

    private ScrollRect ResolveParentScrollRect()
    {
        if (parentScrollRect == null)
        {
            Transform p = transform.parent;
            if (p != null)
                parentScrollRect = p.GetComponentInParent<ScrollRect>();
        }
        return parentScrollRect;
    }

    public void Setup(string skillId, Sprite icon, int count, int cap, bool equipped, bool locked,
        Action tap, Func<bool> lockedProbe,
        Action<PowerupRowWidget, PointerEventData> begin, Action<PointerEventData> drag,
        Action<PowerupRowWidget, PointerEventData> end)
    {
        SkillId = skillId;
        Icon = icon;
        onTap = tap;
        isLocked = lockedProbe;
        onBeginDrag = begin;
        onDrag = drag;
        onEndDrag = end;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
        }

        if (countText != null)
        {
            countText.textWrappingMode = TextWrappingModes.NoWrap;
            countText.overflowMode = TextOverflowModes.Overflow;
            countText.text = Mathf.Max(0, count).ToString();
        }

        if (equippedMarker != null)
            equippedMarker.SetActive(equipped);

        Button target = tapButton != null ? tapButton : GetComponent<Button>();
        if (bound != target)
        {
            if (bound != null) bound.onClick.RemoveListener(HandleTap);
            bound = target;
            if (bound != null) bound.onClick.AddListener(HandleTap);
        }
        if (bound != null) bound.interactable = !locked;
    }

    private void OnDestroy()
    {
        if (bound != null)
            bound.onClick.RemoveListener(HandleTap);
    }

    private void HandleTap()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        onTap?.Invoke();
    }

    public void OnBeginDrag(PointerEventData e)
    {
        ScrollRect scroll = ResolveParentScrollRect();
        if (scroll != null && scroll.horizontal && Mathf.Abs(e.delta.x) > Mathf.Abs(e.delta.y))
        {
            routingDragToScroll = true;
            scroll.OnBeginDrag(e);
            return;
        }

        routingDragToScroll = false;
        if (isLocked != null && isLocked())
        {
            e.pointerDrag = null;
            return;
        }
        onBeginDrag?.Invoke(this, e);
    }

    public void OnDrag(PointerEventData e)
    {
        if (routingDragToScroll)
        {
            ResolveParentScrollRect()?.OnDrag(e);
            return;
        }
        onDrag?.Invoke(e);
    }

    public void OnEndDrag(PointerEventData e)
    {
        if (routingDragToScroll)
        {
            routingDragToScroll = false;
            ResolveParentScrollRect()?.OnEndDrag(e);
            return;
        }
        onEndDrag?.Invoke(this, e);
    }
}
