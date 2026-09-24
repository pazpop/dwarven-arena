using UnityEngine;

namespace PixelLab.MapExport
{
    public sealed class PixelLabAnnotationPoint : MonoBehaviour
    {
        public string pointId;
        public string annotationLayer;

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.85f, 1f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, 0.2f);
            Gizmos.DrawLine(
                transform.position + Vector3.left * 0.3f,
                transform.position + Vector3.right * 0.3f
            );
            Gizmos.DrawLine(
                transform.position + Vector3.down * 0.3f,
                transform.position + Vector3.up * 0.3f
            );
        }
    }
}
