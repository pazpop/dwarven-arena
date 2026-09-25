// Marteau du Nain : swing avec cooldown, zone de frappe, dégâts/knockback aux
// ennemis touchés, et pénalité de vitesse en cas de swing raté.
using UnityEngine;

public class HammerController : MonoBehaviour
{
    [Header("Marteau")]
    public int damage = 1;
    public float hitRadius = 1.5f;
    public float knockbackForce = 15f;
    public float swingCooldown = 1.5f;
    public float missPenaltyDuration = 2f;
    public int chainBonusPerExtraKill = 5; // Bonus par kill au-delà du 1er dans le même swing

    public bool IsOnCooldown => Time.time - lastSwingTime < swingCooldown;

    // Fraction du cooldown restant pour les observations du DwarfAgent (7b)
    // 0 = marteau prêt, 1 = vient de frapper
    public float CooldownFraction =>
        Mathf.Clamp01(1f - (Time.time - lastSwingTime) / swingCooldown);

    private static readonly int AttackHash = Animator.StringToHash("Attack");

    private float lastSwingTime = -999f;
    private PlayerMovement player;
    private Animator animator;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        // Pilotage clavier seulement si l'agent ML ne contrôle pas le nain
        if (player.ExternalControl) return;

        if (Input.GetKeyDown(KeyCode.Space))
        {
            TrySwing();
        }
    }

    // Appelé par le DwarfAgent (et le clavier via Update)
    public void RequestSwing()
    {
        TrySwing();
    }

    private void TrySwing()
    {
        if (IsOnCooldown) return;

        lastSwingTime = Time.time;
        if (animator != null) animator.SetTrigger(AttackHash);

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
        int kills = 0;
        foreach (var hit in hits)
        {
            EnemyAI enemy = hit.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                enemy.NotifyKnockback(EnemyAI.KnockbackSource.Hammer);
                if (enemy.TakeHit(damage, swingDir.normalized * knockbackForce)) kills++;
                enemiesHit++;
            }
        }

        if (enemiesHit == 0)
        {
            player.ApplyMissPenalty(missPenaltyDuration);
        }
        else if (kills >= 2)
        {
            // Multiplicateur de chaîne : plusieurs kills en un seul swing rapportent
            // plus que les mêmes kills pris séparément (chaque EnemyAI.Die() a déjà
            // compté son propre scoreValue, ceci n'ajoute que le bonus de chaîne)
            GameManager.Instance.RegisterKill(chainBonusPerExtraKill * (kills - 1));
        }
    }

    // Visualisation de la vraie zone de frappe dans l'éditeur
    private void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying || player == null) return;

        Gizmos.color = Color.red;
        Vector2 swingDir = player.LastMoveDirection;
        Vector2 hitCenter = (Vector2)transform.position + swingDir * (hitRadius * 0.6f);
        Gizmos.DrawWireSphere(hitCenter, hitRadius);
    }
}