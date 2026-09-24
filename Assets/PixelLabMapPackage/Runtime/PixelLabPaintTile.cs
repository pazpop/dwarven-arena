using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport
{
    [CreateAssetMenu(menuName = "PixelLab/Paint Tile", fileName = "PixelLabPaintTile")]
    public sealed class PixelLabPaintTile : Tile
    {
        public int tileIndex;
        public int paintTerrain = -1;
        public bool paintableTerrain = true;
        public int sourceLayerIndex = -1;
        public string assetPath;

        public bool IsTerrainBrush
        {
            get { return paintTerrain >= 0; }
        }

        public override void GetTileData(
            Vector3Int position,
            ITilemap tilemap,
            ref TileData tileData
        )
        {
            base.GetTileData(position, tilemap, ref tileData);
            var component = tilemap.GetComponent<Tilemap>();
            if (component == null
                || component.cellLayout != GridLayout.CellLayout.Hexagon)
            {
                return;
            }
            // Under the Hexagon layout Unity's interpolated anchor has a
            // negative-cell parity discontinuity. Projection tilemaps use a
            // zero-Y anchor (set by CreateTilemapObject), which makes the
            // correction below a constant offset and preserves the editor's
            // linear q/r lattice for both authored and newly painted cells.
            var anchor = component.tileAnchor;
            var actual = component.CellToLocalInterpolated(
                (Vector3)position + anchor
            );
            var center = component.GetCellCenterLocal(position);
            tileData.transform =
                Matrix4x4.Translate(center - actual) * tileData.transform;
        }
    }
}
