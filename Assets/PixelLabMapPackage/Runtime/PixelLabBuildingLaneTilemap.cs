using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Tilemap), typeof(TilemapRenderer))]
    public sealed class PixelLabBuildingLaneTilemap : MonoBehaviour
    {
        [SerializeField] private PixelLabBuildingController controller;
        [SerializeField] private Tilemap tilemap;
        [SerializeField] private string laneId;
        [SerializeField] private string role;
        [SerializeField] private int depth;
        [SerializeField] private int layerY;

        public PixelLabBuildingController Controller => controller;
        public Tilemap Tilemap => tilemap;
        public string LaneId => laneId;
        public string Role => role;
        public int Depth => depth;
        public int LayerY => layerY;

        public void Configure(
            PixelLabBuildingController owner,
            PixelLabBuildingVisualLane definition,
            int signedStorey
        )
        {
            if (definition == null)
            {
                throw new System.ArgumentNullException(nameof(definition));
            }
            controller = owner;
            tilemap = GetComponent<Tilemap>();
            laneId = definition.id;
            role = definition.role;
            depth = definition.depth;
            layerY = signedStorey;
            PixelLabBuildingRoles.Require(role);

            var renderer = GetComponent<TilemapRenderer>();
            renderer.mode = TilemapRenderer.Mode.Individual;
            // Same painter's-order rule as the map tilemaps: iso starts on the
            // Right cell edge, everything else Top-Left (probed on the real
            // oblique building map — TopRight strips the floor seams).
            renderer.sortOrder = owner.Kit.gridKind == "iso"
                ? TilemapRenderer.SortOrder.TopRight
                : TilemapRenderer.SortOrder.TopLeft;
            renderer.enabled = true;
            renderer.sortingOrder = signedStorey * 1000 + definition.depth;
        }
    }
}
