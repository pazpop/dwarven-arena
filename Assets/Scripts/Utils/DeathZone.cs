using UnityEngine;

public class DeathZone : MonoBehaviour
{
    private void OnTriggerEnter2D(Collider2D other)
    {
        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null)
        {
            enemy.Die();  // Réutilise le kill du GameManager (score +10)
            return;
        }

        if (other.CompareTag("Player"))
        {
            GameManager.Instance.InstantKillPlayer();
        }
    }
}