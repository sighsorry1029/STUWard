namespace STUWard;

/// <summary>Optional integration with STUWard's requester-specific container policy.</summary>
public static class WardAccessApi
{
    /// <summary>Identifies a STUWard-managed area without treating other wards as ours.</summary>
    public static bool IsManagedWard(PrivateArea area) => area != null && WardAccess.IsManagedWard(area, false);

    /// <summary>
    /// Returns whether STUWard handles this location, with its decision in allowed.
    /// Callers must authenticate playerId against the RPC sender and keep their own
    /// distance, vanilla ward, ownership and inventory mutation checks.
    /// </summary>
    public static bool TryCheckContainerAccess(Container container, long playerId, out bool allowed)
    {
        allowed = false;
        if (container == null || playerId == 0 || !WardAccess.HasEnabledManagedWards()) return false;
        var candidates = WardAccess.GetCandidateManagedWards(container.transform.position, 0f, requireEnabled: true);
        var access = WardAccess.EvaluateRestrictionAccessAgainstCandidates(
            WardRestrictionOptions.Containers, container.transform.position, 0f, playerId, candidates, flash: false);
        if (access.Decision == WardAccess.AccessDecision.NoWard) return false;
        allowed = !access.IsDenied;
        return true;
    }
}
