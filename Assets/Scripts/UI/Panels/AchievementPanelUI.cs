using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Standalone Achievement UI presenter (<see cref="UIPanelType.Achievement"/>), completely separated
/// from <see cref="MissionPanelUI"/>.
/// </summary>
public class AchievementPanelUI : MonoBehaviour, IUIPanel
{
    [Header("Data")]
    [SerializeField] private AchievementDatabase achievementDatabase;

    [Header("Header & Summary Progress (Optional)")]
    [SerializeField] private Button closeButton;
    [SerializeField] private Slider totalCompletionSlider;
    [Tooltip("Legacy fallback fill Image when totalCompletionSlider is unassigned.")]
    [SerializeField] private Image totalCompletionFill;
    [SerializeField] private TMP_Text totalCompletionText;
    [Tooltip("Color applied to each character of totalCompletionText as the Slider fill passes over it.")]
    [SerializeField] private Color totalCompletionCoveredTextColor = new Color(0.078f, 0.094f, 0.141f, 1f);

    [Header("Achievement List")]
    [SerializeField] private Transform rowContainer;
    [SerializeField] private AchievementRowWidget rowPrefab;
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private readonly List<AchievementRowWidget> spawnedRows = new List<AchievementRowWidget>();
    private bool buttonsBound;
    private bool subscribed;
    private bool isAnimatingRowClaim;

    public AchievementDatabase AchievementDatabase => achievementDatabase;
    public IReadOnlyList<AchievementRowWidget> SpawnedRows => spawnedRows;

    private void Awake()
    {
        if (rowPrefab != null && rowPrefab.gameObject.scene.IsValid())
            rowPrefab.gameObject.SetActive(false);

        ConfigureNonBlockingSummarySlider();
        BindButtons();
    }

    private void OnEnable()
    {
        ConfigureNonBlockingSummarySlider();
        BindButtons();
        Subscribe();
        RefreshAll();
    }

    private void OnDisable()
    {
        isAnimatingRowClaim = false;
        Unsubscribe();
    }

    private void OnDestroy()
    {
        isAnimatingRowClaim = false;
        Unsubscribe();
        UnbindButtons();
    }

    private void ConfigureNonBlockingSummarySlider()
    {
        if (totalCompletionSlider == null)
            return;

        totalCompletionSlider.interactable = false;
        totalCompletionSlider.transition = Selectable.Transition.None;

        Graphic[] graphics = totalCompletionSlider.GetComponentsInChildren<Graphic>(true);
        for (int i = 0; i < graphics.Length; i++)
        {
            if (graphics[i] != null)
                graphics[i].raycastTarget = false;
        }
    }

    public void Show()
    {
        BindButtons();
        if (panelAnimator != null)
            panelAnimator.PlayIn();
        else
            gameObject.SetActive(true);

        RefreshAll();
    }

    public void Hide()
    {
        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(() => gameObject.SetActive(false));
        else
            gameObject.SetActive(false);
    }

