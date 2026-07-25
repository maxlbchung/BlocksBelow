using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : Entity, IPoolable
{
    [SerializeField, Min(0.1f)] protected float repulsionForce = 1.5f;
    [SerializeField, Min(0.1f)] protected float repulsionRadius = 3f;
    [SerializeField, Min(1f)] protected float exponentialFalloff = 2f;

    [Header("Ground Avoidance")]
    [SerializeField, Min(0f), Tooltip("Lowest this enemy's centre may sit above the terrain surface.")]
    private float groundClearance = 0.5f;
    [SerializeField, Min(0.01f), Tooltip("Height above the clearance line where descent starts easing off, "
        + "so enemies level out over the ground instead of stopping dead against it.")]
    private float groundApproachBand = 1.5f;
    [SerializeField, Min(0f), Tooltip("Climb speed used to lift an enemy that still ended up under the terrain.")]
    private float groundRecoverySpeed = 4f;

    // Top of the terrain, shared by every enemy. Resolved from the Ground-tagged collider
    // the same way SquarePlacement finds the surface it refuses to build below.
    private static float groundSurfaceY = float.NegativeInfinity;
    private static bool groundSurfaceResolved;

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
    public virtual bool CanBeCaged => isCagable;

    [Header("Cage")]
    public bool isCagable = false;

    /// <summary>
    /// World-space top of the terrain, or negative infinity in scenes without a
    /// Ground-tagged collider (the stress-test scenes), where avoidance is skipped.
    /// </summary>
    internal static float GroundSurfaceY
    {
        get
        {
            if (!groundSurfaceResolved)
            {
                groundSurfaceResolved = true;
                GameObject ground = GameObject.FindWithTag("Ground");
                groundSurfaceY = ground != null && ground.TryGetComponent(out Collider2D groundCollider)
                    ? groundCollider.bounds.max.y
                    : float.NegativeInfinity;
            }

            return groundSurfaceY;
        }
    }

    /// <summary>Lowest this enemy's centre may sit above the terrain surface.</summary>
    protected float GroundClearance => groundClearance;

    /// <summary>Lifts <paramref name="position"/> out of the terrain, for code that places an enemy directly.</summary>
    internal static Vector2 ClampAboveGround(Vector2 position, float clearance)
    {
        float floor = GroundSurfaceY + clearance;
        if (!float.IsNegativeInfinity(floor) && position.y < floor)
        {
            position.y = floor;
        }

        return position;
    }

    // Statics outlive a scene, so the cached surface is dropped whenever one loads and
    // re-resolved on the next enemy that needs it. Terrain never moves within a scene.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void HookGroundSurfaceInvalidation()
    {
        groundSurfaceResolved = false;
        SceneManager.sceneLoaded -= InvalidateGroundSurface;
        SceneManager.sceneLoaded += InvalidateGroundSurface;
    }

    private static void InvalidateGroundSurface(Scene scene, LoadSceneMode mode)
    {
        groundSurfaceResolved = false;
    }

    protected override void Awake()
    {
        base.Awake();
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
        // Deactivation kills the flash coroutine mid-blink, so restore the
        // materials here or the enemy respawns from the pool stuck white.
        ResetHitFeedback();
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
            // Only counted here: the other ReleaseOrDestroy callers are despawns
            // (a breaker reaching the player, a lost target), not kills.
            if (!deathHandled)
            {
                RunStats.RecordEnemyDefeated();
            }

            ReleaseOrDestroy();
            return;
        }

        if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            return;
        }

        Vector2 velocityDifference = ApplyGroundAvoidance(desiredVelocity) - rb.linearVelocity;
        rb.AddForce(
            velocityDifference * rb.mass + separationForce,
            ForceMode2D.Force);
    }

    /// <summary>
    /// Trims the descent out of a steering vector as it nears the terrain, and turns it
    /// into a climb once the enemy is under it. Enemies fly with gravity disabled, so a
    /// pursuit vector aimed at a player standing on the island would otherwise drive them
    /// straight through it. This runs every physics step rather than at decision rate
    /// because separation between enemies can shove a body downward between decisions.
    /// </summary>
    private Vector2 ApplyGroundAvoidance(Vector2 velocity)
    {
        float floor = GroundSurfaceY + groundClearance;
        if (float.IsNegativeInfinity(floor))
        {
            return velocity;
        }

        float heightAboveFloor = Position.y - floor;
        if (heightAboveFloor <= 0f)
        {
            velocity.y = groundRecoverySpeed;
            return velocity;
        }

        // Taper the descent to zero across the approach band so enemies flatten out over
        // the surface. Above the band they are free to dive at full speed.
        if (velocity.y < 0f && heightAboveFloor < groundApproachBand)
        {
            velocity.y *= heightAboveFloor / groundApproachBand;
        }

        return velocity;
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

    public virtual bool TryTakeDamage(float damage)
    {
        if (!CanTakeDamage || IsInvincible)
        {
            return false;
        }

        health -= damage;
        PlayHitFeedback(Position);
        return true;
    }

    /// <summary>Called after this enemy is freed from a cage and re-enabled.</summary>
    public virtual void OnReleasedFromCage()
    {
    }

    public void OnPoolAcquire()
    {
        ResetHitFeedback();
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
            float facingScaleY = rb.linearVelocity.x < 0
                // Invert Y scale to prevent being upside down when facing left
                ? -Mathf.Abs(currentScale.y)
                // Normal Y scale when facing right
                : Mathf.Abs(currentScale.y);

            // Writing localScale marks the attached Collider2D dirty, so Box2D re-bakes its
            // fixture on the next sync. Only write when the facing actually flips; otherwise
            // every enemy paid for a rebake on every physics step.
            if (currentScale.y != facingScaleY)
            {
                currentScale.y = facingScaleY;
                transform.localScale = currentScale;
            }
        }
    }

}
