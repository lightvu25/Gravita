using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Reusable floating icon shown while dragging a power. Lives under the panel's dragLayer.
/// </summary>
public class PowerupDragGhost
{
    private readonly Image image;

    public PowerupDragGhost(RectTransform layer)
    {
        var go = new GameObject("PowerupDragGhost", typeof(RectTransform), typeof(CanvasGroup), typeof(Image));
        if (layer != null)
            go.transform.SetParent(layer, false);
        go.GetComponent<CanvasGroup>().blocksRaycasts = false;   // so drop targets receive the drop
        image = go.GetComponent<Image>();
        image.raycastTarget = false;
        go.SetActive(false);
    }

    public void Show(Sprite icon, Vector2 screenPos, Camera cam)
    {
        if (image == null) return;
        image.SetSpriteNative(icon);
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
        if (image != null && image.gameObject != null)
            Object.Destroy(image.gameObject);
    }
}
