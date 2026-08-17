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

    private static bool _rpcsRegistered;
    private static bool? _lastLocalDebugAdminState;
    private static bool _serverApprovedLocalDebugState;
    private static bool _hasReceivedAdminDebugSnapshot;
    private static DateTime _lastLocalDebugAdminSyncUtc = DateTime.MinValue;

    internal static void ResetRuntimeState()
    {
        _rpcsRegistered = false;
        _lastLocalDebugAdminState = null;
        _serverApprovedLocalDebugState = false;
        _hasReceivedAdminDebugSnapshot = false;
        _lastLocalDebugAdminSyncUtc = DateTime.MinValue;
        ServerDebugAdminPlayerIds.Clear();
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterRpcs();
    }

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register<bool>(RpcRequestAdminDebugState, HandleRequestAdminDebugState);
        routedRpc.Register<long, bool>(RpcReceiveAdminDebugProjection, HandleReceiveAdminDebugProjection);
        routedRpc.Register<ZPackage>(RpcReceiveAdminDebugSnapshot, HandleReceiveAdminDebugSnapshot);
        _rpcsRegistered = true;
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

        var shouldRetrySnapshot = !_hasReceivedAdminDebugSnapshot && resendIntervalElapsed;
        if (!force && !stateChanged && !shouldResendEnabledState && !shouldRetrySnapshot)
        {
            return;
        }

        _lastLocalDebugAdminState = enabled;
        _lastLocalDebugAdminSyncUtc = now;
        RegisterRpcs();
        ZRoutedRpc.instance?.InvokeRoutedRPC(RpcRequestAdminDebugState, enabled);
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

        var accountId = WardOwnership.GetPlayerAccountId(playerId);
        if (IsAdminAccountId(accountId))
        {
            return true;
        }

        SetServerAdminDebugState(playerId, false);
        return false;
    }

    internal static void ForgetServerPlayer(long playerId)
    {
        if (playerId == 0L)
        {
            return;
        }

        if (ServerDebugAdminPlayerIds.Remove(playerId))
        {
            BroadcastAdminDebugState(playerId, false);
        }
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

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out var playerId))
        {
            return;
        }

        var accountId = WardOwnership.GetAuthoritativeAccountIdFromSender(sender, playerId);
        var approved = enabled && IsAdminAccountId(accountId);
        SetServerAdminDebugState(playerId, approved);
        SendAdminDebugStateSnapshot(sender);
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
        _serverApprovedLocalDebugState = localPlayerId != 0L && projectedPlayerIds.Contains(localPlayerId);
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
