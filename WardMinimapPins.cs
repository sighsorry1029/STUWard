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
        ServerViewerSyncStatesByPeerUid.Clear();
        PendingServerViewerRefreshPeerUids.Clear();
        _serverViewerRefreshFlushAtUtc = System.DateTime.MinValue;
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

    internal static void NotifyWardDataMayHaveChanged(bool refreshImmediatelyIfVisible = false)
    {
        NotifyLocalWardDataMayHaveChanged(refreshImmediatelyIfVisible);
        QueueServerViewerRefreshRecipients(null);
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
