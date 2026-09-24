using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport
{
    [ExecuteAlways]
    [RequireComponent(typeof(Tilemap))]
    public sealed class PixelLabTileRenderer : MonoBehaviour
    {
        private const int PatternCenterMatchScore = 10;
        private const int PatternCenterMismatchScore = -30;
        private const int PatternRingMatchScore = 1;
        private const int PatternRingMismatchScore = -1;

        public TextAsset manifestAsset;
        public Transform mapRoot;
        public PixelLabPaintTile[] availableTiles;
        public int layerIndex;
        public int layerY;
        public bool legacy;
        public bool legacyShared;
        public bool logicalProjectionTerrain;
        public string gridKind;
        public string paletteAssetPath;
        public string advancedPaletteAssetPath;
        public float pixelsPerUnit = 16f;

        private readonly Dictionary<int, PixelLabPaintTile> tilesByIndex =
            new Dictionary<int, PixelLabPaintTile>();
        private readonly Dictionary<string, PixelLabPlacement> authoredPlacements =
            new Dictionary<string, PixelLabPlacement>();
        private readonly Dictionary<string, PixelLabPaintTile> sharedLegacyTiles =
            new Dictionary<string, PixelLabPaintTile>();
        private readonly Dictionary<Vector2Int, int> lastCells =
            new Dictionary<Vector2Int, int>();
        [SerializeField, HideInInspector]
        private bool sharedLegacyStateInitialized;
        [SerializeField, HideInInspector]
        private PixelLabPaintValue[] sharedLegacyPaintValues =
            Array.Empty<PixelLabPaintValue>();
        private readonly Dictionary<Vector2Int, int> sharedLegacyTerrain =
            new Dictionary<Vector2Int, int>();

        private PixelLabMapManifest manifest;
        private PixelLabProjectionLayer projectionLayer;
        private PixelLabLegacyLayer legacyLayer;
        private PixelLabLegacyLayer[] legacyLayers = Array.Empty<PixelLabLegacyLayer>();
        private PixelLabUnityTileRules rules;
        private Tilemap tilemap;
        private int maxLayerY;
        private int lastSignature = int.MinValue;
        private bool synchronizing;

        private void OnEnable()
        {
            Reload();
        }

        private void OnValidate()
        {
            if (isActiveAndEnabled)
            {
                Reload();
            }
        }

        private void Update()
        {
            RefreshIfChanged();
        }

        private void OnDisable()
        {
        }

        public void Reload()
        {
            tilemap = GetComponent<Tilemap>();
            var nativeRenderer = GetComponent<TilemapRenderer>();
            if (nativeRenderer != null)
            {
                nativeRenderer.enabled = true;
                // Individual, not Chunk: tall sprites (iso diamonds, hex
                // prisms, oblique walls, 1.0-transition tiles) overlap their
                // neighbours and must sort per sprite along the transparency
                // sort axis; Chunk batches whole chunks and breaks that order.
                nativeRenderer.mode = TilemapRenderer.Mode.Individual;
                // iso (XYZ) and hex-flat-top (YXZ) both map screen-down to
                // ascending cell.x, so their painter's order starts on the
                // Right cell edge (measured via sort-order probe on the real
                // hex cliff map); the rest start Top-Left.
                nativeRenderer.sortOrder = gridKind == "iso" || gridKind == "hex-flat-top"
                    ? TilemapRenderer.SortOrder.TopRight
                    : TilemapRenderer.SortOrder.TopLeft;
            }

            manifest = manifestAsset == null
                ? null
                : JsonUtility.FromJson<PixelLabMapManifest>(manifestAsset.text);
            projectionLayer = null;
            legacyLayer = null;
            legacyLayers = Array.Empty<PixelLabLegacyLayer>();
            rules = null;
            maxLayerY = 0;
            tilesByIndex.Clear();
            authoredPlacements.Clear();
            lastCells.Clear();
            sharedLegacyTiles.Clear();
            sharedLegacyTerrain.Clear();

            foreach (var tile in availableTiles ?? Array.Empty<PixelLabPaintTile>())
            {
                if (tile != null && tile.sourceLayerIndex >= 0 && !tile.IsTerrainBrush)
                {
                    sharedLegacyTiles[SharedTileKey(
                        tile.sourceLayerIndex,
                        tile.tileIndex
                    )] = tile;
                }
                if (tile != null && !tile.IsTerrainBrush)
                {
                    tilesByIndex[tile.tileIndex] = tile;
                }
            }
            foreach (var tile in availableTiles ?? Array.Empty<PixelLabPaintTile>())
            {
                if (tile != null && !tilesByIndex.ContainsKey(tile.tileIndex))
                {
                    tilesByIndex[tile.tileIndex] = tile;
                }
            }

            if (manifest != null && legacy)
            {
                var layers = manifest.legacyTerrain == null
                    ? Array.Empty<PixelLabLegacyLayer>()
                    : manifest.legacyTerrain.layers ?? Array.Empty<PixelLabLegacyLayer>();
                if (legacyShared)
                {
                    legacyLayers = layers;
                    LoadSharedLegacyTerrain();
                }
                else if (layerIndex >= 0 && layerIndex < layers.Length)
                {
                    legacyLayer = layers[layerIndex];
                    rules = legacyLayer.unityTileRules;
                    IndexPlacements(legacyLayer.placements);
                }
            }
            else if (manifest != null)
            {
                var layers = manifest.projectionLayers ?? Array.Empty<PixelLabProjectionLayer>();
                if (layerIndex >= 0 && layerIndex < layers.Length)
                {
                    projectionLayer = layers[layerIndex];
                    rules = projectionLayer.unityTileRules;
                    IndexPlacements(projectionLayer.placements);
                    foreach (var placement in projectionLayer.placements
                        ?? Array.Empty<PixelLabPlacement>())
                    {
                        maxLayerY = Mathf.Max(maxLayerY, placement.layerY);
                    }
                }
            }

            lastSignature = int.MinValue;
            PrimeLastCells();
            RefreshIfChanged();
        }

        public void RefreshNow()
        {
            lastSignature = int.MinValue;
            RefreshIfChanged();
        }

        public Tilemap[] HigherObliqueTilemaps()
        {
            if (mapRoot == null || gridKind != "oblique" || layerY != 0)
            {
                return Array.Empty<Tilemap>();
            }
            return mapRoot.GetComponentsInChildren<PixelLabTileRenderer>(true)
                .Where(renderer => renderer != null
                    && renderer != this
                    && !renderer.legacy
                    && renderer.gridKind == gridKind
                    && renderer.layerIndex == layerIndex
                    && renderer.layerY > layerY)
                .Select(renderer => renderer.GetComponent<Tilemap>())
                .Where(item => item != null)
                .ToArray();
        }

        public void ClearHigherObliqueCells(IEnumerable<Vector3Int> cells)
        {
            var axialCells = cells
                .Select(cell => PixelLabGridCoordinates.ToAxial(gridKind, cell))
                .Distinct()
                .ToArray();
            foreach (var higherTilemap in HigherObliqueTilemaps())
            {
                var higherRenderer = higherTilemap.GetComponent<PixelLabTileRenderer>();
                foreach (var axial in axialCells)
                {
                    higherTilemap.SetTile(
                        PixelLabGridCoordinates.ToUnityCell(
                            gridKind,
                            axial.x,
                            axial.y
                        ),
                        null
                    );
                }
                if (higherRenderer != null)
                {
                    higherRenderer.RefreshNow();
                }
            }
        }

        public Vector3Int[] TerrainPaintCells(IEnumerable<Vector3Int> cells)
        {
            var sourceCells = cells.ToArray();
            var cornerCellTerrain = UsesCornerRules()
                || legacyShared && !legacyLayers.Any(layer => layer.sidescroller);
            if (!cornerCellTerrain)
            {
                return sourceCells;
            }

            var output = new HashSet<Vector3Int>();
            foreach (var sourceCell in sourceCells)
            {
                var axial = PixelLabGridCoordinates.ToAxial(gridKind, sourceCell);
                for (var r = 0; r <= 1; r++)
                {
                    for (var q = 0; q <= 1; q++)
                    {
                        output.Add(PixelLabGridCoordinates.ToUnityCell(
                            gridKind,
                            axial.x + q,
                            axial.y + r
                        ));
                    }
                }
            }
            return output.ToArray();
        }

        private void PrimeLastCells()
        {
            lastCells.Clear();
            foreach (var pair in CollectCells())
            {
                lastCells[pair.Key] = legacyShared || logicalProjectionTerrain
                    ? pair.Value.paintTerrain
                    : pair.Value.tileIndex;
            }
        }

        private void IndexPlacements(PixelLabPlacement[] placements)
        {
            foreach (var placement in placements ?? Array.Empty<PixelLabPlacement>())
            {
                authoredPlacements[CellKey(placement.q, placement.r, placement.layerY)] = placement;
            }
        }

        private void RefreshIfChanged()
        {
            if (tilemap == null || manifest == null || synchronizing)
            {
                return;
            }

            var signature = TilemapSignature();
            if (signature == lastSignature)
            {
                return;
            }

            var cells = CollectCells();
            if (!legacy && !legacyShared && UsesCornerRules())
            {
                cells = NormalizeCornerTerrainBrushes(cells);
            }
            if (legacyShared)
            {
                SynchronizeSharedLegacyTiles(cells);
            }
            else if (logicalProjectionTerrain && UsesCornerRules())
            {
                SynchronizeLogicalCornerTiles(cells);
            }
            else if (logicalProjectionTerrain && UsesEdgeRules())
            {
                SynchronizeLogicalEdgeTiles(cells);
            }
            else if (rules != null && rules.ruleType == "corner" && rules.arity == 4)
            {
                SynchronizeCornerTiles(cells);
            }
            else if (rules != null && rules.ruleType == "edge"
                && (rules.arity == 4 || rules.arity == 6))
            {
                SynchronizeEdgeTiles(cells);
            }

            lastCells.Clear();
            foreach (var pair in CollectCells())
            {
                lastCells[pair.Key] = legacyShared || logicalProjectionTerrain
                    ? pair.Value.paintTerrain
                    : pair.Value.tileIndex;
            }
            lastSignature = TilemapSignature();
        }

        private Dictionary<Vector2Int, PixelLabPaintTile> NormalizeCornerTerrainBrushes(
            Dictionary<Vector2Int, PixelLabPaintTile> cells
        )
        {
            var brushCells = cells
                .Where(pair => pair.Value != null && pair.Value.IsTerrainBrush)
                .OrderBy(pair => pair.Key.y)
                .ThenBy(pair => pair.Key.x)
                .ToArray();
            if (brushCells.Length == 0)
            {
                return cells;
            }

            var normalized = new Dictionary<Vector2Int, PixelLabPaintTile>(cells);
            foreach (var pair in brushCells)
            {
                for (var r = 0; r <= 1; r++)
                {
                    for (var q = 0; q <= 1; q++)
                    {
                        var vertex = pair.Key + new Vector2Int(q, r);
                        normalized[vertex] = pair.Value;
                        tilemap.SetTile(
                            PixelLabGridCoordinates.ToUnityCell(
                                gridKind,
                                vertex.x,
                                vertex.y
                            ),
                            pair.Value
                        );
                    }
                }
            }
            return normalized;
        }

        private int TilemapSignature()
        {
            unchecked
            {
                var hash = 17;
                foreach (var position in tilemap.cellBounds.allPositionsWithin)
                {
                    var tile = tilemap.GetTile(position) as PixelLabPaintTile;
                    if (tile == null)
                    {
                        continue;
                    }
                    hash = hash * 31 + position.x;
                    hash = hash * 31 + position.y;
                    hash = hash * 31 + tile.GetEntityId().GetHashCode();
                    hash = hash * 31 + tile.paintTerrain;
                }
                return hash;
            }
        }

        private Dictionary<Vector2Int, PixelLabPaintTile> CollectCells()
        {
            var cells = new Dictionary<Vector2Int, PixelLabPaintTile>();
            foreach (var position in tilemap.cellBounds.allPositionsWithin)
            {
                var tile = tilemap.GetTile(position) as PixelLabPaintTile;
                if (tile == null)
                {
                    continue;
                }
                cells[PixelLabGridCoordinates.ToAxial(gridKind, position)] = tile;
            }
            return cells;
        }

        private List<Vector2Int> ChangedCells(
            Dictionary<Vector2Int, PixelLabPaintTile> current
        )
        {
            var changed = new List<Vector2Int>();
            foreach (var pair in current)
            {
                int previous;
                if (pair.Value.IsTerrainBrush
                    || !lastCells.TryGetValue(pair.Key, out previous)
                    || previous != (legacyShared || logicalProjectionTerrain ? pair.Value.paintTerrain : pair.Value.tileIndex))
                {
                    changed.Add(pair.Key);
                }
            }
            foreach (var pair in lastCells)
            {
                if (!current.ContainsKey(pair.Key))
                {
                    changed.Add(pair.Key);
                }
            }
            return changed;
        }

        private void SynchronizeCornerTiles(
            Dictionary<Vector2Int, PixelLabPaintTile> current
        )
        {
            var changed = ChangedCells(current);
            var paintedVertices = changed
                .Where(cell => current.TryGetValue(cell, out var tile)
                    && tile.IsTerrainBrush)
                .OrderBy(cell => cell.y)
                .ThenBy(cell => cell.x)
                .ToArray();
            var removedCells = changed
                .Where(cell => !current.ContainsKey(cell) && lastCells.ContainsKey(cell))
                .ToArray();
            if (paintedVertices.Length == 0 && removedCells.Length == 0)
            {
                return;
            }

            synchronizing = true;
            var masks = new Dictionary<Vector2Int, int>();
            foreach (var pair in current)
            {
                if (!pair.Value.IsTerrainBrush)
                {
                    masks[pair.Key] = MaskForTile(pair.Value.tileIndex);
                    continue;
                }
                int previousTileIndex;
                if (lastCells.TryGetValue(pair.Key, out previousTileIndex))
                {
                    masks[pair.Key] = MaskForTile(previousTileIndex);
                }
            }

            var affected = new HashSet<Vector2Int>();
            foreach (var vertex in paintedVertices)
            {
                var terrainBit = CornerTerrainBit(current[vertex].paintTerrain);
                ApplyCornerVertex(masks, vertex, terrainBit, affected);
            }
            foreach (var cell in removedCells)
            {
                ApplyCornerVertex(masks, cell, 0, affected);
                ApplyCornerVertex(masks, cell + new Vector2Int(1, 0), 0, affected);
                ApplyCornerVertex(masks, cell + new Vector2Int(0, 1), 0, affected);
                ApplyCornerVertex(masks, cell + new Vector2Int(1, 1), 0, affected);
            }
            foreach (var cell in affected)
            {
                if (removedCells.Contains(cell))
                {
                    tilemap.SetTile(
                        PixelLabGridCoordinates.ToUnityCell(gridKind, cell.x, cell.y),
                        null
                    );
                    continue;
                }
                int mask;
                if (!masks.TryGetValue(cell, out mask))
                {
                    continue;
                }
                var desiredTile = TileForMask(mask, 4);
                if (desiredTile >= 0)
                {
                    SetTileIndex(cell, desiredTile);
                }
            }
            synchronizing = false;
        }

        private static void ApplyCornerVertex(
            Dictionary<Vector2Int, int> masks,
            Vector2Int vertex,
            int terrainBit,
            HashSet<Vector2Int> affected
        )
        {
            var before = new Dictionary<Vector2Int, int>(masks);
            ApplyCornerVertexToCell(
                masks, before, vertex + new Vector2Int(-1, -1), 1, terrainBit, affected
            );
            ApplyCornerVertexToCell(
                masks, before, vertex + new Vector2Int(0, -1), 2, terrainBit, affected
            );
            ApplyCornerVertexToCell(
                masks, before, vertex + new Vector2Int(-1, 0), 4, terrainBit, affected
            );
            ApplyCornerVertexToCell(
                masks, before, vertex, 8, terrainBit, affected
            );
        }

        private static void ApplyCornerVertexToCell(
            Dictionary<Vector2Int, int> masks,
            Dictionary<Vector2Int, int> before,
            Vector2Int cell,
            int bit,
            int terrainBit,
            HashSet<Vector2Int> affected
        )
        {
            int mask;
            if (!before.TryGetValue(cell, out mask))
            {
                mask = CornerMaskFromNeighbours(before, cell);
            }
            masks[cell] = terrainBit == 1 ? mask | bit : mask & ~bit;
            affected.Add(cell);
        }

        private static int CornerMaskFromNeighbours(
            Dictionary<Vector2Int, int> masks,
            Vector2Int cell
        )
        {
            return DerivedCornerVertexBit(masks, cell) << 3
                | DerivedCornerVertexBit(masks, cell + new Vector2Int(1, 0)) << 2
                | DerivedCornerVertexBit(masks, cell + new Vector2Int(0, 1)) << 1
                | DerivedCornerVertexBit(masks, cell + new Vector2Int(1, 1));
        }

        private static int DerivedCornerVertexBit(
            Dictionary<Vector2Int, int> masks,
            Vector2Int vertex
        )
        {
            int mask;
            if (masks.TryGetValue(vertex + new Vector2Int(-1, -1), out mask))
            {
                return mask & 1;
            }
            if (masks.TryGetValue(vertex + new Vector2Int(0, -1), out mask))
            {
                return (mask >> 1) & 1;
            }
            if (masks.TryGetValue(vertex + new Vector2Int(-1, 0), out mask))
            {
                return (mask >> 2) & 1;
            }
            if (masks.TryGetValue(vertex, out mask))
            {
                return (mask >> 3) & 1;
            }
            return 0;
        }

        private void SynchronizeEdgeTiles(
            Dictionary<Vector2Int, PixelLabPaintTile> current
        )
        {
            var changed = ChangedCells(current);
            if (changed.Count == 0)
            {
                return;
            }

            synchronizing = true;
            var offsets = EdgeOffsets();
            var affected = new HashSet<Vector2Int>();
            foreach (var cell in changed)
            {
                affected.Add(cell);
                foreach (var offset in offsets)
                {
                    var candidate = cell + offset;
                    if (current.ContainsKey(candidate))
                    {
                        affected.Add(candidate);
                    }
                }
            }

            foreach (var cell in affected)
            {
                PixelLabPaintTile tile;
                if (!current.TryGetValue(cell, out tile))
                {
                    continue;
                }
                var terrain = LogicalEdgeTerrain(tile);
                if (terrain == "empty")
                {
                    continue;
                }
                if (terrain == "filler")
                {
                    var fillerTile = FillerTileIndex();
                    if (fillerTile >= 0)
                    {
                        SetTileIndex(cell, fillerTile);
                    }
                    continue;
                }
                if (terrain == "ground")
                {
                    continue;
                }

                var mask = 0;
                for (var index = 0; index < offsets.Length; index++)
                {
                    PixelLabPaintTile neighbour;
                    var neighbourTerrain = current.TryGetValue(cell + offsets[index], out neighbour)
                        ? LogicalEdgeTerrain(neighbour)
                        : "empty";
                    var connected = rules.connectivity == "same"
                        ? neighbourTerrain == terrain
                        : neighbourTerrain != "feature";
                    if (connected)
                    {
                        mask |= 1 << index;
                    }
                }

                var desiredTile = TileForMask(mask, rules.arity);
                if (desiredTile >= 0)
                {
                    SetTileIndex(cell, desiredTile);
                }
            }
            synchronizing = false;
        }

        private Vector2Int[] EdgeOffsets()
        {
            if (rules.arity == 4)
            {
                return new[] {
                    new Vector2Int(0, -1),
                    new Vector2Int(1, 0),
                    new Vector2Int(0, 1),
                    new Vector2Int(-1, 0),
                };
            }
            if (gridKind == "hex-pointy-top")
            {
                return new[] {
                    new Vector2Int(0, 1),
                    new Vector2Int(-1, 1),
                    new Vector2Int(-1, 0),
                    new Vector2Int(0, -1),
                    new Vector2Int(1, -1),
                    new Vector2Int(1, 0),
                };
            }
            return new[] {
                new Vector2Int(1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(-1, 1),
                new Vector2Int(-1, 0),
                new Vector2Int(0, -1),
                new Vector2Int(1, -1),
            };
        }

        private void SetTileIndex(Vector2Int axial, int tileIndex)
        {
            PixelLabPaintTile replacement;
            if (!tilesByIndex.TryGetValue(tileIndex, out replacement))
            {
                return;
            }
            tilemap.SetTile(
                PixelLabGridCoordinates.ToUnityCell(gridKind, axial.x, axial.y),
                replacement
            );
        }

        private void SetSharedLegacyTile(Vector2Int cell, int sourceLayerIndex, int tileIndex)
        {
            PixelLabPaintTile tile;
            if (!sharedLegacyTiles.TryGetValue(SharedTileKey(sourceLayerIndex, tileIndex), out tile)
                && !tilesByIndex.TryGetValue(tileIndex, out tile))
            {
                return;
            }
            var unityCell = PixelLabGridCoordinates.ToUnityCell(gridKind, cell.x, cell.y);
            tilemap.SetTile(unityCell, tile);
        }

        private sealed class SharedLegacySelection
        {
            public int sourceLayerIndex;
            public int tileIndex;
        }

        private void SynchronizeSharedLegacyTiles(
            Dictionary<Vector2Int, PixelLabPaintTile> cells
        )
        {
            var hasBrushes = cells.Values.Any(tile => tile != null && tile.IsTerrainBrush);
            var changed = ChangedCells(cells);
            if (!hasBrushes && changed.Count == 0)
            {
                return;
            }

            synchronizing = true;
            if (cells.Count == 0)
            {
                sharedLegacyTerrain.Clear();
            }
            else
            {
                foreach (var cell in changed)
                {
                    PixelLabPaintTile tile;
                    if (cells.TryGetValue(cell, out tile) && tile.IsTerrainBrush)
                    {
                        if (tile.paintTerrain == 0)
                        {
                            sharedLegacyTerrain.Remove(cell);
                        }
                        else
                        {
                            sharedLegacyTerrain[cell] = tile.paintTerrain;
                        }
                    }
                    else if (!cells.ContainsKey(cell))
                    {
                        sharedLegacyTerrain.Remove(cell);
                    }
                }
            }
            SaveSharedLegacyTerrain();
            var vertices = new Dictionary<Vector2Int, int>(sharedLegacyTerrain);
            var affected = new HashSet<Vector2Int>();
            foreach (var cell in cells.Keys.Concat(lastCells.Keys))
            {
                affected.Add(cell);
                affected.Add(cell + new Vector2Int(-1, 0));
                affected.Add(cell + new Vector2Int(0, -1));
                affected.Add(cell + new Vector2Int(-1, -1));
            }

            foreach (var cell in affected)
            {
                // A cell that JUST lost its tile is explicitly erased: clear
                // it rather than letting neighbouring vertices resolve a tile
                // straight back into it. Cells that never held a tile stay
                // resolvable, so painting can still grow edge tiles into empty
                // neighbours.
                if (!cells.ContainsKey(cell)
                    && lastCells.ContainsKey(cell))
                {
                    tilemap.SetTile(PixelLabGridCoordinates.ToUnityCell(gridKind, cell.x, cell.y), null);
                    continue;
                }
                var selections = SharedLegacySelections(cell, vertices);
                if (selections.Count > 0)
                {
                    SetSharedLegacyTile(cell, selections[0].sourceLayerIndex, selections[0].tileIndex);
                }
                else
                {
                    tilemap.SetTile(PixelLabGridCoordinates.ToUnityCell(gridKind, cell.x, cell.y), null);
                }
            }
            synchronizing = false;
        }

        private void LoadSharedLegacyTerrain()
        {
            var values = sharedLegacyStateInitialized
                ? sharedLegacyPaintValues
                : manifest.legacyTerrain.paintValues;
            foreach (var item in values ?? Array.Empty<PixelLabPaintValue>())
            {
                if (item != null && item.value != 0)
                {
                    sharedLegacyTerrain[new Vector2Int(item.x, item.y)] =
                        item.value;
                }
            }
            if (!sharedLegacyStateInitialized)
            {
                sharedLegacyStateInitialized = true;
                SaveSharedLegacyTerrain();
            }
        }

        private void SaveSharedLegacyTerrain()
        {
            sharedLegacyPaintValues = sharedLegacyTerrain
                .OrderBy(pair => pair.Key.y)
                .ThenBy(pair => pair.Key.x)
                .Select(pair => new PixelLabPaintValue {
                    x = pair.Key.x,
                    y = pair.Key.y,
                    value = pair.Value,
                })
                .ToArray();
        }

        private List<SharedLegacySelection> SharedLegacySelections(
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            if (!legacyLayers.Any(layer => layer.sidescroller)
                && !SharedCenterIsComplete(cell, vertices))
            {
                return new List<SharedLegacySelection>();
            }
            var centerTerrains = SharedCenterTerrains(cell, vertices);
            if (centerTerrains.Count == 0)
            {
                return new List<SharedLegacySelection>();
            }

            for (var index = 0; index < legacyLayers.Length; index++)
            {
                var layer = legacyLayers[index];
                if (!SharedLayerSupports(layer, centerTerrains))
                {
                    continue;
                }
                var pattern = SharedLocalPattern(layer, cell, vertices);
                var tileIndex = SharedTileForPattern(layer, pattern);
                if (tileIndex >= 0)
                {
                    return new List<SharedLegacySelection> {
                        new SharedLegacySelection {
                            sourceLayerIndex = index,
                            tileIndex = tileIndex,
                        },
                    };
                }
            }

            if (centerTerrains.Count == 1)
            {
                var terrainId = centerTerrains[0];
                for (var index = 0; index < legacyLayers.Length; index++)
                {
                    var layer = legacyLayers[index];
                    var localTerrain = Array.IndexOf(
                        layer.unityTileRules?.terrainIds ?? Array.Empty<int>(),
                        terrainId
                    );
                    if (localTerrain < 0)
                    {
                        continue;
                    }
                    var tileIndex = SharedCanonicalTile(layer, localTerrain);
                    if (tileIndex >= 0)
                    {
                        return new List<SharedLegacySelection> {
                            new SharedLegacySelection {
                                sourceLayerIndex = index,
                                tileIndex = tileIndex,
                            },
                        };
                    }
                }
            }

            var output = new List<SharedLegacySelection>();
            centerTerrains.Sort();
            for (var terrainIndex = 0; terrainIndex + 1 < centerTerrains.Count;
                terrainIndex++)
            {
                for (var layerIndex = 0; layerIndex < legacyLayers.Length; layerIndex++)
                {
                    var layer = legacyLayers[layerIndex];
                    if (layer.sidescroller
                        || (layer.unityTileRules?.terrainIds?.Length ?? 0) != 2
                        || !SharedLayerContainsPair(
                        layer,
                        centerTerrains[terrainIndex],
                        centerTerrains[terrainIndex + 1]
                    ))
                    {
                        continue;
                    }
                    var tileIndex = SharedTileForPattern(
                        layer,
                        SharedPairFallbackPattern(layer, cell, vertices)
                    );
                    if (tileIndex >= 0)
                    {
                        output.Add(new SharedLegacySelection {
                            sourceLayerIndex = layerIndex,
                            tileIndex = tileIndex,
                        });
                    }
                    break;
                }
            }
            return output;
        }

        private static bool SharedCenterIsComplete(
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            foreach (var offset in new[] {
                Vector2Int.zero,
                new Vector2Int(1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(1, 1),
            })
            {
                int terrain;
                if (!vertices.TryGetValue(cell + offset, out terrain) || terrain == 0)
                {
                    return false;
                }
            }
            return true;
        }

        private int[] SharedPairFallbackPattern(
            PixelLabLegacyLayer layer,
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            var rules = layer.unityTileRules;
            var output = Enumerable.Repeat(rules.wildcard, 16).ToArray();
            var positions = new[] {
                cell,
                cell + new Vector2Int(1, 0),
                cell + new Vector2Int(0, 1),
                cell + new Vector2Int(1, 1),
            };
            var patternIndices = new[] { 5, 6, 9, 10 };
            for (var index = 0; index < positions.Length; index++)
            {
                int terrain;
                vertices.TryGetValue(positions[index], out terrain);
                output[patternIndices[index]] = terrain == layer.upperTerrainId ? 1 : 0;
            }
            return output;
        }

        private List<int> SharedCenterTerrains(
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            var output = new HashSet<int>();
            foreach (var offset in new[] {
                Vector2Int.zero,
                new Vector2Int(1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(1, 1),
            })
            {
                int value;
                if (vertices.TryGetValue(cell + offset, out value) && value != 0)
                {
                    output.Add(value);
                }
            }
            return output.ToList();
        }

        private bool SharedLayerSupports(
            PixelLabLegacyLayer layer,
            List<int> centerTerrains
        )
        {
            var terrainIds = layer.unityTileRules?.terrainIds ?? Array.Empty<int>();
            if (layer.sidescroller)
            {
                var family = SharedTerrainFamily(layer.lowerTerrainId);
                return centerTerrains.All(terrain => SharedTerrainFamily(terrain) == family);
            }
            return centerTerrains.All(terrain => terrainIds.Contains(terrain));
        }

        private bool SharedLayerContainsPair(
            PixelLabLegacyLayer layer,
            int first,
            int second
        )
        {
            var terrainIds = layer.unityTileRules?.terrainIds ?? Array.Empty<int>();
            return terrainIds.Contains(first) && terrainIds.Contains(second);
        }

        private int[] SharedLocalPattern(
            PixelLabLegacyLayer layer,
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            var rules = layer.unityTileRules;
            var output = new int[16];
            for (var row = 0; row < 4; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    var position = cell + new Vector2Int(column - 1, row - 1);
                    int terrain;
                    if (!vertices.TryGetValue(position, out terrain))
                    {
                        var center = row >= 1 && row <= 2
                            && column >= 1 && column <= 2;
                        output[row * 4 + column] = layer.sidescroller || center
                            ? SharedLocalTerrain(layer, 0)
                            : rules.wildcard;
                        continue;
                    }
                    terrain = SharedTransitionTerrain(layer, position, terrain, vertices);
                    output[row * 4 + column] = SharedLocalTerrain(layer, terrain);
                }
            }
            return output;
        }

        private int SharedTransitionTerrain(
            PixelLabLegacyLayer layer,
            Vector2Int position,
            int terrain,
            Dictionary<Vector2Int, int> vertices
        )
        {
            var terrainIds = layer.unityTileRules?.terrainIds ?? Array.Empty<int>();
            if (terrainIds.Length < 3 || terrain == terrainIds[2])
            {
                return terrain;
            }
            int above;
            int below;
            if (!vertices.TryGetValue(position + new Vector2Int(0, -1), out above)
                || !vertices.TryGetValue(position + new Vector2Int(0, 1), out below))
            {
                return terrain;
            }
            var upper = terrainIds[1];
            var lower = terrainIds[0];
            if (terrain == lower && above == upper && below == lower)
            {
                return terrainIds[2];
            }
            return terrain;
        }

        private int SharedLocalTerrain(PixelLabLegacyLayer layer, int terrain)
        {
            var terrainIds = layer.unityTileRules?.terrainIds ?? Array.Empty<int>();
            var local = Array.IndexOf(terrainIds, terrain);
            if (local >= 0)
            {
                return local;
            }
            if (layer.sidescroller
                && terrain != 0
                && SharedTerrainFamily(terrain) == SharedTerrainFamily(layer.lowerTerrainId))
            {
                return 0;
            }
            return int.MinValue;
        }

        private string SharedTerrainFamily(int terrainId)
        {
            foreach (var terrain in manifest.legacyTerrain.terrains
                ?? Array.Empty<PixelLabTerrainDefinition>())
            {
                if (terrain.id == terrainId)
                {
                    return string.IsNullOrEmpty(terrain.baseTileId)
                        ? terrain.id.ToString()
                        : terrain.baseTileId;
                }
            }
            return terrainId.ToString();
        }

        private int SharedTileForPattern(PixelLabLegacyLayer layer, int[] request)
        {
            var rules = layer.unityTileRules;
            var bestTile = -1;
            var bestWildcards = int.MaxValue;
            foreach (var entry in rules?.entries ?? Array.Empty<PixelLabRuleEntry>())
            {
                if (entry.pattern == null || entry.pattern.Length != 16
                    || !SharedPatternsMatch(entry.pattern, request, rules.wildcard))
                {
                    continue;
                }
                var wildcards = entry.pattern.Count(value => value == rules.wildcard);
                if (wildcards < bestWildcards)
                {
                    bestTile = entry.tileIndex;
                    bestWildcards = wildcards;
                }
            }
            if (bestTile >= 0 || (rules?.terrainIds?.Length ?? 0) <= 2)
            {
                return bestTile;
            }

            var bestScore = int.MinValue;
            bestWildcards = int.MaxValue;
            foreach (var entry in rules.entries ?? Array.Empty<PixelLabRuleEntry>())
            {
                if (entry.pattern == null || entry.pattern.Length != 16)
                {
                    continue;
                }
                if (!SharedPatternCenterMatches(entry.pattern, request))
                {
                    continue;
                }
                var score = SharedPatternScore(entry.pattern, request, rules.wildcard);
                var wildcards = entry.pattern.Count(value => value == rules.wildcard);
                if (score > bestScore || score == bestScore && wildcards < bestWildcards)
                {
                    bestTile = entry.tileIndex;
                    bestScore = score;
                    bestWildcards = wildcards;
                }
            }
            return bestTile;
        }

        private static bool SharedPatternCenterMatches(int[] candidate, int[] request)
        {
            foreach (var index in new[] { 5, 6, 9, 10 })
            {
                if (candidate[index] != request[index])
                {
                    return false;
                }
            }
            return true;
        }

        private static int SharedPatternScore(
            int[] candidate,
            int[] request,
            int wildcard
        )
        {
            var score = 0;
            for (var index = 0; index < 16; index++)
            {
                if (candidate[index] == wildcard || request[index] == wildcard)
                {
                    continue;
                }
                var row = index / 4;
                var column = index % 4;
                var center = row >= 1 && row <= 2 && column >= 1 && column <= 2;
                if (request[index] != int.MinValue && candidate[index] == request[index])
                {
                    score += center ? PatternCenterMatchScore : PatternRingMatchScore;
                }
                else
                {
                    score += center ? PatternCenterMismatchScore : PatternRingMismatchScore;
                }
            }
            return score;
        }

        private static bool SharedPatternsMatch(
            int[] candidate,
            int[] request,
            int wildcard
        )
        {
            for (var index = 0; index < 16; index++)
            {
                if (request[index] == int.MinValue)
                {
                    if (candidate[index] != wildcard)
                    {
                        return false;
                    }
                    continue;
                }
                if (candidate[index] != wildcard && request[index] != wildcard
                    && candidate[index] != request[index])
                {
                    return false;
                }
            }
            return true;
        }

        private int SharedCanonicalTile(PixelLabLegacyLayer layer, int terrain)
        {
            foreach (var entry in layer.unityTileRules?.entries
                ?? Array.Empty<PixelLabRuleEntry>())
            {
                var pattern = entry.pattern;
                if (pattern != null && pattern.Length == 16
                    && pattern[5] == terrain && pattern[6] == terrain
                    && pattern[9] == terrain && pattern[10] == terrain)
                {
                    return entry.tileIndex;
                }
            }
            return -1;
        }

        private static string SharedTileKey(int sourceLayerIndex, int tileIndex)
        {
            return sourceLayerIndex + "|" + tileIndex;
        }

        private void SynchronizeLogicalCornerTiles(
            Dictionary<Vector2Int, PixelLabPaintTile> cells
        )
        {
            var paintedVertices = cells
                .Where(pair => pair.Value.IsTerrainBrush)
                .Select(pair => pair.Key)
                .ToArray();
            if (paintedVertices.Length == 0)
            {
                return;
            }

            synchronizing = true;
            var vertices = new Dictionary<Vector2Int, int>();
            foreach (var pair in cells)
            {
                if (pair.Value.IsTerrainBrush)
                {
                    vertices[pair.Key] = pair.Value.paintTerrain;
                }
                else
                {
                    WriteCornerTerrainVertices(
                        vertices,
                        pair.Key,
                        MaskForTile(pair.Value.tileIndex)
                    );
                }
            }

            var affected = new HashSet<Vector2Int>();
            foreach (var vertex in paintedVertices)
            {
                affected.Add(vertex);
                affected.Add(vertex + new Vector2Int(-1, 0));
                affected.Add(vertex + new Vector2Int(0, -1));
                affected.Add(vertex + new Vector2Int(-1, -1));
            }

            foreach (var cell in affected)
            {
                if (!CornerTerrainIsComplete(cell, vertices))
                {
                    continue;
                }
                var mask = CornerTerrainBit(vertices[cell]) << 3
                    | CornerTerrainBit(vertices[cell + new Vector2Int(1, 0)]) << 2
                    | CornerTerrainBit(vertices[cell + new Vector2Int(0, 1)]) << 1
                    | CornerTerrainBit(vertices[cell + new Vector2Int(1, 1)]);
                var tileIndex = TileForMask(mask, 4);
                if (tileIndex >= 0)
                {
                    SetTileIndex(cell, tileIndex);
                }
            }
            synchronizing = false;
        }

        private void SynchronizeLogicalEdgeTiles(
            Dictionary<Vector2Int, PixelLabPaintTile> cells
        )
        {
            var paintedCells = cells
                .Where(pair => pair.Value.IsTerrainBrush)
                .Select(pair => pair.Key)
                .ToArray();
            if (paintedCells.Length == 0)
            {
                return;
            }

            synchronizing = true;
            var offsets = EdgeOffsets();
            foreach (var cell in paintedCells)
            {
                var mask = 0;
                for (var index = 0; index < offsets.Length; index++)
                {
                    PixelLabPaintTile neighbour;
                    var terrain = cells.TryGetValue(cell + offsets[index], out neighbour)
                        ? LogicalEdgeTerrain(neighbour)
                        : "empty";
                    var connected = rules.connectivity == "same"
                        ? terrain == "feature" || terrain == "filler"
                        : terrain != "feature";
                    if (connected)
                    {
                        mask |= 1 << index;
                    }
                }
                var tileIndex = TileForMask(mask, rules.arity);
                if (tileIndex >= 0)
                {
                    SetTileIndex(cell, tileIndex);
                }
            }
            synchronizing = false;
        }

        private string LogicalEdgeTerrain(PixelLabPaintTile tile)
        {
            if (!tile.IsTerrainBrush)
            {
                return TerrainForTile(tile.tileIndex);
            }
            return tile.paintTerrain == 0 ? "feature" : "filler";
        }

        private static void WriteCornerTerrainVertices(
            Dictionary<Vector2Int, int> vertices,
            Vector2Int cell,
            int mask
        )
        {
            if (mask < 0)
            {
                return;
            }
            vertices[cell] = CornerTerrainFromBit((mask >> 3) & 1);
            vertices[cell + new Vector2Int(1, 0)] = CornerTerrainFromBit(
                (mask >> 2) & 1
            );
            vertices[cell + new Vector2Int(0, 1)] = CornerTerrainFromBit(
                (mask >> 1) & 1
            );
            vertices[cell + new Vector2Int(1, 1)] = CornerTerrainFromBit(mask & 1);
        }

        private static bool CornerTerrainIsComplete(
            Vector2Int cell,
            Dictionary<Vector2Int, int> vertices
        )
        {
            return vertices.ContainsKey(cell)
                && vertices.ContainsKey(cell + new Vector2Int(1, 0))
                && vertices.ContainsKey(cell + new Vector2Int(0, 1))
                && vertices.ContainsKey(cell + new Vector2Int(1, 1));
        }

        private int CornerTerrainBit(int terrain)
        {
            if (rules != null && rules.terrainIds != null && rules.terrainIds.Length > 0)
            {
                var index = Array.IndexOf(rules.terrainIds, terrain);
                if (index >= 0)
                {
                    return index == 0 ? 1 : 0;
                }
            }
            // Corner groups publish terrains upper-first, matching the map
            // editor palette: terrains[0] contributes mask bit 1 and
            // terrains[1] contributes mask bit 0.
            return terrain == 0 ? 1 : 0;
        }

        private static int CornerTerrainFromBit(int bit)
        {
            return bit == 1 ? 0 : 1;
        }

        private bool UsesPatternRules()
        {
            return rules != null && rules.ruleType == "pattern_4x4";
        }

        private bool UsesCornerRules()
        {
            return rules != null && rules.ruleType == "corner" && rules.arity == 4;
        }

        private bool UsesEdgeRules()
        {
            return rules != null && rules.ruleType == "edge"
                && (rules.arity == 4 || rules.arity == 6);
        }

        private bool IsSidescroller()
        {
            return legacyLayer != null && legacyLayer.sidescroller;
        }

        private PixelLabRuleEntry[] RuleEntries()
        {
            return rules == null
                ? Array.Empty<PixelLabRuleEntry>()
                : rules.entries ?? Array.Empty<PixelLabRuleEntry>();
        }

        private PixelLabRuleEntry RuleEntry(int tileIndex)
        {
            foreach (var entry in RuleEntries())
            {
                if (entry.tileIndex == tileIndex)
                {
                    return entry;
                }
            }
            return null;
        }

        private int MaskForTile(int tileIndex)
        {
            var entry = RuleEntry(tileIndex);
            return entry != null && entry.hasMask ? entry.mask : -1;
        }

        private int TileForMask(int mask, int arity)
        {
            var bestTile = -1;
            var bestScore = -1;
            var bestMask = int.MaxValue;
            foreach (var entry in RuleEntries())
            {
                var candidateMasks = entry.masks != null && entry.masks.Length > 0
                    ? entry.masks
                    : entry.hasMask ? new[] { entry.mask } : Array.Empty<int>();
                foreach (var candidate in candidateMasks)
                {
                    if (candidate == mask)
                    {
                        return entry.tileIndex;
                    }
                    var score = arity - PopCount(candidate ^ mask);
                    // Keep the Unity importer in lock-step with the map
                    // editor's resolveTileForMask fallback.  Hex coastline
                    // sets intentionally contain only contiguous arcs, so a
                    // newly painted one-column extension can produce a
                    // non-contiguous mask (for example 45).  Two candidates
                    // can agree equally well; choosing the first JSON entry
                    // made that boundary depend on export ordering and picked
                    // the deep-wall variant (mask 61 instead of 47), creating
                    // the one-sided elevation strip seen when drawing left of
                    // a hex map.  The editor's deterministic tie-break is the
                    // lowest candidate mask, with tile index as a final tie.
                    if (score > bestScore
                        || score == bestScore && candidate < bestMask
                        || score == bestScore && candidate == bestMask
                            && (bestTile < 0 || entry.tileIndex < bestTile))
                    {
                        bestScore = score;
                        bestMask = candidate;
                        bestTile = entry.tileIndex;
                    }
                }
            }
            return bestTile;
        }

        private string TerrainForTile(int tileIndex)
        {
            if (tileIndex < 0)
            {
                return "empty";
            }
            var entry = RuleEntry(tileIndex);
            if (entry == null)
            {
                return tileIndex == FillerTileIndex() ? "filler" : "empty";
            }
            if (rules.connectivity == "same" && entry.hasMask && entry.mask == 0)
            {
                return "ground";
            }
            return "feature";
        }

        private int FillerTileIndex()
        {
            if (rules == null || rules.connectivity != "other")
            {
                return -1;
            }
            var indices = new List<int>(tilesByIndex.Keys);
            indices.Sort();
            foreach (var tileIndex in indices)
            {
                if (RuleEntry(tileIndex) == null)
                {
                    return tileIndex;
                }
            }
            return -1;
        }

        private static int PopCount(int value)
        {
            var count = 0;
            var remaining = value;
            while (remaining != 0)
            {
                count += remaining & 1;
                remaining >>= 1;
            }
            return count;
        }

        private static string CellKey(int q, int r, int y)
        {
            return q + "," + r + "," + y;
        }
    }
}
