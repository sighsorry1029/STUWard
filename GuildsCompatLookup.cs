using System;
using System.Collections.Generic;

namespace STUWard;

internal static partial class GuildsCompat
{
    private static readonly Dictionary<long, CachedWardGuildIdentity> PlayerGuildCache = new();

    private static bool TryGetGuild(Player? player, out WardGuildIdentity guild)
    {
        guild = default;
        if (player == null)
        {
            return false;
        }

        var playerId = player.GetPlayerID();
        if (TryGetCachedGuild(playerId, out guild))
        {
            return true;
        }

        if (IsCachedAuthoritativeNoGuild(playerId))
        {
            return false;
        }

        if (IsCachedTransientNoGuild(playerId))
        {
            return false;
        }

        if (TryResolveGuildByPlayerFromApi(player, out guild))
        {
            var hasGuild = guild.Id != 0;
            CacheResolvedGuildLookup(playerId, hasGuild, guild);
            return hasGuild;
        }

        var accountId = WardOwnership.GetPlayerAccountId(player);
        var playerName = player.GetPlayerName();
        if (!string.IsNullOrWhiteSpace(accountId) &&
            !string.IsNullOrWhiteSpace(playerName) &&
            TryResolveGuildByAccountAndName(accountId, playerName, out guild))
        {
            var hasGuild = guild.Id != 0;
            CacheResolvedGuildLookup(playerId, hasGuild, guild);
            return hasGuild;
        }

        // Missing identity, an unavailable API, and reflection failures are
        // unresolved states. Do not turn them into a 30-second no-guild result.
        return false;
    }

    private static bool TryGetGuild(long playerId, out WardGuildIdentity guild)
    {
        guild = default;
        if (playerId == 0L)
        {
            return false;
        }

        var accountId = WardOwnership.GetPlayerAccountId(playerId);
        var playerName = WardOwnership.GetPlayerName(playerId);
        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            TryGetSyncedGuildIdentity(playerId, accountId, playerName, out guild))
        {
            CacheGuildLookup(
                playerId,
                guild.Id != 0,
                guild,
                authoritativeNoGuild: guild.Id == 0);
            return guild.Id != 0;
        }

        if (TryGetCachedGuild(playerId, out guild))
        {
            return true;
        }

        if (IsCachedAuthoritativeNoGuild(playerId))
        {
            return false;
        }

        if (IsCachedTransientNoGuild(playerId))
        {
            return false;
        }

        // A remote player instantiated on this peer is a better identity source
        // than reconstructing a PlayerReference from account/name strings. Only
        // unresolved or confirmed no-guild results reach this lookup, so normal
        // access checks still benefit from the short positive cache above.
        var livePlayer = Player.GetPlayer(playerId);
        if (livePlayer != null)
        {
            if (TryResolveGuildByPlayerFromApi(livePlayer, out guild))
            {
                var hasGuild = guild.Id != 0;
                CacheResolvedGuildLookup(playerId, hasGuild, guild);
                return hasGuild;
            }
        }

        if (!IsAvailable() || GetPlayerGuildByReferenceMethod == null || PlayerReferenceFromStringMethod == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            return false;
        }

        if (TryResolveGuildByAccountAndName(accountId, playerName, out guild))
        {
            var hasGuild = guild.Id != 0;
            CacheResolvedGuildLookup(playerId, hasGuild, guild);
            return hasGuild;
        }

