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

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, "WardPins.Request", out var playerId))
        {
            return;
        }

        var canSeeAllWards = WardAdminDebugAccess.IsPlayerAdminDebugController(playerId);
        var playerGuildId = GuildsCompat.GetPlayerGuildId(playerId);
        var prepared = WardMinimapVisibilityIndex.TryPrepare(ZDOMan.instance, "ward minimap remote request");
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
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Response",
            $"Sent ward minimap snapshot response. requestId={requestId}, receiverUid={receiverUid}, responseKind={responseKind}, viewerRevisionToken={snapshot.ViewerRevisionToken}, playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={snapshot.IndexedWardCount}, candidateWardCount={snapshot.CandidateWardCount}, visibleWardCount={snapshot.VisibleWardCount}, enabledWardCount={snapshot.EnabledWardCount}{DescribeFirstEntry(snapshot.FirstEntry)}");
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
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Push",
            $"Pushed ward minimap {(fullSnapshot ? "full snapshot" : "delta")}. receiverUid={receiverUid}, viewerRevisionToken={viewerRevisionToken}, playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={indexedWardCount}, candidateWardCount={candidateWardCount}, visibleWardCount={visibleWardCount}, enabledWardCount={enabledWardCount}, upsertedWardCount={snapshotEntries.Count}, removedWardCount={removedWardIds.Count}{DescribeFirstEntry(firstEntry)}");
    }

    private static void HandleReceiveWardPins(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Response",
                $"Rejected ward minimap response from a non-server sender. sender={sender}");
            return;
        }

        int requestId;
        WardPinsResponseKind responseKind;
        int viewerRevisionToken;
        long playerId;
        bool canSeeAllWards;
        int indexedWardCount;
        int candidateWardCount;
        int visibleWardCount;
        int enabledWardCount;
        int snapshotCount;
        try
        {
            requestId = pkg.ReadInt();
            responseKind = ReadWardPinsResponseKind(pkg.ReadInt());
            viewerRevisionToken = pkg.ReadInt();
            playerId = pkg.ReadLong();
            canSeeAllWards = pkg.ReadBool();
            indexedWardCount = pkg.ReadInt();
            candidateWardCount = pkg.ReadInt();
            visibleWardCount = pkg.ReadInt();
            enabledWardCount = pkg.ReadInt();
            snapshotCount = pkg.ReadInt();
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure("WardPins.Response", "Failed to deserialize remote ward minimap snapshot header.");
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
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Response",
                $"Ignored remote ward minimap snapshot response because it does not match the current pending request. requestId={requestId}, pendingRequestId={_pendingSnapshotRequestId}");
            return;
        }

        if (responseKind == WardPinsResponseKind.Unavailable)
        {
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Response",
                $"Deferred applying remote ward minimap snapshot because the server could not prepare it yet. requestId={requestId}, playerId={playerId}, canSeeAllWards={canSeeAllWards}");
            return;
        }

        if (responseKind == WardPinsResponseKind.Unchanged)
        {
            if (_snapshotState != ClientSnapshotState.Ready)
            {
                _pendingSnapshotRequestId = 0;
                _lastViewerRevisionToken = 0;
                QueueRemoteSnapshotBootstrapRequest("server reported unchanged before full snapshot was acknowledged");
                Plugin.LogWardDiagnosticVerbose(
                    "WardPins.Response",
                    $"Rejected unchanged remote ward minimap response because the client has not acknowledged a full snapshot yet. requestId={requestId}, snapshotState={_snapshotState}, playerId={playerId}, canSeeAllWards={canSeeAllWards}, serverViewerRevisionToken={viewerRevisionToken}");
                UpdateLocalState(Player.m_localPlayer, force: false);
                return;
            }

            ClearPendingForceRefresh();
            ClearPendingRemoteSnapshotBootstrapRequest();
            _pendingSnapshotRequestId = 0;
            _lastViewerRevisionToken = viewerRevisionToken;
            LogScanSummary(
                $"playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={indexedWardCount}, candidateWardCount={candidateWardCount}, visibleWardCount={visibleWardCount}, enabledWardCount={enabledWardCount}, source=server-unchanged");
            UpdateLocalState(Player.m_localPlayer, force: false);
            return;
        }

        if (!TryReadSnapshotEntries(pkg, snapshotCount, out var snapshotEntries, out var firstEntry))
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Response",
                $"Failed to deserialize remote ward minimap snapshot body. requestId={requestId}, declaredSnapshotCount={snapshotCount}");
            QueueRemoteSnapshotBootstrapRequest("failed to deserialize remote ward minimap snapshot body");
            return;
        }

        ReplaceLocalSnapshot(snapshotEntries);
        ClearPendingForceRefresh();
        ClearPendingRemoteSnapshotBootstrapRequest();
        _pendingSnapshotRequestId = 0;
        _lastViewerRevisionToken = viewerRevisionToken;
        LogScanSummary(
            $"playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={indexedWardCount}, candidateWardCount={candidateWardCount}, visibleWardCount={visibleWardCount}, enabledWardCount={enabledWardCount}, source=server{DescribeFirstEntry(firstEntry)}");
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
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Push",
                $"Rejected ward minimap push from a non-server sender. sender={sender}");
            return;
        }

        bool fullSnapshot;
        int viewerRevisionToken;
        long playerId;
        bool canSeeAllWards;
        int indexedWardCount;
        int candidateWardCount;
        int visibleWardCount;
        int enabledWardCount;
        int snapshotCount;
        int removedWardCount;
        try
        {
            fullSnapshot = pkg.ReadBool();
            viewerRevisionToken = pkg.ReadInt();
            playerId = pkg.ReadLong();
            canSeeAllWards = pkg.ReadBool();
            indexedWardCount = pkg.ReadInt();
            candidateWardCount = pkg.ReadInt();
            visibleWardCount = pkg.ReadInt();
            enabledWardCount = pkg.ReadInt();
            snapshotCount = pkg.ReadInt();
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure("WardPins.Push", "Failed to deserialize pushed ward minimap snapshot header.");
            QueueRemoteSnapshotBootstrapRequest("failed to deserialize pushed ward minimap snapshot header");
            return;
        }

        if (_lastViewerRevisionToken != 0 &&
            viewerRevisionToken != 0 &&
            viewerRevisionToken < _lastViewerRevisionToken)
        {
            return;
        }

        if (!TryReadSnapshotEntries(pkg, snapshotCount, out var snapshotEntries, out var firstEntry))
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Push",
                $"Failed to deserialize pushed ward minimap snapshot body. viewerRevisionToken={viewerRevisionToken}, declaredSnapshotCount={snapshotCount}");
            QueueRemoteSnapshotBootstrapRequest("failed to deserialize pushed ward minimap snapshot body");
            return;
        }

        try
        {
            removedWardCount = pkg.ReadInt();
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Push",
                $"Failed to deserialize pushed ward minimap removed-id count. viewerRevisionToken={viewerRevisionToken}");
            QueueRemoteSnapshotBootstrapRequest("failed to deserialize pushed ward minimap removed-id count");
            return;
        }

        if (!TryReadRemovedWardIds(pkg, removedWardCount, out var removedWardIds))
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Push",
                $"Failed to deserialize pushed ward minimap removed-id body. viewerRevisionToken={viewerRevisionToken}, declaredRemovedCount={removedWardCount}");
            QueueRemoteSnapshotBootstrapRequest("failed to deserialize pushed ward minimap removed-id body");
            return;
        }

        if (!fullSnapshot && _snapshotState != ClientSnapshotState.Ready)
        {
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Push",
                $"Ignored pushed ward minimap delta because the client has not acknowledged a full snapshot yet. snapshotState={_snapshotState}, viewerRevisionToken={viewerRevisionToken}, upsertedWardCount={snapshotEntries.Length}, removedWardCount={removedWardIds.Length}");
            QueueRemoteSnapshotBootstrapRequest("ignored pushed delta before full snapshot was acknowledged");
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
        LogScanSummary(
            $"playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={indexedWardCount}, candidateWardCount={candidateWardCount}, visibleWardCount={visibleWardCount}, enabledWardCount={enabledWardCount}, source={(fullSnapshot ? "server-push-full" : "server-push-delta")}, upsertedWardCount={snapshotEntries.Length}, removedWardCount={removedWardIds.Length}{DescribeFirstEntry(firstEntry)}");
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
        out WardMinimapSnapshotEntry[] snapshotEntries,
        out WardMinimapSnapshotEntry? firstEntry)
    {
        if (snapshotCount < 0 || snapshotCount > MaxSnapshotEntryCount)
        {
            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            firstEntry = null;
            return false;
        }

        snapshotEntries = snapshotCount <= 0
            ? System.Array.Empty<WardMinimapSnapshotEntry>()
            : new WardMinimapSnapshotEntry[snapshotCount];
        firstEntry = null;
        try
        {
            for (var index = 0; index < snapshotEntries.Length; index++)
            {
                snapshotEntries[index] = new WardMinimapSnapshotEntry(
                    pkg.ReadZDOID(),
                    pkg.ReadVector3(),
                    pkg.ReadSingle(),
                    pkg.ReadBool());
                firstEntry ??= snapshotEntries[index];
            }

            return true;
        }
        catch
        {
            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            firstEntry = null;
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
