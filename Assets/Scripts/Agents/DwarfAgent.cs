using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using UnityEngine;

public class DwarfAgent : Agent
{
    [Header("Références")]
    public PlayerMovement player;
    public HammerController hammer;
    public ShieldController shield;

    // Buffer d'inputs clavier (mode Heuristic)
    private bool pendingSwing = false;
    private bool pendingShieldState = false;

    public override void Initialize()
    {
        if (player == null) player = GetComponent<PlayerMovement>();
        if (hammer == null) hammer = GetComponent<HammerController>();
        if (shield == null) shield = GetComponent<ShieldController>();

        // En mode Heuristic (play éditeur), le clavier pilote le nain.
        player.ExternalControl = false;
    }

    // Bufferise les inputs clavier TOUJOURS — pas conditionné par ExternalControl
    private void Update()
    {
        // Swing : on "latch" l'appui (GetKeyDown = 1 frame)
        if (Input.GetKeyDown(KeyCode.Space))
        {
            pendingSwing = true;
        }
        
        // Bouclier : GetMouseButton est maintenu, pas de latch nécessaire
        pendingShieldState = Input.GetMouseButton(0);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // Mode entraînement : l'agent ML prend le contrôle
        player.ExternalControl = true;

        var da = actions.DiscreteActions;
        int mx = da[0] - 1;
        int my = da[1] - 1;
        player.ExternalMoveDir = new Vector2(mx, my).normalized;

        if (da[2] == 1) hammer.RequestSwing();
        shield.RequestShield(da[3] == 1);
    }

    // Mode Heuristic (play éditeur sans entraînement) — consomme les buffers
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        player.ExternalControl = false;

        var da = actionsOut.DiscreteActions;
        da[0] = Mathf.RoundToInt(Input.GetAxisRaw("Horizontal")) + 1;
        da[1] = Mathf.RoundToInt(Input.GetAxisRaw("Vertical")) + 1;
        
        // Swing : consume le latch (et reset le buffer)
        da[2] = pendingSwing ? 1 : 0;
        pendingSwing = false;  // Reset après consommation
        
        // Bouclier : état maintenu
        da[3] = pendingShieldState ? 1 : 0;
    }
}