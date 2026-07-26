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

    [Tooltip("Art shown while the cage is whole. Left empty, the renderer's own sprite is used.")]
    [SerializeField] private Sprite intactSprite;
    [Tooltip("Art shown once the cage is broken. Left empty, the cage keeps its intact art.")]
    [SerializeField] private Sprite brokenSprite;
    [SerializeField, Min(0.1f)] private float captureRadius = 0.75f;
    [SerializeField] private CageState state = CageState.Empty;
    [SerializeField] private AudioClip captureSfx;
    [SerializeField] private AudioClip breakSfx;
    [SerializeField] private WaveSpawner waveSpawner;

    private readonly List<MonoBehaviour> disabledEnemyScripts = new List<MonoBehaviour>();
    private readonly List<Collider2D> disabledEnemyColliders = new List<Collider2D>();
    private SpriteRenderer cageRenderer;
    private GameObject capturedEnemy;
    private Rigidbody2D capturedBody;
    private RigidbodyType2D originalBodyType;
    private float originalGravityScale;
    private RigidbodyConstraints2D originalConstraints;

    public GameObject CapturedEnemy => capturedEnemy;
    public CageState State => state;
    public bool IsBroken => state == CageState.Broken;

    private void Awake()
    {
        // A cage spawned from a prefab never goes through Configure, so the sprite
        // and the capture trigger have to be resolved here instead.
        CacheRenderer();
        RefreshSprite();
        EnsureCaptureTrigger();
    }

    /// <summary>Remembers the resting sprite so a repaired cage can be put back to it.</summary>
    private void CacheRenderer()
    {
        if (cageRenderer == null)
        {
            cageRenderer = GetComponent<SpriteRenderer>();
        }

        // A cage authored as already broken must not record its broken art as the intact art.
        if (intactSprite == null && cageRenderer != null && state != CageState.Broken)
        {
            intactSprite = cageRenderer.sprite;
        }
    }

    private void EnsureCaptureTrigger()
    {
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
        if (enemy != null
            && enemy.TryGetComponent(out Enemy enemyComponent)
            // Neighboring cages' capture circles overlap, so one enemy can trip two
            // triggers in the same physics step. Capture disables the enemy's scripts,
            // so a disabled Enemy here is one another cage has already taken.
            && enemyComponent.isActiveAndEnabled
            && enemyComponent.CanBeCaged)
        {
            Capture(enemy, true);
        }
    }

    /// <summary>
    /// Puts the cage back to <paramref name="restoredState"/>, taking
    /// <paramref name="captive"/> in when that state is Full. For a round retry replaying
    /// the round from the state it opened in.
    /// <para>
    /// Whatever is caged right now is discarded rather than released: a retry clears the
    /// field anyway, so a bird set loose here would only have to be swept up again. The
    /// restored capture is silent for the same reason - a retry would otherwise fire one
    /// capture sound per cage at once.
    /// </para>
    /// </summary>
    public void RestoreState(CageState restoredState, GameObject captive)
    {
        DiscardCaptive();

        // Full is reached by capturing, not by assignment, so the cage is put in the
        // state a capture expects to find it in and the capture does the rest.
        state = restoredState == CageState.Full ? CageState.Empty : restoredState;
        RefreshSprite();

        if (restoredState == CageState.Full && captive != null)
        {
            Capture(captive, false);
        }
    }

    /// <summary>
    /// Takes the captive out of the cage and off the field in one step, undoing what
    /// <see cref="Capture"/> did to it so the pool gets a usable object back rather than
    /// one still holding disabled scripts and a frozen body.
    /// </summary>
    private void DiscardCaptive()
    {
        if (capturedEnemy == null)
        {
            return;
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

        disabledEnemyColliders.Clear();
        disabledEnemyScripts.Clear();

        if (capturedBody != null)
        {
            capturedBody.constraints = originalConstraints;
            capturedBody.bodyType = originalBodyType;
            capturedBody.gravityScale = originalGravityScale;
        }

        // Sorting was moved to the tower layer on capture, and the pool does not reset
        // it, so a recycled bird would stay drawn among the towers without this.
        SetEnemySorting(capturedEnemy, "Enemy");
        capturedEnemy.GetComponent<Enemy>()?.Despawn();
        capturedEnemy = null;
        capturedBody = null;
    }

    private void Capture(GameObject enemy, bool announce)
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

        if (announce)
        {
            PlaySfx(captureSfx);
            FirstCaptureCinematic.TryPlay(this);
        }
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

        enemy.GetComponent<Enemy>()?.OnReleasedFromCage();
        GetWaveSpawner()?.AddLivingEnemy(enemy);
        SetEnemySorting(enemy, "Enemy");
        disabledEnemyColliders.Clear();
        disabledEnemyScripts.Clear();
        capturedEnemy = null;
        capturedBody = null;
        SetBroken(true);
        PlaySfx(breakSfx);
    }

    public void BreakCage()
    {
        if (capturedEnemy != null)
        {
            ReleaseEnemy();
            return;
        }

        if (!IsBroken)
        {
            SetBroken(true);
            PlaySfx(breakSfx);
        }
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
        RefreshSprite();
    }

    /// <summary>
    /// Puts the renderer on the art matching the current state. A sprite left unassigned
    /// means "keep what is already showing", so a cage with no broken art still works.
    /// </summary>
    private void RefreshSprite()
    {
        Sprite stateSprite = IsBroken ? brokenSprite : intactSprite;
        if (cageRenderer != null && stateSprite != null)
        {
            cageRenderer.sprite = stateSprite;
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
