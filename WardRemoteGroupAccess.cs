using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace STUWard;

internal static class WardRemoteGroupAccess
{
    private const string RequestRpc = "STUWard_RequestGroupAccessV1";
    private const string ResponseRpc = "STUWard_GroupAccessV1";
    private const int MaximumPackageBytes = 512 * 1024;
    private static readonly WardGroupSnapshotCache ClientCache = new();
    private static readonly List<WardGroupSnapshotEntry> ServerEntries = new();
    private static readonly Dictionary<long, long> LastServerRequests = new();
    private static long _nextServerRefresh;
    private static long _nextClientRequest;
    private static bool _serverSnapshotValid;

    private static long Now => (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency));

    internal static void RegisterRpcs(ZRoutedRpc rpc)
    {
        rpc.Register<long>(RequestRpc, HandleRequest);
        rpc.Register<ZPackage>(ResponseRpc, HandleResponse);
    }

    internal static void Reset()
    {
        ClientCache.Clear();
        ServerEntries.Clear();
        LastServerRequests.Clear();
        _nextServerRefresh = 0;
        _nextClientRequest = 0;
        _serverSnapshotValid = false;
    }

    internal static void Invalidate()
    {
        _nextServerRefresh = 0;
    }

    internal static void ForgetPeer(long sender)
    {
        LastServerRequests.Remove(sender);
        Invalidate();
    }

    internal static void Update()
    {
        var net = ZNet.instance;
        var rpc = ZRoutedRpc.instance;
        if (net == null || rpc == null) return;
        var now = Now;
        if (net.IsServer())
        {
            if (now >= _nextServerRefresh) RefreshServerSnapshot(now);
            return;
        }

        if (Player.m_localPlayer == null || !WardGroupCompat.HasActiveGroupProvider ||
            now < _nextClientRequest || ClientCache.HasPendingRequest(now)) return;
        var server = rpc.GetServerPeerID();
        if (server == 0) return;
        _nextClientRequest = now + 1000;
        var request = ClientCache.BeginRequest(now);
        rpc.InvokeRoutedRPC(server, RequestRpc, request);
    }

    internal static WardGroupIdentity GetPlayerGroup(long playerId, string provider)
    {
        // Only a live character bound to the projected connection may use this trust.
        var player = Player.GetPlayer(playerId);
        if (player == null) return default;
        var character = player.GetZDOID();
        return ClientCache.Get(playerId, player.GetOwner(), character.UserID, character.ID, provider, Now);
    }

    private static void RefreshServerSnapshot(long now)
    {
        _nextServerRefresh = now + 1000;
        ServerEntries.Clear();
        _serverSnapshotValid = true;
        if (!WardGroupCompat.HasActiveGroupProvider) return;

        var seen = new HashSet<long>();
        foreach (var peer in ZNet.instance.GetPeers())
        {
            if (peer == null || !peer.IsReady() || peer.m_characterID.IsNone()) continue;
            WardOwnership.RefreshServerSessionIdentity(peer);
            if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(peer.m_uid, out var playerId)) continue;
            var zdo = ZDOMan.instance?.GetZDO(peer.m_characterID);
            if (zdo == null || zdo.GetOwner() != peer.m_uid || zdo.GetLong(ZDOVars.s_playerID) != playerId) continue;
            AddServerEntry(playerId, peer.m_uid, peer.m_characterID,
                WardOwnership.GetAuthoritativeAccountIdFromSender(peer.m_uid, playerId),
                WardOwnership.GetPlayerName(playerId), seen);
        }

        var local = Player.m_localPlayer;
        if (local != null)
        {
            AddServerEntry(local.GetPlayerID(), local.GetOwner(), local.GetZDOID(),
                WardOwnership.GetPlayerAccountId(local), local.GetPlayerName(), seen);
        }
    }

    private static void AddServerEntry(long playerId, long sessionId, ZDOID character, string accountId,
        string playerName, HashSet<long> seen)
    {
        if (!seen.Add(playerId))
        {
            _serverSnapshotValid = false;
            return;
        }
        if (!WardGroupCompat.TryResolveAuthoritativeGroupIdentity(playerId, accountId, playerName, out var group) ||
            !group.IsValid) return;
        var entry = new WardGroupSnapshotEntry(playerId, sessionId, character.UserID, character.ID, group);
        if (!entry.IsValid || ServerEntries.Count >= WardGroupSnapshotCache.MaximumEntries)
        {
            _serverSnapshotValid = false;
            return;
        }
        ServerEntries.Add(entry);
    }

    private static void HandleRequest(long sender, long requestId)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || requestId <= 0 ||
            !WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out _)) return;
        var now = Now;
        if (LastServerRequests.TryGetValue(sender, out var last) && now - last < 500) return;
        LastServerRequests[sender] = now;
        if (now >= _nextServerRefresh) RefreshServerSnapshot(now);

        var package = new ZPackage();
        package.Write(requestId);
        package.Write(_serverSnapshotValid ? ServerEntries.Count : 0);
        if (_serverSnapshotValid)
        {
            foreach (var entry in ServerEntries)
            {
                package.Write(entry.PlayerId);
                package.Write(entry.SessionId);
                package.Write(new ZDOID(entry.CharacterUserId, entry.CharacterId));
                package.Write(entry.Group.Provider);
                package.Write(entry.Group.Id);
                package.Write(entry.Group.Name);
            }
        }
        if (package.Size() > MaximumPackageBytes)
        {
            package = new ZPackage();
            package.Write(requestId);
            package.Write(0);
        }
        ZRoutedRpc.instance.InvokeRoutedRPC(sender, ResponseRpc, package);
    }

    private static void HandleResponse(long sender, ZPackage package)
    {
        if (ZNet.instance == null || ZNet.instance.IsServer() || !WardOwnership.IsAuthoritativeServerSender(sender) ||
            !TryReadSnapshot(package, out var requestId, out var entries)) return;
        ClientCache.TryApply(requestId, entries, Now);
    }

    internal static bool TryReadSnapshot(ZPackage package, out long requestId, out List<WardGroupSnapshotEntry> entries)
    {
        requestId = 0;
        entries = new List<WardGroupSnapshotEntry>();
        if (package == null || package.Size() > MaximumPackageBytes) return false;
        try
        {
            requestId = package.ReadLong();
            var count = package.ReadInt();
            if (requestId <= 0 || count < 0 || count > WardGroupSnapshotCache.MaximumEntries) return false;
            entries.Capacity = count;
            for (var i = 0; i < count; i++)
            {
                var playerId = package.ReadLong();
                var sessionId = package.ReadLong();
                var character = package.ReadZDOID();
                var group = new WardGroupIdentity(package.ReadString(), package.ReadString(), package.ReadString());
                entries.Add(new WardGroupSnapshotEntry(playerId, sessionId, character.UserID, character.ID, group));
            }
            return package.GetPos() == package.Size();
        }
        catch (Exception)
        {
            // The caller never applies a partial parse. Existing trust expires.
            return false;
        }
    }
}
