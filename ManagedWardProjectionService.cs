using System;

namespace STUWard;

internal readonly struct ManagedWardProjection
{
    internal ManagedWardProjection(string accountId, bool hasResolvedGroup, WardGroupIdentity group)
    {
        AccountId = accountId ?? string.Empty;
        HasResolvedGroup = hasResolvedGroup;
        Group = group;
    }

    internal string AccountId { get; }
    internal bool HasResolvedGroup { get; }
    internal WardGroupIdentity Group { get; }
}

internal readonly struct ManagedWardProjectionApplyResult
{
    internal ManagedWardProjectionApplyResult(bool accountChanged, bool groupChanged)
    {
        AccountChanged = accountChanged;
        GroupChanged = groupChanged;
    }

    internal bool AccountChanged { get; }
    internal bool GroupChanged { get; }
    internal bool AnyChanged => AccountChanged || GroupChanged;
}

internal static class ManagedWardProjectionService
{
    internal static ManagedWardProjection ResolveProjection(ZDO? zdo, long ownerPlayerId, string wardSteamAccountId)
    {
        if (zdo == null)
        {
            return default;
        }

        var canonicalOwnerAccountId = ownerPlayerId != 0L ? WardOwnership.GetPlayerAccountId(ownerPlayerId) : string.Empty;
        var normalizedAccountId = !string.IsNullOrWhiteSpace(canonicalOwnerAccountId)
            ? WardOwnership.NormalizeAccountIdValue(canonicalOwnerAccountId)
            : WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId, wardSteamAccountId);
        if (string.IsNullOrWhiteSpace(normalizedAccountId))
        {
            return new ManagedWardProjection(string.Empty, hasResolvedGroup: false, default);
        }

        var ownerName = GuildsCompat.GetWardOwnerNameForProjection(zdo);
        if (WardGroupCompat.TryResolveProjectedGroupIdentity(ownerPlayerId, normalizedAccountId, ownerName, out var group))
        {
            return new ManagedWardProjection(normalizedAccountId, hasResolvedGroup: true, group);
        }

        return new ManagedWardProjection(normalizedAccountId, hasResolvedGroup: false, default);
    }

    internal static ManagedWardProjection ResolveExplicitProjection(
        long ownerPlayerId,
        string wardSteamAccountId,
        WardGroupIdentity group)
    {
        var canonicalOwnerAccountId = ownerPlayerId != 0L
            ? WardOwnership.GetPlayerAccountId(ownerPlayerId)
            : string.Empty;
        var normalizedAccountId = !string.IsNullOrWhiteSpace(canonicalOwnerAccountId)
            ? WardOwnership.NormalizeAccountIdValue(canonicalOwnerAccountId)
            : WardOwnership.NormalizeAccountIdValue(wardSteamAccountId);
        return new ManagedWardProjection(normalizedAccountId, hasResolvedGroup: true, group);
    }

    internal static ManagedWardProjectionApplyResult RefreshProjection(ZDO? zdo, long ownerPlayerId, string wardSteamAccountId)
    {
        return ApplyProjection(zdo, ResolveProjection(zdo, ownerPlayerId, wardSteamAccountId));
    }

    internal static ManagedWardProjectionApplyResult ApplyProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        bool requireServer = true)
    {
        if (zdo == null || (requireServer && (ZNet.instance == null || !ZNet.instance.IsServer())))
        {
            return default;
        }

        var accountChanged = false;
        if (!string.IsNullOrWhiteSpace(projection.AccountId) &&
            !string.Equals(WardOwnership.GetWardSteamAccountId(zdo), projection.AccountId, StringComparison.Ordinal))
        {
            zdo.Set(WardOwnership.SteamAccountIdKey, projection.AccountId);
            accountChanged = true;
        }

        var groupChanged = false;
        if (projection.HasResolvedGroup)
        {
            groupChanged = WardGroupCompat.ApplyProjectedGroupMetadata(zdo, projection.Group);
        }

        return new ManagedWardProjectionApplyResult(accountChanged, groupChanged);
    }

    internal static ManagedWardProjectionApplyResult ObserveAuthoritativeWard(
        ZDO? zdo,
        long ownerPlayerId,
        string wardSteamAccountId,
        bool authoritativeMetadataChanged,
        bool liveDisplayRefresh = false)
    {
        return FinalizeMutation(
            zdo,
            RefreshProjection(zdo, ownerPlayerId, wardSteamAccountId),
            authoritativeMetadataChanged,
            forceSendWhenMetadataChanged: true,
            notifyObserved: true,
            notifyPins: false,
            liveDisplayRefresh);
    }

    internal static ManagedWardProjectionApplyResult RefreshProjectedMetadata(
        ZDO? zdo,
        long ownerPlayerId,
        string wardSteamAccountId,
        bool forceSendWhenMetadataChanged = false,
        bool liveDisplayRefresh = false)
    {
        return FinalizeMutation(
            zdo,
            RefreshProjection(zdo, ownerPlayerId, wardSteamAccountId),
            authoritativeMetadataChanged: false,
            forceSendWhenMetadataChanged,
            notifyObserved: false,
            notifyPins: false,
            liveDisplayRefresh);
    }

    internal static ManagedWardProjectionApplyResult ApplyOwnedLocalProjection(
        ZDO? zdo,
        ManagedWardProjection projection,
        bool forceSendWhenMetadataChanged = true,
        bool liveDisplayRefresh = false)
    {
        return FinalizeMutation(
            zdo,
            ApplyProjection(zdo, projection, requireServer: false),
            authoritativeMetadataChanged: false,
            forceSendWhenMetadataChanged,
            notifyObserved: false,
            notifyPins: true,
            liveDisplayRefresh);
    }

    private static ManagedWardProjectionApplyResult FinalizeMutation(
        ZDO? zdo,
        ManagedWardProjectionApplyResult projectionResult,
        bool authoritativeMetadataChanged,
        bool forceSendWhenMetadataChanged,
        bool notifyObserved,
        bool notifyPins,
        bool liveDisplayRefresh)
    {
        ManagedWardRegistry.UpsertEntry(zdo);

        if (zdo != null &&
            zdo.IsValid() &&
            forceSendWhenMetadataChanged &&
            (authoritativeMetadataChanged || projectionResult.AnyChanged))
        {
            ZDOMan.instance?.ForceSendZDO(zdo.m_uid);
        }

        if (notifyObserved)
        {
            ManagedWardMapStateService.NotifyWardMutation(zdo, notifyPins: true, liveDisplayRefresh);
        }
        else if (projectionResult.AnyChanged)
        {
            ManagedWardMapStateService.NotifyWardMutation(zdo, notifyPins, liveDisplayRefresh);
        }

        return projectionResult;
    }
}
