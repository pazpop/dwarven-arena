// Trie l'ordre de rendu d'un SpriteRenderer selon sa position Y (vue du dessus :
// plus bas sur la carte = plus proche de la caméra = affiché par-dessus). Sans
// ça, un sprite plus grand que sa case (tête d'ennemi, pics) se superpose mal
// selon qui devrait passer devant. Un décalage fixe garde tout au-dessus du sol
// (Tilemap, sortingOrder 0) quel que soit le signe de Y.
using UnityEngine;

public class YSortRenderer : MonoBehaviour
{
    private const int BaseOffset = 10000;
    private const int Precision = 100;

    private SpriteRenderer sr;

    private void Awake() => sr = GetComponent<SpriteRenderer>();

    private void LateUpdate()
    {
        sr.sortingOrder = BaseOffset - (int)(transform.position.y * Precision);
    }
}
