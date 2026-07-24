using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public interface IPoolable
{
    void OnPoolAcquire();
    void OnPoolRelease();
}

[DisallowMultipleComponent]
public sealed class PooledObject : MonoBehaviour
{
    private CombatObjectPool owner;
    private GameObject sourcePrefab;
    private MonoBehaviour[] behaviours;
    private Rigidbody2D[] bodies;
    private Renderer[] renderers;
    private Animator[] animators;
    private bool cached;

    internal bool InPool { get; private set; }
    internal float RemainingLifetime { get; set; }
    internal int TimedIndex { get; set; } = -1;
    internal GameObject SourcePrefab => sourcePrefab;

    public Enemy Enemy { get; private set; }
    public Projectile Projectile { get; private set; }
    public EnemyBullet EnemyBullet { get; private set; }

    internal void Initialize(CombatObjectPool poolOwner, GameObject prefab)
    {
        owner = poolOwner;
        sourcePrefab = prefab;
        CacheComponents();
        Enemy?.AssignPoolHandle(this);
        Projectile?.AssignPoolHandle(this);
        EnemyBullet?.AssignPoolHandle(this);
        InPool = true;
    }

    internal void PrepareForAcquire(Vector3 position, Quaternion rotation, float lifetime)
    {
        CacheComponents();
        transform.SetPositionAndRotation(position, rotation);

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body == null)
            {
                continue;
            }

