using System;
using System.Collections.Generic;

namespace STUWard;

internal static class WardAdminDebugAccess
{
    private const string RpcSetAdminDebugState = "STUWard_SetAdminDebugState";
    private const string RpcReceiveAdminDebugState = "STUWard_ReceiveAdminDebugState";
    private static readonly TimeSpan DebugStateResendInterval = TimeSpan.FromSeconds(3);

    private static readonly HashSet<long> ServerDebugAdminPlayerIds = new();

    private static bool _rpcsRegistered;
    private static bool? _lastLocalDebugAdminState;
    private static bool _serverApprovedLocalDebugState;
    private static DateTime _lastLocalDebugAdminSyncUtc = DateTime.MinValue;

    internal static void ResetRuntimeState()
    {
        _rpcsRegistered = false;
        _lastLocalDebugAdminState = null;
        _serverApprovedLocalDebugState = false;
        _lastLocalDebugAdminSyncUtc = DateTime.MinValue;
        ServerDebugAdminPlayerIds.Clear();
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterRpcs();
    }

    internal static void OnZNetAwake()
    {
        ResetRuntimeState();
        EnsureRuntimeBindings();
    }

    internal static void RegisterRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_rpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register<bool>(RpcSetAdminDebugState, HandleSetAdminDebugState);
        routedRpc.Register<bool>(RpcReceiveAdminDebugState, HandleReceiveAdminDebugState);
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

        if (ZNet.instance.IsServer())
        {
            if (!force && _lastLocalDebugAdminState.HasValue && _lastLocalDebugAdminState.Value == enabled)
            {
                return;
            }

            _lastLocalDebugAdminState = enabled;
            _lastLocalDebugAdminSyncUtc = now;
            SetServerAdminDebugState(player.GetPlayerID(), enabled);
            return;
        }

        var stateChanged = !_lastLocalDebugAdminState.HasValue || _lastLocalDebugAdminState.Value != enabled;
        var shouldResend = enabled &&
                           !_serverApprovedLocalDebugState &&
                           now - _lastLocalDebugAdminSyncUtc >= DebugStateResendInterval;
        if (!force && !stateChanged && !shouldResend)
        {
            return;
        }

        _lastLocalDebugAdminState = enabled;
        _lastLocalDebugAdminSyncUtc = now;
        RegisterRpcs();
        ZRoutedRpc.instance?.InvokeRoutedRPC(RpcSetAdminDebugState, enabled);
    }

    internal static bool CanLocallyControlAnyWard(PrivateArea? area, Player? player)
    {
        return area != null &&
               player != null &&
               player == Player.m_localPlayer &&
               WardAccess.IsManagedWard(area, false) &&
               IsLocalAdminDebugController(player);
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

        if (Player.m_localPlayer != null &&
            Player.m_localPlayer.GetPlayerID() == playerId &&
            IsLocalAdminDebugController(Player.m_localPlayer))
        {
            return true;
        }

        return ServerDebugAdminPlayerIds.Contains(playerId);
    }

    internal static void ForgetServerPlayer(long playerId)
    {
        if (playerId == 0L)
        {
            return;
        }

        if (ServerDebugAdminPlayerIds.Remove(playerId))
        {
            Plugin.LogWardDiagnosticVerbose(
                "AdminDebug.Sync",
                $"Removed stale admin debug control state for disconnected playerId={playerId}.");
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

        var accountId = WardOwnership.GetPlayerAccountId(player.GetPlayerID());
        return player != null &&
               (_serverApprovedLocalDebugState || IsAdminAccountId(accountId) || ZNet.instance.LocalPlayerIsAdminOrHost());
    }

    private static bool IsLocalAdminDebugRequested(Player? player)
    {
        return player != null &&
               player == Player.m_localPlayer &&
               Player.m_debugMode &&
               ZNet.instance != null;
    }

    private static void HandleSetAdminDebugState(long sender, bool enabled)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, "AdminDebug.Sync", out var playerId))
        {
            return;
        }

        if (!enabled)
        {
            ServerDebugAdminPlayerIds.Remove(playerId);
            SendAdminDebugStateResponse(sender, false);
            return;
        }

        var accountId = WardOwnership.GetAuthoritativeAccountIdFromSender(sender, playerId);
        if (!IsAdminAccountId(accountId))
        {
            ServerDebugAdminPlayerIds.Remove(playerId);
            Plugin.LogWardDiagnosticFailure(
                "AdminDebug.Sync",
                $"Rejected admin debug enable because the requesting account is not a server admin. sender={sender}, playerId={playerId}, accountId='{accountId}'");
            SendAdminDebugStateResponse(sender, false);
            return;
        }

        ServerDebugAdminPlayerIds.Add(playerId);
        Plugin.LogWardDiagnosticVerbose(
            "AdminDebug.Sync",
            $"Enabled admin debug control for playerId={playerId}, sender={sender}, accountId='{accountId}'.");
        SendAdminDebugStateResponse(sender, true);
    }

    private static void HandleReceiveAdminDebugState(long sender, bool enabled)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender))
        {
            Plugin.LogWardDiagnosticFailure(
                "AdminDebug.Sync",
                $"Rejected admin debug approval from a non-server sender. sender={sender}");
            return;
        }

        _serverApprovedLocalDebugState = enabled;
        _lastLocalDebugAdminSyncUtc = DateTime.UtcNow;
        Plugin.LogWardDiagnosticVerbose(
            "AdminDebug.Sync",
            $"Received admin debug approval state from server. enabled={enabled}");
    }

    private static void SendAdminDebugStateResponse(long receiverUid, bool enabled)
    {
        if (receiverUid == 0L)
        {
            return;
        }

        ZRoutedRpc.instance?.InvokeRoutedRPC(receiverUid, RpcReceiveAdminDebugState, enabled);
    }

    private static void SetServerAdminDebugState(long playerId, bool enabled)
    {
        if (playerId == 0L)
        {
            return;
        }

        if (enabled)
        {
            ServerDebugAdminPlayerIds.Add(playerId);
            return;
        }

        ServerDebugAdminPlayerIds.Remove(playerId);
    }

    internal static bool IsAdminAccountId(string accountId)
    {
        var adminList = ZNet.instance?.GetAdminList();
        if (adminList == null || string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        var normalizedTarget = NormalizeAccountId(accountId);
        var normalizedTargetCore = GetAccountIdCore(normalizedTarget);
        for (var index = 0; index < adminList.Count; index++)
        {
            var normalizedEntry = NormalizeAccountId(adminList[index]);
            if (string.Equals(normalizedEntry, normalizedTarget, StringComparison.Ordinal))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(normalizedTargetCore) &&
                string.Equals(GetAccountIdCore(normalizedEntry), normalizedTargetCore, StringComparison.Ordinal))
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

    private static string GetAccountIdCore(string accountId)
    {
        if (string.IsNullOrWhiteSpace(accountId))
        {
            return string.Empty;
        }

        var separatorIndex = accountId.IndexOf('_');
        if (separatorIndex < 0 || separatorIndex >= accountId.Length - 1)
        {
            return accountId;
        }

        return accountId.Substring(separatorIndex + 1);
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
