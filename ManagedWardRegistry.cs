using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct ManagedWardRegistryEntry
{
    internal ManagedWardRegistryEntry(
        ZDOID zdoId,
        long ownerPlayerId,
        string characterKey,
        int guildId)
    {
        ZdoId = zdoId;
        OwnerPlayerId = ownerPlayerId;
        CharacterKey = characterKey ?? string.Empty;
        GuildId = guildId;
    }

    internal ZDOID ZdoId { get; }
    internal long OwnerPlayerId { get; }
    internal string CharacterKey { get; }
    internal int GuildId { get; }
}

internal static class ManagedWardRegistry
{
    private static readonly Dictionary<ZDOID, ManagedWardRegistryEntry> ManagedWardRegistryEntriesByZdoId = new();
    private static readonly Dictionary<long, HashSet<ZDOID>> ManagedWardIdsByOwnerPlayerId = new();
    private static readonly Dictionary<int, HashSet<ZDOID>> ManagedWardIdsByGuildId = new();
    private static readonly Dictionary<string, HashSet<ZDOID>> ManagedWardIdsByCharacterKey = new(StringComparer.Ordinal);

    internal static void Reset()
    {
        ManagedWardRegistryEntriesByZdoId.Clear();
        ManagedWardIdsByOwnerPlayerId.Clear();
        ManagedWardIdsByGuildId.Clear();
        ManagedWardIdsByCharacterKey.Clear();
    }

    internal static void UpsertEntry(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid() || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        if (!WardOwnership.IsManagedWardZdo(zdo))
        {
            RemoveEntry(zdo.m_uid);
            return;
        }

        var entry = BuildManagedWardRegistryEntry(zdo);
        if (ManagedWardRegistryEntriesByZdoId.TryGetValue(entry.ZdoId, out var existingEntry))
        {
            RemoveManagedWardRegistryIndexes(existingEntry);
        }

        ManagedWardRegistryEntriesByZdoId[entry.ZdoId] = entry;
        AddManagedWardRegistryIndexes(entry);
    }

    internal static void RemoveEntry(ZDOID zdoId)
    {
        if (zdoId.IsNone() || !ManagedWardRegistryEntriesByZdoId.Remove(zdoId, out var entry))
        {
            return;
        }

        RemoveManagedWardRegistryIndexes(entry);
    }

    internal static int CollectCandidateIds(
        HashSet<ZDOID> candidateWardIds,
        HashSet<long>? targetPlayerIds,
        HashSet<string>? targetCharacterKeys,
        HashSet<int>? affectedGuildIds,
        bool fullRefresh)
    {
        candidateWardIds.Clear();

        if (fullRefresh)
        {
            foreach (var entry in ManagedWardRegistryEntriesByZdoId)
            {
                candidateWardIds.Add(entry.Key);
            }

            return candidateWardIds.Count;
        }

        if (targetPlayerIds != null)
        {
            foreach (var targetPlayerId in targetPlayerIds)
            {
                UnionManagedWardRegistryIds(candidateWardIds, ManagedWardIdsByOwnerPlayerId, targetPlayerId);
            }
        }

        if (targetCharacterKeys != null)
        {
            foreach (var targetCharacterKey in targetCharacterKeys)
            {
                if (string.IsNullOrWhiteSpace(targetCharacterKey))
                {
                    continue;
                }

                UnionManagedWardRegistryIds(candidateWardIds, ManagedWardIdsByCharacterKey, targetCharacterKey);
            }
        }

        if (affectedGuildIds != null)
        {
            foreach (var affectedGuildId in affectedGuildIds)
            {
                if (affectedGuildId == 0)
                {
                    continue;
                }

                UnionManagedWardRegistryIds(candidateWardIds, ManagedWardIdsByGuildId, affectedGuildId);
            }
        }

        return candidateWardIds.Count;
    }

    private static ManagedWardRegistryEntry BuildManagedWardRegistryEntry(ZDO zdo)
    {
        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        var accountId = WardOwnership.NormalizeAccountIdValue(
            WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId));
        var ownerName = (WardPrivateAreaSafeAccess.GetCreatorName(zdo) ?? string.Empty).Trim();
        var characterKey = GuildsCompat.BuildCharacterIdentityKey(accountId, ownerName);
        var guildId = GuildsCompat.GetWardGuildId(zdo);
        return new ManagedWardRegistryEntry(
            zdo.m_uid,
            ownerPlayerId,
            characterKey,
            guildId);
    }

    private static void AddManagedWardRegistryIndexes(ManagedWardRegistryEntry entry)
    {
        if (entry.OwnerPlayerId != 0L)
        {
            AddManagedWardRegistryId(ManagedWardIdsByOwnerPlayerId, entry.OwnerPlayerId, entry.ZdoId);
        }

        if (entry.GuildId != 0)
        {
            AddManagedWardRegistryId(ManagedWardIdsByGuildId, entry.GuildId, entry.ZdoId);
        }

        if (!string.IsNullOrWhiteSpace(entry.CharacterKey))
        {
            AddManagedWardRegistryId(ManagedWardIdsByCharacterKey, entry.CharacterKey, entry.ZdoId);
        }
    }

    private static void RemoveManagedWardRegistryIndexes(ManagedWardRegistryEntry entry)
    {
        if (entry.OwnerPlayerId != 0L)
        {
            RemoveManagedWardRegistryId(ManagedWardIdsByOwnerPlayerId, entry.OwnerPlayerId, entry.ZdoId);
        }

        if (entry.GuildId != 0)
        {
            RemoveManagedWardRegistryId(ManagedWardIdsByGuildId, entry.GuildId, entry.ZdoId);
        }

        if (!string.IsNullOrWhiteSpace(entry.CharacterKey))
        {
            RemoveManagedWardRegistryId(ManagedWardIdsByCharacterKey, entry.CharacterKey, entry.ZdoId);
        }
    }

    private static void AddManagedWardRegistryId<TKey>(
        Dictionary<TKey, HashSet<ZDOID>> indexedWardIds,
        TKey key,
        ZDOID zdoId)
        where TKey : notnull
    {
        if (!indexedWardIds.TryGetValue(key, out var wardIds))
        {
            wardIds = new HashSet<ZDOID>();
            indexedWardIds[key] = wardIds;
        }

        wardIds.Add(zdoId);
    }

    private static void RemoveManagedWardRegistryId<TKey>(
        Dictionary<TKey, HashSet<ZDOID>> indexedWardIds,
        TKey key,
        ZDOID zdoId)
        where TKey : notnull
    {
        if (!indexedWardIds.TryGetValue(key, out var wardIds))
        {
            return;
        }

        wardIds.Remove(zdoId);
        if (wardIds.Count == 0)
        {
            indexedWardIds.Remove(key);
        }
    }

    private static void UnionManagedWardRegistryIds<TKey>(
        HashSet<ZDOID> candidateWardIds,
        Dictionary<TKey, HashSet<ZDOID>> indexedWardIds,
        TKey key)
        where TKey : notnull
    {
        if (!indexedWardIds.TryGetValue(key, out var indexedIds))
        {
            return;
        }

        foreach (var indexedId in indexedIds)
        {
            candidateWardIds.Add(indexedId);
        }
    }
}
