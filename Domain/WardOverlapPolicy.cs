using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct WardOverlapArea
{
    internal WardOverlapArea(int id, float x, float z, float radius, long ownerPlayerId, int guildId)
    {
        Id = id;
        X = x;
        Z = z;
        Radius = radius;
        OwnerPlayerId = ownerPlayerId;
        GuildId = guildId;
    }

    internal int Id { get; }
    internal float X { get; }
    internal float Z { get; }
    internal float Radius { get; }
    internal long OwnerPlayerId { get; }
    internal int GuildId { get; }
}

internal readonly struct WardOverlapQuery
{
    internal WardOverlapQuery(float x, float z, float radius, long ownerPlayerId, int guildId, int ignoredAreaId = 0)
    {
        X = x;
        Z = z;
        Radius = radius;
        OwnerPlayerId = ownerPlayerId;
        GuildId = guildId;
        IgnoredAreaId = ignoredAreaId;
    }

    internal float X { get; }
    internal float Z { get; }
    internal float Radius { get; }
    internal long OwnerPlayerId { get; }
    internal int GuildId { get; }
    internal int IgnoredAreaId { get; }
}

internal static class WardOverlapPolicy
{
    internal static bool WouldOverlapForeignWard(WardOverlapQuery query, IEnumerable<WardOverlapArea> areas)
    {
        foreach (var area in areas)
        {
            if (ShouldIgnoreArea(query, area) || SharesTrustedWardGroup(area, query))
            {
                continue;
            }

            if (Overlaps(query, area))
            {
                return true;
            }
        }

        return false;
    }

    internal static float GetMaxNonOverlappingRadius(
        float fallbackRadius,
        WardOverlapQuery query,
        IEnumerable<WardOverlapArea> areas)
    {
        var maxRadius = fallbackRadius;
        foreach (var area in areas)
        {
            if (ShouldIgnoreArea(query, area) || SharesTrustedWardGroup(area, query))
            {
                continue;
            }

            var allowedRadius = DistanceXZ(query.X, query.Z, area.X, area.Z) - area.Radius;
            if (allowedRadius < maxRadius)
            {
                maxRadius = allowedRadius;
            }
        }

        return Clamp(maxRadius, 0f, fallbackRadius);
    }

    internal static bool Overlaps(WardOverlapQuery query, WardOverlapArea area)
    {
        return DistanceXZ(query.X, query.Z, area.X, area.Z) < area.Radius + query.Radius;
    }

    internal static bool SharesTrustedWardGroup(WardOverlapArea area, WardOverlapQuery query)
    {
        if (SharesDirectOwnerGroup(area.OwnerPlayerId, query.OwnerPlayerId))
        {
            return true;
        }

        return area.GuildId != 0 && query.GuildId != 0 && area.GuildId == query.GuildId;
    }

    private static bool ShouldIgnoreArea(WardOverlapQuery query, WardOverlapArea area)
    {
        return query.IgnoredAreaId != 0 && area.Id == query.IgnoredAreaId;
    }

    private static bool SharesDirectOwnerGroup(long leftCreatorPlayerId, long rightCreatorPlayerId)
    {
        return leftCreatorPlayerId != 0L &&
               rightCreatorPlayerId != 0L &&
               leftCreatorPlayerId == rightCreatorPlayerId;
    }

    private static float DistanceXZ(float leftX, float leftZ, float rightX, float rightZ)
    {
        var x = leftX - rightX;
        var z = leftZ - rightZ;
        return (float)Math.Sqrt(x * x + z * z);
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }
}
