using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable UI Widget for a single Power Skill button spawned inside <see cref="InGameHUD"/>'s power container.
/// Supports both legacy consumable counts and modular Gravita core visual states (Ready / Active / Cooldown)
/// indicated solely by continuous opaque color cycling without extra text clutter.
/// </summary>
public class PowerUpSlotWidget : MonoBehaviour
{
    public enum SlotVisualState
    {
        Ready = 0,
        Active = 1,
        Cooldown = 2
    }

    [Header("UI Elements")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;
    [SerializeField] private Image cooldownFillImage;

    [Header("Color Cycling Visuals (No Text Labels)")]
    [Tooltip("Cycle speed (cycles/sec) of icon tint while the core is READY.")]
    [SerializeField, Min(0.1f)] private float readyColorCycleSpeed = 2.2f;
    [Tooltip("Cycle speed (cycles/sec) of icon tint while the core is ACTIVE.")]
    [SerializeField, Min(0.1f)] private float activeColorCycleSpeed = 6.5f;
    [SerializeField] private Color readyCycleColorA = Color.white;
    [SerializeField] private Color readyCycleColorB = new Color(0.45f, 0.95f, 1f, 1f);
    [SerializeField] private Color activeCycleColorA = new Color(1f, 0.92f, 0.35f, 1f);
    [SerializeField] private Color activeCycleColorB = new Color(0.35f, 1f, 0.75f, 1f);
    [SerializeField] private Color cooldownTintColor = new Color(0.42f, 0.46f, 0.55f, 1f);

    private PowerUpType boundType;
    private Action<PowerUpType> onClickCallback;
    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCaptured;
    private bool buttonBound;
    private bool lastActiveEffect;
    private bool isModularDisplay;
    private bool isPlayingFeedbackTint;
    private SlotVisualState visualState = SlotVisualState.Ready;
    private float cooldownNormalized;

    public PowerUpType PowerUpType => boundType;
    public SlotVisualState CurrentVisualState => visualState;
    public float ReadyColorCycleSpeed => readyColorCycleSpeed;
    public float ActiveColorCycleSpeed => activeColorCycleSpeed;
    public float CooldownNormalized => cooldownNormalized;
    public Color CurrentIconColor => iconImage != null ? iconImage.color : Color.white;
    public TMP_Text CountText => countText;

    private void Awake()
    {
        if (button == null)
            button = GetComponent<Button>();

        CaptureBaseScale();
    }

    private void OnDisable()
    {
        transform.DOKill();
        if (baseScaleCaptured)
            transform.localScale = baseScale;
        lastActiveEffect = false;
    }

    private void OnDestroy()
    {
        transform.DOKill();
        if (buttonBound && button != null)
        {
            button.onClick.RemoveListener(HandleButtonClick);
            buttonBound = false;
        }
    }

    private void CaptureBaseScale()
    {
        if (baseScaleCaptured) return;
        baseScale = transform.localScale.sqrMagnitude > 0.001f ? transform.localScale : Vector3.one;
        baseScaleCaptured = true;
    }

    public void Setup(
        PowerUpType type,
        Sprite icon,
        int ownedCount,
        bool canActivate,
        bool isActiveEffect,
        Action<PowerUpType> onClicked,
        bool showChargeCount = true,
        bool isReadyState = true,
        float cooldownFraction = 0f,
        PowerUpActivationMode activationMode = PowerUpActivationMode.ManualActive)
    {
        if (button == null)
            button = GetComponent<Button>();

        CaptureBaseScale();

        boundType = type;
        onClickCallback = onClicked;

        if (button != null && !buttonBound)
        {
            button.onClick.AddListener(HandleButtonClick);
            buttonBound = true;
        }

        Refresh(icon, ownedCount, canActivate, isActiveEffect, showChargeCount, isReadyState, cooldownFraction, activationMode);
    }

    public void Refresh(Sprite icon, int ownedCount, bool canActivate, bool isActiveEffect)
    {
        Refresh(icon, ownedCount, canActivate, isActiveEffect, showChargeCount: true, isReadyState: canActivate, cooldownFraction: 0f, PowerUpActivationMode.ManualActive);
    }

    public void Refresh(
        Sprite icon,
        int ownedCount,
        bool canClick,
        bool isActiveEffect,
        bool showChargeCount,
        bool isReadyState,
        float cooldownFraction,
        PowerUpActivationMode activationMode)
    {
        CaptureBaseScale();
        isModularDisplay = !showChargeCount;
        cooldownNormalized = Mathf.Clamp01(cooldownFraction);
        visualState = isActiveEffect
            ? SlotVisualState.Active
            : ((cooldownNormalized > 0.0001f || !isReadyState) ? SlotVisualState.Cooldown : SlotVisualState.Ready);

        if (iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.enabled = icon != null;
            if (isModularDisplay && !isPlayingFeedbackTint)
                iconImage.color = EvaluateVisualColor(Time.unscaledTime);
            else if (!isModularDisplay && !isPlayingFeedbackTint)
                iconImage.color = Color.white;
        }

        if (cooldownFillImage != null)
        {
            cooldownFillImage.enabled = isModularDisplay && visualState == SlotVisualState.Cooldown && cooldownNormalized > 0f;
            cooldownFillImage.fillAmount = cooldownNormalized;
        }

        if (countText != null)
        {
            if (showChargeCount)
            {
                countText.gameObject.SetActive(true);
                countText.text = ownedCount.ToString();
            }
            else
            {
                countText.gameObject.SetActive(false);
            }
        }

        if (button != null)
        {
            if (isModularDisplay)
            {
                ColorBlock cb = button.colors;
                if (cb.disabledColor.a < 0.99f)
                {
                    Color disabled = cb.disabledColor;
                    disabled.a = 1f;
                    cb.disabledColor = disabled;
                    button.colors = cb;
                }
                button.interactable = activationMode != PowerUpActivationMode.AutoTrigger && canClick;
            }
            else
            {
                button.interactable = canClick;
            }
        }

        if (lastActiveEffect != isActiveEffect)
        {
            lastActiveEffect = isActiveEffect;
            transform.DOKill();
            Vector3 targetScale = isActiveEffect ? baseScale * 1.1f : baseScale;
            transform
                .DOScale(targetScale, 0.18f)
                .SetEase(isActiveEffect ? Ease.OutBack : Ease.OutQuad)
                .SetUpdate(true)
                .SetLink(gameObject);
        }
    }

    private void Update()
    {
        if (!isModularDisplay || iconImage == null || isPlayingFeedbackTint)
            return;

        iconImage.color = EvaluateVisualColor(Time.unscaledTime);
    }

    /// <summary>
    /// Computes the opaque icon color for the current visual state at <paramref name="unscaledTime"/>.
    /// Exposed publicly for deterministic EditMode verification.
    /// </summary>
    public Color EvaluateVisualColor(float unscaledTime)
    {
        switch (visualState)
        {
            case SlotVisualState.Active:
            {
                float t = 0.5f + 0.5f * Mathf.Sin(unscaledTime * activeColorCycleSpeed * Mathf.PI * 2f);
                Color col = Color.Lerp(activeCycleColorA, activeCycleColorB, t);
                col.a = 1f;
                return col;
            }

            case SlotVisualState.Ready:
            {
                float t = 0.5f + 0.5f * Mathf.Sin(unscaledTime * readyColorCycleSpeed * Mathf.PI * 2f);
                Color col = Color.Lerp(readyCycleColorA, readyCycleColorB, t);
                col.a = 1f;
                return col;
            }

            default:
            {
                Color col = cooldownTintColor;
                col.a = 1f;
                return col;
            }
        }
    }

    private void HandleButtonClick()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        onClickCallback?.Invoke(boundType);
    }

