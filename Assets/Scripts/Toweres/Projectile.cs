using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0f)] private float speed = 8f;
    [SerializeField, Min(0.1f), Tooltip("Seconds before the projectile is returned to its pool.")]
    private float lifetime = 8f;
    [SerializeField, Tooltip("Return to the pool after damaging an enemy.")]
    private bool releaseOnEnemyHit;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.left;
    private PooledObject poolHandle;

    public float damage;
    public float Lifetime => lifetime;

    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
    }

    public static Projectile Spawn(
        Projectile prefab,
        Vector3 position,
        Quaternion rotation,
        Vector2 direction,
        float damage)
    {
        if (prefab == null
            || !CombatObjectPool.TryAcquire(
                prefab.gameObject,
                position,
                rotation,
                prefab.lifetime,
                out PooledObject pooledObject)
            || pooledObject.Projectile == null)
        {
            return null;
        }

        Projectile projectile = pooledObject.Projectile;
        projectile.damage = damage;
        // Activate first so the Rigidbody2D exists and is simulated, then set the velocity.
        // A velocity assigned to an inactive (or never-awoken pooled) body does not persist.
        CombatObjectPool.Activate(pooledObject);
        projectile.SetDirection(direction);
        return projectile;
    }

    public void SetDirection(Vector2 newDirection)
    {
        float directionLengthSquared = newDirection.sqrMagnitude;
        if (directionLengthSquared > 0.000001f)
        {
            direction = newDirection / Mathf.Sqrt(directionLengthSquared);
        }

        ApplyVelocity();
    }

    public void OnPoolAcquire()
    {
        damage = 0f;
        direction = Vector2.left;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    public void OnPoolRelease()
    {
        damage = 0f;
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    internal void AssignPoolHandle(PooledObject handle)
    {
        poolHandle = handle;
    }

    private void ApplyVelocity()
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (body != null)
        {
            body.linearVelocity = direction * speed;
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        Enemy enemy = EnemySimulationManager.InstanceOrNull != null
            ? EnemySimulationManager.InstanceOrNull.FindEnemy(other)
            : null;
        if (enemy == null)
        {
            return;
        }

        enemy.health -= damage;
        if (releaseOnEnemyHit)
        {
            Release();
        }
    }

    private void Release()
    {
        if (poolHandle != null)
        {
            poolHandle.Release();
        }
        else if (!CombatObjectPool.Release(gameObject))
        {
            Destroy(gameObject);
        }
    }
}
