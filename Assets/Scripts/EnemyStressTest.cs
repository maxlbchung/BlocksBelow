using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

public sealed class EnemyStressTest : MonoBehaviour
{
    public enum StressScenario
    {
        AllVisible,
        DenseCluster,
        ChokePoint,
        ProjectileHeavy,
        Offscreen
    }

    private static readonly ProfilerMarker StressSpawnMarker =
        new ProfilerMarker("EnemyStressTest.Spawn");

    [Header("Population")]
    [SerializeField] private GameObject[] enemyPrefabs;
    [SerializeField] private StressScenario scenario = StressScenario.AllVisible;
    [SerializeField] private Transform center;
    [SerializeField, Min(0.1f)] private float visibleRadius = 8f;
    [SerializeField, Min(0.01f)] private float denseClusterRadius = 0.35f;
    [SerializeField] private Vector2 offscreenOffset = new Vector2(100f, 0f);

    [Header("Projectile-Heavy Scenario")]
    [SerializeField] private Projectile towerProjectilePrefab;
    [SerializeField, Min(0)] private int projectileCount = 500;
    [SerializeField] private float projectileDamage;

    [Header("Pool Limits")]
    [SerializeField, Min(1)] private int maximumPopulation = 500;
    [SerializeField, Tooltip("A miss skips a spawn, making an undersized prewarm visible in the Pool Misses counter.")]
    private bool strictPrewarmedMode = true;

    private readonly List<Enemy> spawnedEnemies = new List<Enemy>(500);
    private readonly List<Projectile> spawnedProjectiles = new List<Projectile>(512);

    public int SpawnedEnemyCount => spawnedEnemies.Count;
    public int PoolMisses => CombatObjectPool.Instance.PoolMisses;

    [ContextMenu("Spawn 100 Enemies")]
    public void Spawn100()
    {
        SpawnPopulation(100);
    }

    [ContextMenu("Spawn 250 Enemies")]
    public void Spawn250()
    {
        SpawnPopulation(250);
    }

    [ContextMenu("Spawn 500 Enemies")]
    public void Spawn500()
    {
        SpawnPopulation(500);
    }

    [ContextMenu("Release Stress Objects")]
    public void ReleaseAll()
    {
        for (int i = spawnedEnemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = spawnedEnemies[i];
            if (enemy != null && enemy.gameObject.activeSelf)
            {
                if (!CombatObjectPool.Release(enemy.gameObject))
                {
                    enemy.gameObject.SetActive(false);
                }
            }
        }

        for (int i = spawnedProjectiles.Count - 1; i >= 0; i--)
        {
            Projectile projectile = spawnedProjectiles[i];
            if (projectile != null && projectile.gameObject.activeSelf)
            {
                if (!CombatObjectPool.Release(projectile.gameObject))
                {
                    projectile.gameObject.SetActive(false);
                }
            }
        }

        spawnedEnemies.Clear();
        spawnedProjectiles.Clear();
    }

    public void SpawnPopulation(int requestedCount)
    {
        ReleaseAll();
        int count = Mathf.Clamp(requestedCount, 0, maximumPopulation);
        if (count == 0 || enemyPrefabs == null || enemyPrefabs.Length == 0)
        {
            return;
        }

        using (StressSpawnMarker.Auto())
        {
            PreparePools(count);
            Vector2 origin = center != null ? center.position : transform.position;

            for (int i = 0; i < count; i++)
            {
                GameObject prefab = enemyPrefabs[i % enemyPrefabs.Length];
                if (prefab == null)
                {
                    continue;
                }

                Vector2 spawnPosition = GetSpawnPosition(origin, i, count);
                if (!CombatObjectPool.TryAcquire(
                        prefab,
                        spawnPosition,
                        Quaternion.identity,
                        0f,
                        out PooledObject pooledObject)
                    || pooledObject.Enemy == null)
                {
                    continue;
                }

                spawnedEnemies.Add(pooledObject.Enemy);
                CombatObjectPool.Activate(pooledObject);
            }

            if (scenario == StressScenario.ProjectileHeavy)
            {
                SpawnProjectiles(origin);
            }
        }
    }

    private void PreparePools(int count)
    {
        int perType = Mathf.CeilToInt(count / (float)Mathf.Max(1, enemyPrefabs.Length));
        for (int i = 0; i < enemyPrefabs.Length; i++)
        {
            GameObject prefab = enemyPrefabs[i];
            if (prefab == null)
            {
                continue;
            }

            CombatObjectPool.Configure(
                prefab,
                perType,
                maximumPopulation,
                strictPrewarmedMode);

            if (prefab.TryGetComponent(out Enemy enemyPrefab))
            {
                enemyPrefab.PreparePools(
                    perType,
                    Mathf.Max(maximumPopulation, projectileCount),
                    strictPrewarmedMode);
            }
        }

        if (towerProjectilePrefab != null)
        {
            CombatObjectPool.Configure(
                towerProjectilePrefab.gameObject,
                projectileCount,
                Mathf.Max(1, projectileCount),
                strictPrewarmedMode);
        }
    }

    private Vector2 GetSpawnPosition(Vector2 origin, int index, int count)
    {
        float normalized = count > 1 ? index / (float)(count - 1) : 0f;
        float angle = index * 2.39996323f;
        Vector2 radial = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));

        switch (scenario)
        {
            case StressScenario.DenseCluster:
                return origin + radial * denseClusterRadius * Mathf.Sqrt(normalized);

            case StressScenario.ChokePoint:
                float side = (index & 1) == 0 ? -1f : 1f;
                return origin + new Vector2(
                    side * (1.5f + normalized * visibleRadius),
                    radial.y * denseClusterRadius);

            case StressScenario.Offscreen:
                return origin + offscreenOffset
                    + radial * visibleRadius * Mathf.Sqrt(normalized);

            default:
                return origin + radial * visibleRadius * Mathf.Sqrt(normalized);
        }
    }

    private void SpawnProjectiles(Vector2 origin)
    {
        if (towerProjectilePrefab == null)
        {
            return;
        }

        for (int i = 0; i < projectileCount; i++)
        {
            float angle = i * 2.39996323f;
            Vector2 direction = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            Vector2 position = origin - direction * (visibleRadius + 1f);
            Projectile projectile = Projectile.Spawn(
                towerProjectilePrefab,
                position,
                Quaternion.identity,
                direction,
                projectileDamage);
            if (projectile != null)
            {
                spawnedProjectiles.Add(projectile);
            }
        }
    }
}
