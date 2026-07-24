using System.Collections.Generic;
using HarmonyLib;

namespace STUWard;

internal static class WardPermittedSnapshots
{
    private const int BackfillBatchSize = 4;
    private const int SnapshotFormatVersion = 1;
    private const int MaxSnapshotDataBytes = 1024 * 1024;
    private static readonly int SnapshotVersionKey = "stuw_perm_snapshot_version".GetStableHashCode();
    private static readonly int SnapshotDataKey = "stuw_perm_snapshot".GetStableHashCode();
    private static readonly int SnapshotRevisionKey = "stuw_perm_snapshot_revision".GetStableHashCode();
    private static readonly Dictionary<ZDOID, CachedSnapshot> SnapshotCache = new();
    private static readonly List<BackfillRequest> PendingBackfillRequests = new();
    private static readonly HashSet<int> PendingBackfillAreaIds = new();

    private readonly struct BackfillRequest
    {
        internal BackfillRequest(PrivateArea area)
        {
            Area = area;
            InstanceId = area.GetInstanceID();
        }

        internal PrivateArea? Area { get; }
        internal int InstanceId { get; }
    }

    internal static void Refresh(PrivateArea? area)
    {
        if (!TryGetOwnedSnapshotZdo(area, out var zdo, out _))
        {
            return;
        }

        Refresh(zdo);
    }

    internal static void Refresh(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid() || !zdo.IsOwner() || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        WriteSnapshot(zdo, BuildEntries(zdo));
    }

    internal static void Backfill(ManagedWardRef ward)
    {
        if (!TryGetOwnedSnapshotZdo(ward, out var zdo, out _))
        {
            return;
        }

        if (HasCurrentSnapshot(zdo))
        {
            return;
        }

        EnqueueBackfill(ward.Area!);
    }

    internal static void Update()
    {
        if (PendingBackfillRequests.Count == 0 || ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        var processed = 0;
        while (processed < BackfillBatchSize && PendingBackfillRequests.Count > 0)
        {
            var lastIndex = PendingBackfillRequests.Count - 1;
            var request = PendingBackfillRequests[lastIndex];
            PendingBackfillRequests.RemoveAt(lastIndex);
            PendingBackfillAreaIds.Remove(request.InstanceId);
            processed++;

            if (request.Area == null ||
                !TryGetOwnedSnapshotZdo(request.Area, out var zdo, out _) ||
                HasCurrentSnapshot(zdo))
            {
                continue;
            }

            Refresh(request.Area);
        }
    }

    internal static bool HasPendingRuntimeWork()
    {
        return PendingBackfillRequests.Count > 0;
    }

    internal static bool TryGet(PrivateArea? area, long playerId, out string guildName, out string platformId)
    {
        guildName = string.Empty;
        platformId = string.Empty;

        if (area == null || playerId == 0L)
        {
            return false;
        }

        var nview = WardPrivateAreaSafeAccess.GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return false;
        }

        if (!TryGetSnapshot(nview, out var entries))
        {
            return false;
        }

        if (!entries.TryGetValue(playerId, out var entry))
        {
            return false;
        }

        guildName = entry.GuildName;
        platformId = entry.PlatformId;
        return !string.IsNullOrWhiteSpace(guildName) || !string.IsNullOrWhiteSpace(platformId);
    }

