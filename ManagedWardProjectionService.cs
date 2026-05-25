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
    private const string OwnerAccountIdKey = "stuw_owner_account_id";
    private const string GuildIdKey = "stuw_guild_id";
    private const string GuildNameKey = "stuw_guild_name";

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
        var normalizedAccountId = ownerPlayerId != 0L
            ? WardOwnership.NormalizeAccountIdValue(WardOwnership.GetPlayerAccountId(ownerPlayerId))
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
            zdo.Set(OwnerAccountIdKey, projection.AccountId);
            accountChanged = true;
        }

        var guildChanged = false;
        if (projection.HasResolvedGuild)
        {
            guildChanged = ApplyProjectedGuildMetadata(zdo, projection.Guild);
        }

        return new ManagedWardProjectionApplyResult(accountChanged, guildChanged);
    }

    private static bool ApplyProjectedGuildMetadata(ZDO zdo, WardGuildIdentity guild)
    {
        var changed = false;
        var currentGuildId = zdo.GetInt(GuildIdKey, 0);
        if (currentGuildId != guild.Id)
        {
            zdo.Set(GuildIdKey, guild.Id);
            changed = true;
        }

        var guildName = guild.Name ?? string.Empty;
        var currentGuildName = zdo.GetString(GuildNameKey, string.Empty);
        if (!string.Equals(currentGuildName, guildName, StringComparison.Ordinal))
        {
            zdo.Set(GuildNameKey, guildName);
            changed = true;
        }

        return changed;
    }
}
