using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace STUWard;

internal static partial class GuildsCompat
{
    private sealed class PendingWardGuildProjectionRefreshState
    {
        internal readonly Dictionary<long, WardGuildCharacterIdentity> TargetIdentitiesByPlayerId = new();
        internal readonly Dictionary<string, WardGuildCharacterIdentity> TargetIdentitiesByCharacterKey = new(StringComparer.Ordinal);
        internal readonly HashSet<int> AffectedGuildIds = new();

        internal bool PendingFullRefresh;
        internal bool PendingLiveDisplayRefresh;
        internal DateTime FlushAtUtc = DateTime.MinValue;
    }

    private static readonly TimeSpan PendingWardGuildProjectionRefreshDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly PendingWardGuildProjectionRefreshState PendingWardGuildProjectionRefresh = new();

    internal static void ResetPendingWardGuildProjectionRefreshes()
    {
        PendingWardGuildProjectionRefresh.TargetIdentitiesByPlayerId.Clear();
        PendingWardGuildProjectionRefresh.TargetIdentitiesByCharacterKey.Clear();
        PendingWardGuildProjectionRefresh.AffectedGuildIds.Clear();
        PendingWardGuildProjectionRefresh.PendingFullRefresh = false;
        PendingWardGuildProjectionRefresh.PendingLiveDisplayRefresh = false;
        PendingWardGuildProjectionRefresh.FlushAtUtc = DateTime.MinValue;
    }

    internal static bool TryStampLocalWardGuildMetadata(PrivateArea? area)
    {
        return TryStampLocalWardGuildMetadata(ManagedWardRef.FromArea(area));
    }

    internal static bool TryStampLocalWardGuildMetadata(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null || !ManagedWardIdentity.EnsureManagedComponent(ward))
        {
            return false;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            return false;
        }

        if (!WardAccess.IsDirectWardOwner(ward, localPlayer.GetPlayerID()))
        {
            return false;
        }

        if (!ward.HasValidNetworkIdentity || !ward.IsOwner)
        {
            return false;
        }

        var zdo = ward.Zdo;
        if (zdo == null)
        {
            return false;
        }