    private void BindButtons()
    {
        if (buttonsBound) return;
        buttonsBound = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound) return;
        buttonsBound = false;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    private void Subscribe()
    {
        if (subscribed) return;
        subscribed = true;

        AchievementService.EnsureInitialized();
        AchievementService.OnAchievementProgressChanged += RefreshAll;
        MissionService.OnMissionProgressChanged += RefreshAll;
        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged += RefreshAll;
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged += HandleCurrencyChanged;
            EconomyManager.Instance.OnGemsChanged += HandleCurrencyChanged;
        }
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        AchievementService.OnAchievementProgressChanged -= RefreshAll;
        MissionService.OnMissionProgressChanged -= RefreshAll;
        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged -= RefreshAll;
        if (EconomyManager.Instance != null)
        {
            EconomyManager.Instance.OnCoinsChanged -= HandleCurrencyChanged;
            EconomyManager.Instance.OnGemsChanged -= HandleCurrencyChanged;
        }
    }

    private void HandleCurrencyChanged(int newBalance, int delta) => RefreshAll();

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Achievement))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Achievement);
        else
            Hide();
    }

    public void RefreshAll()
    {
        if (isAnimatingRowClaim)
            return;

        MissionService.EnsureProfileMigratedAndUpdated();
        SanitizeEllipsisOverflowModes();
        RefreshSummaryHeader();
        RefreshList();
    }

    private void SanitizeEllipsisOverflowModes()
    {
        TMP_Text[] texts = GetComponentsInChildren<TMP_Text>(true);
        for (int i = 0; i < texts.Length; i++)
        {
            if (texts[i] != null && texts[i].overflowMode == TextOverflowModes.Ellipsis)
                texts[i].overflowMode = TextOverflowModes.Truncate;
        }
    }

    public void RefreshSummaryHeader()
    {
        int totalCount = 0;
        int claimedCount = 0;

        if (achievementDatabase != null && achievementDatabase.Achievements != null)
        {
            IReadOnlyList<AchievementDefinition> all = achievementDatabase.Achievements;
            for (int i = 0; i < all.Count; i++)
            {
                AchievementDefinition def = all[i];
                if (def == null || string.IsNullOrEmpty(def.AchievementId)) continue;
                if (!AchievementService.IsEligibleForCurrentProgression(def)) continue;

                totalCount++;
                if (AchievementService.IsClaimed(def))
                    claimedCount++;
            }
        }

        int safeMax = Mathf.Max(1, totalCount);
        int clampedClaimed = Mathf.Clamp(claimedCount, 0, safeMax);
        if (totalCompletionSlider != null)
        {
            totalCompletionSlider.minValue = 0f;
            totalCompletionSlider.maxValue = safeMax;
            totalCompletionSlider.wholeNumbers = false;
            totalCompletionSlider.value = clampedClaimed;
        }
        else if (totalCompletionFill != null)
        {
            totalCompletionFill.fillAmount = Mathf.Clamp01((float)clampedClaimed / safeMax);
        }

        if (totalCompletionText != null)
        {
            totalCompletionText.text = $"{claimedCount}/{totalCount}";
            UIProgressTextCharacterColorizer.EnsureAndRefresh(
                totalCompletionText,
                totalCompletionSlider,
                totalCompletionFill,
                totalCompletionCoveredTextColor);
        }
    }

    public void RefreshList()
    {
        if (rowContainer == null || rowPrefab == null) return;

        RectTransform prefabRect = rowPrefab.transform as RectTransform;
        float templateHeight = prefabRect != null
            ? (prefabRect.rect.height > 0.01f ? prefabRect.rect.height : Mathf.Abs(prefabRect.sizeDelta.y))
            : 0f;

        if (rowPrefab.gameObject.scene.IsValid() && rowPrefab.gameObject.activeSelf)
            rowPrefab.gameObject.SetActive(false);

        for (int i = 0; i < spawnedRows.Count; i++)
        {
            if (spawnedRows[i] != null)
                Destroy(spawnedRows[i].gameObject);
        }
        spawnedRows.Clear();

        RectTransform containerRect = rowContainer as RectTransform;
        VerticalLayoutGroup layoutGroup = rowContainer.GetComponent<VerticalLayoutGroup>();
        float containerWidth = ResolveContainerWidth(containerRect);

        if (achievementDatabase != null && achievementDatabase.Achievements != null)
        {
            IReadOnlyList<AchievementDefinition> all = achievementDatabase.Achievements;
            var claimable = new List<AchievementDefinition>();
            var incomplete = new List<AchievementDefinition>();
            var claimed = new List<AchievementDefinition>();

            for (int i = 0; i < all.Count; i++)
            {
                AchievementDefinition def = all[i];
                if (def == null || string.IsNullOrEmpty(def.AchievementId)) continue;
                if (!AchievementService.IsEligibleForCurrentProgression(def)) continue;

                if (AchievementService.IsClaimed(def))
                    claimed.Add(def);
                else if (AchievementService.IsComplete(def))
                    claimable.Add(def);
                else
                    incomplete.Add(def);
            }

            SpawnRows(claimable, prefabRect, layoutGroup, containerWidth, templateHeight);
            SpawnRows(incomplete, prefabRect, layoutGroup, containerWidth, templateHeight);
            SpawnRows(claimed, prefabRect, layoutGroup, containerWidth, templateHeight);
        }

        if (containerRect != null)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            ScrollRect parentScroll = containerRect.GetComponentInParent<ScrollRect>();
            if (parentScroll != null)
                parentScroll.verticalNormalizedPosition = 1f;
        }

        if (emptyStateRoot != null && (rowPrefab == null || emptyStateRoot != rowPrefab.gameObject))
            emptyStateRoot.SetActive(spawnedRows.Count == 0);
    }

    private void SpawnRows(
        List<AchievementDefinition> list,
        RectTransform prefabRect,
        VerticalLayoutGroup layoutGroup,
        float containerWidth,
        float templateHeight)
    {
        for (int i = 0; i < list.Count; i++)
        {
            AchievementDefinition def = list[i];
            AchievementRowWidget row = Instantiate(rowPrefab, rowContainer, false);
            ConfigureSpawnedRowLayout(row, prefabRect, layoutGroup, containerWidth, templateHeight);
            row.gameObject.SetActive(true);
            row.Setup(def, OnClaimAchievementPressed);
            spawnedRows.Add(row);
        }
    }

    private static float ResolveContainerWidth(RectTransform containerRect)
    {
        if (containerRect == null)
            return 0f;

        if (containerRect.rect.width > 0.01f)
            return containerRect.rect.width;

        Canvas.ForceUpdateCanvases();
        if (containerRect.rect.width > 0.01f)
            return containerRect.rect.width;

        Transform current = containerRect.parent;
        while (current != null)
        {
            if (current is RectTransform parentRect && parentRect.rect.width > 0.01f)
                return parentRect.rect.width;
            current = current.parent;
        }

        return 0f;
    }

    private static void ConfigureSpawnedRowLayout(
        AchievementRowWidget row,
        RectTransform prefabRect,
        VerticalLayoutGroup layoutGroup,
        float containerWidth,
        float templateHeight)
    {
        if (row == null)
            return;

        RectTransform rowRect = row.transform as RectTransform;
        if (rowRect == null)
            return;

        rowRect.localScale = Vector3.one;

        if (layoutGroup == null)
            return;

        bool prefabStretchedHorizontally = prefabRect != null
            && (prefabRect.anchorMax.x - prefabRect.anchorMin.x) > 0.5f;

        float horizontalMargin = prefabStretchedHorizontally ? prefabRect.sizeDelta.x : 0f;
        float targetWidth = containerWidth > 0.01f
            ? Mathf.Max(0f, containerWidth - layoutGroup.padding.left - layoutGroup.padding.right + horizontalMargin)
            : rowRect.sizeDelta.x;
        float targetHeight = templateHeight > 0.01f ? templateHeight : rowRect.sizeDelta.y;

        Vector2 updatedSize = rowRect.sizeDelta;
        if (!layoutGroup.childControlWidth && prefabStretchedHorizontally && targetWidth > 0.01f)
            updatedSize.x = targetWidth;
        if (!layoutGroup.childControlHeight && targetHeight > 0.01f)
            updatedSize.y = targetHeight;
        rowRect.sizeDelta = updatedSize;

        LayoutElement layoutElement = row.GetComponent<LayoutElement>();
        if (layoutElement == null && (layoutGroup.childControlWidth || layoutGroup.childControlHeight))
            layoutElement = row.gameObject.AddComponent<LayoutElement>();

        if (layoutElement != null)
        {
            if (targetHeight > 0.01f && layoutElement.preferredHeight <= 0f)
                layoutElement.preferredHeight = targetHeight;
            if (prefabStretchedHorizontally && layoutElement.flexibleWidth <= 0f)
                layoutElement.flexibleWidth = 1f;
        }
    }

    private AchievementRowWidget FindSpawnedRow(AchievementDefinition achievement)
    {
        if (achievement == null) return null;
        for (int i = 0; i < spawnedRows.Count; i++)
        {
            AchievementRowWidget row = spawnedRows[i];
            if (row != null && row.AchievementDefinition == achievement)
                return row;
        }
        return null;
    }

    private void OnClaimAchievementPressed(AchievementDefinition achievement)
    {
        AchievementRowWidget claimedRow = FindSpawnedRow(achievement);

        isAnimatingRowClaim = true;
        bool claimed;
        try
        {
            claimed = AchievementService.TryClaim(achievement);
        }
        finally
        {
            isAnimatingRowClaim = false;
        }

        if (claimed)
        {
            UIAudio.Play(UISoundType.RewardClaim);
            RefreshSummaryHeader();

            if (claimedRow != null)
            {
                spawnedRows.Remove(claimedRow);
                spawnedRows.Add(claimedRow);
                claimedRow.PlayClaimAndMoveToBottomAnimation();
            }
            else
            {
                RefreshList();
            }
        }
        else
        {
            UIAudio.Play(UISoundType.ErrorOrLocked);
        }
    }
}

