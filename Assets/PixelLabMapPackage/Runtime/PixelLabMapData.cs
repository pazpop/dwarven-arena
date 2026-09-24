using System;
using UnityEngine;

namespace PixelLab.MapExport
{
    [Serializable]
    public sealed class PixelLabMapManifest
    {
        public string format;
        public string version;
        public string generator;
        public string renderingMode;
        public bool nativeTilemap;
        public bool hasSeparateTileLayers;
        public string mapId;
        public string name;
        public float pixelsPerUnit;
        public PixelLabSize tileSize;
        public PixelLabVector2 origin;
        public PixelLabSize dimensions;
        public string baseImage;
        public bool baseIncludesObjects;
        public PixelLabBackground background;
        public PixelLabOverlay[] overlays;
        public PixelLabObject[] objects;
        public PixelLabAnnotationLayer[] annotations;
        public PixelLabBuildingKit[] buildingKits;
        public PixelLabProjectionLayer[] projectionLayers;
        public PixelLabLegacyTerrain legacyTerrain;
        public string[] sourceFiles;
    }

    [Serializable]
    public sealed class PixelLabSize
    {
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class PixelLabVector2
    {
        public float x;
        public float y;
    }

    [Serializable]
    public sealed class PixelLabBackground
    {
        public string image;
        public float x;
        public float y;
        public float width;
        public float height;
        public bool repeatX;
        public bool repeatY;
        public PixelLabVerticalExtend verticalExtend;
        public string sourceJson;
    }

    [Serializable]
    public sealed class PixelLabVerticalExtend
    {
        public bool enabled;
        public float topHeight;
        public float bottomHeight;
    }

    [Serializable]
    public sealed class PixelLabOverlay
    {
        public string id;
        public string type;
        public string image;
        public float x;
        public float y;
        public float width;
        public float height;
        public string layer;
        public string sourceJson;
    }

    [Serializable]
    public sealed class PixelLabObject
    {
        public string id;
        public string name;
        public string image;
        public float x;
        public float y;
        public float width;
        public float height;
        public int drawOrder;
        public bool visible;
        public PixelLabBox collider;
        public string sourceJson;
    }

    [Serializable]
    public sealed class PixelLabProjectionLayer
    {
        public string gridKind;
        public string tileType;
        public string tileGroupId;
        public string tileGroupName;
        public string role;
        public float tileSize;
        public float tileW;
        public float tileH;
        public float tileFlatTopPx;
        public PixelLabProjection projection;
        public PixelLabCell[] cells;
        public PixelLabTileAsset[] tileAssets;
        public PixelLabPlacement[] placements;
        public PixelLabUnityTileRules unityTileRules;
        public double drawOrderOffset;
    }

    [Serializable]
    public sealed class PixelLabProjection
    {
        public PixelLabVector2 origin;
        public PixelLabVector2 editorOrigin;
        public PixelLabVector2 spriteOrigin;
        public PixelLabVector2 qBasis;
        public PixelLabVector2 rBasis;
        public PixelLabVector2 stackBasis;
        public PixelLabSize cellSize;
    }

    [Serializable]
    public sealed class PixelLabCell
    {
        public int q;
        public int r;
        public int layerY;
        public int tileIndex;
    }

    [Serializable]
    public sealed class PixelLabTileAsset
    {
        public int tileIndex;
        public string assetPath;
        public int width;
        public int height;
    }

    [Serializable]
    public sealed class PixelLabPlacement
    {
        public int q;
        public int r;
        public int layerY;
        public int tileIndex;
        public string assetPath;
        public float xPx;
        public float yPx;
        public float width;
        public float height;
        public float sourceWidth;
        public float sourceHeight;
        public double drawOrder;
        public int renderOrder;
        public PixelLabSourceRect sourceRect;
    }

    [Serializable]
    public sealed class PixelLabSourceRect
    {
        public float x;
        public float y;
        public float width;
        public float height;
    }

    [Serializable]
    public sealed class PixelLabBuildingKit
    {
        public string id;
        public string name;
        public string gridKind;
        public string tileType;
        public float tileSize;
        public float tileW;
        public float tileH;
        public PixelLabProjection projection;
        public PixelLabBuildingMaterials materials;
        public PixelLabBuildingWallGeometry wallGeometry;
        public PixelLabBuildingSemanticAssets semanticAssets;
        public PixelLabTileAsset[] assets;
        public PixelLabBuildingCellAddress[] groundSupportCells;
        public PixelLabBuildingStorey[] storeys;
        public PixelLabBuildingVisualLane[] visualLanes;
        public PixelLabBuildingVisualCell[] visualCells;
        public PixelLabBuildingVariant[] structureVariants;
        public PixelLabBuildingVariant[] partitionVariants;
        public PixelLabBuildingPair[] pairedPieces;
        public int dependencyHalo;
    }

    [Serializable]
    public sealed class PixelLabBuildingMaterials
    {
        public string wall;
        public string floor;
        public string floor2;
    }

    [Serializable]
    public sealed class PixelLabBuildingWallGeometry
    {
        public int groundFacePx;
        public int stackStridePx;
        public int wallTiles;
        public float viewAngle;
        public float obliqueLean;
    }

    [Serializable]
    public sealed class PixelLabBuildingSemanticAssets
    {
        public int floorTileIndex;
        public int roofTileIndex;
        public int pillarTileIndex;
        public int partitionMarkerTileIndex;
        public int noWallFloorOffset;
    }

    [Serializable]
    public sealed class PixelLabBuildingStorey
    {
        public int layerY;
        public PixelLabBuildingLogicalRoles logical;
        public PixelLabBuildingVisualCell[] resolvedPlacements;
    }

