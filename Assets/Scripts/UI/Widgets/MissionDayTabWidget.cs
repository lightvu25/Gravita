using System;
using DG.Tweening;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MissionDayTabWidget : MonoBehaviour
{
    [Header("Day Configuration")]
    [Range(1, 7)]
    [SerializeField] private int dayIndex = 1;

    [Header("UI Elements (Inspector Wired)")]
    [SerializeField] private Button button;
    [SerializeField] private Image backgroundImage;
    [SerializeField] private TMP_Text dayLabel;
    [SerializeField] private Image lockIconImage;
    [SerializeField] private GameObject notificationDot;

    [Header("DOTween Scale Feedback")]
    [SerializeField] private float selectedScale = 1.06f;
    [SerializeField] private float tweenDuration = 0.18f;
    [SerializeField] private Ease scaleUpEase = Ease.OutBack;
    [SerializeField] private Ease scaleDownEase = Ease.OutQuad;

    private Action<int> onSelectDay;
    private bool buttonBound;

    public int DayIndex
    {
        get => dayIndex;
        set => dayIndex = Mathf.Clamp(value, 1, 7);
    }

    private void OnDisable()
    {
        Transform target = button != null ? button.transform : transform;
        target.DOKill();
    }

    private void OnDestroy()
    {
        if (buttonBound && button != null)
        {
            button.onClick.RemoveListener(HandleDayClicked);
            buttonBound = false;
        }
    }

    public void Bind(int day, Action<int> onSelectCallback)
    {
        dayIndex = Mathf.Clamp(day, 1, 7);
        onSelectDay = onSelectCallback;

        if (button != null && !buttonBound)
        {
            button.onClick.AddListener(HandleDayClicked);
            buttonBound = true;
        }
    }

    private void HandleDayClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        onSelectDay?.Invoke(dayIndex);
    }

    public void RefreshState(
        bool isUnlocked,
        bool isSelected,
        Color selectedColor,
        Color unlockedColor,
        Color lockedColor,
        Sprite defaultLockedSprite = null,
        bool hasClaimableMission = false)
    {
        int clampedDay = Mathf.Clamp(dayIndex, 1, 7);

        if (dayLabel != null)
        {
            dayLabel.text = MissionService.GetDayShortName(clampedDay);
            dayLabel.color = isUnlocked
                ? (isSelected ? Color.white : new Color(0.82f, 0.86f, 0.94f, 1f))
                : new Color(0.55f, 0.58f, 0.65f, 0.65f);
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = !isUnlocked
                ? lockedColor
                : (isSelected ? selectedColor : unlockedColor);
        }

        if (lockIconImage != null)
        {
            if (defaultLockedSprite != null && lockIconImage.sprite == null)
                lockIconImage.sprite = defaultLockedSprite;
            lockIconImage.gameObject.SetActive(!isUnlocked);
        }

        if (notificationDot != null)
        {
            notificationDot.SetActive(isUnlocked && hasClaimableMission);
        }

        if (button != null)
        {
            button.interactable = isUnlocked;
        }

        Transform scaleTarget = button != null ? button.transform : transform;
        scaleTarget.DOKill();
        float targetScale = isSelected ? selectedScale : 1f;
        scaleTarget
            .DOScale(targetScale, tweenDuration)
            .SetEase(isSelected ? scaleUpEase : scaleDownEase)
            .SetUpdate(true)
            .SetLink(scaleTarget.gameObject);
    }
}
