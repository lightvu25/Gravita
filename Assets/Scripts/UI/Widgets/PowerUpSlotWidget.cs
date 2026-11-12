using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable UI Widget for a single Power Skill button spawned inside <see cref="InGameHUD"/>'s power container.
/// Binds icon sprite, charge count text, interactability, and click callback cleanly without layout-fighting offsets.
/// </summary>
public class PowerUpSlotWidget : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Button button;
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text countText;

    private PowerUpType boundType;
    private Action<PowerUpType> onClickCallback;
    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCaptured;
    private bool buttonBound;
    private bool lastActiveEffect;

    public PowerUpType PowerUpType => boundType;

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
        Action<PowerUpType> onClicked)
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

        Refresh(icon, ownedCount, canActivate, isActiveEffect);
    }

    public void Refresh(Sprite icon, int ownedCount, bool canActivate, bool isActiveEffect)
    {
        CaptureBaseScale();

        if (iconImage != null)
        {
            iconImage.SetSpriteNative(icon);
            iconImage.enabled = icon != null;
        }

        if (countText != null)
            countText.text = ownedCount.ToString();

        if (button != null)
            button.interactable = canActivate;

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
                iconImage.color = new Color(0.45f, 1f, 0.95f, 1f);
                iconImage
                    .DOColor(Color.white, 0.25f)
                    .SetUpdate(true)
                    .SetLink(gameObject);
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
                iconImage.color = new Color(1f, 0.35f, 0.35f, 1f);
                iconImage
                    .DOColor(Color.white, 0.25f)
                    .SetUpdate(true)
                    .SetLink(gameObject);
            }
        }
    }
}
