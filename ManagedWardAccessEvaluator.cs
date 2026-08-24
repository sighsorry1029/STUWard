namespace STUWard;

internal static class ManagedWardAccessEvaluator
{
    internal static bool HasPlayerAccess(PrivateArea area, ManagedWardAccessActor actor)
    {
        return ManagedWardAccessPolicy.CanAccess(
            actor,
            BuildManagedWardAccessSubjectFromArea(area, actor));
    }

    internal static bool HasPlayerAccess(ZDO zdo, ManagedWardAccessActor actor)
    {
        return ManagedWardAccessPolicy.CanAccess(
            actor,
            BuildManagedWardAccessSubjectFromZdo(zdo, actor));
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

        actor = CreateActor(localPlayer.GetPlayerID(), WardGroupCompat.GetPlayerGroupIdentity(localPlayer));
        return true;
    }

    internal static ManagedWardAccessActor CreateActor(long playerId)
    {
        return CreateActor(playerId, WardGroupCompat.GetPlayerGroupIdentity(playerId));
    }

    internal static ManagedWardAccessActor CreateActor(long playerId, WardGroupIdentity playerGroup)
    {
        return new ManagedWardAccessActor(
            playerId,
            playerGroup,
            WardAdminDebugAccess.IsPlayerAdminDebugController(playerId));
    }

    internal static bool HasPlayerAccessToManagedWardIndexEntry(
        WardMinimapVisibilityIndexedEntry entry,
        long playerId,
        WardGroupIdentity playerGroup)
    {
        if (playerId == 0L)
        {
            return false;
        }

        var actor = CreateActor(playerId, playerGroup);
        return ManagedWardAccessPolicy.CanAccess(
            actor,
            BuildManagedWardAccessSubjectFromIndexEntry(entry, actor));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromArea(
        PrivateArea area,
        ManagedWardAccessActor actor)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo != null && zdo.IsValid())
        {
            return BuildManagedWardAccessSubjectFromZdo(zdo, actor);
        }

        return BuildManagedWardAccessSubjectCore(
            WardAccess.GetCanonicalCreatorPlayerId(area),
            WardGroupCompat.GetWardGroupIdentity(area),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(area, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromZdo(
        ZDO zdo,
        ManagedWardAccessActor actor)
    {
        return BuildManagedWardAccessSubjectCore(
            zdo.GetLong(ZDOVars.s_creator, 0L),
            WardGroupCompat.ResolveWardGroupIdentityReadOnly(zdo),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(zdo, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromIndexEntry(
        WardMinimapVisibilityIndexedEntry entry,
        ManagedWardAccessActor actor)
    {
        return new ManagedWardAccessSubject(
            entry.OwnerPlayerId,
            entry.WardGroup,
            IsPlayerPermitted(entry, actor.PlayerId));
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectCore(
        long ownerPlayerId,
        WardGroupIdentity wardGroup,
        bool permitted)
    {
        return new ManagedWardAccessSubject(
            ownerPlayerId,
            wardGroup,
            permitted);
    }

    private static bool IsPlayerPermitted(WardMinimapVisibilityIndexedEntry entry, long playerId)
    {
        for (var index = 0; index < entry.PermittedPlayerIds.Count; index++)
        {
            if (entry.PermittedPlayerIds[index] == playerId)
            {
                return true;
            }
        }

        return false;
    }
}
