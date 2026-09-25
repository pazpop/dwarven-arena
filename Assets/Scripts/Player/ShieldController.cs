// Bouclier du Nain : blocage, micro-poussée des ennemis proches, et orientation
// automatique vers l'ennemi le plus proche pendant le blocage.
using UnityEngine;

public class ShieldController : MonoBehaviour
{
    [Header("Bouclier")]
    public float microPushForce = 8f;

    [Header("Ciblage du bouclier")]
    public float retargetInterval = 2f; // Verrouille une cible ce temps-là avant d'en reconsidérer une autre

    private static readonly int IsProtectingHash = Animator.StringToHash("IsProtecting");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private PlayerMovement player;
    private Animator animator;
    private bool wasShielding;
    private Transform lockedTarget;
    private float retargetTimer;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        animator = GetComponent<Animator>();
    }

    private void Update()
    {
        // Pilotage clavier seulement si l'agent ML ne contrôle pas le nain (l'agent
        // appelle RequestShield() directement depuis DwarfAgent.OnActionReceived)
        if (!player.ExternalControl)
        {
            bool holding = Input.GetMouseButton(0);
            RequestShield(holding);
        }

        // L'orientation du bouclier, elle, doit s'appliquer peu importe qui contrôle
        // le nain (clavier ou agent ML) — même surface de comportement pour les deux
        if (player.IsShielding)
        {
            UpdateShieldFacing();
        }
        else
        {
            lockedTarget = null; // Relâche la cible dès que le bouclier est baissé
        }
    }

    // Oriente le blend tree vers une cible verrouillée (pas le plus proche à chaque
    // frame : ça évite un jitter si deux ennemis sont à égale distance) — appelé
    // ici, dans Update(), pour écrire MoveX/MoveY avant que l'Animator ne les lise
    // (l'ordre par frame est Update -> Animation -> LateUpdate, donc LateUpdate est trop tard)
    private void UpdateShieldFacing()
    {
        if (animator == null) return;

        retargetTimer -= Time.deltaTime;
        if (lockedTarget == null || retargetTimer <= 0f)
        {
            lockedTarget = FindNearestEnemy();
            retargetTimer = retargetInterval;
        }

        if (lockedTarget == null) return;

        Vector2 dir = ((Vector2)lockedTarget.position - (Vector2)transform.position).normalized;
        animator.SetFloat(MoveXHash, dir.x);
        animator.SetFloat(MoveYHash, dir.y);
    }

    private Transform FindNearestEnemy()
    {
        Transform nearest = null;
        float minDist = float.MaxValue;

        foreach (var enemy in EnemyAI.Alive)
        {
            if (enemy == null) continue;

            float dist = (enemy.transform.position - transform.position).sqrMagnitude;
            if (dist < minDist)
            {
                minDist = dist;
                nearest = enemy.transform;
            }
        }

        return nearest;
    }

    // Appelé par le DwarfAgent (et le clavier via Update)
    public void RequestShield(bool active)
    {
        player.SetShielding(active);
        if (animator != null) animator.SetBool(IsProtectingHash, active);

        // Détection front montant pour la micro-poussée
        if (active && !wasShielding)
        {
            MicroPushNearbyEnemies();
        }
        wasShielding = active;
    }

    // Micro-poussée des ennemis proches au moment du blocage
    private void MicroPushNearbyEnemies()
    {
        Collider2D[] nearby = Physics2D.OverlapCircleAll(transform.position, 1.2f);
        foreach (var col in nearby)
        {
            EnemyAI enemy = col.GetComponent<EnemyAI>();
            if (enemy != null)
            {
                enemy.NotifyKnockback(EnemyAI.KnockbackSource.Shield);
                Vector2 dir = ((Vector2)enemy.transform.position - (Vector2)transform.position).normalized;
                enemy.GetComponent<Rigidbody2D>()?.AddForce(dir * microPushForce, ForceMode2D.Impulse);
            }
        }
    }

    // Visualisation du rayon de la micro-poussée dans l'éditeur
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, 1.2f);
    }
}