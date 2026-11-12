using DG.Tweening;
using TMPro;
using UnityEngine;

[RequireComponent(typeof(TMP_Text))]
public class CurrencyUI : MonoBehaviour
{
    [SerializeField] private AudioClip tickSfx;
    [SerializeField, Range(0f, 1f)] private float sfxVolume = 0.7f;
    [SerializeField, Min(0.05f)] private float duration = 0.45f;
    [SerializeField, Min(0.025f)] private float minTickInterval = 0.055f;
    [SerializeField] private string prefix = string.Empty;

    private TMP_Text text;
    private Tween tween;
    private int displayed;
    private bool initialized;
    private float lastTickTime;

    public int Value => displayed;
    public bool IsAnimating => tween != null && tween.IsActive();

    private TMP_Text Text => text != null ? text : (text = GetComponent<TMP_Text>());
    private void OnDisable() => KillTween();

    public void Snap(int value)
    {
        KillTween();
        displayed = value;
        initialized = true;
        Write(value);
    }

    public void SetValue(int target, bool animate = true)
    {
        if (!initialized || !animate || !Application.isPlaying || !gameObject.activeInHierarchy)
        {
            Snap(target);
            return;
        }
        if (displayed == target) { Write(target); return; }
        Run(displayed, target);
    }

    public void AnimateFromTo(int from, int target)
    {
        initialized = true;
        if (!Application.isPlaying || !gameObject.activeInHierarchy || from == target)
        {
            Snap(target);
            return;
        }
        Run(from, target);
    }

    private void Run(int from, int target)
    {
        KillTween();
        displayed = from;
        Write(from);
        int lastStep = from;
        float interval = Mathf.Max(0.025f, minTickInterval);
        tween = DOTween.To(() => displayed, v =>
            {
                displayed = v;
                Write(v);
                if (v != lastStep)
                {
                    lastStep = v;
                    if (Time.unscaledTime - lastTickTime >= interval)
                    {
                        lastTickTime = Time.unscaledTime;
                        PlayTick();
                    }
                }
            }, target, Mathf.Max(0.05f, duration))
            .SetEase(Ease.OutQuad)
            .SetUpdate(true)
            .SetLink(gameObject)
            .OnComplete(() => { displayed = target; Write(target); tween = null; });
    }

    private void PlayTick()
    {
        if (tickSfx != null && SoundManager.Instance != null)
            SoundManager.Instance.PlaySFX(tickSfx, sfxVolume, randomPitch: true);
        else
            UIAudio.Play(UISoundType.CurrencyCountTick);
    }

    private void Write(int v) => Text.text = string.IsNullOrEmpty(prefix) ? v.ToString() : prefix + v;

    private void KillTween()
    {
        if (tween != null) { tween.Kill(); tween = null; }
    }

    // ── Static helpers: panels keep their TMP_Text fields ──
    public static void Set(TMP_Text target, int value, bool animate = true)
    {
        if (target == null) return;
        if (target.TryGetComponent(out CurrencyUI c)) c.SetValue(value, animate);
        else target.text = value.ToString();
    }

    public static void Animate(TMP_Text target, int from, int to)
    {
        if (target == null) return;
        if (target.TryGetComponent(out CurrencyUI c)) c.AnimateFromTo(from, to);
        else target.text = to.ToString();
    }
}
