namespace STUWard;

internal static class ManagedWardMetadataMutationService
{
    internal static ManagedWardProjectionApplyResult ObserveAuthoritativeWard(
        ZDO? zdo,
        long ownerPlayerId,
        string wardSteamAccountId,
        bool authoritativeMetadataChanged,
        string reason,
        bool liveDisplayRefresh = false)
    {
        var projectionResult = ManagedWardProjectionService.RefreshProjection(zdo, ownerPlayerId, wardSteamAccountId);
        return FinalizeMutation(
            zdo,
            projectionResult,
            authoritativeMetadataChanged,
            reason,
            forceSendWhenMetadataChanged: true,
            notifyObserved: true,
            notifyPins: false,
            liveDisplayRefresh);
    }

    internal static ManagedWardProjectionApplyResult RefreshProjectedMetadata(
        ZDO? zdo,
        long ownerPlayerId,
        string wardSteamAccountId,
        string reason,
        bool forceSendWhenMetadataChanged = false,
        bool liveDisplayRefresh = false)
    {
        var projectionResult = ManagedWardProjectionService.RefreshProjection(zdo, ownerPlayerId, wardSteamAccountId);
        return FinalizeMutation(
            zdo,
            projectionResult,
            authoritativeMetadataChanged: false,
            reason,
            forceSendWhenMetadataChanged,
            notifyObserved: false,
            notifyPins: false,
            liveDisplayRefresh);
    }

    internal static ManagedWardProjectionApplyResult ApplyExplicitProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        string reason,
        bool forceSendWhenMetadataChanged = true,
        bool liveDisplayRefresh = false)
    {
        var projectionResult = ManagedWardProjectionService.ApplyProjection(zdo, projection);
        return FinalizeMutation(
            zdo,
            projectionResult,
            authoritativeMetadataChanged: false,
            reason,
            forceSendWhenMetadataChanged,
            notifyObserved: false,
            notifyPins: true,
            liveDisplayRefresh);
    }

    internal static ManagedWardProjectionApplyResult ApplyOwnedLocalProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        string reason,
        bool forceSendWhenMetadataChanged = true,
        bool liveDisplayRefresh = false)
    {
        var projectionResult = ManagedWardProjectionService.ApplyProjection(
            zdo,
            projection,
            requireServer: false);
        return FinalizeMutation(
            zdo,
            projectionResult,
            authoritativeMetadataChanged: false,
            reason,
            forceSendWhenMetadataChanged,
            notifyObserved: false,
            notifyPins: true,
            liveDisplayRefresh);
    }

    internal static void SynchronizeRegistryEntry(ZDO? zdo)
    {
        if (CanSynchronizeRegistry(zdo))
        {
            ManagedWardRegistry.UpsertEntry(zdo);
        }
    }

    private static ManagedWardProjectionApplyResult FinalizeMutation(
        ZDO? zdo,
        ManagedWardProjectionApplyResult projectionResult,
        bool authoritativeMetadataChanged,
        string reason,
        bool forceSendWhenMetadataChanged,
        bool notifyObserved,
        bool notifyPins,
        bool liveDisplayRefresh)
    {
        if (CanSynchronizeRegistry(zdo))
        {
            ManagedWardRegistry.UpsertEntry(zdo);
        }

        if (zdo != null &&
            zdo.IsValid() &&
            forceSendWhenMetadataChanged &&
            (authoritativeMetadataChanged || projectionResult.AnyChanged))
        {
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        }

        if (notifyObserved)
        {
            ManagedWardMapStateService.NotifyWardObserved(zdo, reason, liveDisplayRefresh);
        }
        else if (projectionResult.AnyChanged)
        {
            ManagedWardMapStateService.NotifyZdoWardMutation(zdo, reason, notifyPins, liveDisplayRefresh);
        }

        return projectionResult;
    }

    private static bool CanSynchronizeRegistry(ZDO? zdo)
    {
        return zdo != null &&
               zdo.IsValid() &&
               ZNet.instance != null &&
               ZNet.instance.IsServer() &&
               WardOwnership.IsManagedWardZdo(zdo);
    }
}
