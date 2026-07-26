using UnityEngine;

public class FlyerEnemy : Enemy
{
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float desiredRange = 5f;
    [SerializeField] private float runRange = 1f;

    [Header("Strafing")]
    [SerializeField, Min(0f), Tooltip("Sideways orbit speed around the player. Zero makes the flyer hold station instead of circling.")]
    private float strafeSpeed = 2f;
    [SerializeField, Min(0f), Tooltip("How far in and out of the desired range the flyer drifts while circling.")]
    private float rangeVariation = 1.5f;
    [SerializeField, Min(0f), Tooltip("Closer-and-further drifts per second. Higher makes the flyer press in and back off more often.")]
    private float rangeVariationRate = 0.35f;
    [SerializeField, Min(0f), Tooltip("How hard the flyer pulls back onto its preferred range. Higher settles onto the ring faster and overshoots more.")]
    private float rangeCorrection = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("Shortest time the flyer circles one way before reversing.")]
    private float minimumStrafeDuration = 1.5f;
    [SerializeField, Min(0.1f), Tooltip("Longest time the flyer circles one way before reversing.")]
    private float maximumStrafeDuration = 4f;
    [SerializeField, Min(0f), Tooltip("Height above the terrain at which the flyer reverses its orbit rather than circling down into it.")]
    private float groundTurnHeight = 2f;

    [Header("Shooting")]
    [SerializeField] private float shootInterval = 2f;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 5f;
    [SerializeField, Min(0.1f), Tooltip("Seconds before an enemy bullet is returned to its pool.")]
    private float bulletLifetime = 8f;
    [SerializeField, Min(0), Tooltip("Bullets reserved when this enemy type is prepared by a wave spawner.")]
    private int bulletPrewarmCount = 30;

    private const float TwoPi = Mathf.PI * 2f;

    private float shootCounter;
    private float rangePhase;
    private float strafeDirection = 1f;
    private float strafeCountdown;
    private Animator anim;

    protected override void Awake()
    {
        anim = GetComponent<Animator>();
        base.Awake();
        SeedStrafe();
    }

    /// <summary>
    /// Circles the player rather than parking at a fixed range: a tangential orbit that
    /// reverses on its own, over a preferred range that drifts closer and further. All of
    /// it runs off the decision tick's own elapsed time, so a flyer stepped at the
    /// simulation's staggered rate strafes at the same pace as one stepped every frame.
    /// </summary>
    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (player == null)
        {
            return Vector2.zero;
        }

        Vector2 position = Position;
        Vector2 fromPlayer = position - (Vector2)player.position;
        float distance = fromPlayer.magnitude;
        Vector2 outward = distance > 0.001f ? fromPlayer / distance : Vector2.right;

        rangePhase += elapsed * rangeVariationRate * TwoPi;
        if (rangePhase >= TwoPi)
        {
            rangePhase -= TwoPi;
        }

        strafeCountdown -= elapsed;
        if (strafeCountdown <= 0f)
        {
            strafeDirection = -strafeDirection;
            strafeCountdown = RollStrafeDuration();
        }

        Vector2 tangent = new Vector2(-outward.y, outward.x) * strafeDirection;

        // Orbiting down into the island only grinds the flyer along the surface, so it
        // turns around at the bottom of the arc and circles back up. In a scene with no
        // terrain the floor is negative infinity, leaving the height infinite, and the
        // turn never fires.
        if (tangent.y < 0f && position.y - (GroundSurfaceY + GroundClearance) < groundTurnHeight)
        {
            strafeDirection = -strafeDirection;
            strafeCountdown = RollStrafeDuration();
            tangent = -tangent;
        }

        // The preferred range never dips inside the run range, so a player who closes in
        // is still backed away from - that case falls out of the same correction below
        // rather than needing a flee branch of its own.
        float preferredRange = Mathf.Max(
            runRange,
            desiredRange + Mathf.Sin(rangePhase) * rangeVariation);
        float radialSpeed = Mathf.Clamp(
            (preferredRange - distance) * rangeCorrection,
            -moveSpeed,
            moveSpeed);

        Vector2 velocity = outward * radialSpeed + tangent * strafeSpeed;
        float speedSquared = velocity.sqrMagnitude;
        if (speedSquared > moveSpeed * moveSpeed)
        {
            velocity *= moveSpeed / Mathf.Sqrt(speedSquared);
        }

        return velocity;
    }

    protected override void OnDecisionTick(Transform player, float elapsed)
    {
        shootCounter += elapsed;
        if (shootCounter < shootInterval + 2f)
        {
            anim.SetBool("Charging", true);
        }
        if (shootCounter < Mathf.Max(0.01f, shootInterval))
        {
            return;
        }
        anim.SetBool("Charging", false);
        shootCounter %= Mathf.Max(0.01f, shootInterval);
        ShootAtPlayer(player);
    }

    public override void PreparePools(int prewarmCount, int maxPoolSize, bool strict)
    {
        if (bulletPrefab != null)
        {
            int bulletCount = Mathf.Max(bulletPrewarmCount, prewarmCount);
            CombatObjectPool.Configure(
                bulletPrefab,
                bulletCount,
                Mathf.Max(bulletCount, maxPoolSize),
                strict);
        }
    }

    protected override void ResetEnemyState()
    {
        shootCounter = 0f;
        SeedStrafe();
    }

    /// <summary>
    /// Gives this flyer its own orbit direction, drift phase and time to the first
    /// reversal. Without it a wave of flyers pulled from the pool would circle the player
    /// in lockstep, which reads as one moving shape rather than several enemies.
    /// </summary>
    private void SeedStrafe()
    {
        rangePhase = Random.value * TwoPi;
        strafeDirection = Random.value < 0.5f ? -1f : 1f;
        strafeCountdown = RollStrafeDuration();
    }

    private float RollStrafeDuration()
    {
        return Random.Range(
            minimumStrafeDuration,
            Mathf.Max(minimumStrafeDuration, maximumStrafeDuration));
    }

    private void ShootAtPlayer(Transform player)
    {
        if (player == null || bulletPrefab == null)
        {
            return;
        }

        Vector2 direction = (Vector2)player.position - Position;
        float distanceSquared = direction.sqrMagnitude;
        if (distanceSquared <= 0.000001f)
        {
            return;
        }
        
        Vector2 velocity = direction * (bulletSpeed / Mathf.Sqrt(distanceSquared));
        EnemyBullet.Spawn(
            bulletPrefab,
            Position,
            Quaternion.identity,
            velocity,
            bulletLifetime);
    }
}