    public void PlayActivationFeedback(bool succeeded)
    {
        CaptureBaseScale();
        transform.DOKill();
        if (iconImage != null)
            iconImage.DOKill();

        Vector3 restingScale = lastActiveEffect ? baseScale * 1.1f : baseScale;
        transform.localScale = restingScale;

        if (succeeded)
        {
            transform
                .DOPunchScale(new Vector3(0.14f, 0.14f, 0f), 0.22f, 6, 0.5f)
                .SetUpdate(true)
                .SetLink(gameObject);

            if (iconImage != null)
            {
                isPlayingFeedbackTint = true;
                iconImage.color = new Color(0.45f, 1f, 0.95f, 1f);
                iconImage
                    .DOColor(Color.white, 0.25f)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() => isPlayingFeedbackTint = false);
            }
        }
        else
        {
            transform
                .DOShakePosition(0.22f, new Vector3(8f, 0f, 0f), 18, 0f, false, true)
                .SetUpdate(true)
                .SetLink(gameObject);

            if (iconImage != null)
            {
                isPlayingFeedbackTint = true;
                iconImage.color = new Color(1f, 0.35f, 0.35f, 1f);
                iconImage
                    .DOColor(Color.white, 0.25f)
                    .SetUpdate(true)
                    .SetLink(gameObject)
                    .OnComplete(() => isPlayingFeedbackTint = false);
            }
        }
    }
}
