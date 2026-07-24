using UnityEngine;

public class FlyerEnemy : Enemy
{
    [SerializeField] private float moveSpeed = 1f;
    [SerializeField] private float desiredRange = 5f;
    [SerializeField] private float runRange = 1f;
    [SerializeField] private float shootInterval = 2f;
    [SerializeField] private GameObject bulletPrefab;
    [SerializeField] private float bulletSpeed = 5f;
    [SerializeField, Min(0.1f), Tooltip("Seconds before an enemy bullet is returned to its pool.")]
    private float bulletLifetime = 8f;
    [SerializeField, Min(0), Tooltip("Bullets reserved when this enemy type is prepared by a wave spawner.")]
    private int bulletPrewarmCount = 128;

    private Vector2 target;
    private float shootCounter;

    protected override Vector2 CalculateDesiredVelocity(Transform player, float elapsed)
    {
        if (player == null)
        {
            target = Position;
            return Vector2.zero;
        }

        Vector2 position = Position;
        Vector2 playerPosition = player.position;
        Vector2 fromPlayer = position - playerPosition;
        float distanceSquared = fromPlayer.sqrMagnitude;
        float desiredRangeSquared = desiredRange * desiredRange;
        float runRangeSquared = runRange * runRange;

        if (distanceSquared > desiredRangeSquared)
        {
            target = GetClosestPointOnCircle(playerPosition, desiredRange, position);
        }
        else if (distanceSquared < runRangeSquared)
        {
            Vector2 away = distanceSquared > 0.000001f
                ? fromPlayer / Mathf.Sqrt(distanceSquared)
                : Vector2.right;
            target = position + away * desiredRange;
        }
        else
        {
            target = position;
        }

        Vector2 toTarget = target - position;
        float targetDistanceSquared = toTarget.sqrMagnitude;
        if (targetDistanceSquared <= 0.01f)
        {
            return Vector2.zero;
        }

        return toTarget * (moveSpeed / Mathf.Sqrt(targetDistanceSquared));
    }

    protected override void OnDecisionTick(Transform player, float elapsed)
    {
        shootCounter += elapsed;
        if (shootCounter < Mathf.Max(0.01f, shootInterval))
        {
            return;
        }

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
        target = Position;
        shootCounter = 0f;
    }

    public static Vector2 GetClosestPointOnCircle(
        Vector2 circleCenter,
        float radius,
        Vector2 targetPosition)
    {
        Vector2 direction = targetPosition - circleCenter;
        float distanceSquared = direction.sqrMagnitude;
        if (distanceSquared < 0.0001f)
        {
            return circleCenter + Vector2.right * radius;
        }

        return circleCenter + direction * (radius / Mathf.Sqrt(distanceSquared));
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
