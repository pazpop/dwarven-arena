using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace PixelLab.MapExport
{
    public static class PixelLabBuildingRoles
    {
        public const string Floor = "floor";
        public const string Partition = "partition";
        public const string Structure = "structure";
        public const string Stamp = "stamp";

        public static void Require(string role)
        {
            if (role != Floor && role != Partition && role != Structure && role != Stamp)
            {
                throw new ArgumentException(
                    "PixelLab Buildings does not define logical role \"" + role + "\".",
                    nameof(role)
                );
            }
        }
    }

    [Serializable]
    public sealed class PixelLabBuildingCellAddress : IEquatable<PixelLabBuildingCellAddress>
    {
        public int q;
        public int r;
        public int layerY;

        public PixelLabBuildingCellAddress()
        {
        }

        public PixelLabBuildingCellAddress(int qValue, int rValue, int layerYValue)
        {
            q = qValue;
            r = rValue;
            layerY = layerYValue;
        }

        public bool Equals(PixelLabBuildingCellAddress other)
        {
            return other != null && q == other.q && r == other.r && layerY == other.layerY;
        }

        public override bool Equals(object value)
        {
            return Equals(value as PixelLabBuildingCellAddress);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = 17;
                hash = hash * 31 + q;
                hash = hash * 31 + r;
                hash = hash * 31 + layerY;
                return hash;
            }
        }
    }

    [Serializable]
    public sealed class PixelLabBuildingLogicalCellState
    {
        public string role;
        public int q;
        public int r;
        public int layerY;
        public int tileIndex;

        public PixelLabBuildingLogicalCellState Copy()
        {
            return new PixelLabBuildingLogicalCellState
            {
                role = role,
                q = q,
                r = r,
                layerY = layerY,
                tileIndex = tileIndex,
            };
        }
    }

    [Serializable]
    public sealed class PixelLabBuildingStoreyState
    {
        public int layerY;
        public List<PixelLabBuildingLogicalCellState> cells =
            new List<PixelLabBuildingLogicalCellState>();

        public PixelLabBuildingLogicalCellState Find(string role, int q, int r)
        {
            for (var index = 0; index < cells.Count; index++)
            {
                var cell = cells[index];
                if (cell.role == role && cell.q == q && cell.r == r)
                {
                    return cell;
                }
            }
            return null;
        }

        public void Set(string role, int q, int r, int tileIndex)
        {
            var cell = Find(role, q, r);
            if (cell == null)
            {
                cells.Add(new PixelLabBuildingLogicalCellState
                {
                    role = role,
                    q = q,
                    r = r,
                    layerY = layerY,
                    tileIndex = tileIndex,
                });
                return;
            }
            cell.tileIndex = tileIndex;
        }

        public bool Remove(string role, int q, int r)
        {
            for (var index = cells.Count - 1; index >= 0; index--)
            {
                var cell = cells[index];
                if (cell.role == role && cell.q == q && cell.r == r)
                {
                    cells.RemoveAt(index);
                    return true;
                }
            }
            return false;
        }
    }

    public sealed class PixelLabBuildingEditState : ScriptableObject
    {
        [SerializeField] private string mapId;
        [SerializeField] private string kitId;
        [SerializeField] private string sourceFingerprint;
        [SerializeField] private bool hasUserEdits;
        [SerializeField] private List<PixelLabBuildingStoreyState> storeys =
            new List<PixelLabBuildingStoreyState>();

        public string MapId => mapId;
        public string KitId => kitId;
        public string SourceFingerprint => sourceFingerprint;
        public bool HasUserEdits => hasUserEdits;
        public bool Edited => hasUserEdits;
        public IReadOnlyList<PixelLabBuildingStoreyState> Storeys => storeys;

        public bool RequiresExplicitReplacement(string incomingFingerprint)
        {
            return hasUserEdits && sourceFingerprint != incomingFingerprint;
        }

        public static string OwnerKey(string role, int q, int r, int layerY)
        {
            PixelLabBuildingRoles.Require(role);
            return role + ":" + q + "," + r + "," + layerY;
        }

        public void InitializeFromManifest(
            string mapIdentifier,
            PixelLabBuildingKit kit,
            string fingerprint
        )
        {
            if (kit == null)
            {
                throw new ArgumentNullException(nameof(kit));
            }
            if (string.IsNullOrEmpty(kit.id))
            {
                throw new InvalidOperationException("A Buildings kit is missing its id.");
            }

            mapId = mapIdentifier;
            kitId = kit.id;
            sourceFingerprint = fingerprint;
            hasUserEdits = false;
            storeys.Clear();

            foreach (var source in kit.storeys ?? Array.Empty<PixelLabBuildingStorey>())
            {
                var target = EnsureStorey(source.layerY, false);
                if (source.logical == null)
                {
                    throw new InvalidOperationException(
                        "Buildings kit \"" + kit.id + "\" has no logical data for Y" +
                        source.layerY + "."
                    );
                }
                CopyCells(target, PixelLabBuildingRoles.Floor, source.logical.floor);
                CopyCells(target, PixelLabBuildingRoles.Partition, source.logical.partition);
                CopyCells(target, PixelLabBuildingRoles.Structure, source.logical.structure);
                CopyCells(target, PixelLabBuildingRoles.Stamp, source.logical.stamp);
            }

        }

        public PixelLabBuildingStoreyState EnsureStorey(int layerY, bool userEdit = true)
        {
            var storey = FindStorey(layerY);
            if (storey != null)
            {
                return storey;
            }
            storey = new PixelLabBuildingStoreyState { layerY = layerY };
            storeys.Add(storey);
            storeys.Sort((left, right) => left.layerY.CompareTo(right.layerY));
            if (userEdit)
            {
                hasUserEdits = true;
            }
            return storey;
        }

        public PixelLabBuildingStoreyState FindStorey(int layerY)
        {
            for (var index = 0; index < storeys.Count; index++)
            {
                if (storeys[index].layerY == layerY)
                {
                    return storeys[index];
                }
            }
            return null;
        }

        public PixelLabBuildingLogicalCellState FindCell(
            string role,
            int layerY,
            int q,
            int r
        )
        {
            PixelLabBuildingRoles.Require(role);
            var storey = FindStorey(layerY);
            return storey == null ? null : storey.Find(role, q, r);
        }

        public bool HasCell(string role, int layerY, int q, int r)
        {
            return FindCell(role, layerY, q, r) != null;
        }

        public void SetSemanticCell(string role, int layerY, int q, int r, int tileIndex)
        {
            PixelLabBuildingRoles.Require(role);
            EnsureStorey(layerY).Set(role, q, r, tileIndex);
            hasUserEdits = true;
        }

        public void SetResolvedCell(string role, int layerY, int q, int r, int tileIndex)
        {
            PixelLabBuildingRoles.Require(role);
            EnsureStorey(layerY, false).Set(role, q, r, tileIndex);
        }

        public bool RemoveSemanticCell(string role, int layerY, int q, int r)
        {
            PixelLabBuildingRoles.Require(role);
            var storey = FindStorey(layerY);
            if (storey == null || !storey.Remove(role, q, r))
            {
                return false;
            }
            hasUserEdits = true;
            return true;
        }

        public void RemoveResolvedCell(string role, int layerY, int q, int r)
        {
            var storey = FindStorey(layerY);
            if (storey != null)
            {
                storey.Remove(role, q, r);
            }
        }

        private static void CopyCells(
            PixelLabBuildingStoreyState target,
            string role,
            PixelLabCell[] source
        )
        {
            foreach (var cell in source ?? Array.Empty<PixelLabCell>())
            {
                if (cell.layerY != target.layerY)
                {
                    throw new InvalidOperationException(
                        "A Buildings logical cell was assigned to the wrong signed storey."
                    );
                }
                target.Set(role, cell.q, cell.r, cell.tileIndex);
            }
        }
    }
}
