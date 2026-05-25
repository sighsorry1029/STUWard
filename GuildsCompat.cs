using System;

namespace STUWard;

internal readonly struct CachedWardGuildIdentity
{
    internal CachedWardGuildIdentity(bool hasGuild, int guildId, string guildName, DateTime expiresAtUtc)
    {
        HasGuild = hasGuild;
        GuildId = guildId;
        GuildName = guildName;
        ExpiresAtUtc = expiresAtUtc;
    }

    internal bool HasGuild { get; }
    internal int GuildId { get; }
    internal string GuildName { get; }
    internal DateTime ExpiresAtUtc { get; }
}

internal readonly struct CachedPlayerPlatformIdentity
{
    internal CachedPlayerPlatformIdentity(bool hasPlatformId, string platformId, DateTime expiresAtUtc)
    {
        HasPlatformId = hasPlatformId;
        PlatformId = platformId;
        ExpiresAtUtc = expiresAtUtc;
    }

    internal bool HasPlatformId { get; }
    internal string PlatformId { get; }
    internal DateTime ExpiresAtUtc { get; }
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
    private const string GuildIdKey = "stuw_guild_id";
    private const string GuildNameKey = "stuw_guild_name";
    private static readonly TimeSpan GuildLookupCacheDuration = TimeSpan.FromSeconds(30);

    internal static void ResetRuntimeState()
    {
        PlayerGuildCache.Clear();
        PlayerPlatformIdCache.Clear();
        ResetPendingWardGuildProjectionRefreshes();
        ResetSyncedGuildState();
        _availabilityState = AvailabilityState.Unknown;
        _nextAvailabilityProbeUtc = DateTime.MinValue;
    }

    internal static void EnsureRuntimeBindings()
    {
        RegisterSyncRpcs();
    }

    internal static void OnZNetAwake()
    {
        ResetRuntimeState();
        EnsureRuntimeBindings();
    }

    internal static WardGuildIdentity GetPlayerGuildIdentity(Player? player)
    {
        return TryGetGuild(player, out var guild) ? guild : default;
    }

    internal static WardGuildIdentity GetPlayerGuildIdentity(long playerId)
    {
        return TryGetGuild(playerId, out var guild) ? guild : default;
    }

    internal static WardGuildIdentity GetWardGuildIdentity(PrivateArea? area)
    {
        return new WardGuildIdentity(GetWardGuildId(area), GetWardGuildName(area));
    }

    internal static WardGuildIdentity GetWardGuildIdentity(ZDO? zdo)
    {
        return new WardGuildIdentity(GetWardGuildId(zdo), GetWardGuildName(zdo));
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

    internal static string GetWardGuildName(ZDO? zdo)
    {
        return zdo?.GetString(GuildNameKey, string.Empty) ?? string.Empty;
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
