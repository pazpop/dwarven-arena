using UnityEngine;

public class HammerController : MonoBehaviour
{
    [Header("Attaque")]
    public float swingCooldown = 1.5f;     // Cooldown élevé : chaque coup compte
    public float hitRadius = 1.2f;         // Portée du swing devant le nain
    public float knockbackForce = 15f;
    public int damage = 1;

    [Header("Pénalité")]
    public float missPenaltyDuration = 2f; // Durée de lenteur après un swing raté

    private float lastSwingTime = -10f;
    private PlayerMovement player;

    public bool IsOnCooldown => Time.time - lastSwingTime < swingCooldown;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
    }

    private void Update()
    {
        // Clic gauche OU Espace pour frapper
        if (Input.GetKeyDown(KeyCode.Mouse0) || Input.GetKeyDown(KeyCode.Space))
        {
            TrySwing();
        }
    }

    private void TrySwing()
    {
        if (IsOnCooldown) return;

        lastSwingTime = Time.time;

        // --- AUTO-VISÉE : chercher l'ennemi le plus proche dans le rayon d'attaque ---
        Vector2 swingDir = player.LastMoveDirection;  // fallback : direction de déplacement
        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, hitRadius * 1.4f);

        EnemyAI nearestEnemy = null;
        float nearestDist = float.MaxValue;

        foreach (var col in nearby)
        {
            EnemyAI enemy = col.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                float dist = Vector2.Distance(transform.position, enemy.transform.position);
                if (dist < nearestDist)
                {
                    nearestDist = dist;
                    nearestEnemy = enemy;
                }
            }
        }

        // S'il y a un ennemi proche : viser automatiquement vers lui
        if (nearestEnemy != null)
        {
            swingDir = ((Vector2)nearestEnemy.transform.position - (Vector2)transform.position).normalized;
        }

        // Zone de frappe DEVANT le nain (centre décalé)
        Vector2 hitCenter = (Vector2)transform.position + swingDir * (hitRadius * 0.6f);

        Collider2D[] hits = Physics2D.OverlapCircleAll(hitCenter, hitRadius);

        int enemiesHit = 0;
        foreach (var hit in hits)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                enemy.TakeHit(damage, swingDir.normalized * knockbackForce);
                enemiesHit++;
            }
        }

        if (enemiesHit == 0)
        {
            player.ApplyMissPenalty(missPenaltyDuration);
            Debug.Log("Swing raté ! Pénalité de vitesse appliquée.");
        }
        else
        {
            Debug.Log($"Swing réussi : {enemiesHit} ennemi(s) touché(s)");
        }
    }

    // Visualisation de la zone de frappe dans l'éditeur
    private void OnDrawGizmosSelected()
    {
        if (player == null) return;
        Gizmos.color = Color.red;
        Vector2 dir = Application.isPlaying ? player.LastMoveDirection : Vector2.right;
        Vector2 center = (Vector2)transform.position + dir * (hitRadius * 0.6f);
        Gizmos.DrawWireSphere(center, hitRadius);
    }
}