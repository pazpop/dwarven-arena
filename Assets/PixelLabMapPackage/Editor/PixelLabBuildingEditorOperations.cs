using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PixelLab.MapExport.Editor
{
    public static class PixelLabBuildingEditorOperations
    {
        public static bool PaintFloor(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r,
            bool roof,
            bool autoWalls
        )
        {
            return Mutate(controller, "Paint PixelLab Building Floor", () =>
                controller.PaintFloor(layerY, q, r, roof, autoWalls));
        }

        public static bool PaintPartition(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r
        )
        {
            return Mutate(controller, "Paint PixelLab Building Partition", () =>
                controller.PaintPartition(layerY, q, r));
        }

        public static bool SetAutoWalls(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r,
            bool enabled
        )
        {
            return Mutate(controller, "Change PixelLab Building Auto Walls", () =>
                controller.SetAutoWalls(layerY, q, r, enabled));
        }

        public static bool PaintPillar(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r
        )
        {
            return Mutate(controller, "Paint PixelLab Building Pillar", () =>
                controller.PaintPillar(layerY, q, r));
        }

        public static bool PaintPairedPiece(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r,
            string kind,
            string orientation = null,
            string side = null,
            string axis = null
        )
        {
            return Mutate(controller, "Paint PixelLab Building Paired Piece", () =>
                controller.PaintPairedPiece(
                    layerY,
                    q,
                    r,
                    kind,
                    orientation,
                    side,
                    axis
                ));
        }

        public static bool PaintStamp(
            PixelLabBuildingController controller,
            int layerY,
            int q,
            int r,
            int tileIndex
        )
        {
            return Mutate(controller, "Paint PixelLab Building Stamp", () =>
                controller.PaintStamp(layerY, q, r, tileIndex));
        }

        public static bool Erase(
            PixelLabBuildingController controller,
            string role,
            int layerY,
            int q,
            int r
        )
        {
            return Mutate(controller, "Erase PixelLab Building Cell", () =>
                controller.EraseCell(role, layerY, q, r));
        }

        public static bool AddStorey(PixelLabBuildingController controller, int layerY)
        {
            Require(controller);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Add PixelLab Building Storey Y" + layerY);
            Undo.RegisterCompleteObjectUndo(
                controller.EditState,
                "Add PixelLab Building Storey Y" + layerY
            );
            var existing = ExistingObjectIds(controller);
            if (!controller.AddStorey(layerY))
            {
                Undo.CollapseUndoOperations(group);
                return false;
            }
            RegisterNewRootsForUndo(
                controller,
                existing,
                "Add PixelLab Building Storey Y" + layerY
            );
            MarkDirty(controller);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        public static int ApplyCells(
            PixelLabBuildingController controller,
            string undoName,
            IEnumerable<Vector2Int> cells,
            Func<Vector2Int, bool> mutation
        )
        {
            if (mutation == null)
            {
                throw new ArgumentNullException(nameof(mutation));
            }
            var changed = 0;
            Mutate(controller, undoName, () =>
            {
                using (controller.BeginSemanticBatch())
                {
                    foreach (var cell in cells.Distinct())
                    {
                        if (mutation(cell))
                        {
                            changed++;
                        }
                    }
                }
                return changed > 0;
            });
            return changed;
        }

        private static bool Mutate(
            PixelLabBuildingController controller,
            string undoName,
            Func<bool> mutation
        )
        {
            Require(controller);
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(undoName);
            var existing = ExistingObjectIds(controller);
            Undo.RecordObject(controller.EditState, undoName);
            if (!mutation())
            {
                Undo.CollapseUndoOperations(group);
                return false;
            }
            RegisterNewRootsForUndo(controller, existing, undoName);
            MarkDirty(controller);
            Undo.CollapseUndoOperations(group);
            return true;
        }

        private static HashSet<int> ExistingObjectIds(
            PixelLabBuildingController controller
        )
        {
            return new HashSet<int>(
                controller.GetComponentsInChildren<Transform>(true)
                    .Select(item => item.gameObject.GetEntityId().GetHashCode())
            );
        }

        private static void RegisterNewRootsForUndo(
            PixelLabBuildingController controller,
            HashSet<int> existing,
            string undoName
        )
        {
            foreach (var item in controller.GetComponentsInChildren<Transform>(true))
            {
                var gameObject = item.gameObject;
                if (existing.Contains(gameObject.GetEntityId().GetHashCode()))
                {
                    continue;
                }
                var parent = item.parent;
                if (parent != null &&
                    !existing.Contains(parent.gameObject.GetEntityId().GetHashCode()))
                {
                    continue;
                }
                Undo.RegisterCreatedObjectUndo(gameObject, undoName);
            }
        }

        internal static void MarkDirty(PixelLabBuildingController controller)
        {
            EditorUtility.SetDirty(controller);
            EditorUtility.SetDirty(controller.EditState);
            foreach (var lane in controller.LaneTilemaps)
            {
                if (lane != null)
                {
                    EditorUtility.SetDirty(lane);
                    EditorUtility.SetDirty(lane.Tilemap);
                }
            }
            if (controller.gameObject.scene.IsValid())
            {
                EditorSceneManager.MarkSceneDirty(controller.gameObject.scene);
            }
        }

        private static void Require(PixelLabBuildingController controller)
        {
            if (controller == null || controller.EditState == null)
            {
                throw new InvalidOperationException(
                    "A configured PixelLabBuildingController is required."
                );
            }
        }
    }
}
