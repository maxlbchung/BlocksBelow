using UnityEngine;
using UnityEngine.SceneManagement;

[RequireComponent(typeof(Rigidbody2D))]
public class Enemy : Entity, IPoolable
{
    [SerializeField, Min(0.1f)] protected float repulsionForce = 1.5f;
    [SerializeField, Min(0.1f)] protected float repulsionRadius = 3f;
    [SerializeField, Min(1f)] protected float exponentialFalloff = 2f;

    [Header("Ground Avoidance")]
    [SerializeField, Tooltip("Lowest this enemy's centre may sit relative to the terrain surface. "
        + "Negative lets it fly with its centre below the top of the terrain, which is how enemies "
        + "come down level with a player standing on the island instead of hovering over them.")]
    private float groundClearance = -0.5f;
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
    private bool initialHealthCaptured;
    private Vector2 desiredVelocity;
    private Vector2 separationForce;
    private bool deathHandled;
    private bool deathSoundPlayed;
    private PooledObject poolHandle;

    [Header("Death SFX")]
    [SerializeField, AudioClipDropdown] private AudioClip deathSfx;

    [Header("Movement SFX")]
    [SerializeField, AudioClipDropdown] private AudioClip movementLoopSfx;
    [SerializeField, Range(0f, 1f)] private float movementLoopVolume = 0.25f;
    private AudioSource movementLoopSource;

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

    /// <summary>
    /// Damage this enemy deals to the player on contact. Zero for enemies that hurt the
    /// player some other way, or not at all: a flyer does its damage through its bullets,
    /// and a breaker only goes for cages. A caged enemy never lands contact damage - the
    /// cage disables its colliders, so no overlap is reported while it is held.
    /// </summary>
    public virtual int ContactDamage => 0;

    /// <summary>
    /// Whether the round has to wait for this enemy before it can end. False for enemies
    /// that are on the field with nothing left to do - a breaker with no cage to target -
    /// which would otherwise hold the wave open forever, since they cannot be shot down.
    /// </summary>
    public virtual bool BlocksWaveCompletion => true;

    [Header("Cage")]
    public bool isCagable = false;

    [Header("Description")]
    [Tooltip("What this enemy does, shown in the shop's round tab while it is hovered.")]
    [SerializeField, TextArea(2, 5)] private string description;

    /// <summary>What this enemy does, as written on its prefab. Empty when none was set.</summary>
    public string Description => description;

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

