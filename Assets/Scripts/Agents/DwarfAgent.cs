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

    [Header("Récompenses")]
    public float killReward = 1f;
    public float damagePenalty = -0.3f;
    public float deathPenalty = -1f;
    public float stepCost = -0.0005f;

    // Suivi des deltas pour les rewards par polling
    private int lastKnownScore;
    private int lastKnownHP;

    private bool pendingSwing = false;
    private bool pendingShieldState = false;

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

        // Durée max d'un épisode : 3000 décisions × 5 frames ≈ 4 minutes de jeu.
        // Au-delà, l'épisode se termine (sans pénalité de mort) — évite les parties infinies.
        MaxStep = 3000;
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
        lastKnownScore = 0;
        lastKnownHP = GameManager.Instance.CurrentDwarfHP;
    }

    // ==================== REWARDS ====================

    public override void OnActionReceived(ActionBuffers actions)
    {
        player.ExternalControl = true;

        // --- Récompenses par détection de deltas (polling, zéro couplage avec le gameplay) ---
        var gm = GameManager.Instance;

        if (gm.Score > lastKnownScore)
        {
            // Chaque +10 de score = un kill validé (les suicides ne rapportent rien — anti-reward-hacking)
            AddReward(killReward * (gm.Score - lastKnownScore) / 10f);
            lastKnownScore = gm.Score;
        }

        if (gm.CurrentDwarfHP < lastKnownHP)
        {
            AddReward(damagePenalty * (lastKnownHP - gm.CurrentDwarfHP));
            lastKnownHP = gm.CurrentDwarfHP;
        }

        AddReward(stepCost);

        // --- Mort : fin d'épisode avec sanction ---
        if (gm.IsGameOver)
        {
            SetReward(deathPenalty);
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
        // nuls. On injecte des valeurs neutres pour garantir TOUJOURS 25 observations.
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

        // --- Pics (4) ---
        if (spikeTraps != null)
        {
            for (int i = 0; i < 2; i++)
            {
                if (i < spikeTraps.Length && spikeTraps[i] != null)
                {
                    Vector2 delta = (Vector2)spikeTraps[i].transform.position - pos;
                    sensor.AddObservation(delta.x / 6f);
                    sensor.AddObservation(delta.y / 4f);
                }
                else { sensor.AddObservation(0f); sensor.AddObservation(0f); }
            }
        }
        else { for (int i = 0; i < 4; i++) sensor.AddObservation(0f); }

        // --- Ennemis les plus proches (12) ---
        var allEnemies = FindObjectsByType<EnemyAI>();
        var validEnemies = new System.Collections.Generic.List<EnemyAI>(allEnemies.Length);
        foreach (var e in allEnemies)
        {
            if (e != null) validEnemies.Add(e);
        }

        validEnemies.Sort((a, b) =>
            (pos - (Vector2)a.transform.position).sqrMagnitude
                .CompareTo((pos - (Vector2)b.transform.position).sqrMagnitude));

        for (int i = 0; i < observedEnemies; i++)
        {
            if (i < validEnemies.Count)
            {
                Rigidbody2D erb = validEnemies[i].GetComponent<Rigidbody2D>();
                Vector2 delta = (Vector2)validEnemies[i].transform.position - pos;
                sensor.AddObservation(delta.x / 6f);
                sensor.AddObservation(delta.y / 4f);
                sensor.AddObservation(delta.magnitude / 6f);
                sensor.AddObservation(erb != null ? erb.linearVelocity.magnitude / 5f : 0f);
            }
            else
            {
                for (int j = 0; j < 4; j++) sensor.AddObservation(0f);
            }
        }
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