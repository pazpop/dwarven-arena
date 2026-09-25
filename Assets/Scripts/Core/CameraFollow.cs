// Fait suivre la caméra à une cible (le Nain), en se figeant à la mort du joueur.
using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    public Transform target;
    public float smoothSpeed = 5f;
    public Vector3 offset = new Vector3(0, 0, -10);

    private void LateUpdate()
    {
        if (target == null) return;

        // Fige la vue au dernier cadrage vivant : sinon, une mort dans un ravin/piège
        // recentre la caméra sur le cadavre, souvent au-dessus du vide hors du sol
        if (GameManager.Instance != null && GameManager.Instance.IsGameOver) return;

        Vector3 desiredPosition = target.position + offset;
        transform.position = Vector3.Lerp(transform.position, desiredPosition, smoothSpeed * Time.deltaTime);
    }
}
