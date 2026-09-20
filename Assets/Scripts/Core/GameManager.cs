using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [Header("Nain")]
    public int dwarfMaxHP = 3;

    private int dwarfHP;
    private int score;
    private int currentWave;

    public bool IsGameOver { get; private set; } = false;

    // Lecture seule pour les observations du DwarfAgent (7b)
    public int CurrentDwarfHP => dwarfHP;
    public int Score => score;
    public int CurrentWave => currentWave;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        dwarfHP = dwarfMaxHP;
    }

    public void RegisterKill(int points = 10)
    {
        if (IsGameOver) return;
        score += points;
        Debug.Log($"Score : {score}");
    }

    public void TakeDamage()
    {
        if (IsGameOver) return;

        dwarfHP--;
        Debug.Log($"HP du nain : {dwarfHP}");

        FlashPlayerRed();

        if (dwarfHP <= 0)
        {
            GameOver();
        }
    }

    // Nain tombé dans un ravin (DeathZone) — mort instantanée
    public void InstantKillPlayer()
    {
        if (IsGameOver) return;
        dwarfHP = 0;
        GameOver();
    }

    public void NextWave()
    {
        if (IsGameOver) return;
        currentWave++;
        Debug.Log($"--- Vague {currentWave} ---");
    }

    void GameOver()
    {
        IsGameOver = true;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            ExplosionEffect.Spawn(player.transform.position,
                                   new Color(0.9f, 0.1f, 0.1f), 22, 6f);
            // Désactivation au lieu de Destroy : le DwarfAgent doit survivre à sa mort
            // pour permettre les resets d'épisodes ML-Agents
            player.GetComponent<PlayerMovement>()?.SetEntityActive(false);
        }
        Debug.Log($"Game Over — Score final : {score}");
    }

    // Reset complet de la partie — appelé par DwarfAgent.OnEpisodeBegin()
    public void ResetGame()
    {
        IsGameOver = false;
        dwarfHP = dwarfMaxHP;
        score = 0;
        currentWave = 0;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            PlayerMovement pm = player.GetComponent<PlayerMovement>();
            pm?.ResetToStart();
            pm?.SetEntityActive(true);
        }

        // Redémarrage des vagues — voir note sur SpawnManager ci-dessous
        SpawnManager.Instance.StartTrainingEpisode();

        Debug.Log("--- Nouvel épisode ---");
    }

    private void FlashPlayerRed()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            StartCoroutine(FlashRoutine(player.GetComponent<SpriteRenderer>()));
        }
    }

    private System.Collections.IEnumerator FlashRoutine(SpriteRenderer renderer)
    {
        if (renderer == null) yield break;

        Color original = renderer.color;
        renderer.color = new Color(1f, 0.3f, 0.3f);

        float elapsed = 0f;
        while (elapsed < 0.15f)
        {
            if (renderer == null) yield break;
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (renderer != null)
        {
            renderer.color = original;
        }
    }
}