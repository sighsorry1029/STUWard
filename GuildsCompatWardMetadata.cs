using System;
using System.Collections;
using System.Collections.Generic;
using HarmonyLib;

namespace STUWard;

internal static partial class GuildsCompat
{
    private sealed class PendingWardGuildProjectionRefreshState
    {
        internal readonly HashSet<long> TargetPlayerIds = new();
        internal readonly HashSet<string> TargetCharacterKeys = new(StringComparer.Ordinal);
        internal readonly HashSet<int> AffectedGuildIds = new();

        internal bool PendingFullRefresh;
        internal bool PendingLiveDisplayRefresh;
        internal DateTime FlushAtUtc = DateTime.MinValue;
    }

    private static readonly TimeSpan PendingWardGuildProjectionRefreshDebounce = TimeSpan.FromMilliseconds(250);
    private static readonly PendingWardGuildProjectionRefreshState PendingWardGuildProjectionRefresh = new();

    internal static void ResetPendingWardGuildProjectionRefreshes()
    {
        PendingWardGuildProjectionRefresh.TargetPlayerIds.Clear();
        PendingWardGuildProjectionRefresh.TargetCharacterKeys.Clear();
        PendingWardGuildProjectionRefresh.AffectedGuildIds.Clear();
        PendingWardGuildProjectionRefresh.PendingFullRefresh = false;
        PendingWardGuildProjectionRefresh.PendingLiveDisplayRefresh = false;
        PendingWardGuildProjectionRefresh.FlushAtUtc = DateTime.MinValue;
    }

    internal static int GetWardGuildId(PrivateArea? area)
    {
        var zdo = GetWardZdo(area);
        if (TryGetStoredWardGuildIdentity(zdo, out var storedGuild))
        {
            return storedGuild.Id;
        }

        return TryResolveWardGuildIdentity(area, out var guild) ? guild.Id : 0;
    }

