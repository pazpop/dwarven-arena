using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;
    
    private Rigidbody2D rb;
    
    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }
    
    private void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal"); // Flèches ou A/D
        float moveY = Input.GetAxisRaw("Vertical");   // Flèches ou W/S
        
        Vector2 moveDir = new Vector2(moveX, moveY).normalized;
        rb.linearVelocity = moveDir * speed;
    }
}