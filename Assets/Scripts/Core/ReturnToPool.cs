using UnityEngine;
using System.Collections;

public class ReturnToPool : MonoBehaviour
{
    [Tooltip("If true, it will wait for 'time' seconds instead of detecting animation end.")]
    [SerializeField] private bool useFixedDelay = false;
    [SerializeField] private float time = 1f;

    private Animator animator;
    private ParticleSystem particleSys;
    private Coroutine routine;

    private void Awake()
    {
        animator = GetComponentInChildren<Animator>();
        particleSys = GetComponentInChildren<ParticleSystem>();
    }

    private void OnEnable()
    {
        if (useFixedDelay)
        {
            routine = StartCoroutine(Routine(time));
        }
        else if (animator == null && particleSys != null)
        {
            // Fallback for particles if we just want to rely on ParticleSystem duration
            routine = StartCoroutine(Routine(particleSys.main.duration));
        }
        else if (animator == null)
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
        if (!useFixedDelay)
        {
            if (animator != null && animator.runtimeAnimatorController != null)
            {
                AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(0);
                // Return to pool when the animation finishes playing
                if (stateInfo.normalizedTime >= 1f && !animator.IsInTransition(0))
                {
                    ObjectPoolManager.ReturnObjectToPool(gameObject);
                }
            }
            else if (animator != null && animator.runtimeAnimatorController == null)
            {
                // If there's an Animator but no controller, it will never finish. Fallback to fixed delay.
                useFixedDelay = true;
                routine = StartCoroutine(Routine(time));
            }
            else if (particleSys != null)
            {
                if (!particleSys.IsAlive(true))
                {
                    ObjectPoolManager.ReturnObjectToPool(gameObject);
                }
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
        ObjectPoolManager.ReturnObjectToPool(gameObject);
    }
}
