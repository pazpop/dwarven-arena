// Liste partagée des colliders de danger (DeathZone + SpikeTrap), remplie une seule
// fois par auto-enregistrement (Awake/OnDestroy) au lieu d'un FindObjectsByType
// refait par chaque SpawnManager/EnemyAI pour le même résultat.
using System.Collections.Generic;
using UnityEngine;

public static class HazardRegistry
{
    public static readonly List<Collider2D> Colliders = new List<Collider2D>();

    // Sous-ensemble : les ravins seulement (DeathZone), pour les observations de l'agent
    public static readonly List<Collider2D> Ravines = new List<Collider2D>();

    // Ce projet a Domain Reload + Scene Reload désactivés (voir EditorSettings) :
    // sans ce reset explicite au (re)lancement du Play, les DeathZone/SpikeTrap de
    // la scène se ré-enregistreraient sur une liste jamais vidée entre deux sessions
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Colliders.Clear();
        Ravines.Clear();
    }

    public static void Register(Collider2D col, bool isRavine = false)
    {
        Colliders.Add(col);
        if (isRavine) Ravines.Add(col);
    }

    public static void Unregister(Collider2D col)
    {
        Colliders.Remove(col);
        Ravines.Remove(col);
    }
}
