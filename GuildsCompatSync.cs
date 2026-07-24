using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct SyncedWardGuildIdentity
{
    internal SyncedWardGuildIdentity(
        bool hasGuild,
        int guildId,
        string guildName,
        string accountId,
        string playerName)
    {
        HasGuild = hasGuild;
        GuildId = guildId;
        GuildName = guildName ?? string.Empty;
        AccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        PlayerName = playerName?.Trim() ?? string.Empty;
    }

    internal bool HasGuild { get; }
    internal int GuildId { get; }
    internal string GuildName { get; }
    internal string AccountId { get; }
    internal string PlayerName { get; }
}

internal static partial class GuildsCompat
{
    private const string SyncPlayerGuildRpc = "STUWard_SyncPlayerGuild";
    private static readonly TimeSpan LocalGuildSyncHeartbeat = TimeSpan.FromSeconds(10);
    private static readonly Dictionary<long, SyncedWardGuildIdentity> ServerSyncedGuildByPlayerId = new();
    private static readonly Dictionary<string, SyncedWardGuildIdentity> ServerSyncedGuildByCharacterKey = new(StringComparer.Ordinal);

    private static bool _syncRpcsRegistered;
    private static bool _localGuildSyncPending = true;
    private static long _lastSyncedLocalPlayerId;
    private static int _lastSyncedLocalGuildId = int.MinValue;
    private static string _lastSyncedLocalGuildName = string.Empty;
    private static DateTime _nextLocalGuildSyncUtc = DateTime.MinValue;

    internal static void Update()
    {
        SyncLocalPlayerGuildIfNeeded(force: false);
        ProcessPendingWardGuildProjectionRefreshes();
    }

