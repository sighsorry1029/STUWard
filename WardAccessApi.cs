using UnityEngine;

namespace STUWard;

/// <summary>Optional integration with STUWard's coverage and container policies.</summary>
public static class WardAccessApi
{
    /// <summary>
    /// Tests current, locally loaded active STUWard coverage using the spatial index.
    /// This is not an access grant or a promise to block every kind of damage.
    /// Callers delegating protection must leave STUWard's action/RPC checks in place.
    /// </summary>
    public static bool IsInsideActiveWard(Vector3 point)
    {
        if (!Plugin.IsReady || ZNet.instance == null) return false;
        var candidates = WardAccess.GetCandidateManagedWards(point, 0f, requireEnabled: true);
        for (int i = 0; i < candidates.Count; i++)
        {
            var area = candidates[i];
            // Membership may lag a received ZDO change until UpdateStatus runs.
            if (area != null && WardAccess.IsManagedWard(area, requireEnabled: true) && area.IsInside(point, 0f))
                return true;
        }
        return false;
    }

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
