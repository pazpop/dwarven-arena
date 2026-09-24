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

    // Lecture seule pour les observations du DwarfAgent (7b)
    public float StunTimer => stunTimer;

    // Contrôle externe (agent ML) : quand actif, ExternalMoveDir remplace le clavier
    public bool ExternalControl { get; set; } = false;
    public Vector2 ExternalMoveDir { get; set; } = Vector2.zero;

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    private Rigidbody2D rb;
    private Animator animator;
    private float penaltyTimer = 0f;
    private float stunTimer = 0f;
    private Vector3 startPosition;

    private void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        startPosition = transform.position;
    }

    // Appelé par GameManager.ResetGame() : remet le nain à sa position de
    // spawn réelle (celle placée dans l'éditeur), pas un point arbitraire
    public void ResetToStart()
    {
        transform.position = startPosition;
        rb.linearVelocity = Vector2.zero;
    }

    private void Update()
    {
        // NOTE: le stun gèle aussi le décompte de la pénalité de swing raté (early return
        // ci-dessous). Couplage assumé — impact négligeable tant que stun <= 0.4s.
        // Si des sources de stun multiples s'ajoutent, revoir cette interaction.
        if (stunTimer > 0f)
        {
            stunTimer -= Time.deltaTime;
            return; // Le nain est étourdi : on laisse le knockback faire son œuvre
        }

        Vector2 moveDir;

        if (ExternalControl)
        {
            // Piloté par l'agent ML (DwarfAgent)
            moveDir = ExternalMoveDir;
        }
        else
        {
            // Piloté au clavier
            float moveX = Input.GetAxisRaw("Horizontal");
            float moveY = Input.GetAxisRaw("Vertical");
            moveDir = new Vector2(moveX, moveY).normalized;
        }

        // Mémoriser la dernière direction non-nulle (pour viser le marteau)
        if (moveDir != Vector2.zero)
        {
            LastMoveDirection = moveDir;
        }

        if (animator != null)
        {
            animator.SetBool(IsMovingHash, moveDir != Vector2.zero);
            // Le Blend Tree (8 directions, voir Assets/Editor/GenerateDwarvenArenaAnimations.cs)
            // choisit le bon sprite via ces deux floats — sprites PixelLab réellement
            // dessinés dans les 8 directions, plus besoin de flip gauche/droite.
            animator.SetFloat(MoveXHash, LastMoveDirection.x);
            animator.SetFloat(MoveYHash, LastMoveDirection.y);
        }

        // Choix de la vitesse selon l'état
        float currentSpeed = IsSlowPenalty ? slowSpeed : normalSpeed;
        if (IsShielding) currentSpeed *= shieldSpeedMult;

        rb.linearVelocity = moveDir * currentSpeed;

        // Gestion du timer de pénalité (swing raté)
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

    // Appelé par ShieldController et le DwarfAgent
    public void SetShielding(bool value)
    {
        IsShielding = value;
    }

    // Appelé par SpikeTrap : éjection + perte de contrôle temporaire
    public void ApplyStun(float duration, Vector2 ejection)
    {
        stunTimer = duration;
        rb.linearVelocity = ejection;
    }

    // Activé/désactivé par GameManager : mort = désactivation, reset d'épisode = réactivation
    // (le DwarfAgent reste fonctionnel même désactivé — indispensable pour l'entraînement)
    public void SetEntityActive(bool active)
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.enabled = active;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = active;

        if (!active) rb.linearVelocity = Vector2.zero;

        // Le collider/renderer ne suffisent pas à "geler" le nain : sans ça, le
        // cadavre garde son Update() actif (glisse sur l'input résiduel, peut
        // encore swinguer/repousser des ennemis vivants) jusqu'au reset d'épisode.
        HammerController hammerCtrl = GetComponent<HammerController>();
        if (hammerCtrl != null) hammerCtrl.enabled = active;

        ShieldController shieldCtrl = GetComponent<ShieldController>();
        if (shieldCtrl != null) shieldCtrl.enabled = active;

        enabled = active; // PlayerMovement lui-même, en dernier (sinon on ne s'exécute plus)
    }
}