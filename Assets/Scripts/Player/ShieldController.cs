using UnityEngine;

public class ShieldController : MonoBehaviour
{
    [Header("Bouclier")]
    public float microPushForce = 8f;

    private PlayerMovement player;
    private SpriteRenderer spriteRenderer;
    private Color originalColor;
    private bool wasShielding;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null)
        {
            originalColor = spriteRenderer.color;
        }
    }

    private void Update()
    {
        // Pilotage clavier seulement si l'agent ML ne contrôle pas le nain
        if (player.ExternalControl) return;

        bool holding = Input.GetMouseButton(0);
        RequestShield(holding);
    }

    // Appelé par le DwarfAgent (et le clavier via Update)
    public void RequestShield(bool active)
    {
        player.SetShielding(active);

        // Teinte visuelle : bleu quand le bouclier est levé
        if (spriteRenderer != null)
        {
            spriteRenderer.color = active
                ? new Color(0.4f, 0.55f, 1f)   // bleu
                : originalColor;
        }

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