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
        // Utils.GetSaveDataPath(Local) touches the platform cloud provider even
        // for local files, so the managed YAML must not be loaded during the
        // earlier BepInEx Awake phase. A server ZNet session also guarantees
        // that Valheim has already applied any -savedir override.
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