    internal static void OnLocalPlayerStarted(Player? player)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        _localGuildSyncPending = true;
        SyncLocalPlayerGuildIfNeeded(force: true);
    }

    internal static void ResetSyncedGuildState()
    {
        ServerSyncedGuildByPlayerId.Clear();
        ServerSyncedGuildByCharacterKey.Clear();
        _syncRpcsRegistered = false;
        _localGuildSyncPending = true;
        _lastSyncedLocalPlayerId = 0L;
        _lastSyncedLocalGuildId = int.MinValue;
        _lastSyncedLocalGuildName = string.Empty;
        _nextLocalGuildSyncUtc = DateTime.MinValue;
    }

    internal static bool TryGetSyncedGuildIdentity(long playerId, string accountId, string playerName, out WardGuildIdentity guild)
    {
        guild = default;
        if (playerId != 0L &&
            ServerSyncedGuildByPlayerId.TryGetValue(playerId, out var syncedByPlayer))
        {
            guild = syncedByPlayer.HasGuild
                ? new WardGuildIdentity(syncedByPlayer.GuildId, syncedByPlayer.GuildName)
                : default;
            return true;
        }

        var characterKey = BuildCharacterIdentityKey(accountId, playerName);
        if (string.IsNullOrWhiteSpace(characterKey) ||
            !ServerSyncedGuildByCharacterKey.TryGetValue(characterKey, out var syncedByCharacter))
        {
            return false;
        }

        guild = syncedByCharacter.HasGuild
            ? new WardGuildIdentity(syncedByCharacter.GuildId, syncedByCharacter.GuildName)
            : default;
        return true;
    }

    internal static bool TryGetSyncedGuildIdentity(string accountId, string playerName, out WardGuildIdentity guild)
    {
        return TryGetSyncedGuildIdentity(0L, accountId, playerName, out guild);
    }

    private static void RegisterSyncRpcs()
    {
        var routedRpc = ZRoutedRpc.instance;
        if (_syncRpcsRegistered || routedRpc == null)
        {
            return;
        }

        routedRpc.Register(SyncPlayerGuildRpc, new Action<long, ZPackage>(HandleSyncPlayerGuild));
        _syncRpcsRegistered = true;
    }

    private static void SyncLocalPlayerGuildIfNeeded(bool force)
    {
        var localPlayer = Player.m_localPlayer;
        var znet = ZNet.instance;
        if (localPlayer == null || znet == null)
        {
            return;
        }

        if (!IsAvailable())
        {
            return;
        }

        var playerId = localPlayer.GetPlayerID();
        var accountId = WardOwnership.GetPlayerAccountId(localPlayer);
        if (playerId == 0L || string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        var guild = GetPlayerGuildIdentity(localPlayer);
        var guildName = guild.Name ?? string.Empty;
        var now = DateTime.UtcNow;
        var heartbeatDue = !znet.IsServer() && now >= _nextLocalGuildSyncUtc;
        var changed = _localGuildSyncPending ||
                      playerId != _lastSyncedLocalPlayerId ||
                      guild.Id != _lastSyncedLocalGuildId ||
                      !string.Equals(guildName, _lastSyncedLocalGuildName, StringComparison.Ordinal) ||
                      heartbeatDue;
        if (!force && !changed)
        {
            return;
        }

        if (znet.IsServer())
        {
            if (UpsertSyncedGuildIdentity(playerId, accountId, localPlayer.GetPlayerName(), guild, out var previousGuild))
            {
                RefreshWardGuildProjectionForCharacter(
                    new WardGuildCharacterIdentity(playerId, accountId, localPlayer.GetPlayerName()),
                    liveDisplayRefresh: true,
                    affectedGuildId: guild.Id,
                    previousGuildId: previousGuild.Id);
            }

            RememberLocalGuildSync(playerId, guild, now);
            return;
        }

        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null)
        {
            return;
        }

        var serverPeerId = routedRpc.GetServerPeerID();
        if (serverPeerId == 0L)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(guild.Id);
        pkg.Write(guildName);
        routedRpc.InvokeRoutedRPC(serverPeerId, SyncPlayerGuildRpc, pkg);
        RememberLocalGuildSync(playerId, guild, now);
    }

    private static void HandleSyncPlayerGuild(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || pkg == null || !IsAvailable())
        {
            return;
        }

        int reportedGuildId;
        string reportedGuildName;
        try
        {
            reportedGuildId = pkg.ReadInt();
            reportedGuildName = pkg.ReadString();
        }
        catch
        {
            return;
        }

        if (reportedGuildName.Length > 256)
        {
            return;
        }

        _ = TryApplySyncedGuildIdentity(sender, reportedGuildId);
    }

    private static bool TryApplySyncedGuildIdentity(long sender, int reportedGuildId)
    {
        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, out var playerId))
        {
            return false;
        }

        var accountId = WardOwnership.GetAuthoritativeAccountIdFromSender(sender, playerId);
        if (string.IsNullOrWhiteSpace(accountId))
        {
            accountId = WardOwnership.GetPlayerAccountId(playerId);
        }

        if (string.IsNullOrWhiteSpace(accountId))
        {
            return false;
        }

        var playerName = WardOwnership.GetPlayerName(playerId);
        if (!TryResolveAuthoritativeGuildIdentity(playerId, accountId, playerName, out var guild))
        {
            return false;
        }

        if (!GuildIdentityPolicy.CanApplyAuthoritativeGuild(reportedGuildId, guild.Id))
        {
            InvalidateSyncedGuildIdentity(new WardGuildCharacterIdentity(playerId, accountId, playerName));
            return false;
        }

        if (UpsertSyncedGuildIdentity(playerId, accountId, playerName, guild, out var previousGuild))
        {
            WardOwnership.RefreshServerPlayerAccountIdForResolvedPlayer(playerId, accountId);
            RefreshWardGuildProjectionForCharacter(
                new WardGuildCharacterIdentity(playerId, accountId, playerName),
                liveDisplayRefresh: true,
                affectedGuildId: guild.Id,
                previousGuildId: previousGuild.Id);
        }

        return true;
    }

    private static void RememberLocalGuildSync(long playerId, WardGuildIdentity guild, DateTime sentAtUtc)
    {
        _lastSyncedLocalPlayerId = playerId;
        _lastSyncedLocalGuildId = guild.Id;
        _lastSyncedLocalGuildName = guild.Name ?? string.Empty;
        _localGuildSyncPending = false;
        _nextLocalGuildSyncUtc = sentAtUtc + LocalGuildSyncHeartbeat;
    }

    internal static void ForgetServerPlayerGuildIdentity(
        long playerId,
        string accountId,
        string playerName)
    {
        InvalidateSyncedGuildIdentity(new WardGuildCharacterIdentity(playerId, accountId, playerName));
    }

    internal static void InvalidateSyncedGuildIdentity(WardGuildCharacterIdentity identity)
    {
        var playerIdsToRemove = new HashSet<long>();
        if (identity.HasPlayerId)
        {
            playerIdsToRemove.Add(identity.PlayerId);
        }

        if (identity.HasAccountAndName)
        {
            foreach (var entry in ServerSyncedGuildByPlayerId)
            {
                if (string.Equals(entry.Value.AccountId, identity.AccountId, StringComparison.Ordinal) &&
                    string.Equals(entry.Value.PlayerName, identity.PlayerName, StringComparison.Ordinal))
                {
                    playerIdsToRemove.Add(entry.Key);
                }
            }
        }

        foreach (var playerId in playerIdsToRemove)
        {
            if (ServerSyncedGuildByPlayerId.Remove(playerId, out var removed))
            {
                RemoveSyncedGuildCharacterKey(removed.AccountId, removed.PlayerName);
            }

            PlayerGuildCache.Remove(playerId);
        }

        RemoveSyncedGuildCharacterKey(identity.AccountId, identity.PlayerName);
    }

    internal static void InvalidateSyncedGuildIdentitiesForGuild(int guildId)
    {
        if (guildId == 0)
        {
            return;
        }

        var affectedIdentities = new List<WardGuildCharacterIdentity>();
        foreach (var entry in ServerSyncedGuildByPlayerId)
        {
            if (entry.Value.GuildId == guildId)
            {
                affectedIdentities.Add(new WardGuildCharacterIdentity(
                    entry.Key,
                    entry.Value.AccountId,
                    entry.Value.PlayerName));
            }
        }

        foreach (var identity in affectedIdentities)
        {
            InvalidateSyncedGuildIdentity(identity);
        }
    }

    internal static void InvalidateAllSyncedGuildIdentities()
    {
        ServerSyncedGuildByPlayerId.Clear();
        ServerSyncedGuildByCharacterKey.Clear();
        PlayerGuildCache.Clear();
    }

    private static void RemoveSyncedGuildCharacterKey(string accountId, string playerName)
    {
        var characterKey = BuildCharacterIdentityKey(accountId, playerName);
        if (!string.IsNullOrWhiteSpace(characterKey))
        {
            ServerSyncedGuildByCharacterKey.Remove(characterKey);
        }
    }

    private static void NotifyGuildProjectionRefreshApplied(
        bool fullRefresh,
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds)
    {
        HashSet<long>? recipientPeerUids = null;
        if (!fullRefresh)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            var peers = ZNet.instance.GetPeers();
            recipientPeerUids = peers == null
                ? new HashSet<long>()
                : CollectGuildProjectionRefreshRecipients(
                    peers,
                    targetPlayerIds,
                    targetCharacterKeys,
                    affectedGuildIds);
        }

        ManagedWardMapStateService.NotifyViewerProjectionChanged(
            fullRefresh,
            recipientPeerUids,
            refreshImmediatelyIfVisible: true);
    }

    private static HashSet<long> CollectGuildProjectionRefreshRecipients(
        List<ZNetPeer> peers,
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds)
    {
        var recipientPeerUids = new HashSet<long>();
        for (var index = 0; index < peers.Count; index++)
        {
            var peer = peers[index];
            if (peer == null || peer.m_uid == 0L)
            {
                continue;
            }

            var playerId = WardOwnership.GetPlayerIdFromSender(peer.m_uid);
            var accountId = WardOwnership.GetAuthoritativeAccountIdFromSender(peer.m_uid, playerId);
            var playerName = playerId != 0L ? WardOwnership.GetPlayerName(playerId) : string.Empty;
            if (ShouldReceiveGuildProjectionRefresh(playerId, accountId, playerName, targetPlayerIds, targetCharacterKeys, affectedGuildIds))
            {
                recipientPeerUids.Add(peer.m_uid);
            }
        }

        return recipientPeerUids;
    }

    private static bool ShouldReceiveGuildProjectionRefresh(
        long playerId,
        string accountId,
        string playerName,
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds)
    {
        if (playerId != 0L && WardAdminDebugAccess.IsPlayerAdminDebugController(playerId))
        {
            return true;
        }

        if (targetPlayerIds != null &&
            targetPlayerIds.Count > 0 &&
            playerId != 0L &&
            targetPlayerIds.Contains(playerId))
        {
            return true;
        }

        if (targetCharacterKeys != null && targetCharacterKeys.Count > 0)
        {
            var characterKey = BuildCharacterIdentityKey(accountId, playerName);
            if (!string.IsNullOrWhiteSpace(characterKey) && targetCharacterKeys.Contains(characterKey))
            {
                return true;
            }
        }

        if (affectedGuildIds == null || affectedGuildIds.Count == 0)
        {
            return false;
        }

        var guild = GetPlayerGuildIdentity(playerId);
        return guild.Id != 0 && affectedGuildIds.Contains(guild.Id);
    }

    private static bool UpsertSyncedGuildIdentity(
        long playerId,
        string accountId,
        string playerName,
        WardGuildIdentity guild,
        out WardGuildIdentity previousGuild)
    {
        previousGuild = default;
        var changed = false;
        var updated = new SyncedWardGuildIdentity(
            guild.Id != 0,
            guild.Id,
            guild.Name,
            accountId,
            playerName);
        if (playerId != 0L)
        {
            if (ServerSyncedGuildByPlayerId.TryGetValue(playerId, out var currentByPlayer))
            {
                previousGuild = ToWardGuildIdentity(currentByPlayer);
                var previousCharacterKey = BuildCharacterIdentityKey(
                    currentByPlayer.AccountId,
                    currentByPlayer.PlayerName);
                var updatedCharacterKey = BuildCharacterIdentityKey(updated.AccountId, updated.PlayerName);
                if (!string.Equals(previousCharacterKey, updatedCharacterKey, StringComparison.Ordinal) &&
                    !string.IsNullOrWhiteSpace(previousCharacterKey))
                {
                    ServerSyncedGuildByCharacterKey.Remove(previousCharacterKey);
                }
            }

            if (!ServerSyncedGuildByPlayerId.TryGetValue(playerId, out currentByPlayer) ||
                currentByPlayer.HasGuild != updated.HasGuild ||
                currentByPlayer.GuildId != updated.GuildId ||
                !string.Equals(currentByPlayer.GuildName, updated.GuildName, StringComparison.Ordinal) ||
                !string.Equals(currentByPlayer.AccountId, updated.AccountId, StringComparison.Ordinal) ||
                !string.Equals(currentByPlayer.PlayerName, updated.PlayerName, StringComparison.Ordinal))
            {
                ServerSyncedGuildByPlayerId[playerId] = updated;
                changed = true;
            }
        }

        var characterKey = BuildCharacterIdentityKey(accountId, playerName);
        if (string.IsNullOrWhiteSpace(characterKey))
        {
            return changed;
        }

        if (previousGuild.Id == 0 &&
            ServerSyncedGuildByCharacterKey.TryGetValue(characterKey, out var existingByCharacter))
        {
            previousGuild = ToWardGuildIdentity(existingByCharacter);
        }

        if (!ServerSyncedGuildByCharacterKey.TryGetValue(characterKey, out var currentByCharacter) ||
            currentByCharacter.HasGuild != updated.HasGuild ||
            currentByCharacter.GuildId != updated.GuildId ||
            !string.Equals(currentByCharacter.GuildName, updated.GuildName, StringComparison.Ordinal))
        {
            ServerSyncedGuildByCharacterKey[characterKey] = updated;
            changed = true;
        }

        return changed;
    }

    private static WardGuildIdentity ToWardGuildIdentity(SyncedWardGuildIdentity syncedGuild)
    {
        return syncedGuild.HasGuild
            ? new WardGuildIdentity(syncedGuild.GuildId, syncedGuild.GuildName)
            : default;
    }
}
