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
        [Tooltip("Enemy prefab spawned during this wave.")]
        public GameObject enemyPrefab;

        [Min(0)]
        [Tooltip("Exact number of this enemy spawned during the wave.")]
        public int count;
    }

    [Serializable]
    public class Wave
    {
        [Tooltip("Exact number of each regular enemy spawned during this wave.")]
        public List<EnemySpawnData> enemies = new List<EnemySpawnData>();

        [Min(0)]
        [Tooltip("Exact number of birds spawned during this wave.")]
        public int birdCount;

        [Min(0)]
        [Tooltip("Exact number of cage breakers spawned during this wave.")]
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

    /// <summary>Loaded by name, because no wave references the big enemy for this to borrow.</summary>
    private const string BigEnemyResourcePath = "BigEnemy";
    private const string BuildingMusic = "BuildingTheme";
    private const string FightingMusic = "FightingTheme";
    private const float MusicCrossfadeSeconds = 1.5f;

    private static readonly ProfilerMarker SpawnMarker =
        new ProfilerMarker("EnemySpawning.Spawn");
    private static readonly ProfilerMarker PoolPreparationMarker =
        new ProfilerMarker("EnemySpawning.Prewarm");

    [Header("Waves")]
    [SerializeField] private List<Wave> waves = new List<Wave>();
    [SerializeField] private float timeForFirstWave = 0f;
    [SerializeField, Min(1), Tooltip("The round the campaign is aimed at: clearing it takes the "
        + "enemy leader and shows the victory screen. Rounds past it are extra, and the last "
        + "round on the list is marked the same way when it is cleared.")]
    private int bossRound = 20;

    [Header("Spawning")]
    [SerializeField] private Transform player;
    [SerializeField, Min(0f)] private float spawnRadius = 12f;
    [SerializeField, Range(0f, 360f), Tooltip("Width of the spawn arc, centred straight above the player. "
        + "180 spawns across the upper half only, so nothing appears below the player.")]
    private float spawnArcDegrees = 180f;
    [SerializeField] private GameObject bird;
    [SerializeField] private GameObject breaker;
    [SerializeField, Tooltip("Fielded in bulk when the player gives up. Left empty, the big "
        + "enemy is loaded from Resources, so this only needs setting to field something else.")]
    private GameObject lastStandBoss;

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

    /// <summary>The spawner in the loaded scene, or null in scenes that field no waves.</summary>
    internal static WaveSpawner InstanceOrNull => instance;

    private readonly List<Enemy> livingEnemies = new List<Enemy>(512);
    private readonly List<GameObject> spawnPool = new List<GameObject>(512);
    private readonly List<GameObject> previewPool = new List<GameObject>(64);
    private Coroutine spawnRoutine;
    private int currentWaveIndex = -1;
    private bool finishedSpawning;
    private bool firstWaveHeld;

    /// <summary>
    /// The round the victory screen was last shown for, so a round cannot be celebrated
    /// twice - a last stand survived after a landmark round would otherwise end that
    /// same round a second time.
    /// </summary>
    private int celebratedRound = -1;

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

        // Statics survive a scene reload, so the run's counters start over here. The
        // same goes for the retry snapshot, which would otherwise point at cages and a
        // player belonging to the scene that was just unloaded.
        RunStats.ResetRun();
        RoundSnapshot.Clear();
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
            StartCoroutine(StartFirstWave());
        }
        else
        {
            SetBuildingToolsEnabled(true);
        }
    }

    /// <summary>
    /// Keeps the opening round from starting until <see cref="ReleaseFirstWave"/> is called.
    /// The tutorial's opening card holds it while the player reads, so the birds it is about
    /// are not already in the air behind it. Only has an effect if it is set before
    /// <see cref="Start"/>, which any other component's Awake is.
    /// </summary>
    public void HoldFirstWave()
    {
        firstWaveHeld = true;
    }

    public void ReleaseFirstWave()
    {
        firstWaveHeld = false;
    }

    private IEnumerator StartFirstWave()
    {
        if (firstWaveHeld)
        {
            // A hold replaces the opening pause rather than being added to it: whoever is
            // holding is already filling that moment, and serving the wait afterwards would
            // leave the player looking at an empty field having just asked to begin.
            while (firstWaveHeld)
            {
                yield return null;
            }
        }
        else
        {
            yield return new WaitForSeconds(timeForFirstWave);
        }

        StartNextWave();
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
        // Wave zero opens in silence. Music begins only after that first fight has
        // been cleared and its following build phase has begun.
        if (currentWaveIndex > 0)
        {
            AudioController.CrossfadeMusic(FightingMusic, MusicCrossfadeSeconds);
        }
        finishedSpawning = false;
        livingEnemies.Clear();
        SetBuildingToolsEnabled(false);

        // Taken here, at the last moment before anything is spawned, so it records the
        // board exactly as the player left it in the build phase.
        RoundSnapshot.Capture();

        BuildSpawnPool(waves[currentWaveIndex], spawnPool);
        Shuffle(spawnPool);
        spawnRoutine = StartCoroutine(SpawnWave(waves[currentWaveIndex].targetTime));
    }

    /// <summary>
    /// Runs the current round again from the top. The world is put back by
    /// <see cref="RoundSnapshot"/> first; this only rewinds the spawner itself, which
    /// then starts the round over as if it had never been begun.
    /// </summary>
    internal void RetryCurrentRound()
    {
        if (spawnRoutine != null)
        {
            // StartNextWave refuses to run while a spawn routine is live, and this one
            // is part-way through a wave that is being thrown away.
            StopCoroutine(spawnRoutine);
            spawnRoutine = null;
        }

        finishedSpawning = false;
        livingEnemies.Clear();

        // Stepped back so StartNextWave lands on the same wave rather than the next one.
        currentWaveIndex--;
        StartNextWave();
    }

    /// <summary>
    /// Fields one last hopeless fight all at once, for <see cref="GiveUpPrompt"/>: the
    /// player asked to be finished off rather than walk a board that cannot shoot back.
    /// <para>
    /// The swarm joins <c>livingEnemies</c> like any wave, so the round ends normally in
    /// the unlikely event the player survives it.
    /// </para>
    /// </summary>
    public void SpawnLastStand(int bossCount, int gruntCount)
    {
        // No wave fields a big enemy, so there is no prefab reference in the scene to
        // borrow. It lives under Resources purely so this can reach it without the
        // spawner having to be wired up by hand.
        GameObject boss = lastStandBoss != null
            ? lastStandBoss
            : Resources.Load<GameObject>(BigEnemyResourcePath);
        GameObject grunt = FindWavePrefab<BasicEnemy>(exactType: true);

        if (boss == null)
        {
            Debug.LogWarning(
                $"No big enemy to field: Resources/{BigEnemyResourcePath} is missing and no "
                + "prefab is set on Last Stand Boss. Giving up will send the small enemies only.",
                this);
        }

        // Far more of each type than any wave fields, so the pools are grown up front
        // rather than instantiating mid-burst - that would be one hitch per enemy.
        PrepareLastStandPool(boss, bossCount);
        PrepareLastStandPool(grunt, gruntCount);

        // Towers only fire during a wave, and the shop has no business being open while
        // this lands. StartNextWave is deliberately not used: no new round is beginning.
        gameState = GameState.Wave;
        AudioController.CrossfadeMusic(FightingMusic, MusicCrossfadeSeconds);
        SetBuildingToolsEnabled(false);

        // The bosses come in behind the grunts, so the wall arrives before the weight.
        SpawnBurst(grunt, gruntCount, spawnRadius);
        SpawnBurst(boss, bossCount, spawnRadius * 1.4f);

        // Nothing further is coming, so the round is free to end once this is cleared.
        finishedSpawning = true;
    }

    /// <summary>
    /// Spreads <paramref name="count"/> enemies evenly across the spawn arc. Even rather
    /// than random, because fifty random angles clump, and a clump arrives as one shove
    /// instead of a wall.
    /// </summary>
    private void SpawnBurst(GameObject enemyPrefab, int count, float radius)
    {
        if (enemyPrefab == null || count <= 0)
        {
            return;
        }

        float halfArc = spawnArcDegrees * 0.5f * Mathf.Deg2Rad;
        for (int i = 0; i < count; i++)
        {
            float alongArc = count > 1 ? i / (float)(count - 1) : 0.5f;
            // Staggered over three rings so neighbours are not shoulder to shoulder,
            // shoving each other off the arc the moment they arrive.
            float ringOffset = (i % 3) * 1.25f;
            SpawnEnemyAt(enemyPrefab, Mathf.Lerp(-halfArc, halfArc, alongArc), radius + ringOffset);
        }
    }

    private void PrepareLastStandPool(GameObject enemyPrefab, int count)
    {
        if (enemyPrefab == null || count <= 0)
        {
            return;
        }

        int size = Mathf.Max(maxPoolSizePerType, count);
        CombatObjectPool.Configure(enemyPrefab, count, size, false);

        if (enemyPrefab.TryGetComponent(out Enemy enemy))
        {
            enemy.PreparePools(count, size, false);
        }
    }

    /// <summary>
    /// The first prefab in the wave list carrying <typeparamref name="T"/>, or null when
    /// no wave fields one. Reuses what the rounds already reference rather than asking the
    /// scene to wire the same prefab up a second time.
    /// <para>
    /// <paramref name="exactType"/> rejects subclasses. <see cref="BigEnemy"/> is a
    /// <see cref="BasicEnemy"/>, so a wave that fields one would otherwise answer a
    /// request for small enemies with the boss.
    /// </para>
    /// </summary>
    private GameObject FindWavePrefab<T>(bool exactType = false) where T : Component
    {
        for (int waveIndex = 0; waveIndex < waves.Count; waveIndex++)
        {
            Wave wave = waves[waveIndex];
            if (wave == null)
            {
                continue;
            }

            for (int enemyIndex = 0; enemyIndex < wave.enemies.Count; enemyIndex++)
            {
                EnemySpawnData spawnData = wave.enemies[enemyIndex];
                if (spawnData == null || spawnData.enemyPrefab == null)
                {
                    continue;
                }

                T match = spawnData.enemyPrefab.GetComponent<T>();
                if (match != null && (!exactType || match.GetType() == typeof(T)))
                {
                    return spawnData.enemyPrefab;
                }
            }
        }

        return null;
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

                for (int enemyIndex = 0; enemyIndex < wave.enemies.Count; enemyIndex++)
                {
                    EnemySpawnData spawnData = wave.enemies[enemyIndex];
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

        for (int i = 0; i < wave.enemies.Count; i++)
        {
            EnemySpawnData spawnData = wave.enemies[i];
            if (spawnData != null)
            {
                AddMandatorySpawns(target, spawnData.enemyPrefab, spawnData.count);
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
        float halfArc = spawnArcDegrees * 0.5f * Mathf.Deg2Rad;
        SpawnEnemyAt(enemyPrefab, UnityEngine.Random.Range(-halfArc, halfArc), spawnRadius);
    }

    /// <summary>
    /// Places one enemy on the spawn arc at <paramref name="angleRadians"/> from straight
    /// up, <paramref name="radius"/> out from the player. Waves pick the angle at random;
    /// <see cref="SpawnLastStand"/> spaces its own out instead.
    /// </summary>
    private void SpawnEnemyAt(GameObject enemyPrefab, float angleRadians, float radius)
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
            Vector3 spawnPosition = center + (Vector3)(DirectionOnArc(angleRadians) * radius);
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
        AudioController.CrossfadeMusic(BuildingMusic, MusicCrossfadeSeconds);
        SetBuildingToolsEnabled(true);

        // Pay after the shop is re-enabled so the payout effect lands on a visible canvas.
        if (roundJustEnded)
        {
            RestorePlayerHealth();
            PayOutEnergyTowers();
            // Last, so the screen it may put up pauses a board that is already settled:
            // healed, paid out, and back in its build phase behind the panel.
            ShowVictoryIfLandmarkRoundCleared();
        }
    }

    /// <summary>
    /// Puts the victory screen up for the two rounds a run is built around - the one that
    /// takes the enemy leader, and the last round on the list. Every other round ends
    /// quietly.
    /// </summary>
    private void ShowVictoryIfLandmarkRoundCleared()
    {
        // Below zero when no round has been fought, which a scene fielding no waves at all
        // reaches on its first state change.
        if (currentWaveIndex < 0)
        {
            return;
        }

        int roundCleared = currentWaveIndex + 1;
        bool finalRound = !HasNextWave;
        if (roundCleared == celebratedRound || (roundCleared != bossRound && !finalRound))
        {
            return;
        }

        // A player killed as the last enemy fell has the game over screen already on its
        // way, and that is the ending the run actually got.
        PlayerController playerController = ResolvePlayerController();
        if (playerController != null && !playerController.IsAlive)
        {
            return;
        }

        celebratedRound = roundCleared;
        VictoryScreen.Show(roundCleared, finalRound);
    }

    private void RestorePlayerHealth()
    {
        ResolvePlayerController()?.RestoreFullHealth();
    }

    private PlayerController ResolvePlayerController()
    {
        return player != null
            ? player.GetComponent<PlayerController>()
            : FindFirstObjectByType<PlayerController>();
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
