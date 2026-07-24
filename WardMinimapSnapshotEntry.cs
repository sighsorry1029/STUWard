using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal readonly struct WardMinimapSnapshotEntry
{
    internal WardMinimapSnapshotEntry(ZDOID zdoId, Vector3 position, float radius, bool isEnabled)
    {
        ZdoId = zdoId;
        Position = position;
        Radius = radius;
        IsEnabled = isEnabled;
    }

    internal ZDOID ZdoId { get; }
    internal Vector3 Position { get; }
    internal float Radius { get; }
    internal bool IsEnabled { get; }
}

internal readonly struct WardMinimapViewerSnapshot
{
    private static readonly IReadOnlyDictionary<ZDOID, uint> EmptyVisibleWardDataRevisions =
        new Dictionary<ZDOID, uint>(0);

    internal WardMinimapViewerSnapshot(
        int viewerRevisionToken,
        int indexedWardCount,
        int candidateWardCount,
        int visibleWardCount,
        int enabledWardCount,
        IReadOnlyList<WardMinimapSnapshotEntry>? entries,
        IReadOnlyDictionary<ZDOID, uint>? visibleWardDataRevisions)
    {
        ViewerRevisionToken = viewerRevisionToken;
        IndexedWardCount = indexedWardCount;
        CandidateWardCount = candidateWardCount;
        VisibleWardCount = visibleWardCount;
        EnabledWardCount = enabledWardCount;
        Entries = entries ?? Array.Empty<WardMinimapSnapshotEntry>();
        VisibleWardDataRevisions = visibleWardDataRevisions ?? EmptyVisibleWardDataRevisions;
    }

    internal static WardMinimapViewerSnapshot Empty { get; } = new(
        0,
        0,
        0,
        0,
        0,
        Array.Empty<WardMinimapSnapshotEntry>(),
        EmptyVisibleWardDataRevisions);

    internal int ViewerRevisionToken { get; }
    internal int IndexedWardCount { get; }
    internal int CandidateWardCount { get; }
    internal int VisibleWardCount { get; }
    internal int EnabledWardCount { get; }
    internal IReadOnlyList<WardMinimapSnapshotEntry> Entries { get; }
    internal IReadOnlyDictionary<ZDOID, uint> VisibleWardDataRevisions { get; }
}

internal static class WardMinimapViewerSnapshotBuilder
{
    internal static WardMinimapViewerSnapshot Build(
        long playerId,
        int playerGuildId,
        bool canSeeAllWards,
        int viewerRevisionToken,
        bool includeEntries,
        bool includeVisibleWardDataRevisions)
    {
        var visibleWardIds = WardMinimapVisibilityIndex.GetVisibleCandidateWardIds(
            playerId,
            playerGuildId,
            canSeeAllWards);
        var indexedWardCount = WardMinimapVisibilityIndex.GetIndexedWardCount();
        var candidateWardCount = visibleWardIds.Length;
        var visibleWardCount = 0;
        var enabledWardCount = 0;
        List<WardMinimapSnapshotEntry>? entries = includeEntries && visibleWardIds.Length > 0
            ? new List<WardMinimapSnapshotEntry>(visibleWardIds.Length)
            : null;
        Dictionary<ZDOID, uint>? visibleWardDataRevisions = includeVisibleWardDataRevisions && visibleWardIds.Length > 0
            ? new Dictionary<ZDOID, uint>(visibleWardIds.Length)
            : null;

        for (var index = 0; index < visibleWardIds.Length; index++)
        {
            var wardId = visibleWardIds[index];
            if (!WardMinimapVisibilityIndex.TryGetEntry(wardId, out var entry))
            {
                continue;
            }

            var snapshotEntry = new WardMinimapSnapshotEntry(
                entry.ZdoId,
                entry.Position,
                entry.Radius,
                entry.IsEnabled);
            visibleWardCount++;
            if (snapshotEntry.IsEnabled)
            {
                enabledWardCount++;
            }

            entries?.Add(snapshotEntry);
            if (visibleWardDataRevisions != null)
            {
                visibleWardDataRevisions[snapshotEntry.ZdoId] =
                    WardMinimapVisibilityIndex.TryGetDataRevision(snapshotEntry.ZdoId, out var dataRevision)
                        ? dataRevision
                        : 0u;
            }
        }

        return new WardMinimapViewerSnapshot(
            viewerRevisionToken,
            indexedWardCount,
            candidateWardCount,
            visibleWardCount,
            enabledWardCount,
            entries == null || entries.Count == 0 ? Array.Empty<WardMinimapSnapshotEntry>() : entries,
            visibleWardDataRevisions);
    }
}
