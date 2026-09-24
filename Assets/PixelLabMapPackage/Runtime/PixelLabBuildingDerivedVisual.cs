using UnityEngine;

namespace PixelLab.MapExport
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PixelLabBuildingDerivedVisual : MonoBehaviour
    {
        [SerializeField] private PixelLabBuildingController controller;
        [SerializeField] private PixelLabBuildingLaneTilemap lane;
        [SerializeField] private int q;
        [SerializeField] private int r;
        [SerializeField] private int tileIndex;
        [SerializeField] private double drawOrder;

        public PixelLabBuildingController Controller => controller;
        public PixelLabBuildingLaneTilemap Lane => lane;
        public int Q => q;
        public int R => r;
        public int TileIndex => tileIndex;
        public int LayerY => lane == null ? 0 : lane.LayerY;
        public string LaneId => lane == null ? string.Empty : lane.LaneId;
        public string Role => lane == null ? string.Empty : lane.Role;
        public double DrawOrder => drawOrder;
        public SpriteRenderer Renderer => GetComponent<SpriteRenderer>();

        public void Configure(
            PixelLabBuildingController owner,
            PixelLabBuildingLaneTilemap sourceLane,
            int logicalQ,
            int logicalR,
            PixelLabBuildingNativeTile tile,
            Vector3 worldPivot,
            double sourceDrawOrder
        )
        {
            if (owner == null)
            {
                throw new System.ArgumentNullException(nameof(owner));
            }
            if (sourceLane == null || sourceLane.Controller != owner)
            {
                throw new System.InvalidOperationException(
                    "A derived Buildings visual requires one of its controller's native lanes."
                );
            }
            if (tile == null || tile.sprite == null)
            {
                throw new System.InvalidOperationException(
                    "A derived Buildings visual requires a generated native tile sprite."
                );
            }

            controller = owner;
            lane = sourceLane;
            q = logicalQ;
            r = logicalR;
            tileIndex = tile.tileIndex;
            drawOrder = sourceDrawOrder;
            transform.SetParent(sourceLane.transform, false);
            transform.position = worldPivot;
            transform.localScale = Vector3.one;
            var renderer = Renderer;
            renderer.sprite = tile.sprite;
            renderer.color = Color.white;
        }
    }
}
