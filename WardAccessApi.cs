using UnityEngine;

namespace STUWard;

/// <summary>Optional integration with STUWard's coverage and action policies.</summary>
public static class WardAccessApi
{
    /// <summary>
    /// Checks the blocked-item policy at both the actor and effect positions.
    /// Callers must authenticate playerId against the RPC sender, supply current
    /// positions, and keep their own distance and transaction validation.
    /// Coverage is limited to currently loaded wards, as with normal item checks.
    /// </summary>
    public static bool IsItemUseAllowed(string itemPrefab, long playerId, Vector3 playerPosition, Vector3 targetPosition)
    {
        if (!Plugin.IsReady || ZNet.instance == null || playerId == 0 ||
            !IsFinite(playerPosition) || !IsFinite(targetPosition)) return false;
        if (!WardItemPrefabPolicy.IsBlockedItem(itemPrefab)) return true;
        return !WardAccess.EvaluateAccess(playerPosition, 0f, playerId, flash: false).IsDenied &&
               !WardAccess.EvaluateAccess(targetPosition, 0f, playerId, flash: false).IsDenied;
    }

    private static bool IsFinite(Vector3 point) =>
        !float.IsNaN(point.x) && !float.IsInfinity(point.x) &&
        !float.IsNaN(point.y) && !float.IsInfinity(point.y) &&
        !float.IsNaN(point.z) && !float.IsInfinity(point.z);

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
    /// A tombstone's stored character owner can recover it without ward trust.
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
        allowed = !access.IsDenied || IsOwnTombstone(container, playerId);
        return true;
    }

    internal static bool IsOwnTombstone(Container container, long playerId)
    {
        if (container == null || playerId == 0) return false;
        var tombstone = container.GetComponent<TombStone>();
        // Match the exact inventory used by TombStone.Awake, not a container in
        // its parent/children or a second container attached by another mod.
        if (tombstone == null || tombstone.GetComponent<Container>() != container) return false;
        var view = tombstone.GetComponent<ZNetView>();
        // This is the deceased character ID, independent of the current ZDO owner.
        return view != null && view.IsValid() && view.GetZDO().GetLong(ZDOVars.s_owner, 0L) == playerId;
    }
}
