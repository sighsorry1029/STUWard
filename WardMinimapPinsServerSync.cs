using System;
using System.Collections.Generic;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    private static readonly TimeSpan ServerViewerRefreshDebounce = TimeSpan.FromMilliseconds(150);
    private static readonly Dictionary<long, ServerViewerSyncState> ServerViewerSyncStatesByPeerUid = new();
    private static readonly HashSet<long> PendingServerViewerRefreshPeerUids = new();

    private sealed class ServerViewerSyncState
    {
        internal readonly Dictionary<ZDOID, uint> VisibleWardDataRevisions = new();
        internal int ViewerRevisionToken;
        internal bool HasSentFullSnapshot;
    }

    private static bool _pendingServerViewerRefreshForAll;
    private static DateTime _serverViewerRefreshFlushAtUtc = DateTime.MinValue;

    internal static void QueueServerViewerRefreshRecipients(HashSet<long>? recipientPeerUids)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        if (recipientPeerUids == null)
        {
            _pendingServerViewerRefreshForAll = true;
            PendingServerViewerRefreshPeerUids.Clear();
        }
        else if (!_pendingServerViewerRefreshForAll)
        {
            foreach (var recipientPeerUid in recipientPeerUids)
            {
                if (recipientPeerUid != 0L)
                {
                    PendingServerViewerRefreshPeerUids.Add(recipientPeerUid);
                }
            }
        }

        if (_serverViewerRefreshFlushAtUtc == DateTime.MinValue)
        {
            _serverViewerRefreshFlushAtUtc = DateTime.UtcNow + ServerViewerRefreshDebounce;
        }
    }

    private static void ProcessPendingServerViewerRefreshes()
    {
        if ((!_pendingServerViewerRefreshForAll && PendingServerViewerRefreshPeerUids.Count == 0) ||
            ZNet.instance == null ||
            !ZNet.instance.IsServer())
        {
            return;
        }

        if (_serverViewerRefreshFlushAtUtc > DateTime.UtcNow)
        {
            return;
        }

        var peers = ZNet.instance.GetPeers();
        var livePeerUids = new HashSet<long>();
        var activePlayerIds = new HashSet<long>();
        if (peers != null)
        {
            for (var index = 0; index < peers.Count; index++)
            {
                var peer = peers[index];
                if (peer != null && peer.m_uid != 0L)
                {
                    livePeerUids.Add(peer.m_uid);
                    var playerId = WardOwnership.GetPlayerIdFromSender(peer.m_uid);
                    if (playerId != 0L)
                    {
                        activePlayerIds.Add(playerId);
                    }
                }
            }
        }

        AddActivePlayerIds(activePlayerIds);
        PruneServerViewerSyncStates(livePeerUids);
        if (activePlayerIds.Count > 0 || livePeerUids.Count == 0)
        {
            WardMinimapVisibilityIndex.PruneViewerCaches(activePlayerIds);
        }

        var targetPeerUids = new List<long>();
        if (_pendingServerViewerRefreshForAll)
        {
            targetPeerUids.AddRange(livePeerUids);
        }
        else
        {
            foreach (var pendingPeerUid in PendingServerViewerRefreshPeerUids)
            {
                if (livePeerUids.Contains(pendingPeerUid))
                {
                    targetPeerUids.Add(pendingPeerUid);
                }
            }
        }

        _pendingServerViewerRefreshForAll = false;
        PendingServerViewerRefreshPeerUids.Clear();
        _serverViewerRefreshFlushAtUtc = DateTime.MinValue;

        if (targetPeerUids.Count == 0 || !WardMinimapVisibilityIndex.TryPrepare(ZDOMan.instance))
        {
            return;
        }

        for (var index = 0; index < targetPeerUids.Count; index++)
        {
            PushWardPinsUpdateToPeer(targetPeerUids[index]);
        }
    }

    private static void AddActivePlayerIds(HashSet<long> activePlayerIds)
    {
        var players = Player.GetAllPlayers();
        if (players == null || players.Count == 0)
        {
            return;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            if (player == null)
            {
                continue;
            }

            var playerId = player.GetPlayerID();
            if (playerId != 0L)
            {
                activePlayerIds.Add(playerId);
            }
        }
    }

    private static void PruneServerViewerSyncStates(HashSet<long> livePeerUids)
    {
        List<long>? stalePeerUids = null;
        foreach (var syncedViewer in ServerViewerSyncStatesByPeerUid)
        {
            if (livePeerUids.Contains(syncedViewer.Key))
            {
                continue;
            }

            stalePeerUids ??= new List<long>();
            stalePeerUids.Add(syncedViewer.Key);
        }

        if (stalePeerUids == null)
        {
            return;
        }

        for (var index = 0; index < stalePeerUids.Count; index++)
        {
            ServerViewerSyncStatesByPeerUid.Remove(stalePeerUids[index]);
        }
    }

    private static void PushWardPinsUpdateToPeer(long receiverUid)
    {
        if (!TryBuildServerViewerSyncUpdate(
                receiverUid,
                out var fullSnapshot,
                out var viewerRevisionToken,
                out var playerId,
                out var canSeeAllWards,
                out var indexedWardCount,
                out var candidateWardCount,
                out var visibleWardCount,
                out var enabledWardCount,
                out var snapshotEntries,
                out var removedWardIds,
                out var firstEntry))
        {
            return;
        }

        SendWardPinsPush(
            receiverUid,
            fullSnapshot,
            viewerRevisionToken,
            playerId,
            canSeeAllWards,
            indexedWardCount,
            candidateWardCount,
            visibleWardCount,
            enabledWardCount,
            snapshotEntries,
            removedWardIds,
            firstEntry);
    }

    private static bool TryBuildServerViewerSyncUpdate(
        long receiverUid,
        out bool fullSnapshot,
        out int viewerRevisionToken,
        out long playerId,
        out bool canSeeAllWards,
        out int indexedWardCount,
        out int candidateWardCount,
        out int visibleWardCount,
        out int enabledWardCount,
        out IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries,
        out IReadOnlyList<ZDOID> removedWardIds,
        out WardMinimapSnapshotEntry? firstEntry)
    {
        fullSnapshot = false;
        viewerRevisionToken = 0;
        playerId = 0L;
        canSeeAllWards = false;
        indexedWardCount = 0;
        candidateWardCount = 0;
        visibleWardCount = 0;
        enabledWardCount = 0;
        snapshotEntries = Array.Empty<WardMinimapSnapshotEntry>();
        removedWardIds = Array.Empty<ZDOID>();
        firstEntry = null;

        playerId = WardOwnership.GetPlayerIdFromSender(receiverUid);
        if (playerId == 0L)
        {
            return false;
        }

        canSeeAllWards = playerId != 0L && WardAdminDebugAccess.IsPlayerAdminDebugController(playerId);
        var playerGuildId = GuildsCompat.GetPlayerGuildId(playerId);
        viewerRevisionToken = WardMinimapVisibilityIndex.GetViewerRevisionToken(playerId, playerGuildId, canSeeAllWards);
        var snapshot = WardMinimapViewerSnapshotBuilder.Build(
            playerId,
            playerGuildId,
            canSeeAllWards,
            viewerRevisionToken,
            includeEntries: true,
            includeVisibleWardDataRevisions: true);
        indexedWardCount = snapshot.IndexedWardCount;
        candidateWardCount = snapshot.CandidateWardCount;
        visibleWardCount = snapshot.VisibleWardCount;
        enabledWardCount = snapshot.EnabledWardCount;
        firstEntry = snapshot.FirstEntry;

        var syncState = GetOrCreateServerViewerSyncState(receiverUid);
        if (syncState.HasSentFullSnapshot && syncState.ViewerRevisionToken == viewerRevisionToken)
        {
            return false;
        }

        List<WardMinimapSnapshotEntry>? changedEntries = null;
        for (var index = 0; index < snapshot.Entries.Count; index++)
        {
            var snapshotEntry = snapshot.Entries[index];
            var dataRevision = snapshot.VisibleWardDataRevisions.TryGetValue(snapshotEntry.ZdoId, out var indexedRevision)
                ? indexedRevision
                : 0u;
            if (!syncState.HasSentFullSnapshot ||
                !syncState.VisibleWardDataRevisions.TryGetValue(snapshotEntry.ZdoId, out var previousRevision) ||
                previousRevision != dataRevision)
            {
                changedEntries ??= new List<WardMinimapSnapshotEntry>();
                changedEntries.Add(snapshotEntry);
            }
        }

        List<ZDOID>? removedIds = null;
        if (syncState.HasSentFullSnapshot && syncState.VisibleWardDataRevisions.Count > 0)
        {
            foreach (var previousWard in syncState.VisibleWardDataRevisions)
            {
                if (snapshot.VisibleWardDataRevisions.ContainsKey(previousWard.Key))
                {
                    continue;
                }

                removedIds ??= new List<ZDOID>();
                removedIds.Add(previousWard.Key);
            }
        }

        var changedEntryCount = changedEntries?.Count ?? 0;
        var removedEntryCount = removedIds?.Count ?? 0;
        fullSnapshot = !syncState.HasSentFullSnapshot ||
                       ShouldSendFullSnapshot(
                           changedEntryCount,
                           removedEntryCount,
                           snapshot.VisibleWardCount);
        if (!fullSnapshot && changedEntryCount == 0 && removedEntryCount == 0)
        {
            UpdateServerViewerSyncState(syncState, viewerRevisionToken, snapshot.VisibleWardDataRevisions);
            return false;
        }

        snapshotEntries = fullSnapshot
            ? snapshot.Entries
            : (changedEntries == null || changedEntries.Count == 0
                ? Array.Empty<WardMinimapSnapshotEntry>()
                : changedEntries);
        removedWardIds = fullSnapshot || removedIds == null || removedIds.Count == 0
            ? Array.Empty<ZDOID>()
            : removedIds;

        UpdateServerViewerSyncState(syncState, viewerRevisionToken, snapshot.VisibleWardDataRevisions);
        return true;
    }

    private static ServerViewerSyncState GetOrCreateServerViewerSyncState(long receiverUid)
    {
        if (!ServerViewerSyncStatesByPeerUid.TryGetValue(receiverUid, out var syncState))
        {
            syncState = new ServerViewerSyncState();
            ServerViewerSyncStatesByPeerUid[receiverUid] = syncState;
        }

        return syncState;
    }

    private static void UpdateServerViewerSyncState(
        ServerViewerSyncState syncState,
        int viewerRevisionToken,
        IReadOnlyDictionary<ZDOID, uint> currentVisibleWardRevisions)
    {
        syncState.VisibleWardDataRevisions.Clear();
        foreach (var visibleWardRevision in currentVisibleWardRevisions)
        {
            syncState.VisibleWardDataRevisions[visibleWardRevision.Key] = visibleWardRevision.Value;
        }

        syncState.ViewerRevisionToken = viewerRevisionToken;
        syncState.HasSentFullSnapshot = true;
    }

    private static bool ShouldSendFullSnapshot(int changedEntryCount, int removedEntryCount, int visibleWardCount)
    {
        if (visibleWardCount <= 0)
        {
            return removedEntryCount > 0;
        }

        return changedEntryCount + removedEntryCount >= visibleWardCount;
    }

    private static void TrackServerViewerSyncState(long receiverUid, WardMinimapViewerSnapshot snapshot)
    {
        if (receiverUid == 0L || snapshot.ViewerRevisionToken == 0)
        {
            return;
        }

        UpdateServerViewerSyncState(
            GetOrCreateServerViewerSyncState(receiverUid),
            snapshot.ViewerRevisionToken,
            snapshot.VisibleWardDataRevisions);
    }
}
