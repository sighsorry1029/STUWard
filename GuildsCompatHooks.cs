using System;
using System.Linq.Expressions;
using System.Reflection;
using HarmonyLib;

namespace STUWard;

internal static partial class GuildsCompat
{
    private static bool _guildHooksRegistered;
    private static bool _guildHooksActive;
    private static bool _saveGuildPatched;

    internal static void TryPatch(Harmony harmony)
    {
        EnsureHooksRegistered();
        if (_saveGuildPatched || harmony == null || SaveGuildMethod == null)
        {
            return;
        }

        var postfix = AccessTools.DeclaredMethod(typeof(GuildsSaveGuildPatch), nameof(GuildsSaveGuildPatch.Postfix));
        if (postfix == null)
        {
            return;
        }

        harmony.Patch(SaveGuildMethod, postfix: new HarmonyMethod(postfix));
        _saveGuildPatched = true;
    }

    private static void EnsureHooksRegistered()
    {
        if (_guildHooksRegistered)
        {
            _guildHooksActive = true;
            return;
        }

        if (ApiType == null)
        {
            return;
        }

        RegisterGuildHook(RegisterOnGuildJoinedMethod, GuildJoinedDelegateType, nameof(HandleGuildJoinedEvent));
        RegisterGuildHook(RegisterOnGuildLeftMethod, GuildLeftDelegateType, nameof(HandleGuildLeftEvent));
        RegisterGuildHook(RegisterOnGuildCreatedMethod, GuildCreatedDelegateType, nameof(HandleGuildCreatedEvent));
        RegisterGuildHook(RegisterOnGuildDeletedMethod, GuildDeletedDelegateType, nameof(HandleGuildDeletedEvent));
        _guildHooksRegistered = true;
        _guildHooksActive = true;
    }

    internal static void TryShutdownHooks()
    {
        _guildHooksActive = false;
        _saveGuildPatched = false;
    }

    internal static bool IsGuildHooksActive()
    {
        return _guildHooksActive;
    }

    private static void RegisterGuildHook(MethodInfo? registerMethod, Type? delegateType, string handlerName)
    {
        if (registerMethod == null || delegateType == null)
        {
            return;
        }

        var callback = CreateGuildCallback(delegateType, handlerName);
        if (callback == null)
        {
            return;
        }

        registerMethod.Invoke(null, new object[] { callback });
    }

    private static Delegate? CreateGuildCallback(Type delegateType, string handlerName)
    {
        var handler = AccessTools.DeclaredMethod(typeof(GuildsCompat), handlerName);
        var invoke = delegateType.GetMethod("Invoke");
        if (handler == null || invoke == null)
        {
            return null;
        }

        var delegateParameters = invoke.GetParameters();
        var lambdaParameters = new ParameterExpression[delegateParameters.Length];
        var callArguments = new Expression[delegateParameters.Length];
        for (var index = 0; index < delegateParameters.Length; index++)
        {
            var parameter = Expression.Parameter(delegateParameters[index].ParameterType, delegateParameters[index].Name);
            lambdaParameters[index] = parameter;
            callArguments[index] = Expression.Convert(parameter, typeof(object));
        }

        var body = Expression.Call(handler, callArguments);
        return Expression.Lambda(delegateType, body, lambdaParameters).Compile();
    }

    private static void HandleGuildJoinedEvent(object guild, object playerReference)
    {
        if (!_guildHooksActive)
        {
            return;
        }

        if (!TryCreateCharacterIdentityFromPlayerReference(playerReference, out var identity))
        {
            RefreshAllWardGuildProjections(liveDisplayRefresh: true);
            return;
        }

        RefreshWardGuildProjectionForCharacter(
            identity,
            liveDisplayRefresh: true,
            affectedGuildId: TryParseGuild(guild, out var joinedGuild) ? joinedGuild.Id : 0);
    }

    private static void HandleGuildLeftEvent(object guild, object playerReference)
    {
        if (!_guildHooksActive)
        {
            return;
        }

        if (!TryCreateCharacterIdentityFromPlayerReference(playerReference, out var identity))
        {
            RefreshAllWardGuildProjections(liveDisplayRefresh: true);
            return;
        }

        RefreshWardGuildProjectionForCharacter(
            identity,
            liveDisplayRefresh: true,
            affectedGuildId: TryParseGuild(guild, out var leftGuild) ? leftGuild.Id : 0);
    }

    private static void HandleGuildCreatedEvent(object guild)
    {
        if (!_guildHooksActive)
        {
            return;
        }

        RefreshAllWardGuildProjections(liveDisplayRefresh: true);
    }

    private static void HandleGuildDeletedEvent(object guild)
    {
        if (!_guildHooksActive)
        {
            return;
        }

        RefreshAllWardGuildProjections(liveDisplayRefresh: true);
    }
}

internal static class GuildsSaveGuildPatch
{
    internal static void Postfix(object[] __args)
    {
        if (!GuildsCompat.IsGuildHooksActive())
        {
            return;
        }

        if (__args.Length == 0)
        {
            return;
        }

        GuildsCompat.HandleGuildSaved(__args[0]);
    }
}
