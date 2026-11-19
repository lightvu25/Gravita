using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Single Event Banner card inside <see cref="EventPanelUI"/>.
/// Displays Banner artwork, Title, short Description, Days Left countdown, and Claimable badge.
/// </summary>
public class EventBannerCardWidget : MonoBehaviour
{
    [Header("Card Visuals")]
    [SerializeField] private Image bannerImage;
    [SerializeField] private TMP_Text titleText;
    [SerializeField] private TMP_Text descriptionText;
    [SerializeField] private TMP_Text daysLeftText;
    [SerializeField] private GameObject claimableBadge;
    [SerializeField] private Button bannerButton;

    [Header("Optional Custom Destination Override")]
    [Tooltip("Optional custom GameObject panel opened directly when this card is clicked (overrides EventDetailPopup).")]
    [SerializeField] private GameObject optionalCustomTargetPanel;

    private GameEventDefinition eventDefinition;
    private Action<EventBannerCardWidget> onClicked;
    private bool buttonBound;

    public GameEventDefinition EventDefinition => eventDefinition;
    public GameObject OptionalCustomTargetPanel => optionalCustomTargetPanel;
    public Button BannerButton => bannerButton;

    private void Awake()
    {
        EnsureButtonBound();
    }

    private void OnDestroy()
    {
        if (buttonBound && bannerButton != null)
        {
            bannerButton.onClick.RemoveListener(HandleCardClicked);
            buttonBound = false;
        }
    }

    public void Setup(GameEventDefinition definition, Action<EventBannerCardWidget> clickCallback)
    {
        eventDefinition = definition;
        onClicked = clickCallback;
        EnsureButtonBound();
        RefreshState();
    }

    public void RefreshState()
    {
        if (eventDefinition == null)
            return;

        if (bannerImage != null && eventDefinition.BannerSprite != null)
        {
            bannerImage.sprite = eventDefinition.BannerSprite;
            bannerImage.color = Color.white;
        }

        if (titleText != null)
            titleText.text = eventDefinition.Title;

        if (descriptionText != null)
            descriptionText.text = eventDefinition.BannerDescription;

        if (daysLeftText != null)
            daysLeftText.text = EventService.FormatRemainingTime(eventDefinition);

        if (claimableBadge != null)
            claimableBadge.SetActive(EventService.HasAnyClaimableTask(eventDefinition));
    }

    private void EnsureButtonBound()
    {
        if (bannerButton == null)
            bannerButton = GetComponent<Button>();

        if (buttonBound || bannerButton == null)
            return;

        bannerButton.onClick.AddListener(HandleCardClicked);
        buttonBound = true;
    }

    private void HandleCardClicked()
    {
        onClicked?.Invoke(this);
    }
}
