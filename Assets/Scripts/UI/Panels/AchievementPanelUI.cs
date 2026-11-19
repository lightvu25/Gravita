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
    [SerializeField] private GameObject rowPrefab;
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private readonly List<AchievementRowWidget> spawnedRows = new List<AchievementRowWidget>();
    private readonly List<AchievementRowWidget> rowPool = new List<AchievementRowWidget>();
    private bool buttonsBound;
    private bool subscribed;
    private bool isAnimatingRowClaim;

    public AchievementDatabase AchievementDatabase => achievementDatabase;
    public IReadOnlyList<AchievementRowWidget> SpawnedRows => spawnedRows;

    private void Awake()
    {
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
    }

    private void Unsubscribe()
    {
        if (!subscribed) return;
        subscribed = false;

        AchievementService.OnAchievementProgressChanged -= RefreshAll;
        MissionService.OnMissionProgressChanged -= RefreshAll;
        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged -= RefreshAll;
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null)
        {
            if (UIManager.Instance.CurrentScreen == UIPanelType.Achievement)
                UIManager.Instance.ShowScreen(UIPanelType.Map);
            else if (UIManager.Instance.IsPanelOpen(UIPanelType.Achievement))
                UIManager.Instance.ClosePanelIfOpen(UIPanelType.Achievement);
            else
                Hide();
        }
        else
        {
            Hide();
        }
    }

    public void RefreshAll()
    {
        if (isAnimatingRowClaim)
            return;

        MissionService.EnsureProfileMigratedAndUpdated();
        RefreshSummaryHeader();
        RefreshList();
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

        spawnedRows.Clear();
        for (int i = rowPool.Count - 1; i >= 0; i--)
        {
            if (rowPool[i] == null)
                rowPool.RemoveAt(i);
            else
                rowPool[i].gameObject.SetActive(false);
        }

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

            SpawnRows(claimable);
            SpawnRows(incomplete);
            SpawnRows(claimed);
        }

        if (rowContainer is RectTransform containerRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            ScrollRect parentScroll = containerRect.GetComponentInParent<ScrollRect>();
            if (parentScroll != null)
                parentScroll.verticalNormalizedPosition = 1f;
        }

        if (emptyStateRoot != null && emptyStateRoot != rowPrefab)
            emptyStateRoot.SetActive(spawnedRows.Count == 0);
    }

    private void SpawnRows(List<AchievementDefinition> list)
    {
        for (int i = 0; i < list.Count; i++)
        {
            int poolIndex = spawnedRows.Count;
            AchievementRowWidget row = GetOrCreatePooledRow(poolIndex);
            if (row == null)
                return;

            row.transform.SetSiblingIndex(poolIndex);
            row.transform.localScale = Vector3.one;
            row.gameObject.SetActive(true);
            row.Setup(list[i], OnClaimAchievementPressed);
            spawnedRows.Add(row);
        }
    }

    private AchievementRowWidget GetOrCreatePooledRow(int poolIndex)
    {
        if (poolIndex < rowPool.Count && rowPool[poolIndex] != null)
            return rowPool[poolIndex];

        GameObject rowObj = Instantiate(rowPrefab, rowContainer, false);
        if (!rowObj.TryGetComponent(out AchievementRowWidget row))
        {
            Debug.LogError("[AchievementPanelUI] Assigned rowPrefab is missing AchievementRowWidget component.", this);
            if (Application.isPlaying)
                Destroy(rowObj);
            else
                DestroyImmediate(rowObj);
            return null;
        }

        if (poolIndex < rowPool.Count)
            rowPool[poolIndex] = row;
        else
            rowPool.Add(row);

        return row;
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

