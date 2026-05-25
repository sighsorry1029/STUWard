using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal readonly struct WardMinimapVisibilityIndexedEntry
{
    internal WardMinimapVisibilityIndexedEntry(
        ZDOID zdoId,
        long ownerPlayerId,
        int wardGuildId,
        UnityEngine.Vector3 position,
        float radius,
        bool isEnabled,
        long[] permittedPlayerIds)
    {
        ZdoId = zdoId;
        OwnerPlayerId = ownerPlayerId;
        WardGuildId = wardGuildId;
        Position = position;
        Radius = radius;
        IsEnabled = isEnabled;
        PermittedPlayerIds = permittedPlayerIds ?? Array.Empty<long>();
    }

    internal ZDOID ZdoId { get; }
    internal long OwnerPlayerId { get; }
    internal int WardGuildId { get; }
    internal UnityEngine.Vector3 Position { get; }
    internal float Radius { get; }
    internal bool IsEnabled { get; }
    internal long[] PermittedPlayerIds { get; }
}

internal static class WardMinimapVisibilityIndex
{
    private static readonly Dictionary<ZDOID, WardMinimapVisibilityIndexedEntry> IndexedWards = new();
    private static readonly Dictionary<ViewerCacheKey, ViewerCacheState> ViewerCaches = new();
    private static readonly List<ZDO> PrepareBuffer = new();
    private static readonly Dictionary<ZDOID, uint> IndexedWardDataRevisions = new();

    private static bool _prepared;
    private static int _indexRevision;
    private static int _nextViewerRevisionToken;

