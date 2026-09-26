// État central de la partie : HP, score, vagues, game over/reset. Point d'entrée
// unique consulté par (presque) tous les autres scripts.
using Unity.MLAgents;
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

    // Vrai pendant un entraînement mlagents-learn — sert à couper tout ce qui ne
    // doit jamais s'exécuter côté entraînement (menu, logs de debug...)
    public static bool IsTraining => Academy.Instance.IsCommunicatorOn;

    // Lecture seule pour les observations/rewards du DwarfAgent
    public int CurrentDwarfHP => dwarfHP;
    public int Score => score;

    // Nombre de kills, indépendant des points (RegisterKill varie selon le type
    // d'ennemi/le bonus de chaîne) — c'est CE compteur que DwarfAgent utilise pour
    // le reward, afin qu'un kill vaille toujours +1, peu importe combien de points
    // il rapporte au score affiché au joueur
    public int Kills { get; private set; }

    // Détail des kills pour le reward de l'agent, selon la façon de tuer (voir
    // RegisterEnemyKilled) : kills directs (marteau, ou chute sans poussée) et somme
    // des multiplicateurs des kills obtenus en poussant un ennemi dans un danger
    public int DirectKills { get; private set; }
    public float PushedKillMultiplierSum { get; private set; }

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
    }

    // Appelé une seule fois par ennemi effectivement tué (voir EnemyAI.Die()) —
    // distinct de RegisterKill() : le bonus de chaîne appelle RegisterKill() sans
    // qu'un ennemi supplémentaire soit mort, donc il ne doit pas incrémenter Kills
    // `scoreMultiplier` : celui du score (×1 direct, ×1.5 à ×3 poussé dans un danger)
    public void RegisterEnemyKilled(float scoreMultiplier = 1f)
    {
        if (IsGameOver) return;
        Kills++;
        if (scoreMultiplier > 1f) PushedKillMultiplierSum += scoreMultiplier;
        else DirectKills++;
    }

    public void TakeDamage()
    {
        if (IsGameOver) return;

        dwarfHP--;
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
        if (!IsTraining) Debug.Log($"--- Vague {currentWave} ---");
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

        MainMenuController.Instance?.OpenMenu();

        if (!IsTraining) Debug.Log($"Game Over — Score final : {score}");
    }

    // Reset complet de la partie — appelé par DwarfAgent.OnEpisodeBegin()
    public void ResetGame()
    {
        IsGameOver = false;
        dwarfHP = dwarfMaxHP;
        score = 0;
        Kills = 0;
        DirectKills = 0;
        PushedKillMultiplierSum = 0f;
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

        if (!IsTraining) Debug.Log("--- Nouvel épisode ---");
    }

    private Coroutine flashCoroutine;

    private void FlashPlayerRed()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            // Arrête un flash déjà en cours avant d'en relancer un — sinon deux flashs
            // rapprochés (fréquent en entraînement accéléré) se chevauchent, et le
            // second capturait la couleur "actuelle" (encore rouge) comme référence à
            // restaurer, laissant le nain rouge en permanence après quelques morts
            if (flashCoroutine != null) StopCoroutine(flashCoroutine);
            flashCoroutine = StartCoroutine(FlashRoutine(player.GetComponent<SpriteRenderer>()));
        }
    }

    private System.Collections.IEnumerator FlashRoutine(SpriteRenderer renderer)
    {
        if (renderer == null) yield break;

        // Couleur de base fixe (pas "renderer.color" au moment de l'appel) : rien
        // d'autre ne teinte le sprite du nain, donc blanc est toujours la bonne cible
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
            renderer.color = Color.white;
        }

        flashCoroutine = null;
    }
}