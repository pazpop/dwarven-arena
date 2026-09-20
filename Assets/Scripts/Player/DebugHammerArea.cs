using UnityEngine;

public class DebugHammerArea : MonoBehaviour
{
    public float radius = 1.2f;
    public Color color = Color.red;
    
    private void OnDrawGizmos()
    {
        Gizmos.color = color;
        Vector2 dir = Vector2.right; // Direction par défaut en éditeur
        Vector2 center = (Vector2)transform.position + dir * (radius * 0.6f);
        Gizmos.DrawWireSphere(center, radius);
    }
}