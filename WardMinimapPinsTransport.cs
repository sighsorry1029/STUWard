using System.Collections.Generic;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    private const string RequestWardPinsRpc = "STUWard_RequestWardPins";
    private const string ReceiveWardPinsRpc = "STUWard_ReceiveWardPins";
    private const string PushWardPinsRpc = "STUWard_PushWardPins";

    private enum WardPinsResponseKind
    {
        Unavailable = 0,
        FullSnapshot = 1,
        Unchanged = 2,
        TooLarge = 3
    }

    private enum WardPinsPushKind
    {
        FullSnapshot = 0,
        Delta = 1,
        TooLarge = 2
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

        int requestId;
        int knownViewerRevisionToken;
        bool requestFullSnapshot;
        try
        {
            requestId = pkg?.ReadInt() ?? 0;
            knownViewerRevisionToken = pkg?.ReadInt() ?? 0;
            requestFullSnapshot = pkg?.ReadBool() ?? false;
        }
        catch
        {
            return;
        }

        if (requestId <= 0 || pkg == null)
        {
            return;
        }

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out var playerId) ||
            !TryBeginServerSnapshotRequest(sender))
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
            if (snapshot.VisibleWardCount > WardMinimapSnapshotProtocol.MaxEntryCount)
            {
                responseKind = WardPinsResponseKind.TooLarge;
                TrackServerViewerSnapshotTooLarge(sender, snapshot.ViewerRevisionToken);
            }
            else
            {
                responseKind = includeEntries ? WardPinsResponseKind.FullSnapshot : WardPinsResponseKind.Unchanged;
                TrackServerViewerSyncState(sender, snapshot);
            }
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

        IReadOnlyList<WardMinimapSnapshotEntry> entriesToSend = System.Array.Empty<WardMinimapSnapshotEntry>();
        if (responseKind == WardPinsResponseKind.FullSnapshot)
        {
            if (snapshot.Entries.Count > WardMinimapSnapshotProtocol.MaxEntryCount)
            {
                responseKind = WardPinsResponseKind.TooLarge;
                TrackServerViewerSnapshotTooLarge(receiverUid, snapshot.ViewerRevisionToken);
            }
            else if (!AreSnapshotEntriesValid(snapshot.Entries))
            {
                responseKind = WardPinsResponseKind.Unavailable;
            }
            else
            {
                entriesToSend = snapshot.Entries;
            }
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
        pkg.Write(entriesToSend.Count);
        WriteSnapshotEntries(pkg, entriesToSend);

        routedRpc.InvokeRoutedRPC(receiverUid, ReceiveWardPinsRpc, pkg);
    }

    private static void SendWardPinsPush(
        long receiverUid,
        WardPinsPushKind pushKind,
        int viewerRevisionToken,
        long playerId,
        bool canSeeAllWards,
        int indexedWardCount,
        int candidateWardCount,
        int visibleWardCount,
        int enabledWardCount,
        IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries,
        IReadOnlyList<ZDOID> removedWardIds)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || receiverUid == 0L)
        {
            return;
        }

        var entryCount = snapshotEntries.Count;
        var removedCount = removedWardIds.Count;
        if (pushKind != WardPinsPushKind.TooLarge &&
            visibleWardCount > WardMinimapSnapshotProtocol.MaxEntryCount)
        {
            pushKind = WardPinsPushKind.TooLarge;
            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            removedWardIds = System.Array.Empty<ZDOID>();
            TrackServerViewerSnapshotTooLarge(receiverUid, viewerRevisionToken);
        }
        else if (pushKind == WardPinsPushKind.TooLarge)
        {
            if (visibleWardCount <= WardMinimapSnapshotProtocol.MaxEntryCount)
            {
                Plugin.Log.LogError("Refusing to send an invalid ward minimap TooLarge response.");
                return;
            }

            snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
            removedWardIds = System.Array.Empty<ZDOID>();
        }
        else if (entryCount > WardMinimapSnapshotProtocol.MaxEntryCount ||
                 removedCount > WardMinimapSnapshotProtocol.MaxEntryCount - entryCount)
        {
            Plugin.Log.LogError("Refusing to send an oversized ward minimap snapshot payload.");
            return;
        }
        else if (!AreSnapshotEntriesValid(snapshotEntries) || !AreRemovedWardIdsValid(removedWardIds))
        {
            Plugin.Log.LogError("Refusing to send an invalid ward minimap snapshot payload.");
            return;
        }

        var pkg = new ZPackage();
        pkg.Write((int)pushKind);
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
        int visibleWardCount;
        int snapshotCount;
        try
        {
            requestId = pkg.ReadInt();
            if (!TryReadWardPinsResponseKind(pkg.ReadInt(), out responseKind))
            {
                return;
            }
            viewerRevisionToken = pkg.ReadInt();
            _ = pkg.ReadLong();
            _ = pkg.ReadBool();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            visibleWardCount = pkg.ReadInt();
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

        if (responseKind != WardPinsResponseKind.TooLarge &&
            (visibleWardCount < 0 || visibleWardCount > WardMinimapSnapshotProtocol.MaxEntryCount))
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (responseKind == WardPinsResponseKind.Unavailable)
        {
            if (snapshotCount != 0)
            {
                QueueRemoteSnapshotBootstrapRequest();
            }

            return;
        }

        if (responseKind == WardPinsResponseKind.TooLarge)
        {
            if (snapshotCount != 0 || visibleWardCount <= WardMinimapSnapshotProtocol.MaxEntryCount)
            {
                QueueRemoteSnapshotBootstrapRequest();
                return;
            }

            MarkLocalSnapshotTooLarge(viewerRevisionToken, visibleWardCount);
            UpdateLocalState(Player.m_localPlayer, force: false);
            return;
        }

        if (responseKind == WardPinsResponseKind.Unchanged)
        {
            if (snapshotCount != 0)
            {
                QueueRemoteSnapshotBootstrapRequest();
                return;
            }

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

        if (visibleWardCount < 0 || snapshotCount != visibleWardCount ||
            !TryReadSnapshotEntries(pkg, snapshotCount, out var snapshotEntries))
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

    private static bool TryReadWardPinsResponseKind(int rawValue, out WardPinsResponseKind responseKind)
    {
        switch (rawValue)
        {
            case (int)WardPinsResponseKind.Unavailable:
            case (int)WardPinsResponseKind.FullSnapshot:
            case (int)WardPinsResponseKind.Unchanged:
            case (int)WardPinsResponseKind.TooLarge:
                responseKind = (WardPinsResponseKind)rawValue;
                return true;
            default:
                responseKind = WardPinsResponseKind.Unavailable;
                return false;
        }
    }

    private static void HandlePushWardPins(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            return;
        }

        WardPinsPushKind pushKind;
        int viewerRevisionToken;
        int visibleWardCount;
        int snapshotCount;
        int removedWardCount;
        try
        {
            if (!TryReadWardPinsPushKind(pkg.ReadInt(), out pushKind))
            {
                QueueRemoteSnapshotBootstrapRequest();
                return;
            }
            viewerRevisionToken = pkg.ReadInt();
            _ = pkg.ReadLong();
            _ = pkg.ReadBool();
            _ = pkg.ReadInt();
            _ = pkg.ReadInt();
            visibleWardCount = pkg.ReadInt();
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

        if (snapshotCount > WardMinimapSnapshotProtocol.MaxEntryCount - removedWardCount)
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (pushKind != WardPinsPushKind.TooLarge &&
            (visibleWardCount < 0 || visibleWardCount > WardMinimapSnapshotProtocol.MaxEntryCount))
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (pushKind == WardPinsPushKind.TooLarge)
        {
            if (snapshotCount != 0 || removedWardCount != 0 ||
                visibleWardCount <= WardMinimapSnapshotProtocol.MaxEntryCount)
            {
                QueueRemoteSnapshotBootstrapRequest();
                return;
            }

            MarkLocalSnapshotTooLarge(viewerRevisionToken, visibleWardCount);
            UpdateLocalState(Player.m_localPlayer, force: false);
            return;
        }

        if (pushKind == WardPinsPushKind.Delta && _snapshotState != ClientSnapshotState.Ready)
        {
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        if (pushKind == WardPinsPushKind.FullSnapshot)
        {
            if (removedWardCount != 0 || visibleWardCount < 0 || snapshotCount != visibleWardCount)
            {
                QueueRemoteSnapshotBootstrapRequest();
                return;
            }

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

    private static bool TryReadWardPinsPushKind(int rawValue, out WardPinsPushKind pushKind)
    {
        switch (rawValue)
        {
            case (int)WardPinsPushKind.FullSnapshot:
            case (int)WardPinsPushKind.Delta:
            case (int)WardPinsPushKind.TooLarge:
                pushKind = (WardPinsPushKind)rawValue;
                return true;
            default:
                pushKind = WardPinsPushKind.Delta;
                return false;
        }
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
        if (snapshotCount < 0 || snapshotCount > WardMinimapSnapshotProtocol.MaxEntryCount)
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
                var entry = new WardMinimapSnapshotEntry(
                    pkg.ReadZDOID(),
                    pkg.ReadVector3(),
                    pkg.ReadSingle(),
                    pkg.ReadBool());
                if (!WardMinimapSnapshotProtocol.IsValidEntry(entry))
                {
                    snapshotEntries = System.Array.Empty<WardMinimapSnapshotEntry>();
                    return false;
                }

                snapshotEntries[index] = entry;
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
        if (removedWardCount < 0 || removedWardCount > WardMinimapSnapshotProtocol.MaxEntryCount)
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
                var removedWardId = pkg.ReadZDOID();
                if (removedWardId.IsNone())
                {
                    removedWardIds = System.Array.Empty<ZDOID>();
                    return false;
                }

                removedWardIds[index] = removedWardId;
            }

            return true;
        }
        catch
        {
            removedWardIds = System.Array.Empty<ZDOID>();
            return false;
        }
    }

    private static bool AreSnapshotEntriesValid(IReadOnlyList<WardMinimapSnapshotEntry> snapshotEntries)
    {
        for (var index = 0; index < snapshotEntries.Count; index++)
        {
            if (!WardMinimapSnapshotProtocol.IsValidEntry(snapshotEntries[index]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreRemovedWardIdsValid(IReadOnlyList<ZDOID> removedWardIds)
    {
        for (var index = 0; index < removedWardIds.Count; index++)
        {
            if (removedWardIds[index].IsNone())
            {
                return false;
            }
        }

        return true;
    }

}
