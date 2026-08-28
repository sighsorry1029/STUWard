namespace STUWard;

internal static class ManagedWardRuntimeLifecycle
{
    private static ZRoutedRpc? _boundRoutedRpc;

    internal static void ResetSession()
    {
        _boundRoutedRpc = null;
        ManagedWardConfigFileService.Shutdown();
        DoorRpcUseDoorPatch.Reset();
        ManagedWardInteractionRpc.ResetLocalInteractionState();
        WardSettings.ResetLocalBoundaryFlashState();
        ManagedWardRuntimeContexts.Reset();
        WardAccess.ResetManagedWardCache();
        WardPermittedSnapshots.ClearCache();
        WardPrivateAreaSafeAccess.ResetRuntimeState();
        WardRecentPlayers.ResetRuntimeState();

        WardAdminDebugAccess.ResetRuntimeState();
        ManagedWardReportService.ResetRuntimeState();
        WardOwnership.ResetRuntimeState();

        GuildsCompat.ResetRuntimeState();
        WardGroupCompat.ResetRuntimeState();

        WardMinimapPinsManager.ResetRuntimeState();
        WardMinimapVisibilityIndex.ResetRuntimeState();
    }

    internal static void BindNetwork()
    {
        // Managed YAML is authoritative server state. Wait for a server ZNet
        // session so remote clients never initialize or monitor local copies.
        if (ZNet.instance?.IsServer() == true)
        {
            ManagedWardConfigFileService.Initialize();
        }

        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null || ReferenceEquals(_boundRoutedRpc, routedRpc))
        {
            return;
        }

        WardAdminDebugAccess.EnsureRuntimeBindings();
        WardOwnership.RegisterRpcs();
        ManagedWardInteractionRpc.RegisterRoutedRpcs(routedRpc);
        WardSettings.RegisterRoutedRpcs(routedRpc);
        WardRecentPlayers.RegisterRpcs();
        ManagedWardReportService.RegisterRpcs();

        GuildsCompat.EnsureRuntimeBindings();
        WardGroupCompat.EnsureRuntimeBindings();

        WardMinimapPinsManager.EnsureRuntimeBindings();
        _boundRoutedRpc = routedRpc;
    }

    internal static void Update()
    {
        BindNetwork();
        ManagedWardConfigFileService.Update();
        WardRecentPlayers.Update();

        if (WardPermittedSnapshots.HasPendingRuntimeWork())
        {
            WardPermittedSnapshots.Update();
        }

        if (WardOwnership.HasPendingRuntimeWork())
        {
            WardOwnership.Update();
        }

        WardSettings.UpdateLocalBoundaryFlash();
        GuildsCompat.Update();
        WardGroupCompat.Update();
        ManagedWardPresenceService.Update();

        if (WardMinimapPinsManager.HasPendingRuntimeWork())
        {
            WardMinimapPinsManager.Update();
        }
    }
}