    [Serializable]
    public sealed class PixelLabBuildingLogicalRoles
    {
        public PixelLabCell[] floor;
        public PixelLabCell[] partition;
        public PixelLabCell[] structure;
        public PixelLabCell[] stamp;
    }

    [Serializable]
    public sealed class PixelLabBuildingVisualLane
    {
        public string id;
        public string name;
        public string role;
        public int depth;
        public int[] tileIndices;
    }

    [Serializable]
    public sealed class PixelLabBuildingVisualCell
    {
        public int q;
        public int r;
        public int layerY;
        public int tileIndex;
        public string role;
        public string lane;
        public int depth;
        public string assetPath;
        public float xPx;
        public float yPx;
        public float width;
        public float height;
        public double drawOrder;
        public string ownerKey;
    }

    [Serializable]
    public sealed class PixelLabBuildingVariant
    {
        public int mask;
        public PixelLabBuildingPart[] parts;
    }

    [Serializable]
    public sealed class PixelLabBuildingPart
    {
        public string part;
        public int tileIndex;
        public int depth;
    }

    [Serializable]
    public sealed class PixelLabBuildingPair
    {
        public string kind;
        public string orientation;
        public string side;
        public string axis;
        public int firstTileIndex;
        public int secondTileIndex;
        public PixelLabBuildingCellOffset offset;
    }

    [Serializable]
    public sealed class PixelLabBuildingCellOffset
    {
        public int q;
        public int r;
    }

    [Serializable]
    public sealed class PixelLabLegacyTerrain
    {
        public bool enabled;
        public PixelLabSize tileSize;
        public PixelLabSize bounds;
        public PixelLabTerrainDefinition[] terrains;
        public PixelLabPaintValue[] paintValues;
        public PixelLabLegacyLayer[] layers;
    }

    [Serializable]
    public sealed class PixelLabTerrainDefinition
    {
        public int id;
        public string name;
        public string baseTileId;
        public bool isTransition;
    }

    [Serializable]
    public sealed class PixelLabLegacyLayer
    {
        public string id;
        public string name;
        public string tilesetId;
        public string image;
        public int lowerTerrainId;
        public int upperTerrainId;
        public int atlasColumns;
        public int atlasRows;
        public int[] atlasTiles;
        public bool sidescroller;
        public PixelLabPaintValue[] paintValues;
        public PixelLabCell[] cells;
        public PixelLabPlacement[] placements;
        public PixelLabUnityTileRules unityTileRules;
    }

    [Serializable]
    public sealed class PixelLabPaintValue
    {
        public int x;
        public int y;
        public int value;
    }

    [Serializable]
    public sealed class PixelLabUnityTileRules
    {
        public string ruleType;
        public int arity;
        public string connectivity;
        public int stepPx;
        public int wildcard = 255;
        public string[] terrains;
        public int[] terrainIds;
        public PixelLabRuleEntry[] entries;
    }

    [Serializable]
    public sealed class PixelLabRuleEntry
    {
        public int tileIndex;
        public bool hasMask;
        public int mask;
        public int[] masks;
        public int[] pattern;
    }

    [Serializable]
    public sealed class PixelLabAnnotationLayer
    {
        public string id;
        public string name;
        public string role;
        public PixelLabBox[] rects;
        public PixelLabMarker[] points;
        public string sourceJson;
    }

    [Serializable]
    public sealed class PixelLabBox
    {
        public string id;
        public string role;
        public float x;
        public float y;
        public float width;
        public float height;
        public string sourceJson;
    }

    [Serializable]
    public sealed class PixelLabMarker
    {
        public string id;
        public string name;
        public float x;
        public float y;
        public string sourceJson;
    }

    public static class PixelLabGridCoordinates
    {
        public static Vector3Int ToUnityCell(string gridKind, int q, int r)
        {
            switch (gridKind)
            {
                case "iso":
                    return new Vector3Int(-r, -q, 0);
                case "hex-pointy-top":
                    return new Vector3Int(q + FloorDiv2(r), -r, 0);
                case "hex-flat-top":
                    // Measured on real Unity (PixelLabGridMathProbe): under the
                    // YXZ swizzle the COLUMN axis is cell.y (world.x = y*0.75*sy)
                    // and odd columns shift world.y by +sx/2 — so q maps to
                    // cell.y and the cumulative row shift needs CEIL parity to
                    // net -sx/2 per column against Unity's own stagger. The
                    // import breakage was never this mapping; it was the
                    // magnitude-only AxisScale cell sizes and the anchor.
                    return new Vector3Int(-r - CeilDiv2(q), q, 0);
                default:
                    return new Vector3Int(q, -r, 0);
            }
        }

        public static Vector2Int ToAxial(string gridKind, Vector3Int cell)
        {
            switch (gridKind)
            {
                case "iso":
                    return new Vector2Int(-cell.y, -cell.x);
                case "hex-pointy-top":
                {
                    var r = -cell.y;
                    return new Vector2Int(cell.x - FloorDiv2(r), r);
                }
                case "hex-flat-top":
                {
                    var q = cell.y;
                    return new Vector2Int(q, -cell.x - CeilDiv2(q));
                }
                default:
                    return new Vector2Int(cell.x, -cell.y);
            }
        }

        private static int FloorDiv2(int value)
        {
            return value >= 0 ? value / 2 : -((-value + 1) / 2);
        }

        private static int CeilDiv2(int value)
        {
            return value >= 0 ? (value + 1) / 2 : -((-value) / 2);
        }
    }
}
