using UnityEngine;

public class WaveSpawner : MonoBehaviour
{
    [SerializeField] private Transform[] spawnPoints;
    [SerializeField] private GameObject[] enemyPrefabs;

    [SerializeField] private float timeBetweenSpawns = 5f;
    [SerializeField] private float timeBetweenSpawnsDecrease = 0.1f;
    [SerializeField] private float minTimeBetweenSpawns = 1f;

    private float spawnTimer = 0f;


    // Update is called once per frame
    void Update()
    {
        spawnTimer += Time.deltaTime;
        if (spawnTimer >= timeBetweenSpawns)
        {
            SpawnEnemy();
            spawnTimer = 0f;
            timeBetweenSpawns = Mathf.Max(minTimeBetweenSpawns, timeBetweenSpawns - timeBetweenSpawnsDecrease);
        }
    }

    public void SpawnEnemy()
    {
        int spawnIndex = Random.Range(0, spawnPoints.Length);
        int enemyIndex = Random.Range(0, enemyPrefabs.Length);
        Transform spawnPoint = spawnPoints[spawnIndex];
        GameObject enemyPrefab = enemyPrefabs[enemyIndex];
        Instantiate(enemyPrefab, spawnPoint.position, spawnPoint.rotation);
    }
}
