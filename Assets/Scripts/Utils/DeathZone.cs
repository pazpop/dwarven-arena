// Trigger de ravin autour de l'arène : fait tomber les ennemis (voir FallIntoRavine)
// et tue le Nain instantanément au contact.
using UnityEngine;

public class DeathZone : MonoBehaviour
{
    private Collider2D col;

    private void Awake()
    {
        col = GetComponent<Collider2D>();
        HazardRegistry.Register(col);
    }

    private void OnDestroy()
    {
        HazardRegistry.Unregister(col);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        EnemyAI enemy = other.GetComponent<EnemyAI>();
        if (enemy != null)
        {
            enemy.FallIntoRavine();
            return;
        }

        if (other.CompareTag("Player"))
        {
            GameManager.Instance.InstantKillPlayer();
        }
    }
}