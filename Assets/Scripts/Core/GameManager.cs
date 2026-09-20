using UnityEngine;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    
    [Header("Vies")]
    public int dwarfHP = 3;
    
    [Header("Score")]
    public int score = 0;
    
    [Header("Vagues")]
    public int currentWave = 1;
    public int enemiesPerWave = 5;
    public float spawnDelay = 2f;

    public bool IsGameOver { get; private set; } = false;

    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    
    public void RegisterKill()
    {
        score += 10;
        Debug.Log($"Score : {score}");
    }
    
    public void NextWave()
    {
        currentWave++;
        enemiesPerWave += 2; // Courbe progressive
        Debug.Log($"Vague {currentWave} — Ennemis : {enemiesPerWave}");
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

    // Mort instantanée (chute dans un ravin), indépendante des HP restants
    public void InstantKillPlayer()
    {
        if (IsGameOver) return;

        dwarfHP = 0;
        GameOver();
    }

    void GameOver()
    {
        IsGameOver = true;

        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
        {
            ExplosionEffect.Spawn(player.transform.position,
                                   new Color(0.9f, 0.1f, 0.1f), 22, 6f); // rouge
            Destroy(player);
        }
        Debug.Log($"Game Over — Score final : {score}");
        // TODO: restart / UI
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
            // Si le nain meurt pendant le flash, on arrête proprement
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