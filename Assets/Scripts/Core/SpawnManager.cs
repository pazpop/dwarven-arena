using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public GameObject enemyPrefab;  // Goblin.prefab
    public Transform[] spawnPoints; // Positions de spawn
    
    private int enemiesAlive = 0;
    private int enemiesInWave = 0;
    private bool spawning = false;
    
    private void Start()
    {
        enemiesInWave = GameManager.Instance.enemiesPerWave;
        StartCoroutine(SpawnWaveRoutine());
    }
    
    System.Collections.IEnumerator SpawnWaveRoutine()
    {
        for (int i = 0; i < enemiesInWave; i++)
        {
            SpawnEnemy();
            yield return new WaitForSeconds(GameManager.Instance.spawnDelay);
        }
    }
    
    void SpawnEnemy()
    {
        Vector3 pos = GetRandomSpawnPoint();
        Instantiate(enemyPrefab, pos, Quaternion.identity);
        enemiesAlive++;
    }
    
    Vector3 GetRandomSpawnPoint()
    {
        if (spawnPoints.Length == 0)
        {
            // Fallback : position aléatoire autour de l'arène
            float radius = 6f;
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, -2f, 0);
        }
        else
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
        }
    }
}