using System;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

[DefaultExecutionOrder(-500)]
public sealed class EnemySimulationManager : MonoBehaviour
{
    private readonly struct CellKey : IEquatable<CellKey>
    {
        public readonly int X;
        public readonly int Y;

        public CellKey(int x, int y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(CellKey other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is CellKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X * 73856093) ^ (Y * 19349663);
            }
        }
    }

    private sealed class GridBucket
    {
        public readonly List<int> EnemyIndices = new List<int>(8);
    }

    private static readonly ProfilerMarker RegistrationMarker =
        new ProfilerMarker("EnemySimulation.Registration");
    private static readonly ProfilerMarker GridMarker =
        new ProfilerMarker("EnemySimulation.GridRebuild");
    private static readonly ProfilerMarker SteeringMarker =
        new ProfilerMarker("EnemySimulation.Steering");
    private static readonly ProfilerMarker DecisionMarker =
        new ProfilerMarker("EnemySimulation.Decisions");
    private static readonly ProfilerMarker MovementMarker =
        new ProfilerMarker("EnemySimulation.Movement");

    // Distance (squared) below which two enemies count as exactly overlapping and are pushed
    // apart along a deterministic direction instead of a normalized offset (avoids 0/0).
    private const float MinSeparationDistanceSquared = 0.0001f;

    private static EnemySimulationManager instance;

    [Header("Shared Target")]
    [SerializeField, Tooltip("Optional explicit player target. PlayerController registers itself automatically.")]
    private Transform player;

    [Header("Simulation Rates")]
    [SerializeField, Range(10f, 20f), Tooltip("Spatial-grid rebuilds and local separation updates per second.")]
    private float steeringRate = 15f;
    [SerializeField, Range(5f, 10f), Tooltip("Direct pursuit, range checks, and shooting decisions per enemy per second.")]
    private float perceptionRate = 7.5f;
    [SerializeField, Range(1f, 2f), Tooltip("Reserved rate for expensive path or strategic validation.")]
    private float strategicRate = 1f;
    [SerializeField, Range(1, 16), Tooltip("AI work is staggered across this many fixed-tick slices.")]
    private int decisionBatchCount = 4;

    [Header("Spatial Grid")]
    [SerializeField, Min(0.1f), Tooltip("Use a value near the common enemy separation radius.")]
    private float cellSize = 3f;
    [SerializeField, Range(1, 32), Tooltip("Maximum separation neighbors processed by one enemy per steering tick.")]
    private int maxNeighbors = 12;
    [SerializeField, Min(64), Tooltip("Initial registry and grid capacity. Set near the expected peak population.")]
    private int initialCapacity = 512;

    [Header("Separation Tuning")]
    [SerializeField, Min(0f), Tooltip("Constant separation push that tapers linearly to zero at each enemy's repulsion radius. This is the main 'keep them apart' base force - raise it to spread clustered melee enemies more evenly.")]
    private float separationBaseForce = 2f;
    [SerializeField, Min(0.01f), Tooltip("Hard cap on the total separation force one enemy receives per steering tick. Prevents a dense clump from launching bodies across the arena.")]
    private float maxSeparationForce = 6f;

    private readonly List<Enemy> enemies = new List<Enemy>(512);
    private readonly Dictionary<CellKey, GridBucket> grid =
        new Dictionary<CellKey, GridBucket>(512);
    private readonly Dictionary<Collider2D, Enemy> colliderOwners =
        new Dictionary<Collider2D, Enemy>(512);
    private readonly Dictionary<Rigidbody2D, Enemy> bodyOwners =
        new Dictionary<Rigidbody2D, Enemy>(512);
    private readonly List<GridBucket> activeBuckets = new List<GridBucket>(512);
    private readonly Stack<GridBucket> bucketPool = new Stack<GridBucket>(512);

    private float steeringAccumulator;
    private float decisionAccumulator;
    private float strategicAccumulator;
    private float simulationTime;
    private int decisionBucket;
    private int strategicBucket;
    private int registrationBucket;

    public static EnemySimulationManager Instance => GetOrCreate();
    public static EnemySimulationManager InstanceOrNull => instance;
    public Transform Player => player;
    public int ActiveEnemyCount => enemies.Count;
    public float SteeringRate => steeringRate;
    public float PerceptionRate => perceptionRate;
    public float StrategicRate => strategicRate;
    public float CellSize => cellSize;
    public int MaxNeighbors => maxNeighbors;
    public int DecisionBatchCount => decisionBatchCount;

    private static EnemySimulationManager GetOrCreate()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindFirstObjectByType<EnemySimulationManager>();
        if (instance != null)
        {
            return instance;
        }

        GameObject managerObject = new GameObject(nameof(EnemySimulationManager));
        instance = managerObject.AddComponent<EnemySimulationManager>();
        return instance;
    }

    public static void SetPlayer(Transform newPlayer)
    {
        GetOrCreate().player = newPlayer;
    }

    public static void ClearPlayer(Transform oldPlayer)
    {
        if (instance != null && instance.player == oldPlayer)
        {
            instance.player = null;
        }
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
        enemies.Capacity = Mathf.Max(enemies.Capacity, initialCapacity);
        activeBuckets.Capacity = Mathf.Max(activeBuckets.Capacity, initialCapacity);

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        int enemyLayer = LayerMask.NameToLayer("Enemy");
        if (enemyLayer >= 0)
        {
            Physics2D.IgnoreLayerCollision(enemyLayer, enemyLayer, true);
        }
    }

    internal void Register(Enemy enemy)
    {
        if (enemy == null || enemy.SimulationIndex >= 0)
        {
            return;
        }

        using (RegistrationMarker.Auto())
        {
            enemy.SimulationIndex = enemies.Count;
            enemy.DecisionBucket = registrationBucket++ % Mathf.Max(1, decisionBatchCount);
            enemy.LastDecisionTime = simulationTime;
            enemy.LastStrategicTime = simulationTime;
            enemies.Add(enemy);
            if (enemy.EnemyCollider != null)
            {
                colliderOwners[enemy.EnemyCollider] = enemy;
            }

            if (enemy.Body != null)
            {
                bodyOwners[enemy.Body] = enemy;
            }
        }
    }

    internal void Unregister(Enemy enemy)
    {
        if (enemy == null)
        {
            return;
        }

        using (RegistrationMarker.Auto())
        {
            int index = enemy.SimulationIndex;
            if ((uint)index >= (uint)enemies.Count || enemies[index] != enemy)
            {
                enemy.SimulationIndex = -1;
                return;
            }

            int lastIndex = enemies.Count - 1;
            Enemy movedEnemy = enemies[lastIndex];
            enemies[index] = movedEnemy;
            enemies.RemoveAt(lastIndex);
            enemy.SimulationIndex = -1;
            if (enemy.EnemyCollider != null)
            {
                colliderOwners.Remove(enemy.EnemyCollider);
            }

            if (enemy.Body != null)
            {
                bodyOwners.Remove(enemy.Body);
            }

            if (index < enemies.Count && movedEnemy != null)
            {
                movedEnemy.SimulationIndex = index;
            }
        }
    }

    private void FixedUpdate()
    {
        float fixedDeltaTime = Time.fixedDeltaTime;
        simulationTime += fixedDeltaTime;
        steeringAccumulator += fixedDeltaTime;
        decisionAccumulator += fixedDeltaTime;
        strategicAccumulator += fixedDeltaTime;

        float steeringInterval = 1f / Mathf.Max(1f, steeringRate);
        if (steeringAccumulator >= steeringInterval)
        {
            steeringAccumulator %= steeringInterval;
            RebuildGrid();
            UpdateSeparation();
        }

        int batches = Mathf.Max(1, decisionBatchCount);
        float decisionSliceInterval = 1f / (Mathf.Max(1f, perceptionRate) * batches);
        if (decisionAccumulator >= decisionSliceInterval)
        {
            decisionAccumulator %= decisionSliceInterval;
            UpdateDecisionBucket(decisionBucket);
            decisionBucket = (decisionBucket + 1) % batches;
        }

        float strategicSliceInterval = 1f / (Mathf.Max(0.1f, strategicRate) * batches);
        if (strategicAccumulator >= strategicSliceInterval)
        {
            strategicAccumulator %= strategicSliceInterval;
            UpdateStrategicBucket(strategicBucket);
            strategicBucket = (strategicBucket + 1) % batches;
        }

        ApplyMovement(fixedDeltaTime);
    }

    private void RebuildGrid()
    {
        using (GridMarker.Auto())
        {
            for (int i = 0; i < activeBuckets.Count; i++)
            {
                GridBucket bucket = activeBuckets[i];
                bucket.EnemyIndices.Clear();
                bucketPool.Push(bucket);
            }

            activeBuckets.Clear();
            grid.Clear();

            float inverseCellSize = 1f / Mathf.Max(0.1f, cellSize);
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || !enemy.IsSimulationActive)
                {
                    continue;
                }

                Vector2 position = enemy.Position;
                CellKey key = ToCell(position, inverseCellSize);
                if (!grid.TryGetValue(key, out GridBucket bucket))
                {
                    bucket = bucketPool.Count > 0 ? bucketPool.Pop() : new GridBucket();
                    grid.Add(key, bucket);
                    activeBuckets.Add(bucket);
                }

                bucket.EnemyIndices.Add(i);
            }
        }
    }

    private void UpdateSeparation()
    {
        using (SteeringMarker.Auto())
        {
            float inverseCellSize = 1f / Mathf.Max(0.1f, cellSize);
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || !enemy.IsSimulationActive)
                {
                    continue;
                }

                Vector2 position = enemy.Position;
                float radius = enemy.RepulsionRadius;
                float radiusSquared = radius * radius;
                float inverseRadius = 1f / Mathf.Max(0.0001f, radius);
                int cellRange = Mathf.Max(1, Mathf.CeilToInt(radius * inverseCellSize));
                CellKey centerCell = ToCell(position, inverseCellSize);
                Vector2 separation = Vector2.zero;
                int processedNeighbors = 0;

                for (int y = -cellRange; y <= cellRange && processedNeighbors < maxNeighbors; y++)
                {
                    for (int x = -cellRange; x <= cellRange && processedNeighbors < maxNeighbors; x++)
                    {
                        CellKey key = new CellKey(centerCell.X + x, centerCell.Y + y);
                        if (!grid.TryGetValue(key, out GridBucket bucket))
                        {
                            continue;
                        }

                        List<int> indices = bucket.EnemyIndices;
                        for (int bucketIndex = 0;
                             bucketIndex < indices.Count && processedNeighbors < maxNeighbors;
                             bucketIndex++)
                        {
                            int otherIndex = indices[bucketIndex];
                            if (otherIndex == i || (uint)otherIndex >= (uint)enemies.Count)
                            {
                                continue;
                            }

                            Enemy other = enemies[otherIndex];
                            if (other == null || !other.IsSimulationActive)
                            {
                                continue;
                            }

                            Vector2 offset = position - other.Position;
                            float distanceSquared = offset.sqrMagnitude;
                            if (distanceSquared >= radiusSquared)
                            {
                                continue;
                            }

                            // Resolve a unit push direction and a distance. The push magnitude is
                            // bounded below, so overlapping enemies get a firm-but-finite shove
                            // instead of the 1/distance spike that launched clumps apart.
                            Vector2 pushDirection;
                            float distance;
                            if (distanceSquared <= MinSeparationDistanceSquared)
                            {
                                pushDirection = DeterministicFallback(enemy, other);
                                distance = 0f;
                            }
                            else
                            {
                                distance = Mathf.Sqrt(distanceSquared);
                                pushDirection = offset / distance;
                            }

                            // Two bounded terms: a steep, per-enemy close-range shove plus a gentle
                            // linear base that provides even spacing across the whole radius.
                            float proximity = 1f - distance * inverseRadius; // 1 at contact -> 0 at radius
                            float closeRange = enemy.RepulsionForce
                                * Mathf.Pow(proximity, enemy.RepulsionFalloff);
                            float baseRange = separationBaseForce * proximity;
                            separation += pushDirection * (closeRange + baseRange);
                            processedNeighbors++;
                        }
                    }
                }

                // Clamp the summed force so a dense clump cannot fling a body across the arena.
                separation = Vector2.ClampMagnitude(separation, maxSeparationForce);
                enemy.SetSeparationForce(separation);
            }
        }
    }

    private void UpdateDecisionBucket(int bucket)
    {
        using (DecisionMarker.Auto())
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || !enemy.IsSimulationActive || enemy.DecisionBucket != bucket)
                {
                    continue;
                }

                float elapsed = Mathf.Max(0f, simulationTime - enemy.LastDecisionTime);
                enemy.LastDecisionTime = simulationTime;
                enemy.SimulateDecision(player, elapsed);
            }
        }
    }

    private void UpdateStrategicBucket(int bucket)
    {
        using (DecisionMarker.Auto())
        {
            for (int i = 0; i < enemies.Count; i++)
            {
                Enemy enemy = enemies[i];
                if (enemy == null || !enemy.IsSimulationActive || enemy.DecisionBucket != bucket)
                {
                    continue;
                }

                float elapsed = Mathf.Max(0f, simulationTime - enemy.LastStrategicTime);
                enemy.LastStrategicTime = simulationTime;
                enemy.SimulateStrategicDecision(player, elapsed);
            }
        }
    }

    private void ApplyMovement(float fixedDeltaTime)
    {
        using (MovementMarker.Auto())
        {
            for (int i = enemies.Count - 1; i >= 0; i--)
            {
                if ((uint)i >= (uint)enemies.Count)
                {
                    continue;
                }

                Enemy enemy = enemies[i];
                if (enemy == null)
                {
                    continue;
                }

                enemy.ApplySimulationStep(fixedDeltaTime);
            }
        }
    }

    public Enemy FindClosestEnemy(
        Vector2 origin,
        float radius,
        Enemy[] excludedEnemies = null,
        int excludedCount = 0)
    {
        float radiusSquared = radius * radius;
        float closestDistanceSquared = radiusSquared;
        Enemy closest = null;

        // This query is intentionally data-only and allocation-free. Strategic users
        // such as Tesla towers call it at a low rate.
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy == null || !enemy.IsSimulationActive
                || IsExcluded(enemy, excludedEnemies, excludedCount))
            {
                continue;
            }

            float distanceSquared = (enemy.Position - origin).sqrMagnitude;
            if (distanceSquared <= closestDistanceSquared)
            {
                closestDistanceSquared = distanceSquared;
                closest = enemy;
            }
        }

        return closest;
    }

    public Enemy FindEnemy(Collider2D collider)
    {
        if (collider == null)
        {
            return null;
        }

        if (colliderOwners.TryGetValue(collider, out Enemy enemy)
            && enemy != null
            && enemy.IsSimulationActive)
        {
            return enemy;
        }

        Rigidbody2D attachedBody = collider.attachedRigidbody;
        if (attachedBody != null
            && bodyOwners.TryGetValue(attachedBody, out enemy)
            && enemy != null
            && enemy.IsSimulationActive)
        {
            return enemy;
        }

        return null;
    }

    public void CopyActiveEnemies(List<Enemy> destination)
    {
        if (destination == null)
        {
            return;
        }

        destination.Clear();
        for (int i = 0; i < enemies.Count; i++)
        {
            Enemy enemy = enemies[i];
            if (enemy != null && enemy.IsSimulationActive)
            {
                destination.Add(enemy);
            }
        }
    }

    private static bool IsExcluded(Enemy enemy, Enemy[] excludedEnemies, int excludedCount)
    {
        if (excludedEnemies == null)
        {
            return false;
        }

        int count = Mathf.Min(excludedCount, excludedEnemies.Length);
        for (int i = 0; i < count; i++)
        {
            if (excludedEnemies[i] == enemy)
            {
                return true;
            }
        }

        return false;
    }

    private static CellKey ToCell(Vector2 position, float inverseCellSize)
    {
        return new CellKey(
            Mathf.FloorToInt(position.x * inverseCellSize),
            Mathf.FloorToInt(position.y * inverseCellSize));
    }

    private static Vector2 DeterministicFallback(Enemy first, Enemy second)
    {
        unchecked
        {
            uint hash = (uint)(first.GetInstanceID() * 73856093)
                ^ (uint)(second.GetInstanceID() * 19349663);
            switch (hash & 7u)
            {
                case 0: return Vector2.right;
                case 1: return Vector2.left;
                case 2: return Vector2.up;
                case 3: return Vector2.down;
                case 4: return new Vector2(0.70710678f, 0.70710678f);
                case 5: return new Vector2(-0.70710678f, 0.70710678f);
                case 6: return new Vector2(0.70710678f, -0.70710678f);
                default: return new Vector2(-0.70710678f, -0.70710678f);
            }
        }
    }

    private void OnDestroy()
    {
        if (instance != this)
        {
            return;
        }

        for (int i = 0; i < enemies.Count; i++)
        {
            if (enemies[i] != null)
            {
                enemies[i].SimulationIndex = -1;
            }
        }

        enemies.Clear();
        colliderOwners.Clear();
        bodyOwners.Clear();
        grid.Clear();
        activeBuckets.Clear();
        bucketPool.Clear();
        instance = null;
    }
}
