using System;
using System.Collections.Generic;

namespace STUWard;

internal static class WardAdminDebugAccess
{
    private const string RpcRequestAdminDebugState = "STUWard_RequestAdminDebugState";
    private const string RpcReceiveAdminDebugProjection = "STUWard_ReceiveAdminDebugProjection";
    private const string RpcReceiveAdminDebugSnapshot = "STUWard_ReceiveAdminDebugSnapshot";
    private const int MaxAdminDebugSnapshotEntries = 1024;
    private static readonly TimeSpan DebugStateResendInterval = TimeSpan.FromSeconds(3);

    private static readonly HashSet<long> ServerDebugAdminPlayerIds = new();
    // Only the server owns these live connection bindings. Clients consume the
    // existing player-id projection, never a locally inferred administrator list.
    private static readonly Dictionary<long, ZNetPeer> ServerAdminPeers = new();
    private static readonly Dictionary<long, string> LastServerDecisions = new();

    private static ZRoutedRpc? _registeredRoutedRpc;
    private static bool? _lastLocalDebugAdminState;
    private static bool _serverApprovedLocalDebugState;
    private static bool _hasReceivedAdminDebugSnapshot;
    private static DateTime _lastLocalDebugAdminSyncUtc = DateTime.MinValue;
    private static DateTime _responseWaitStartedUtc = DateTime.MinValue;
    private static DateTime _nextNoResponseWarningUtc = DateTime.MinValue;
    private static bool? _lastLoggedLocalApproval;

