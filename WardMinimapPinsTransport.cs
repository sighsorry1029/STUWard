using System.Collections.Generic;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    private const string RequestWardPinsRpc = "STUWard_RequestWardPins";
    private const string ReceiveWardPinsRpc = "STUWard_ReceiveWardPins";
    private const string PushWardPinsRpc = "STUWard_PushWardPins";
    private const int MaxSnapshotEntryCount = 16384;

    private enum WardPinsResponseKind
    {
        Unavailable = 0,
        FullSnapshot = 1,
        Unchanged = 2
    }

    private static bool _rpcsRegistered;

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register<ZPackage>(RequestWardPinsRpc, HandleRequestWardPins);
        routedRpc.Register<ZPackage>(ReceiveWardPinsRpc, HandleReceiveWardPins);
        routedRpc.Register<ZPackage>(PushWardPinsRpc, HandlePushWardPins);
        _rpcsRegistered = true;
    }

    private static void HandleRequestWardPins(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var requestId = 0;
        var knownViewerRevisionToken = 0;
        var requestFullSnapshot = true;
        try
        {
            requestId = pkg?.ReadInt() ?? 0;
        }
        catch
        {
            requestId = 0;
        }

        if (requestId > 0 && pkg != null)
        {
            try
            {
                knownViewerRevisionToken = pkg.ReadInt();
            }
            catch
            {
                knownViewerRevisionToken = 0;
            }

            requestFullSnapshot = knownViewerRevisionToken == 0;
            try
            {
                requestFullSnapshot = pkg.ReadBool();
            }
            catch
            {
                // Older clients only send the revision token. Treat token 0 as a full snapshot request.
            }
        }

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out var playerId))
        {
            return;
        }

        var canSeeAllWards = WardAdminDebugAccess.IsPlayerAdminDebugController(playerId);
        var playerGuildId = GuildsCompat.GetPlayerGuildId(playerId);
        var prepared = WardMinimapVisibilityIndex.TryPrepare(ZDOMan.instance);
        var responseKind = WardPinsResponseKind.Unavailable;
        var snapshot = WardMinimapViewerSnapshot.Empty;
        if (prepared)
        {
            var viewerRevisionToken = WardMinimapVisibilityIndex.GetViewerRevisionToken(
                playerId,
                playerGuildId,
                canSeeAllWards);
            var includeEntries = requestFullSnapshot ||
                                 knownViewerRevisionToken == 0 ||
                                 viewerRevisionToken != knownViewerRevisionToken;
            snapshot = WardMinimapViewerSnapshotBuilder.Build(
                playerId,
                playerGuildId,
                canSeeAllWards,
                viewerRevisionToken,
                includeEntries,
                includeVisibleWardDataRevisions: true);
            responseKind = includeEntries ? WardPinsResponseKind.FullSnapshot : WardPinsResponseKind.Unchanged;
            TrackServerViewerSyncState(sender, snapshot);
        }

        SendWardPinsResponse(
            sender,
            requestId,
            responseKind,
            playerId,
            canSeeAllWards,
            snapshot);
    }

    private static void SendWardPinsResponse(
        long receiverUid,
        int requestId,
        WardPinsResponseKind responseKind,
        long playerId,
        bool canSeeAllWards,
        WardMinimapViewerSnapshot snapshot)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || receiverUid == 0L)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(requestId);
        pkg.Write((int)responseKind);
        pkg.Write(snapshot.ViewerRevisionToken);
        pkg.Write(playerId);
        pkg.Write(canSeeAllWards);
        pkg.Write(snapshot.IndexedWardCount);
        pkg.Write(snapshot.CandidateWardCount);
        pkg.Write(snapshot.VisibleWardCount);
        pkg.Write(snapshot.EnabledWardCount);
        pkg.Write(snapshot.Entries.Count);
        WriteSnapshotEntries(pkg, snapshot.Entries);

        routedRpc.InvokeRoutedRPC(receiverUid, ReceiveWardPinsRpc, pkg);
    }

    private static void SendWardPinsPush(
        long receiverUid,
        bool fullSnapshot,
        int viewerRevisionToken,
        long playerId,
        bool canSeeAllWards,
        int indexedWardCount,
        int candidateWardCount,
        int visibleWardCount,
        int enabledWardCount,
        IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries,
        IReadOnlyList<ZDOID> removedWardIds,
        WardMinimapSnapshotEntry? firstEntry)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || receiverUid == 0L)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(fullSnapshot);
        pkg.Write(viewerRevisionToken);
        pkg.Write(playerId);
        pkg.Write(canSeeAllWards);
        pkg.Write(indexedWardCount);
        pkg.Write(candidateWardCount);
        pkg.Write(visibleWardCount);
        pkg.Write(enabledWardCount);
        pkg.Write(snapshotEntries.Count);
        WriteSnapshotEntries(pkg, snapshotEntries);
        pkg.Write(removedWardIds.Count);
        WriteRemovedWardIds(pkg, removedWardIds);
        routedRpc.InvokeRoutedRPC(receiverUid, PushWardPinsRpc, pkg);
    }

    private static void HandleReceiveWardPins(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            return;
        }

        int requestId;
        WardPinsResponseKind responseKind;
        int viewerRevisionToken;
        int snapshotCount;
        try
        {
            requestId = pkg.ReadInt();
            responseKind = ReadWardPinsResponseKind(pkg.ReadInt());
            viewerRevisionToken = pkg.ReadInt();
            _ = pkg.ReadLong();
            _ = pkg.ReadBool();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            snapshotCount = pkg.ReadInt();
        }
        catch
        {
            return;
        }

        if (requestId <= 0)
        {
            return;
        }

        if (_lastViewerRevisionToken != 0 &&
            viewerRevisionToken != 0 &&
            viewerRevisionToken < _lastViewerRevisionToken)
        {
            return;
        }

        if (_pendingSnapshotRequestId == 0 || requestId != _pendingSnapshotRequestId)
        {
            return;
        }

        if (responseKind == WardPinsResponseKind.Unavailable)
        {
            return;
        }

        if (responseKind == WardPinsResponseKind.Unchanged)
        {
            if (_snapshotState != ClientSnapshotState.Ready)
            {
                _pendingSnapshotRequestId = 0;
                _lastViewerRevisionToken = 0;
                QueueRemoteSnapshotBootstrapRequest();
                UpdateLocalState(Player.m_localPlayer, force: false);
                return;
            }

            ClearPendingForceRefresh();
            ClearPendingRemoteSnapshotBootstrapRequest();
            _pendingSnapshotRequestId = 0;
            _lastViewerRevisionToken = viewerRevisionToken;
            UpdateLocalState(Player.m_localPlayer, force: false);
            return;
        }

        if (!TryReadSnapshotEntries(pkg, snapshotCount, out var snapshotEntries))
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        ReplaceLocalSnapshot(snapshotEntries);
        ClearPendingForceRefresh();
        ClearPendingRemoteSnapshotBootstrapRequest();
        _pendingSnapshotRequestId = 0;
        _lastViewerRevisionToken = viewerRevisionToken;
        UpdateLocalState(Player.m_localPlayer, force: false);
    }

    private static WardPinsResponseKind ReadWardPinsResponseKind(int rawValue)
    {
        return rawValue switch
        {
            (int)WardPinsResponseKind.Unavailable => WardPinsResponseKind.Unavailable,
            (int)WardPinsResponseKind.FullSnapshot => WardPinsResponseKind.FullSnapshot,
            (int)WardPinsResponseKind.Unchanged => WardPinsResponseKind.Unchanged,
            _ => WardPinsResponseKind.Unavailable
        };
    }

    private static void HandlePushWardPins(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            return;
        }

        bool fullSnapshot;
        int viewerRevisionToken;
        int snapshotCount;
        int removedWardCount;
        try
        {
            fullSnapshot = pkg.ReadBool();
            viewerRevisionToken = pkg.ReadInt();
            _ = pkg.ReadLong();
            _ = pkg.ReadBool();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            snapshotCount = pkg.ReadInt();
        }
        catch
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (_lastViewerRevisionToken != 0 &&
            viewerRevisionToken != 0 &&
            viewerRevisionToken < _lastViewerRevisionToken)
        {
            return;
        }

        if (!TryReadSnapshotEntries(pkg, snapshotCount, out var snapshotEntries))
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        try
        {
            removedWardCount = pkg.ReadInt();
        }
        catch
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (!TryReadRemovedWardIds(pkg, removedWardCount, out var removedWardIds))
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (!fullSnapshot && _snapshotState != ClientSnapshotState.Ready)
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (fullSnapshot)
        {
            ReplaceLocalSnapshot(snapshotEntries);
        }
        else
        {
            ApplyLocalSnapshotDelta(snapshotEntries, removedWardIds);
        }

        ClearPendingForceRefresh();
        ClearPendingRemoteSnapshotBootstrapRequest();
        _pendingSnapshotRequestId = 0;
        _lastViewerRevisionToken = viewerRevisionToken;
        UpdateLocalState(Player.m_localPlayer, force: false);
    }

    private static void WriteSnapshotEntries(ZPackage pkg, IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries)
    {
        for (var index = 0; index < snapshotEntries.Count; index++)
        {
            var entry = snapshotEntries[index];
            pkg.Write(entry.ZdoId);
            pkg.Write(entry.Position);
            pkg.Write(entry.Radius);
            pkg.Write(entry.IsEnabled);
        }
    }

    private static void WriteRemovedWardIds(ZPackage pkg, IReadOnlyList<ZDOID> removedWardIds)
    {
        for (var index = 0; index < removedWardIds.Count; index++)
        {
            pkg.Write(removedWardIds[index]);
        }
    }

    private static bool TryReadSnapshotEntries(
        ZPackage pkg,
        int snapshotCount,
        out WardMinimapSnapshotEntry[] snapshotEntries)
    {
        if (snapshotCount < 0 || snapshotCount > MaxSnapshotEntryCount)
        {
            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            return false;
        }

        snapshotEntries = snapshotCount <= 0
            ? System.Array.Empty<WardMinimapSnapshotEntry>()
            : new WardMinimapSnapshotEntry[snapshotCount];
        try
        {
            for (var index = 0; index < snapshotEntries.Length; index++)
            {
                snapshotEntries[index] = new WardMinimapSnapshotEntry(
                    pkg.ReadZDOID(),
                    pkg.ReadVector3(),
                    pkg.ReadSingle(),
                    pkg.ReadBool());
            }

            return true;
        }
        catch
        {
            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            return false;
        }
    }

    private static bool TryReadRemovedWardIds(ZPackage pkg, int removedWardCount, out ZDOID[] removedWardIds)
    {
        if (removedWardCount < 0 || removedWardCount > MaxSnapshotEntryCount)
        {
            removedWardIds = System.Array.Empty<ZDOID>();
            return false;
        }

        removedWardIds = removedWardCount <= 0
            ? System.Array.Empty<ZDOID>()
            : new ZDOID[removedWardCount];
        try
        {
            for (var index = 0; index < removedWardIds.Length; index++)
            {
                removedWardIds[index] = pkg.ReadZDOID();
            }

            return true;
        }
        catch
        {
            removedWardIds = System.Array.Empty<ZDOID>();
            return false;
        }
    }

}
