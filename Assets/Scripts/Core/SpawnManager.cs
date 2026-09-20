using UnityEngine;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    public GameObject enemyPrefab;   // Goblin.prefab
    public Transform[] spawnPoints;   // Positions de spawn

    [Header("Vagues")]
    public int enemiesPerWave = 5;
    public int enemiesPerWaveIncrease = 2; // Courbe progressive
    public float spawnDelay = 2f;

    private int enemiesAlive = 0;
    private int enemiesInWave = 0;
    private int baseEnemiesPerWave;   // Valeur de départ, sauvegardée pour les resets d'épisode

    private void Awake()
    {
        Instance = this;
        // Sauvegarde AVANT toute mutation : la courbe de progression incrémente
        // enemiesPerWave à chaque vague, il faut pouvoir revenir au départ à chaque épisode
        baseEnemiesPerWave = enemiesPerWave;
    }

    private void Start()
    {
        enemiesInWave = enemiesPerWave;
        StartCoroutine(SpawnLoopRoutine());
    }

    // ==================== ENTRAÎNEMENT ML-AGENTS ====================

    // Appelé par GameManager.ResetGame() à chaque début d'épisode :
    // purge les ennemis survivants, remet les compteurs à zéro, relance les vagues
    public void StartTrainingEpisode()
    {
        StopAllCoroutines();

        // Purge des gobelins survivants de l'épisode précédent
        foreach (var enemy in FindObjectsByType<EnemyAI>())
        {
            Destroy(enemy.gameObject);
        }
        enemiesAlive = 0;

        // Reset de la courbe de progression (sinon : vagues inflationnistes épisode après épisode)
        enemiesPerWave = baseEnemiesPerWave;
        enemiesInWave = enemiesPerWave;

        StartCoroutine(SpawnLoopRoutine());
    }

    // ==================== BOUCLE DE VAGUES (inchangée) ====================

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