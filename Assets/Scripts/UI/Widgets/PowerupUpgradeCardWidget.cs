using System;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class PowerupUpgradeCardWidget : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
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
    private ScrollRect parentScrollRect;
    private int cachedRequiredCoins;
    private bool cachedIsMax;
    private bool cachedIsUnlocked = true;

    public int DisplayedCoinCost => cachedRequiredCoins;

    private void Awake()
    {
        DisableBrokenNestedScrollRects();
    }

    private void DisableBrokenNestedScrollRects()
    {
        ScrollRect[] nestedScrolls = GetComponentsInChildren<ScrollRect>(true);
        for (int i = 0; i < nestedScrolls.Length; i++)
        {
            ScrollRect sr = nestedScrolls[i];
            if (sr == null || sr.transform == transform)
                continue;

            sr.enabled = false;
            if (sr.content == null || sr.content.childCount == 0)
                sr.gameObject.SetActive(false);
        }
    }

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

    public void OnBeginDrag(PointerEventData e) => ResolveParentScrollRect()?.OnBeginDrag(e);
    public void OnDrag(PointerEventData e) => ResolveParentScrollRect()?.OnDrag(e);
    public void OnEndDrag(PointerEventData e) => ResolveParentScrollRect()?.OnEndDrag(e);

    public void Setup(PowerUpData data, Sprite icon, string displayName, int level, int coins, Action upgrade)
    {
        int defaultCost = data != null ? data.GetUpgradeCost(level) : -1;
        Setup(data, icon, displayName, level, coins, defaultCost, 0, isUnlocked: true, upgrade);
    }

    public void Setup(
        PowerUpData data,
        Sprite icon,
        string displayName,
        int level,
        int coins,
        int effectiveCoinCost,
        int shardsDiscountCount,
        bool isUnlocked,
        Action upgrade)
    {
        DisableBrokenNestedScrollRects();
        onUpgrade = upgrade;

        if (iconImage != null)
        {
            iconImage.sprite = icon;
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

        int cost = effectiveCoinCost >= 0 ? effectiveCoinCost : (data != null ? data.GetUpgradeCost(level) : -1);
        bool isMax = data != null && data.GetUpgradeCost(level) < 0;
        cachedIsMax = isMax;
        cachedIsUnlocked = isUnlocked;
        cachedRequiredCoins = Mathf.Max(0, cost);

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
        {
            if (isMax)
                costText.text = string.Empty;
            else if (!isUnlocked)
                costText.text = "LOCKED";
            else if (cachedRequiredCoins == 0 && shardsDiscountCount > 0)
                costText.text = "0";
            else
                costText.text = cachedRequiredCoins.ToString();
        }

        if (upgradeButton != null)
        {
            upgradeButton.onClick.RemoveListener(HandleClick);
            upgradeButton.onClick.AddListener(HandleClick);
            upgradeButton.gameObject.SetActive(!isMax);
            RefreshAffordability(coins);
        }
    }

    public void RefreshAffordability(int coins)
    {
        if (upgradeButton != null)
            upgradeButton.interactable = cachedIsUnlocked && !cachedIsMax && coins >= cachedRequiredCoins;
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
