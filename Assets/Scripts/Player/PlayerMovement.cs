using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [Header("Vitesses")]
    public float normalSpeed = 5f;        // Plus rapide que les gobelins
    public float slowSpeed = 3f;          // = gobelins quand pénalisé
    public float shieldSpeedMult = 0.7f;  // Bouclier levé = un peu plus lent

    public Vector2 LastMoveDirection { get; private set; } = Vector2.right;
    public bool IsShielding { get; private set; } = false;
    public bool IsSlowPenalty { get; private set; } = false;

    private Rigidbody2D rb;
    private float penaltyTimer = 0f;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
    }

    private void Update()
    {
        float moveX = Input.GetAxisRaw("Horizontal");
        float moveY = Input.GetAxisRaw("Vertical");

        Vector2 moveDir = new Vector2(moveX, moveY).normalized;

        // Mémoriser la dernière direction non-nulle (pour viser le marteau)
        if (moveDir != Vector2.zero)
        {
            LastMoveDirection = moveDir;
        }

        // Choix de la vitesse selon l'état
        float currentSpeed = IsSlowPenalty ? slowSpeed : normalSpeed;
        if (IsShielding) currentSpeed *= shieldSpeedMult;

        rb.linearVelocity = moveDir * currentSpeed;

        // Gestion du timer de pénalité
        if (IsSlowPenalty)
        {
            penaltyTimer -= Time.deltaTime;
            if (penaltyTimer <= 0f)
            {
                IsSlowPenalty = false;
            }
        }
    }

    // Appelé par HammerController quand un swing est raté
    public void ApplyMissPenalty(float duration)
    {
        IsSlowPenalty = true;
        penaltyTimer = duration;
    }

    // Appelé par ShieldController à chaque frame
    public void SetShielding(bool value)
    {
        IsShielding = value;
    }
}