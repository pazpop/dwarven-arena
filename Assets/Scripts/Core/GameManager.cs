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
    
    private void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }
    
    public void TakeDamage()
    {
        dwarfHP--;
        Debug.Log($"HP du nain : {dwarfHP}");
        
        if (dwarfHP <= 0)
        {
            GameOver();
        }
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
    
    void GameOver()
    {
        Debug.Log($"Game Over — Score final : {score}");
        // TODO: Restart / UI
    }
}