        var guildIdentity = TryGetGuild(localPlayer, out var guild) && guild.Id != 0
            ? new WardGuildIdentity(guild.Id, guild.Name ?? string.Empty)
            : default;
        var projection = ManagedWardProjectionService.ResolveExplicitProjection(
            localPlayer.GetPlayerID(),
            WardOwnership.GetPlayerAccountId(localPlayer),
            guildIdentity);
        var projectionResult = ManagedWardMetadataMutationService.ApplyOwnedLocalProjection(
            zdo,
            projection,
            forceSendWhenMetadataChanged: false);
        return projectionResult.GuildChanged;
    }

    internal static int GetWardGuildId(PrivateArea? area)
    {
        var zdo = GetWardZdo(area);
        if (TryGetStoredWardGuildIdentity(zdo, out var storedGuild))
        {
            return storedGuild.Id;
        }

        return TryResolveWardGuildIdentity(area, allowMetadataStamp: false, out var guild) ? guild.Id : 0;
    }

    internal static string GetWardGuildName(PrivateArea? area)
    {
        var zdo = GetWardZdo(area);
        if (TryGetStoredWardGuildIdentity(zdo, out var storedGuild) &&
            !string.IsNullOrWhiteSpace(storedGuild.Name))
        {
            return storedGuild.Name;
        }

        if (TryResolveWardGuildIdentity(area, allowMetadataStamp: false, out var guild))
        {
            return guild.Name;
        }

        var storedGuildId = storedGuild.Id;
        if (storedGuildId != 0 && TryGetGuildById(storedGuildId, out guild))
        {
            return guild.Name;
        }

        var localPlayer = Player.m_localPlayer;
        if (storedGuildId != 0 &&
            localPlayer != null &&
            TryGetGuild(localPlayer, out guild) &&
            guild.Id == storedGuildId)
        {
            return guild.Name;
        }

        return string.Empty;
    }

    internal static WardGuildIdentity ResolveWardGuildIdentityReadOnly(ZDO? zdo)
    {
        if (TryGetStoredWardGuildIdentity(zdo, out var storedGuild) && storedGuild.Id != 0)
        {
            return storedGuild;
        }

        if (zdo == null)
        {
            return default;
        }

        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        var wardSteamAccountId = WardOwnership.ResolveWardSteamAccountId(
            zdo,
            ownerPlayerId,
            WardOwnership.GetWardSteamAccountId(zdo));
        var ownerName = GetWardOwnerNameForProjection(zdo);
        return TryResolveWardGuildIdentityReadOnly(
                ownerPlayerId,
                wardSteamAccountId,
                ownerName,
                treatResolvedNoGuildAsResolved: false,
                out var guild)
            ? guild
            : default;
    }

    private static bool TryGetStoredWardGuildIdentity(ZDO? zdo, out WardGuildIdentity guild)
    {
        guild = default;
        if (zdo == null)
        {
            return false;
        }

        var guildId = zdo.GetInt(GuildIdKey, 0);
        var guildName = zdo.GetString(GuildNameKey, string.Empty) ?? string.Empty;
        if (guildId == 0 && string.IsNullOrWhiteSpace(guildName))
        {
            return false;
        }

        guild = new WardGuildIdentity(guildId, guildName.Trim());
        return true;
    }

    private static ZDO? GetWardZdo(PrivateArea? area)
    {
        return WardPrivateAreaSafeAccess.GetZdo(area);
    }

    private static bool TryResolveWardGuildIdentity(
        PrivateArea? area,
        bool allowMetadataStamp,
        out WardGuildIdentity guild)
    {
        guild = default;
        if (area == null)
        {
            return false;
        }

        var ownerPlayerId = WardAccess.GetCanonicalCreatorPlayerId(area);
        var wardSteamAccountId = WardOwnership.ResolveWardSteamAccountId(GetWardZdo(area), ownerPlayerId, WardOwnership.GetWardSteamAccountId(area));
        var ownerName = GetWardOwnerName(area);
        if (TryResolveWardGuildIdentityReadOnly(
                ownerPlayerId,
                wardSteamAccountId,
                ownerName,
                treatResolvedNoGuildAsResolved: true,
                out guild))
        {
            if (allowMetadataStamp)
            {
                StampResolvedWardGuildMetadata(area, ownerPlayerId, wardSteamAccountId, guild);
            }

            return true;
        }

        return false;
    }

    private static string GetWardOwnerName(PrivateArea? area)
    {
        if (area == null)
        {
            return string.Empty;
        }

        var creatorName = WardPrivateAreaSafeAccess.GetCreatorName(area);
        if (!string.IsNullOrWhiteSpace(creatorName))
        {
            return creatorName.Trim();
        }

        return GetWardOwnerNameForProjection(GetWardZdo(area));
    }

    internal static string GetWardOwnerNameForProjection(ZDO? zdo)
    {
        return WardPrivateAreaSafeAccess.GetCreatorName(zdo);
    }

    internal static bool TryResolveProjectedGuildIdentity(
        long ownerPlayerId,
        string normalizedAccountId,
        string ownerName,
        out WardGuildIdentity guild)
    {
        return TryResolveWardGuildIdentityReadOnly(
            ownerPlayerId,
            normalizedAccountId,
            ownerName,
            treatResolvedNoGuildAsResolved: true,
            out guild);
    }

    private static bool TryResolveWardGuildIdentityReadOnly(
        long ownerPlayerId,
        string wardSteamAccountId,
        string ownerName,
        bool treatResolvedNoGuildAsResolved,
        out WardGuildIdentity guild)
    {
        guild = default;
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(wardSteamAccountId);
        var normalizedOwnerName = ownerName?.Trim() ?? string.Empty;
        if (ZNet.instance != null &&
            ZNet.instance.IsServer() &&
            TryGetSyncedGuildIdentity(ownerPlayerId, normalizedAccountId, normalizedOwnerName, out guild))
        {
            return treatResolvedNoGuildAsResolved || guild.Id != 0;
        }

        if (ownerPlayerId != 0L && TryGetGuild(ownerPlayerId, out guild))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(normalizedAccountId) &&
               !string.IsNullOrWhiteSpace(normalizedOwnerName) &&
               TryGetGuildByAccountAndName(normalizedAccountId, normalizedOwnerName, out guild);
    }

    private static void StampResolvedWardGuildMetadata(PrivateArea? area, long ownerPlayerId, string wardSteamAccountId, WardGuildIdentity guild)
    {
        if (guild.Id == 0 || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var zdo = GetWardZdo(area);
        if (zdo == null)
        {
            return;
        }

        _ = ManagedWardMetadataMutationService.ApplyExplicitProjection(
            zdo,
            ManagedWardProjectionService.ResolveExplicitProjection(ownerPlayerId, wardSteamAccountId, guild));
    }

    internal static void HandleGuildSaved(object? guild)
    {
        RefreshWardGuildProjectionForGuild(guild);
    }

    internal static void RefreshAllWardGuildProjections(bool liveDisplayRefresh = false)
    {
        QueueWardGuildProjectionRefreshForAll(liveDisplayRefresh);
    }

    private static void RefreshWardGuildProjectionForGuild(object? guild)
    {
        var resolvedGuildId = TryParseGuild(guild, out var resolvedGuild) ? resolvedGuild.Id : 0;
        var memberIdentities = CollectGuildMemberCharacterIdentities(guild, out var hadUnresolvedMembers);
        if (memberIdentities.Count == 0 || hadUnresolvedMembers)
        {
            QueueWardGuildProjectionRefreshForAll(liveDisplayRefresh: false);
            return;
        }

        foreach (var identity in memberIdentities)
        {
            RefreshWardGuildProjectionForCharacter(identity, affectedGuildId: resolvedGuildId);
        }
    }

    private static void RefreshWardGuildProjectionForCharacter(
        WardGuildCharacterIdentity identity,
        bool liveDisplayRefresh = false,
        int affectedGuildId = 0,
        int previousGuildId = 0)
    {
        if (!identity.HasPlayerId && !identity.HasAccountAndName)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(identity.AccountId))
        {
            InvalidateGuildCacheForAccountId(identity.AccountId);
        }

        QueueWardGuildProjectionRefreshForCharacter(
            identity,
            liveDisplayRefresh,
            affectedGuildId,
            previousGuildId);
    }

    internal static void ProcessPendingWardGuildProjectionRefreshes()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var pendingState = PendingWardGuildProjectionRefresh;
        if (!pendingState.PendingFullRefresh &&
            pendingState.TargetIdentitiesByPlayerId.Count == 0 &&
            pendingState.TargetIdentitiesByCharacterKey.Count == 0 &&
            pendingState.AffectedGuildIds.Count == 0)
        {
            return;
        }

        if (pendingState.FlushAtUtc > DateTime.UtcNow)
        {
            return;
        }

        var pendingFullRefresh = pendingState.PendingFullRefresh;
        var pendingLiveDisplayRefresh = pendingState.PendingLiveDisplayRefresh;
        var targetPlayerIds = pendingState.TargetIdentitiesByPlayerId.Count == 0
            ? null
            : new HashSet<long>(pendingState.TargetIdentitiesByPlayerId.Keys);
        var targetCharacterKeys = pendingState.TargetIdentitiesByCharacterKey.Count == 0
            ? null
            : new HashSet<string>(pendingState.TargetIdentitiesByCharacterKey.Keys, StringComparer.Ordinal);
        var affectedGuildIds = pendingState.AffectedGuildIds.Count == 0
            ? null
            : new HashSet<int>(pendingState.AffectedGuildIds);

        ResetPendingWardGuildProjectionRefreshes();
        _ = RefreshWardGuildProjectionForManagedWards(
            targetPlayerIds,
            targetCharacterKeys,
            affectedGuildIds,
            pendingFullRefresh,
            pendingLiveDisplayRefresh);
    }

    private static void QueueWardGuildProjectionRefreshForAll(bool liveDisplayRefresh)
    {
        PendingWardGuildProjectionRefresh.PendingFullRefresh = true;
        PendingWardGuildProjectionRefresh.TargetIdentitiesByPlayerId.Clear();
        PendingWardGuildProjectionRefresh.TargetIdentitiesByCharacterKey.Clear();
        UpdatePendingWardGuildProjectionRefreshWindow(liveDisplayRefresh);
    }

    private static void QueueWardGuildProjectionRefreshForCharacter(
        WardGuildCharacterIdentity identity,
        bool liveDisplayRefresh,
        int affectedGuildId,
        int previousGuildId)
    {
        if (!PendingWardGuildProjectionRefresh.PendingFullRefresh)
        {
            if (identity.HasPlayerId)
            {
                PendingWardGuildProjectionRefresh.TargetIdentitiesByPlayerId[identity.PlayerId] = MergeQueuedWardGuildCharacterIdentity(
                    PendingWardGuildProjectionRefresh.TargetIdentitiesByPlayerId.TryGetValue(identity.PlayerId, out var existingByPlayer)
                        ? existingByPlayer
                        : default,
                    identity);
            }

            if (identity.HasAccountAndName)
            {
                var characterKey = BuildCharacterIdentityKey(identity.AccountId, identity.PlayerName);
                if (!string.IsNullOrWhiteSpace(characterKey))
                {
                    PendingWardGuildProjectionRefresh.TargetIdentitiesByCharacterKey[characterKey] = MergeQueuedWardGuildCharacterIdentity(
                        PendingWardGuildProjectionRefresh.TargetIdentitiesByCharacterKey.TryGetValue(characterKey, out var existingByCharacter)
                            ? existingByCharacter
                            : default,
                        identity);
                }
            }
        }

        if (affectedGuildId != 0)
        {
            PendingWardGuildProjectionRefresh.AffectedGuildIds.Add(affectedGuildId);
        }

        if (previousGuildId != 0)
        {
            PendingWardGuildProjectionRefresh.AffectedGuildIds.Add(previousGuildId);
        }

        UpdatePendingWardGuildProjectionRefreshWindow(liveDisplayRefresh);
    }

    private static void UpdatePendingWardGuildProjectionRefreshWindow(bool liveDisplayRefresh)
    {
        if (liveDisplayRefresh)
        {
            PendingWardGuildProjectionRefresh.PendingLiveDisplayRefresh = true;
        }

        if (PendingWardGuildProjectionRefresh.FlushAtUtc == DateTime.MinValue)
        {
            PendingWardGuildProjectionRefresh.FlushAtUtc = DateTime.UtcNow + PendingWardGuildProjectionRefreshDebounce;
        }
    }

    private static WardGuildCharacterIdentity MergeQueuedWardGuildCharacterIdentity(
        WardGuildCharacterIdentity existingIdentity,
        WardGuildCharacterIdentity incomingIdentity)
    {
        var mergedPlayerId = existingIdentity.HasPlayerId ? existingIdentity.PlayerId : incomingIdentity.PlayerId;
        var mergedAccountId = !string.IsNullOrWhiteSpace(existingIdentity.AccountId)
            ? existingIdentity.AccountId
            : incomingIdentity.AccountId;
        var mergedPlayerName = !string.IsNullOrWhiteSpace(existingIdentity.PlayerName)
            ? existingIdentity.PlayerName
            : incomingIdentity.PlayerName;
        return new WardGuildCharacterIdentity(mergedPlayerId, mergedAccountId, mergedPlayerName);
    }

    private static bool RefreshWardGuildProjectionForManagedWards(
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds,
        bool fullRefresh,
        bool liveDisplayRefresh)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return false;
        }

        if (!fullRefresh &&
            (targetPlayerIds == null || targetPlayerIds.Count == 0) &&
            (targetCharacterKeys == null || targetCharacterKeys.Count == 0) &&
            (affectedGuildIds == null || affectedGuildIds.Count == 0))
        {
            return false;
        }

        if (fullRefresh)
        {
            InvalidateAllGuildCaches();
        }

        var candidateWardIds = new HashSet<ZDOID>();
        ManagedWardRegistry.CollectCandidateIds(
            candidateWardIds,
            targetPlayerIds,
            targetCharacterKeys,
            affectedGuildIds,
            fullRefresh);
        var changedCount = 0;
        foreach (var candidateWardId in candidateWardIds)
        {
            var managedWardZdo = ZDOMan.instance?.GetZDO(candidateWardId);
            if (managedWardZdo == null || !WardOwnership.IsManagedWardZdo(managedWardZdo))
            {
                ManagedWardRegistry.RemoveEntry(candidateWardId);
                continue;
            }

            var ownerPlayerId = managedWardZdo.GetLong(ZDOVars.s_creator, 0L);
            var wardAccountId = WardOwnership.NormalizeAccountIdValue(
                WardOwnership.ResolveWardSteamAccountId(
                    managedWardZdo,
                    ownerPlayerId,
                    WardOwnership.GetWardSteamAccountId(managedWardZdo)));

            if (!ManagedWardMetadataMutationService.RefreshProjectedMetadata(
                    managedWardZdo,
                    ownerPlayerId,
                    wardAccountId).AnyChanged)
            {
                continue;
            }

            changedCount++;
        }

        if (changedCount > 0 && liveDisplayRefresh)
        {
            NotifyGuildProjectionRefreshApplied(
                fullRefresh,
                targetPlayerIds,
                targetCharacterKeys,
                affectedGuildIds);
        }

        return changedCount > 0;
    }

    private static List<WardGuildCharacterIdentity> CollectGuildMemberCharacterIdentities(object? guild, out bool hadUnresolvedMembers)
    {
        var memberIdentitiesByKey = new Dictionary<string, WardGuildCharacterIdentity>(StringComparer.Ordinal);
        hadUnresolvedMembers = false;
        if (guild == null || GuildMembersField == null)
        {
            return new List<WardGuildCharacterIdentity>();
        }

        var members = GuildMembersField.GetValue(guild);
        if (members is IDictionary memberDictionary)
        {
            foreach (var key in memberDictionary.Keys)
            {
                if (!TryCreateCharacterIdentityFromPlayerReference(key, out var identity))
                {
                    hadUnresolvedMembers = true;
                    continue;
                }

                memberIdentitiesByKey[BuildCharacterIdentityKey(identity.AccountId, identity.PlayerName)] = identity;
            }

            return new List<WardGuildCharacterIdentity>(memberIdentitiesByKey.Values);
        }

        if (members is not IEnumerable enumerableMembers)
        {
            return new List<WardGuildCharacterIdentity>();
        }

        foreach (var entry in enumerableMembers)
        {
            if (entry == null)
            {
                continue;
            }

            var key = AccessTools.Property(entry.GetType(), "Key")?.GetValue(entry, null);
            if (!TryCreateCharacterIdentityFromPlayerReference(key, out var identity))
            {
                hadUnresolvedMembers = true;
                continue;
            }

            memberIdentitiesByKey[BuildCharacterIdentityKey(identity.AccountId, identity.PlayerName)] = identity;
        }

        return new List<WardGuildCharacterIdentity>(memberIdentitiesByKey.Values);
    }

    private static bool TryCreateCharacterIdentityFromPlayerReference(object? playerReference, out WardGuildCharacterIdentity identity)
    {
        identity = default;
        if (playerReference == null || PlayerReferenceIdField == null)
        {
            return false;
        }

        try
        {
            var accountId = WardOwnership.NormalizeAccountIdValue(PlayerReferenceIdField.GetValue(playerReference)?.ToString());
            var playerName = GetPlayerNameFromPlayerReference(playerReference);
            if (string.IsNullOrWhiteSpace(accountId) || string.IsNullOrWhiteSpace(playerName))
            {
                return false;
            }

            identity = new WardGuildCharacterIdentity(0L, accountId, playerName);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GetPlayerNameFromPlayerReference(object playerReference)
    {
        if (PlayerReferenceNameField != null)
        {
            var playerName = PlayerReferenceNameField.GetValue(playerReference)?.ToString()?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                return playerName;
            }
        }

        var serialized = playerReference.ToString()?.Trim() ?? string.Empty;
        var separatorIndex = serialized.IndexOf(':');
        if (separatorIndex < 0 || separatorIndex >= serialized.Length - 1)
        {
            return string.Empty;
        }

        return serialized.Substring(separatorIndex + 1).Trim();
    }

    private static void InvalidateGuildCacheForAccountId(string accountId)
    {
        var normalizedAccountId = WardOwnership.NormalizeAccountIdValue(accountId);
        if (string.IsNullOrWhiteSpace(normalizedAccountId))
        {
            return;
        }

        var playerIdsToInvalidate = new HashSet<long>();
        foreach (var cachedPlatformId in PlayerPlatformIdCache)
        {
            if (!cachedPlatformId.Value.HasPlatformId ||
                !string.Equals(cachedPlatformId.Value.PlatformId, normalizedAccountId, StringComparison.Ordinal))
            {
                continue;
            }

            playerIdsToInvalidate.Add(cachedPlatformId.Key);
        }

        var players = ZNet.instance?.m_players;
        if (players != null)
        {
            for (var index = 0; index < players.Count; index++)
            {
                var playerInfo = players[index];
                var onlineAccountId = WardOwnership.NormalizeAccountIdValue(playerInfo.m_userInfo.m_id.ToString());
                if (!string.Equals(onlineAccountId, normalizedAccountId, StringComparison.Ordinal))
                {
                    continue;
                }

                var playerZdo = ZDOMan.instance?.GetZDO(playerInfo.m_characterID);
                var playerId = playerZdo?.GetLong(ZDOVars.s_playerID, 0L) ?? 0L;
                if (playerId != 0L)
                {
                    playerIdsToInvalidate.Add(playerId);
                }
            }
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer != null &&
            string.Equals(WardOwnership.NormalizeAccountIdValue(WardOwnership.GetPlayerAccountId(localPlayer)), normalizedAccountId, StringComparison.Ordinal))
        {
            playerIdsToInvalidate.Add(localPlayer.GetPlayerID());
        }

        foreach (var playerId in playerIdsToInvalidate)
        {
            PlayerGuildCache.Remove(playerId);
            PlayerPlatformIdCache.Remove(playerId);
        }
    }

    private static void InvalidateAllGuildCaches()
    {
        PlayerGuildCache.Clear();
        PlayerPlatformIdCache.Clear();
    }

    internal static string DescribeGuildObject(object? guild)
    {
        if (guild == null)
        {
            return "null";
        }

        try
        {
            var guildName = GuildNameField?.GetValue(guild) as string ?? string.Empty;
            var guildId = GuildGeneralField != null && GuildGeneralIdField != null
                ? Convert.ToInt32(GuildGeneralIdField.GetValue(GuildGeneralField.GetValue(guild)))
                : 0;
            return $"type={guild.GetType().FullName}, id={guildId}, name='{guildName}'";
        }
        catch
        {
            return $"type={guild.GetType().FullName}";
        }
    }
}