    /// <summary>
    /// How sharply this enemy's velocity closes on the velocity its AI asked for, in
    /// multiples per second. 1 - what every enemy has always run at - takes about a second
    /// to swap direction, which reads as a heavy drifting turn. Raise it for enemies that
    /// have to change heading quickly: at 1, a diving enemy coasts most of a second past its
    /// target before it can pull out, however hard its AI steers.
    /// </summary>
    protected virtual float SteeringResponse => 1f;

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
        CaptureInitialHealth();
        ConfigureBody();
    }

    /// <summary>
    /// Records the prefab's health the first time it is seen, from whichever of Awake or
    /// OnPoolAcquire runs first. The pool instantiates items under an inactive root, so Awake is
    /// deferred until the first acquire activates them - it lands *after* OnPoolAcquire. Capturing
    /// only in Awake meant the first acquire restored health from a still-zero initialHealth,
    /// zeroing the enemy's health for the rest of its life and letting any hit kill it.
    /// </summary>
    private void CaptureInitialHealth()
    {
        if (initialHealthCaptured)
        {
            return;
        }

        initialHealth = health;
        initialHealthCaptured = true;
    }

    protected virtual void OnEnable()
    {
        deathHandled = false;
        EnemySimulationManager.Instance.Register(this);
        movementLoopSource =
            AudioController.PlayLoop(movementLoopSfx, gameObject, movementLoopVolume);
    }

    protected virtual void OnDisable()
    {
        AudioController.StopLoop(movementLoopSource);
        movementLoopSource = null;
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
        // Dead at zero, matching the player's threshold. "< 0" let an enemy survive on empty
        // health, so 3 health took four hits of 1 damage.
        if (health <= 0f)
        {
            // Only counted here: the other ReleaseOrDestroy callers are despawns
            // (a breaker reaching the player, a lost target), not kills.
            if (!deathHandled)
            {
                RunStats.RecordEnemyDefeated();
                PlayDeathSfxOnce();
            }

            ReleaseOrDestroy();
            return;
        }

        if (rb == null || !rb.simulated || rb.bodyType != RigidbodyType2D.Dynamic)
        {
            return;
        }

        // Integrated here rather than handed to AddForce, which would apply exactly this
        // delta for ForceMode2D.Force: the velocity the step will actually run with has to
        // be known before the body gets it, so ground avoidance can trim it.
        //
        // Steering closes on the desired velocity at this enemy's own response rate, while
        // separation stays a force and keeps its own scaling - a sharp turner should not
        // also be flung further by its neighbours. The clamp stops a very high response
        // from overshooting the target velocity inside one step.
        Vector2 velocity = rb.linearVelocity;
        velocity += (desiredVelocity - velocity)
                * Mathf.Clamp01(SteeringResponse * fixedDeltaTime)
            + separationForce * (fixedDeltaTime / rb.mass);
        rb.linearVelocity = ApplyGroundAvoidance(velocity, fixedDeltaTime);
    }

    /// <summary>
    /// Keeps a step's velocity above the terrain: it eases off the descent on the way down,
    /// never lets a step carry the body through the surface, and climbs out from under it.
    /// Enemies fly with gravity disabled, so a pursuit vector aimed at a player standing on
    /// the island drives them straight into it.
    /// <para>
    /// This shapes the velocity the body is about to run with rather than the steering that
    /// asked for it, which is what stops them flying underground: steering only closes a
    /// fraction of the velocity gap per step, so a fast dive kept sinking well after the
    /// desired velocity had already turned into a climb. It also catches the descent that
    /// separation between enemies adds after their decisions were made.
    /// </para>
    /// </summary>
    private Vector2 ApplyGroundAvoidance(Vector2 velocity, float fixedDeltaTime)
    {
        float floor = GroundSurfaceY + groundClearance;
        if (float.IsNegativeInfinity(floor))
        {
            return velocity;
        }

        float heightAboveFloor = rb.position.y - floor;
        if (heightAboveFloor <= 0f)
        {
            // Only reached when something else put the body under - a spawn point inside
            // the island, a push from a fan - since the cap below stops it descending
            // through the surface under its own power.
            velocity.y = Mathf.Max(velocity.y, groundRecoverySpeed);
            return velocity;
        }

        if (velocity.y < 0f)
        {
            // Taper the descent to zero across the approach band so enemies flatten out
            // over the surface, then cap it at the gap actually left underneath, so the
            // step lands on the ground instead of crossing it. Above the band they are
            // free to dive at full speed.
            if (heightAboveFloor < groundApproachBand)
            {
                velocity.y *= heightAboveFloor / groundApproachBand;
            }

            velocity.y = Mathf.Max(velocity.y, -heightAboveFloor / fixedDeltaTime);
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
        CaptureInitialHealth();
        ResetHitFeedback();
        health = initialHealth;
        desiredVelocity = Vector2.zero;
        separationForce = Vector2.zero;
        deathHandled = false;
        deathSoundPlayed = false;
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

    protected void PlayDeathSfxOnce()
    {
        if (deathSoundPlayed)
        {
            return;
        }

        deathSoundPlayed = true;
        AudioController.Play(deathSfx);
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

    /// <summary>
    /// Takes this enemy off the field without counting it as a kill, for the spawner
    /// clearing up leftovers at the end of a round.
    /// </summary>
    internal void Despawn()
    {
        ReleaseOrDestroy();
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
