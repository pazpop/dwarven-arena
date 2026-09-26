// Agent ML-Agents (PPO) qui contrôle le Nain à la place du clavier : observations,
// actions et rewards de l'entraînement — voir POC_IA_ML-AGENTS.md pour le détail.
using System.Collections.Generic;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;

public class DwarfAgent : Agent
{
    [Header("Références")]
    public PlayerMovement player;
    public HammerController hammer;
    public ShieldController shield;
    public SpikeTrap[] spikeTraps;

    [Header("Observations")]
    public int observedEnemies = 3;
    // Nombre de pics observés (2 valeurs chacun, dans l'ordre du tableau spikeTraps) ;
    // slots au-delà de spikeTraps.Length = zéros.
    // Taille d'observation totale = 11 + 2 × observedSpikes + 6 × observedEnemies, à
    // reporter dans Behavior Parameters (Space Size) : 39 avec 5 pics et 3 ennemis
    // (dwarf_v03). Avant : 25 (dwarf_v01/v02, 2 pics non assignés, sans ravins).
    public int observedSpikes = 2;

    [Header("Récompenses")]
    // Kill en poussant un ennemi dans un danger : killReward × multiplicateur du score
    // (×1.5 ravin au marteau … ×3 pic au bouclier). Kill direct (marteau) : bien moins,
    // pour que le décor soit le chemin le plus rentable — c'est le cœur du gameplay.
    public float killReward = 1f;
    public float directKillReward = 0.3f;
    public float damagePenalty = -0.3f;
    public float deathPenalty = -1f;
    public float stepCost = -0.0005f;

    // Suivi des deltas pour les rewards par polling
    private int lastKnownDirectKills;
    private float lastKnownPushedSum;
    private int lastKnownHP;

    private bool pendingSwing = false;
    private bool pendingShieldState = false;

    // Buffers réutilisés par CollectObservations pour la sélection des ennemis les
    // plus proches — évite un FindObjectsByType + allocation de liste + tri à
    // chaque décision (des millions de fois pendant l'entraînement)
    private EnemyAI[] nearestEnemies;
    private float[] nearestDistSq;

