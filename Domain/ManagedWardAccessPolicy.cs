namespace STUWard;

internal readonly struct WardGuildIdentity
{
    internal WardGuildIdentity(int id, string name)
    {
        Id = id;
        Name = name;
    }

    internal int Id { get; }
    internal string Name { get; }
}

internal readonly struct ManagedWardAccessEvaluation
{
    internal ManagedWardAccessEvaluation(bool allowed, string reason, bool permitted, bool sameGuild)
    {
        Allowed = allowed;
        Reason = reason;
        Permitted = permitted;
        SameGuild = sameGuild;
    }

    internal bool Allowed { get; }
    internal string Reason { get; }
    internal bool Permitted { get; }
    internal bool SameGuild { get; }
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
    internal ManagedWardAccessSubject(long ownerPlayerId, WardGuildIdentity wardGuild, bool permitted, string wardSteamAccountId, string wardZdoLabel)
    {
        OwnerPlayerId = ownerPlayerId;
        WardGuild = wardGuild;
        Permitted = permitted;
        WardSteamAccountId = wardSteamAccountId ?? string.Empty;
        WardZdoLabel = wardZdoLabel ?? "none";
    }

    internal long OwnerPlayerId { get; }
    internal WardGuildIdentity WardGuild { get; }
    internal bool Permitted { get; }
    internal string WardSteamAccountId { get; }
    internal string WardZdoLabel { get; }
}

internal static class ManagedWardAccessPolicy
{
    internal static ManagedWardAccessEvaluation Evaluate(
        ManagedWardAccessActor actor,
        ManagedWardAccessSubject subject)
    {
        if (subject.OwnerPlayerId != 0L && subject.OwnerPlayerId == actor.PlayerId)
        {
            return new ManagedWardAccessEvaluation(allowed: true, reason: "owner", permitted: subject.Permitted, sameGuild: false);
        }

        if (actor.IsAdminDebug)
        {
            return new ManagedWardAccessEvaluation(allowed: true, reason: "admin_debug", permitted: subject.Permitted, sameGuild: false);
        }

        var sameGuild = HasMatchingGuild(actor.PlayerGuild, subject.WardGuild);
        if (sameGuild)
        {
            return new ManagedWardAccessEvaluation(allowed: true, reason: "guild", permitted: subject.Permitted, sameGuild: true);
        }

        if (subject.Permitted)
        {
            return new ManagedWardAccessEvaluation(allowed: true, reason: "permitted", permitted: true, sameGuild: false);
        }

        return new ManagedWardAccessEvaluation(allowed: false, reason: "denied", permitted: subject.Permitted, sameGuild: false);
    }

    internal static bool HasMatchingGuild(WardGuildIdentity playerGuild, WardGuildIdentity wardGuild)
    {
        return playerGuild.Id != 0 &&
               wardGuild.Id != 0 &&
               playerGuild.Id == wardGuild.Id;
    }
}
