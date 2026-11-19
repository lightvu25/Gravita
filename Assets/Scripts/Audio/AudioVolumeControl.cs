using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared audio channel UI controller synchronizing mute buttons, optional whole-number 0..max
/// sliders, icons, and pre-mute/pre-drag volume persistence across SettingUI and PauseMenuUI.
/// </summary>
internal sealed class AudioChannelControl
{
    private readonly int maxVolume;
    private readonly Func<bool> hasManager;
    private readonly Func<int> getVolume;
    private readonly Action<int> setVolume;
    private readonly Func<int> getPreviousVolume;
    private readonly Action<int> setPreviousVolume;
    private readonly Action saveVolume;
    private readonly Func<int> getFallbackVolume;
    private readonly Action<int> setFallbackVolume;
    private readonly Action onUnmuteAudio;

    private Button button;
    private Slider resolvedSlider;
    private Image icon;
    private Sprite onSprite;
    private Sprite offSprite;

    private UnityAction<float> sliderListener;
    private bool bound;
    private bool dragActive;
    private int dragStartVolume;

    public AudioChannelControl(
        int maxVolume,
        Func<bool> hasManager,
        Func<int> getVolume,
        Action<int> setVolume,
        Func<int> getPreviousVolume,
        Action<int> setPreviousVolume,
        Action saveVolume,
        Func<int> getFallbackVolume,
        Action<int> setFallbackVolume,
        Action onUnmuteAudio = null)
    {
        this.maxVolume = Mathf.Max(1, maxVolume);
        this.hasManager = hasManager;
        this.getVolume = getVolume;
        this.setVolume = setVolume;
        this.getPreviousVolume = getPreviousVolume;
        this.setPreviousVolume = setPreviousVolume;
        this.saveVolume = saveVolume;
        this.getFallbackVolume = getFallbackVolume;
        this.setFallbackVolume = setFallbackVolume;
        this.onUnmuteAudio = onUnmuteAudio;
    }

    public void ConfigureWidgets(Button button, Slider assignedSlider, Image icon, Sprite onSprite, Sprite offSprite)
    {
        Slider nextSlider = assignedSlider != null
            ? assignedSlider
            : (button != null ? button.GetComponentInChildren<Slider>(true) : null);

        if (bound && resolvedSlider != nextSlider)
        {
            if (resolvedSlider != null && sliderListener != null)
                resolvedSlider.onValueChanged.RemoveListener(sliderListener);

            if (nextSlider != null)
            {
                nextSlider.minValue = 0f;
                nextSlider.maxValue = maxVolume;
                nextSlider.wholeNumbers = true;
                sliderListener ??= HandleSliderChanged;
                nextSlider.onValueChanged.AddListener(sliderListener);
            }
        }

        this.button = button;
        this.icon = icon;
        this.onSprite = onSprite;
        this.offSprite = offSprite;
        resolvedSlider = nextSlider;
    }

    public void Bind(UnityAction onButtonClicked)
    {
        if (bound)
            return;

        if (button != null && onButtonClicked != null)
            button.onClick.AddListener(onButtonClicked);

        if (resolvedSlider != null)
        {
            resolvedSlider.minValue = 0f;
            resolvedSlider.maxValue = maxVolume;
            resolvedSlider.wholeNumbers = true;

            sliderListener ??= HandleSliderChanged;
            resolvedSlider.onValueChanged.AddListener(sliderListener);
        }

        bound = true;
    }

    public void Unbind(UnityAction onButtonClicked)
    {
        if (!bound)
            return;

        if (button != null && onButtonClicked != null)
            button.onClick.RemoveListener(onButtonClicked);

        if (resolvedSlider != null && sliderListener != null)
            resolvedSlider.onValueChanged.RemoveListener(sliderListener);

        dragActive = false;
        bound = false;
    }

    public void SyncUI()
    {
        bool managerReady = hasManager();
        int current = managerReady ? Mathf.Clamp(getVolume(), 0, maxVolume) : 0;

        if (icon != null)
            icon.sprite = current > 0 ? onSprite : offSprite;

        if (resolvedSlider != null)
        {
            resolvedSlider.minValue = 0f;
            resolvedSlider.maxValue = maxVolume;
            resolvedSlider.wholeNumbers = true;

            if (managerReady && !Mathf.Approximately(resolvedSlider.value, current))
                resolvedSlider.SetValueWithoutNotify(current);
        }

        if (managerReady && current > 0 && !dragActive)
            setFallbackVolume(current);
    }

