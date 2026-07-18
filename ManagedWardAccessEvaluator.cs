namespace STUWard;

internal static class ManagedWardAccessEvaluator
{
    internal static bool HasPlayerAccess(PrivateArea area, ManagedWardAccessActor actor)
    {
        return ManagedWardAccessPolicy.Evaluate(
            actor,
            BuildManagedWardAccessSubjectFromArea(area, actor)).Allowed;
    }

    internal static bool TryCreateActorForAccessCheck(long playerId, out ManagedWardAccessActor actor)
    {
        if (playerId != 0L)
        {
            actor = CreateActor(playerId);
            return true;
        }

        var localPlayer = Player.m_localPlayer;
        if (localPlayer == null)
        {
            actor = default;
            return false;
        }

        actor = CreateActor(localPlayer.GetPlayerID(), GuildsCompat.GetPlayerGuildIdentity(localPlayer));
        return true;
    }

    internal static ManagedWardAccessActor CreateActor(long playerId)
    {
        return CreateActor(playerId, GuildsCompat.GetPlayerGuildIdentity(playerId));
    }

    internal static ManagedWardAccessActor CreateActor(long playerId, WardGuildIdentity playerGuild)
    {
        return new ManagedWardAccessActor(
            playerId,
            playerGuild,
            WardAdminDebugAccess.IsPlayerAdminDebugController(playerId));
    }

    internal static bool HasPlayerAccessToManagedWardZdo(ZDO? zdo, long playerId)
    {
        return HasPlayerAccessToManagedWardZdo(zdo, playerId, GuildsCompat.GetPlayerGuildIdentity(playerId));
    }

    internal static bool HasPlayerAccessToManagedWardZdo(ZDO? zdo, long playerId, WardGuildIdentity playerGuild)
    {
        if (zdo == null || !zdo.IsValid() || playerId == 0L)
        {
            return false;
        }

        var actor = CreateActor(playerId, playerGuild);
        return ManagedWardAccessPolicy.Evaluate(
            actor,
            BuildManagedWardAccessSubjectFromZdo(zdo, actor)).Allowed;
    }

    internal static bool HasPlayerAccessToManagedWardIndexEntry(
        WardMinimapVisibilityIndexedEntry entry,
        long playerId,
        WardGuildIdentity playerGuild)
    {
        if (playerId == 0L)
        {
            return false;
        }

        var actor = CreateActor(playerId, playerGuild);
        return ManagedWardAccessPolicy.Evaluate(
            actor,
            BuildManagedWardAccessSubjectFromIndexEntry(entry, actor)).Allowed;
    }

    internal static bool HasMatchingGuild(WardGuildIdentity playerGuild, WardGuildIdentity wardGuild)
    {
        return ManagedWardAccessPolicy.HasMatchingGuild(playerGuild, wardGuild);
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromArea(
        PrivateArea area,
        ManagedWardAccessActor actor)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        return BuildManagedWardAccessSubjectCore(
            WardAccess.GetCanonicalCreatorPlayerId(area),
            GuildsCompat.GetWardGuildId(area),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(area, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromZdo(
        ZDO zdo,
        ManagedWardAccessActor actor)
    {
        return BuildManagedWardAccessSubjectCore(
            zdo.GetLong(ZDOVars.s_creator, 0L),
            GuildsCompat.GetWardGuildId(zdo),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(zdo, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromIndexEntry(
        WardMinimapVisibilityIndexedEntry entry,
        ManagedWardAccessActor actor)
    {
        return new ManagedWardAccessSubject(
            entry.OwnerPlayerId,
            new WardGuildIdentity(entry.WardGuildId, string.Empty),
            IsPlayerPermitted(entry, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectCore(
        long ownerPlayerId,
        int wardGuildId,
        bool permitted)
    {
        return new ManagedWardAccessSubject(
            ownerPlayerId,
            new WardGuildIdentity(wardGuildId, string.Empty),
            permitted);
    }

    private static bool IsPlayerPermitted(WardMinimapVisibilityIndexedEntry entry, long playerId)
    {
        for (var index = 0; index < entry.PermittedPlayerIds.Length; index++)
        {
            if (entry.PermittedPlayerIds[index] == playerId)
            {
                return true;
            }
        }

        return false;
    }
}
