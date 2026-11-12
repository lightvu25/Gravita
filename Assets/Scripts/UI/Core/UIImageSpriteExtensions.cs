using UnityEngine;
using UnityEngine.UI;

public static class UIImageSpriteExtensions
{
    public static void SetSpriteNative(this Image image, Sprite sprite)
    {
        if (image == null)
            return;

        image.sprite = sprite;
        if (sprite != null)
        {
            image.SetNativeSize();
        }
    }
}
