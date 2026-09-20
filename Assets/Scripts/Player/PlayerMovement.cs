using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    public float speed = 5f;

    public Vector2 LastMoveDirection { get; private set; } = Vector2.right;

    private Rigidbody2D rb;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        Vector2 moveDir = new Vector2(moveX, moveY).normalized;
        rb.linearVelocity = moveDir * speed;

        // Mémoriser la dernière direction non-nulle (pour viser le marteau)
        if (moveDir != Vector2.zero)
        {
            LastMoveDirection = moveDir;
        }
    }
}