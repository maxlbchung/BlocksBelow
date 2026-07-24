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

        [Min(0f)]
        [Tooltip("Seconds from starting the wave until the final enemy is spawned.")]
        public float targetTime = 20f;
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

    [Header("Pooling")]
    [SerializeField, Min(0), Tooltip("Minimum inactive instances prepared for each configured enemy type.")]
    private int prewarmPerEnemyType = 128;
    [SerializeField, Min(1), Tooltip("Hard size limit for each enemy and related projectile pool.")]
    private int maxPoolSizePerType = 512;
    [SerializeField, Tooltip("When enabled, a depleted pool records a miss and skips a spawn instead of instantiating.")]
    private bool strictPrewarmedPools;

    [Header("Building Mode")]
    [SerializeField] private TowerShopUI towerShop;
    [SerializeField] private SquarePlacement squarePlacement;
    [SerializeField] private Button startGameButton;

    [Header("Runtime")]
    public GameState gameState = GameState.Wave;

    private readonly List<Enemy> livingEnemies = new List<Enemy>(512);
    private readonly List<GameObject> spawnPool = new List<GameObject>(512);
    private readonly List<EnemySpawnData> validEnemies = new List<EnemySpawnData>(16);
    private Coroutine spawnRoutine;
    private int currentWaveIndex = -1;
    private bool finishedSpawning;

    public int CurrentWaveIndex => currentWaveIndex;
    public int LivingEnemyCount => livingEnemies.Count;

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

        if (startGameButton != null)
        {
            startGameButton.onClick.AddListener(StartNextWave);
        }

        if (gameState == GameState.Wave)
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

        for (int i = livingEnemies.Count - 1; i >= 0; i--)
        {
            Enemy enemy = livingEnemies[i];
            if (enemy == null || !enemy.isActiveAndEnabled)
            {
                int lastIndex = livingEnemies.Count - 1;
                livingEnemies[i] = livingEnemies[lastIndex];
                livingEnemies.RemoveAt(lastIndex);
            }
        }

        if (finishedSpawning && livingEnemies.Count == 0)
        {
            switchGameState(GameState.Building);
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
        gameState = GameState.Wave;
        finishedSpawning = false;
        livingEnemies.Clear();
        SetBuildingToolsEnabled(false);

        BuildSpawnPool(waves[currentWaveIndex]);
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
        }
    }

    private void BuildSpawnPool(Wave wave)
    {
        spawnPool.Clear();
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

        if (validEnemies.Count == 0 || wave.tokens <= 0)
        {
            return;
        }

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
            spawnPool.Add(selected.enemyPrefab);
            creditsRemaining = Mathf.Max(0, creditsRemaining - selected.spawnCredits);
        }

        while (creditsRemaining > 0)
        {
            spawnPool.Add(cheapest.enemyPrefab);
            creditsRemaining = Mathf.Max(0, creditsRemaining - cheapest.spawnCredits);
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
            Vector2 direction = UnityEngine.Random.insideUnitCircle;
            float directionLengthSquared = direction.sqrMagnitude;
            direction = directionLengthSquared > 0.000001f
                ? direction / Mathf.Sqrt(directionLengthSquared)
                : Vector2.right;

            Vector3 spawnPosition = center + (Vector3)(direction * spawnRadius);
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

        gameState = GameState.Building;
        SetBuildingToolsEnabled(true);
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
        if (startGameButton != null)
        {
            startGameButton.onClick.RemoveListener(StartNextWave);
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.red;
        Vector3 center = player != null ? player.position : transform.position;
        Gizmos.DrawWireSphere(center, spawnRadius);
    }
}
