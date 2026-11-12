using System.Collections;
using UnityEngine;

public class ReturnToPool : MonoBehaviour
{
    [Tooltip("If true, it will wait for 'time' seconds instead of detecting animation/particle end.")]
    [SerializeField] private bool useFixedDelay = false;
    [SerializeField] private float time = 1f;

    private Animator animator;
    private ParticleSystem[] particleSystems;
    private Coroutine routine;
    private bool isReturned;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>(true);
        particleSystems = GetComponentsInChildren<ParticleSystem>(true);
    }

    private void OnEnable()
    {
        isReturned = false;

        if (particleSystems != null && particleSystems.Length > 0)
        {
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps == null) continue;
                ps.Clear(false);
                ps.Play(false);
            }
        }

        if (useFixedDelay)
        {
            routine = StartCoroutine(Routine(time));
        }
        else if (animator == null && (particleSystems == null || particleSystems.Length == 0))
        {
            // Sprite-only VFX still need a deterministic lifetime.
            routine = StartCoroutine(Routine(time));
        }
    }

    public void ConfigureDelay(float delay)
    {
        useFixedDelay = true;
        time = Mathf.Max(0.05f, delay);
        if (!isActiveAndEnabled) return;
        if (routine != null) StopCoroutine(routine);
        routine = StartCoroutine(Routine(time));
    }

    private void Update()
    {
        if (isReturned || useFixedDelay) return;

        if (animator != null && animator.runtimeAnimatorController != null)
        {
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
            if (!stateInfo.loop && stateInfo.normalizedTime >= 1f && !animator.IsInTransition(0))
            {
                ReturnNow();
            }
        }
        else if (animator != null && animator.runtimeAnimatorController == null)
        {
            useFixedDelay = true;
            routine = StartCoroutine(Routine(time));
        }
        else if (particleSystems != null && particleSystems.Length > 0)
        {
            bool anyAlive = false;
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem ps = particleSystems[i];
                if (ps != null && ps.IsAlive(false))
                {
                    anyAlive = true;
                    break;
                }
            }

            if (!anyAlive)
            {
                ReturnNow();
            }
        }
    }

    private void OnDisable()
    {
        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
    }

    private IEnumerator Routine(float delay)
    {
        yield return new WaitForSeconds(delay);
        ReturnNow();
    }

    private void ReturnNow()
    {
        if (isReturned) return;
        isReturned = true;
        ObjectPoolManager.ReturnObjectToPool(gameObject);
    }
}
