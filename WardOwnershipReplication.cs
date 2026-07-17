using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct PendingManagedWardPlacementObserve
{
    internal PendingManagedWardPlacementObserve(ZDOID wardZdoId, long senderUid, long requesterId, DateTime firstSeenUtc)
    {
        WardZdoId = wardZdoId;
        SenderUid = senderUid;
        RequesterId = requesterId;
        FirstSeenUtc = firstSeenUtc;
    }

    internal ZDOID WardZdoId { get; }
    internal long SenderUid { get; }
    internal long RequesterId { get; }
    internal DateTime FirstSeenUtc { get; }
}

internal readonly struct PendingManagedWardMapStateRefresh
{
    internal PendingManagedWardMapStateRefresh(
        ZDOID wardZdoId,
        long senderUid,
        uint expectedDataRevision,
        DateTime firstSeenUtc)
    {
        WardZdoId = wardZdoId;
        SenderUid = senderUid;
        ExpectedDataRevision = expectedDataRevision;
        FirstSeenUtc = firstSeenUtc;
    }

    internal ZDOID WardZdoId { get; }
    internal long SenderUid { get; }
    internal uint ExpectedDataRevision { get; }
    internal DateTime FirstSeenUtc { get; }
}

internal static partial class WardOwnership
{
    private const uint MaxPendingMapStateRevisionLead = 128u;
    private static readonly TimeSpan PendingManagedWardPlacementObserveLifetime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PendingManagedWardMapStateRefreshLifetime = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<ZDOID, PendingManagedWardPlacementObserve> PendingManagedWardPlacementObserves = new();
    private static readonly Dictionary<ZDOID, PendingManagedWardMapStateRefresh> PendingManagedWardMapStateRefreshes = new();

    internal static void ForceSyncManagedWardZdoToServer(PrivateArea? area, string context)
    {
        ForceSyncManagedWardZdoToServer(ManagedWardRef.FromArea(area), context);
    }

    internal static void ForceSyncManagedWardZdoToServer(ManagedWardRef ward, string context)
    {
        var area = ward.Area;
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(ward) || ZNet.instance == null || ZNet.instance.IsServer())
        {
            return;
        }

        if (!ward.HasValidNetworkIdentity || !ward.IsOwner)
        {
            return;
        }

        var zdo = ward.Zdo;
        if (zdo == null || !zdo.IsValid() || !TryGetServerPeerId(out var serverPeerId))
        {
            return;
        }

