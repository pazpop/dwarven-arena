using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float speed = 3f;        // Gobelin : plus lent que le nain (5f)
    public float moveForce = 5f;

    private Transform target;       // Le nain
    private Rigidbody2D rb;

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        target = GameObject.FindGameObjectWithTag("Player").transform;
    }

    private void FixedUpdate()
    {
        if (target == null) return;

        Vector2 dir = ((Vector2)target.position - rb.position).normalized;
        rb.AddForce(dir * moveForce);

        // Limiter la vitesse max
        if (rb.linearVelocity.magnitude > speed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * speed;
        }
    }
}