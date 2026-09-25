// IA des ennemis (Gobelin/Orc) : poursuite du Nain, évitement des dangers,
// santé/knockback, chute dans un ravin et mort.
using System.Collections.Generic;
using UnityEngine;

public class EnemyAI : MonoBehaviour
{
    // Registre partagé des ennemis vivants — évite un FindObjectsByType<EnemyAI>()
    // à chaque appelant (DwarfAgent à chaque décision, ShieldController toutes les 2s)
    public static readonly List<EnemyAI> Alive = new List<EnemyAI>();

    // Ce projet a Domain Reload + Scene Reload désactivés (voir EditorSettings) :
    // sans ce reset explicite, Alive garderait les entrées de la session Play
    // précédente. OnEnable() se redéclenche bien à chaque entrée en Play (même
    // sans Domain Reload), donc la liste se repeuple correctement juste après.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay() => Alive.Clear();

    public float speed = 3f;
    public float moveForce = 5f;
    public int damage = 1;

    [Header("Santé / knockback")]
    public int health = 2;               // Gobelin : 2 coups de marteau
    public int scoreValue = 10;          // Points au kill — Orc à 20 dans son prefab
    public float knockbackDamping = 3f;  // Rapidité de récupération après poussée

    [Header("Évitement des dangers")]
    public float hazardAvoidDistance = 1.2f; // Distance à partir de laquelle l'ennemi s'écarte
    public float hazardAvoidStrength = 3f;   // Poids de l'évitement face à la poursuite

    private static readonly int IsMovingHash = Animator.StringToHash("IsMoving");
    private static readonly int MoveXHash = Animator.StringToHash("MoveX");
    private static readonly int MoveYHash = Animator.StringToHash("MoveY");

    // D'où vient la dernière poussée reçue — sert à donner plus de points quand le
    // joueur pousse un ennemi sur un piège plutôt que de le tuer directement, et
    // encore plus si c'est fait au bouclier (portée/force bien plus faibles que le
    // marteau, donc plus risqué/technique à réussir). Expire après un délai pour
    // ne pas attribuer une mort à une poussée trop ancienne et sans rapport.
    // Double usage de cette même fenêtre : elle sert aussi à exempter temporairement
    // l'ennemi du plafond de vitesse en FixedUpdate (voir ActiveKnockbackSource plus
    // bas), sinon le knockback est écrasé au tick physique suivant. Les deux usages
    // tolèrent bien la même durée (1.5s) ; à séparer en deux constantes si jamais
    // l'un des deux doit être réglé indépendamment de l'autre.
    public enum KnockbackSource { None, Hammer, Shield }
    private const float KnockbackAttributionWindow = 1.5f;
    private KnockbackSource lastKnockbackSource = KnockbackSource.None;
    private float lastKnockbackTime = -999f;

    private Transform target;
    private Rigidbody2D rb;
    private Animator animator;
    private Vector2 lastFacing = Vector2.down;
    private bool isFalling;

    private void OnEnable() => Alive.Add(this);
    private void OnDisable() => Alive.Remove(this);

