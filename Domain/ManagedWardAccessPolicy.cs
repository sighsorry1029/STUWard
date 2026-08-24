namespace STUWard;

internal readonly struct WardGuildIdentity
{
    internal WardGuildIdentity(int id, string name)
    {
        Id = id;
        Name = name ?? string.Empty;
    }

    internal int Id { get; }
    internal string Name { get; }
}

internal readonly struct ManagedWardAccessActor
{
    internal ManagedWardAccessActor(long playerId, WardGroupIdentity playerGroup, bool isAdminDebug)
    {
        PlayerId = playerId;
        PlayerGroup = playerGroup;
        IsAdminDebug = isAdminDebug;
    }

    internal long PlayerId { get; }
    internal WardGroupIdentity PlayerGroup { get; }
    internal bool IsAdminDebug { get; }
}

internal readonly struct ManagedWardAccessSubject
{
    internal ManagedWardAccessSubject(long ownerPlayerId, WardGroupIdentity wardGroup, bool permitted)
    {
        OwnerPlayerId = ownerPlayerId;
        WardGroup = wardGroup;
        Permitted = permitted;
    }

    internal long OwnerPlayerId { get; }
    internal WardGroupIdentity WardGroup { get; }
    internal bool Permitted { get; }
}

internal static class ManagedWardAccessPolicy
{
    internal static bool CanAccess(
        ManagedWardAccessActor actor,
        ManagedWardAccessSubject subject)
    {
        if (subject.OwnerPlayerId != 0L && subject.OwnerPlayerId == actor.PlayerId)
        {
            return true;
        }

        if (actor.IsAdminDebug)
        {
            return true;
        }

        return subject.Permitted || HasMatchingGroup(actor.PlayerGroup, subject.WardGroup);
    }

    internal static bool HasMatchingGroup(WardGroupIdentity playerGroup, WardGroupIdentity wardGroup)
    {
        return playerGroup.IsValid && wardGroup.IsValid && playerGroup == wardGroup;
    }
}
