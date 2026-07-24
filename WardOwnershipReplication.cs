using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct PendingManagedWardPlacementObserve
{
    internal PendingManagedWardPlacementObserve(
        DateTime firstSeenUtc,
        bool isVerified = false)
    {
        FirstSeenUtc = firstSeenUtc;
        IsVerified = isVerified;
    }

    internal DateTime FirstSeenUtc { get; }
    internal bool IsVerified { get; }
}

internal readonly struct PendingManagedWardMapStateRefresh
{
    internal PendingManagedWardMapStateRefresh(
        uint expectedDataRevision,
        DateTime firstSeenUtc)
    {
        ExpectedDataRevision = expectedDataRevision;
        FirstSeenUtc = firstSeenUtc;
    }

    internal uint ExpectedDataRevision { get; }
    internal DateTime FirstSeenUtc { get; }
}

internal static partial class WardOwnership
{
    private const int MaxPendingManagedWardPlacementObserves = 512;
    private const int MaxPendingManagedWardPlacementObservesPerRequester = 32;
    private const uint MaxPendingMapStateRevisionLead = 128u;
    private static readonly TimeSpan MinimumManagedWardPlacementObserveInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan PendingManagedWardPlacementObserveLifetime = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan PendingManagedWardMapStateRefreshLifetime = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<long, DateTime> LastManagedWardPlacementObserveUtcByRequesterId = new();
    private static readonly Dictionary<(ZDOID WardZdoId, long RequesterId), PendingManagedWardPlacementObserve>
        PendingManagedWardPlacementObserves = new();
    private static readonly Dictionary<ZDOID, PendingManagedWardMapStateRefresh> PendingManagedWardMapStateRefreshes = new();

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
        ManagedWardMapStateService.NotifyWardMutation(zdo);

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

        long claimedPlayerId;
        ZDOID wardZdoId;
        try
        {
            claimedPlayerId = pkg.ReadLong();
            wardZdoId = pkg.ReadZDOID();
        }
        catch
        {
            return;
        }

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

        var pendingObserveKey = (WardZdoId: wardZdoId, RequesterId: requesterId);
        if (creatorPlayerId == requesterId &&
            PendingManagedWardPlacementObserves.TryGetValue(pendingObserveKey, out var existingObserve))
        {
            PendingManagedWardPlacementObserves[pendingObserveKey] = new PendingManagedWardPlacementObserve(
                existingObserve.FirstSeenUtc,
                isVerified: true);
            return;
        }

        if (!TryBeginManagedWardPlacementObserve(requesterId))
        {
            EnqueuePendingManagedWardPlacementObserve(
                requesterId,
                wardZdoId,
                isVerified: true);
            return;
        }

