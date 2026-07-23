using System;
using System.Collections;
using System.Collections.Generic;
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

    [Header("Waves")]
    [SerializeField] private List<Wave> waves = new List<Wave>();

    [Header("Spawning")]
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float spawnRadius = 12f;

    [Header("Building Mode")]
    [SerializeField] private TowerShopUI towerShop;
    [SerializeField] private SquarePlacement squarePlacement;
    [SerializeField] private Button startGameButton;

    [Header("Runtime")]
    public GameState gameState = GameState.Wave;

    private readonly List<GameObject> livingEnemies = new List<GameObject>();
    private Coroutine spawnRoutine;
    private int currentWaveIndex = -1;
    private bool finishedSpawning;

    public int CurrentWaveIndex => currentWaveIndex;
    public int LivingEnemyCount => livingEnemies.Count;

    private void Start()
    {
        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

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

        // Destroyed Unity objects compare equal to null, so this also catches
        // enemies destroyed by systems other than the Enemy script.
        livingEnemies.RemoveAll(enemy => enemy == null);
        livingEnemies.RemoveAll(enemy => enemy.GetComponent<Enemy>().enabled == false);

        Debug.Log($"Wave {currentWaveIndex + 1} - Living Enemies: {livingEnemies.Count}, Finished Spawning: {finishedSpawning}");

        if (finishedSpawning && livingEnemies.Count == 0)
        {
            Debug.Log("Switched");
            switchGameState(GameState.Building);
        }
    }

    public void StartNextWave()
    {
        if (spawnRoutine != null || currentWaveIndex + 1 >= waves.Count)
        {
            if (currentWaveIndex + 1 >= waves.Count)
            {
                Debug.Log("All configured waves have been completed.", this);
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

        List<GameObject> spawnPool = BuildSpawnPool(waves[currentWaveIndex]);
        Shuffle(spawnPool);
        spawnRoutine = StartCoroutine(SpawnWave(spawnPool, waves[currentWaveIndex].targetTime));
    }

    private List<GameObject> BuildSpawnPool(Wave wave)
    {
        List<EnemySpawnData> validEnemies = wave.enemiesEnabled.FindAll(
            enemy => enemy != null && enemy.enemyPrefab != null && enemy.spawnCredits > 0
        );

        validEnemies.Sort((left, right) => right.spawnCredits.CompareTo(left.spawnCredits));

        List<GameObject> pool = new List<GameObject>();
        if (validEnemies.Count == 0 || wave.tokens <= 0)
        {
            return pool;
        }

        int creditsRemaining = wave.tokens;
        int lowestCost = validEnemies[validEnemies.Count - 1].spawnCredits;
        GameObject lowestCostEnemy = validEnemies[validEnemies.Count - 1].enemyPrefab;

        // Fill the requested number of slots with the most expensive enemies
        // possible while reserving enough credits for the remaining slots.
        for (int slot = 0; slot < wave.targetEnemyCount && creditsRemaining > 0; slot++)
        {
            int slotsAfterThis = wave.targetEnemyCount - slot - 1;
            int spendableCredits = creditsRemaining - (slotsAfterThis * lowestCost);
            EnemySpawnData selected = null;

            for (int i = 0; i < validEnemies.Count; i++)
            {
                if (validEnemies[i].spawnCredits <= spendableCredits)
                {
                    selected = validEnemies[i];
                    break;
                }
            }

            // The target count cannot be fully funded. Use the cheapest enemy
            // and let it consume the last partial credit balance.
            if (selected == null)
            {
                selected = validEnemies[validEnemies.Count - 1];
            }

            pool.Add(selected.enemyPrefab);
            creditsRemaining = Mathf.Max(0, creditsRemaining - selected.spawnCredits);
        }

        // If the requested count cannot consume the budget exactly, going over
        // the count with the cheapest enemy is preferable to leaving credits.
        while (creditsRemaining > 0)
        {
            pool.Add(lowestCostEnemy);
            creditsRemaining = Mathf.Max(0, creditsRemaining - lowestCost);
        }

        return pool;
    }

    private IEnumerator SpawnWave(List<GameObject> spawnPool, float targetTime)
    {
        if (spawnPool.Count == 0)
        {
            spawnRoutine = null;
            finishedSpawning = true;
            yield break;
        }

        float delayBetweenSpawns = targetTime / spawnPool.Count;

        for (int i = 0; i < spawnPool.Count; i++)
        {
            if (delayBetweenSpawns > 0f)
            {
                yield return new WaitForSeconds(delayBetweenSpawns);
            }

            SpawnEnemy(spawnPool[i]);
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

        if (player == null)
        {
            GameObject playerObject = GameObject.FindGameObjectWithTag("Player");
            if (playerObject != null)
            {
                player = playerObject.transform;
            }
        }

        Vector3 center = player != null ? player.position : transform.position;
        Vector2 direction = UnityEngine.Random.insideUnitCircle.normalized;
        if (direction == Vector2.zero)
        {
            direction = Vector2.right;
        }

        Vector3 spawnPosition = center + (Vector3)(direction * spawnRadius);
        GameObject spawnedEnemy = Instantiate(enemyPrefab, spawnPosition, Quaternion.identity);
        livingEnemies.Add(spawnedEnemy);
    }

    private static void Shuffle(List<GameObject> spawnPool)
    {
        for (int i = spawnPool.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            GameObject temporary = spawnPool[i];
            spawnPool[i] = spawnPool[swapIndex];
            spawnPool[swapIndex] = temporary;
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
