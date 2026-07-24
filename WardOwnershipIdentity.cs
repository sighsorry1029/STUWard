using System;
using System.Collections.Generic;
using Splatform;

namespace STUWard;

internal readonly struct ServerSessionIdentity
{
    internal ServerSessionIdentity(long senderUid, ZDOID characterZdoId, long playerId, string accountId, string playerName)
    {
        SenderUid = senderUid;
        CharacterZdoId = characterZdoId;
        PlayerId = playerId;
        AccountId = accountId ?? string.Empty;
        PlayerName = playerName ?? string.Empty;
    }

    internal long SenderUid { get; }
    internal ZDOID CharacterZdoId { get; }
    internal long PlayerId { get; }
    internal string AccountId { get; }
    internal string PlayerName { get; }
}

internal static partial class WardOwnership
{
    // Server-side identity/auth state:
    // sender -> session identity resolution and playerId -> accountId cache.
    private sealed class IdentityAuthState
    {
        internal readonly Dictionary<long, string> ServerPlayerAccountIdsByPlayerId = new();
        internal readonly Dictionary<long, ServerSessionIdentity> ServerSessionIdentitiesBySender = new();
    }

    private static readonly IdentityAuthState IdentityAuthData = new();

    private static Dictionary<long, string> ServerPlayerAccountIdsByPlayerId => IdentityAuthData.ServerPlayerAccountIdsByPlayerId;
    private static Dictionary<long, ServerSessionIdentity> ServerSessionIdentitiesBySender => IdentityAuthData.ServerSessionIdentitiesBySender;

    private static void ResetIdentityAuthState()
    {
        ServerPlayerAccountIdsByPlayerId.Clear();
        ServerSessionIdentitiesBySender.Clear();
    }

