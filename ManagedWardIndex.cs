using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal sealed class ManagedWardIndex
{
    private const float SpatialCellSize = 32f;

    private readonly Func<PrivateArea, bool> _isTrackable;
    private readonly List<PrivateArea> _areas = new();
    private readonly HashSet<int> _areaIds = new();
    private readonly Dictionary<long, List<SpatialWardEntry>> _spatialIndex = new();
    private readonly Dictionary<int, List<long>> _spatialCellsByInstanceId = new();
    private readonly Dictionary<int, int> _queryStamps = new();
    private int _queryStamp;

    internal ManagedWardIndex(Func<PrivateArea, bool> isTrackable)
    {
        _isTrackable = isTrackable;
    }

    internal int Count => _areaIds.Count;

    internal IReadOnlyList<PrivateArea> Areas => _areas;

    internal bool Add(PrivateArea area)
    {
        var instanceId = area.GetInstanceID();
        if (!_areaIds.Add(instanceId))
        {
            return false;
        }

        _areas.Add(area);
        return true;
    }

    internal bool Remove(PrivateArea area)
    {
        var instanceId = area.GetInstanceID();
        if (!_areaIds.Remove(instanceId))
        {
            return false;
        }

        _areas.Remove(area);
        return true;
    }

    internal bool Contains(int instanceId)
    {
        return _areaIds.Contains(instanceId);
    }

    internal void Clear()
    {
        _areas.Clear();
        _areaIds.Clear();
        ClearSpatialIndex();
    }

    internal void ClearSpatialIndex()
    {
        _spatialIndex.Clear();
        _spatialCellsByInstanceId.Clear();
        _queryStamps.Clear();
        _queryStamp = 0;
    }

    internal void RebuildSpatialIndex()
    {
        ClearSpatialIndex();
        for (var index = 0; index < _areas.Count; index++)
        {
            var area = _areas[index];
            if (_isTrackable(area))
            {
                AddAreaToSpatialIndex(area);
            }
        }
    }

    internal void UpdateSpatialIndex(PrivateArea area, int instanceId, bool shouldContain)
    {
        RemoveAreaFromSpatialIndex(instanceId);
        if (!shouldContain || !_isTrackable(area))
        {
            return;
        }

        AddAreaToSpatialIndex(area);
    }

    internal void FillCandidates(Vector3 point, float radius, List<PrivateArea> destination)
    {
        destination.Clear();
        if (_spatialIndex.Count == 0)
        {
            return;
        }

        var queryStamp = NextQueryStamp();
        var queryRadius = Mathf.Max(0f, radius);
        var minCellX = GetSpatialCellCoordinate(point.x - queryRadius);
        var maxCellX = GetSpatialCellCoordinate(point.x + queryRadius);
        var minCellZ = GetSpatialCellCoordinate(point.z - queryRadius);
        var maxCellZ = GetSpatialCellCoordinate(point.z + queryRadius);

        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                if (!_spatialIndex.TryGetValue(GetSpatialCellKey(cellX, cellZ), out var entries))
                {
                    continue;
                }

                for (var index = 0; index < entries.Count; index++)
                {
                    var entry = entries[index];
                    if (_queryStamps.TryGetValue(entry.InstanceId, out var seenStamp) && seenStamp == queryStamp)
                    {
                        continue;
                    }

                    _queryStamps[entry.InstanceId] = queryStamp;
                    destination.Add(entry.Area);
                }
            }
        }
    }

    private void AddAreaToSpatialIndex(PrivateArea area)
    {
        var position = area.transform.position;
        var radius = WardSettings.GetRadius(area);
        var entry = new SpatialWardEntry(area);
        var occupiedCells = new List<long>();
        var minCellX = GetSpatialCellCoordinate(position.x - radius);
        var maxCellX = GetSpatialCellCoordinate(position.x + radius);
        var minCellZ = GetSpatialCellCoordinate(position.z - radius);
        var maxCellZ = GetSpatialCellCoordinate(position.z + radius);

        for (var cellX = minCellX; cellX <= maxCellX; cellX++)
        {
            for (var cellZ = minCellZ; cellZ <= maxCellZ; cellZ++)
            {
                var cellKey = GetSpatialCellKey(cellX, cellZ);
                if (!_spatialIndex.TryGetValue(cellKey, out var entries))
                {
                    entries = new List<SpatialWardEntry>();
                    _spatialIndex[cellKey] = entries;
                }

                entries.Add(entry);
                occupiedCells.Add(cellKey);
            }
        }

        _spatialCellsByInstanceId[entry.InstanceId] = occupiedCells;
    }

    private void RemoveAreaFromSpatialIndex(int instanceId)
    {
        if (!_spatialCellsByInstanceId.TryGetValue(instanceId, out var occupiedCells))
        {
            return;
        }

        for (var index = 0; index < occupiedCells.Count; index++)
        {
            var cellKey = occupiedCells[index];
            if (!_spatialIndex.TryGetValue(cellKey, out var entries))
            {
                continue;
            }

            for (var entryIndex = entries.Count - 1; entryIndex >= 0; entryIndex--)
            {
                if (entries[entryIndex].InstanceId == instanceId)
                {
                    entries.RemoveAt(entryIndex);
                }
            }

            if (entries.Count == 0)
            {
                _spatialIndex.Remove(cellKey);
            }
        }

        _spatialCellsByInstanceId.Remove(instanceId);
    }

    private static int GetSpatialCellCoordinate(float coordinate)
    {
        return Mathf.FloorToInt(coordinate / SpatialCellSize);
    }

    private static long GetSpatialCellKey(int cellX, int cellZ)
    {
        return ((long)cellX << 32) ^ (uint)cellZ;
    }

    private int NextQueryStamp()
    {
        if (_queryStamp == int.MaxValue)
        {
            _queryStamp = 1;
            _queryStamps.Clear();
            return _queryStamp;
        }

        _queryStamp++;
        return _queryStamp;
    }

    private sealed class SpatialWardEntry
    {
        internal SpatialWardEntry(PrivateArea area)
        {
            Area = area;
            InstanceId = area.GetInstanceID();
        }

        internal PrivateArea Area { get; }

        internal int InstanceId { get; }
    }
}
