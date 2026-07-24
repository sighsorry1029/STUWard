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

        if (IsCachedNoGuild(playerId))
        {
            return false;
        }

        if (IsAvailable() && GetPlayerGuildByPlayerMethod != null)
        {
            try
            {
                var guildObject = GetPlayerGuildByPlayerMethod.Invoke(null, new object[] { player });
                var hasGuild = TryParseGuild(guildObject, out guild);
                if (hasGuild)
                {
                    CacheGuildLookup(playerId, hasGuild: true, guild);
                    return true;
                }
            }
            catch
            {
            }
        }

        var accountId = WardOwnership.GetPlayerAccountId(player);
        var playerName = player.GetPlayerName();
        if (!string.IsNullOrWhiteSpace(accountId) &&
            !string.IsNullOrWhiteSpace(playerName) &&
            TryGetGuildByAccountAndName(accountId, playerName, out guild))
        {
            CacheGuildLookup(playerId, hasGuild: true, guild);
            return true;
        }

        CacheGuildLookup(playerId, hasGuild: false, default);
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
            CacheGuildLookup(playerId, guild.Id != 0, guild);
            return guild.Id != 0;
        }

        if (TryGetCachedGuild(playerId, out guild))
        {
            return true;
        }

        if (IsCachedNoGuild(playerId))
        {
            return false;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
        {
            return TryGetGuild(localPlayer, out guild);
        }

        if (!IsAvailable() || GetPlayerGuildByReferenceMethod == null || PlayerReferenceFromStringMethod == null)
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            CacheGuildLookup(playerId, hasGuild: false, default);
            return false;
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            CacheGuildLookup(playerId, hasGuild: false, default);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(playerName) &&
            TryGetGuildByAccountAndName(accountId, playerName, out guild))
        {
            CacheGuildLookup(playerId, hasGuild: true, guild);
            return true;
        }

        CacheGuildLookup(playerId, hasGuild: false, default);
        return false;
    }

    private static bool TryGetGuildByAccountAndName(string accountId, string playerName, out WardGuildIdentity guild)
    {
        guild = default;
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        var normalizedPlayerName = playerName?.Trim() ?? string.Empty;
        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            TryGetSyncedGuildIdentity(normalizedAccountId, normalizedPlayerName, out guild))
        {
            return guild.Id != 0;
        }

        return TryResolveGuildByAccountAndNameFromApi(
                   normalizedAccountId,
                   normalizedPlayerName,
                   out guild) &&
               guild.Id != 0;
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

    private static bool TryResolveGuildByPlayerFromApi(Player player, out WardGuildIdentity guild)
    {
        guild = default;
        if (GetPlayerGuildByPlayerMethod == null)
        {
            return false;
        }

        try
        {
            var guildObject = GetPlayerGuildByPlayerMethod.Invoke(null, new object[] { player });
            _ = TryParseGuild(guildObject, out guild);
            return true;
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

        var accountCandidates = GuildIdentityPolicy.GetAccountLookupCandidates(normalizedAccountId);
        var primaryLookupResolved = TryResolveGuildByReferenceId(
            accountCandidates.PrimaryAccountId,
            normalizedPlayerName,
            out var primaryGuild);
        if (primaryGuild.Id != 0 || !accountCandidates.HasFallback)
        {
            guild = primaryGuild;
            return primaryLookupResolved;
        }

        var fallbackLookupResolved = TryResolveGuildByReferenceId(
            accountCandidates.FallbackAccountId,
            normalizedPlayerName,
            out var fallbackGuild);
        if (fallbackLookupResolved)
        {
            guild = fallbackGuild;
            return true;
        }

        guild = primaryGuild;
        return primaryLookupResolved;
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
            TryParseGuild(guildObject, out guild);
            return true;
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

    private static bool IsCachedNoGuild(long playerId)
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

        return !cached.HasGuild;
    }

    private static void CacheGuildLookup(long playerId, bool hasGuild, WardGuildIdentity guild)
    {
        if (playerId == 0L)
        {
            return;
        }

        PlayerGuildCache[playerId] = new CachedWardGuildIdentity(
            hasGuild && guild.Id != 0,
            guild.Id,
            guild.Name ?? string.Empty,
            DateTime.UtcNow + GuildLookupCacheDuration);
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