    public void ToggleMute()
    {
        if (IsPointerOverChildSlider(button, resolvedSlider))
            return;

        GameHaptics.Play(HapticEvent.UISelect);

        if (!hasManager())
            return;

        int current = getVolume();
        if (current > 0)
        {
            setPreviousVolume(current);
            setFallbackVolume(current);
            setVolume(0);
        }
        else
        {
            int savedPrev = getPreviousVolume();
            int fallback = getFallbackVolume();
            int restored = savedPrev > 0 ? savedPrev : (fallback > 0 ? fallback : maxVolume / 2);
            restored = Mathf.Clamp(restored, 1, maxVolume);

            setFallbackVolume(restored);
            setVolume(restored);
            onUnmuteAudio?.Invoke();
        }

        saveVolume();
        SyncUI();
    }

    public void HandleSliderChanged(float rawValue)
    {
        int newValue = Mathf.Clamp(Mathf.RoundToInt(rawValue), 0, maxVolume);

        if (resolvedSlider != null && !Mathf.Approximately(resolvedSlider.value, newValue))
            resolvedSlider.SetValueWithoutNotify(newValue);

        if (!hasManager())
        {
            SyncUI();
            return;
        }

        int previousLive = Mathf.Clamp(getVolume(), 0, maxVolume);
        bool pointerPressed = IsPointerPressed();

        if (pointerPressed && !dragActive)
        {
            dragStartVolume = previousLive > 0 ? previousLive : getPreviousVolume();
            dragActive = true;
        }

        setVolume(newValue);

        // Preserve pre-zero level when changed without an active drag (direct tap/unit test).
        if (!pointerPressed && !dragActive)
        {
            if (newValue > 0)
            {
                setPreviousVolume(newValue);
                setFallbackVolume(newValue);
            }
            else if (previousLive > 0)
            {
                setPreviousVolume(previousLive);
                setFallbackVolume(previousLive);
            }

            saveVolume();
        }

        SyncUI();
    }

    public void TickPointerRelease()
    {
        if (!dragActive || IsPointerPressed())
            return;

        dragActive = false;
        if (!hasManager())
            return;

        int settled = getVolume();
        if (settled > 0)
        {
            setPreviousVolume(settled);
            setFallbackVolume(settled);
        }
        else if (dragStartVolume > 0)
        {
            setPreviousVolume(dragStartVolume);
            setFallbackVolume(dragStartVolume);
        }

        saveVolume();
        SyncUI();
    }

    private static bool IsPointerPressed()
    {
        Pointer pointer = Pointer.current;
        if (pointer != null && pointer.press.isPressed)
            return true;

        return Mouse.current != null && Mouse.current.leftButton.isPressed;
    }

    private static bool IsPointerReleasedThisFrame()
    {
        Pointer pointer = Pointer.current;
        if (pointer != null && pointer.press.wasReleasedThisFrame)
            return true;

        return Mouse.current != null && Mouse.current.leftButton.wasReleasedThisFrame;
    }

    private static bool IsPointerOverChildSlider(Button button, Slider slider)
    {
        if (EventSystem.current == null || button == null || slider == null || !slider.gameObject.activeInHierarchy)
            return false;

        // Sliders parented under a Button bubble OnPointerClick up to that Button.
        if (!slider.transform.IsChildOf(button.transform))
            return false;

        if (!IsPointerReleasedThisFrame() && !IsPointerPressed())
            return false;

        if (!TryGetPointerScreenPosition(out Vector2 screenPos))
            return false;

        Camera eventCamera = GetEventCamera(slider.transform);
        RectTransform sliderRect = slider.transform as RectTransform;
        if (sliderRect != null && RectTransformUtility.RectangleContainsScreenPoint(sliderRect, screenPos, eventCamera))
            return true;

        RectTransform handleRect = slider.handleRect;
        return handleRect != null
               && handleRect.IsChildOf(button.transform)
               && RectTransformUtility.RectangleContainsScreenPoint(handleRect, screenPos, eventCamera);
    }

    private static bool TryGetPointerScreenPosition(out Vector2 screenPos)
    {
        Pointer pointer = Pointer.current;
        if (pointer != null)
        {
            screenPos = pointer.position.ReadValue();
            return true;
        }

        if (Mouse.current != null)
        {
            screenPos = Mouse.current.position.ReadValue();
            return true;
        }

        screenPos = default;
        return false;
    }

    private static Camera GetEventCamera(Transform target)
    {
        Canvas canvas = target != null ? target.GetComponentInParent<Canvas>() : null;
        if (canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay)
            return null;

        return canvas.worldCamera != null ? canvas.worldCamera : Camera.main;
    }
}
