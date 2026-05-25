using System;
using System.Reflection;
using BepInEx.Bootstrap;
using HarmonyLib;

namespace STUWard;

internal static partial class GuildsCompat
{
    private enum AvailabilityState
    {
        Unknown = 0,
        Available = 1,
        Unavailable = 2
    }

    private const string GuildsPluginGuid = "org.bepinex.plugins.guilds";
    private static readonly TimeSpan AvailabilityProbeBackoff = TimeSpan.FromSeconds(2);

    private static readonly Assembly? GuildsAssembly = GetPluginAssembly(GuildsPluginGuid);
    private static readonly Type? ApiType = GuildsAssembly?.GetType("Guilds.API");
    private static readonly Type? GuildType = GuildsAssembly?.GetType("Guilds.Guild");
    private static readonly Type? GuildGeneralType = GuildsAssembly?.GetType("Guilds.GuildGeneral");
    private static readonly Type? PlayerReferenceType = GuildsAssembly?.GetType("Guilds.PlayerReference");
    private static readonly Type? GuildJoinedDelegateType = ApiType?.GetNestedType("GuildJoined", BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Type? GuildLeftDelegateType = ApiType?.GetNestedType("GuildLeft", BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Type? GuildCreatedDelegateType = ApiType?.GetNestedType("GuildCreated", BindingFlags.Public | BindingFlags.NonPublic);
    private static readonly Type? GuildDeletedDelegateType = ApiType?.GetNestedType("GuildDeleted", BindingFlags.Public | BindingFlags.NonPublic);

    private static readonly MethodInfo? IsLoadedMethod = ApiType != null ? AccessTools.Method(ApiType, "IsLoaded") : null;
    private static readonly MethodInfo? GetPlayerGuildByPlayerMethod = ApiType != null
        ? AccessTools.Method(ApiType, "GetPlayerGuild", new[] { typeof(Player) })
        : null;
    private static readonly MethodInfo? GetPlayerGuildByReferenceMethod = ApiType != null && PlayerReferenceType != null
        ? AccessTools.Method(ApiType, "GetPlayerGuild", new[] { PlayerReferenceType })
        : null;
    private static readonly MethodInfo? GetGuildsMethod = ApiType != null
        ? AccessTools.Method(ApiType, "GetGuilds", Type.EmptyTypes)
        : null;
    private static readonly MethodInfo? GetGuildByIdMethod = ApiType != null
        ? AccessTools.Method(ApiType, "GetGuild", new[] { typeof(int) })
        : null;
    private static readonly MethodInfo? PlayerReferenceFromStringMethod = PlayerReferenceType != null
        ? AccessTools.Method(PlayerReferenceType, "fromString", new[] { typeof(string) })
        : null;
    private static readonly MethodInfo? RegisterOnGuildJoinedMethod = ApiType != null && GuildJoinedDelegateType != null
        ? AccessTools.Method(ApiType, "RegisterOnGuildJoined", new[] { GuildJoinedDelegateType })
        : null;
    private static readonly MethodInfo? RegisterOnGuildLeftMethod = ApiType != null && GuildLeftDelegateType != null
        ? AccessTools.Method(ApiType, "RegisterOnGuildLeft", new[] { GuildLeftDelegateType })
        : null;
    private static readonly MethodInfo? RegisterOnGuildCreatedMethod = ApiType != null && GuildCreatedDelegateType != null
        ? AccessTools.Method(ApiType, "RegisterOnGuildCreated", new[] { GuildCreatedDelegateType })
        : null;
    private static readonly MethodInfo? RegisterOnGuildDeletedMethod = ApiType != null && GuildDeletedDelegateType != null
        ? AccessTools.Method(ApiType, "RegisterOnGuildDeleted", new[] { GuildDeletedDelegateType })
        : null;
    private static readonly MethodInfo? SaveGuildMethod = ApiType != null && GuildType != null
        ? AccessTools.Method(ApiType, "SaveGuild", new[] { GuildType })
        : null;
    private static readonly FieldInfo? PlayerInfoUserInfoField = AccessTools.Field(typeof(ZNet.PlayerInfo), "m_userInfo");
    private static readonly FieldInfo? UserInfoIdField = PlayerInfoUserInfoField?.FieldType != null
        ? AccessTools.Field(PlayerInfoUserInfoField.FieldType, "m_id")
        : null;

    private static readonly FieldInfo? GuildNameField = GuildType != null ? AccessTools.Field(GuildType, "Name") : null;
    private static readonly FieldInfo? GuildGeneralField = GuildType != null ? AccessTools.Field(GuildType, "General") : null;
    private static readonly FieldInfo? GuildGeneralIdField = GuildGeneralType != null ? AccessTools.Field(GuildGeneralType, "id") : null;
    private static readonly FieldInfo? GuildMembersField = GuildType != null ? AccessTools.Field(GuildType, "Members") : null;
    private static readonly FieldInfo? PlayerReferenceIdField = PlayerReferenceType != null ? AccessTools.Field(PlayerReferenceType, "id") : null;
    private static readonly FieldInfo? PlayerReferenceNameField = PlayerReferenceType != null
        ? AccessTools.Field(PlayerReferenceType, "name") ?? AccessTools.Field(PlayerReferenceType, "Name")
        : null;
    private static readonly bool HasGuildsApiSurface = ApiType != null && IsLoadedMethod != null;
    private static AvailabilityState _availabilityState = AvailabilityState.Unknown;
    private static DateTime _nextAvailabilityProbeUtc = DateTime.MinValue;

    private static Assembly? GetPluginAssembly(string pluginGuid)
    {
        if (!Chainloader.PluginInfos.TryGetValue(pluginGuid, out var pluginInfo))
        {
            return null;
        }

        return pluginInfo.Instance?.GetType().Assembly;
    }

    internal static bool IsAvailable()
    {
        if (!HasGuildsApiSurface)
        {
            return false;
        }

        if (_availabilityState == AvailabilityState.Available)
        {
            return true;
        }

        var now = DateTime.UtcNow;
        if (_availabilityState == AvailabilityState.Unavailable && now < _nextAvailabilityProbeUtc)
        {
            return false;
        }

        try
        {
            var isAvailable = IsLoadedMethod!.Invoke(null, Array.Empty<object>()) as bool? ?? false;
            if (isAvailable)
            {
                _availabilityState = AvailabilityState.Available;
                _nextAvailabilityProbeUtc = DateTime.MaxValue;
                return true;
            }
        }
        catch
        {
        }

        _availabilityState = AvailabilityState.Unavailable;
        _nextAvailabilityProbeUtc = now + AvailabilityProbeBackoff;
        return false;
    }
}