    internal static string GetWardGuildName(PrivateArea? area)
    {
        var zdo = GetWardZdo(area);
        if (TryGetStoredWardGuildIdentity(zdo, out var storedGuild) &&
            !string.IsNullOrWhiteSpace(storedGuild.Name))
        {
            return storedGuild.Name;
        }

        if (TryResolveWardGuildIdentity(area, out var guild))
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
        var wardSteamAccountId = WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId);
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
        out WardGuildIdentity guild)
    {
        guild = default;
        if (area == null)
        {
            return false;
        }

        var zdo = GetWardZdo(area);
        var ownerPlayerId = WardAccess.GetCanonicalCreatorPlayerId(area);
        var wardSteamAccountId = WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId);
        var ownerName = GetWardOwnerNameForProjection(zdo);
        if (TryResolveWardGuildIdentityReadOnly(
                ownerPlayerId,
                wardSteamAccountId,
                ownerName,
                treatResolvedNoGuildAsResolved: true,
                out guild))
        {
            return true;
        }

        return false;
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
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            if (TryGetSyncedGuildIdentity(
                    ownerPlayerId,
                    normalizedAccountId,
                    normalizedOwnerName,
                    out guild))
            {
                return treatResolvedNoGuildAsResolved || guild.Id != 0;
            }

            if (TryResolveAuthoritativeGuildIdentity(
                    ownerPlayerId,
                    normalizedAccountId,
                    normalizedOwnerName,
                    out guild))
            {
                return treatResolvedNoGuildAsResolved || guild.Id != 0;
            }

            return false;
        }

        if (ownerPlayerId != 0L && TryGetGuild(ownerPlayerId, out guild))
        {
            return true;
        }

        return !string.IsNullOrWhiteSpace(normalizedAccountId) &&
               !string.IsNullOrWhiteSpace(normalizedOwnerName) &&
               TryGetGuildByAccountAndName(normalizedAccountId, normalizedOwnerName, out guild);
    }

    internal static void HandleGuildSaved(object? guild)
    {
        RefreshWardGuildProjectionForGuild(guild);
    }

    internal static void RefreshAllWardGuildProjections(bool liveDisplayRefresh = false)
    {
        InvalidateAllSyncedGuildIdentities();
        QueueWardGuildProjectionRefreshForAll(liveDisplayRefresh);
    }

    private static void RefreshWardGuildProjectionForGuild(object? guild)
    {
        var resolvedGuildId = TryParseGuild(guild, out var resolvedGuild) ? resolvedGuild.Id : 0;
        InvalidateSyncedGuildIdentitiesForGuild(resolvedGuildId);
        var memberIdentities = CollectGuildMemberCharacterIdentities(guild, out var hadUnresolvedMembers);
        if (memberIdentities.Count == 0 || hadUnresolvedMembers)
        {
            QueueWardGuildProjectionRefreshForAll(liveDisplayRefresh: false);
            return;
        }

        foreach (var identity in memberIdentities)
        {
            InvalidateSyncedGuildIdentity(identity);
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
        var pendingState = PendingWardGuildProjectionRefresh;
        if (!pendingState.PendingFullRefresh &&
            pendingState.TargetPlayerIds.Count == 0 &&
            pendingState.TargetCharacterKeys.Count == 0 &&
            pendingState.AffectedGuildIds.Count == 0)
        {
            return;
        }

        if (ZNet.instance == null)
        {
            return;
        }

        if (!ZNet.instance.IsServer())
        {
            ResetPendingWardGuildProjectionRefreshes();
            return;
        }

        if (pendingState.FlushAtUtc > DateTime.UtcNow)
        {
            return;
        }

        var pendingFullRefresh = pendingState.PendingFullRefresh;
        var pendingLiveDisplayRefresh = pendingState.PendingLiveDisplayRefresh;
        var targetPlayerIds = pendingState.TargetPlayerIds.Count == 0
            ? null
            : new HashSet<long>(pendingState.TargetPlayerIds);
        var targetCharacterKeys = pendingState.TargetCharacterKeys.Count == 0
            ? null
            : new HashSet<string>(pendingState.TargetCharacterKeys, StringComparer.Ordinal);
        var affectedGuildIds = pendingState.AffectedGuildIds.Count == 0
            ? null
            : new HashSet<int>(pendingState.AffectedGuildIds);

        ResetPendingWardGuildProjectionRefreshes();
        RefreshWardGuildProjectionForManagedWards(
            targetPlayerIds,
            targetCharacterKeys,
            affectedGuildIds,
            pendingFullRefresh,
            pendingLiveDisplayRefresh);
    }

    private static void QueueWardGuildProjectionRefreshForAll(bool liveDisplayRefresh)
    {
        if (ZNet.instance != null && !ZNet.instance.IsServer())
        {
            if (liveDisplayRefresh)
            {
                ManagedWardMapStateService.RequestLocalDisplayRefresh(refreshImmediatelyIfVisible: true);
            }

            return;
        }

        PendingWardGuildProjectionRefresh.PendingFullRefresh = true;
        PendingWardGuildProjectionRefresh.TargetPlayerIds.Clear();
        PendingWardGuildProjectionRefresh.TargetCharacterKeys.Clear();
        UpdatePendingWardGuildProjectionRefreshWindow(liveDisplayRefresh);
    }

    private static void QueueWardGuildProjectionRefreshForCharacter(
        WardGuildCharacterIdentity identity,
        bool liveDisplayRefresh,
        int affectedGuildId,
        int previousGuildId)
    {
        if (ZNet.instance != null && !ZNet.instance.IsServer())
        {
            if (liveDisplayRefresh)
            {
                ManagedWardMapStateService.RequestLocalDisplayRefresh(refreshImmediatelyIfVisible: true);
            }

            return;
        }

        if (!PendingWardGuildProjectionRefresh.PendingFullRefresh)
        {
            if (identity.HasPlayerId)
            {
                PendingWardGuildProjectionRefresh.TargetPlayerIds.Add(identity.PlayerId);
            }

            if (identity.HasAccountAndName)
            {
                var characterKey = BuildCharacterIdentityKey(identity.AccountId, identity.PlayerName);
                if (!string.IsNullOrWhiteSpace(characterKey))
                {
                    PendingWardGuildProjectionRefresh.TargetCharacterKeys.Add(characterKey);
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

    private static void RefreshWardGuildProjectionForManagedWards(
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds,
        bool fullRefresh,
        bool liveDisplayRefresh)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        if (!fullRefresh &&
            (targetPlayerIds == null || targetPlayerIds.Count == 0) &&
            (targetCharacterKeys == null || targetCharacterKeys.Count == 0) &&
            (affectedGuildIds == null || affectedGuildIds.Count == 0))
        {
            return;
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
                WardOwnership.ResolveWardSteamAccountId(managedWardZdo, ownerPlayerId));

            _ = ManagedWardProjectionService.RefreshProjectedMetadata(
                managedWardZdo,
                ownerPlayerId,
                wardAccountId);
        }

        if (liveDisplayRefresh)
        {
            NotifyGuildProjectionRefreshApplied(
                fullRefresh,
                targetPlayerIds,
                targetCharacterKeys,
                affectedGuildIds);
        }
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
        foreach (var cachedGuild in PlayerGuildCache)
        {
            var authoritativeAccountId = WardOwnership.NormalizeAccountIdValue(
                WardOwnership.GetPlayerAccountId(cachedGuild.Key));
            if (!string.Equals(authoritativeAccountId, normalizedAccountId, StringComparison.Ordinal))
            {
                continue;
            }

            playerIdsToInvalidate.Add(cachedGuild.Key);
        }

        foreach (var playerId in playerIdsToInvalidate)
        {
            PlayerGuildCache.Remove(playerId);
        }
    }

    private static void InvalidateAllGuildCaches()
    {
        PlayerGuildCache.Clear();
    }
}
