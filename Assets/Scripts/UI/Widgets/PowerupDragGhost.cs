using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable floating icon shown while dragging a power. Uses a pre-wired scene Image under dragLayer.
/// </summary>
public class PowerupDragGhost
{
    private readonly Image image;

    public PowerupDragGhost(Image ghostImage)
    {
        image = ghostImage;
        if (image != null)
        {
            image.raycastTarget = false;
            if (image.TryGetComponent(out CanvasGroup cg))
                cg.blocksRaycasts = false;
            image.gameObject.SetActive(false);
        }
    }

    public void Show(Sprite icon, Vector2 screenPos, Camera cam)
    {
        if (image == null) return;
        image.sprite = icon;
        image.gameObject.SetActive(true);
        Move(screenPos, cam);
    }

    public void Move(Vector2 screenPos, Camera cam)
    {
        if (image == null) return;
        var rt = (RectTransform)image.transform;
        RectTransformUtility.ScreenPointToWorldPointInRectangle(rt, screenPos, cam, out Vector3 world);
        rt.position = world;
    }

    public void Hide()
    {
        if (image != null && image.gameObject != null)
            image.gameObject.SetActive(false);
    }

    public void Destroy()
    {
        Hide();
    }
}
