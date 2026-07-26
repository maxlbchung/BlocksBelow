using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The state a round opened in, kept so the game over screen can offer a retry of that
/// round instead of the whole run.
/// <para>
/// Only what a round actually changes is recorded. Towers are not: nothing destroys a
/// placed tower mid-round, so the board still holds whatever was built before the round
/// began and the layout needs no restoring. Energy is recorded but rarely differs - the
/// shop is closed for the length of a round and energy towers only pay out once one
/// ends - so it is covered for safety rather than because a round is expected to move it.
/// </para>
/// <para>
/// Static, so it survives the reload behind Play Again; <see cref="Clear"/> drops the
/// stale references that would otherwise leave behind.
/// </para>
/// </summary>
public static class RoundSnapshot
{
    /// <summary>One cage as it stood when the round began.</summary>
    private readonly struct CagedState
    {
        public readonly CageTower Cage;
        public readonly CageTower.CageState State;

        /// <summary>
        /// The prefab behind the bird the cage held, which is what lets a replacement be
        /// drawn from the pool. Null unless <see cref="State"/> is Full.
        /// </summary>
        public readonly GameObject CapturedPrefab;

        public CagedState(CageTower cage, CageTower.CageState state, GameObject capturedPrefab)
        {
            Cage = cage;
            State = state;
            CapturedPrefab = capturedPrefab;
        }
    }

    private static readonly List<CagedState> cages = new List<CagedState>(32);
    private static readonly List<CageTower> cageBuffer = new List<CageTower>(32);
    private static readonly List<Enemy> enemyBuffer = new List<Enemy>(128);

    private static PlayerController player;
    private static TowerShopUI shop;
    private static float playerHealth;
    private static Vector3 playerPosition;
    private static int enemiesDefeated;
    private static int energy;
    private static bool captured;

    /// <summary>
    /// True while there is a round to go back to. False in a scene with no spawner, and
    /// before the first round of a run has started.
    /// </summary>
    public static bool CanRetry => captured && WaveSpawner.InstanceOrNull != null;

    /// <summary>Forgets the recorded round. Called when a scene starts.</summary>
    public static void Clear()
    {
        cages.Clear();
        player = null;
        shop = null;
        captured = false;
    }

    /// <summary>Records the state the round starting now is being fought from.</summary>
    public static void Capture()
    {
        player = Object.FindFirstObjectByType<PlayerController>();
        shop = Object.FindFirstObjectByType<TowerShopUI>();

        playerHealth = player != null ? player.health : 0f;
        playerPosition = player != null ? player.transform.position : Vector3.zero;
        energy = shop != null ? shop.Energy : 0;
        enemiesDefeated = RunStats.EnemiesDefeated;

        cages.Clear();
        TowerGrid.CollectCages(cageBuffer);
        for (int i = 0; i < cageBuffer.Count; i++)
        {
            CageTower cage = cageBuffer[i];
            if (cage != null)
            {
                cages.Add(new CagedState(cage, cage.State, GetSourcePrefab(cage.CapturedEnemy)));
            }
        }

        cageBuffer.Clear();
        captured = true;
    }

    /// <summary>
    /// Puts the world back to the recorded state and starts that round over. Returns
    /// false when there is nothing to go back to, leaving the world untouched so the
    /// caller can fall back to restarting the run.
    /// </summary>
    public static bool Restore()
    {
        WaveSpawner spawner = WaveSpawner.InstanceOrNull;
        if (!captured || spawner == null)
        {
            return false;
        }

        // Ordered: the field is swept first so a bird freed from a broken cage during
        // the failed attempt is gone before the cages are put back, and the player is
        // stood up before the spawner starts sending the round at them.
        ClearField();
        RestoreCages();
        RestorePlayer();
        RestoreEnergy();
        RunStats.RestoreEnemiesDefeated(enemiesDefeated);

        spawner.RetryCurrentRound();
        return true;
    }

    /// <summary>Takes every enemy and shot still in play off the field, quietly.</summary>
    private static void ClearField()
    {
        EnemySimulationManager simulation = EnemySimulationManager.InstanceOrNull;
        if (simulation != null)
        {
            // Caged birds had their scripts disabled on capture, which unregistered
            // them, so this is exactly the enemies still loose. The cages are dealt
            // with on their own terms below.
            simulation.CopyActiveEnemies(enemyBuffer);
            for (int i = 0; i < enemyBuffer.Count; i++)
            {
                if (enemyBuffer[i] != null)
                {
                    enemyBuffer[i].Despawn();
                }
            }

            enemyBuffer.Clear();
        }

        // Shots outlive the enemy or tower that fired them, so they are swept
        // separately. Scanned rather than tracked: this runs once, on a retry.
        EnemyBullet[] bullets = Object.FindObjectsByType<EnemyBullet>(FindObjectsSortMode.None);
        for (int i = 0; i < bullets.Length; i++)
        {
            CombatObjectPool.Release(bullets[i].gameObject);
        }

        Projectile[] projectiles = Object.FindObjectsByType<Projectile>(FindObjectsSortMode.None);
        for (int i = 0; i < projectiles.Length; i++)
        {
            CombatObjectPool.Release(projectiles[i].gameObject);
        }
    }

    private static void RestoreCages()
    {
        for (int i = 0; i < cages.Count; i++)
        {
            CagedState recorded = cages[i];
            if (recorded.Cage == null)
            {
                continue;
            }

            // A cage that was full and still is keeps the bird it has. A captive is
            // frozen with its colliders off, so nothing in the round could have touched
            // it, and swapping in a pooled replacement would only risk emptying the cage
            // if the pool had nothing to give.
            if (recorded.State == CageTower.CageState.Full
                && recorded.Cage.State == CageTower.CageState.Full)
            {
                continue;
            }

            GameObject captive = recorded.State == CageTower.CageState.Full
                ? AcquireCaptive(recorded.CapturedPrefab, recorded.Cage.transform.position)
                : null;

            recorded.Cage.RestoreState(recorded.State, captive);
        }
    }

    /// <summary>
    /// A bird for a cage that was full when the round began. It comes from the pool
    /// rather than being the same instance the round started with, so it arrives at full
    /// health - a cage the retry hands back is a whole one.
    /// </summary>
    private static GameObject AcquireCaptive(GameObject prefab, Vector3 position)
    {
        if (prefab == null
            || !CombatObjectPool.TryAcquire(
                prefab,
                position,
                Quaternion.identity,
                0f,
                out PooledObject pooledObject))
        {
            return null;
        }

        // Activated before it is caged: capture works off the enemy's live components,
        // and the cage disables them again immediately afterwards.
        CombatObjectPool.Activate(pooledObject);
        return pooledObject.gameObject;
    }

    private static void RestorePlayer()
    {
        if (player == null)
        {
            player = Object.FindFirstObjectByType<PlayerController>();
        }

        if (player != null)
        {
            player.Revive(playerHealth, playerPosition);
        }
    }

    private static void RestoreEnergy()
    {
        if (shop == null)
        {
            shop = Object.FindFirstObjectByType<TowerShopUI>();
        }

        if (shop != null && shop.Energy != energy)
        {
            shop.AddEnergy(energy - shop.Energy);
        }
    }

    /// <summary>The prefab a pooled object was made from, or null for anything else.</summary>
    private static GameObject GetSourcePrefab(GameObject instance)
    {
        return instance != null && instance.TryGetComponent(out PooledObject pooledObject)
            ? pooledObject.SourcePrefab
            : null;
    }
}
