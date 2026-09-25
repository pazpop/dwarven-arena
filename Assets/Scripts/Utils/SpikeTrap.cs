// Piège à pics : empale les ennemis instantanément, inflige des dégâts répétés
// au Nain et l'éjecte s'il campe dedans.
using UnityEngine;

public class SpikeTrap : MonoBehaviour
{
    [Header("Dégâts au nain")]
    public float damageInterval = 0.5f;   // Dégâts répétés si le nain reste/campe dans les pics

    private float lastDamageTime = -10f;
    private Collider2D col;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        HazardRegistry.Register(col);
    }

    private void OnDestroy()
    {
        HazardRegistry.Unregister(col);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Ennemis : empalés instantanément — mort dès le contact du bord
        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null)
        {
            enemy.DieFromSpike();
            return;
        }

        // Nain : repoussé dès le contact du bord + 1 dégât
        if (other.CompareTag("Player") && Time.time - lastDamageTime >= damageInterval)
        {
            lastDamageTime = Time.time;
            GameManager.Instance.TakeDamage();

            // Éjection avec mini-stun : le joueur ne peut pas écraser le knockback
            PlayerMovement pm = other.GetComponent<PlayerMovement>();
            Rigidbody2D rb = other.GetComponent<Rigidbody2D>();
            if (pm != null && rb != null)
            {
                Vector2 away = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
                pm.ApplyStun(0.3f, away * 3.5f);
            }
        }
    }
}