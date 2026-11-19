using System;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;

public class LevelNodeWidget : MonoBehaviour
{
    [Header("UI Elements")]
    [SerializeField] private TMP_Text levelTitleText;
    [SerializeField] private Button selectButton;
    [SerializeField] private GameObject lockOverlay;
    [FormerlySerializedAs("star")]
    [SerializeField] private Image[] stars;
    [SerializeField] private Sprite starEmpty;
    [SerializeField] private Sprite starFilled;

    private LevelData levelData;
    private Action<LevelData> onSelectedCallback;
    private bool subscribedToProgression;
    private bool buttonBound;

    public LevelData LevelData => levelData;
    public int DisplayedStars { get; private set; }

    private void OnEnable()
    {
        SubscribeToProgression();
        if (levelData != null)
            Refresh();
    }

    private void OnDisable()
    {
        UnsubscribeFromProgression();
    }

    private void OnDestroy()
    {
        UnsubscribeFromProgression();
        if (buttonBound && selectButton != null)
        {
            selectButton.onClick.RemoveListener(HandleClick);
            buttonBound = false;
        }
    }

    private void SubscribeToProgression()
    {
        if (subscribedToProgression || ProgressionManager.Instance == null) return;
        ProgressionManager.Instance.OnProgressionChanged += Refresh;
        subscribedToProgression = true;
    }

    private void UnsubscribeFromProgression()
    {
        if (!subscribedToProgression) return;
        if (ProgressionManager.Instance != null)
            ProgressionManager.Instance.OnProgressionChanged -= Refresh;
        subscribedToProgression = false;
    }

    public void Setup(LevelData data, Action<LevelData> onSelected)
    {
        levelData = data;
        onSelectedCallback = onSelected;

        if (selectButton != null && !buttonBound)
        {
            selectButton.onClick.AddListener(HandleClick);
            buttonBound = true;
        }

        SubscribeToProgression();
        Refresh();
    }

    public void SetStars(int earnedStars)
    {
        earnedStars = Mathf.Clamp(earnedStars, 0, 3);
        DisplayedStars = earnedStars;

        if (stars == null) return;

        for (int i = 0; i < stars.Length; i++)
        {
            if (stars[i] == null) continue;
            Sprite targetSprite = i < earnedStars ? starFilled : starEmpty;
            if (targetSprite != null)
                stars[i].sprite = targetSprite;
        }
    }

    public void Refresh()
    {
        if (levelData == null)
        {
            SetStars(0);
            return;
        }

        if (levelTitleText != null)
        {
            var db = ProgressionManager.Instance != null ? ProgressionManager.Instance.Database : null;
            int levelNumber = db != null ? db.GetLevelNumber(levelData) : 0;
            levelTitleText.text = levelNumber > 0 ? levelNumber.ToString() : "";
        }

        ProgressionManager progression = ProgressionManager.Instance;
        bool isUnlocked = progression == null || progression.IsLevelUnlocked(levelData);
        bool isCompleted = progression != null && progression.IsLevelCompleted(levelData);
        int earnedStars = (isCompleted && progression != null)
            ? progression.GetLevelStars(levelData.LevelId)
            : 0;

        if (selectButton != null)
        {
            selectButton.interactable = isUnlocked;
        }

        if (lockOverlay != null)
        {
            lockOverlay.SetActive(!isUnlocked);
        }

        SetStars(earnedStars);
    }

    private void HandleClick()
    {
        if (levelData != null)
        {
            GameHaptics.Play(HapticEvent.UISelect);
            UIAudio.Play(UISoundType.ButtonClick);
            onSelectedCallback?.Invoke(levelData);
        }
    }
}
