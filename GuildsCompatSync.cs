using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct SyncedWardGuildIdentity
{
    internal SyncedWardGuildIdentity(bool hasGuild, int guildId, string guildName)
    {
        HasGuild = hasGuild;
        GuildId = guildId;
        GuildName = guildName ?? string.Empty;
    }

    internal bool HasGuild { get; }
    internal int GuildId { get; }
    internal string GuildName { get; }
}

internal readonly struct PendingPlayerGuildSync
{
    internal PendingPlayerGuildSync(long senderUid, int guildId, string guildName, DateTime firstSeenUtc)
    {
        SenderUid = senderUid;
        GuildId = guildId;
        GuildName = guildName ?? string.Empty;
        FirstSeenUtc = firstSeenUtc;
    }

    internal long SenderUid { get; }
    internal int GuildId { get; }
    internal string GuildName { get; }
    internal DateTime FirstSeenUtc { get; }
}

internal static partial class GuildsCompat
{
    private const string SyncPlayerGuildRpc = "STUWard_SyncPlayerGuild";
    private static readonly TimeSpan PendingPlayerGuildSyncLifetime = TimeSpan.FromSeconds(15);
    private static readonly Dictionary<long, SyncedWardGuildIdentity> ServerSyncedGuildByPlayerId = new();
    private static readonly Dictionary<string, SyncedWardGuildIdentity> ServerSyncedGuildByCharacterKey = new(StringComparer.Ordinal);
    private static readonly Dictionary<long, PendingPlayerGuildSync> PendingPlayerGuildSyncsBySender = new();

    private static bool _syncRpcsRegistered;
    private static bool _localGuildSyncPending = true;
    private static long _lastSyncedLocalPlayerId;
    private static int _lastSyncedLocalGuildId = int.MinValue;
    private static string _lastSyncedLocalGuildName = string.Empty;

    internal static void Update()
    {
        SyncLocalPlayerGuildIfNeeded(force: false);
        ProcessPendingPlayerGuildSyncs();
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
        PendingPlayerGuildSyncsBySender.Clear();
        _syncRpcsRegistered = false;
        _localGuildSyncPending = true;
        _lastSyncedLocalPlayerId = 0L;
        _lastSyncedLocalGuildId = int.MinValue;
        _lastSyncedLocalGuildName = string.Empty;
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

        var playerId = localPlayer.GetPlayerID();
        var accountId = WardOwnership.GetPlayerAccountId(localPlayer);
        if (playerId == 0L || string.IsNullOrWhiteSpace(accountId))
        {
            return;
        }

        var guild = GetPlayerGuildIdentity(localPlayer);
        var guildName = guild.Name ?? string.Empty;
        var changed = _localGuildSyncPending ||
                      playerId != _lastSyncedLocalPlayerId ||
                      guild.Id != _lastSyncedLocalGuildId ||
                      !string.Equals(guildName, _lastSyncedLocalGuildName, StringComparison.Ordinal);
        if (!force && !changed)
        {
            return;
        }

        _lastSyncedLocalPlayerId = playerId;
        _lastSyncedLocalGuildId = guild.Id;
        _lastSyncedLocalGuildName = guildName;
        _localGuildSyncPending = false;

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
    }

    private static void HandleSyncPlayerGuild(long sender, ZPackage pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var guildId = pkg.ReadInt();
        var guildName = pkg.ReadString();
        if (!TryApplySyncedGuildIdentity(sender, guildId, guildName))
        {
            PendingPlayerGuildSyncsBySender[sender] = new PendingPlayerGuildSync(sender, guildId, guildName, DateTime.UtcNow);
        }
    }

    private static void ProcessPendingPlayerGuildSyncs()
    {
        if (PendingPlayerGuildSyncsBySender.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        List<long>? expiredSenders = null;
        List<long>? appliedSenders = null;
        var now = DateTime.UtcNow;
        foreach (var entry in PendingPlayerGuildSyncsBySender)
        {
            if (now - entry.Value.FirstSeenUtc > PendingPlayerGuildSyncLifetime)
            {
                expiredSenders ??= new List<long>();
                expiredSenders.Add(entry.Key);
                continue;
            }

            if (!TryApplySyncedGuildIdentity(entry.Value.SenderUid, entry.Value.GuildId, entry.Value.GuildName))
            {
                continue;
            }

            appliedSenders ??= new List<long>();
            appliedSenders.Add(entry.Key);
        }

        if (expiredSenders != null)
        {
            foreach (var sender in expiredSenders)
            {
                PendingPlayerGuildSyncsBySender.Remove(sender);
            }
        }

        if (appliedSenders == null)
        {
            return;
        }

        foreach (var sender in appliedSenders)
        {
            PendingPlayerGuildSyncsBySender.Remove(sender);
        }
    }

    private static bool TryApplySyncedGuildIdentity(long sender, int guildId, string guildName)
    {
        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, "GuildsCompat.Sync", out var playerId))
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

        var guild = guildId != 0
            ? new WardGuildIdentity(guildId, guildName)
            : default;
        var playerName = WardOwnership.GetPlayerName(playerId);
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

    private static void NotifyGuildProjectionRefreshApplied(
        string reason,
        bool fullRefresh,
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds)
    {
        var refreshReason = string.IsNullOrWhiteSpace(reason) ? "guild projection refreshed" : reason;
        HashSet<long>? recipientPeerUids = null;
        if (!fullRefresh)
        {
            if (ZNet.instance == null || !ZNet.instance.IsServer())
            {
                return;
            }

            var peers = ZNet.instance.GetPeers();
            if (peers == null)
            {
                return;
            }

            recipientPeerUids = CollectGuildProjectionRefreshRecipients(peers, targetPlayerIds, targetCharacterKeys, affectedGuildIds);
            if (recipientPeerUids.Count == 0)
            {
                return;
            }
        }

        ManagedWardMapStateService.NotifyViewerProjectionChanged(
            refreshReason,
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

        return TryGetSyncedGuildIdentity(playerId, accountId, playerName, out var guild) &&
               guild.Id != 0 &&
               affectedGuildIds.Contains(guild.Id);
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
        var updated = new SyncedWardGuildIdentity(guild.Id != 0, guild.Id, guild.Name);
        if (playerId != 0L)
        {
            if (ServerSyncedGuildByPlayerId.TryGetValue(playerId, out var currentByPlayer))
            {
                previousGuild = ToWardGuildIdentity(currentByPlayer);
            }

            if (!ServerSyncedGuildByPlayerId.TryGetValue(playerId, out currentByPlayer) ||
                currentByPlayer.HasGuild != updated.HasGuild ||
                currentByPlayer.GuildId != updated.GuildId ||
                !string.Equals(currentByPlayer.GuildName, updated.GuildName, StringComparison.Ordinal))
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
