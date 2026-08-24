using System;
using System.Globalization;
using System.Reflection;
using BepInEx.Bootstrap;

namespace STUWard;

internal static class WardGroupCompat
{
    internal const string GroupProviderKey = "stuw_group_provider";
    internal const string GroupIdKey = "stuw_group_id";
    internal const string GroupNameKey = "stuw_group_name";

    internal const string GuildsProvider = "guilds";
    internal const string ClanProvider = "clan";

    private static bool _providerConflictWarningLogged;

    private enum ActiveProvider
    {
        None,
        Guilds,
        Clan,
        Conflict
    }

    internal static void ResetRuntimeState()
    {
        _providerConflictWarningLogged = false;
        ClanCompat.ResetRuntimeState();
    }

    internal static void EnsureRuntimeBindings()
    {
        if (GetActiveProvider() == ActiveProvider.Clan)
        {
            ClanCompat.EnsureRuntimeBindings();
        }

        LogProviderConflictIfNeeded();
    }

    internal static void Update()
    {
        if (GetActiveProvider() == ActiveProvider.Clan)
        {
            ClanCompat.Update();
        }

        LogProviderConflictIfNeeded();
    }

    internal static void Shutdown()
    {
        ClanCompat.Shutdown();
    }

    internal static void OnLocalPlayerStarted(Player player)
    {
        GuildsCompat.OnLocalPlayerStarted(player);
        if (GetActiveProvider() == ActiveProvider.Clan)
        {
            ClanCompat.InvalidateLocalSnapshot();
        }
    }

    internal static WardGroupIdentity GetPlayerGroupIdentity(Player? player)
    {
        if (player == null)
        {
            return default;
        }

        switch (GetActiveProvider())
        {
            case ActiveProvider.Guilds:
                return FromGuild(GuildsCompat.GetPlayerGuildIdentity(player));
            case ActiveProvider.Clan:
                return TryResolveClan(player.GetPlayerID(), WardOwnership.GetPlayerAccountId(player), out var clan)
                    ? clan
                    : default;
            default:
                return default;
        }
    }

    internal static WardGroupIdentity GetPlayerGroupIdentity(long playerId)
    {
        switch (GetActiveProvider())
        {
            case ActiveProvider.Guilds:
                return FromGuild(GuildsCompat.GetPlayerGuildIdentity(playerId));
            case ActiveProvider.Clan:
                return TryResolveClan(playerId, WardOwnership.GetPlayerAccountId(playerId), out var clan)
                    ? clan
                    : default;
            default:
                return default;
        }
    }

    internal static string GetPlayerGroupName(long playerId)
    {
        return GetPlayerGroupIdentity(playerId).Name;
    }

