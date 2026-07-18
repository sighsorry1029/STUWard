using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct PendingManagedWardPlacementObserve
{
    internal PendingManagedWardPlacementObserve(ZDOID wardZdoId, long requesterId, DateTime firstSeenUtc)
    {
        WardZdoId = wardZdoId;
        RequesterId = requesterId;
        FirstSeenUtc = firstSeenUtc;
    }

    internal ZDOID WardZdoId { get; }
    internal long RequesterId { get; }
    internal DateTime FirstSeenUtc { get; }
}

internal readonly struct PendingManagedWardMapStateRefresh
{
    internal PendingManagedWardMapStateRefresh(
        ZDOID wardZdoId,
        uint expectedDataRevision,
        DateTime firstSeenUtc)
    {
        WardZdoId = wardZdoId;
        ExpectedDataRevision = expectedDataRevision;
        FirstSeenUtc = firstSeenUtc;
    }

    internal ZDOID WardZdoId { get; }
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

    internal static void ForceSyncManagedWardZdoToServer(PrivateArea? area)
    {
        ForceSyncManagedWardZdoToServer(ManagedWardRef.FromArea(area));
    }

    internal static void ForceSyncManagedWardZdoToServer(ManagedWardRef ward)
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

    }

    internal static bool TryClaimManagedWardMutationOwnership(PrivateArea? area)
    {
        var nview = WardPrivateAreaSafeAccess.GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        return TryClaimManagedWardMutationOwnership(WardPrivateAreaSafeAccess.GetZdo(nview));
    }

    internal static bool TryClaimManagedWardMutationOwnership(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return true;
        }

        if (zdo.GetOwner() != ZDOMan.GetSessionID())
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        }

        var claimed = zdo.GetOwner() == ZDOMan.GetSessionID();
        if (!claimed)
        {
            return false;
        }

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
            ObserveAuthoritativeManagedWardPlacement(zdo);
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
        routedRpc.InvokeRoutedRPC(serverPeerId, NotifyManagedWardPlacedRpc, pkg);
    }

    internal static bool CanApplyManagedWardStateLocally(ZNetView? nview)
    {
        return nview != null &&
               nview.IsValid() &&
               ZNet.instance != null &&
               ZNet.instance.IsServer();
    }

    internal static bool TryInvokeServerRoutedRpc(string method, params object[] parameters)
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || string.IsNullOrWhiteSpace(method) || !TryGetServerPeerId(out var serverPeerId))
        {
            return false;
        }

        routedRpc.InvokeRoutedRPC(serverPeerId, method, parameters);
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

    internal static bool TryResolveAuthoritativeManagedWardRequest(
        long sender,
        ZDOID wardZdoId,
        out ZDO zdo,
        out long requesterId)
    {
        zdo = null!;
        requesterId = 0L;
        if (ZNet.instance == null || !ZNet.instance.IsServer() || wardZdoId.IsNone() ||
            !TryResolveAuthoritativePlayerIdFromSender(sender, out requesterId))
        {
            return false;
        }

        var resolvedZdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (resolvedZdo == null || !resolvedZdo.IsValid() || !IsManagedWardZdo(resolvedZdo))
        {
            return false;
        }

        zdo = resolvedZdo;
        return true;
    }

    internal static void CompleteAuthoritativeManagedWardMutation(ZDO zdo)
    {
        ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        ManagedWardMapStateService.NotifyZdoWardMutation(zdo);

        var instance = ZNetScene.instance?.FindInstance(zdo.m_uid);
        var area = instance != null
            ? instance.GetComponent<PrivateArea>() ?? instance.GetComponentInChildren<PrivateArea>()
            : null;
        area?.UpdateStatus();
    }

    internal static void CompleteAuthoritativePermittedMutation(ZDO zdo)
    {
        WardPermittedSnapshots.Refresh(zdo);
        ManagedWardPresenceService.Invalidate();
        CompleteAuthoritativeManagedWardMutation(zdo);
    }

    private static void HandleNotifyManagedWardPlaced(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null)
        {
            return;
        }

        var claimedPlayerId = pkg.ReadLong();
        var wardZdoId = pkg.ReadZDOID();
        if (!TryResolveClaimedPlayerIdFromSender(sender, claimedPlayerId, out var requesterId))
        {
            return;
        }

        if (wardZdoId.IsNone())
        {
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (zdo == null || !zdo.IsValid())
        {
            EnqueuePendingManagedWardPlacementObserve(requesterId, wardZdoId);
            return;
        }

        var creatorPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        if (creatorPlayerId != 0L && creatorPlayerId != requesterId)
        {
            return;
        }

        ObserveAuthoritativeManagedWardPlacement(zdo);
    }

    private static void EnqueuePendingManagedWardPlacementObserve(long requesterId, ZDOID wardZdoId)
    {
        if (wardZdoId.IsNone())
        {
            return;
        }

        PendingManagedWardPlacementObserves[wardZdoId] = new PendingManagedWardPlacementObserve(
            wardZdoId,
            requesterId,
            DateTime.UtcNow);
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
                continue;
            }

            var creatorPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (creatorPlayerId != 0L && creatorPlayerId != pendingObserve.RequesterId)
            {
                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                continue;
            }

            ObserveAuthoritativeManagedWardPlacement(zdo);
            completedWardIds ??= new List<ZDOID>();
            completedWardIds.Add(entry.Key);
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
            return;
        }

        if (wardZdoId.IsNone())
        {
            return;
        }

        if (!TryResolveAuthoritativePlayerIdFromSender(sender, out var senderPlayerId))
        {
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (zdo == null || !zdo.IsValid() || !IsManagedWardZdo(zdo))
        {
            return;
        }

        if (zdo.GetOwner() != sender)
        {
            return;
        }

        var serverDataRevision = zdo.DataRevision;
        if (expectedDataRevision > serverDataRevision &&
            expectedDataRevision - serverDataRevision > MaxPendingMapStateRevisionLead)
        {
            return;
        }

        if (expectedDataRevision <= serverDataRevision)
        {
            PendingManagedWardMapStateRefreshes.Remove(wardZdoId);
            ManagedWardMapStateService.NotifyZdoWardMutation(zdo);
            return;
        }

        if (PendingManagedWardMapStateRefreshes.TryGetValue(wardZdoId, out var existingRefresh) &&
            existingRefresh.ExpectedDataRevision >= expectedDataRevision)
        {
            return;
        }

        PendingManagedWardMapStateRefreshes[wardZdoId] = new PendingManagedWardMapStateRefresh(
            wardZdoId,
            expectedDataRevision,
            DateTime.UtcNow);
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
                continue;
            }

            if (!IsManagedWardZdo(zdo))
            {
                completedWardIds ??= new List<ZDOID>();
                completedWardIds.Add(entry.Key);
                continue;
            }

            if (zdo.DataRevision < pendingRefresh.ExpectedDataRevision && !timedOut)
            {
                continue;
            }

            ManagedWardMapStateService.NotifyZdoWardMutation(zdo);
            completedWardIds ??= new List<ZDOID>();
            completedWardIds.Add(entry.Key);
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
