using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    public GameObject enemyPrefab;  // Goblin.prefab
    public Transform[] spawnPoints; // Positions de spawn

    [Header("Vagues")]
    public int enemiesPerWave = 5;
    public int enemiesPerWaveIncrease = 2; // Courbe progressive
    public float spawnDelay = 2f;

    private int enemiesAlive = 0;
    private int enemiesInWave = 0;

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        enemiesInWave = enemiesPerWave;
        StartCoroutine(SpawnLoopRoutine());
    }

    // Enchaîne les vagues : spawn, attend que tous les ennemis soient morts, vague suivante
    System.Collections.IEnumerator SpawnLoopRoutine()
    {
        while (!GameManager.Instance.IsGameOver)
        {
            yield return StartCoroutine(SpawnWaveRoutine());

            while (enemiesAlive > 0 && !GameManager.Instance.IsGameOver)
            {
                yield return null;
            }

            if (GameManager.Instance.IsGameOver) yield break;

            GameManager.Instance.NextWave();
            enemiesPerWave += enemiesPerWaveIncrease;
            enemiesInWave = enemiesPerWave;
        }
    }

    System.Collections.IEnumerator SpawnWaveRoutine()
    {
        for (int i = 0; i < enemiesInWave; i++)
        {
            if (GameManager.Instance.IsGameOver) yield break;
            SpawnEnemy();
            yield return new WaitForSeconds(spawnDelay);
        }
    }

    void SpawnEnemy()
    {
        Vector3 pos = GetRandomSpawnPoint();
        Instantiate(enemyPrefab, pos, Quaternion.identity);
        enemiesAlive++;
    }

    // Appelé par EnemyAI.Die(), quelle que soit la cause de la mort
    public void OnEnemyDied()
    {
        enemiesAlive--;
    }

    Vector3 GetRandomSpawnPoint()
    {
        if (spawnPoints.Length == 0)
        {
            // Fallback : position aléatoire autour de l'arène
            float radius = 4f;
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, -2f, 0);
        }
        else
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
        }
    }
}
