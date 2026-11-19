using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dedicated UI widget for a single World entry spawned by <see cref="CampaignMapUI"/>.
/// Preserves authored Prefab layout while binding click callback, title text, and lock state.
/// </summary>
public class WorldButtonWidget : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private Button selectButton;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private GameObject lockOverlay;

    private Action onSelectedCallback;
    private bool buttonBound;

    public void Setup(string worldName, bool unlocked, string unlockRequirementText, Action onSelected)
    {
        if (selectButton == null)
            selectButton = GetComponent<Button>();

        onSelectedCallback = onSelected;

        if (titleText != null)
        {
            if (unlocked)
            {
                titleText.text = worldName;
            }
            else
            {
                titleText.text = string.IsNullOrEmpty(unlockRequirementText)
                    ? $"{worldName} (Locked)"
                    : $"{worldName}\n<size=65%>{unlockRequirementText}</size>";
            }
        }

        if (lockOverlay != null)
            lockOverlay.SetActive(!unlocked);

        if (selectButton != null)
        {
            if (!buttonBound)
            {
                selectButton.onClick.AddListener(HandleClicked);
                buttonBound = true;
            }
            selectButton.interactable = unlocked;
        }
    }

    private void OnDestroy()
    {
        if (buttonBound && selectButton != null)
        {
            selectButton.onClick.RemoveListener(HandleClicked);
            buttonBound = false;
        }
    }

    private void HandleClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);
        onSelectedCallback?.Invoke();
    }
}
