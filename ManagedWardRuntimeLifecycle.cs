namespace STUWard;

internal static class ManagedWardRuntimeLifecycle
{
    private static ZRoutedRpc? _boundRoutedRpc;

    internal static void ResetSession()
    {
        _boundRoutedRpc = null;
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