    private readonly struct ViewerCacheKey : IEquatable<ViewerCacheKey>
    {
        internal ViewerCacheKey(long playerId, int guildId, bool canSeeAllWards)
        {
            PlayerId = playerId;
            GuildId = guildId;
            CanSeeAllWards = canSeeAllWards;
        }

        internal long PlayerId { get; }
        internal int GuildId { get; }
        internal bool CanSeeAllWards { get; }

        public bool Equals(ViewerCacheKey other)
        {
            return PlayerId == other.PlayerId &&
                   GuildId == other.GuildId &&
                   CanSeeAllWards == other.CanSeeAllWards;
        }

        public override bool Equals(object? obj)
        {
            return obj is ViewerCacheKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = PlayerId.GetHashCode();
                hashCode = (hashCode * 397) ^ GuildId;
                hashCode = (hashCode * 397) ^ CanSeeAllWards.GetHashCode();
                return hashCode;
            }
        }
    }

    private sealed class ViewerCacheState
    {
        internal int IndexedRevision;
        internal int ViewerRevisionToken;
        internal ZDOID[] VisibleWardIds = Array.Empty<ZDOID>();
    }

    internal static void ResetRuntimeState()
    {
        IndexedWards.Clear();
        IndexedWardDataRevisions.Clear();
        ViewerCaches.Clear();
        PrepareBuffer.Clear();
        _prepared = false;
        _indexRevision = 0;
        _nextViewerRevisionToken = 0;
    }

    internal static void OnZNetAwake()
    {
        ResetRuntimeState();
    }

    internal static bool TryPrepare(ZDOMan? zdoMan, string reason)
    {
        if (_prepared)
        {
            return true;
        }

        if (zdoMan == null)
        {
            return false;
        }

        PrepareBuffer.Clear();
        var scanIndex = 0;
        while (!zdoMan.GetAllZDOsWithPrefabIterative(StuWardArea.PrefabName, PrepareBuffer, ref scanIndex))
        {
        }

        IndexedWards.Clear();
        var observedWardCount = 0;
        for (var index = 0; index < PrepareBuffer.Count; index++)
        {
            if (!TryBuildEntry(PrepareBuffer[index], out var entry))
            {
                continue;
            }

            IndexedWards[entry.ZdoId] = entry;
            observedWardCount++;
        }

        _prepared = true;
        BumpIndexRevision();
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Index",
            $"Prepared ward minimap visibility index. reason='{reason}', scannedWardCount={PrepareBuffer.Count}, indexedWardCount={observedWardCount}");
        return true;
    }

    internal static void ObserveManagedWard(ZDO? zdo)
    {
        UpdateEntry(zdo);
    }

    internal static void NotifyWardStateChanged(PrivateArea? area)
    {
        if (area == null)
        {
            return;
        }

        UpdateEntry(area);
    }

    internal static void NotifyWardStateChanged(ZDO? zdo)
    {
        UpdateEntry(zdo);
    }

    internal static bool ForgetWard(ZDOID zdoId)
    {
        IndexedWardDataRevisions.Remove(zdoId);
        if (!IndexedWards.Remove(zdoId))
        {
            return false;
        }

        BumpIndexRevision();
        return true;
    }

    internal static void InvalidateAll(string reason)
    {
        IndexedWards.Clear();
        IndexedWardDataRevisions.Clear();
        ViewerCaches.Clear();
        PrepareBuffer.Clear();
        _prepared = false;
        BumpIndexRevision();
        Plugin.LogWardDiagnosticVerbose("WardPins.Index", $"Invalidated ward minimap visibility index. reason='{reason}'");
    }

    internal static void ObserveSyncedWardState(
        ZDOID zdoId,
        uint dataRevision,
        long ownerPlayerId,
        int wardGuildId,
        UnityEngine.Vector3 position,
        float radius,
        bool isEnabled,
        long[] permittedPlayerIds)
    {
        var entry = new WardMinimapVisibilityIndexedEntry(
            zdoId,
            ownerPlayerId,
            wardGuildId,
            position,
            radius,
            isEnabled,
            permittedPlayerIds ?? Array.Empty<long>());
        ApplyEntry(entry, dataRevision);
    }

    internal static int GetViewerRevisionToken(long playerId, int guildId, bool canSeeAllWards)
    {
        return GetOrBuildViewerCache(playerId, guildId, canSeeAllWards).ViewerRevisionToken;
    }

    internal static ZDOID[] GetVisibleCandidateWardIds(long playerId, int guildId, bool canSeeAllWards)
    {
        return GetOrBuildViewerCache(playerId, guildId, canSeeAllWards).VisibleWardIds;
    }

    internal static bool TryGetEntry(ZDOID zdoId, out WardMinimapVisibilityIndexedEntry entry)
    {
        return IndexedWards.TryGetValue(zdoId, out entry);
    }

    internal static bool TryGetDataRevision(ZDOID zdoId, out uint dataRevision)
    {
        return IndexedWardDataRevisions.TryGetValue(zdoId, out dataRevision);
    }

    internal static int GetIndexedWardCount()
    {
        return IndexedWards.Count;
    }

    internal static void PruneViewerCaches(HashSet<long> activePlayerIds)
    {
        if (ViewerCaches.Count == 0)
        {
            return;
        }

        if (activePlayerIds.Count == 0)
        {
            var clearedCount = ViewerCaches.Count;
            ViewerCaches.Clear();
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Index",
                $"Pruned ward minimap viewer caches. removedCacheCount={clearedCount}, activePlayerCount=0");
            return;
        }

        List<ViewerCacheKey>? staleKeys = null;
        foreach (var viewerCache in ViewerCaches)
        {
            if (activePlayerIds.Contains(viewerCache.Key.PlayerId))
            {
                continue;
            }

            staleKeys ??= new List<ViewerCacheKey>();
            staleKeys.Add(viewerCache.Key);
        }

        if (staleKeys == null)
        {
            return;
        }

        for (var index = 0; index < staleKeys.Count; index++)
        {
            ViewerCaches.Remove(staleKeys[index]);
        }

        Plugin.LogWardDiagnosticVerbose(
            "WardPins.Index",
            $"Pruned ward minimap viewer caches. removedCacheCount={staleKeys.Count}, activePlayerCount={activePlayerIds.Count}");
    }

    private static ViewerCacheState GetOrBuildViewerCache(long playerId, int guildId, bool canSeeAllWards)
    {
        var cacheKey = new ViewerCacheKey(playerId, guildId, canSeeAllWards);
        if (!ViewerCaches.TryGetValue(cacheKey, out var cacheState))
        {
            cacheState = new ViewerCacheState();
            ViewerCaches[cacheKey] = cacheState;
        }

        if (cacheState.IndexedRevision == _indexRevision)
        {
            return cacheState;
        }

        cacheState.VisibleWardIds = BuildVisibleWardIds(playerId, guildId, canSeeAllWards);
        cacheState.IndexedRevision = _indexRevision;
        cacheState.ViewerRevisionToken = NextViewerRevisionToken();
        return cacheState;
    }

    private static ZDOID[] BuildVisibleWardIds(long playerId, int guildId, bool canSeeAllWards)
    {
        if (IndexedWards.Count == 0)
        {
            return Array.Empty<ZDOID>();
        }

        var visibleWardIds = new List<ZDOID>(IndexedWards.Count);
        var playerGuild = new WardGuildIdentity(guildId, string.Empty);
        foreach (var indexedWard in IndexedWards.Values)
        {
            if (!canSeeAllWards &&
                !ManagedWardAccessEvaluator.HasPlayerAccessToManagedWardIndexEntry(indexedWard, playerId, playerGuild))
            {
                continue;
            }

            visibleWardIds.Add(indexedWard.ZdoId);
        }

        return visibleWardIds.Count == 0 ? Array.Empty<ZDOID>() : visibleWardIds.ToArray();
    }

    private static void UpdateEntry(ZDO? zdo)
    {
        if (!TryBuildEntry(zdo, out var entry))
        {
            if (zdo != null)
            {
                ForgetWard(zdo.m_uid);
            }

            return;
        }

        ApplyEntry(entry, zdo!.DataRevision);
    }

    private static void UpdateEntry(PrivateArea? area)
    {
        if (!TryBuildEntry(area, out var entry))
        {
            var existingZdo = WardPrivateAreaSafeAccess.GetZdo(area);
            if (existingZdo != null)
            {
                ForgetWard(existingZdo.m_uid);
            }

            return;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        ApplyEntry(entry, zdo != null ? zdo.DataRevision : 0u);
    }

    private static void ApplyEntry(WardMinimapVisibilityIndexedEntry entry, uint dataRevision)
    {
        if (IndexedWardDataRevisions.TryGetValue(entry.ZdoId, out var currentRevision) && currentRevision > dataRevision)
        {
            return;
        }

        if (IndexedWards.TryGetValue(entry.ZdoId, out var existingEntry) &&
            EntriesEqual(existingEntry, entry) &&
            currentRevision == dataRevision)
        {
            return;
        }

        IndexedWards[entry.ZdoId] = entry;
        IndexedWardDataRevisions[entry.ZdoId] = dataRevision;
        BumpIndexRevision();
    }

    private static bool TryBuildEntry(ZDO? zdo, out WardMinimapVisibilityIndexedEntry entry)
    {
        if (!WardOwnership.IsManagedWardZdo(zdo))
        {
            entry = default;
            return false;
        }

        var managedZdo = zdo!;
        entry = new WardMinimapVisibilityIndexedEntry(
            managedZdo.m_uid,
            managedZdo.GetLong(ZDOVars.s_creator, 0L),
            GuildsCompat.ResolveWardGuildIdentityReadOnly(managedZdo).Id,
            managedZdo.GetPosition(),
            WardSettings.GetStoredRadius(managedZdo, WardSettings.MinRadius),
            managedZdo.GetBool(ZDOVars.s_enabled, false),
            WardPrivateAreaSafeAccess.GetPermittedPlayerIds(managedZdo));
        return true;
    }

    private static bool TryBuildEntry(PrivateArea? area, out WardMinimapVisibilityIndexedEntry entry)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (area == null || zdo == null || !WardOwnership.IsManagedWardZdo(zdo))
        {
            entry = default;
            return false;
        }

        entry = new WardMinimapVisibilityIndexedEntry(
            zdo.m_uid,
            zdo.GetLong(ZDOVars.s_creator, 0L),
            GuildsCompat.ResolveWardGuildIdentityReadOnly(zdo).Id,
            area.transform.position,
            WardSettings.GetStoredRadiusOrMin(area),
            area.IsEnabled(),
            WardPrivateAreaSafeAccess.GetPermittedPlayerIds(zdo));
        return true;
    }

    private static bool EntriesEqual(WardMinimapVisibilityIndexedEntry left, WardMinimapVisibilityIndexedEntry right)
    {
        if (left.OwnerPlayerId != right.OwnerPlayerId ||
            left.WardGuildId != right.WardGuildId ||
            left.Position != right.Position ||
            !Mathf.Approximately(left.Radius, right.Radius) ||
            left.IsEnabled != right.IsEnabled ||
            left.PermittedPlayerIds.Length != right.PermittedPlayerIds.Length)
        {
            return false;
        }

        for (var index = 0; index < left.PermittedPlayerIds.Length; index++)
        {
            if (left.PermittedPlayerIds[index] != right.PermittedPlayerIds[index])
            {
                return false;
            }
        }

        return true;
    }

    private static void BumpIndexRevision()
    {
        if (_indexRevision == int.MaxValue)
        {
            _indexRevision = 1;
            ViewerCaches.Clear();
            return;
        }

        _indexRevision++;
    }

    private static int NextViewerRevisionToken()
    {
        if (_nextViewerRevisionToken == int.MaxValue)
        {
            _nextViewerRevisionToken = 1;
            return _nextViewerRevisionToken;
        }

        _nextViewerRevisionToken++;
        return _nextViewerRevisionToken;
    }
}
