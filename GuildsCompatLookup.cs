using System;
using System.Collections;
using System.Collections.Generic;

namespace STUWard;

internal static partial class GuildsCompat
{
    private static readonly Dictionary<long, CachedWardGuildIdentity> PlayerGuildCache = new();
    private static readonly Dictionary<long, CachedPlayerPlatformIdentity> PlayerPlatformIdCache = new();

    internal static string GetPlayerPlatformId(long playerId)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        if (TryGetCachedPlatformId(playerId, out var cachedPlatformId))
        {
            return cachedPlatformId;
        }

        var cachedAccountId = WardOwnership.GetPlayerAccountId(playerId);
        if (!string.IsNullOrWhiteSpace(cachedAccountId))
        {
            CachePlatformId(playerId, cachedAccountId);
            return cachedAccountId;
        }

        var playerInfo = FindPlayerInfo(playerId);
        if (playerInfo == null || PlayerInfoUserInfoField == null || UserInfoIdField == null)
        {
            CachePlatformId(playerId, string.Empty);
            return string.Empty;
        }

        try
        {
            var boxedPlayerInfo = (object)playerInfo.Value;
            var userInfo = PlayerInfoUserInfoField.GetValue(boxedPlayerInfo);
            if (userInfo == null)
            {
                CachePlatformId(playerId, string.Empty);
                return string.Empty;
            }

            var platformId = UserInfoIdField.GetValue(userInfo);
            var platformIdString = platformId?.ToString() ?? string.Empty;
            CachePlatformId(playerId, platformIdString);
            return platformIdString;
        }
        catch
        {
            CachePlatformId(playerId, string.Empty);
            return string.Empty;
        }
    }

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

        Plugin.LogWardDiagnosticVerbose(
            "GuildsCompat.Lookup",
            $"Failed local guild lookup. playerId={playerId}, playerName='{playerName}', accountId='{accountId}', apiAvailable={IsAvailable()}, hasPlayerLookup={(GetPlayerGuildByPlayerMethod != null)}, hasReferenceLookup={(GetPlayerGuildByReferenceMethod != null && PlayerReferenceFromStringMethod != null)}");
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
            Plugin.LogWardDiagnosticVerbose(
                "GuildsCompat.Lookup",
                $"Failed remote guild lookup because Guilds API reference lookup is unavailable. playerId={playerId}, playerName='{WardOwnership.GetPlayerName(playerId)}', accountId='{WardOwnership.GetPlayerAccountId(playerId)}', apiAvailable={IsAvailable()}, hasReferenceLookup={(GetPlayerGuildByReferenceMethod != null && PlayerReferenceFromStringMethod != null)}");
            return false;
        }

        var platformId = GetPlayerPlatformId(playerId);
        if (string.IsNullOrWhiteSpace(platformId))
        {
            var fallbackAccountId = WardOwnership.GetPlayerAccountId(playerId);
            Plugin.LogWardDiagnosticVerbose(
                "GuildsCompat.Lookup",
                $"Failed remote guild lookup because live player platform id is unavailable. playerId={playerId}, playerName='{WardOwnership.GetPlayerName(playerId)}', fallbackAccountId='{fallbackAccountId}'");
            CacheGuildLookup(playerId, hasGuild: false, default);
            return false;
        }

        if (string.IsNullOrWhiteSpace(playerName))
        {
            Plugin.LogWardDiagnosticVerbose(
                "GuildsCompat.Lookup",
                $"Failed remote guild lookup because player name is unavailable. playerId={playerId}, accountId='{platformId}'");
            CacheGuildLookup(playerId, hasGuild: false, default);
            return false;
        }

        if (!string.IsNullOrWhiteSpace(playerName) &&
            TryGetGuildByAccountAndName(platformId, playerName, out guild))
        {
            CacheGuildLookup(playerId, hasGuild: true, guild);
            return true;
        }

        Plugin.LogWardDiagnosticVerbose(
            "GuildsCompat.Lookup",
            $"Failed remote guild lookup after account/name lookup. playerId={playerId}, playerName='{playerName}', accountId='{platformId}'");
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

        if (string.IsNullOrWhiteSpace(normalizedAccountId) ||
            string.IsNullOrWhiteSpace(normalizedPlayerName) ||
            GetPlayerGuildByReferenceMethod == null ||
            PlayerReferenceFromStringMethod == null ||
            !IsAvailable())
        {
            return false;
        }

        try
        {
            var playerReference = PlayerReferenceFromStringMethod.Invoke(null, new object[] { $"{normalizedAccountId}:{normalizedPlayerName}" });
            var guildObject = GetPlayerGuildByReferenceMethod.Invoke(null, new[] { playerReference! });
            return TryParseGuild(guildObject, out guild);
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

    private static bool TryGetCachedPlatformId(long playerId, out string platformId)
    {
        platformId = string.Empty;
        if (!PlayerPlatformIdCache.TryGetValue(playerId, out var cached))
        {
            return false;
        }

        if (cached.ExpiresAtUtc <= DateTime.UtcNow)
        {
            PlayerPlatformIdCache.Remove(playerId);
            return false;
        }

        if (!cached.HasPlatformId)
        {
            return true;
        }

        platformId = cached.PlatformId;
        return true;
    }

    private static void CachePlatformId(long playerId, string platformId)
    {
        if (playerId == 0L)
        {
            return;
        }

        var hasPlatformId = !string.IsNullOrWhiteSpace(platformId);
        PlayerPlatformIdCache[playerId] = new CachedPlayerPlatformIdentity(
            hasPlatformId,
            hasPlatformId ? platformId : string.Empty,
            DateTime.UtcNow + GuildLookupCacheDuration);
    }

    private static ZNet.PlayerInfo? FindPlayerInfo(long playerId)
    {
        var players = ZNet.instance?.m_players;
        if (players == null)
        {
            return null;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var playerInfo = players[index];
            var playerZdo = ZDOMan.instance?.GetZDO(playerInfo.m_characterID);
            if ((playerZdo?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L) == playerId)
            {
                return playerInfo;
            }
        }

        return null;
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
