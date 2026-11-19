using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;

/// <summary>
/// Pooled lightweight procedural VFX helper for <see cref="PowerUpController"/>.
/// Avoids runtime Texture2D/GameObject allocation churn (L2 / L11).
/// </summary>
public sealed class PowerUpVfxPool
{
    private static Sprite cachedRingSprite;
    private static Sprite cachedBoxSprite;

    private readonly Queue<SpriteRenderer> pulsePool = new Queue<SpriteRenderer>(8);
    private readonly Queue<SpriteRenderer> boxPool = new Queue<SpriteRenderer>(8);
    private readonly List<SpriteRenderer> activeItems = new List<SpriteRenderer>(16);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStaticSprites()
    {
        cachedRingSprite = null;
        cachedBoxSprite = null;
    }

    public static Sprite GetOrCreateRingSprite()
    {
        if (cachedRingSprite != null) return cachedRingSprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                bool isRing = dist >= 11.5f && dist <= 15.2f;
                tex.SetPixel(x, y, isRing ? Color.white : Color.clear);
            }
        }
        tex.Apply();
        cachedRingSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedRingSprite;
    }

    public static Sprite GetOrCreateBoxSprite()
    {
        if (cachedBoxSprite != null) return cachedBoxSprite;

        const int size = 32;
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                bool isBorder = x < 3 || x >= size - 3 || y < 3 || y >= size - 3;
                Color c = isBorder ? Color.white : new Color(1f, 1f, 1f, 0.25f);
                tex.SetPixel(x, y, c);
            }
        }
        tex.Apply();
        cachedBoxSprite = Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 32f);
        return cachedBoxSprite;
    }

    public void SpawnRadialPulse(Vector3 centerPos, Color color, float targetScale, float duration)
    {
        if (!Application.isPlaying)
            return;

        SpriteRenderer sr = Rent(pulsePool, "SkillPulseVFX", GetOrCreateRingSprite());
        GameObject go = sr.gameObject;
        go.transform.position = new Vector3(centerPos.x, centerPos.y, 0f);
        go.transform.localScale = Vector3.one * 0.35f;
        sr.color = color;

        go.transform.DOKill();
        sr.DOKill();
        Sequence seq = DOTween.Sequence().SetUpdate(true).SetLink(go);
        seq.Join(go.transform.DOScale(Vector3.one * targetScale, duration).SetEase(Ease.OutCubic));
        seq.Join(sr.DOFade(0f, duration).SetEase(Ease.InQuad));
        seq.OnComplete(() => ReturnToPool(sr, pulsePool));
    }

    public void SpawnBoxPulse(Vector3 cellCenter, Color color, bool succeeded)
    {
        if (!Application.isPlaying)
            return;

        SpriteRenderer sr = Rent(boxPool, "SkillCellVFX", GetOrCreateBoxSprite());
        GameObject go = sr.gameObject;
        go.transform.position = new Vector3(cellCenter.x, cellCenter.y, 0f);
        go.transform.localScale = Vector3.one * (succeeded ? 0.5f : 0.85f);
        sr.color = color;

        go.transform.DOKill();
        sr.DOKill();
        Sequence seq = DOTween.Sequence().SetUpdate(true).SetLink(go);
        if (succeeded)
        {
            seq.Join(go.transform.DOScale(Vector3.one * 1.25f, 0.28f).SetEase(Ease.OutBack));
            seq.Join(sr.DOFade(0f, 0.28f).SetEase(Ease.InQuad));
        }
        else
        {
            seq.Join(go.transform.DOShakePosition(0.24f, new Vector3(0.08f, 0.08f, 0f), 20, 90f, false, true));
            seq.Join(sr.DOFade(0f, 0.28f).SetEase(Ease.InQuad));
        }
        seq.OnComplete(() => ReturnToPool(sr, boxPool));
    }

    public void DisposeAll()
    {
        for (int i = activeItems.Count - 1; i >= 0; i--)
        {
            SpriteRenderer sr = activeItems[i];
            if (sr != null)
            {
                sr.transform.DOKill();
                sr.DOKill();
                Object.Destroy(sr.gameObject);
            }
        }
        activeItems.Clear();

        while (pulsePool.Count > 0)
        {
            SpriteRenderer sr = pulsePool.Dequeue();
            if (sr != null)
                Object.Destroy(sr.gameObject);
        }

        while (boxPool.Count > 0)
        {
            SpriteRenderer sr = boxPool.Dequeue();
            if (sr != null)
                Object.Destroy(sr.gameObject);
        }
    }

    private SpriteRenderer Rent(Queue<SpriteRenderer> pool, string name, Sprite sprite)
    {
        while (pool.Count > 0)
        {
            SpriteRenderer pooled = pool.Dequeue();
            if (pooled != null)
            {
                pooled.sprite = sprite;
                pooled.gameObject.SetActive(true);
                if (!activeItems.Contains(pooled))
                    activeItems.Add(pooled);
                return pooled;
            }
        }

        var go = new GameObject(name);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.sortingOrder = 25;
        activeItems.Add(sr);
        return sr;
    }

    private void ReturnToPool(SpriteRenderer sr, Queue<SpriteRenderer> pool)
    {
        if (sr == null)
            return;

        activeItems.Remove(sr);
        sr.gameObject.SetActive(false);
        pool.Enqueue(sr);
    }
}
