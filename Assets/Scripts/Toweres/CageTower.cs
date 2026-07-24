using System.Collections.Generic;
using UnityEngine;

public class CageTower : MonoBehaviour
{
    public enum CageState
    {
        Empty,
        Full,
        Broken
    }

    [SerializeField] private Sprite brokenSprite;
    [SerializeField, Min(0.1f)] private float captureRadius = 1.25f;
    [SerializeField] private CageState state = CageState.Empty;
    [SerializeField] private AudioClip captureSfx;
    [SerializeField] private AudioClip breakSfx;
    [SerializeField] private WaveSpawner waveSpawner;

    private readonly List<MonoBehaviour> disabledEnemyScripts = new List<MonoBehaviour>();
    private readonly List<Collider2D> disabledEnemyColliders = new List<Collider2D>();
    private SpriteRenderer cageRenderer;
    private Sprite intactSprite;
    private GameObject capturedEnemy;
    private Rigidbody2D capturedBody;
    private RigidbodyType2D originalBodyType;
    private float originalGravityScale;
    private RigidbodyConstraints2D originalConstraints;

    public GameObject CapturedEnemy => capturedEnemy;
    public CageState State => state;
    public bool IsBroken => state == CageState.Broken;

    public void Configure(
        Sprite newBrokenSprite,
        float newCaptureRadius,
        AudioClip newCaptureSfx = null,
        AudioClip newBreakSfx = null)
    {
        brokenSprite = newBrokenSprite;
        captureRadius = Mathf.Max(0.1f, newCaptureRadius);
        captureSfx = newCaptureSfx;
        breakSfx = newBreakSfx;

        cageRenderer = GetComponent<SpriteRenderer>();
        intactSprite = cageRenderer != null ? cageRenderer.sprite : null;

        CircleCollider2D captureTrigger = GetComponent<CircleCollider2D>();
        if (captureTrigger == null)
        {
            captureTrigger = gameObject.AddComponent<CircleCollider2D>();
        }

        captureTrigger.isTrigger = true;
        captureTrigger.radius = captureRadius;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (state != CageState.Empty)
        {
            return;
        }

        GameObject enemy = FindTaggedEnemy(other);
        if (enemy != null && enemy.GetComponent<Enemy>().isCagable)
        {
            Capture(enemy);
        }
    }

    private void Capture(GameObject enemy)
    {
        capturedEnemy = enemy;
        state = CageState.Full;
        disabledEnemyScripts.Clear();
        disabledEnemyColliders.Clear();
        GetWaveSpawner()?.RemoveLivingEnemy(enemy);

        foreach (MonoBehaviour behaviour in enemy.GetComponentsInChildren<MonoBehaviour>())
        {
            if (behaviour.enabled)
            {
                behaviour.enabled = false;
                disabledEnemyScripts.Add(behaviour);
            }
        }

        foreach (Collider2D enemyCollider in enemy.GetComponentsInChildren<Collider2D>())
        {
            if (enemyCollider.enabled)
            {
                enemyCollider.enabled = false;
                disabledEnemyColliders.Add(enemyCollider);
            }
        }

        capturedBody = enemy.GetComponentInParent<Rigidbody2D>();
        if (capturedBody != null)
        {
            originalBodyType = capturedBody.bodyType;
            originalGravityScale = capturedBody.gravityScale;
            originalConstraints = capturedBody.constraints;
            capturedBody.linearVelocity = Vector2.zero;
            capturedBody.angularVelocity = 0f;
            capturedBody.bodyType = RigidbodyType2D.Kinematic;
            capturedBody.constraints = RigidbodyConstraints2D.FreezeAll;
            capturedBody.position = transform.position;
        }
        else
        {
            enemy.transform.position = transform.position;
        }

        SetEnemySorting(enemy, "Towers");
        PlaySfx(captureSfx);
    }

    public void ReleaseEnemy()
    {
        if (capturedEnemy == null)
        {
            return;
        }

        GameObject enemy = capturedEnemy;

        if (capturedBody != null)
        {
            capturedBody.constraints = originalConstraints;
            capturedBody.bodyType = originalBodyType;
            capturedBody.gravityScale = originalGravityScale;
            capturedBody.position = (Vector2)transform.position
                + (Vector2)transform.right * (captureRadius + 0.6f);
        }
        else
        {
            enemy.transform.position = transform.position
                + transform.right * (captureRadius + 0.6f);
        }

        foreach (Collider2D enemyCollider in disabledEnemyColliders)
        {
            if (enemyCollider != null)
            {
                enemyCollider.enabled = true;
            }
        }

        foreach (MonoBehaviour behaviour in disabledEnemyScripts)
        {
            if (behaviour != null)
            {
                behaviour.enabled = true;
            }
        }

        GetWaveSpawner()?.AddLivingEnemy(enemy);
        SetEnemySorting(enemy, "Enemy");
        disabledEnemyColliders.Clear();
        disabledEnemyScripts.Clear();
        capturedEnemy = null;
        capturedBody = null;
        SetBroken(true);
        PlaySfx(breakSfx);
    }

    public void FixCage()
    {
        if (capturedEnemy != null)
        {
            return;
        }

        SetBroken(false);
    }

    private void SetBroken(bool broken)
    {
        state = broken ? CageState.Broken : CageState.Empty;
        if (cageRenderer != null)
        {
            cageRenderer.sprite = broken && brokenSprite != null ? brokenSprite : intactSprite;
        }
    }

    private WaveSpawner GetWaveSpawner()
    {
        if (waveSpawner == null)
        {
            waveSpawner = FindFirstObjectByType<WaveSpawner>();
        }

        return waveSpawner;
    }

    private static GameObject FindTaggedEnemy(Collider2D other)
    {
        Transform current = other.attachedRigidbody != null
            ? other.attachedRigidbody.transform
            : other.transform;

        while (current != null)
        {
            if (current.CompareTag("Enemy"))
            {
                return current.gameObject;
            }

            current = current.parent;
        }

        return null;
    }

    private static void SetEnemySorting(GameObject enemy, string sortingLayer)
    {
        foreach (Renderer renderer in enemy.GetComponentsInChildren<Renderer>())
        {
            renderer.sortingLayerName = sortingLayer;
            renderer.sortingOrder = 0;
        }
    }

    private static void PlaySfx(AudioClip clip)
    {
        if (clip != null)
        {
            AudioController.Play(clip);
        }
    }
}
