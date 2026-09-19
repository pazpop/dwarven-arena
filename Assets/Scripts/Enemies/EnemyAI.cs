using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    public float speed = 3f;
    public float moveForce = 5f;
    public int damage = 1; // Dégâts infligés au nain
    
    private Transform target;
    private Rigidbody2D rb;
    
    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        
        if (playerObj != null)
        {
            target = playerObj.transform;
        }
    }
    
    private void FixedUpdate()
    {
        if (target == null) return;
        
        Vector2 dir = ((Vector2)target.position - rb.position).normalized;
        rb.AddForce(dir * moveForce);
        
        // Limiter vitesse max
        if (rb.linearVelocity.magnitude > speed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * speed;
        }
    }
    
    private void OnCollisionEnter2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player"))
        {
            GameManager.Instance.TakeDamage();
            Destroy(gameObject); // Ennemi disparaît après collision
        }
    }
    
    public void Die()
    {
        GameManager.Instance.RegisterKill();
        Destroy(gameObject);
    }
}