    internal static void ResetRuntimeState()
    {
        _registeredRoutedRpc = null;
        _lastLocalDebugAdminState = null;
        _serverApprovedLocalDebugState = false;
        _hasReceivedAdminDebugSnapshot = false;
        _lastLocalDebugAdminSyncUtc = DateTime.MinValue;
        _responseWaitStartedUtc = DateTime.MinValue;
        _nextNoResponseWarningUtc = DateTime.MinValue;
        _lastLoggedLocalApproval = null;
        ServerDebugAdminPlayerIds.Clear();
        ServerAdminPeers.Clear();
        LastServerDecisions.Clear();
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterRpcs();
    }

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || ReferenceEquals(_registeredRoutedRpc, routedRpc))
        {
            return;
        }

        routedRpc.Register<bool>(RpcRequestAdminDebugState, HandleRequestAdminDebugState);
        routedRpc.Register<long, bool>(RpcReceiveAdminDebugProjection, HandleReceiveAdminDebugProjection);
        routedRpc.Register<ZPackage>(RpcReceiveAdminDebugSnapshot, HandleReceiveAdminDebugSnapshot);
        _registeredRoutedRpc = routedRpc;
    }

    internal static void UpdateLocalState(Player? player, bool force = false)
    {
        if (player == null || player != Player.m_localPlayer || ZNet.instance == null)
        {
            return;
        }

        var enabled = IsLocalAdminDebugRequested(player);
        var now = DateTime.UtcNow;
        var stateChanged = !_lastLocalDebugAdminState.HasValue || _lastLocalDebugAdminState.Value != enabled;
        var resendIntervalElapsed = now - _lastLocalDebugAdminSyncUtc >= DebugStateResendInterval;
        var shouldResendEnabledState = enabled && resendIntervalElapsed;

        if (ZNet.instance.IsServer())
        {
            if (!force && !stateChanged && !shouldResendEnabledState)
            {
                return;
            }

            _lastLocalDebugAdminState = enabled;
            _lastLocalDebugAdminSyncUtc = now;
            SetServerAdminDebugState(player.GetPlayerID(), enabled);
            return;
        }

        var shouldRetrySnapshot = (!_hasReceivedAdminDebugSnapshot ||
                                   _responseWaitStartedUtc != DateTime.MinValue ||
                                   (!enabled && _serverApprovedLocalDebugState)) && resendIntervalElapsed;
        if (!force && !stateChanged && !shouldResendEnabledState && !shouldRetrySnapshot)
        {
            return;
        }

        RegisterRpcs();
        var routedRpc = ZRoutedRpc.instance;
        var server = routedRpc?.GetServerPeerID() ?? 0L;
        if (server == 0L) return;

        if (stateChanged)
        {
            Plugin.Log.LogInfo($"[admin-debug] Local debug requested={enabled}; waiting for server approval.");
            _lastLoggedLocalApproval = null;
        }
        if (_responseWaitStartedUtc == DateTime.MinValue)
        {
            _responseWaitStartedUtc = now;
            _nextNoResponseWarningUtc = now.AddSeconds(10);
        }
        else if (now >= _nextNoResponseWarningUtc)
        {
            Plugin.Log.LogWarning($"[admin-debug] No server approval response; requested={enabled}. Check STUWard on the server and its [admin-debug] log.");
            _nextNoResponseWarningUtc = now.AddSeconds(30);
        }

        _lastLocalDebugAdminState = enabled;
        _lastLocalDebugAdminSyncUtc = now;
        routedRpc!.InvokeRoutedRPC(server, RpcRequestAdminDebugState, enabled);
    }

    // UI/input preview path only. Server-side RPC validation remains authoritative.
    internal static bool CanLocallyAttemptAnyWardControl(PrivateArea? area, Player? player)
    {
        return area != null &&
               player != null &&
               player == Player.m_localPlayer &&
               WardAccess.IsManagedWard(area, false) &&
               Player.m_debugMode;
    }

    internal static bool IsPlayerAdminDebugController(long playerId)
    {
        if (playerId == 0L)
        {
            return false;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
        {
            return IsLocalAdminDebugController(localPlayer);
        }

        if (!ServerDebugAdminPlayerIds.Contains(playerId))
        {
            return false;
        }

        var znet = ZNet.instance;
        if (znet == null)
        {
            return false;
        }

        // Remote peers consume only the server-authoritative projection. Their
        // local admin list is not an authority for another player's account.
        if (!znet.IsServer())
        {
            return true;
        }

        if (ServerAdminPeers.TryGetValue(playerId, out var peer) &&
            TryAuthorizePeer(peer, out var currentPlayerId, out _, out _) && currentPlayerId == playerId)
        {
            return true;
        }

        Plugin.Log.LogInfo($"[admin-debug] Revoked playerId={playerId}: connection identity or server administrator permission changed.");
        SetServerAdminDebugState(playerId, false);
        return false;
    }

    internal static void ForgetServerPlayer(long playerId)
    {
        if (playerId == 0L)
        {
            return;
        }

        ServerAdminPeers.Remove(playerId);
        if (ServerDebugAdminPlayerIds.Remove(playerId))
        {
            BroadcastAdminDebugState(playerId, false);
        }
    }

    internal static void ForgetServerPeer(long sender)
    {
        LastServerDecisions.Remove(sender);
        List<long>? removed = null;
        foreach (var entry in ServerAdminPeers)
        {
            if (entry.Value.m_uid != sender) continue;
            (removed ??= new List<long>()).Add(entry.Key);
        }
        if (removed != null)
            foreach (var playerId in removed) ForgetServerPlayer(playerId);
    }

    private static bool IsLocalAdminDebugController(Player? player)
    {
        if (player == null || player != Player.m_localPlayer || !Player.m_debugMode || ZNet.instance == null)
        {
            return false;
        }

        if (ZNet.instance.IsServer())
        {
            return true;
        }

        return _serverApprovedLocalDebugState;
    }

    private static bool IsLocalAdminDebugRequested(Player? player)
    {
        return player != null &&
               player == Player.m_localPlayer &&
               Player.m_debugMode &&
               ZNet.instance != null;
    }

    private static void HandleRequestAdminDebugState(long sender, bool enabled)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var peer = ZNet.instance.GetPeer(sender);
        if (peer == null) return;
        var isAdmin = TryAuthorizePeer(peer, out var playerId, out var hostId, out var reason);
        var approved = enabled && isAdmin;
        if (!enabled && playerId != 0L) reason = "debug-off";
        LogServerDecision(sender, playerId, hostId, enabled, reason);
        if (playerId == 0L) return; // Retry when the character identity is ready.
        if (approved) ServerAdminPeers[playerId] = peer;
        SetServerAdminDebugState(playerId, approved);
        SendAdminDebugStateSnapshot(sender);
    }

    private static bool TryAuthorizePeer(ZNetPeer peer, out long playerId, out string hostId, out string reason)
    {
        playerId = 0L;
        hostId = string.Empty;
        reason = "peer-not-ready";
        var net = ZNet.instance;
        if (net == null || !net.IsServer() || !peer.IsReady() ||
            !ReferenceEquals(net.GetPeer(peer.m_uid), peer)) return false;
        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(peer.m_uid, out playerId))
        {
            reason = "identity-not-ready";
            return false;
        }
        try
        {
            hostId = peer.m_socket?.GetHostName() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(hostId))
            {
                reason = "host-id-unavailable";
                return false;
            }

            // Same live authenticated identity and policy as Valheim's remote
            // admin commands, including Server Devcommands' ListContainsId patch.
            // Do not override a native denial with cached or normalized account IDs.
            var approved = net.IsAdmin(hostId);
            reason = approved ? "approved" : "not-server-admin";
            return approved;
        }
        catch (Exception error)
        {
            reason = "admin-check-error:" + error.GetType().Name;
            return false;
        }
    }

    private static void LogServerDecision(long sender, long playerId, string hostId, bool requested, string reason)
    {
        // Native host IDs are accounts, not user-supplied character names. Bound
        // and sanitize diagnostics, and log changes instead of every heartbeat.
        var host = hostId.Length > 128 ? hostId.Substring(0, 128) : hostId;
        var decision = $"playerId={playerId} host='{host.Replace('\r', ' ').Replace('\n', ' ')}' requested={requested} result={reason}";
        if (LastServerDecisions.TryGetValue(sender, out var previous) && previous == decision) return;
        LastServerDecisions[sender] = decision;
        Plugin.Log.LogInfo($"[admin-debug] sender={sender} {decision}");
    }

    private static void HandleReceiveAdminDebugProjection(long sender, long playerId, bool enabled)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || playerId == 0L)
        {
            return;
        }

        if (enabled)
        {
            ServerDebugAdminPlayerIds.Add(playerId);
        }
        else
        {
            ServerDebugAdminPlayerIds.Remove(playerId);
        }

        UpdateLocalServerApproval(playerId, enabled);
    }

    private static void HandleReceiveAdminDebugSnapshot(long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || pkg == null)
        {
            return;
        }

        var projectedPlayerIds = new HashSet<long>();
        try
        {
            var count = pkg.ReadInt();
            if (count < 0 || count > MaxAdminDebugSnapshotEntries)
            {
                return;
            }

            for (var index = 0; index < count; index++)
            {
                var playerId = pkg.ReadLong();
                if (playerId != 0L)
                {
                    projectedPlayerIds.Add(playerId);
                }
            }
        }
        catch
        {
            return;
        }

        ServerDebugAdminPlayerIds.Clear();
        foreach (var playerId in projectedPlayerIds)
        {
            ServerDebugAdminPlayerIds.Add(playerId);
        }

        var localPlayerId = Player.m_localPlayer?.GetPlayerID() ?? 0L;
        // A snapshot received between character instances must also revoke the
        // old local approval; the notification helper has no player in that gap.
        _serverApprovedLocalDebugState = localPlayerId != 0L && projectedPlayerIds.Contains(localPlayerId);
        UpdateLocalServerApproval(localPlayerId, _serverApprovedLocalDebugState);
        _hasReceivedAdminDebugSnapshot = true;
        _lastLocalDebugAdminSyncUtc = DateTime.UtcNow;
    }

    private static void SetServerAdminDebugState(long playerId, bool enabled)
    {
        if (playerId == 0L)
        {
            return;
        }

        var changed = enabled
            ? ServerDebugAdminPlayerIds.Add(playerId)
            : ServerDebugAdminPlayerIds.Remove(playerId);
        if (!enabled) ServerAdminPeers.Remove(playerId);
        if (changed || enabled)
        {
            // Enabled clients heartbeat so peers that joined after the original
            // delta converge even if their first snapshot request raced identity setup.
            BroadcastAdminDebugState(playerId, enabled);
        }
    }

    private static void BroadcastAdminDebugState(long playerId, bool enabled)
    {
        var znet = ZNet.instance;
        if (playerId == 0L || znet == null || !znet.IsServer())
        {
            return;
        }

        ZRoutedRpc.instance?.InvokeRoutedRPC(
            ZRoutedRpc.Everybody,
            RpcReceiveAdminDebugProjection,
            playerId,
            enabled);
    }

    private static void SendAdminDebugStateSnapshot(long receiverUid)
    {
        var znet = ZNet.instance;
        if (receiverUid == 0L || znet == null || !znet.IsServer())
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(ServerDebugAdminPlayerIds.Count);
        foreach (var playerId in ServerDebugAdminPlayerIds)
        {
            pkg.Write(playerId);
        }

        ZRoutedRpc.instance?.InvokeRoutedRPC(receiverUid, RpcReceiveAdminDebugSnapshot, pkg);
    }

    private static void UpdateLocalServerApproval(long playerId, bool enabled)
    {
        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null || localPlayer.GetPlayerID() != playerId)
        {
            return;
        }

        _serverApprovedLocalDebugState = enabled;
        _lastLocalDebugAdminSyncUtc = DateTime.UtcNow;
        _responseWaitStartedUtc = DateTime.MinValue;
        if (_lastLoggedLocalApproval != enabled)
        {
            Plugin.Log.LogInfo($"[admin-debug] Server approval={enabled}; local debug requested={Player.m_debugMode}.");
            _lastLoggedLocalApproval = enabled;
        }
    }

    internal static bool IsAdminAccountId(string accountId)
    {
        var adminList = ZNet.instance?.GetAdminList();
        if (adminList == null || string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        var normalizedTarget = NormalizeAccountId(accountId);
        for (var index = 0; index < adminList.Count; index++)
        {
            var normalizedEntry = NormalizeAccountId(adminList[index]);
            if (string.Equals(normalizedEntry, normalizedTarget, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string NormalizeAccountId(string? rawAccountId)
    {
        if (string.IsNullOrWhiteSpace(rawAccountId))
        {
            return string.Empty;
        }

        return WardOwnership.NormalizeAccountIdValue(rawAccountId);
    }
}

[HarmonyLib.HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdateWardAdminDebugPatch
{
    private static void Postfix(Player __instance)
    {
        WardAdminDebugAccess.UpdateLocalState(__instance);
    }
}
