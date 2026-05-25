using HarmonyLib;
using UnityEngine;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    internal static void ResetRuntimeState()
    {
        ResetClientRuntimeState();
        _rpcsRegistered = false;
        _pendingServerViewerRefreshForAll = false;
        _lastServerViewerRefreshReason = null;
        ServerViewerSyncStatesByPeerUid.Clear();
        PendingServerViewerRefreshPeerUids.Clear();
        _serverViewerRefreshFlushAtUtc = System.DateTime.MinValue;
        Plugin.LogWardDiagnosticVerbose("WardPins.State", "Reset ward minimap pin manager state after ZNet awake.");
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

    internal static void Update()
    {
        ProcessPendingServerViewerRefreshes();
    }

    internal static bool HasPendingRuntimeWork()
    {
        return _pendingServerViewerRefreshForAll || PendingServerViewerRefreshPeerUids.Count > 0;
    }

    internal static void NotifyWardDataMayHaveChanged(string reason, bool refreshImmediatelyIfVisible = false)
    {
        NotifyLocalWardDataMayHaveChanged(reason, refreshImmediatelyIfVisible);
        QueueServerViewerRefreshRecipients(null, reason);
    }
}

[HarmonyPatch(typeof(Minimap), nameof(Minimap.SetMapMode))]
internal static class MinimapSetMapModeWardPinsPatch
{
    private static void Postfix(Minimap __instance, Minimap.MapMode mode)
    {
        WardMinimapPinsManager.HandleMapModeChanged(__instance, mode);
    }
}

[HarmonyPatch(typeof(Player), "Update")]
internal static class PlayerUpdateWardPinsPatch
{
    private static void Postfix(Player __instance)
    {
        WardMinimapPinsManager.UpdatePendingRemoteState(__instance);
    }
}