        return false;
    }

    private static bool TryGetGuildByAccountAndName(string accountId, string playerName, out WardGuildIdentity guild)
    {
        return TryResolveGuildByAccountAndName(accountId, playerName, out guild) && guild.Id != 0;
    }

    private static bool TryResolveGuildByAccountAndName(
        string accountId,
        string playerName,
        out WardGuildIdentity guild)
    {
        guild = default;
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            TryGetSyncedGuildIdentity(normalizedAccountId, normalizedPlayerName, out guild))
        {
            return true;
        }

        return TryResolveGuildByAccountAndNameFromApi(
            normalizedAccountId,
            normalizedPlayerName,
            out guild);
    }

    internal static bool TryResolveAuthoritativeGuildIdentity(
        long playerId,
        string accountId,
        string playerName,
        out WardGuildIdentity guild)
    {
        guild = default;
        if (!IsAvailable())
        {
            return false;
        }

        var player = Player.GetPlayer(playerId);
        if (player != null && TryResolveGuildByPlayerFromApi(player, out guild))
        {
            return true;
        }

        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        return TryResolveGuildByAccountAndNameFromApi(
            normalizedAccountId,
            normalizedPlayerName,
            out guild);
    }

    internal static bool TryResolveCachedAuthoritativeGuildIdentity(
        long playerId,
        string accountId,
        string playerName,
        out WardGuildIdentity guild)
    {
        guild = default;
        if (playerId == 0L)
        {
            return false;
        }

        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            TryGetSyncedGuildIdentity(
                playerId,
                normalizedAccountId,
                normalizedPlayerName,
                out guild))
        {
            CacheGuildLookup(
                playerId,
                guild.Id != 0,
                guild,
                authoritativeNoGuild: guild.Id == 0);
            return true;
        }

        if (TryGetCachedGuild(playerId, out guild))
        {
            return true;
        }

        if (IsCachedAuthoritativeNoGuild(playerId))
        {
            return true;
        }

        if (!TryResolveAuthoritativeGuildIdentity(
                playerId,
                normalizedAccountId,
                normalizedPlayerName,
                out guild))
        {
            return false;
        }

        CacheGuildLookup(
            playerId,
            guild.Id != 0,
            guild,
            authoritativeNoGuild: guild.Id == 0);
        return true;
    }

    private static bool TryResolveGuildByPlayerFromApi(Player player, out WardGuildIdentity guild)
    {
        guild = default;
        if (player == null || GetPlayerGuildByPlayerMethod == null || !IsAvailable())
        {
            return false;
        }

        try
        {
            var guildObject = GetPlayerGuildByPlayerMethod.Invoke(null, new object[] { player });
            return guildObject == null || TryParseGuild(guildObject, out guild);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryResolveGuildByAccountAndNameFromApi(
        string accountId,
        string playerName,
        out WardGuildIdentity guild)
    {
        guild = default;
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedAccountId) ||
            string.IsNullOrWhiteSpace(normalizedPlayerName) ||
            GetPlayerGuildByReferenceMethod == null ||
            PlayerReferenceFromStringMethod == null ||
            !IsAvailable())
        {
            return false;
        }

        return TryResolveGuildByReferenceId(
            GuildIdentityPolicy.GetGuildsAccountId(normalizedAccountId),
            normalizedPlayerName,
            out guild);
    }

    private static bool TryResolveGuildByReferenceId(
        string accountId,
        string playerName,
        out WardGuildIdentity guild)
    {
        guild = default;
        try
        {
            var playerReference = PlayerReferenceFromStringMethod!.Invoke(
                null,
                new object[] { $"{accountId}:{playerName}" });
            var guildObject = GetPlayerGuildByReferenceMethod!.Invoke(null, new[] { playerReference! });
            return guildObject == null || TryParseGuild(guildObject, out guild);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetGuildById(int guildId, out WardGuildIdentity guild)
    {
        guild = default;
        if (guildId == 0 || GetGuildByIdMethod == null || !IsAvailable())
        {
            return false;
        }

        try
        {
            var guildObject = GetGuildByIdMethod.Invoke(null, new object[] { guildId });
            return TryParseGuild(guildObject, out guild);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetCachedGuild(long playerId, out WardGuildIdentity guild)
    {
        guild = default;
        if (!PlayerGuildCache.TryGetValue(playerId, out var cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= DateTime.UtcNow)
        {
            PlayerGuildCache.Remove(playerId);
            return false;
        }

        if (!cached.HasGuild || cached.GuildId == 0)
        {
            return false;
        }

        guild = new WardGuildIdentity(cached.GuildId, cached.GuildName);
        return true;
    }

    private static bool IsCachedAuthoritativeNoGuild(long playerId)
    {
        if (!PlayerGuildCache.TryGetValue(playerId, out var cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= DateTime.UtcNow)
        {
            PlayerGuildCache.Remove(playerId);
            return false;
        }

        return !cached.HasGuild && cached.AuthoritativeNoGuild;
    }

    private static bool IsCachedTransientNoGuild(long playerId)
    {
        if (!PlayerGuildCache.TryGetValue(playerId, out var cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= DateTime.UtcNow)
        {
            PlayerGuildCache.Remove(playerId);
            return false;
        }

        return !cached.HasGuild && !cached.AuthoritativeNoGuild;
    }

    private static void CacheGuildLookup(
        long playerId,
        bool hasGuild,
        WardGuildIdentity guild,
        bool authoritativeNoGuild = false)
    {
        if (playerId == 0L)
        {
            return;
        }

        PlayerGuildCache[playerId] = new CachedWardGuildIdentity(
            hasGuild && guild.Id != 0,
            guild.Id,
            guild.Name ?? string.Empty,
            DateTime.UtcNow + GuildLookupCacheDuration,
            authoritativeNoGuild && !hasGuild);
    }

    private static void CacheResolvedGuildLookup(
        long playerId,
        bool hasGuild,
        WardGuildIdentity guild)
    {
        // Guilds.API.IsLoaded() does not mean its ServerSync guild list has
        // reached this client. Keep a short retry delay for null results so an
        // unsynchronized late join cannot become a 30-second denial, while a
        // truly guildless client also avoids a reflection lookup every frame.
        if (!hasGuild && (ZNet.instance == null || !ZNet.instance.IsServer()))
        {
            PlayerGuildCache[playerId] = new CachedWardGuildIdentity(
                hasGuild: false,
                guildId: 0,
                guildName: string.Empty,
                DateTime.UtcNow + TransientNoGuildLookupCacheDuration,
                authoritativeNoGuild: false);
            return;
        }

        CacheGuildLookup(
            playerId,
            hasGuild,
            guild,
            authoritativeNoGuild: !hasGuild);
    }

    private static bool TryParseGuild(object? guildObject, out WardGuildIdentity guild)
    {
        guild = default;
        if (guildObject == null || GuildNameField == null || GuildGeneralField == null || GuildGeneralIdField == null)
        {
            return false;
        }

        try
        {
            var general = GuildGeneralField.GetValue(guildObject);
            var id = general != null ? Convert.ToInt32(GuildGeneralIdField.GetValue(general)) : 0;
            if (id == 0)
            {
                return false;
            }

            var name = GuildNameField.GetValue(guildObject) as string ?? string.Empty;
            guild = new WardGuildIdentity(id, name);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
