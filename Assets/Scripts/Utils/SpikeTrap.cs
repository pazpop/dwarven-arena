// Piège à pics : empale les ennemis instantanément, inflige des dégâts répétés
// au Nain et l'éjecte s'il campe dedans. Bouclier levé, le Nain ne prend aucun
// dégât et bute sur les pics comme sur un mur invisible.
using UnityEngine;

public class SpikeTrap : MonoBehaviour
{
    [Header("Dégâts au nain")]
    public float damageInterval = 0.5f;   // Dégâts répétés si le nain reste/campe dans les pics

    private float lastDamageTime = -10f;
    private Collider2D col;

    // Même forme que le trigger, mais solide : les ennemis l'ignorent toujours
    // (EnemyAI.Start), le Nain seulement bouclier baissé (FixedUpdate).
    private BoxCollider2D wall;
    private PlayerMovement player;
    private Collider2D playerCol;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        HazardRegistry.Register(col);

        var trigger = (BoxCollider2D)col;
        wall = gameObject.AddComponent<BoxCollider2D>();
        wall.size = trigger.size;
        wall.offset = trigger.offset;
        HazardRegistry.SpikeWalls.Add(wall);
    }

    private void Start()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        if (playerObj == null) return;
        player = playerObj.GetComponent<PlayerMovement>();
        playerCol = playerObj.GetComponent<Collider2D>();
    }

    // Réappliqué à chaque pas physique : l'état « ignoré » est perdu quand le
    // collider du Nain est désactivé (mort) puis réactivé (nouvelle partie).
    private void FixedUpdate()
    {
        if (player != null && playerCol.enabled)
            Physics2D.IgnoreCollision(wall, playerCol, !player.IsShielding);
    }

    private void OnDestroy()
    {
        HazardRegistry.Unregister(col);
        HazardRegistry.SpikeWalls.Remove(wall);
    }

    private void OnTriggerStay2D(Collider2D other)
    {
        // Ennemis : empalés instantanément — mort dès le contact du bord
        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null)
        {
            enemy.DieFromSpike();
            return;
        }

        // Nain : repoussé dès le contact du bord + 1 dégât, sauf bouclier levé
        if (!other.CompareTag("Player")) return;
        PlayerMovement pm = other.GetComponent<PlayerMovement>();
        if (pm == null || pm.IsShielding || Time.time - lastDamageTime < damageInterval) return;

        lastDamageTime = Time.time;
        GameManager.Instance.TakeDamage();

        // Éjection avec mini-stun : le joueur ne peut pas écraser le knockback
        Vector2 away = ((Vector2)other.transform.position - (Vector2)transform.position).normalized;
        pm.ApplyStun(0.3f, away * 3.5f);
    }
}