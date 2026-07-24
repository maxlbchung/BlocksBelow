using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : Entity, IPoolable
{
    [SerializeField, Min(0.1f)] protected float repulsionForce = 1.5f;
    [SerializeField, Min(0.1f)] protected float repulsionRadius = 3f;
    [SerializeField, Min(1f)] protected float exponentialFalloff = 2f;

    protected Rigidbody2D rb;
    protected Collider2D enemyCollider;

    private float initialHealth;
    private Vector2 desiredVelocity;
    private Vector2 separationForce;
    private bool deathHandled;
    private PooledObject poolHandle;

    internal int SimulationIndex { get; set; } = -1;
    internal int DecisionBucket { get; set; }
    internal float LastDecisionTime { get; set; }
    internal float LastStrategicTime { get; set; }
    internal float RepulsionForce => repulsionForce;
    internal float RepulsionRadius => repulsionRadius;
    internal float RepulsionFalloff => exponentialFalloff;
    internal virtual bool UsesSeparation => true;
    internal Vector2 Position => rb != null ? rb.position : (Vector2)transform.position;
    internal bool IsSimulationActive =>
        isActiveAndEnabled && rb != null && rb.simulated;
    public Rigidbody2D Body => rb;
    public Collider2D EnemyCollider => enemyCollider;
    public virtual bool CanTakeDamage => true;

    [Header("Cage")]
    public bool isCagable = false;

    protected virtual void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        enemyCollider = GetComponent<Collider2D>();
        initialHealth = health;
        ConfigureBody();
    }

    protected virtual void OnEnable()
    {
        deathHandled = false;
        EnemySimulationManager.Instance.Register(this);
    }

    protected virtual void OnDisable()
    {
        EnemySimulationManager.InstanceOrNull?.Unregister(this);
    }

    internal void SimulateDecision(Transform player, float elapsed)
    {
        desiredVelocity = CalculateDesiredVelocity(player, elapsed);
        OnDecisionTick(player, elapsed);
    }

    internal void SimulateStrategicDecision(Transform player, float elapsed)
    {
        OnStrategicTick(player, elapsed);
    }

    internal void SetSeparationForce(Vector2 force)
    {
        separationForce = force;
    }

    internal void ApplySimulationStep(float fixedDeltaTime)
    {
        if (health < 0f)
        {
            ReleaseOrDestroy();
            return;
        }

        if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            return;
        }

        Vector2 velocityDifference = desiredVelocity - rb.linearVelocity;
        rb.AddForce(
            velocityDifference * rb.mass + separationForce,
            ForceMode2D.Force);
    }

    protected virtual Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        return Vector2.zero;
    }

    protected virtual void OnDecisionTick(Transform player, float elapsed)
    {
    }

    protected virtual void OnStrategicTick(Transform player, float elapsed)
    {
    }

    public virtual void PreparePools(int prewarmCount, int maxPoolSize, bool strict)
    {
    }

    public bool TryTakeDamage(float damage)
    {
        if (!CanTakeDamage)
        {
            return false;
        }

        health -= damage;
        return true;
    }

    public void OnPoolAcquire()
    {
        health = initialHealth;
        desiredVelocity = Vector2.zero;
        separationForce = Vector2.zero;
        deathHandled = false;
        ConfigureBody();
        ResetEnemyState();
    }

    public void OnPoolRelease()
    {
        desiredVelocity = Vector2.zero;
        separationForce = Vector2.zero;
        StopAllCoroutines();
        ResetEnemyState();
    }

    internal void AssignPoolHandle(PooledObject handle)
    {
        poolHandle = handle;
    }

    protected virtual void ResetEnemyState()
    {
    }

    private void ConfigureBody()
    {
        if (rb == null)
        {
            return;
        }

        rb.bodyType = RigidbodyType2D.Dynamic;
        rb.gravityScale = 0f;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
    }

    protected void ReleaseOrDestroy()
    {
        if (deathHandled)
        {
            return;
        }

        deathHandled = true;
        if (poolHandle != null)
        {
            poolHandle.Release();
        }
        else if (!CombatObjectPool.Release(gameObject))
        {
            Destroy(gameObject);
        }
    }

    void FixedUpdate()
    {
        if (rb.linearVelocity.sqrMagnitude > 0.01f)
        {
            // Get angle toward velocity
            float angle = Mathf.Atan2(rb.linearVelocity.y, rb.linearVelocity.x) * Mathf.Rad2Deg;

            // Apply rotation
            rb.MoveRotation(angle);

            // Reflect/Flip on Y-axis if pointing left so it doesn't appear upside down
            Vector3 currentScale = transform.localScale;
            if (rb.linearVelocity.x < 0)
            {
                // Invert Y scale to prevent being upside down when facing left
                currentScale.y = -Mathf.Abs(currentScale.y);
            }
            else
            {
                // Normal Y scale when facing right
                currentScale.y = Mathf.Abs(currentScale.y);
            }
            transform.localScale = currentScale;
        }
    }

}
