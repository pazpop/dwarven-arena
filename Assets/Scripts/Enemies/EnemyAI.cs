using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float speed = 3f;
    public float moveForce = 5f;
    public int damage = 1;

    [Header("Santé / knockback")]
    public int health = 2;               // Gobelin : 2 coups de marteau
    public float knockbackDamping = 3f;  // Rapidité de récupération après poussée

    private Transform target;
    private Rigidbody2D rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) target = playerObj.transform;
    }

    private void FixedUpdate()
    {
        if (target == null) return;

        // Ralentir le knockback progressivement (l'ennemi reprend sa poursuite)
        rb.linearVelocity *= 1f - knockbackDamping * Time.fixedDeltaTime;

        // Poursuite
        Vector2 dir = ((Vector2)target.position - rb.position).normalized;
        rb.AddForce(dir * moveForce);

        // Limiter la vitesse max
        if (rb.linearVelocity.magnitude > speed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * speed;
        }
    }

    // Appelé par le marteau
    public void TakeHit(int dmg, Vector2 knockback)
    {
        health -= dmg;
        rb.AddForce(knockback, ForceMode2D.Impulse);

        if (health <= 0)
        {
            Die();
        }
    }

    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            PlayerMovement pm = collision.gameObject.GetComponent<PlayerMovement>();
            if (pm != null && pm.IsShielding) return;

            // Suicide : dégâts au nain, mort SANS score (anti-reward-hacking)
            ExplodeWithoutScore();
            GameManager.Instance.TakeDamage();
        }
    }

    private void ExplodeWithoutScore()
    {
        ExplosionEffect.Spawn(transform.position, new Color(0.1f, 0.5f, 0.1f)); // vert sombre
        if (SpawnManager.Instance != null) SpawnManager.Instance.OnEnemyDied();
        Destroy(gameObject);
    }

    public void Die()
    {
        ExplosionEffect.Spawn(transform.position, new Color(0.2f, 0.9f, 0.2f)); // vert
        GameManager.Instance.RegisterKill();
        if (SpawnManager.Instance != null) SpawnManager.Instance.OnEnemyDied();
        Destroy(gameObject);
    }
}