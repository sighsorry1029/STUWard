using System;

namespace STUWard;

internal readonly struct ManagedWardProjection
{
    internal ManagedWardProjection(string accountId, bool hasResolvedGuild, WardGuildIdentity guild)
    {
        AccountId = accountId ?? string.Empty;
        HasResolvedGuild = hasResolvedGuild;
        Guild = guild;
    }

    internal string AccountId { get; }
    internal bool HasResolvedGuild { get; }
    internal WardGuildIdentity Guild { get; }
}

internal readonly struct ManagedWardProjectionApplyResult
{
    internal ManagedWardProjectionApplyResult(bool accountChanged, bool guildChanged)
    {
        AccountChanged = accountChanged;
        GuildChanged = guildChanged;
    }

    internal bool AccountChanged { get; }
    internal bool GuildChanged { get; }
    internal bool AnyChanged => AccountChanged || GuildChanged;
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
            return new ManagedWardProjection(string.Empty, hasResolvedGuild: false, default);
        }

        var ownerName = GuildsCompat.GetWardOwnerNameForProjection(zdo);
        if (GuildsCompat.TryResolveProjectedGuildIdentity(ownerPlayerId, normalizedAccountId, ownerName, out var guild))
        {
            return new ManagedWardProjection(normalizedAccountId, hasResolvedGuild: true, guild);
        }

        return new ManagedWardProjection(normalizedAccountId, hasResolvedGuild: false, default);
    }

    internal static ManagedWardProjection ResolveExplicitProjection(long ownerPlayerId, string wardSteamAccountId, WardGuildIdentity guild)
    {
        var canonicalOwnerAccountId = ownerPlayerId != 0L
            ? WardOwnership.GetPlayerAccountId(ownerPlayerId)
            : string.Empty;
        var normalizedAccountId = !string.IsNullOrWhiteSpace(canonicalOwnerAccountId)
            ? WardOwnership.NormalizeAccountIdValue(canonicalOwnerAccountId)
            : WardOwnership.NormalizeAccountIdValue(wardSteamAccountId);
        return new ManagedWardProjection(normalizedAccountId, hasResolvedGuild: true, guild);
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

        var guildChanged = false;
        if (projection.HasResolvedGuild)
        {
            guildChanged = ApplyProjectedGuildMetadata(zdo, projection.Guild);
        }

        return new ManagedWardProjectionApplyResult(accountChanged, guildChanged);
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

    private static bool ApplyProjectedGuildMetadata(ZDO zdo, WardGuildIdentity guild)
    {
        var changed = false;
        var currentGuildId = zdo.GetInt(GuildsCompat.GuildIdKey, 0);
        if (currentGuildId != guild.Id)
        {
            zdo.Set(GuildsCompat.GuildIdKey, guild.Id);
            changed = true;
        }

        var guildName = guild.Name ?? string.Empty;
        var currentGuildName = zdo.GetString(GuildsCompat.GuildNameKey, string.Empty);
        if (!string.Equals(currentGuildName, guildName, StringComparison.Ordinal))
        {
            zdo.Set(GuildsCompat.GuildNameKey, guildName);
            changed = true;
        }

        return changed;
    }
}
