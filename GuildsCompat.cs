using System;

namespace STUWard;

internal readonly struct CachedWardGuildIdentity
{
    internal CachedWardGuildIdentity(
        bool hasGuild,
        int guildId,
        string guildName,
        DateTime expiresAtUtc,
        bool authoritativeNoGuild)
    {
        HasGuild = hasGuild;
        GuildId = guildId;
        GuildName = guildName;
        ExpiresAtUtc = expiresAtUtc;
        AuthoritativeNoGuild = authoritativeNoGuild;
    }

    internal bool HasGuild { get; }
    internal int GuildId { get; }
    internal string GuildName { get; }
    internal DateTime ExpiresAtUtc { get; }
    internal bool AuthoritativeNoGuild { get; }
}

internal readonly struct WardGuildCharacterIdentity
{
    internal WardGuildCharacterIdentity(long playerId, string accountId, string playerName)
    {
        PlayerId = playerId;
        AccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        PlayerName = playerName?.Trim() ?? string.Empty;
    }

    internal long PlayerId { get; }
    internal string AccountId { get; }
    internal string PlayerName { get; }
    internal bool HasPlayerId => PlayerId != 0L;
    internal bool HasAccountAndName => !string.IsNullOrWhiteSpace(AccountId) && !string.IsNullOrWhiteSpace(PlayerName);
}

internal static partial class GuildsCompat
{
    internal const string GuildIdKey = "stuw_guild_id";
    internal const string GuildNameKey = "stuw_guild_name";
    private static readonly TimeSpan GuildLookupCacheDuration = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan TransientNoGuildLookupCacheDuration = TimeSpan.FromSeconds(1);

    internal static void ResetRuntimeState()
    {
        PlayerGuildCache.Clear();
        ResetPendingWardGuildProjectionRefreshes();
        ResetSyncedGuildState();
        _availabilityState = AvailabilityState.Unknown;
        _nextAvailabilityProbeUtc = DateTime.MinValue;
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterSyncRpcs();
    }

    internal static WardGuildIdentity GetPlayerGuildIdentity(Player? player)
    {
        return TryGetGuild(player, out var guild) ? guild : default;
    }

    internal static WardGuildIdentity GetPlayerGuildIdentity(long playerId)
    {
        return TryGetGuild(playerId, out var guild) ? guild : default;
    }

    internal static int GetPlayerGuildId(Player? player)
    {
        return TryGetGuild(player, out var guild) ? guild.Id : 0;
    }

    internal static int GetPlayerGuildId(long playerId)
    {
        return TryGetGuild(playerId, out var guild) ? guild.Id : 0;
    }

    internal static string GetPlayerGuildName(long playerId)
    {
        return TryGetGuild(playerId, out var guild) ? guild.Name : string.Empty;
    }

    internal static int GetWardGuildId(ZDO? zdo)
    {
        return zdo?.GetInt(GuildIdKey, 0) ?? 0;
    }

    internal static string BuildCharacterIdentityKey(string accountId, string playerName)
    {
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAccountId) || string.IsNullOrWhiteSpace(normalizedPlayerName))
        {
            return string.Empty;
        }

        return $"{normalizedAccountId}\n{normalizedPlayerName}";
    }
}
