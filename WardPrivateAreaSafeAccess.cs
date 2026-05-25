using System;
using System.Collections.Generic;

namespace STUWard;

internal static class WardPrivateAreaSafeAccess
{
    private sealed class CachedPermittedPlayerIds
    {
        internal CachedPermittedPlayerIds(uint revision, long[] playerIds)
        {
            Revision = revision;
            PlayerIds = playerIds ?? Array.Empty<long>();
        }

        internal uint Revision { get; }
        internal long[] PlayerIds { get; }
    }

    private static readonly Dictionary<ZDOID, CachedPermittedPlayerIds> PermittedPlayerIdsCache = new();
    private static readonly List<int> PermittedPlayerIdKeys = new();
    private static readonly List<int> PermittedPlayerNameKeys = new();

    internal static ZNetView? GetNView(PrivateArea? area)
    {
        if (area == null)
        {
            return null;
        }

        return area.m_nview != null ? area.m_nview : area.GetComponent<ZNetView>();
    }

    internal static ZDO? GetZdo(PrivateArea? area)
    {
        return GetZdo(GetNView(area));
    }

    internal static ZDO? GetZdo(ZNetView? nview)
    {
        if (nview == null || !nview.IsValid())
        {
            return null;
        }

        return nview.GetZDO();
    }

    internal static string GetCreatorName(PrivateArea? area)
    {
        return GetCreatorName(GetZdo(area));
    }

    internal static string GetCreatorName(ZDO? zdo)
    {
        return zdo == null
            ? string.Empty
            : (zdo.GetString(ZDOVars.s_creatorName, string.Empty) ?? string.Empty).Trim();
    }

    internal static List<KeyValuePair<long, string>> GetPermittedPlayers(PrivateArea? area)
    {
        return GetPermittedPlayers(GetZdo(area));
    }

    internal static List<KeyValuePair<long, string>> GetPermittedPlayers(ZDO? zdo)
    {
        if (zdo == null)
        {
            return new List<KeyValuePair<long, string>>();
        }

        var permittedCount = zdo.GetInt(ZDOVars.s_permitted, 0);
        if (permittedCount <= 0)
        {
            return new List<KeyValuePair<long, string>>();
        }

        var permittedPlayers = new List<KeyValuePair<long, string>>(permittedCount);
        for (var index = 0; index < permittedCount; index++)
        {
            var playerId = zdo.GetLong(GetPermittedPlayerIdKey(index), 0L);
            if (playerId == 0L)
            {
                continue;
            }

            permittedPlayers.Add(new KeyValuePair<long, string>(
                playerId,
                zdo.GetString(GetPermittedPlayerNameKey(index), string.Empty) ?? string.Empty));
        }

        return permittedPlayers;
    }

    internal static long[] GetPermittedPlayerIds(PrivateArea? area)
    {
        return GetPermittedPlayerIds(GetZdo(area));
    }

    internal static long[] GetPermittedPlayerIds(ZDO? zdo)
    {
        if (zdo == null || !zdo.IsValid())
        {
            return Array.Empty<long>();
        }

        if (PermittedPlayerIdsCache.TryGetValue(zdo.m_uid, out var cached) && cached.Revision == zdo.DataRevision)
        {
            return cached.PlayerIds;
        }

        var permittedIds = ReadPermittedPlayerIds(zdo);
        PermittedPlayerIdsCache[zdo.m_uid] = new CachedPermittedPlayerIds(zdo.DataRevision, permittedIds);
        return permittedIds;
    }

    internal static bool IsPlayerPermitted(PrivateArea? area, long playerId)
    {
        return IsPlayerPermitted(GetZdo(area), playerId);
    }

    internal static bool IsPlayerPermitted(ZDO? zdo, long playerId)
    {
        if (playerId == 0L)
        {
            return false;
        }

        if (zdo == null)
        {
            return false;
        }

        var permittedPlayerIds = GetPermittedPlayerIds(zdo);
        for (var index = 0; index < permittedPlayerIds.Length; index++)
        {
            if (permittedPlayerIds[index] == playerId)
            {
                return true;
            }
        }

        return false;
    }

    internal static void SetPermittedPlayers(ZDO? zdo, IReadOnlyList<KeyValuePair<long, string>> permittedPlayers)
    {
        if (zdo == null)
        {
            return;
        }

        zdo.Set(ZDOVars.s_permitted, permittedPlayers.Count);
        var cachedPermittedIds = permittedPlayers.Count == 0 ? Array.Empty<long>() : new long[permittedPlayers.Count];
        for (var index = 0; index < permittedPlayers.Count; index++)
        {
            var entry = permittedPlayers[index];
            cachedPermittedIds[index] = entry.Key;
            zdo.Set(GetPermittedPlayerIdKey(index), entry.Key);
            zdo.Set(GetPermittedPlayerNameKey(index), entry.Value ?? string.Empty);
        }

        PermittedPlayerIdsCache[zdo.m_uid] = new CachedPermittedPlayerIds(zdo.DataRevision, cachedPermittedIds);
    }

    internal static bool HasLocalAccess(PrivateArea? area)
    {
        if (area == null)
        {
            return false;
        }

        var piece = area.m_piece != null ? area.m_piece : area.GetComponent<Piece>();
        if (piece != null && piece.IsCreator())
        {
            return true;
        }

        var localPlayer = Player.m_localPlayer;
        return localPlayer != null && IsPlayerPermitted(area, localPlayer.GetPlayerID());
    }

    internal static void ForgetPermittedPlayerIds(ZDOID zdoId)
    {
        if (zdoId.IsNone())
        {
            return;
        }

        PermittedPlayerIdsCache.Remove(zdoId);
    }

    internal static void ResetRuntimeState()
    {
        PermittedPlayerIdsCache.Clear();
    }

    internal static void OnZNetAwake()
    {
        ResetRuntimeState();
    }

    private static long[] ReadPermittedPlayerIds(ZDO zdo)
    {
        var permittedCount = zdo.GetInt(ZDOVars.s_permitted, 0);
        if (permittedCount <= 0)
        {
            return Array.Empty<long>();
        }

        var permittedPlayerIds = new long[permittedCount];
        var observedCount = 0;
        for (var index = 0; index < permittedCount; index++)
        {
            var playerId = zdo.GetLong(GetPermittedPlayerIdKey(index), 0L);
            if (playerId == 0L)
            {
                continue;
            }

            permittedPlayerIds[observedCount] = playerId;
            observedCount++;
        }

        if (observedCount == 0)
        {
            return Array.Empty<long>();
        }

        if (observedCount == permittedPlayerIds.Length)
        {
            return permittedPlayerIds;
        }

        var trimmedPlayerIds = new long[observedCount];
        Array.Copy(permittedPlayerIds, trimmedPlayerIds, observedCount);
        return trimmedPlayerIds;
    }

    private static int GetPermittedPlayerIdKey(int index)
    {
        EnsurePermittedPlayerKeys(index);
        return PermittedPlayerIdKeys[index];
    }

    private static int GetPermittedPlayerNameKey(int index)
    {
        EnsurePermittedPlayerKeys(index);
        return PermittedPlayerNameKeys[index];
    }

    private static void EnsurePermittedPlayerKeys(int index)
    {
        while (PermittedPlayerIdKeys.Count <= index)
        {
            var nextIndex = PermittedPlayerIdKeys.Count;
            PermittedPlayerIdKeys.Add($"pu_id{nextIndex}".GetStableHashCode());
            PermittedPlayerNameKeys.Add($"pu_name{nextIndex}".GetStableHashCode());
        }
    }
}
