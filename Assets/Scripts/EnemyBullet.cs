using UnityEngine;

[RequireComponent(typeof(Rigidbody2D))]
[RequireComponent(typeof(BoxCollider2D))]
public class EnemyBullet : MonoBehaviour, IPoolable
{
    private Rigidbody2D body;
    private PooledObject poolHandle;
    public int damage = 1;
    private void Awake()
    {
        body = GetComponent<Rigidbody2D>();
        body.gravityScale = 0f;
        body.constraints = RigidbodyConstraints2D.FreezeRotation;
        body.collisionDetectionMode = CollisionDetectionMode2D.Discrete;
    }

    public static EnemyBullet Spawn(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        Vector2 velocity,
        float lifetime)
    {
        if (!CombatObjectPool.TryAcquire(
                prefab,
                position,
                rotation,
                lifetime,
                out PooledObject pooledObject)
            || pooledObject.EnemyBullet == null)
        {
            return null;
        }

        EnemyBullet bullet = pooledObject.EnemyBullet;
        // Activate first so the Rigidbody2D exists and is simulated, then set the velocity.
        // A velocity assigned to an inactive (or never-awoken pooled) body does not persist.
        CombatObjectPool.Activate(pooledObject);
        bullet.SetVelocity(velocity);
        return bullet;
    }

    public void SetVelocity(Vector2 velocity)
    {
        if (body == null)
        {
            body = GetComponent<Rigidbody2D>();
        }

        if (body != null)
        {
            body.linearVelocity = velocity;
        }
    }

    public void OnPoolAcquire()
    {
        if (body != null)
        {
            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }
    }

    public void OnPoolRelease()
    {
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

    public void Release()
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