    internal static int GetRevision(PrivateArea? area)
    {
        if (area == null)
        {
            return 0;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        return zdo?.GetInt(SnapshotRevisionKey, 0) ?? 0;
    }

    private static void WriteSnapshot(ZDO zdo, List<SnapshotEntry> entries)
    {
        var package = new ZPackage();
        package.Write(SnapshotFormatVersion);
        package.Write(entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            package.Write(entry.PlayerId);
            package.Write(entry.GuildName);
            package.Write(entry.PlatformId);
        }

        var snapshotData = package.GetArray();
        var previousVersion = zdo.GetInt(SnapshotVersionKey, 0);
        var previousData = zdo.GetByteArray(SnapshotDataKey, null);
        var previousRevision = zdo.GetInt(SnapshotRevisionKey, 0);
        if (previousVersion != SnapshotFormatVersion ||
            previousRevision <= 0 ||
            !ByteArraysEqual(previousData, snapshotData))
        {
            zdo.Set(SnapshotVersionKey, SnapshotFormatVersion);
            zdo.Set(SnapshotDataKey, snapshotData);
            zdo.Set(SnapshotRevisionKey, previousRevision == int.MaxValue ? 1 : previousRevision + 1);
        }

        SnapshotCache[zdo.m_uid] = new CachedSnapshot(zdo.DataRevision, ToLookup(entries));
    }

    internal static void ClearCache()
    {
        SnapshotCache.Clear();
        PendingBackfillRequests.Clear();
        PendingBackfillAreaIds.Clear();
    }

    internal static void Forget(ZDOID zdoId)
    {
        if (!zdoId.IsNone())
        {
            SnapshotCache.Remove(zdoId);
        }
    }

    private static bool HasCurrentSnapshot(ZDO zdo)
    {
        return zdo.GetInt(SnapshotRevisionKey, 0) > 0 &&
               TryDeserialize(zdo, out _);
    }

    private static void EnqueueBackfill(PrivateArea area)
    {
        var instanceId = area.GetInstanceID();
        if (!PendingBackfillAreaIds.Add(instanceId))
        {
            return;
        }

        PendingBackfillRequests.Add(new BackfillRequest(area));
    }

    private static bool TryGetOwnedSnapshotZdo(PrivateArea? area, out ZDO zdo, out ZNetView nview)
    {
        return TryGetOwnedSnapshotZdo(ManagedWardRef.FromArea(area), out zdo, out nview);
    }

    private static bool TryGetOwnedSnapshotZdo(ManagedWardRef ward, out ZDO zdo, out ZNetView nview)
    {
        zdo = null!;
        nview = null!;

        var area = ward.Area;
        if (area == null ||
            Player.IsPlacementGhost(area.gameObject) ||
            ZNet.instance == null ||
            !ZNet.instance.IsServer())
        {
            return false;
        }

        nview = ward.NView!;
        if (!ward.HasValidNetworkIdentity || !ward.IsOwner)
        {
            return false;
        }

        zdo = ward.Zdo!;
        return true;
    }

    private static bool TryGetSnapshot(ZNetView nview, out Dictionary<long, SnapshotEntry> entries)
    {
        entries = null!;

        var zdo = WardPrivateAreaSafeAccess.GetZdo(nview);
        if (zdo == null)
        {
            return false;
        }

        if (SnapshotCache.TryGetValue(zdo.m_uid, out var cached) && cached.Revision == zdo.DataRevision)
        {
            entries = cached.Entries;
            return true;
        }

        if (!TryDeserialize(zdo, out entries))
        {
            return false;
        }

        SnapshotCache[zdo.m_uid] = new CachedSnapshot(zdo.DataRevision, entries);
        return true;
    }

    private static List<SnapshotEntry> BuildEntries(ZDO? zdo)
    {
        var permittedPlayerIds = WardPrivateAreaSafeAccess.GetPermittedPlayerIds(zdo);
        var entries = new List<SnapshotEntry>(permittedPlayerIds.Count);

        for (var index = 0; index < permittedPlayerIds.Count; index++)
        {
            var playerId = permittedPlayerIds[index];
            if (playerId == 0L)
            {
                continue;
            }

            entries.Add(new SnapshotEntry(
                playerId,
                GuildsCompat.GetPlayerGuildName(playerId) ?? string.Empty,
                WardOwnership.GetPlayerSteamIdDisplay(playerId) ?? string.Empty));
        }

        return entries;
    }

    private static bool TryDeserialize(ZDO zdo, out Dictionary<long, SnapshotEntry> entries)
    {
        entries = new Dictionary<long, SnapshotEntry>();
        try
        {
            if (zdo.GetInt(SnapshotVersionKey, 0) != SnapshotFormatVersion)
            {
                return false;
            }

            var snapshotData = zdo.GetByteArray(SnapshotDataKey, null);
            if (snapshotData == null || snapshotData.Length == 0 || snapshotData.Length > MaxSnapshotDataBytes)
            {
                return false;
            }

            var package = new ZPackage(snapshotData);
            if (package.ReadInt() != SnapshotFormatVersion)
            {
                return false;
            }

            var count = package.ReadInt();
            if (count < 0 || count > WardPrivateAreaSafeAccess.MaxPermittedPlayers)
            {
                return false;
            }

            for (var index = 0; index < count; index++)
            {
                var playerId = package.ReadLong();
                var guildName = package.ReadString();
                var platformId = package.ReadString();
                if (playerId != 0L)
                {
                    entries[playerId] = new SnapshotEntry(playerId, guildName, platformId);
                }
            }

            return true;
        }
        catch
        {
            entries.Clear();
            return false;
        }
    }

    private static Dictionary<long, SnapshotEntry> ToLookup(List<SnapshotEntry> entries)
    {
        var lookup = new Dictionary<long, SnapshotEntry>(entries.Count);

        for (var index = 0; index < entries.Count; index++)
        {
            var entry = entries[index];
            lookup[entry.PlayerId] = entry;
        }

        return lookup;
    }

    private static bool ByteArraysEqual(byte[]? left, byte[] right)
    {
        if (left == null || left.Length != right.Length)
        {
            return false;
        }

        for (var index = 0; index < right.Length; index++)
        {
            if (left[index] != right[index])
            {
                return false;
            }
        }

        return true;
    }

    private readonly struct SnapshotEntry
    {
        internal SnapshotEntry(long playerId, string guildName, string platformId)
        {
            PlayerId = playerId;
            GuildName = guildName;
            PlatformId = platformId;
        }

        internal long PlayerId { get; }

        internal string GuildName { get; }

        internal string PlatformId { get; }
    }

    private sealed class CachedSnapshot
    {
        internal CachedSnapshot(uint revision, Dictionary<long, SnapshotEntry> entries)
        {
            Revision = revision;
            Entries = entries;
        }

        internal uint Revision { get; }

        internal Dictionary<long, SnapshotEntry> Entries { get; }
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.AddPermitted))]
internal static class PrivateAreaAddPermittedSnapshotPatch
{
    private static void Postfix(PrivateArea __instance, long playerID)
    {
        var ward = ManagedWardRef.FromArea(__instance);
        if (!WardAccess.IsManagedWard(ward, false))
        {
            return;
        }

        WardPermittedSnapshots.Refresh(__instance);
        ManagedWardPresenceService.Invalidate();
        ManagedWardMapStateService.NotifyWardMutation(__instance);
        WardOwnership.ForceSyncManagedWardZdoToServer(ward);
    }
}

[HarmonyPatch(typeof(PrivateArea), nameof(PrivateArea.RemovePermitted))]
internal static class PrivateAreaRemovePermittedSnapshotPatch
{
    private static void Postfix(PrivateArea __instance, long playerID)
    {
        var ward = ManagedWardRef.FromArea(__instance);
        if (!WardAccess.IsManagedWard(ward, false))
        {
            return;
        }

        WardPermittedSnapshots.Refresh(__instance);
        ManagedWardPresenceService.Invalidate();
        ManagedWardMapStateService.NotifyWardMutation(__instance);
        WardOwnership.ForceSyncManagedWardZdoToServer(ward);
    }
}
