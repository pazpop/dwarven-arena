// Gère les vagues d'ennemis : instanciation Gobelin/Orc à un rythme croissant,
// sur des positions de spawn validées (sol réel, loin du Nain et des dangers).
using UnityEngine;
using UnityEngine.Tilemaps;

public class SpawnManager : MonoBehaviour
{
    public static SpawnManager Instance { get; private set; }

    public GameObject enemyPrefab;   // Goblin.prefab
    public GameObject orcPrefab;     // Orc.prefab (optionnel — vagues 100% Goblin si non assigné)
    public Transform[] spawnPoints;   // Positions de spawn (si vide : spawn aléatoire hors caméra, voir GetRandomSpawnPoint)

    [Header("Vagues")]
    public int enemiesPerWave = 5;
    public int enemiesPerWaveIncrease = 2; // Courbe progressive
    public float spawnDelay = 2f;
    [Range(0f, 1f)] public float orcChance = 0.2f; // Proportion d'Orcs, le reste = Gobelins

    [Header("Zone de spawn (fallback si spawnPoints est vide)")]
    public Tilemap groundTilemap;    // Sert à borner le spawn au sol réellement peint
    public float spawnMargin = 2.5f; // Marge par rapport au bord du sol (évite de spawn dans une DeathZone/SpikeTrap)
    public float minPlayerDistance = 4f; // Distance minimale au Nain pour éviter un spawn sur lui
    public float hazardSpawnDistance = 1.5f; // Distance minimale à une DeathZone/un SpikePit pour éviter un spawn dessus

    private int enemiesInWave = 0;
    private int baseEnemiesPerWave;   // Valeur de départ, sauvegardée pour les resets d'épisode
    private Bounds floorBounds;
    private bool hasFloorBounds;
    private Transform player;

    private void Awake()
    {
        Instance = this;
        // Sauvegarde AVANT toute mutation : la courbe de progression incrémente
        // enemiesPerWave à chaque vague, il faut pouvoir revenir au départ à chaque épisode
        baseEnemiesPerWave = enemiesPerWave;

        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) player = playerObj.transform;

        if (groundTilemap != null)
        {
            Bounds local = groundTilemap.localBounds;
            floorBounds = new Bounds(
                groundTilemap.transform.TransformPoint(local.center),
                Vector3.Scale(local.size, groundTilemap.transform.lossyScale));
            hasFloorBounds = true;
        }
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

        // Purge des gobelins survivants de l'épisode précédent — copie de la liste
        // car Destroy() déclenche EnemyAI.OnDisable(), qui la modifie en direct
        foreach (var enemy in EnemyAI.Alive.ToArray())
        {
            Destroy(enemy.gameObject);
        }

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

            while (EnemyAI.Alive.Count > 0 && !GameManager.Instance.IsGameOver)
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
        GameObject prefab = (orcPrefab != null && Random.value < orcChance) ? orcPrefab : enemyPrefab;
        Instantiate(prefab, pos, Quaternion.identity);
    }

    Vector3 GetRandomSpawnPoint()
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            return spawnPoints[Random.Range(0, spawnPoints.Length)].position;
        }

        if (!hasFloorBounds)
        {
            // Fallback si aucun Tilemap n'est assigné : position aléatoire autour de l'arène
            float radius = 4f;
            float angle = Random.Range(0f, 360f) * Mathf.Deg2Rad;
            return new Vector3(Mathf.Cos(angle) * radius, -2f, 0);
        }

        // Le sol n'est pas un rectangle plein (salle + corridor en L) : on tire dans le
        // rectangle englobant, mais on vérifie que la case (et une marge autour) est du
        // vrai sol peint, sinon on retombe dans un des "coins vides" hors du L.
        Vector3 candidate = new Vector3(
            (floorBounds.min.x + floorBounds.max.x) * 0.5f,
            (floorBounds.min.y + floorBounds.max.y) * 0.5f, 0);

        for (int attempt = 0; attempt < 30; attempt++)
        {
            float x = Random.Range(floorBounds.min.x + spawnMargin, floorBounds.max.x - spawnMargin);
            float y = Random.Range(floorBounds.min.y + spawnMargin, floorBounds.max.y - spawnMargin);
            candidate = new Vector3(x, y, 0);

            if (IsSafeFloorTile(candidate)) return candidate;
        }

        return candidate; // Repli après 30 essais : le dernier point testé
    }

    // Vérifie que la case est du sol réellement peint, assez loin du Nain, et assez
    // loin (distance réelle au collider, pas juste au rectangle englobant) d'une
    // DeathZone ou d'un SpikePit — sinon l'ennemi meurt dès son apparition
    private bool IsSafeFloorTile(Vector3 worldPos)
    {
        if (!HasTileAt(worldPos)) return false;
        if (player != null && Vector3.Distance(worldPos, player.position) < minPlayerDistance) return false;

        foreach (var hazard in HazardRegistry.Colliders)
        {
            if (hazard == null) continue;
            Vector2 closest = hazard.ClosestPoint(worldPos);
            if (Vector2.Distance(worldPos, closest) < hazardSpawnDistance) return false;
        }

        return true;
    }

    private bool HasTileAt(Vector3 worldPos)
    {
        Vector3Int cell = groundTilemap.WorldToCell(worldPos);
        return groundTilemap.HasTile(cell);
    }
}