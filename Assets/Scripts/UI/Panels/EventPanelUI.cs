using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Live-Ops Events Screen Overlay (<see cref="UIPanelType.Event"/>) hosted in MainMenu's TabBar.
/// Displays scrollable event banners (image, title, description, days left) and opens the
/// banner's target UI (defaulting to <see cref="EventDetailPopupUI"/>).
/// </summary>
public class EventPanelUI : MonoBehaviour, IUIPanel
{
    [Header("Data")]
    [SerializeField] private EventDatabase eventDatabase;

    [Header("Banner Feed")]
    [SerializeField] private Transform bannerContainer;
    [SerializeField] private GameObject bannerCardPrefab;
    [SerializeField] private GameObject emptyStateRoot;

    [Header("Detail UI & Navigation")]
    [Tooltip("Optional explicit reference; falls back to UIManager.GetPanel<EventDetailPopupUI>(UIPanelType.EventDetail).")]
    [SerializeField] private EventDetailPopupUI eventDetailPopup;
    [SerializeField] private Button closeButton;

    [Header("Optional Animation (Leave Empty for No Panel Animation)")]
    [SerializeField] private UIPanelAnimator panelAnimator;

    private readonly List<EventBannerCardWidget> spawnedCards = new List<EventBannerCardWidget>();
    private readonly List<GameEventDefinition> visibleEventsScratch = new List<GameEventDefinition>();
    private bool buttonsBound;
    private bool subscribed;

    public EventDatabase EventDatabase => eventDatabase;
    public IReadOnlyList<EventBannerCardWidget> SpawnedCards => spawnedCards;

    private void Awake()
    {
        BindButtons();
    }

    private void OnEnable()
    {
        BindButtons();
        Subscribe();
        RefreshBanners();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    private void OnDestroy()
    {
        Unsubscribe();
        UnbindButtons();
    }

    public void Show()
    {
        BindButtons();
        if (panelAnimator != null)
            panelAnimator.PlayIn();
        else
            gameObject.SetActive(true);

        RefreshBanners();
    }

    public void Hide()
    {
        if (panelAnimator != null && gameObject.activeInHierarchy)
            panelAnimator.PlayOut(() => gameObject.SetActive(false));
        else
            gameObject.SetActive(false);
    }

    public void RefreshBanners()
    {
        if (bannerContainer == null || bannerCardPrefab == null)
            return;

        for (int i = 0; i < spawnedCards.Count; i++)
        {
            if (spawnedCards[i] != null)
            {
                if (Application.isPlaying)
                    Destroy(spawnedCards[i].gameObject);
                else
                    DestroyImmediate(spawnedCards[i].gameObject);
            }
        }
        spawnedCards.Clear();

        if (eventDatabase != null)
        {
            eventDatabase.GetVisibleEvents(visibleEventsScratch);
            for (int i = 0; i < visibleEventsScratch.Count; i++)
            {
                GameEventDefinition def = visibleEventsScratch[i];
                GameObject cardObj = Instantiate(bannerCardPrefab, bannerContainer, false);
                if (!cardObj.TryGetComponent(out EventBannerCardWidget card))
                {
                    Debug.LogError("[EventPanelUI] Assigned bannerCardPrefab is missing EventBannerCardWidget component.", this);
                    if (Application.isPlaying)
                        Destroy(cardObj);
                    else
                        DestroyImmediate(cardObj);
                    break;
                }

                cardObj.transform.localScale = Vector3.one;
                cardObj.SetActive(true);
                card.Setup(def, HandleBannerClicked);
                spawnedCards.Add(card);
            }
        }

        if (bannerContainer is RectTransform containerRect)
        {
            LayoutRebuilder.ForceRebuildLayoutImmediate(containerRect);
            ScrollRect scroll = containerRect.GetComponentInParent<ScrollRect>();
            if (scroll != null)
                scroll.verticalNormalizedPosition = 1f;
        }

        if (emptyStateRoot != null && emptyStateRoot != bannerCardPrefab)
            emptyStateRoot.SetActive(spawnedCards.Count == 0);
    }

    private void HandleBannerClicked(EventBannerCardWidget card)
    {
        if (card == null || card.EventDefinition == null)
            return;

        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (card.OptionalCustomTargetPanel != null)
        {
            card.OptionalCustomTargetPanel.SetActive(true);
            return;
        }

        GameEventDefinition def = card.EventDefinition;
        if (def.BannerAction == EventBannerActionType.OpenEventDetailPopup)
        {
            EventDetailPopupUI popup = eventDetailPopup != null
                ? eventDetailPopup
                : UIManager.Instance?.GetPanel<EventDetailPopupUI>(UIPanelType.EventDetail);

            if (popup != null)
                popup.BindEvent(def);

            if (UIManager.Instance != null && UIManager.Instance.IsPanelRegistered(UIPanelType.EventDetail))
                UIManager.Instance.PushPopup(UIPanelType.EventDetail);
            else if (popup != null)
                popup.Show();

            return;
        }

        EventDetailPopupUI.ExecuteEventAction(def.BannerAction, def.CtaTargetPanel, def.CtaRunnerMode);
    }

    private void OnCloseClicked()
    {
        GameHaptics.Play(HapticEvent.UISelect);
        UIAudio.Play(UISoundType.ButtonClick);

        if (UIManager.Instance != null && UIManager.Instance.IsPanelOpen(UIPanelType.Event))
            UIManager.Instance.ClosePanelIfOpen(UIPanelType.Event);
        else
            Hide();
    }

    private void BindButtons()
    {
        if (buttonsBound)
            return;
        buttonsBound = true;

        if (closeButton != null)
            closeButton.onClick.AddListener(OnCloseClicked);
    }

    private void UnbindButtons()
    {
        if (!buttonsBound)
            return;
        buttonsBound = false;

        if (closeButton != null)
            closeButton.onClick.RemoveListener(OnCloseClicked);
    }

    private void Subscribe()
    {
        if (subscribed)
            return;
        subscribed = true;

        EventService.EnsureInitialized();
        EventService.OnEventStateChanged += RefreshBanners;
    }

    private void Unsubscribe()
    {
        if (!subscribed)
            return;
        subscribed = false;

        EventService.OnEventStateChanged -= RefreshBanners;
    }
}