    internal static string GetWardSteamAccountId(PrivateArea? area)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        return GetWardSteamAccountId(zdo);
    }

    internal static string GetWardSteamAccountId(ZDO? zdo)
    {
        return NormalizeAccountId(zdo?.GetString(SteamAccountIdKey, string.Empty) ?? string.Empty);
    }

    internal static string ResolveWardSteamAccountId(ZDO? zdo, long playerId = 0L, string fallbackSteamAccountId = "")
    {
        var storedSteamAccountId = NormalizeAccountId(zdo?.GetString(SteamAccountIdKey, string.Empty) ?? string.Empty);
        if (!string.IsNullOrWhiteSpace(storedSteamAccountId))
        {
            return storedSteamAccountId;
        }

        var normalizedFallbackSteamAccountId = NormalizeAccountId(fallbackSteamAccountId);
        if (!string.IsNullOrWhiteSpace(normalizedFallbackSteamAccountId))
        {
            return normalizedFallbackSteamAccountId;
        }

        return playerId == 0L ? string.Empty : GetPlayerAccountId(playerId);
    }

    internal static string GetPlayerAccountId(Player? player)
    {
        return player == null ? string.Empty : GetPlayerAccountId(player.GetPlayerID());
    }

    internal static string GetPlayerAccountId(long playerId)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
        {
            var localAccountId = GetLocalPlayerAccountId();
            if (!string.IsNullOrWhiteSpace(localAccountId))
            {
                RememberServerPlayerAccountId(playerId, localAccountId);
                return localAccountId;
            }
        }

        var cachedAccountId = GetCachedServerPlayerAccountId(playerId);
        if (!string.IsNullOrWhiteSpace(cachedAccountId))
        {
            return cachedAccountId;
        }

        var sessionAccountId = GetServerSessionAccountId(playerId);
        if (!string.IsNullOrWhiteSpace(sessionAccountId))
        {
            return sessionAccountId;
        }

        var playerInfo = FindPlayerInfo(playerId);
        if (playerInfo == null)
        {
            return string.Empty;
        }

        try
        {
            var resolvedAccountId = NormalizeAccountId(playerInfo.Value.m_userInfo.m_id.ToString());
            RememberServerPlayerAccountId(playerId, resolvedAccountId);
            return resolvedAccountId;
        }
        catch
        {
            return string.Empty;
        }
    }

    internal static string GetPlayerSteamIdDisplay(long playerId)
    {
        return GetPlayerAccountId(playerId);
    }

    internal static string GetPlayerName(long playerId)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null && localPlayer.GetPlayerID() == playerId)
        {
            return localPlayer.GetPlayerName();
        }

        var playerInfo = FindPlayerInfo(playerId);
        if (playerInfo != null)
        {
            return playerInfo.Value.m_name ?? string.Empty;
        }

        return GetServerSessionPlayerName(playerId);
    }

    internal static string NormalizeAccountIdValue(string? rawAccountId)
    {
        return NormalizeAccountId(rawAccountId);
    }

    internal static string NormalizeOverrideAccountIdValue(string? rawAccountId)
    {
        return NormalizeAccountId(rawAccountId);
    }

    internal static long ResolvePlayerIdFromSender(long sender)
    {
        if (sender == 0L)
        {
            return 0L;
        }

        if (TryResolvePlayerIdFromSessionId(sender, out var sessionPlayerId))
        {
            return sessionPlayerId;
        }

        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            if (TryGetServerSessionIdentity(sender, out var sessionIdentity))
            {
                if (sessionIdentity.PlayerId != 0L)
                {
                    return sessionIdentity.PlayerId;
                }

                RefreshServerSessionIdentity(ZNet.instance.GetPeer(sender));
                if (TryGetServerSessionIdentity(sender, out sessionIdentity) && sessionIdentity.PlayerId != 0L)
                {
                    return sessionIdentity.PlayerId;
                }
            }
            else
            {
                RefreshServerSessionIdentity(ZNet.instance.GetPeer(sender));
                if (TryGetServerSessionIdentity(sender, out sessionIdentity) && sessionIdentity.PlayerId != 0L)
                {
                    return sessionIdentity.PlayerId;
                }
            }
        }

        var resolvedPlayerId = GetPlayerId(ZNet.instance?.GetPeer(sender));
        return resolvedPlayerId != 0L ? resolvedPlayerId : GetLocalHostPlayerId(sender);
    }

    internal static bool TryResolveAuthoritativePlayerIdFromSender(long sender, out long playerId)
    {
        playerId = ResolvePlayerIdFromSender(sender);
        if (playerId != 0L)
        {
            return true;
        }

        return false;
    }

    internal static bool IsAuthoritativeServerSender(long sender)
    {
        var routedRpc = ZRoutedRpc.instance;
        return sender != 0L && routedRpc != null && sender == routedRpc.GetServerPeerID();
    }

    internal static string GetAuthoritativeAccountIdFromSender(long sender, long playerId = 0L)
    {
        if (sender != 0L)
        {
            if (TryGetServerSessionIdentity(sender, out var sessionIdentity) &&
                !string.IsNullOrWhiteSpace(sessionIdentity.AccountId))
            {
                return sessionIdentity.AccountId;
            }

            if (ZNet.instance != null && ZNet.instance.IsServer())
            {
                RefreshServerSessionIdentity(ZNet.instance.GetPeer(sender));
                if (TryGetServerSessionIdentity(sender, out sessionIdentity) &&
                    !string.IsNullOrWhiteSpace(sessionIdentity.AccountId))
                {
                    return sessionIdentity.AccountId;
                }
            }
        }

        return playerId == 0L ? string.Empty : GetPlayerAccountId(playerId);
    }

    internal static bool TryResolveClaimedPlayerIdFromSender(long sender, long claimedPlayerId, out long playerId)
    {
        playerId = 0L;
        if (claimedPlayerId == 0L)
        {
            return false;
        }

        var claimedPlayer = Player.GetPlayer(claimedPlayerId);
        if (claimedPlayer != null && claimedPlayer.GetOwner() == sender)
        {
            playerId = claimedPlayerId;
            return true;
        }

        var resolvedPlayerId = ResolvePlayerIdFromSender(sender);
        if (resolvedPlayerId == claimedPlayerId && resolvedPlayerId != 0L)
        {
            playerId = resolvedPlayerId;
            return true;
        }

        return false;
    }

    internal static long GetPlayerIdFromSender(long sender)
    {
        return ResolvePlayerIdFromSender(sender);
    }

    internal static void RefreshServerSessionIdentity(ZNetPeer? peer, ZDOID characterIdOverride = default)
    {
        if (peer == null || peer.m_uid == 0L || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var characterId = !characterIdOverride.IsNone() ? characterIdOverride : peer.m_characterID;
        var playerId = GetPlayerId(characterId);
        var accountId = ResolveServerSessionAccountId(playerId, characterId);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = ResolveServerPeerAccountId(peer);
        }
        var playerName = ResolveServerSessionPlayerName(peer, characterId, playerId);
        ServerSessionIdentitiesBySender[peer.m_uid] = new ServerSessionIdentity(
            peer.m_uid,
            characterId,
            playerId,
            accountId,
            playerName);
    }

    internal static void ForgetServerSessionIdentity(ZNetPeer? peer)
    {
        if (peer == null || peer.m_uid == 0L)
        {
            return;
        }

        if (ServerSessionIdentitiesBySender.TryGetValue(peer.m_uid, out var sessionIdentity))
        {
            GuildsCompat.ForgetServerPlayerGuildIdentity(
                sessionIdentity.PlayerId,
                sessionIdentity.AccountId,
                sessionIdentity.PlayerName);
            if (sessionIdentity.PlayerId != 0L)
            {
                WardAdminDebugAccess.ForgetServerPlayer(sessionIdentity.PlayerId);
            }
        }

        ServerSessionIdentitiesBySender.Remove(peer.m_uid);
    }

    private static bool TryResolvePlayerIdFromSessionId(long sender, out long playerId)
    {
        playerId = GetLocalHostPlayerId(sender);
        if (playerId != 0L)
        {
            return true;
        }

        var playerInfo = FindPlayerInfoBySessionId(sender);
        if (playerInfo != null)
        {
            playerId = GetPlayerId(playerInfo.Value.m_characterID);
            if (playerId != 0L)
            {
                return true;
            }
        }

        var peer = ZRoutedRpc.instance?.GetPeer(sender) ?? ZNet.instance?.GetPeer(sender);
        if (peer != null)
        {
            playerId = GetPlayerId(peer.m_characterID);
            if (playerId != 0L)
            {
                return true;
            }
        }

        return false;
    }

    private static long GetLocalHostPlayerId(long sender)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return 0L;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return 0L;
        }

        var localPlayerId = localPlayer.GetPlayerID();
        if (localPlayerId == 0L)
        {
            return 0L;
        }

        return localPlayer.GetOwner() == sender ? localPlayerId : 0L;
    }

    private static long GetPlayerId(ZNetPeer? peer)
    {
        return peer == null ? 0L : GetPlayerId(peer.m_characterID);
    }

    private static long GetPlayerId(ZDOID characterId)
    {
        if (characterId.IsNone())
        {
            return 0L;
        }

        var playerZdo = ZDOMan.instance?.GetZDO(characterId);
        return playerZdo?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L;
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

    private static ZNet.PlayerInfo? FindPlayerInfoByCharacterId(ZDOID characterId)
    {
        if (characterId.IsNone())
        {
            return null;
        }

        var players = ZNet.instance?.m_players;
        if (players == null)
        {
            return null;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var playerInfo = players[index];
            if (playerInfo.m_characterID.Equals(characterId))
            {
                return playerInfo;
            }
        }

        return null;
    }

    private static ZNet.PlayerInfo? FindPlayerInfoBySessionId(long sessionId)
    {
        if (sessionId == 0L)
        {
            return null;
        }

        var players = ZNet.instance?.m_players;
        if (players == null)
        {
            return null;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var playerInfo = players[index];
            if (playerInfo.m_characterID.UserID == sessionId)
            {
                return playerInfo;
            }
        }

        return null;
    }

    private static bool TryGetServerSessionIdentity(long sender, out ServerSessionIdentity sessionIdentity)
    {
        return ServerSessionIdentitiesBySender.TryGetValue(sender, out sessionIdentity);
    }

    private static string ResolveServerSessionAccountId(long playerId, ZDOID characterId)
    {
        if (playerId != 0L)
        {
            var cachedAccountId = GetCachedServerPlayerAccountId(playerId);
            if (!string.IsNullOrWhiteSpace(cachedAccountId))
            {
                return cachedAccountId;
            }
        }

        var playerInfo = FindPlayerInfoByCharacterId(characterId);
        if (playerInfo == null && playerId != 0L)
        {
            playerInfo = FindPlayerInfo(playerId);
        }

        if (playerInfo == null)
        {
            return string.Empty;
        }

        try
        {
            var accountId = NormalizeAccountId(playerInfo.Value.m_userInfo.m_id.ToString());
            if (playerId != 0L && !string.IsNullOrWhiteSpace(accountId))
            {
                RememberServerPlayerAccountId(playerId, accountId);
            }

            return accountId;
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveServerPeerAccountId(ZNetPeer peer)
    {
        try
        {
            var platformUserId = (int)ZNet.m_onlineBackend == 0
                ? new PlatformUserID(ZNet.instance.m_steamPlatform, peer.m_socket.GetHostName())
                : new PlatformUserID(peer.m_socket.GetHostName());
            return NormalizeAccountId(platformUserId.m_userID.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string ResolveServerSessionPlayerName(ZNetPeer peer, ZDOID characterId, long playerId)
    {
        if (!string.IsNullOrWhiteSpace(peer.m_playerName))
        {
            return peer.m_playerName;
        }

        var playerInfo = FindPlayerInfoByCharacterId(characterId);
        if (playerInfo != null)
        {
            return playerInfo.Value.m_name ?? string.Empty;
        }

        return playerId == 0L ? string.Empty : GetPlayerName(playerId);
    }

    private static string GetServerSessionAccountId(long playerId)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        foreach (var sessionIdentity in ServerSessionIdentitiesBySender.Values)
        {
            if (sessionIdentity.PlayerId == playerId && !string.IsNullOrWhiteSpace(sessionIdentity.AccountId))
            {
                return sessionIdentity.AccountId;
            }
        }

        return string.Empty;
    }

    private static string GetServerSessionPlayerName(long playerId)
    {
        if (playerId == 0L)
        {
            return string.Empty;
        }

        foreach (var sessionIdentity in ServerSessionIdentitiesBySender.Values)
        {
            if (sessionIdentity.PlayerId == playerId && !string.IsNullOrWhiteSpace(sessionIdentity.PlayerName))
            {
                return sessionIdentity.PlayerName;
            }
        }

        return string.Empty;
    }

    private static bool TryGetServerSessionSenderUid(long playerId, out long senderUid)
    {
        senderUid = 0L;
        if (playerId == 0L)
        {
            return false;
        }

        foreach (var sessionIdentity in ServerSessionIdentitiesBySender.Values)
        {
            if (sessionIdentity.PlayerId != playerId || sessionIdentity.SenderUid == 0L)
            {
                continue;
            }

            senderUid = sessionIdentity.SenderUid;
            return true;
        }

        return false;
    }

    private static void RefreshServerSessionAccountIds(long playerId, string accountId)
    {
        if (playerId == 0L || string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        List<long>? matchingSenders = null;
        foreach (var sessionIdentity in ServerSessionIdentitiesBySender)
        {
            if (sessionIdentity.Value.PlayerId != playerId)
            {
                continue;
            }

            matchingSenders ??= new List<long>();
            matchingSenders.Add(sessionIdentity.Key);
        }

        if (matchingSenders == null)
        {
            return;
        }

        var canonicalAccountId = NormalizeAccountId(accountId);
        for (var index = 0; index < matchingSenders.Count; index++)
        {
            var sender = matchingSenders[index];
            var existingIdentity = ServerSessionIdentitiesBySender[sender];
            ServerSessionIdentitiesBySender[sender] = new ServerSessionIdentity(
                existingIdentity.SenderUid,
                existingIdentity.CharacterZdoId,
                existingIdentity.PlayerId,
                canonicalAccountId,
                existingIdentity.PlayerName);
        }
    }

    private static string GetCachedServerPlayerAccountId(long playerId)
    {
        if (playerId == 0L || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return string.Empty;
        }

        return ServerPlayerAccountIdsByPlayerId.TryGetValue(playerId, out var accountId)
            ? accountId
            : string.Empty;
    }

    private static void RememberServerPlayerAccountId(long playerId, string accountId)
    {
        if (playerId == 0L || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var canonicalAccountId = NormalizeAccountId(accountId);
        if (string.IsNullOrWhiteSpace(canonicalAccountId))
        {
            return;
        }

        if (ServerPlayerAccountIdsByPlayerId.TryGetValue(playerId, out var existingAccountId) &&
            SameAccountId(existingAccountId, canonicalAccountId))
        {
            return;
        }

        ServerPlayerAccountIdsByPlayerId[playerId] = canonicalAccountId;
        RefreshServerSessionAccountIds(playerId, canonicalAccountId);
    }

    private static string GetLocalPlayerAccountId()
    {
        try
        {
            return NormalizeAccountId(UserInfo.GetLocalUser().UserId.ToString());
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string NormalizeAccountId(string? rawAccountId)
    {
        return GuildIdentityPolicy.NormalizeAccountId(rawAccountId);
    }

    private static bool SameAccountId(string left, string right)
    {
        return string.Equals(
            NormalizeAccountId(left),
            NormalizeAccountId(right),
            StringComparison.Ordinal);
    }

}
