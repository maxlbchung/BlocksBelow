using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.UI;

public class WaveSpawner : MonoBehaviour
{
    [Serializable]
    public class EnemySpawnData
    {
        [Tooltip("Enemy prefab that may be spawned during this wave.")]
        public GameObject enemyPrefab;

        [Min(1)]
        [Tooltip("How many of the wave's spawn credits this enemy costs.")]
        public int spawnCredits = 1;
    }

    [Serializable]
    public class Wave
    {
        [Tooltip("The enemy types enabled for this wave and their spawn-credit costs.")]
        public List<EnemySpawnData> enemiesEnabled = new List<EnemySpawnData>();

        [Min(0)]
        [Tooltip("Total spawn credits available to this wave.")]
        public int tokens = 10;

        [Min(1)]
        [Tooltip("The spawner tries to build a pool containing this many enemies.")]
        public int targetEnemyCount = 5;

        [Min(0)]
        [Tooltip("Mandatory birds added to this wave. These do not count toward the target enemy count or cost spawn credits.")]
        public int birdCount;

        [Min(0)]
        [Tooltip("Mandatory breakers added to this wave. These do not count toward the target enemy count or cost spawn credits.")]
        public int breakerCount;

        [Min(0f)]
        [Tooltip("Seconds from starting the wave until the final enemy is spawned.")]
        public float targetTime = 20f;
    }

    /// <summary>One enemy type in an upcoming wave, and how many of it will spawn.</summary>
    public struct WavePreviewEntry
    {
        public GameObject prefab;
        public int count;
    }

    public enum GameState
    {
        Building,
        Wave
    }

    private static readonly ProfilerMarker SpawnMarker =
        new ProfilerMarker("EnemySpawning.Spawn");
    private static readonly ProfilerMarker PoolPreparationMarker =
        new ProfilerMarker("EnemySpawning.Prewarm");

    [Header("Waves")]
    [SerializeField] private List<Wave> waves = new List<Wave>();

    [Header("Spawning")]
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float spawnRadius = 12f;
    [SerializeField, Range(0f, 360f), Tooltip("Width of the spawn arc, centred straight above the player. "
        + "180 spawns across the upper half only, so nothing appears below the player.")]
    private float spawnArcDegrees = 180f;
    [SerializeField] private GameObject bird;
    [SerializeField] private GameObject breaker;

    [Header("Pooling")]
    [SerializeField, Min(0), Tooltip("Minimum inactive instances prepared for each configured enemy type. "
        + "The hardest wave fields ~25 enemies across every type at once, so this only needs to cover "
        + "one type's share. Pools still grow on demand unless strictPrewarmedPools is set.")]
    private int prewarmPerEnemyType = 20;
    [SerializeField, Min(1), Tooltip("Hard size limit for each enemy and related projectile pool.")]
    private int maxPoolSizePerType = 512;
    [SerializeField, Tooltip("When enabled, a depleted pool records a miss and skips a spawn instead of instantiating.")]
    private bool strictPrewarmedPools;

    [Header("Building Mode")]
    [SerializeField] private TowerShopUI towerShop;
    [SerializeField] private SquarePlacement squarePlacement;

    // Created by the tower shop inside its own panel; resolved in Start.
    private Button startGameButton;

    [Header("Runtime")]
    [SerializeField, Tooltip("Starts round 1 as soon as the level loads, with no build phase "
        + "in front of it. Later rounds still wait for the Start Round button.")]
    private bool startFirstWaveImmediately = true;
    public GameState gameState = GameState.Wave;

    private static WaveSpawner instance;

    /// <summary>
    /// True while enemies are being fought. Towers hold their fire outside of it.
    /// Scenes without a WaveSpawner (e.g. stress tests) count as always active.
    /// </summary>
    public static bool IsWaveActive => instance == null || instance.gameState == GameState.Wave;

    private readonly List<Enemy> livingEnemies = new List<Enemy>(512);
    private readonly List<GameObject> spawnPool = new List<GameObject>(512);
    private readonly List<GameObject> previewPool = new List<GameObject>(64);
    private readonly List<EnemySpawnData> validEnemies = new List<EnemySpawnData>(16);
    private Coroutine spawnRoutine;
    private int currentWaveIndex = -1;
    private bool finishedSpawning;

    public int CurrentWaveIndex => currentWaveIndex;
    public int LivingEnemyCount => livingEnemies.Count;

