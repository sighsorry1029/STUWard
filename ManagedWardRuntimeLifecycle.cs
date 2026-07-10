namespace STUWard;

internal static class ManagedWardRuntimeLifecycle
{
    internal static void ResetSession()
    {
        ManagedWardInteractionRpc.ResetLocalInteractionState();
        ManagedWardRuntimeContexts.Reset();
        WardAccess.ResetManagedWardCache();
        WardPermittedSnapshots.ClearCache();
        WardPrivateAreaSafeAccess.ResetRuntimeState();

        WardAdminDebugAccess.ResetRuntimeState();
        WardOwnership.ResetRuntimeState();

        GuildsCompat.ResetRuntimeState();

        WardMinimapPinsManager.ResetRuntimeState();
        WardMinimapVisibilityIndex.ResetRuntimeState();
    }

    internal static void BindNetwork()
    {
        WardAdminDebugAccess.EnsureRuntimeBindings();
        WardOwnership.EnsureRuntimeBindings();

        GuildsCompat.EnsureRuntimeBindings();

        WardMinimapPinsManager.EnsureRuntimeBindings();
    }

    internal static void Update()
    {
        ManagedWardConfigFileService.Update();

        if (WardPermittedSnapshots.HasPendingRuntimeWork())
        {
            WardPermittedSnapshots.Update();
        }

        if (WardOwnership.HasPendingRuntimeWork())
        {
            WardOwnership.Update();
        }

        GuildsCompat.Update();
        ManagedWardPresenceService.Update();

        if (WardMinimapPinsManager.HasPendingRuntimeWork())
        {
            WardMinimapPinsManager.Update();
        }
    }
}