            body.linearVelocity = Vector2.zero;
            body.angularVelocity = 0f;
        }

        for (int i = 0; i < renderers.Length; i++)
        {
            if (renderers[i] != null)
            {
                renderers[i].enabled = true;
            }
        }

        for (int i = 0; i < animators.Length; i++)
        {
            Animator animator = animators[i];
            if (animator != null)
            {
                animator.Rebind();
                animator.Update(0f);
            }
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is IPoolable poolable)
            {
                poolable.OnPoolAcquire();
            }
        }

        RemainingLifetime = lifetime;
        InPool = false;
    }

    internal void PrepareForRelease()
    {
        if (InPool)
        {
            return;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            MonoBehaviour behaviour = behaviours[i];
            if (behaviour == null)
            {
                continue;
            }

            behaviour.StopAllCoroutines();
            if (behaviour is IPoolable poolable)
            {
                poolable.OnPoolRelease();
            }
        }

        for (int i = 0; i < bodies.Length; i++)
        {
            Rigidbody2D body = bodies[i];
            if (body != null)
            {
                body.linearVelocity = Vector2.zero;
                body.angularVelocity = 0f;
            }
        }

        RemainingLifetime = 0f;
        InPool = true;
    }

    public void Release()
    {
        if (owner != null)
        {
            owner.Release(this);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private void CacheComponents()
    {
        if (cached)
        {
            return;
        }

        behaviours = GetComponentsInChildren<MonoBehaviour>(true);
        bodies = GetComponentsInChildren<Rigidbody2D>(true);
        renderers = GetComponentsInChildren<Renderer>(true);
        animators = GetComponentsInChildren<Animator>(true);
        Enemy = GetComponent<Enemy>();
        Projectile = GetComponent<Projectile>();
        EnemyBullet = GetComponent<EnemyBullet>();
        cached = true;
    }
}

[DefaultExecutionOrder(-600)]
public sealed class CombatObjectPool : MonoBehaviour
{
    private sealed class Pool
    {
        public readonly GameObject Prefab;
        public readonly Stack<PooledObject> Inactive;
        public int CreatedCount;
        public int MaxSize;
        public bool Strict;

        public Pool(GameObject prefab, int capacity, int maxSize, bool strict)
        {
            Prefab = prefab;
            Inactive = new Stack<PooledObject>(Mathf.Max(1, capacity));
            MaxSize = Mathf.Max(1, maxSize);
            Strict = strict;
        }
    }

    private static readonly ProfilerMarker PoolGetMarker =
        new ProfilerMarker("CombatPool.Get");
    private static readonly ProfilerMarker PoolReleaseMarker =
        new ProfilerMarker("CombatPool.Release");
    private static readonly ProfilerMarker PoolPrewarmMarker =
        new ProfilerMarker("CombatPool.Prewarm");

    private static CombatObjectPool instance;

    [SerializeField, Min(1), Tooltip("Maximum instances for pools created without explicit configuration.")]
    private int defaultMaxSize = 1024;
    [SerializeField, Tooltip("When enabled, unconfigured pools never grow during combat.")]
    private bool strictByDefault;

    private readonly Dictionary<GameObject, Pool> pools = new Dictionary<GameObject, Pool>(32);
    private readonly List<PooledObject> timedObjects = new List<PooledObject>(256);
    private Transform inactiveRoot;
    private int poolMisses;

    public static CombatObjectPool Instance => GetOrCreate();
    public int PoolMisses => poolMisses;

    private static CombatObjectPool GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<CombatObjectPool>();
        if (instance != null)
        {
            return instance;
        }

        GameObject poolObject = new GameObject(nameof(CombatObjectPool));
        instance = poolObject.AddComponent<CombatObjectPool>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        GameObject rootObject = new GameObject("Inactive Pooled Objects");
        inactiveRoot = rootObject.transform;
        inactiveRoot.SetParent(transform, false);
        rootObject.SetActive(false);
    }

    public static void Configure(
        GameObject prefab,
        int prewarmCount,
        int maxSize,
        bool strict)
    {
        if (prefab == null)
        {
            return;
        }

        GetOrCreate().ConfigureInternal(prefab, prewarmCount, maxSize, strict);
    }

    private void ConfigureInternal(
        GameObject prefab,
        int prewarmCount,
        int maxSize,
        bool strict)
    {
        using (PoolPrewarmMarker.Auto())
        {
            Pool pool = GetOrCreatePool(prefab, prewarmCount, maxSize, strict);
            pool.MaxSize = Mathf.Max(pool.MaxSize, maxSize);
            pool.Strict |= strict;

            int target = Mathf.Min(Mathf.Max(0, prewarmCount), pool.MaxSize);
            while (pool.CreatedCount < target)
            {
                PooledObject item = CreateItem(pool);
                pool.Inactive.Push(item);
            }
        }
    }

    public static bool TryAcquire(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        out PooledObject item)
    {
        item = null;
        if (prefab == null)
        {
            return false;
        }

        return GetOrCreate().TryAcquireInternal(prefab, position, rotation, lifetime, out item);
    }

    private bool TryAcquireInternal(
        GameObject prefab,
        Vector3 position,
        Quaternion rotation,
        float lifetime,
        out PooledObject item)
    {
        using (PoolGetMarker.Auto())
        {
            Pool pool = GetOrCreatePool(prefab, 0, defaultMaxSize, strictByDefault);
            if (pool.Inactive.Count > 0)
            {
                item = pool.Inactive.Pop();
            }
            else if (!pool.Strict && pool.CreatedCount < pool.MaxSize)
            {
                item = CreateItem(pool);
            }
            else
            {
                poolMisses++;
                item = null;
                return false;
            }

            item.gameObject.SetActive(false);
            item.transform.SetParent(transform, false);
            item.PrepareForAcquire(position, rotation, lifetime);
            return true;
        }
    }

    public static void Activate(PooledObject item)
    {
        if (item == null || item.InPool)
        {
            return;
        }

        CombatObjectPool pool = GetOrCreate();
        if (item.RemainingLifetime > 0f)
        {
            item.TimedIndex = pool.timedObjects.Count;
            pool.timedObjects.Add(item);
        }

        item.gameObject.SetActive(true);
    }

    public static bool Release(GameObject instanceObject)
    {
        if (instanceObject == null
            || !instanceObject.TryGetComponent(out PooledObject pooledObject)
            || pooledObject.SourcePrefab == null)
        {
            return false;
        }

        pooledObject.Release();
        return true;
    }

    internal void Release(PooledObject item)
    {
        if (item == null || item.InPool)
        {
            return;
        }

        using (PoolReleaseMarker.Auto())
        {
            RemoveTimed(item);
            item.PrepareForRelease();
            item.gameObject.SetActive(false);
            item.transform.SetParent(inactiveRoot, false);

            if (pools.TryGetValue(item.SourcePrefab, out Pool pool)
                && pool.Inactive.Count < pool.MaxSize)
            {
                pool.Inactive.Push(item);
            }
            else
            {
                Destroy(item.gameObject);
            }
        }
    }

    private void Update()
    {
        float deltaTime = Time.deltaTime;
        for (int i = timedObjects.Count - 1; i >= 0; i--)
        {
            PooledObject item = timedObjects[i];
            if (item == null || item.InPool)
            {
                RemoveTimedAt(i);
                continue;
            }

            item.RemainingLifetime -= deltaTime;
            if (item.RemainingLifetime <= 0f)
            {
                Release(item);
            }
        }
    }

    private Pool GetOrCreatePool(
        GameObject prefab,
        int capacity,
        int maxSize,
        bool strict)
    {
        if (pools.TryGetValue(prefab, out Pool pool))
        {
            return pool;
        }

        pool = new Pool(prefab, capacity, Mathf.Max(1, maxSize), strict);
        pools.Add(prefab, pool);
        return pool;
    }

    private PooledObject CreateItem(Pool pool)
    {
        GameObject itemObject = Instantiate(pool.Prefab, inactiveRoot);
        itemObject.SetActive(false);
        PooledObject item = itemObject.GetComponent<PooledObject>();
        if (item == null)
        {
            item = itemObject.AddComponent<PooledObject>();
        }

        item.Initialize(this, pool.Prefab);
        pool.CreatedCount++;
        return item;
    }

    private void RemoveTimed(PooledObject item)
    {
        int index = item.TimedIndex;
        if ((uint)index < (uint)timedObjects.Count && timedObjects[index] == item)
        {
            RemoveTimedAt(index);
        }
        else
        {
            item.TimedIndex = -1;
        }
    }

    private void RemoveTimedAt(int index)
    {
        int lastIndex = timedObjects.Count - 1;
        PooledObject removed = timedObjects[index];
        PooledObject moved = timedObjects[lastIndex];
        timedObjects[index] = moved;
        timedObjects.RemoveAt(lastIndex);

        if (removed != null)
        {
            removed.TimedIndex = -1;
        }

        if (index < timedObjects.Count && moved != null)
        {
            moved.TimedIndex = index;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }
}