    private void Start()
    {
        rb = GetComponent<Rigidbody2D>();
        animator = GetComponent<Animator>();
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj != null) target = playerObj.transform;
    }

    private void FixedUpdate()
    {
        if (target == null || isFalling) return;

        // Ralentir le knockback progressivement (l'ennemi reprend sa poursuite)
        rb.linearVelocity *= 1f - knockbackDamping * Time.fixedDeltaTime;

        // Poursuite, déviée par l'évitement des dangers proches
        Vector2 dir = ((Vector2)target.position - rb.position).normalized;
        Vector2 avoidance = ComputeHazardAvoidance();
        Vector2 desired = dir + avoidance * hazardAvoidStrength;
        if (desired != Vector2.zero) desired.Normalize();
        rb.AddForce(desired * moveForce);

        // Limiter la vitesse max — sauf juste après une poussée (marteau/bouclier) :
        // sinon le knockback est écrasé au tick physique suivant, quelle que soit sa
        // force, et ne laisse aucun élan pour porter l'ennemi jusqu'à un ravin/pic
        if (ActiveKnockbackSource == KnockbackSource.None && rb.linearVelocity.magnitude > speed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * speed;
        }

        if (animator != null)
        {
            if (dir != Vector2.zero) lastFacing = dir;
            animator.SetBool(IsMovingHash, rb.linearVelocity.sqrMagnitude > 0.01f);
            animator.SetFloat(MoveXHash, lastFacing.x);
            animator.SetFloat(MoveYHash, lastFacing.y);
        }
    }

    // Repousse loin des ravins/pics dès qu'on s'en approche — simple somme de vecteurs
    // "s'éloigner du point le plus proche du danger", pondérée par la proximité
    private Vector2 ComputeHazardAvoidance()
    {
        Vector2 avoidance = Vector2.zero;

        foreach (var hazard in HazardRegistry.Colliders)
        {
            if (hazard == null) continue;

            Vector2 closest = hazard.ClosestPoint(rb.position);
            Vector2 away = rb.position - closest;
            float dist = away.magnitude;

            if (dist < hazardAvoidDistance)
            {
                float strength = (hazardAvoidDistance - dist) / hazardAvoidDistance;
                avoidance += away.normalized * strength;
            }
        }

        return avoidance;
    }

    // Appelé par HammerController/ShieldController quand ils poussent cet ennemi —
    // détermine le multiplicateur de score si ça mène à un kill sur un piège ou un
    // ravin, ET exempte temporairement l'ennemi du plafond de vitesse (FixedUpdate)
    // pour que la poussée porte réellement quelque part
    public void NotifyKnockback(KnockbackSource source)
    {
        lastKnockbackSource = source;
        lastKnockbackTime = Time.time;
    }

    private KnockbackSource ActiveKnockbackSource =>
        Time.time - lastKnockbackTime <= KnockbackAttributionWindow ? lastKnockbackSource : KnockbackSource.None;

    // Appelé par DeathZone : chute dans le ravin avec un petit effet avant l'explosion,
    // plutôt qu'une mort instantanée — l'élan du knockback continue de le porter par-dessus le bord
    public void FallIntoRavine()
    {
        if (isFalling) return;
        isFalling = true;

        Collider2D col = GetComponent<Collider2D>();
        if (col != null) col.enabled = false;

        StartCoroutine(FallRoutine());
    }

    private System.Collections.IEnumerator FallRoutine()
    {
        const float duration = 0.4f;
        const float rotationSpeed = 540f; // degrés/seconde
        float elapsed = 0f;
        Vector3 startScale = transform.localScale;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            transform.localScale = Vector3.Lerp(startScale, Vector3.zero, t);
            transform.Rotate(0f, 0f, rotationSpeed * Time.deltaTime);
            yield return null;
        }

        // Ravin : poussé au marteau = ×1.5, poussé au bouclier (portée/force bien
        // plus faibles, donc plus risqué à réussir) = ×2.5, tombé sans être poussé
        // récemment (évitement en échec) = valeur normale
        float multiplier = ActiveKnockbackSource switch
        {
            KnockbackSource.Hammer => 1.5f,
            KnockbackSource.Shield => 2.5f,
            _ => 1f
        };
        Die(multiplier);
    }

    // Appelé par le marteau — renvoie true si ce coup a tué l'ennemi (utilisé par
    // HammerController pour le multiplicateur de chaîne sur les kills multiples)
    public bool TakeHit(int dmg, Vector2 knockback)
    {
        health -= dmg;
        rb.AddForce(knockback, ForceMode2D.Impulse);

        if (health <= 0)
        {
            Die();
            return true;
        }
        return false;
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

    // Gros morceaux + une fine pluie de petites particules par-dessus, pour plus de richesse visuelle
    private static void SpawnExplosion(Vector3 position, Color color)
    {
        ExplosionEffect.Spawn(position, color, 26, 3.5f);
        ExplosionEffect.Spawn(position, color, 14, 6f, 0.02f, 0.06f);
    }

    private void ExplodeWithoutScore()
    {
        SpawnExplosion(transform.position, new Color(0.1f, 0.5f, 0.1f)); // vert sombre
        Destroy(gameObject);
    }

    public void Die(float scoreMultiplier = 1f)
    {
        SpawnExplosion(transform.position, new Color(0.2f, 0.9f, 0.2f)); // vert
        GameManager.Instance.RegisterKill(Mathf.RoundToInt(scoreValue * scoreMultiplier));
        GameManager.Instance.RegisterEnemyKilled();
        Destroy(gameObject);
    }

    // Appelé par SpikeTrap (empalement) : pic au marteau = ×2, pic au bouclier = ×3
    // (zone du pic plus petite que le ravin, donc plus dur à viser dans les deux cas)
    public void DieFromSpike()
    {
        float multiplier = ActiveKnockbackSource switch
        {
            KnockbackSource.Hammer => 2f,
            KnockbackSource.Shield => 3f,
            _ => 1f
        };
        Die(multiplier);
    }
}