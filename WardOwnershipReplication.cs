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

internal static partial class WardOwnership
{
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

        var permittedPlayerIds = WardPrivateAreaSafeAccess.GetPermittedPlayerIds(area);
        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        var wardGuildId = GuildsCompat.GetWardGuildId(zdo);
        var position = area.transform.position;
        var radius = WardSettings.GetStoredRadiusOrMin(area);
        var isEnabled = area.IsEnabled();
        ZDOMan.instance?.ForceSendZDO(serverPeerId, zdo.m_uid);
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc != null)
        {
            var pkg = new ZPackage();
            pkg.Write(zdo.m_uid);
            pkg.Write(zdo.DataRevision);
            pkg.Write(position);
            pkg.Write(radius);
            pkg.Write(isEnabled);
            pkg.Write(ownerPlayerId);
            pkg.Write(wardGuildId);
            pkg.Write(permittedPlayerIds.Length);
            for (var index = 0; index < permittedPlayerIds.Length; index++)
            {
                pkg.Write(permittedPlayerIds[index]);
            }

            routedRpc.InvokeRoutedRPC(serverPeerId, NotifyManagedWardMapStateChangedRpc, pkg);
        }

        Plugin.LogWardDiagnosticVerbose(
            context,
            $"Force-sent managed ward ZDO to server after local mutation and notified server map-state refresh. serverPeerId={serverPeerId}, dataRevision={zdo.DataRevision}, radius={radius:F1}, enabled={isEnabled}, permittedCount={permittedPlayerIds.Length}, {WardDiagnosticInfo.DescribeWard(area)}");
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

    internal static bool CanHandleManagedWardStateRpc(ZNetView? nview)
    {
        return nview != null &&
               nview.IsValid() &&
               (nview.IsOwner() || (ZNet.instance != null && ZNet.instance.IsServer()));
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

        ObserveManagedWard(zdo);
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

            ObserveManagedWard(zdo);
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
        uint dataRevision;
        UnityEngine.Vector3 position;
        float radius;
        bool isEnabled;
        long ownerPlayerId;
        int wardGuildId;
        int permittedPlayerCount;
        try
        {
            wardZdoId = pkg.ReadZDOID();
            dataRevision = pkg.ReadUInt();
            position = pkg.ReadVector3();
            radius = pkg.ReadSingle();
            isEnabled = pkg.ReadBool();
            ownerPlayerId = pkg.ReadLong();
            wardGuildId = pkg.ReadInt();
            permittedPlayerCount = pkg.ReadInt();
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
            return;
        }

        if (permittedPlayerCount < 0)
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Rejected managed ward map-state sync with invalid permitted count. sender={sender}, wardZdo={wardZdoId}, permittedCount={permittedPlayerCount}");
            return;
        }

        var permittedPlayerIds = permittedPlayerCount == 0 ? Array.Empty<long>() : new long[permittedPlayerCount];
        try
        {
            for (var index = 0; index < permittedPlayerIds.Length; index++)
            {
                permittedPlayerIds[index] = pkg.ReadLong();
            }
        }
        catch
        {
            Plugin.LogWardDiagnosticFailure(
                "WardPins.Sync",
                $"Failed to deserialize managed ward map-state sync permitted players. sender={sender}, wardZdo={wardZdoId}, permittedCount={permittedPlayerCount}");
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        if (zdo != null && zdo.IsValid() && !IsManagedWardZdo(zdo))
        {
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Sync",
                $"Ignored managed ward map-state sync because the ward ZDO was available but not managed. sender={sender}, wardZdo={wardZdoId}");
            return;
        }

        ManagedWardMapStateService.NotifySyncedWardState(
            wardZdoId,
            dataRevision,
            ownerPlayerId,
            wardGuildId,
            position,
            radius,
            isEnabled,
            permittedPlayerIds,
            "server applied managed ward map-state sync");
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Sync",
            $"Applied managed ward map-state sync on server. sender={sender}, wardZdo={wardZdoId}, dataRevision={dataRevision}, position={position}, radius={radius:F1}, enabled={isEnabled}, ownerPlayerId={ownerPlayerId}, guildId={wardGuildId}, permittedCount={permittedPlayerIds.Length}, zdoPresent={zdo != null && zdo.IsValid()}");
    }

}
