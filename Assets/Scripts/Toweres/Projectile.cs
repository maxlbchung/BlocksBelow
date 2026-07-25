using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
public class Projectile : MonoBehaviour, IPoolable
{
    [SerializeField, Min(0f)] private float speed = 8f;
    [SerializeField, Min(0.1f), Tooltip("Seconds before the projectile is returned to its pool.")]
    private float lifetime = 8f;
    [SerializeField, Tooltip("Degrees per second the bullet spins as it flies. Negative spins the other way.")]
    private float spinSpeed = 1440f;

    private Rigidbody2D body;
    private Vector2 direction = Vector2.left;
    private PooledObject poolHandle;
    private Vector3 baseScale = Vector3.one;
    private bool baseScaleCaptured;

    public float damage;
    public float Lifetime => lifetime;

    private void Awake()
    {
        CaptureBaseScale();
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        // The spin is physics-driven, so rotation has to stay free. The trigger
        // collider is a circle centred on the bullet, so spinning never changes
        // what the shot can hit.
        body.constraints = RigidbodyConstraints2D.None;
        // Damping would bleed the spin off over a long flight.
        body.angularDamping = 0f;
        body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
    }

    public static Projectile Spawn(
        Projectile prefab,
        Vector3 position,
        Quaternion rotation,
        Vector2 direction,
        float damage,
        float sizeMultiplier = 1f)
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
        projectile.SetSizeMultiplier(sizeMultiplier);
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

    /// <summary>
    /// Scales the bullet around its prefab size. The trigger collider is a child of
    /// the same transform, so a bigger bullet also sweeps a wider hit area.
    /// </summary>
    public void SetSizeMultiplier(float multiplier)
    {
        CaptureBaseScale();
        transform.localScale = baseScale * Mathf.Max(0.01f, multiplier);
    }

    public void OnPoolAcquire()
    {
        damage = 0f;
        direction = Vector2.left;
        // Pooled bullets keep the scale of their previous life, so reset it here for
        // callers that spawn without a size.
        CaptureBaseScale();
        transform.localScale = baseScale;
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

    private void CaptureBaseScale()
    {
        // Pooled bullets are instantiated inactive, so Awake has not necessarily run
        // by the first acquire. Whoever gets here first records the prefab scale.
        if (!baseScaleCaptured)
        {
            baseScale = transform.localScale;
            baseScaleCaptured = true;
        }
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
            body.angularVelocity = spinSpeed;
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

        if (enemy.TryTakeDamage(damage))
        {
            // Pooled projectiles must be released rather than directly destroyed.
            // An invulnerable enemy returns false so the shot continues through it.
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