    /// <summary>The round being fought right now, counting from 1.</summary>
    public int CurrentRoundNumber => Mathf.Max(1, currentWaveIndex + 1);

    /// <summary>The round the Start Round button would begin, counting from 1.</summary>
    public int NextRoundNumber => currentWaveIndex + 2;

    public int TotalRounds => waves.Count;

    public bool HasNextWave => currentWaveIndex + 1 < waves.Count;

    /// <summary>
    /// Fills <paramref name="results"/> with the enemy types the next wave will field and
    /// how many of each. Runs the same selection the wave itself will run, so the preview
    /// cannot drift from what actually spawns; only the spawn order is randomised later.
    /// </summary>
    public void GetNextWavePreview(List<WavePreviewEntry> results)
    {
        results.Clear();
        if (!HasNextWave)
        {
            return;
        }

        BuildSpawnPool(waves[currentWaveIndex + 1], previewPool);

        for (int i = 0; i < previewPool.Count; i++)
        {
            GameObject prefab = previewPool[i];
            if (prefab == null)
            {
                continue;
            }

            bool counted = false;
            for (int j = 0; j < results.Count; j++)
            {
                if (results[j].prefab == prefab)
                {
                    WavePreviewEntry entry = results[j];
                    entry.count++;
                    results[j] = entry;
                    counted = true;
                    break;
                }
            }

            if (!counted)
            {
                results.Add(new WavePreviewEntry { prefab = prefab, count = 1 });
            }
        }

        previewPool.Clear();
    }

