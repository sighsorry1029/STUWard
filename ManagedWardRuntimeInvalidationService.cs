namespace STUWard;

internal static class ManagedWardRuntimeInvalidationService
{
    internal static void InvalidatePresence()
    {
        InvalidatePresenceCache();
    }

    internal static void ResetPresence()
    {
        ManagedWardPresenceService.ResetRuntimeState();
    }

    internal static void InvalidatePlacementPreview()
    {
        InvalidatePlacementPreviewCache();
    }

    internal static void PublishWardEnabledChanged(ManagedWardRef ward, string reason)
    {
        _ = ward;
        _ = reason;
        InvalidatePresenceCache();
        InvalidatePlacementPreviewCache();
    }

    internal static void PublishWardPermissionsChanged(ManagedWardRef ward, string reason)
    {
        _ = ward;
        _ = reason;
        InvalidatePresenceCache();
    }

    internal static void PublishWardRadiusChanged(ManagedWardRef ward, string reason)
    {
        _ = ward;
        _ = reason;
        InvalidatePresenceCache();
        InvalidatePlacementPreviewCache();
    }

    internal static void PublishPresencePolicyChanged(string reason)
    {
        _ = reason;
        InvalidatePresenceCache();
    }

    internal static void PublishSpatialIndexChanged(string reason)
    {
        _ = reason;
        InvalidatePlacementPreviewCache();
    }

    internal static void PublishPlacementPolicyChanged(string reason)
    {
        _ = reason;
        InvalidatePlacementPreviewCache();
    }

    private static void InvalidatePresenceCache()
    {
        ManagedWardPresenceService.Invalidate();
    }

    private static void InvalidatePlacementPreviewCache()
    {
        ManagedWardPlacementPreviewService.Invalidate();
    }
}
