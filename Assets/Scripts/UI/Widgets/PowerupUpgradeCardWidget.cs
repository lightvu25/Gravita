using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PowerupUpgradeCardWidget : MonoBehaviour
{
    [SerializeField] private Image iconImage;
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text levelText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text currentPowerText;       // e.g. "1.00"
    [SerializeField] private TMP_Text currentPowerTypeText;   // e.g. "sec"
    [SerializeField] private TMP_Text nextPowerText;          // e.g. "2.00"
    [SerializeField] private TMP_Text nextPowerTypeText;      // e.g. "sec"
    [SerializeField] private GameObject nextGroupRoot;        // arrow + next texts, hidden at max level
    [SerializeField] private Button upgradeButton;
    [SerializeField] private TMP_Text costText;
    [SerializeField] private GameObject maxLevelRoot;         // optional "MAX" object

    private Action onUpgrade;

    public void Setup(PowerUpData data, Sprite icon, string displayName, int level, int coins, Action upgrade)
    {
        onUpgrade = upgrade;

        if (iconImage != null)
        {
            iconImage.SetSpriteNative(icon);
            iconImage.enabled = icon != null;
        }

        if (nameText != null)
            nameText.text = displayName;

        if (levelText != null)
            levelText.text = $"LVL {level}";

        if (descriptionText != null)
            descriptionText.text = data != null ? data.Description : string.Empty;

        string unit = data != null ? data.StatUnitLabel : string.Empty;
        float legacy = data == null ? 0f : (data.UpgradeStat == PowerUpStatKind.DurationSeconds ? data.Duration
                       : data.UpgradeStat == PowerUpStatKind.WallCells && data.PowerUpType == PowerUpType.Teleport ? data.MaxWallTraversal
                       : data.MaxWallCells);

        if (currentPowerText != null)
            currentPowerText.text = data != null ? data.FormatStat(level, legacy) : "-";

        if (currentPowerTypeText != null)
            currentPowerTypeText.text = unit;

        int cost = data != null ? data.GetUpgradeCost(level) : -1;
        bool isMax = cost < 0;

        if (nextGroupRoot != null)
            nextGroupRoot.SetActive(!isMax);

        if (!isMax && data != null)
        {
            if (nextPowerText != null)
                nextPowerText.text = data.FormatStat(level + 1, legacy);

            if (nextPowerTypeText != null)
                nextPowerTypeText.text = unit;
        }

        if (maxLevelRoot != null)
            maxLevelRoot.SetActive(isMax);

        if (costText != null)
            costText.text = isMax ? string.Empty : cost.ToString();

        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveListener(HandleClick);
            upgradeButton.onClick.AddListener(HandleClick);
            upgradeButton.gameObject.SetActive(!isMax);
            upgradeButton.interactable = !isMax && coins >= cost;   // no alpha dimming per project rules
        }
    }

    private void OnDestroy()
    {
        if (upgradeButton != null)
            upgradeButton.onClick.RemoveListener(HandleClick);
    }

    private void HandleClick()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        onUpgrade?.Invoke();
    }
}