        ObserveAuthoritativeManagedWardPlacement(zdo);
    }

    private static bool TryBeginManagedWardPlacementObserve(long requesterId)
    {
        var nowUtc = DateTime.UtcNow;
        if (LastManagedWardPlacementObserveUtcByRequesterId.TryGetValue(requesterId, out var lastObserveUtc) &&
            nowUtc >= lastObserveUtc &&
            nowUtc - lastObserveUtc < MinimumManagedWardPlacementObserveInterval)
        {
            return false;
        }

        LastManagedWardPlacementObserveUtcByRequesterId[requesterId] = nowUtc;
        return true;
    }

    private static void EnqueuePendingManagedWardPlacementObserve(
        long requesterId,
        ZDOID wardZdoId,
        bool isVerified = false)
    {
        var pendingObserveKey = (WardZdoId: wardZdoId, RequesterId: requesterId);
        if (wardZdoId.IsNone() || PendingManagedWardPlacementObserves.ContainsKey(pendingObserveKey))
        {
            return;
        }

        if (!isVerified &&
            PendingManagedWardPlacementObserves.Count >= MaxPendingManagedWardPlacementObserves)
        {
            return;
        }

        var requesterPendingCount = 0;
        foreach (var pendingEntry in PendingManagedWardPlacementObserves)
        {
            if (pendingEntry.Key.RequesterId != requesterId)
            {
                continue;
            }

            requesterPendingCount++;
            if (requesterPendingCount >= MaxPendingManagedWardPlacementObservesPerRequester)
            {
                break;
            }
        }

        var requesterQueueFull =
            requesterPendingCount >= MaxPendingManagedWardPlacementObservesPerRequester;
        var globalQueueFull =
            PendingManagedWardPlacementObserves.Count >= MaxPendingManagedWardPlacementObserves;
        if ((requesterQueueFull || globalQueueFull) &&
            (!isVerified ||
             !TryEvictOldestUnverifiedPlacementObserve(
                 requesterId,
                 requireSameRequester: requesterQueueFull)))
        {
            return;
        }

        PendingManagedWardPlacementObserves.Add(
            pendingObserveKey,
            new PendingManagedWardPlacementObserve(
                DateTime.UtcNow,
                isVerified));
    }

    private static bool TryEvictOldestUnverifiedPlacementObserve(
        long requesterId,
        bool requireSameRequester)
    {
        var foundCandidate = false;
        var candidateKey = default((ZDOID WardZdoId, long RequesterId));
        var oldestFirstSeenUtc = DateTime.MaxValue;
        foreach (var pendingEntry in PendingManagedWardPlacementObserves)
        {
            var pendingObserve = pendingEntry.Value;
            if (pendingObserve.IsVerified ||
                (requireSameRequester && pendingEntry.Key.RequesterId != requesterId) ||
                pendingObserve.FirstSeenUtc >= oldestFirstSeenUtc)
            {
                continue;
            }

            var pendingZdo = ZDOMan.instance?.GetZDO(pendingEntry.Key.WardZdoId);
            if (pendingZdo != null &&
                pendingZdo.IsValid() &&
                pendingZdo.GetLong(ZDOVars.s_creator, 0L) == pendingEntry.Key.RequesterId)
            {
                continue;
            }

            foundCandidate = true;
            candidateKey = pendingEntry.Key;
            oldestFirstSeenUtc = pendingObserve.FirstSeenUtc;
        }

        return foundCandidate && PendingManagedWardPlacementObserves.Remove(candidateKey);
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

        List<(ZDOID WardZdoId, long RequesterId)>? completedObserveKeys = null;
        List<(ZDOID WardZdoId, long RequesterId)>? verifiedObserveKeys = null;
        var now = DateTime.UtcNow;
        foreach (var entry in PendingManagedWardPlacementObserves)
        {
            var pendingObserve = entry.Value;
            var zdo = zdoMan.GetZDO(entry.Key.WardZdoId);
            if (zdo == null || !zdo.IsValid())
            {
                if (now - pendingObserve.FirstSeenUtc < PendingManagedWardPlacementObserveLifetime)
                {
                    continue;
                }

                completedObserveKeys ??= new List<(ZDOID WardZdoId, long RequesterId)>();
                completedObserveKeys.Add(entry.Key);
                continue;
            }

            var creatorPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
            if (creatorPlayerId != 0L && creatorPlayerId != entry.Key.RequesterId)
            {
                completedObserveKeys ??= new List<(ZDOID WardZdoId, long RequesterId)>();
                completedObserveKeys.Add(entry.Key);
                continue;
            }

            if (!TryBeginManagedWardPlacementObserve(entry.Key.RequesterId))
            {
                if (!pendingObserve.IsVerified && creatorPlayerId == entry.Key.RequesterId)
                {
                    verifiedObserveKeys ??= new List<(ZDOID WardZdoId, long RequesterId)>();
                    verifiedObserveKeys.Add(entry.Key);
                }

                continue;
            }

            ObserveAuthoritativeManagedWardPlacement(zdo);
            completedObserveKeys ??= new List<(ZDOID WardZdoId, long RequesterId)>();
            completedObserveKeys.Add(entry.Key);
        }

        if (verifiedObserveKeys != null)
        {
            for (var index = 0; index < verifiedObserveKeys.Count; index++)
            {
                var verifiedObserveKey = verifiedObserveKeys[index];
                if (!PendingManagedWardPlacementObserves.TryGetValue(
                        verifiedObserveKey,
                        out var pendingObserve) ||
                    pendingObserve.IsVerified)
                {
                    continue;
                }

                PendingManagedWardPlacementObserves[verifiedObserveKey] =
                    new PendingManagedWardPlacementObserve(
                        pendingObserve.FirstSeenUtc,
                        isVerified: true);
            }
        }

        if (completedObserveKeys == null)
        {
            return;
        }

        for (var index = 0; index < completedObserveKeys.Count; index++)
        {
            PendingManagedWardPlacementObserves.Remove(completedObserveKeys[index]);
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
            ManagedWardMapStateService.NotifyWardMutation(zdo);
            return;
        }

        if (PendingManagedWardMapStateRefreshes.TryGetValue(wardZdoId, out var existingRefresh) &&
            existingRefresh.ExpectedDataRevision >= expectedDataRevision)
        {
            return;
        }

        PendingManagedWardMapStateRefreshes[wardZdoId] = new PendingManagedWardMapStateRefresh(
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
            var zdo = zdoMan.GetZDO(entry.Key);
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

            ManagedWardMapStateService.NotifyWardMutation(zdo);
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