    internal static WardGroupIdentity GetWardGroupIdentity(PrivateArea? area)
    {
        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        var stored = ResolveWardGroupIdentityReadOnly(zdo);
        if (stored.IsValid ||
            GetActiveProvider() == ActiveProvider.Clan &&
            ZNet.instance != null &&
            ZNet.instance.IsServer())
        {
            return stored;
        }

        if (area == null)
        {
            return default;
        }

        var ownerPlayerId = WardAccess.GetCanonicalCreatorPlayerId(area);
        var accountId = WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId);
        return TryResolveProjectedGroupIdentity(
            ownerPlayerId,
            accountId,
            GuildsCompat.GetWardOwnerNameForProjection(zdo),
            out var group)
            ? group
            : default;
    }

    internal static string GetWardGroupName(PrivateArea? area)
    {
        return GetWardGroupIdentity(area).Name;
    }

    internal static string GetGroupLabelFallback()
    {
        return GetActiveProvider() == ActiveProvider.Clan ? "Clan: {0}" : "Guild: {0}";
    }

    internal static string GetGroupLabelToken()
    {
        return GetActiveProvider() == ActiveProvider.Clan
            ? WardLocalization.UiClanToken
            : WardLocalization.UiGuildToken;
    }

    internal static WardGroupIdentity ResolveWardGroupIdentityReadOnly(ZDO? zdo)
    {
        if (zdo == null)
        {
            return default;
        }

        var activeProvider = GetActiveProvider();
        if (activeProvider is ActiveProvider.None or ActiveProvider.Conflict)
        {
            return default;
        }

        if (activeProvider == ActiveProvider.Clan && !ClanCompat.IsUsable)
        {
            return default;
        }

        if (activeProvider == ActiveProvider.Clan && ZNet.instance != null && ZNet.instance.IsServer())
        {
            var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
            var ownerAccountId = WardOwnership.ResolveWardSteamAccountId(zdo, ownerPlayerId);
            return TryResolveClan(ownerPlayerId, ownerAccountId, out var authoritativeClan)
                ? authoritativeClan
                : default;
        }

        var storedProvider = (zdo.GetString(GroupProviderKey, string.Empty) ?? string.Empty).Trim();
        var storedId = (zdo.GetString(GroupIdKey, string.Empty) ?? string.Empty).Trim();
        var expectedProvider = activeProvider == ActiveProvider.Clan ? ClanProvider : GuildsProvider;
        if (string.Equals(storedProvider, expectedProvider, StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(storedId))
        {
            return new WardGroupIdentity(
                storedProvider,
                storedId,
                zdo.GetString(GroupNameKey, string.Empty) ?? string.Empty);
        }

        if (activeProvider != ActiveProvider.Guilds)
        {
            return default;
        }

        var legacyGuild = GuildsCompat.ResolveWardGuildIdentityReadOnly(zdo);
        return FromGuild(legacyGuild);
    }

    internal static bool TryResolveProjectedGroupIdentity(
        long playerId,
        string accountId,
        string playerName,
        out WardGroupIdentity group)
    {
        group = default;
        switch (GetActiveProvider())
        {
            case ActiveProvider.Guilds:
                if (!GuildsCompat.TryResolveProjectedGuildIdentity(playerId, accountId, playerName, out var guild))
                {
                    return false;
                }

                group = FromGuild(guild);
                return true;
            case ActiveProvider.Clan:
                return TryResolveClan(playerId, accountId, out group);
            case ActiveProvider.None:
            case ActiveProvider.Conflict:
                // Missing or conflicting providers are unavailable, not an
                // authoritative no-group result. Existing
                // projections are retained, while access reads still fail closed.
                return false;
            default:
                return false;
        }
    }

    internal static bool TryResolveCachedAuthoritativeGroupIdentity(
        long playerId,
        string accountId,
        string playerName,
        out WardGroupIdentity group)
    {
        group = default;
        switch (GetActiveProvider())
        {
            case ActiveProvider.Guilds:
                if (!GuildsCompat.TryResolveCachedAuthoritativeGuildIdentity(
                        playerId,
                        accountId,
                        playerName,
                        out var guild))
                {
                    return false;
                }

                group = FromGuild(guild);
                return true;
            case ActiveProvider.Clan:
                return TryResolveClan(playerId, accountId, out group);
            case ActiveProvider.None:
                return true;
            case ActiveProvider.Conflict:
                return false;
            default:
                return false;
        }
    }

    internal static bool ApplyProjectedGroupMetadata(ZDO zdo, WardGroupIdentity group)
    {
        var normalizedGroup = group.IsValid ? group : default;
        var changed = SetString(zdo, GroupProviderKey, normalizedGroup.Provider);
        changed |= SetString(zdo, GroupIdKey, normalizedGroup.Id);
        changed |= SetString(zdo, GroupNameKey, normalizedGroup.Name);

        var legacyGuildId = 0;
        if (string.Equals(normalizedGroup.Provider, GuildsProvider, StringComparison.Ordinal))
        {
            _ = int.TryParse(normalizedGroup.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out legacyGuildId);
        }

        if (zdo.GetInt(GuildsCompat.GuildIdKey, 0) != legacyGuildId)
        {
            zdo.Set(GuildsCompat.GuildIdKey, legacyGuildId);
            changed = true;
        }

        var legacyGuildName = legacyGuildId != 0 ? normalizedGroup.Name : string.Empty;
        changed |= SetString(zdo, GuildsCompat.GuildNameKey, legacyGuildName);
        return changed;
    }

    private static bool TryResolveClan(long playerId, string accountId, out WardGroupIdentity group)
    {
        group = default;
        if (!ClanCompat.TryResolveWardAuthorization(accountId, playerId, out var clanId, out var clanName))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(clanId))
        {
            group = new WardGroupIdentity(ClanProvider, clanId, clanName);
        }

        return true;
    }

    private static WardGroupIdentity FromGuild(WardGuildIdentity guild)
    {
        return guild.Id == 0
            ? default
            : new WardGroupIdentity(
                GuildsProvider,
                guild.Id.ToString(CultureInfo.InvariantCulture),
                guild.Name);
    }

    private static ActiveProvider GetActiveProvider()
    {
        var guildsInstalled = GuildsCompat.IsInstalled();
        var clanInstalled = ClanCompat.IsInstalled;
        if (guildsInstalled && clanInstalled)
        {
            return ActiveProvider.Conflict;
        }

        if (guildsInstalled)
        {
            return ActiveProvider.Guilds;
        }

        return clanInstalled ? ActiveProvider.Clan : ActiveProvider.None;
    }

    private static void LogProviderConflictIfNeeded()
    {
        if (_providerConflictWarningLogged || GetActiveProvider() != ActiveProvider.Conflict || Plugin.Log == null)
        {
            return;
        }

        _providerConflictWarningLogged = true;
        Plugin.Log.LogWarning(
            "Guilds and Clan are both installed. STUWard group authorization is disabled until only one provider remains.");
    }

    private static bool SetString(ZDO zdo, string key, string value)
    {
        value ??= string.Empty;
        if (string.Equals(zdo.GetString(key, string.Empty), value, StringComparison.Ordinal))
        {
            return false;
        }

        zdo.Set(key, value);
        return true;
    }

    private static class ClanCompat
    {
        private const string PluginGuid = "sighsorry.valheim.Clan";
        private const int MinimumApiVersion = 4;
        private static readonly TimeSpan UpdateProbeInterval = TimeSpan.FromMilliseconds(500);

        private static readonly Assembly? ClanAssembly = GetPluginAssembly();
        private static readonly Type? ApiType = ClanAssembly?.GetType("Clan.ClanApi");
        private static readonly MemberInfo? ApiVersionMember = GetStaticMember(ApiType, "ApiVersion");
        private static readonly MemberInfo? RegistryRevisionMember = GetStaticMember(ApiType, "RegistryRevision");
        private static readonly MemberInfo? CurrentMember = GetStaticMember(ApiType, "Current");
        private static readonly MethodInfo? ResolveWardAuthorizationMethod = FindResolveMethod();
        private static readonly EventInfo? WardAuthorizationChangedEvent =
            ApiType?.GetEvent("WardAuthorizationChanged", BindingFlags.Public | BindingFlags.Static);
        private static readonly EventInfo? StateChangedEvent =
            ApiType?.GetEvent("StateChanged", BindingFlags.Public | BindingFlags.Static);

        private static readonly Action WardAuthorizationChangedHandler = HandleWardAuthorizationChanged;
        private static Delegate? _stateChangedHandler;
        private static bool _eventBound;
        private static bool _stateChangedEventBound;
        private static bool _apiCompatibilityWarningLogged;
        private static bool _hasObservedRevision;
        private static long _lastObservedRevision;
        private static bool _hasObservedLocalGroup;
        private static WardGroupIdentity _lastObservedLocalGroup;
        private static DateTime _nextUpdateProbeUtc = DateTime.MinValue;

        internal static bool IsInstalled => ClanAssembly != null;
        internal static bool IsUsable => HasUsableApi;

        internal static void ResetRuntimeState()
        {
            _hasObservedRevision = false;
            _lastObservedRevision = 0L;
            _hasObservedLocalGroup = false;
            _lastObservedLocalGroup = default;
            _nextUpdateProbeUtc = DateTime.MinValue;
            _apiCompatibilityWarningLogged = false;
        }

        internal static void EnsureRuntimeBindings()
        {
            if (IsInstalled && !HasUsableApi && !_apiCompatibilityWarningLogged)
            {
                _apiCompatibilityWarningLogged = true;
                Plugin.Log?.LogWarning(
                    "Clan is installed without the required public API v4. STUWard Clan authorization is disabled.");
            }

            if (!_eventBound && HasUsableApi && WardAuthorizationChangedEvent != null)
            {
                try
                {
                    WardAuthorizationChangedEvent.AddEventHandler(null, WardAuthorizationChangedHandler);
                    _eventBound = true;
                }
                catch (Exception exception)
                {
                    Plugin.Log?.LogWarning(
                        $"Failed to subscribe to Clan ward authorization changes: {exception.GetType().Name}: {exception.Message}");
                }
            }

            TryBindStateChangedEvent();
        }

        internal static void Update()
        {
            if (!IsInstalled)
            {
                return;
            }

            var now = DateTime.UtcNow;
            if (now < _nextUpdateProbeUtc)
            {
                return;
            }

            _nextUpdateProbeUtc = now + UpdateProbeInterval;
            if (!HasUsableApi || !TryGetRegistryRevision(out var revision))
            {
                ObserveLocalSnapshot();
                return;
            }

            if (!_hasObservedRevision)
            {
                _hasObservedRevision = true;
                _lastObservedRevision = revision;
                ObserveLocalSnapshot();
                return;
            }

            if (_lastObservedRevision == revision)
            {
                ObserveLocalSnapshot();
                return;
            }

            _lastObservedRevision = revision;
            QueueProjectionRefresh();
            ObserveLocalSnapshot();
        }

        internal static void Shutdown()
        {
            if (_eventBound && WardAuthorizationChangedEvent != null)
            {
                try
                {
                    WardAuthorizationChangedEvent.RemoveEventHandler(null, WardAuthorizationChangedHandler);
                }
                catch
                {
                }
            }

            _eventBound = false;

            if (_stateChangedEventBound && StateChangedEvent != null && _stateChangedHandler != null)
            {
                try
                {
                    StateChangedEvent.RemoveEventHandler(null, _stateChangedHandler);
                }
                catch
                {
                }
            }

            _stateChangedEventBound = false;
            _stateChangedHandler = null;
        }

        internal static void InvalidateLocalSnapshot()
        {
            _hasObservedLocalGroup = false;
            _lastObservedLocalGroup = default;
            NotifyLocalGroupChanged();
        }

        internal static bool TryResolveWardAuthorization(
            string accountId,
            long playerId,
            out string clanId,
            out string clanName)
        {
            clanId = string.Empty;
            clanName = string.Empty;
            if (!HasUsableApi)
            {
                return false;
            }

            if (IsLocalClientPlayer(playerId))
            {
                return TryResolveLocalWardAuthorization(out clanId, out clanName);
            }

            if (ResolveWardAuthorizationMethod == null)
            {
                return false;
            }

            var platformId = GuildIdentityPolicy.GetGuildsAccountId(accountId);
            if (string.IsNullOrWhiteSpace(platformId) || playerId == 0L)
            {
                return false;
            }

            var arguments = new object?[] { platformId, playerId, null, null };
            try
            {
                var resolution = ResolveWardAuthorizationMethod.Invoke(null, arguments);
                var resolutionName = resolution?.ToString() ?? string.Empty;
                if (string.Equals(resolutionName, "Unavailable", StringComparison.Ordinal))
                {
                    return false;
                }

                if (string.Equals(resolutionName, "ResolvedNoAuthorization", StringComparison.Ordinal))
                {
                    return true;
                }

                if (!string.Equals(resolutionName, "Authorized", StringComparison.Ordinal))
                {
                    return false;
                }

                clanId = (arguments[2] as string ?? string.Empty).Trim();
                clanName = (arguments[3] as string ?? string.Empty).Trim();
                return !string.IsNullOrWhiteSpace(clanId);
            }
            catch
            {
                clanId = string.Empty;
                clanName = string.Empty;
                return false;
            }
        }

        private static bool HasUsableApi =>
            IsInstalled &&
            ResolveWardAuthorizationMethod != null &&
            TryReadInt(ApiVersionMember, out var apiVersion) &&
            apiVersion >= MinimumApiVersion;

        private static void HandleWardAuthorizationChanged()
        {
            if (TryGetRegistryRevision(out var revision))
            {
                _hasObservedRevision = true;
                _lastObservedRevision = revision;
            }

            QueueProjectionRefresh();
        }

        private static void HandleLocalStateChanged(object? _)
        {
            ObserveLocalSnapshot();
        }

        private static void QueueProjectionRefresh()
        {
            GuildsCompat.RefreshAllWardGuildProjections(liveDisplayRefresh: true);
            ManagedWardPresenceService.Invalidate();
            WardMinimapVisibilityIndex.InvalidateAll();
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(refreshImmediatelyIfVisible: true);
        }

        private static void ObserveLocalSnapshot()
        {
            var localPlayer = Player.m_localPlayer;
            if (localPlayer == null || (ZNet.instance != null && ZNet.instance.IsServer()))
            {
                return;
            }

            var resolved = TryResolveLocalWardAuthorization(out var clanId, out var clanName);
            var current = resolved && !string.IsNullOrWhiteSpace(clanId)
                ? new WardGroupIdentity(ClanProvider, clanId, clanName)
                : default;
            var sameDisplayIdentity = _hasObservedLocalGroup &&
                                      _lastObservedLocalGroup == current &&
                                      string.Equals(
                                          _lastObservedLocalGroup.Name,
                                          current.Name,
                                          StringComparison.Ordinal);
            if (sameDisplayIdentity)
            {
                return;
            }

            _hasObservedLocalGroup = true;
            _lastObservedLocalGroup = current;
            NotifyLocalGroupChanged();
        }

        private static void NotifyLocalGroupChanged()
        {
            ManagedWardPlacementPreviewService.Invalidate();
            ManagedWardPresenceService.Invalidate();
            WardMinimapVisibilityIndex.InvalidateAll();
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(refreshImmediatelyIfVisible: true);
            WardGuiController.Instance?.RefreshGroupMetadata();
        }

        private static bool IsLocalClientPlayer(long playerId)
        {
            var localPlayer = Player.m_localPlayer;
            return playerId != 0L &&
                   localPlayer != null &&
                   localPlayer.GetPlayerID() == playerId &&
                   (ZNet.instance == null || !ZNet.instance.IsServer());
        }

        private static bool TryResolveLocalWardAuthorization(out string clanId, out string clanName)
        {
            clanId = string.Empty;
            clanName = string.Empty;
            if (!TryReadValue(CurrentMember, out var snapshot) || snapshot == null)
            {
                return false;
            }

            var snapshotType = snapshot.GetType();
            if (!TryReadInstanceProperty(snapshot, snapshotType, "IsReady", out var readyValue) ||
                readyValue is not bool isReady ||
                !isReady)
            {
                return false;
            }

            if (!TryReadInstanceProperty(snapshot, snapshotType, "PrimaryClanId", out var clanIdValue))
            {
                return false;
            }

            clanId = (clanIdValue as string ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(clanId))
            {
                clanId = string.Empty;
                return true;
            }

            if (!TryReadInstanceProperty(snapshot, snapshotType, "PrimaryRole", out var roleValue))
            {
                clanId = string.Empty;
                return false;
            }

            var roleName = roleValue?.ToString() ?? string.Empty;
            if (!string.Equals(roleName, "Leader", StringComparison.Ordinal) &&
                !string.Equals(roleName, "Officer", StringComparison.Ordinal) &&
                !string.Equals(roleName, "Member", StringComparison.Ordinal))
            {
                clanId = string.Empty;
                return true;
            }

            _ = TryReadInstanceProperty(snapshot, snapshotType, "PrimaryClanName", out var clanNameValue);
            clanName = (clanNameValue as string ?? string.Empty).Trim();
            return true;
        }

        private static void TryBindStateChangedEvent()
        {
            if (_stateChangedEventBound || !HasUsableApi || StateChangedEvent?.EventHandlerType == null)
            {
                return;
            }

            try
            {
                var handlerMethod = typeof(ClanCompat).GetMethod(
                    nameof(HandleLocalStateChanged),
                    BindingFlags.NonPublic | BindingFlags.Static);
                if (handlerMethod == null)
                {
                    return;
                }

                _stateChangedHandler = Delegate.CreateDelegate(StateChangedEvent.EventHandlerType, handlerMethod);
                StateChangedEvent.AddEventHandler(null, _stateChangedHandler);
                _stateChangedEventBound = true;
            }
            catch (Exception exception)
            {
                _stateChangedHandler = null;
                Plugin.Log?.LogWarning(
                    $"Failed to subscribe to Clan local state changes: {exception.GetType().Name}: {exception.Message}");
            }
        }

        private static bool TryReadInstanceProperty(
            object instance,
            Type instanceType,
            string propertyName,
            out object? value)
        {
            value = null;
            try
            {
                var property = instanceType.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
                if (property == null)
                {
                    return false;
                }

                value = property.GetValue(instance, null);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryGetRegistryRevision(out long revision)
        {
            revision = 0L;
            if (!TryReadValue(RegistryRevisionMember, out var value) || value == null)
            {
                return false;
            }

            try
            {
                revision = Convert.ToInt64(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static MethodInfo? FindResolveMethod()
        {
            if (ApiType == null)
            {
                return null;
            }

            return ApiType.GetMethod(
                "ResolveWardAuthorization",
                BindingFlags.Public | BindingFlags.Static,
                binder: null,
                new[]
                {
                    typeof(string),
                    typeof(long),
                    typeof(string).MakeByRefType(),
                    typeof(string).MakeByRefType()
                },
                modifiers: null);
        }

        private static Assembly? GetPluginAssembly()
        {
            return Chainloader.PluginInfos.TryGetValue(PluginGuid, out var pluginInfo)
                ? pluginInfo.Instance?.GetType().Assembly
                : null;
        }

        private static MemberInfo? GetStaticMember(Type? type, string name)
        {
            return (MemberInfo?)type?.GetProperty(name, BindingFlags.Public | BindingFlags.Static) ??
                   type?.GetField(name, BindingFlags.Public | BindingFlags.Static);
        }

        private static bool TryReadInt(MemberInfo? member, out int value)
        {
            value = 0;
            if (!TryReadValue(member, out var rawValue) || rawValue == null)
            {
                return false;
            }

            try
            {
                value = Convert.ToInt32(rawValue, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryReadValue(MemberInfo? member, out object? value)
        {
            value = null;
            try
            {
                switch (member)
                {
                    case PropertyInfo property:
                        value = property.GetValue(null, null);
                        return true;
                    case FieldInfo field:
                        value = field.GetValue(null);
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
