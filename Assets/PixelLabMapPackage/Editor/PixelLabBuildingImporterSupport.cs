using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport.Editor
{
    public static class PixelLabBuildingImporterSupport
    {
        public static PixelLabBuildingController BuildNativeKit(
            Transform parent,
            PixelLabBuildingKit kit,
            float pixelsPerUnit,
            string mapId,
            string sourceFingerprint,
            string generatedRoot
        )
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }
            ValidateKit(kit);
            if (string.IsNullOrEmpty(sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "Buildings import requires a deterministic source fingerprint."
                );
            }
            if (string.IsNullOrEmpty(generatedRoot) ||
                !generatedRoot.Replace('\\', '/').Contains("/Generated"))
            {
                throw new InvalidOperationException(
                    "Buildings generated tiles must be written below the Generated folder."
                );
            }

            var state = LoadOrCreateEditState(
                mapId,
                kit,
                sourceFingerprint,
                false
            );
            var nativeTiles = BuildNativeTiles(kit, generatedRoot);
            var root = new GameObject("Buildings - " + kit.name);
            root.transform.SetParent(parent, false);
            root.AddComponent<Grid>();
            var controller = root.AddComponent<PixelLabBuildingController>();
            controller.Configure(kit, state, pixelsPerUnit, nativeTiles);
            EditorUtility.SetDirty(controller);
            if (root.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(root.scene);
            }
            AssetDatabase.SaveAssets();
            return controller;
        }

        public static PixelLabBuildingEditState LoadOrCreateEditState(
            string mapId,
            PixelLabBuildingKit kit,
            string sourceFingerprint,
            bool explicitlyReplaceEditedState
        )
        {
            ValidateKit(kit);
            EnsureFolder(EditStateRoot(mapId));
            var statePath = EditStatePath(mapId, kit.id);
            var state = AssetDatabase.LoadAssetAtPath<PixelLabBuildingEditState>(
                statePath
            );
            if (state == null)
            {
                state = ScriptableObject.CreateInstance<PixelLabBuildingEditState>();
                state.name = SafeName(mapId) + " " + SafeName(kit.name) + " Edits";
                state.InitializeFromManifest(mapId, kit, sourceFingerprint);
                AssetDatabase.CreateAsset(state, statePath);
                EditorUtility.SetDirty(state);
                return state;
            }
            if (state.KitId != kit.id)
            {
                throw new InvalidOperationException(
                    "Serialized Buildings edit state at " + statePath +
                    " belongs to kit \"" + state.KitId + "\"."
                );
            }
            if (state.SourceFingerprint == sourceFingerprint)
            {
                return state;
            }
            if (state.RequiresExplicitReplacement(sourceFingerprint) &&
                !explicitlyReplaceEditedState)
            {
                throw new InvalidOperationException(
                    "PixelLab will not replace edited Buildings state at " + statePath +
                    ". Import as a new scene or explicitly replace the saved edits."
                );
            }

            Undo.RecordObject(state, "Replace PixelLab Building Edit State");
            state.InitializeFromManifest(mapId, kit, sourceFingerprint);
            EditorUtility.SetDirty(state);
            return state;
        }

        public static PixelLabBuildingEditState ExplicitlyReplaceEditState(
            string mapId,
            PixelLabBuildingKit kit,
            string sourceFingerprint
        )
        {
            return LoadOrCreateEditState(mapId, kit, sourceFingerprint, true);
        }

        public static void AssertRefreshAllowed(
            string mapId,
            PixelLabBuildingKit kit,
            string sourceFingerprint
        )
        {
            ValidateKit(kit);
            var statePath = EditStatePath(mapId, kit.id);
            var state = AssetDatabase.LoadAssetAtPath<PixelLabBuildingEditState>(
                statePath
            );
            if (state == null)
            {
                return;
            }
            if (state.KitId != kit.id)
            {
                throw new InvalidOperationException(
                    "Serialized Buildings edit state at " + statePath +
                    " belongs to kit \"" + state.KitId + "\"."
                );
            }
            if (state.RequiresExplicitReplacement(sourceFingerprint))
            {
                throw new InvalidOperationException(
                    "PixelLab will not replace edited Buildings state at " + statePath +
                    ". Use Tools > PixelLab > Rebuild Map (Replace Buildings Edits) " +
                    "only when you intend to discard those native edits."
                );
            }
        }

        public static string EditStatePath(string mapId, string kitId)
        {
            return EditStateRoot(mapId) + "/" + SafeName(mapId) + "_" +
                SafeName(kitId) + ".asset";
        }

        public static string EditStateRoot(string mapId)
        {
            if (string.IsNullOrWhiteSpace(mapId))
            {
                throw new InvalidOperationException(
                    "Buildings export requires a stable map id."
                );
            }
            var characters = mapId.Select(character =>
                char.IsLetterOrDigit(character) || character == '_' || character == '-'
                    ? character
                    : '-'
            ).ToArray();
            var segment = new string(characters).Trim('-');
            if (string.IsNullOrEmpty(segment))
            {
                throw new InvalidOperationException(
                    "Buildings export requires a stable map id."
                );
            }
            return "Assets/PixelLabMaps/" + segment + "/Edits/Buildings";
        }

        private static List<PixelLabBuildingNativeTile> BuildNativeTiles(
            PixelLabBuildingKit kit,
            string generatedRoot
        )
        {
            var output = new List<PixelLabBuildingNativeTile>();
            var folder = generatedRoot.TrimEnd('/', '\\') + "/BuildingTiles/" +
                SafeName(kit.id);
            EnsureFolder(folder);
            foreach (var asset in kit.assets ?? Array.Empty<PixelLabTileAsset>())
            {
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(asset.assetPath);
                if (sprite == null)
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" cannot load sprite asset " +
                        asset.assetPath + "."
                    );
                }
                if (Mathf.RoundToInt(sprite.rect.width) != asset.width ||
                    Mathf.RoundToInt(sprite.rect.height) != asset.height)
                {
                    throw new InvalidOperationException(
                        "Buildings sprite \"" + asset.assetPath +
                        "\" dimensions do not match the manifest."
                    );
                }
                if (sprite.pivot != new Vector2(0f, sprite.rect.height))
                {
                    throw new InvalidOperationException(
                        "Buildings sprite \"" + asset.assetPath +
                        "\" must use the exported top-left pivot."
                    );
                }

                var tilePath = folder + "/Tile_" + asset.tileIndex + ".asset";
                var tileName = Path.GetFileNameWithoutExtension(tilePath);
                var tile = AssetDatabase.LoadAssetAtPath<PixelLabBuildingNativeTile>(
                    tilePath
                );
                if (tile == null)
                {
                    tile = ScriptableObject.CreateInstance<PixelLabBuildingNativeTile>();
                    tile.name = tileName;
                    AssetDatabase.CreateAsset(tile, tilePath);
                }
                tile.name = tileName;
                tile.tileIndex = asset.tileIndex;
                tile.sourceAssetPath = asset.assetPath;
                tile.sourceWidth = asset.width;
                tile.sourceHeight = asset.height;
                tile.sprite = sprite;
                tile.color = Color.white;
                tile.transform = Matrix4x4.identity;
                tile.flags = TileFlags.None;
                tile.colliderType = Tile.ColliderType.None;
                EditorUtility.SetDirty(tile);
                output.Add(tile);
            }
            return output;
        }

        private static void ValidateKit(PixelLabBuildingKit kit)
        {
            if (kit == null)
            {
                throw new ArgumentNullException(nameof(kit));
            }
            if (string.IsNullOrEmpty(kit.id))
            {
                throw new InvalidOperationException("A Buildings kit is missing its id.");
            }
            if (kit.assets == null || kit.assets.Length == 0)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" has no individual assets."
                );
            }
            if (kit.groundSupportCells == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id +
                    "\" is missing its regular-terrain support contract."
                );
            }
            if (kit.structureVariants == null || kit.structureVariants.Length != 256)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id +
                    "\" must contain all 256 structureVariants."
                );
            }
            if (kit.partitionVariants == null || kit.partitionVariants.Length != 512)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id +
                    "\" must contain all 512 partitionVariants."
                );
            }
            if (kit.visualLanes == null || kit.visualLanes.Length == 0)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" has no native visual lanes."
                );
            }
            if (kit.pairedPieces == null)
            {
                throw new InvalidOperationException(
                    "Buildings kit \"" + kit.id + "\" has no paired-piece contract."
                );
            }
        }

        private static void EnsureFolder(string folder)
        {
            var normalized = folder.Replace('\\', '/').TrimEnd('/');
            var pieces = normalized.Split('/');
            var current = pieces[0];
            for (var index = 1; index < pieces.Length; index++)
            {
                var next = current + "/" + pieces[index];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, pieces[index]);
                }
                current = next;
            }
        }

        private static string SafeName(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return "building";
            }
            foreach (var invalid in Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalid, '_');
            }
            return value.Replace('/', '_').Replace('\\', '_');
        }
    }
}
