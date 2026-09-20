using UnityEngine;

public class ShieldController : MonoBehaviour
{
    [Header("Stats")]
    public float knockbackForce = 4f;   // Micro-poussée des ennemis au contact

    private PlayerMovement player;
    private Rigidbody2D rb;
    private SpriteRenderer dwarfRenderer;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        rb = GetComponent<Rigidbody2D>();
        dwarfRenderer = GetComponent<SpriteRenderer>();
    }

    private void Update()
    {
        // Clic droit OU Maj pour lever le bouclier
        bool shielding = Input.GetKey(KeyCode.Mouse1) || Input.GetKey(KeyCode.LeftShift);
        player.SetShielding(shielding);

        // Feedback visuel temporaire : nain bleuté quand le bouclier est levé
        if (dwarfRenderer != null)
        {
            dwarfRenderer.color = shielding ? new Color(0.6f, 0.6f, 1f) : Color.white;
        }
    }

    private void OnCollisionStay2D(Collision2D collision)
    {
        // Micro-poussée des ennemis collés au nain quand le bouclier est levé
        if (!player.IsShielding) return;

        EnemyAI enemy = collision.gameObject.GetComponent<EnemyAI>();
        if (enemy != null)
        {
            Vector2 dir = ((Vector2)collision.transform.position - rb.position).normalized;
            enemy.GetComponent<Rigidbody2D>().AddForce(dir * knockbackForce, ForceMode2D.Force);
        }
    }
}