        ZDOMan.instance?.ForceSendZDO(serverPeerId, zdo.m_uid);
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc != null)
        {
            var pkg = new ZPackage();
            pkg.Write(zdo.m_uid);
            pkg.Write(zdo.DataRevision);
            routedRpc.InvokeRoutedRPC(serverPeerId, NotifyManagedWardMapStateChangedRpc, pkg);
        }

        Plugin.LogWardDiagnosticVerbose(
            context,
            $"Force-sent managed ward ZDO to server after local mutation and notified server map-state refresh. serverPeerId={serverPeerId}, expectedDataRevision={zdo.DataRevision}, {WardDiagnosticInfo.DescribeWard(area)}");
    }

    internal static bool TryClaimManagedWardMutationOwnership(PrivateArea? area, string context)
    {
        var nview = WardPrivateAreaSafeAccess.GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        if (ZNet.instance == null || !ZNet.instance.IsServer() || nview.IsOwner())
        {
            return true;
        }

        nview.ClaimOwnership();
        if (!nview.IsOwner())
        {
            var zdo = WardPrivateAreaSafeAccess.GetZdo(nview);
            if (zdo != null && zdo.IsValid())
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
                ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
            }
        }

        if (!nview.IsOwner())
        {
            Plugin.LogWardDiagnosticFailure(
                context,
                $"Failed to claim managed ward ownership for mutation. {WardDiagnosticInfo.DescribeWard(area)}");
            return false;
        }

        Plugin.LogWardDiagnosticVerbose(
            context,
            $"Claimed managed ward ownership for mutation. {WardDiagnosticInfo.DescribeWard(area)}");
        return true;
    }

    private static bool TryClaimManagedWardMutationOwnership(ZDO? zdo, string context)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return true;
        }

        var instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
        var nview = instance != null ? instance.GetComponent<ZNetView>() : null;
        if (nview != null && nview.IsValid())
        {
            var area = instance!.GetComponent<PrivateArea>();
            return area != null
                ? TryClaimManagedWardMutationOwnership(area, context)
                : nview.IsOwner();
        }

        if (zdo.GetOwner() != ZDOMan.GetSessionID())
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        }

        var claimed = zdo.GetOwner() == ZDOMan.GetSessionID();
        if (!claimed)
        {
            Plugin.LogWardDiagnosticFailure(
                context,
                $"Failed to claim managed ward ZDO ownership for mutation. {DescribeManagedWardZdo(zdo)}");
            return false;
        }

        Plugin.LogWardDiagnosticVerbose(
            context,
            $"Claimed managed ward ZDO ownership for mutation. {DescribeManagedWardZdo(zdo)}");
        return true;
    }

    internal static void NotifyServerManagedWardPlaced(PrivateArea? area)
    {
        NotifyServerManagedWardPlaced(ManagedWardRef.FromArea(area));
    }

    internal static void NotifyServerManagedWardPlaced(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return;
        }

        if (!ward.HasValidNetworkIdentity)
        {
            return;
        }

        var zdo = ward.Zdo;
        if (zdo == null)
        {
            return;
        }

        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            ObserveManagedWard(ward);
            return;
        }

        var localPlayer = Player.m_localPlayer;
        var routedRpc = ZRoutedRpc.instance;
        if (localPlayer == null || routedRpc == null)
        {
            return;
        }

        var serverPeerId = routedRpc.GetServerPeerID();
        if (serverPeerId == 0L)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(localPlayer.GetPlayerID());
        pkg.Write(zdo.m_uid);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.Notify",
            $"Notifying server about locally placed managed ward. senderPlayerId={localPlayer.GetPlayerID()}, wardZdo={zdo.m_uid}");
        routedRpc.InvokeRoutedRPC(serverPeerId, NotifyManagedWardPlacedRpc, pkg);
    }

    internal static bool CanApplyManagedWardStateLocally(ZNetView? nview)
    {
        return nview != null &&
               nview.IsValid() &&
               nview.IsOwner() &&
               ZNet.instance != null &&
               ZNet.instance.IsServer();
    }

    internal static bool TryInvokeManagedWardStateRpcOnServer(ZNetView? nview, string method, params object[] parameters)
    {
        if (nview == null || !nview.IsValid() || string.IsNullOrWhiteSpace(method) ||
            !TryGetServerPeerId(out var serverPeerId))
        {
            return false;
        }

        nview.InvokeRPC(serverPeerId, method, parameters);
        return true;
    }

    internal static bool CanHandleManagedWardStateRpc(ZNetView? nview)
    {
        return nview != null &&
               nview.IsValid() &&
               ZNet.instance != null &&
               ZNet.instance.IsServer();
    }

    internal static bool TryGetServerPeerId(out long serverPeerId)
    {
        serverPeerId = 0L;
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null)
        {
            return false;
        }

        serverPeerId = routedRpc.GetServerPeerID();
        return serverPeerId != 0L;
    }

    private static void HandleNotifyManagedWardPlaced(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null)
        {
            return;
        }

        var claimedPlayerId = pkg.ReadLong();
        var wardZdoId = pkg.ReadZDOID();
        if (!TryResolveClaimedPlayerIdFromSender(sender, claimedPlayerId, "Placement.Notify", out var requesterId))
        {
            return;
        }

        if (wardZdoId.IsNone())
        {
            Plugin.LogWardDiagnosticFailure(
                "Placement.Notify",
                $"Rejected managed ward placement notify because ward ZDO id was empty. sender={sender}, requesterId={requesterId}.");
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (zdo == null || !zdo.IsValid())
        {
            EnqueuePendingManagedWardPlacementObserve(sender, requesterId, wardZdoId);
            return;
        }

        var creatorPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        if (creatorPlayerId != 0L && creatorPlayerId != requesterId)
        {
            Plugin.LogWardDiagnosticFailure(
                "Placement.Notify",
                $"Rejected managed ward placement notify because requester did not match ward creator. sender={sender}, requesterId={requesterId}, wardCreator={creatorPlayerId}, wardZdo={wardZdoId}");
            return;
        }

        ObserveAuthoritativeManagedWardPlacement(zdo);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.Notify",
            $"Observed managed ward from placement notify. sender={sender}, requesterId={requesterId}, {DescribeManagedWardZdo(zdo)}");
    }

    private static void EnqueuePendingManagedWardPlacementObserve(long sender, long requesterId, ZDOID wardZdoId)
    {
        if (wardZdoId.IsNone())
        {
            return;
        }

        PendingManagedWardPlacementObserves[wardZdoId] = new PendingManagedWardPlacementObserve(
            wardZdoId,
            sender,
            requesterId,
            DateTime.UtcNow);
        Plugin.LogWardDiagnosticVerbose(
            "Placement.Notify",
            $"Deferred managed ward placement observe because ward ZDO was not available yet. sender={sender}, requesterId={requesterId}, wardZdo={wardZdoId}");
    }

    private static void ProcessPendingManagedWardPlacementObserves()
    {
        if (PendingManagedWardPlacementObserves.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var zdoMan = ZDOMan.instance;
        if (zdoMan == null)
        {
            return;
        }

        List<ZDOID>? completedWardIds = null;
        var now = DateTime.UtcNow;
        foreach (var entry in PendingManagedWardPlacementObserves)
        {
            var pendingObserve = entry.Value;
            var zdo = zdoMan.GetZDO(pendingObserve.WardZdoId);
            if (zdo == null || !zdo.IsValid())
            {
                if (now - pendingObserve.FirstSeenUtc < PendingManagedWardPlacementObserveLifetime)
                {
                    continue;
                }

                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                Plugin.LogWardDiagnosticFailure(
                    "Placement.Notify",
                    $"Dropped deferred managed ward placement observe because the ward ZDO never became available. sender={pendingObserve.SenderUid}, requesterId={pendingObserve.RequesterId}, wardZdo={pendingObserve.WardZdoId}");
                continue;
            }

            var creatorPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (creatorPlayerId != 0L && creatorPlayerId != pendingObserve.RequesterId)
            {
                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                Plugin.LogWardDiagnosticFailure(
                    "Placement.Notify",
                    $"Dropped deferred managed ward placement observe because requester did not match ward creator. sender={pendingObserve.SenderUid}, requesterId={pendingObserve.RequesterId}, wardCreator={creatorPlayerId}, wardZdo={pendingObserve.WardZdoId}");
                continue;
            }

            ObserveAuthoritativeManagedWardPlacement(zdo);
            completedWardIds ??= new List<ZDOID>();
            completedWardIds.Add(entry.Key);
            Plugin.LogWardDiagnosticVerbose(
                "Placement.Notify",
                $"Observed managed ward from deferred placement notify. sender={pendingObserve.SenderUid}, requesterId={pendingObserve.RequesterId}, {DescribeManagedWardZdo(zdo)}");
        }

        if (completedWardIds == null)
        {
            return;
        }

        for (var index = 0; index < completedWardIds.Count; index++)
        {
            PendingManagedWardPlacementObserves.Remove(completedWardIds[index]);
        }
    }

    private static void HandleNotifyManagedWardMapStateChanged(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null)
        {
            return;
        }

        ZDOID wardZdoId;
        uint expectedDataRevision;
        try
        {
            wardZdoId = pkg.ReadZDOID();
            expectedDataRevision = pkg.ReadUInt();
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Failed to deserialize managed ward map-state sync header. sender={sender}");
            return;
        }

        if (wardZdoId.IsNone())
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Rejected managed ward map-state refresh with an empty ward ZDO id. sender={sender}");
            return;
        }

        if (!TryResolveAuthoritativePlayerIdFromSender(sender, "WardPins.Sync", out var senderPlayerId))
        {
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (zdo == null || !zdo.IsValid() || !IsManagedWardZdo(zdo))
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Rejected managed ward map-state sync because the ward ZDO was unavailable or not managed. sender={sender}, playerId={senderPlayerId}, wardZdo={wardZdoId}");
            return;
        }

        if (zdo.GetOwner() != sender)
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Rejected managed ward map-state sync from a peer that does not own the ward ZDO. sender={sender}, playerId={senderPlayerId}, zdoOwner={zdo.GetOwner()}, wardZdo={wardZdoId}");
            return;
        }

        var serverDataRevision = zdo.DataRevision;
        if (expectedDataRevision > serverDataRevision &&
            expectedDataRevision - serverDataRevision > MaxPendingMapStateRevisionLead)
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Rejected managed ward map-state refresh too far ahead of the server ZDO revision. sender={sender}, playerId={senderPlayerId}, wardZdo={wardZdoId}, expectedRevision={expectedDataRevision}, serverRevision={serverDataRevision}");
            return;
        }

        if (expectedDataRevision <= serverDataRevision)
        {
            PendingManagedWardMapStateRefreshes.Remove(wardZdoId);
            ManagedWardMapStateService.NotifyZdoWardMutation(
                zdo,
                "server applied authoritative managed ward ZDO state");
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Sync",
                $"Applied authoritative managed ward ZDO state immediately. sender={sender}, playerId={senderPlayerId}, wardZdo={wardZdoId}, expectedRevision={expectedDataRevision}, serverRevision={serverDataRevision}");
            return;
        }

        if (PendingManagedWardMapStateRefreshes.TryGetValue(wardZdoId, out var existingRefresh) &&
            existingRefresh.ExpectedDataRevision >= expectedDataRevision)
        {
            return;
        }

        PendingManagedWardMapStateRefreshes[wardZdoId] = new PendingManagedWardMapStateRefresh(
            wardZdoId,
            sender,
            expectedDataRevision,
            DateTime.UtcNow);
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Sync",
            $"Deferred managed ward map-state refresh until the authoritative ZDO revision arrives. sender={sender}, playerId={senderPlayerId}, wardZdo={wardZdoId}, expectedRevision={expectedDataRevision}, serverRevision={serverDataRevision}");
    }

    private static void ProcessPendingManagedWardMapStateRefreshes()
    {
        if (PendingManagedWardMapStateRefreshes.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var zdoMan = ZDOMan.instance;
        if (zdoMan == null)
        {
            return;
        }

        List<ZDOID>? completedWardIds = null;
        var now = DateTime.UtcNow;
        foreach (var entry in PendingManagedWardMapStateRefreshes)
        {
            var pendingRefresh = entry.Value;
            var timedOut = now - pendingRefresh.FirstSeenUtc >= PendingManagedWardMapStateRefreshLifetime;
            var zdo = zdoMan.GetZDO(pendingRefresh.WardZdoId);
            if (zdo == null || !zdo.IsValid())
            {
                if (!timedOut)
                {
                    continue;
                }

                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                Plugin.LogWardDiagnosticFailure(
                    "WardPins.Sync",
                    $"Dropped deferred managed ward map-state refresh because the ward ZDO became unavailable. sender={pendingRefresh.SenderUid}, wardZdo={pendingRefresh.WardZdoId}, expectedRevision={pendingRefresh.ExpectedDataRevision}");
                continue;
            }

            if (!IsManagedWardZdo(zdo))
            {
                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                Plugin.LogWardDiagnosticFailure(
                    "WardPins.Sync",
                    $"Dropped deferred managed ward map-state refresh because the ZDO is no longer managed. sender={pendingRefresh.SenderUid}, wardZdo={pendingRefresh.WardZdoId}");
                continue;
            }

            if (zdo.DataRevision < pendingRefresh.ExpectedDataRevision && !timedOut)
            {
                continue;
            }

            ManagedWardMapStateService.NotifyZdoWardMutation(
                zdo,
                timedOut
                    ? "server reconciled timed-out managed ward map-state refresh"
                    : "server applied deferred authoritative managed ward ZDO state");
            completedWardIds ??= new List<ZDOID>();
            completedWardIds.Add(entry.Key);

            if (timedOut && zdo.DataRevision < pendingRefresh.ExpectedDataRevision)
            {
                Plugin.LogWardDiagnosticFailure(
                    "WardPins.Sync",
                    $"Reconciled deferred managed ward map-state refresh with the current server ZDO after the expected revision did not arrive. sender={pendingRefresh.SenderUid}, wardZdo={pendingRefresh.WardZdoId}, expectedRevision={pendingRefresh.ExpectedDataRevision}, serverRevision={zdo.DataRevision}");
            }
            else
            {
                Plugin.LogWardDiagnosticVerbose(
                    "WardPins.Sync",
                    $"Applied deferred authoritative managed ward ZDO state. sender={pendingRefresh.SenderUid}, wardZdo={pendingRefresh.WardZdoId}, expectedRevision={pendingRefresh.ExpectedDataRevision}, serverRevision={zdo.DataRevision}");
            }
        }

        if (completedWardIds == null)
        {
            return;
        }

        for (var index = 0; index < completedWardIds.Count; index++)
        {
            PendingManagedWardMapStateRefreshes.Remove(completedWardIds[index]);
        }
    }

}