    public void RemoveLivingEnemy(GameObject enemyObject)
    {
        if (enemyObject == null)
        {
            return;
        }

        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = livingEnemies[i];
            if (enemy == null || enemy.gameObject == enemyObject)
            {
                int lastIndex = livingEnemies.Count - 1;
                livingEnemies[i] = livingEnemies[lastIndex];
                livingEnemies.RemoveAt(lastIndex);
            }
        }
    }

    public void AddLivingEnemy(GameObject enemyObject)
    {
        if (gameState != GameState.Wave
            || enemyObject == null
            || !enemyObject.TryGetComponent(out Enemy enemy))
        {
            return;
        }

        for (int i = 0; i < livingEnemies.Count; i++)
        {
            if (livingEnemies[i] == enemy)
            {
                return;
            }
        }

        livingEnemies.Add(enemy);
    }

    private void Awake()
    {
        instance = this;

        // Statics survive a scene reload, so the run's counters start over here.
        RunStats.ResetRun();
    }

    private void Start()
    {
        if (player == null)
        {
            player = EnemySimulationManager.Instance.Player;
        }
        else
        {
            EnemySimulationManager.SetPlayer(player);
        }

        PreparePools();

        if (towerShop == null)
        {
            towerShop = FindFirstObjectByType<TowerShopUI>();
        }

        startGameButton = towerShop != null ? towerShop.StartRoundButton : null;
        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartNextWave);
        }

        if (startFirstWaveImmediately || gameState == GameState.Wave)
        {
            SetBuildingToolsEnabled(false);
            StartNextWave();
        }
        else
        {
            SetBuildingToolsEnabled(true);
        }
    }

    private void Update()
    {
        if (gameState != GameState.Wave)
        {
            return;
        }

        int blockingEnemies = 0;
        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = livingEnemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled)
            {
                int lastIndex = livingEnemies.Count - 1;
                livingEnemies[i] = livingEnemies[lastIndex];
                livingEnemies.RemoveAt(lastIndex);
                continue;
            }

            if (enemy.BlocksWaveCompletion)
            {
                blockingEnemies++;
            }
        }

        if (finishedSpawning && blockingEnemies == 0)
        {
            DespawnIdleEnemies();
            switchGameState(GameState.Building);
        }
    }

    /// <summary>
    /// Clears out whatever was not holding the round open - breakers still hunting for a
    /// cage. They spawned, which is what the wave's breaker count promises, but there is
    /// nothing left for them to do once every other enemy is gone.
    /// </summary>
    private void DespawnIdleEnemies()
    {
        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = livingEnemies[i];
            livingEnemies.RemoveAt(i);

            if (enemy != null && enemy.isActiveAndEnabled)
            {
                enemy.Despawn();
            }
        }
    }

    public void StartNextWave()
    {
        if (spawnRoutine != null || currentWaveIndex + 1 >= waves.Count)
        {
            if (currentWaveIndex + 1 >= waves.Count)
            {
                switchGameState(GameState.Building);

                if (startGameButton != null)
                {
                    startGameButton.interactable = false;
                }
            }

            return;
        }

        currentWaveIndex++;
        RunStats.RecordRoundStarted(currentWaveIndex + 1);
        gameState = GameState.Wave;
        finishedSpawning = false;
        livingEnemies.Clear();
        SetBuildingToolsEnabled(false);

        BuildSpawnPool(waves[currentWaveIndex], spawnPool);
        Shuffle(spawnPool);
        spawnRoutine = StartCoroutine(SpawnWave(waves[currentWaveIndex].targetTime));
    }

    private void PreparePools()
    {
        using (PoolPreparationMarker.Auto())
        {
            for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
            {
                Wave wave = waves[waveIndex];
                if (wave == null)
                {
                    continue;
                }

                for (int enemyIndex = 0; enemyIndex < wave.enemiesEnabled.Count; enemyIndex++)
                {
                    EnemySpawnData spawnData = wave.enemiesEnabled[enemyIndex];
                    if (spawnData == null || spawnData.enemyPrefab == null)
                    {
                        continue;
                    }

                    CombatObjectPool.Configure(
                        spawnData.enemyPrefab,
                        prewarmPerEnemyType,
                        maxPoolSizePerType,
                        strictPrewarmedPools);

                    if (spawnData.enemyPrefab.TryGetComponent(out Enemy enemyPrefab))
                    {
                        enemyPrefab.PreparePools(
                            prewarmPerEnemyType,
                            maxPoolSizePerType,
                        strictPrewarmedPools);
                    }
                }
            }

            PrepareEnemyPool(bird);
            PrepareEnemyPool(breaker);
        }
    }

    private void PrepareEnemyPool(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
        {
            return;
        }

        CombatObjectPool.Configure(
            enemyPrefab,
            prewarmPerEnemyType,
            maxPoolSizePerType,
            strictPrewarmedPools);

        if (enemyPrefab.TryGetComponent(out Enemy enemy))
        {
            enemy.PreparePools(
                prewarmPerEnemyType,
                maxPoolSizePerType,
                strictPrewarmedPools);
        }
    }

    private void BuildSpawnPool(Wave wave, List<GameObject> target)
    {
        target.Clear();
        validEnemies.Clear();

        for (int i = 0; i < wave.enemiesEnabled.Count; i++)
        {
            EnemySpawnData spawnData = wave.enemiesEnabled[i];
            if (spawnData != null
                && spawnData.enemyPrefab != null
                && spawnData.spawnCredits > 0)
            {
                validEnemies.Add(spawnData);
            }
        }

        // The list is tiny and this avoids a Comparison delegate allocation.
        for (int i = 1; i < validEnemies.Count; i++)
        {
            EnemySpawnData current = validEnemies[i];
            int insertAt = i - 1;
            while (insertAt >= 0
                && validEnemies[insertAt].spawnCredits < current.spawnCredits)
            {
                validEnemies[insertAt + 1] = validEnemies[insertAt];
                insertAt--;
            }

            validEnemies[insertAt + 1] = current;
        }

        if (validEnemies.Count > 0 && wave.tokens > 0)
        {
            int creditsRemaining = wave.tokens;
            EnemySpawnData cheapest = validEnemies[validEnemies.Count - 1];

            for (int slot = 0; slot < wave.targetEnemyCount && creditsRemaining > 0; slot++)
            {
                int slotsAfterThis = wave.targetEnemyCount - slot - 1;
                int spendableCredits = creditsRemaining - slotsAfterThis * cheapest.spawnCredits;
                EnemySpawnData selected = null;

                for (int i = 0; i < validEnemies.Count; i++)
                {
                    if (validEnemies[i].spawnCredits <= spendableCredits)
                    {
                        selected = validEnemies[i];
                        break;
                    }
                }

                selected ??= cheapest;
                target.Add(selected.enemyPrefab);
                creditsRemaining = Mathf.Max(0, creditsRemaining - selected.spawnCredits);
            }

            while (creditsRemaining > 0)
            {
                target.Add(cheapest.enemyPrefab);
                creditsRemaining = Mathf.Max(0, creditsRemaining - cheapest.spawnCredits);
            }
        }

        AddMandatorySpawns(target, bird, wave.birdCount);
        AddMandatorySpawns(target, breaker, wave.breakerCount);
    }

    private static void AddMandatorySpawns(List<GameObject> target, GameObject enemyPrefab, int count)
    {
        if (enemyPrefab == null)
        {
            return;
        }

        for (int i = 0; i < count; i++)
        {
            target.Add(enemyPrefab);
        }
    }

    private IEnumerator SpawnWave(float targetTime)
    {
        if (spawnPool.Count == 0)
        {
            spawnRoutine = null;
            finishedSpawning = true;
            yield break;
        }

        float delayBetweenSpawns = targetTime / spawnPool.Count;
        float nextSpawnTime = Time.time + delayBetweenSpawns;

        for (int i = 0; i < spawnPool.Count; i++)
        {
            while (delayBetweenSpawns > 0f && Time.time < nextSpawnTime)
            {
                yield return null;
            }

            SpawnEnemy(spawnPool[i]);
            nextSpawnTime += delayBetweenSpawns;
        }

        spawnRoutine = null;
        finishedSpawning = true;
    }

    private void SpawnEnemy(GameObject enemyPrefab)
    {
        if (enemyPrefab == null)
        {
            return;
        }

        using (SpawnMarker.Auto())
        {
            if (player == null)
            {
                player = EnemySimulationManager.Instance.Player;
            }

            Vector3 center = player != null ? player.position : transform.position;
            float halfArc = spawnArcDegrees * 0.5f * Mathf.Deg2Rad;
            float angle = UnityEngine.Random.Range(-halfArc, halfArc);
            Vector3 spawnPosition = center + (Vector3)(DirectionOnArc(angle) * spawnRadius);
            if (!CombatObjectPool.TryAcquire(
                    enemyPrefab,
                    spawnPosition,
                    Quaternion.identity,
                    0f,
                    out PooledObject pooledObject)
                || pooledObject.Enemy == null)
            {
                return;
            }

            Enemy enemy = pooledObject.Enemy;
            livingEnemies.Add(enemy);
            CombatObjectPool.Activate(pooledObject);
        }
    }

    /// <summary>Unit direction rotated <paramref name="offsetRadians"/> away from straight up.</summary>
    private static Vector2 DirectionOnArc(float offsetRadians)
    {
        return new Vector2(Mathf.Sin(offsetRadians), Mathf.Cos(offsetRadians));
    }

    private static void Shuffle(List<GameObject> items)
    {
        for (int i = items.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            GameObject temporary = items[i];
            items[i] = items[swapIndex];
            items[swapIndex] = temporary;
        }
    }

    public void switchGameState(GameState state)
    {
        if (state == GameState.Wave)
        {
            StartNextWave();
            return;
        }

        bool roundJustEnded = gameState == GameState.Wave;
        gameState = GameState.Building;
        SetBuildingToolsEnabled(true);

        // Pay after the shop is re-enabled so the payout effect lands on a visible canvas.
        if (roundJustEnded)
        {
            PayOutEnergyTowers();
        }
    }

    private static void PayOutEnergyTowers()
    {
        EnergyTower[] energyTowers = FindObjectsByType<EnergyTower>(FindObjectsSortMode.None);
        for (int i = 0; i < energyTowers.Length; i++)
        {
            energyTowers[i].PayOutRound();
        }
    }

    private void SetBuildingToolsEnabled(bool enabled)
    {
        if (towerShop != null)
        {
            towerShop.enabled = enabled;
        }

        if (squarePlacement != null)
        {
            squarePlacement.enabled = enabled;
        }

        if (startGameButton != null)
        {
            startGameButton.gameObject.SetActive(enabled);
            startGameButton.enabled = enabled;
            startGameButton.interactable = enabled && currentWaveIndex + 1 < waves.Count;
        }
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }

        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartNextWave);
        }
    }

    private void OnDrawGizmosSelected()
    {
        const int SegmentCount = 32;

        Gizmos.color = Color.red;
        Vector3 center = player != null ? player.position : transform.position;

        float halfArc = spawnArcDegrees * 0.5f * Mathf.Deg2Rad;
        float step = spawnArcDegrees * Mathf.Deg2Rad / SegmentCount;
        Vector3 previous = center + (Vector3)(DirectionOnArc(-halfArc) * spawnRadius);
        Gizmos.DrawLine(center, previous);

        for (int i = 1; i <= SegmentCount; i++)
        {
            Vector3 current = center + (Vector3)(DirectionOnArc(-halfArc + i * step) * spawnRadius);
            Gizmos.DrawLine(previous, current);
            previous = current;
        }

        Gizmos.DrawLine(center, previous);
    }
}