    public override void Initialize()
    {
        if (player == null) player = GetComponent<PlayerMovement>();
        if (hammer == null) hammer = GetComponent<HammerController>();
        if (shield == null) shield = GetComponent<ShieldController>();

        // player est utilisé juste en dessous donc une référence manquante
        // plante ici ; hammer/shield, eux, ne sont touchés que plus tard
        // (CollectObservations/OnActionReceived) — sans ce check, une
        // référence manquante donne une NullReferenceException confuse,
        // loin de sa vraie cause.
        if (player == null || hammer == null || shield == null)
        {
            Debug.LogError($"DwarfAgent sur '{name}' : référence manquante — " +
                $"player={player != null}, hammer={hammer != null}, shield={shield != null}. " +
                "Assigne-les dans l'Inspector, ou vérifie que HammerController/ShieldController " +
                "sont bien sur le même GameObject que DwarfAgent.");
            enabled = false;
            return;
        }

        player.ExternalControl = false;

        // Durée max d'un épisode : 3000 steps, soit ~600 décisions (1 décision tous les
        // 5 steps, Decision Requester). Au-delà, l'épisode se termine (sans pénalité de
        // mort) — évite les parties infinies.
        MaxStep = 3000;

        nearestEnemies = new EnemyAI[observedEnemies];
        nearestDistSq = new float[observedEnemies];
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space)) pendingSwing = true;
        pendingShieldState = Input.GetMouseButton(0);
    }

    // ==================== CYCLE D'ÉPISODE ====================

    public override void OnEpisodeBegin()
    {
        GameManager.Instance.ResetGame();
        lastKnownDirectKills = 0;
        lastKnownPushedSum = 0f;
        lastKnownHP = GameManager.Instance.CurrentDwarfHP;
    }

    // ==================== REWARDS ====================

    public override void OnActionReceived(ActionBuffers actions)
    {
        player.ExternalControl = true;

        // --- Récompenses par détection de deltas (polling, zéro couplage avec le gameplay) ---
        var gm = GameManager.Instance;

        // Reward par façon de tuer (voir GameManager.RegisterEnemyKilled). Pas le score
        // en points : il varie selon le type d'ennemi et le bonus de chaîne. Suicides
        // d'ennemis contre le nain = pas de kill.
        if (gm.DirectKills > lastKnownDirectKills)
        {
            AddReward(directKillReward * (gm.DirectKills - lastKnownDirectKills));
            lastKnownDirectKills = gm.DirectKills;
        }

        if (gm.PushedKillMultiplierSum > lastKnownPushedSum)
        {
            AddReward(killReward * (gm.PushedKillMultiplierSum - lastKnownPushedSum));
            lastKnownPushedSum = gm.PushedKillMultiplierSum;
        }

        if (gm.CurrentDwarfHP < lastKnownHP)
        {
            AddReward(damagePenalty * (lastKnownHP - gm.CurrentDwarfHP));
            lastKnownHP = gm.CurrentDwarfHP;
        }

        AddReward(stepCost);

        // --- Mort : fin d'épisode avec sanction ---
        // AddReward (pas SetReward) : s'ajoute au damagePenalty/stepCost déjà
        // appliqués plus haut cette même étape, au lieu de les écraser.
        if (gm.IsGameOver)
        {
            AddReward(deathPenalty);
            EndEpisode();   // déclenchera OnEpisodeBegin → ResetGame()
            return;
        }

        // --- Actions (inchangé) ---
        var da = actions.DiscreteActions;
        int mx = da[0] - 1;
        int my = da[1] - 1;
        player.ExternalMoveDir = new Vector2(mx, my).normalized;

        if (da[2] == 1) hammer.RequestSwing();
        shield.RequestShield(da[3] == 1);
    }

    // ==================== OBSERVATIONS ====================

    private bool loggedFallbackOnce = false;  // Anti-spam console du garde-fou

    public override void CollectObservations(VectorSensor sensor)
    {
        Vector2 pos = transform.position;
        var gm = GameManager.Instance;

        // GARDE-FOU : à la frame de destruction (mort), player/hammer/gm peuvent être
        // nuls. On injecte des valeurs neutres pour garantir TOUJOURS le même nombre d'observations.
        bool refsValid = player != null && hammer != null && gm != null;

        if (!refsValid && !loggedFallbackOnce)
        {
            Debug.LogWarning("DwarfAgent: références nulles dans CollectObservations — " +
                             "observations neutres envoyées. Cause probable : destruction " +
                             "du Player au GameOver (GameOver doit désactiver, pas Destroy).");
            loggedFallbackOnce = true;
        }

        // --- Position du nain (2) ---
        sensor.AddObservation(pos.x / 6f);
        sensor.AddObservation(pos.y / 4f);

        // --- État du nain (7) ---
        if (refsValid)
        {
            sensor.AddObservation(player.LastMoveDirection);
            sensor.AddObservation(hammer.CooldownFraction);
            sensor.AddObservation(gm.CurrentDwarfHP / 3f);
            sensor.AddObservation(player.IsShielding ? 1f : 0f);
            sensor.AddObservation(player.IsSlowPenalty ? 1f : 0f);
            sensor.AddObservation(player.StunTimer > 0f ? 1f : 0f);
        }
        else
        {
            sensor.AddObservation(Vector2.zero);            // direction (2 obs)
            for (int k = 0; k < 5; k++) sensor.AddObservation(0f); // reste (5 obs)
        }

        // --- Pics (2 × observedSpikes) ---
        for (int i = 0; i < observedSpikes; i++)
        {
            if (spikeTraps != null && i < spikeTraps.Length && spikeTraps[i] != null)
            {
                Vector2 delta = (Vector2)spikeTraps[i].transform.position - pos;
                sensor.AddObservation(delta.x / 6f);
                sensor.AddObservation(delta.y / 4f);
            }
            else { sensor.AddObservation(0f); sensor.AddObservation(0f); }
        }

        // --- Ravin le plus proche du nain (2) ---
        AddNearestHazardObservation(sensor, pos, HazardRegistry.Ravines);

        // --- Ennemis les plus proches (6 chacun) ---
        // Sélection des `observedEnemies` plus proches par insertion dans un petit
        // buffer trié réutilisé (nearestEnemies/nearestDistSq) : évite le
        // FindObjectsByType + l'allocation de liste + le tri complet d'avant,
        // qui tournaient à chaque décision (des millions de fois à l'entraînement)
        for (int i = 0; i < observedEnemies; i++)
        {
            nearestEnemies[i] = null;
            nearestDistSq[i] = float.MaxValue;
        }

        foreach (var e in EnemyAI.Alive)
        {
            if (e == null) continue;

            float distSq = (pos - (Vector2)e.transform.position).sqrMagnitude;
            if (distSq >= nearestDistSq[observedEnemies - 1]) continue;

            int insertAt = observedEnemies - 1;
            while (insertAt > 0 && nearestDistSq[insertAt - 1] > distSq)
            {
                nearestDistSq[insertAt] = nearestDistSq[insertAt - 1];
                nearestEnemies[insertAt] = nearestEnemies[insertAt - 1];
                insertAt--;
            }
            nearestDistSq[insertAt] = distSq;
            nearestEnemies[insertAt] = e;
        }

        for (int i = 0; i < observedEnemies; i++)
        {
            if (nearestEnemies[i] != null)
            {
                Rigidbody2D erb = nearestEnemies[i].GetComponent<Rigidbody2D>();
                Vector2 delta = (Vector2)nearestEnemies[i].transform.position - pos;
                sensor.AddObservation(delta.x / 6f);
                sensor.AddObservation(delta.y / 4f);
                sensor.AddObservation(delta.magnitude / 6f);
                sensor.AddObservation(erb != null ? erb.linearVelocity.magnitude / 5f : 0f);
                // Danger (pic ou ravin) le plus proche DE CET ENNEMI : l'info qui permet
                // de décider où le pousser
                AddNearestHazardObservation(sensor, nearestEnemies[i].transform.position, HazardRegistry.Colliders);
            }
            else
            {
                for (int j = 0; j < 6; j++) sensor.AddObservation(0f);
            }
        }
    }

    // Vecteur (2 valeurs) de `origin` vers le point le plus proche parmi `hazards` ;
    // zéros s'il n'y en a aucun
    private static void AddNearestHazardObservation(VectorSensor sensor, Vector2 origin, List<Collider2D> hazards)
    {
        float bestDistSq = float.MaxValue;
        Vector2 bestDelta = Vector2.zero;

        foreach (var hazard in hazards)
        {
            if (hazard == null) continue;

            Vector2 delta = hazard.ClosestPoint(origin) - origin;
            float distSq = delta.sqrMagnitude;
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                bestDelta = delta;
            }
        }

        sensor.AddObservation(bestDelta.x / 6f);
        sensor.AddObservation(bestDelta.y / 4f);
    }

    // ==================== HEURISTIC (inchangé) ====================

    public override void Heuristic(in ActionBuffers actionsOut)
    {
        player.ExternalControl = false;

        var da = actionsOut.DiscreteActions;
        da[0] = Mathf.RoundToInt(Input.GetAxisRaw("Horizontal")) + 1;
        da[1] = Mathf.RoundToInt(Input.GetAxisRaw("Vertical")) + 1;
        da[2] = pendingSwing ? 1 : 0;
        pendingSwing = false;
        da[3] = pendingShieldState ? 1 : 0;
    }
}