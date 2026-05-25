namespace STUWard;

internal readonly struct ManagedWardMetadataMutationResult
{
    internal ManagedWardMetadataMutationResult(
        ManagedWardProjectionApplyResult projectionResult,
        bool authoritativeMetadataChanged,
        bool registrySynchronized,
        bool fastSendTriggered)
    {
        ProjectionResult = projectionResult;
        AuthoritativeMetadataChanged = authoritativeMetadataChanged;
        RegistrySynchronized = registrySynchronized;
        FastSendTriggered = fastSendTriggered;
    }

    internal ManagedWardProjectionApplyResult ProjectionResult { get; }
    internal bool AuthoritativeMetadataChanged { get; }
    internal bool RegistrySynchronized { get; }
    internal bool FastSendTriggered { get; }
    internal bool AnyMetadataChanged => AuthoritativeMetadataChanged || ProjectionResult.AnyChanged;
}

internal static class ManagedWardMetadataMutationService
{
    internal static ManagedWardMetadataMutationResult ObserveAuthoritativeWard(
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
            mutationKind: null,
            liveDisplayRefresh);
    }

    internal static ManagedWardMetadataMutationResult RefreshProjectedMetadata(
        ZDO? zdo,
        long ownerPlayerId,
        string wardSteamAccountId,
        ManagedWardMapMutationKind mutationKind,
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
            mutationKind,
            liveDisplayRefresh);
    }

    internal static ManagedWardMetadataMutationResult ApplyExplicitProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        ManagedWardMapMutationKind mutationKind,
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
            mutationKind,
            liveDisplayRefresh);
    }

    internal static ManagedWardMetadataMutationResult ApplyOwnedLocalProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        ManagedWardMapMutationKind mutationKind,
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
            mutationKind,
            liveDisplayRefresh);
    }

    internal static void SynchronizeRegistryEntry(ZDO? zdo)
    {
        if (!CanSynchronizeRegistry(zdo))
        {
            return;
        }

        ManagedWardRegistry.UpsertEntry(zdo);
    }

    private static ManagedWardMetadataMutationResult FinalizeMutation(
        ZDO? zdo,
        ManagedWardProjectionApplyResult projectionResult,
        bool authoritativeMetadataChanged,
        string reason,
        bool forceSendWhenMetadataChanged,
        bool notifyObserved,
        ManagedWardMapMutationKind? mutationKind,
        bool liveDisplayRefresh)
    {
        var registrySynchronized = false;
        if (CanSynchronizeRegistry(zdo))
        {
            ManagedWardRegistry.UpsertEntry(zdo);
            registrySynchronized = true;
        }

        var fastSendTriggered = false;
        if (zdo != null &&
            zdo.IsValid() &&
            forceSendWhenMetadataChanged &&
            (authoritativeMetadataChanged || projectionResult.AnyChanged))
        {
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
            fastSendTriggered = true;
        }

        if (notifyObserved)
        {
            ManagedWardMapStateService.NotifyWardObserved(zdo, reason, liveDisplayRefresh);
        }
        else if (mutationKind.HasValue && projectionResult.AnyChanged)
        {
            ManagedWardMapStateService.NotifyZdoWardMutation(zdo, mutationKind.Value, reason, liveDisplayRefresh);
        }

        return new ManagedWardMetadataMutationResult(
            projectionResult,
            authoritativeMetadataChanged,
            registrySynchronized,
            fastSendTriggered);
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
