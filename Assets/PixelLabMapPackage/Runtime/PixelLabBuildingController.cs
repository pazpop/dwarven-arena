using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Grid))]
    public sealed class PixelLabBuildingController : MonoBehaviour
    {
        private const int SideN = 1;
        private const int SideE = 2;
        private const int SideS = 4;
        private const int SideW = 8;
        private const int CornerNE = 16;
        private const int CornerSE = 32;
        private const int CornerSW = 64;
        private const int CornerNW = 128;

        private const int PartHub = 1;
        private const int PartN = 2;
        private const int PartE = 4;
        private const int PartS = 8;
        private const int PartW = 16;
        private const int PartWallN = 32;
        private const int PartWallE = 64;
        private const int PartWallS = 128;
        private const int PartWallW = 256;

        [SerializeField] private PixelLabBuildingKit kit;
        [SerializeField] private PixelLabBuildingEditState editState;
        [SerializeField] private float pixelsPerUnit = 1f;
        [SerializeField] private Grid grid;
        [SerializeField] private List<PixelLabBuildingNativeTile> nativeTiles =
            new List<PixelLabBuildingNativeTile>();
        [SerializeField] private List<PixelLabBuildingLaneTilemap> laneTilemaps =
            new List<PixelLabBuildingLaneTilemap>();
        [SerializeField] private List<PixelLabBuildingDerivedVisual> derivedVisuals =
            new List<PixelLabBuildingDerivedVisual>();

        [NonSerialized] private int regenerationDepth;
        [NonSerialized] private int semanticBatchDepth;
        private readonly HashSet<PixelLabBuildingCellAddress> batchedChanges =
            new HashSet<PixelLabBuildingCellAddress>();
        private readonly Dictionary<int, PixelLabBuildingNativeTile> tilesByIndex =
            new Dictionary<int, PixelLabBuildingNativeTile>();
        private readonly Dictionary<int, PixelLabBuildingVariant> structureByMask =
            new Dictionary<int, PixelLabBuildingVariant>();
        private readonly Dictionary<int, PixelLabBuildingVariant> partitionByMask =
            new Dictionary<int, PixelLabBuildingVariant>();
        private readonly HashSet<PixelLabBuildingCellAddress> groundSupport =
            new HashSet<PixelLabBuildingCellAddress>();

        public PixelLabBuildingKit Kit => kit;
        public PixelLabBuildingEditState EditState => editState;
        public float PixelsPerUnit => pixelsPerUnit;
        public bool IsRegenerating => regenerationDepth > 0;
        public bool Edited => editState != null && editState.Edited;
        public IReadOnlyList<PixelLabBuildingNativeTile> NativeTiles => nativeTiles;
        public IReadOnlyList<PixelLabBuildingLaneTilemap> LaneTilemaps => laneTilemaps;
        public IReadOnlyList<PixelLabBuildingDerivedVisual> DerivedVisuals => derivedVisuals;

        public PixelLabBuildingNativeTile NativeTile(int tileIndex)
        {
            RequireConfigured();
            return RequireTile(tileIndex);
        }

        public PixelLabBuildingLogicalCellState SemanticCell(
            string role,
            int layerY,
            int q,
            int r
        )
        {
            RequireConfigured();
            return editState.FindCell(role, layerY, q, r);
        }

        public bool HasGroundAt(int layerY, int q, int r)
        {
            RequireConfigured();
            return editState.FindCell(PixelLabBuildingRoles.Floor, layerY, q, r) != null ||
                groundSupport.Contains(
                    new PixelLabBuildingCellAddress(q, r, layerY)
                );
        }

        public void Configure(
            PixelLabBuildingKit sourceKit,
            PixelLabBuildingEditState state,
            float sourcePixelsPerUnit,
            IEnumerable<PixelLabBuildingNativeTile> tiles
        )
        {
            kit = sourceKit ?? throw new ArgumentNullException(nameof(sourceKit));
            editState = state ?? throw new ArgumentNullException(nameof(state));
            pixelsPerUnit = sourcePixelsPerUnit;
            if (pixelsPerUnit <= 0f)
            {
                throw new InvalidOperationException("Buildings pixelsPerUnit must be positive.");
            }
            if (editState.KitId != kit.id)
            {
                throw new InvalidOperationException(
                    "Buildings edit state belongs to kit \"" + editState.KitId +
                    "\", not \"" + kit.id + "\"."
                );
            }

            grid = GetComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Rectangle;
            grid.cellSwizzle = GridLayout.CellSwizzle.XYZ;
            grid.cellSize = Vector3.one;
            nativeTiles.Clear();
            nativeTiles.AddRange(tiles ?? Array.Empty<PixelLabBuildingNativeTile>());
            RefreshCachesAndValidate();

            foreach (var storey in editState.Storeys)
            {
                EnsureStoreyLanes(storey.layerY);
            }
            RegenerateAll();
        }

        public void RefreshLaneCache()
        {
            laneTilemaps.Clear();
            GetComponentsInChildren(true, laneTilemaps);
            derivedVisuals.Clear();
            GetComponentsInChildren(true, derivedVisuals);
        }

        public GameObject[] EnsureStoreyLanes(int layerY)
        {
            RequireConfigured();
            var created = new List<GameObject>();
            var storeyRoot = FindOrCreateChild(
                transform,
                "Storey Y" + layerY,
                created
            );
            storeyRoot.gameObject.hideFlags =
                HideFlags.HideInHierarchy | HideFlags.NotEditable;
            foreach (var definition in kit.visualLanes ??
                Array.Empty<PixelLabBuildingVisualLane>())
            {
                var roleRoot = FindOrCreateChild(
                    storeyRoot,
                    RoleLabel(definition.role),
                    created
                );
                roleRoot.gameObject.hideFlags =
                    HideFlags.HideInHierarchy | HideFlags.NotEditable;
                if (FindLane(layerY, definition.id, false) != null)
                {
                    continue;
                }
                var laneObject = new GameObject(definition.name);
                laneObject.transform.SetParent(roleRoot, false);
                laneObject.hideFlags =
                    HideFlags.HideInHierarchy | HideFlags.NotEditable;
                var map = laneObject.AddComponent<Tilemap>();
                map.orientation = Tilemap.Orientation.XY;
                laneObject.AddComponent<TilemapRenderer>();
                var lane = laneObject.AddComponent<PixelLabBuildingLaneTilemap>();
                lane.Configure(this, definition, layerY);
                laneTilemaps.Add(lane);
                created.Add(laneObject);
            }
            return created.ToArray();
        }

        public bool PaintFloor(
            int layerY,
            int q,
            int r,
            bool roof,
            bool autoWalls
        )
        {
            RequireConfigured();
            var tileIndex = roof
                ? kit.semanticAssets.roofTileIndex
                : kit.semanticAssets.floorTileIndex;
            if (!autoWalls)
            {
                tileIndex += kit.semanticAssets.noWallFloorOffset;
            }
            return PaintCell(PixelLabBuildingRoles.Floor, layerY, q, r, tileIndex);
        }

        public bool SetAutoWalls(int layerY, int q, int r, bool enabled)
        {
            RequireConfigured();
            var cell = editState.FindCell(PixelLabBuildingRoles.Floor, layerY, q, r);
            if (cell == null)
            {
                return false;
            }
            var offset = kit.semanticAssets.noWallFloorOffset;
            var tileIndex = cell.tileIndex;
            if (enabled && tileIndex >= offset)
            {
                tileIndex -= offset;
            }
            else if (!enabled && tileIndex < offset)
            {
                tileIndex += offset;
            }
            editState.SetSemanticCell(
                PixelLabBuildingRoles.Floor,
                layerY,
                q,
                r,
                tileIndex
            );
            RegenerateFootprint(new[] { new PixelLabBuildingCellAddress(q, r, layerY) });
            return true;
        }

        public bool PaintPartition(int layerY, int q, int r)
        {
            RequireConfigured();
            return PaintCell(
                PixelLabBuildingRoles.Partition,
                layerY,
                q,
                r,
                kit.semanticAssets.partitionMarkerTileIndex
            );
        }

        public bool PaintPillar(int layerY, int q, int r)
        {
            RequireConfigured();
            return PaintStamp(layerY, q, r, kit.semanticAssets.pillarTileIndex);
        }

        public bool PaintPairedPiece(
            int layerY,
            int q,
            int r,
            string kind,
            string orientation = null,
            string side = null,
            string axis = null
        )
        {
            RequireConfigured();
            var pair = RequirePair(kind, orientation, side, axis);
            return PaintStamp(layerY, q, r, pair.firstTileIndex);
        }

        public bool PaintStamp(int layerY, int q, int r, int tileIndex)
        {
            RequireConfigured();
            PixelLabBuildingPair pair;
            bool selectedFirst;
            if (!TryPair(tileIndex, out pair, out selectedFirst))
            {
                if (!HasGroundAt(layerY, q, r))
                {
                    return false;
                }
                RequireTile(tileIndex);
                editState.SetSemanticCell(
                    PixelLabBuildingRoles.Stamp,
                    layerY,
                    q,
                    r,
                    tileIndex
                );
                EnsureStoreyLanes(layerY);
                RegenerateFootprint(new[]
                {
                    new PixelLabBuildingCellAddress(q, r, layerY),
                });
                return true;
            }

            var firstQ = selectedFirst ? q : q - pair.offset.q;
            var firstR = selectedFirst ? r : r - pair.offset.r;
            var secondQ = firstQ + pair.offset.q;
            var secondR = firstR + pair.offset.r;
            if (!HasGroundAt(layerY, firstQ, firstR) ||
                !HasGroundAt(layerY, secondQ, secondR))
            {
                return false;
            }
            RequireTile(pair.firstTileIndex);
            RequireTile(pair.secondTileIndex);
            editState.SetSemanticCell(
                PixelLabBuildingRoles.Stamp,
                layerY,
                firstQ,
                firstR,
                pair.firstTileIndex
            );
            editState.SetSemanticCell(
                PixelLabBuildingRoles.Stamp,
                layerY,
                secondQ,
                secondR,
                pair.secondTileIndex
            );
            RegenerateFootprint(new[]
            {
                new PixelLabBuildingCellAddress(firstQ, firstR, layerY),
                new PixelLabBuildingCellAddress(secondQ, secondR, layerY),
            });
            return true;
        }

        public bool PaintCell(
            string role,
            int layerY,
            int q,
            int r,
            int tileIndex
        )
        {
            RequireConfigured();
            PixelLabBuildingRoles.Require(role);
            if (role == PixelLabBuildingRoles.Structure)
            {
                throw new InvalidOperationException(
                    "Structure cells are derived from floor outlines. Use floor auto-walls."
                );
            }
            if (role == PixelLabBuildingRoles.Stamp)
            {
                return PaintStamp(layerY, q, r, tileIndex);
            }
            if (role == PixelLabBuildingRoles.Partition &&
                !HasGroundAt(layerY, q, r))
            {
                return false;
            }
            if (role == PixelLabBuildingRoles.Floor)
            {
                var sourceIndex = tileIndex >= kit.semanticAssets.noWallFloorOffset
                    ? tileIndex - kit.semanticAssets.noWallFloorOffset
                    : tileIndex;
                RequireTile(sourceIndex);
            }
            editState.SetSemanticCell(role, layerY, q, r, tileIndex);
            EnsureStoreyLanes(layerY);
            RegenerateFootprint(new[] { new PixelLabBuildingCellAddress(q, r, layerY) });
            return true;
        }

        public bool EraseCell(string role, int layerY, int q, int r)
        {
            RequireConfigured();
            PixelLabBuildingRoles.Require(role);
            if (role == PixelLabBuildingRoles.Stamp)
            {
                return EraseStampPair(layerY, q, r);
            }

            var changed = new List<PixelLabBuildingCellAddress>
            {
                new PixelLabBuildingCellAddress(q, r, layerY),
            };
            var removed = editState.RemoveSemanticCell(role, layerY, q, r);
            if (role == PixelLabBuildingRoles.Floor)
            {
                editState.RemoveResolvedCell(
                    PixelLabBuildingRoles.Structure,
                    layerY,
                    q,
                    r
                );
                if (!HasGroundAt(layerY, q, r))
                {
                    removed |= editState.RemoveSemanticCell(
                        PixelLabBuildingRoles.Partition,
                        layerY,
                        q,
                        r
                    );
                    if (editState.HasCell(PixelLabBuildingRoles.Stamp, layerY, q, r))
                    {
                        removed |= EraseStampPairStateOnly(layerY, q, r, changed);
                    }
                }
            }
            if (!removed)
            {
                return false;
            }
            RegenerateFootprint(changed);
            return true;
        }

        public bool AddStorey(int layerY)
        {
            RequireConfigured();
            if (editState.FindStorey(layerY) != null)
            {
                return false;
            }
            editState.EnsureStorey(layerY);
            EnsureStoreyLanes(layerY);
            return true;
        }

        public Vector2Int WorldToLogical(Vector3 worldPosition, int layerY)
        {
            RequireConfigured();
            var projection = kit.projection;
            var local = transform.InverseTransformPoint(worldPosition);
            var x = local.x * pixelsPerUnit - projection.editorOrigin.x -
                layerY * projection.stackBasis.x;
            var y = -local.y * pixelsPerUnit - projection.editorOrigin.y -
                layerY * projection.stackBasis.y;
            var determinant = projection.qBasis.x * projection.rBasis.y -
                projection.qBasis.y * projection.rBasis.x;
            if (Mathf.Abs(determinant) < 0.0001f)
            {
                throw new InvalidOperationException(
                    "Buildings projection basis is singular for kit \"" + kit.id + "\"."
                );
            }
            var q = (x * projection.rBasis.y - y * projection.rBasis.x) /
                determinant;
            var r = (projection.qBasis.x * y - projection.qBasis.y * x) /
                determinant;
            return new Vector2Int(Mathf.RoundToInt(q), Mathf.RoundToInt(r));
        }

        public Vector3 LogicalToWorld(int q, int r, int layerY)
        {
            RequireConfigured();
            var projection = kit.projection;
            var x = projection.editorOrigin.x + q * projection.qBasis.x +
                r * projection.rBasis.x + layerY * projection.stackBasis.x;
            var y = projection.editorOrigin.y + q * projection.qBasis.y +
                r * projection.rBasis.y + layerY * projection.stackBasis.y;
            return transform.TransformPoint(new Vector3(
                x / pixelsPerUnit,
                -y / pixelsPerUnit,
                0f
            ));
        }

        public IDisposable BeginSemanticBatch()
        {
            RequireConfigured();
            semanticBatchDepth++;
            return new SemanticBatchScope(this);
        }

        public void RegenerateAll()
        {
            RequireConfigured();
            RefreshLaneCache();
            using (BeginRegeneration())
            {
                foreach (var lane in laneTilemaps)
                {
                    if (lane != null && lane.Tilemap != null)
                    {
                        lane.Tilemap.ClearAllTiles();
                    }
                }
                foreach (var storey in editState.Storeys)
                {
                    EnsureStoreyLanes(storey.layerY);
                    ResolveStorey(storey);
                    foreach (var cell in storey.cells)
                    {
                        RenderOwner(cell.role, storey.layerY, cell.q, cell.r);
                    }
                }
                RebuildDerivedVisuals();
            }
        }

        public void RegenerateFootprint(IEnumerable<PixelLabBuildingCellAddress> changed)
        {
            RequireConfigured();
            if (semanticBatchDepth > 0)
            {
                foreach (var cell in changed)
                {
                    batchedChanges.Add(cell);
                }
                return;
            }
            RegenerateFootprintNow(changed);
        }

        private void RegenerateFootprintNow(
            IEnumerable<PixelLabBuildingCellAddress> changed
        )
        {
            if (kit.dependencyHalo != 1)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" declares dependencyHalo=" +
                    kit.dependencyHalo + "; Unity native Buildings requires exactly one cell."
                );
            }
            var footprint = new HashSet<PixelLabBuildingCellAddress>();
            foreach (var cell in changed)
            {
                for (var dr = -1; dr <= 1; dr++)
                {
                    for (var dq = -1; dq <= 1; dq++)
                    {
                        footprint.Add(new PixelLabBuildingCellAddress(
                            cell.q + dq,
                            cell.r + dr,
                            cell.layerY
                        ));
                    }
                }
            }

            using (BeginRegeneration())
            {
                foreach (var cell in footprint)
                {
                    ResolveCell(cell.layerY, cell.q, cell.r);
                }
                foreach (var cell in footprint)
                {
                    RenderOwner(PixelLabBuildingRoles.Floor, cell.layerY, cell.q, cell.r);
                    RenderOwner(
                        PixelLabBuildingRoles.Structure,
                        cell.layerY,
                        cell.q,
                        cell.r
                    );
                    RenderOwner(
                        PixelLabBuildingRoles.Partition,
                        cell.layerY,
                        cell.q,
                        cell.r
                    );
                    RenderOwner(PixelLabBuildingRoles.Stamp, cell.layerY, cell.q, cell.r);
                }
            }
            RebuildDerivedVisuals();
        }

        public void RefreshDerivedFootprint(
            IEnumerable<PixelLabBuildingCellAddress> changed
        )
        {
            RebuildDerivedVisuals();
        }

        private void RebuildDerivedVisuals()
        {
            ClearDerivedVisuals();
            // The editor composites every building sprite in ONE painter pass
            // ordered by base-line screen position, with floors (all storeys)
            // in an epoch below every wall and lane depth only breaking ties
            // (building_engine_export._placement publishes the same key as
            // visualCells.drawOrder). Lane tilemaps cannot interleave per
            // sprite, so they stay as edit state and each piece renders as a
            // SpriteRenderer whose sortingOrder is the rank of that key.
            var pending = new List<PixelLabBuildingDerivedVisual>();
            foreach (var lane in laneTilemaps)
            {
                if (lane == null || lane.Tilemap == null)
                {
                    continue;
                }
                var laneRenderer = lane.GetComponent<TilemapRenderer>();
                if (laneRenderer != null)
                {
                    laneRenderer.enabled = false;
                }
                var bounds = lane.Tilemap.cellBounds;
                foreach (var position in bounds.allPositionsWithin)
                {
                    var tile = lane.Tilemap.GetTile<PixelLabBuildingNativeTile>(
                        position
                    );
                    if (tile == null)
                    {
                        continue;
                    }
                    var axial = PixelLabGridCoordinates.ToAxial(
                        kit.gridKind,
                        position
                    );
                    var visualObject = new GameObject(
                        "Piece " + lane.LaneId + " " + axial.x + "," + axial.y
                    );
                    visualObject.hideFlags = HideFlags.HideInHierarchy
                        | HideFlags.NotEditable;
#if UNITY_EDITOR
                    // An unregistered child dangles on Ctrl-Z and Unity
                    // destroys it, taking the whole building invisible. In the
                    // undo system, a paint stroke's visual churn undoes as one
                    // unit with the lane tilemap edits.
                    if (!Application.isPlaying)
                    {
                        UnityEditor.Undo.RegisterCreatedObjectUndo(
                            visualObject,
                            "PixelLab Buildings Visuals"
                        );
                    }
#endif
                    visualObject.AddComponent<SpriteRenderer>();
                    var visual = visualObject
                        .AddComponent<PixelLabBuildingDerivedVisual>();
                    var pivot = ProjectedPartPivot(
                        axial.x,
                        axial.y,
                        lane.LayerY,
                        tile.sourceHeight
                    );
                    visual.Configure(
                        this,
                        lane,
                        axial.x,
                        axial.y,
                        tile,
                        transform.TransformPoint(pivot),
                        DerivedDrawOrder(lane, axial.x, axial.y)
                    );
                    pending.Add(visual);
                }
            }
            pending.Sort((first, second) =>
            {
                var byOrder = first.DrawOrder.CompareTo(second.DrawOrder);
                if (byOrder != 0)
                {
                    return byOrder;
                }
                // JavaScript's stable painter sort preserves the Map Editor's
                // numeric q/r cell insertion order when two projected cells
                // share the same baseline. Sorting by GameObject.name here
                // was lexicographic (1, 10, 11, 2...), so overlapping
                // Buildings frames painted in a different order in Unity and
                // visibly changed the authored floor/wall footprint.
                var byQ = first.Q.CompareTo(second.Q);
                if (byQ != 0)
                {
                    return byQ;
                }
                var byR = first.R.CompareTo(second.R);
                if (byR != 0)
                {
                    return byR;
                }
                var byLane = string.CompareOrdinal(first.LaneId, second.LaneId);
                return byLane != 0
                    ? byLane
                    : first.TileIndex.CompareTo(second.TileIndex);
            });
            for (var index = 0; index < pending.Count; index++)
            {
                pending[index].Renderer.sortingOrder = index;
                derivedVisuals.Add(pending[index]);
            }
        }

        private double DerivedDrawOrder(
            PixelLabBuildingLaneTilemap lane,
            int q,
            int r
        )
        {
            var projection = kit.projection;
            var x = projection.spriteOrigin.x + q * projection.qBasis.x +
                r * projection.rBasis.x + lane.LayerY * projection.stackBasis.x;
            var baseY = projection.spriteOrigin.y + q * projection.qBasis.y +
                r * projection.rBasis.y + lane.LayerY * projection.stackBasis.y;
            var epoch = lane.Role == PixelLabBuildingRoles.Floor ? -1e15 : 0.0;
            return epoch + lane.LayerY * 1e12 + baseY * 1e6 + x +
                lane.Depth / 1000.0;
        }

        private void ClearDerivedVisuals()
        {
            derivedVisuals.Clear();
            var stale = GetComponentsInChildren<PixelLabBuildingDerivedVisual>(
                true
            );
            foreach (var visual in stale)
            {
                if (visual == null)
                {
                    continue;
                }
#if UNITY_EDITOR
                if (!Application.isPlaying)
                {
                    UnityEditor.Undo.DestroyObjectImmediate(visual.gameObject);
                    continue;
                }
#endif
                DestroyImmediate(visual.gameObject);
            }
        }

        private void ResolveStorey(PixelLabBuildingStoreyState storey)
        {
            var candidates = new HashSet<PixelLabBuildingCellAddress>();
            foreach (var cell in storey.cells)
            {
                candidates.Add(new PixelLabBuildingCellAddress(cell.q, cell.r, storey.layerY));
            }
            foreach (var candidate in candidates)
            {
                ResolveCell(candidate.layerY, candidate.q, candidate.r);
            }
        }

        private void ResolveCell(int layerY, int q, int r)
        {
            var structureMask = StructureMaskAt(layerY, q, r);
            var hasPartition = editState.HasCell(
                PixelLabBuildingRoles.Partition,
                layerY,
                q,
                r
            );
            var partitionMask = hasPartition ? PartitionMaskAt(layerY, q, r) : 0;

            if (hasPartition)
            {
                foreach (var direction in Directions)
                {
                    var arm = PartitionArm(direction.side);
                    var wallArm = PartitionWallArm(direction.side);
                    var side = StructureSide(direction.side);
                    if ((partitionMask & arm) != 0 && (structureMask & side) != 0)
                    {
                        partitionMask = (partitionMask & ~arm) | wallArm;
                    }
                }
                var hasHorizontalArm = (partitionMask & (
                    PartE | PartW | PartWallE | PartWallW
                )) != 0;
                var hasVerticalArm = (partitionMask & (
                    PartN | PartS | PartWallN | PartWallS
                )) != 0;
                if (!(hasHorizontalArm && hasVerticalArm))
                {
                    foreach (var direction in Directions)
                    {
                        if ((partitionMask & PartitionWallArm(direction.side)) != 0)
                        {
                            structureMask &= ~StructureSide(direction.side);
                        }
                    }
                }
                if (IsPartitionDoorAt(layerY, q, r))
                {
                    partitionMask = 0;
                }
                editState.SetResolvedCell(
                    PixelLabBuildingRoles.Partition,
                    layerY,
                    q,
                    r,
                    partitionMask
                );
            }

            var exteriorSide = ExteriorDoorSideAt(layerY, q, r);
            if (exteriorSide != '\0')
            {
                structureMask &= ~StructureSide(exteriorSide);
            }
            if (structureMask == 0)
            {
                editState.RemoveResolvedCell(
                    PixelLabBuildingRoles.Structure,
                    layerY,
                    q,
                    r
                );
            }
            else
            {
                editState.SetResolvedCell(
                    PixelLabBuildingRoles.Structure,
                    layerY,
                    q,
                    r,
                    structureMask
                );
            }
        }

        private int StructureMaskAt(int layerY, int q, int r)
        {
            if (!HasWalledFloor(layerY, q, r))
            {
                return 0;
            }
            var mask = 0;
            foreach (var direction in Directions)
            {
                if (!HasGroundAt(layerY, q + direction.dq, r + direction.dr))
                {
                    mask |= StructureSide(direction.side);
                }
            }
            foreach (var diagonal in Diagonals)
            {
                if (HasGroundAt(layerY, q + diagonal.dq, r + diagonal.dr))
                {
                    continue;
                }
                var first = DirectionFor(diagonal.first);
                var second = DirectionFor(diagonal.second);
                if (HasGroundAt(layerY, q + first.dq, r + first.dr) &&
                    HasGroundAt(layerY, q + second.dq, r + second.dr))
                {
                    mask |= diagonal.bit;
                }
            }
            return mask;
        }

        private int PartitionMaskAt(int layerY, int q, int r)
        {
            var mask = PartHub;
            foreach (var direction in Directions)
            {
                var neighbourPartition = editState.HasCell(
                    PixelLabBuildingRoles.Partition,
                    layerY,
                    q + direction.dq,
                    r + direction.dr
                );
                if (neighbourPartition)
                {
                    mask |= PartitionArm(direction.side);
                }
                else if (HasWalledFloor(layerY, q, r) &&
                    !HasGroundAt(
                        layerY,
                        q + direction.dq,
                        r + direction.dr
                    ))
                {
                    mask |= PartitionWallArm(direction.side);
                }
            }
            return mask;
        }

        private bool HasWalledFloor(int layerY, int q, int r)
        {
            var cell = editState.FindCell(PixelLabBuildingRoles.Floor, layerY, q, r);
            return cell != null && cell.tileIndex < kit.semanticAssets.noWallFloorOffset;
        }

        private void RenderOwner(string role, int layerY, int q, int r)
        {
            var lanes = LanesFor(role, layerY);
            var position = PixelLabGridCoordinates.ToUnityCell(kit.gridKind, q, r);
            foreach (var lane in lanes)
            {
                lane.Tilemap.SetTile(position, null);
            }

            var logical = editState.FindCell(role, layerY, q, r);
            if (logical == null)
            {
                return;
            }
            if (role == PixelLabBuildingRoles.Floor)
            {
                var tileIndex = logical.tileIndex >= kit.semanticAssets.noWallFloorOffset
                    ? logical.tileIndex - kit.semanticAssets.noWallFloorOffset
                    : logical.tileIndex;
                PlaceAuto("floor", layerY, q, r, RequireTile(tileIndex));
                return;
            }
            if (role == PixelLabBuildingRoles.Stamp)
            {
                PlaceAuto("stamp", layerY, q, r, RequireTile(logical.tileIndex));
                return;
            }

            var variant = RequireVariant(role, logical.tileIndex);
            foreach (var part in variant.parts ?? Array.Empty<PixelLabBuildingPart>())
            {
                PlaceAuto(
                    role + "_" + part.part,
                    layerY,
                    q,
                    r,
                    RequireTile(part.tileIndex)
                );
            }
        }

        private void PlaceAuto(
            string laneId,
            int layerY,
            int q,
            int r,
            PixelLabBuildingNativeTile tile
        )
        {
            var lane = FindLane(layerY, laneId, true);
            var position = PixelLabGridCoordinates.ToUnityCell(kit.gridKind, q, r);
            lane.Tilemap.SetTile(position, tile);
            lane.Tilemap.SetTransformMatrix(
                position,
                ProjectedTransform(lane.Tilemap, q, r, layerY, tile)
            );
        }

        private Matrix4x4 ProjectedTransform(
            Tilemap tilemap,
            int q,
            int r,
            int layerY,
            PixelLabBuildingNativeTile tile
        )
        {
            var position = PixelLabGridCoordinates.ToUnityCell(kit.gridKind, q, r);
            var pivot = ProjectedPartPivot(q, r, layerY, tile.sourceHeight);
            var localPivot = tilemap.transform.InverseTransformPoint(
                transform.TransformPoint(pivot)
            );
            return Matrix4x4.Translate(
                localPivot - tilemap.GetCellCenterLocal(position)
            );
        }

        private Vector3 ProjectedPartPivot(int q, int r, int layerY, int sourceHeight)
        {
            var projection = kit.projection;
            var x = projection.spriteOrigin.x + q * projection.qBasis.x +
                r * projection.rBasis.x + layerY * projection.stackBasis.x;
            var y = projection.spriteOrigin.y + q * projection.qBasis.y +
                r * projection.rBasis.y + layerY * projection.stackBasis.y -
                sourceHeight;
            return new Vector3(x / pixelsPerUnit, -y / pixelsPerUnit, 0f);
        }

        private bool EraseStampPair(int layerY, int q, int r)
        {
            var changed = new List<PixelLabBuildingCellAddress>();
            if (!EraseStampPairStateOnly(layerY, q, r, changed))
            {
                return false;
            }
            RegenerateFootprint(changed);
            return true;
        }

        private bool EraseStampPairStateOnly(
            int layerY,
            int q,
            int r,
            List<PixelLabBuildingCellAddress> changed
        )
        {
            var cell = editState.FindCell(PixelLabBuildingRoles.Stamp, layerY, q, r);
            if (cell == null)
            {
                return false;
            }
            PixelLabBuildingPair pair;
            bool first;
            var cells = new List<PixelLabBuildingCellAddress>
            {
                new PixelLabBuildingCellAddress(q, r, layerY),
            };
            if (TryPair(cell.tileIndex, out pair, out first))
            {
                cells.Add(new PixelLabBuildingCellAddress(
                    first ? q + pair.offset.q : q - pair.offset.q,
                    first ? r + pair.offset.r : r - pair.offset.r,
                    layerY
                ));
            }
            var removed = false;
            foreach (var address in cells)
            {
                removed |= editState.RemoveSemanticCell(
                    PixelLabBuildingRoles.Stamp,
                    address.layerY,
                    address.q,
                    address.r
                );
                changed.Add(address);
            }
            return removed;
        }

        private bool TryPair(
            int tileIndex,
            out PixelLabBuildingPair pair,
            out bool first
        )
        {
            foreach (var candidate in kit.pairedPieces ??
                Array.Empty<PixelLabBuildingPair>())
            {
                if (candidate.offset == null)
                {
                    throw new InvalidOperationException(
                        "Buildings pair \"" + candidate.kind + "\" has no offset."
                    );
                }
                if (candidate.firstTileIndex == tileIndex)
                {
                    pair = candidate;
                    first = true;
                    return true;
                }
                if (candidate.secondTileIndex == tileIndex)
                {
                    pair = candidate;
                    first = false;
                    return true;
                }
            }
            pair = null;
            first = false;
            return false;
        }

        private PixelLabBuildingPair RequirePair(
            string kind,
            string orientation,
            string side,
            string axis
        )
        {
            PixelLabBuildingPair match = null;
            foreach (var candidate in kit.pairedPieces ??
                Array.Empty<PixelLabBuildingPair>())
            {
                if (candidate.kind != kind ||
                    (!string.IsNullOrEmpty(orientation) &&
                     candidate.orientation != orientation) ||
                    (!string.IsNullOrEmpty(side) && candidate.side != side) ||
                    (!string.IsNullOrEmpty(axis) && candidate.axis != axis))
                {
                    continue;
                }
                if (match != null)
                {
                    throw new InvalidOperationException(
                        "Buildings paired-piece selection is ambiguous for kind \"" +
                        kind + "\". Specify its orientation, side, or axis."
                    );
                }
                match = candidate;
            }
            if (match == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" has no paired piece matching " +
                    kind + "/" + orientation + "/" + side + "/" + axis + "."
                );
            }
            if (match.offset == null)
            {
                throw new InvalidOperationException(
                    "Buildings pair \"" + match.kind + "\" has no offset."
                );
            }
            RequireTile(match.firstTileIndex);
            RequireTile(match.secondTileIndex);
            return match;
        }

        private char ExteriorDoorSideAt(int layerY, int q, int r)
        {
            var stamp = editState.FindCell(PixelLabBuildingRoles.Stamp, layerY, q, r);
            if (stamp == null)
            {
                return '\0';
            }
            foreach (var pair in kit.pairedPieces ?? Array.Empty<PixelLabBuildingPair>())
            {
                if (pair.kind == "exterior-door" &&
                    (pair.firstTileIndex == stamp.tileIndex ||
                     pair.secondTileIndex == stamp.tileIndex) &&
                    !string.IsNullOrEmpty(pair.side))
                {
                    return pair.side[0];
                }
            }
            return '\0';
        }

        private bool IsPartitionDoorAt(int layerY, int q, int r)
        {
            var stamp = editState.FindCell(PixelLabBuildingRoles.Stamp, layerY, q, r);
            if (stamp == null)
            {
                return false;
            }
            foreach (var pair in kit.pairedPieces ?? Array.Empty<PixelLabBuildingPair>())
            {
                if (pair.kind == "partition-door" &&
                    (pair.firstTileIndex == stamp.tileIndex ||
                     pair.secondTileIndex == stamp.tileIndex))
                {
                    return true;
                }
            }
            return false;
        }

        private PixelLabBuildingVariant RequireVariant(string role, int mask)
        {
            PixelLabBuildingVariant value;
            var source = role == PixelLabBuildingRoles.Structure
                ? structureByMask
                : partitionByMask;
            if (!source.TryGetValue(mask, out value))
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" is missing " + role +
                    " variant mask " + mask + "."
                );
            }
            if (value.parts == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" has null parts for " + role +
                    " variant mask " + mask + "."
                );
            }
            return value;
        }

        private PixelLabBuildingNativeTile RequireTile(int tileIndex)
        {
            PixelLabBuildingNativeTile tile;
            if (!tilesByIndex.TryGetValue(tileIndex, out tile) || tile == null ||
                tile.sprite == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" is missing native tile asset " +
                    tileIndex + "."
                );
            }
            return tile;
        }

        private PixelLabBuildingLaneTilemap FindLane(
            int layerY,
            string laneId,
            bool required
        )
        {
            for (var index = 0; index < laneTilemaps.Count; index++)
            {
                var lane = laneTilemaps[index];
                if (lane != null && lane.LayerY == layerY && lane.LaneId == laneId)
                {
                    return lane;
                }
            }
            if (required)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" is missing native lane \"" +
                    laneId + "\" on Y" + layerY + "."
                );
            }
            return null;
        }

        private List<PixelLabBuildingLaneTilemap> LanesFor(string role, int layerY)
        {
            var output = new List<PixelLabBuildingLaneTilemap>();
            foreach (var lane in laneTilemaps)
            {
                if (lane != null && lane.Role == role && lane.LayerY == layerY)
                {
                    output.Add(lane);
                }
            }
            return output;
        }

        private void RefreshCachesAndValidate()
        {
            if (kit.projection == null || kit.projection.spriteOrigin == null ||
                kit.projection.editorOrigin == null || kit.projection.qBasis == null ||
                kit.projection.rBasis == null || kit.projection.stackBasis == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" is missing projection bases."
                );
            }
            if (kit.semanticAssets == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" is missing semantic assets."
                );
            }
            if (kit.dependencyHalo != 1)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" must declare dependencyHalo=1."
                );
            }

            groundSupport.Clear();
            foreach (var cell in kit.groundSupportCells ??
                Array.Empty<PixelLabBuildingCellAddress>())
            {
                groundSupport.Add(
                    new PixelLabBuildingCellAddress(cell.q, cell.r, cell.layerY)
                );
            }

            tilesByIndex.Clear();
            foreach (var tile in nativeTiles)
            {
                if (tile == null)
                {
                    continue;
                }
                if (tilesByIndex.ContainsKey(tile.tileIndex))
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" repeats tile asset " +
                        tile.tileIndex + "."
                    );
                }
                tilesByIndex.Add(tile.tileIndex, tile);
            }
            foreach (var asset in kit.assets ?? Array.Empty<PixelLabTileAsset>())
            {
                var native = RequireTile(asset.tileIndex);
                if (native.sourceWidth != asset.width || native.sourceHeight != asset.height)
                {
                    throw new InvalidOperationException(
                        "Buildings tile " + asset.tileIndex +
                        " dimensions do not match the manifest."
                    );
                }
            }

            IndexVariants(
                kit.structureVariants,
                256,
                PixelLabBuildingRoles.Structure,
                structureByMask
            );
            IndexVariants(
                kit.partitionVariants,
                512,
                PixelLabBuildingRoles.Partition,
                partitionByMask
            );
            foreach (var definition in kit.visualLanes ??
                Array.Empty<PixelLabBuildingVisualLane>())
            {
                PixelLabBuildingRoles.Require(definition.role);
                if (definition.tileIndices == null || definition.tileIndices.Length == 0)
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" lane \"" + definition.id +
                        "\" has no native tile vocabulary."
                    );
                }
                foreach (var tileIndex in definition.tileIndices)
                {
                    RequireTile(tileIndex);
                }
            }
            if (FindLaneDefinition("floor") == null || FindLaneDefinition("stamp") == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id +
                    "\" must define native floor and stamp visual lanes."
                );
            }
        }

        private void IndexVariants(
            PixelLabBuildingVariant[] variants,
            int expectedCount,
            string role,
            Dictionary<int, PixelLabBuildingVariant> output
        )
        {
            output.Clear();
            foreach (var variant in variants ?? Array.Empty<PixelLabBuildingVariant>())
            {
                if (output.ContainsKey(variant.mask))
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" repeats " + role +
                        " variant mask " + variant.mask + "."
                    );
                }
                if (variant.parts == null)
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" has null parts for " +
                        role + " variant mask " + variant.mask + "."
                    );
                }
                output.Add(variant.mask, variant);
                foreach (var part in variant.parts)
                {
                    RequireTile(part.tileIndex);
                    if (FindLaneDefinition(role + "_" + part.part) == null)
                    {
                        throw new InvalidOperationException(
                            "Buildings kit \"" + kit.id + "\" has no visual lane for " +
                            role + " part \"" + part.part + "\"."
                        );
                    }
                }
            }
            for (var mask = 0; mask < expectedCount; mask++)
            {
                if (!output.ContainsKey(mask))
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" is missing " + role +
                        " variant mask " + mask + "."
                    );
                }
            }
        }

        private PixelLabBuildingVisualLane FindLaneDefinition(string laneId)
        {
            foreach (var definition in kit.visualLanes ??
                Array.Empty<PixelLabBuildingVisualLane>())
            {
                if (definition.id == laneId)
                {
                    return definition;
                }
            }
            return null;
        }

        private void RequireConfigured()
        {
            if (kit == null || editState == null)
            {
                throw new InvalidOperationException(
                    "PixelLabBuildingController has not been configured."
                );
            }
            if (tilesByIndex.Count == 0 ||
                structureByMask.Count == 0 ||
                partitionByMask.Count == 0)
            {
                RefreshCachesAndValidate();
            }
        }

        private IDisposable BeginRegeneration()
        {
            regenerationDepth++;
            return new RegenerationScope(this);
        }

        private Transform FindOrCreateChild(
            Transform parent,
            string name,
            List<GameObject> created
        )
        {
            var child = parent.Find(name);
            if (child != null)
            {
                return child;
            }
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            created.Add(gameObject);
            return gameObject.transform;
        }

        private static string RoleLabel(string role)
        {
            return char.ToUpperInvariant(role[0]) + role.Substring(1) + " Lanes";
        }

        private static int StructureSide(char side)
        {
            switch (side)
            {
                case 'N': return SideN;
                case 'E': return SideE;
                case 'S': return SideS;
                case 'W': return SideW;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private static int PartitionArm(char side)
        {
            switch (side)
            {
                case 'N': return PartN;
                case 'E': return PartE;
                case 'S': return PartS;
                case 'W': return PartW;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private static int PartitionWallArm(char side)
        {
            switch (side)
            {
                case 'N': return PartWallN;
                case 'E': return PartWallE;
                case 'S': return PartWallS;
                case 'W': return PartWallW;
                default: throw new ArgumentOutOfRangeException(nameof(side));
            }
        }

        private static Direction DirectionFor(char side)
        {
            foreach (var direction in Directions)
            {
                if (direction.side == side)
                {
                    return direction;
                }
            }
            throw new ArgumentOutOfRangeException(nameof(side));
        }

        private static readonly Direction[] Directions =
        {
            new Direction('N', 0, -1),
            new Direction('E', 1, 0),
            new Direction('S', 0, 1),
            new Direction('W', -1, 0),
        };

        private static readonly Diagonal[] Diagonals =
        {
            new Diagonal(1, -1, 'N', 'E', CornerNE),
            new Diagonal(1, 1, 'S', 'E', CornerSE),
            new Diagonal(-1, 1, 'S', 'W', CornerSW),
            new Diagonal(-1, -1, 'N', 'W', CornerNW),
        };

        private struct Direction
        {
            public readonly char side;
            public readonly int dq;
            public readonly int dr;

            public Direction(char sideValue, int qOffset, int rOffset)
            {
                side = sideValue;
                dq = qOffset;
                dr = rOffset;
            }
        }

        private struct Diagonal
        {
            public readonly int dq;
            public readonly int dr;
            public readonly char first;
            public readonly char second;
            public readonly int bit;

            public Diagonal(int qOffset, int rOffset, char firstSide, char secondSide, int value)
            {
                dq = qOffset;
                dr = rOffset;
                first = firstSide;
                second = secondSide;
                bit = value;
            }
        }

        private sealed class RegenerationScope : IDisposable
        {
            private PixelLabBuildingController owner;

            public RegenerationScope(PixelLabBuildingController controller)
            {
                owner = controller;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }
                owner.regenerationDepth--;
                owner = null;
            }
        }

        private sealed class SemanticBatchScope : IDisposable
        {
            private PixelLabBuildingController owner;

            public SemanticBatchScope(PixelLabBuildingController value)
            {
                owner = value;
            }

            public void Dispose()
            {
                if (owner == null)
                {
                    return;
                }
                owner.semanticBatchDepth--;
                if (owner.semanticBatchDepth == 0 && owner.batchedChanges.Count > 0)
                {
                    var changed = new List<PixelLabBuildingCellAddress>(
                        owner.batchedChanges
                    );
                    owner.batchedChanges.Clear();
                    owner.RegenerateFootprintNow(changed);
                }
                owner = null;
            }
        }
    }
}
