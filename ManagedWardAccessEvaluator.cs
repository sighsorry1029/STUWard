using System;

namespace STUWard;

internal static class ManagedWardAccessEvaluator
{
    internal static bool HasPlayerAccess(PrivateArea area, ManagedWardAccessActor actor, bool includeDiagnosticData, bool logDiagnostic = true)
    {
        var subject = BuildManagedWardAccessSubjectFromArea(area, actor, includeDiagnosticData);
        var evaluation = ManagedWardAccessPolicy.Evaluate(actor, subject);
        if (logDiagnostic)
        {
            LogResolutionVerbose(actor, subject, evaluation);
        }

        return evaluation.Allowed;
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
        var subject = BuildManagedWardAccessSubjectFromZdo(
            zdo,
            actor,
            includeDiagnosticData: Plugin.ShouldLogWardDiagnosticVerbose());
        var evaluation = ManagedWardAccessPolicy.Evaluate(actor, subject);
        LogResolutionVerbose(actor, subject, evaluation);
        return evaluation.Allowed;
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
        var subject = BuildManagedWardAccessSubjectFromIndexEntry(
            entry,
            actor,
            includeDiagnosticData: Plugin.ShouldLogWardDiagnosticVerbose());
        var evaluation = ManagedWardAccessPolicy.Evaluate(actor, subject);
        LogResolutionVerbose(actor, subject, evaluation);
        return evaluation.Allowed;
    }

    internal static bool HasMatchingGuild(WardGuildIdentity playerGuild, WardGuildIdentity wardGuild)
    {
        return ManagedWardAccessPolicy.HasMatchingGuild(playerGuild, wardGuild);
    }

    private static void LogResolutionVerbose(
        ManagedWardAccessActor actor,
        ManagedWardAccessSubject subject,
        ManagedWardAccessEvaluation evaluation)
    {
        if (!Plugin.ShouldLogWardDiagnosticVerbose())
        {
            return;
        }

        var playerName = WardOwnership.GetPlayerName(actor.PlayerId);
        var accountId = WardOwnership.GetPlayerAccountId(actor.PlayerId);
        Plugin.LogWardDiagnosticVerbose(
            "Access.Resolve",
            $"Resolved managed ward access. allowed={evaluation.Allowed}, reason={evaluation.Reason}, playerId={actor.PlayerId}, playerName='{playerName}', accountId='{accountId}', playerGuildId={actor.PlayerGuild.Id}, playerGuildName='{actor.PlayerGuild.Name ?? string.Empty}', permitted={evaluation.Permitted}, sameGuild={evaluation.SameGuild}, wardGuildId={subject.WardGuild.Id}, wardGuildName='{subject.WardGuild.Name ?? string.Empty}', wardOwnerPlayerId={subject.OwnerPlayerId}, wardSteamAccountId='{subject.WardSteamAccountId}', wardZdo={subject.WardZdoLabel}");
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromArea(
        PrivateArea area,
        ManagedWardAccessActor actor,
        bool includeDiagnosticData)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        return BuildManagedWardAccessSubjectCore(
            zdo,
            WardAccess.GetCanonicalCreatorPlayerId(area),
            GuildsCompat.GetWardGuildId(area),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(area, actor.PlayerId),
            includeDiagnosticData);
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromZdo(
        ZDO zdo,
        ManagedWardAccessActor actor,
        bool includeDiagnosticData)
    {
        return BuildManagedWardAccessSubjectCore(
            zdo,
            zdo.GetLong(ZDOVars.s_creator, 0L),
            GuildsCompat.GetWardGuildId(zdo),
            WardPrivateAreaSafeAccess.IsPlayerPermitted(zdo, actor.PlayerId),
            includeDiagnosticData);
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectFromIndexEntry(
        WardMinimapVisibilityIndexedEntry entry,
        ManagedWardAccessActor actor,
        bool includeDiagnosticData)
    {
        return new ManagedWardAccessSubject(
            entry.OwnerPlayerId,
            new WardGuildIdentity(entry.WardGuildId, string.Empty),
            IsPlayerPermitted(entry, actor.PlayerId),
            string.Empty,
            includeDiagnosticData ? entry.ZdoId.ToString() : string.Empty);
    }

    private static ManagedWardAccessSubject BuildManagedWardAccessSubjectCore(
        ZDO? zdo,
        long ownerPlayerId,
        int wardGuildId,
        bool permitted,
        bool includeDiagnosticData)
    {
        return new ManagedWardAccessSubject(
            ownerPlayerId,
            new WardGuildIdentity(
                wardGuildId,
                includeDiagnosticData && wardGuildId != 0
                    ? GuildsCompat.GetWardGuildName(zdo)
                    : string.Empty),
            permitted,
            includeDiagnosticData
                ? WardOwnership.GetWardSteamAccountId(zdo)
                : string.Empty,
            includeDiagnosticData
                ? zdo?.m_uid.ToString() ?? "none"
                : string.Empty);
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
