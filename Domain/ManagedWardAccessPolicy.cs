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
    internal ManagedWardAccessActor(long playerId, WardGuildIdentity playerGuild, bool isAdminDebug)
    {
        PlayerId = playerId;
        PlayerGuild = playerGuild;
        IsAdminDebug = isAdminDebug;
    }

    internal long PlayerId { get; }
    internal WardGuildIdentity PlayerGuild { get; }
    internal bool IsAdminDebug { get; }
}

internal readonly struct ManagedWardAccessSubject
{
    internal ManagedWardAccessSubject(long ownerPlayerId, WardGuildIdentity wardGuild, bool permitted)
    {
        OwnerPlayerId = ownerPlayerId;
        WardGuild = wardGuild;
        Permitted = permitted;
    }

    internal long OwnerPlayerId { get; }
    internal WardGuildIdentity WardGuild { get; }
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

        return subject.Permitted || HasMatchingGuild(actor.PlayerGuild, subject.WardGuild);
    }

    internal static bool HasMatchingGuild(WardGuildIdentity playerGuild, WardGuildIdentity wardGuild)
    {
        return playerGuild.Id != 0 &&
               wardGuild.Id != 0 &&
               playerGuild.Id == wardGuild.Id;
    }
}
