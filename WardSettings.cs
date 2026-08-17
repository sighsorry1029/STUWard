using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

[Flags]
internal enum WardRestrictionOptions
{
    None = 0,
    Doors = 1 << 0,
    Portals = 1 << 1,
    Pickup = 1 << 2,
    PlacedConsumables = 1 << 3,
    ItemStands = 1 << 4,
    ArmorStands = 1 << 5,
    Containers = 1 << 6,
    CraftingStations = 1 << 7,
    TameablesAndSaddles = 1 << 8,
    All = Doors |
          Portals |
          Pickup |
          PlacedConsumables |
          ItemStands |
          ArmorStands |
          Containers |
          CraftingStations |
          TameablesAndSaddles
}

internal readonly struct WardRestrictionDefinition
{
    internal WardRestrictionDefinition(
        WardRestrictionOptions restriction,
        string configName,
        string configDescription,
        string localizationToken,
        string localizationFallback)
    {
        Restriction = restriction;
        ConfigName = configName;
        ConfigDescription = configDescription;
        LocalizationToken = localizationToken;
        LocalizationFallback = localizationFallback;
    }

    internal WardRestrictionOptions Restriction { get; }
    internal string ConfigName { get; }
    internal string ConfigDescription { get; }
    internal string LocalizationToken { get; }
    internal string LocalizationFallback { get; }
}

internal readonly struct WardConfiguration
{
    internal WardConfiguration(
        float radius,
        bool areaMarkerRotationEnabled,
        bool autoCloseEnabled,
        bool warningSoundEnabled,
        bool warningFlashEnabled,
        WardRestrictionOptions restrictions = WardRestrictionOptions.All)
    {
        Radius = radius;
        AreaMarkerRotationEnabled = areaMarkerRotationEnabled;
        AutoCloseEnabled = autoCloseEnabled;
        WarningSoundEnabled = warningSoundEnabled;
        WarningFlashEnabled = warningFlashEnabled;
        Restrictions = restrictions;
    }

    internal float Radius { get; }
    internal bool AreaMarkerRotationEnabled { get; }
    internal bool AutoCloseEnabled { get; }
    internal bool WarningSoundEnabled { get; }
    internal bool WarningFlashEnabled { get; }
    internal WardRestrictionOptions Restrictions { get; }
}

internal readonly struct CachedWardConfiguration
{
    internal CachedWardConfiguration(
        uint dataRevision,
        float maxRadius,
        WardRestrictionOptions forcedRestrictions,
        WardConfiguration configuration)
    {
        DataRevision = dataRevision;
        MaxRadius = maxRadius;
        ForcedRestrictions = forcedRestrictions;
        Configuration = configuration;
    }

    internal uint DataRevision { get; }
    internal float MaxRadius { get; }
    internal WardRestrictionOptions ForcedRestrictions { get; }
    internal WardConfiguration Configuration { get; }
}

internal readonly struct CachedAreaMarkerVisualState
{
    internal CachedAreaMarkerVisualState(
        int markerInstanceId,
        int segmentCount,
        int firstSegmentInstanceId,
        int lastSegmentInstanceId,
        float maxRadius,
        float radius)
    {
        MarkerInstanceId = markerInstanceId;
        SegmentCount = segmentCount;
        FirstSegmentInstanceId = firstSegmentInstanceId;
        LastSegmentInstanceId = lastSegmentInstanceId;
        MaxRadius = maxRadius;
        Radius = radius;
    }

    internal int MarkerInstanceId { get; }
    internal int SegmentCount { get; }
    internal int FirstSegmentInstanceId { get; }
    internal int LastSegmentInstanceId { get; }
    internal float MaxRadius { get; }
    internal float Radius { get; }
}

internal enum WardConfigurationRequestResultCode
{
    Applied = 0,
    Unchanged = 1,
    Denied = 2,
    InvalidPayload = 3,
    InvalidState = 4
}

internal readonly struct WardConfigurationRequestSubmission
{
    internal WardConfigurationRequestSubmission(
        bool isPending,
        long requestId,
        WardConfigurationRequestResultCode resultCode,
        WardConfiguration configuration)
    {
        IsPending = isPending;
        RequestId = requestId;
        ResultCode = resultCode;
        Configuration = configuration;
    }

    internal bool IsPending { get; }
    internal long RequestId { get; }
    internal WardConfigurationRequestResultCode ResultCode { get; }
    internal WardConfiguration Configuration { get; }
}

internal readonly struct WardConfigurationUpdateResult
{
    internal WardConfigurationUpdateResult(
        WardConfigurationRequestResultCode resultCode,
        WardConfiguration configuration)
    {
        ResultCode = resultCode;
        Configuration = configuration;
    }

    internal WardConfigurationRequestResultCode ResultCode { get; }
    internal WardConfiguration Configuration { get; }
}

internal static class WardSettings
{
    internal const int ManagedAreaMarkerSegments = 36;
    private const float ManagedAreaMarkerSegmentLengthMultiplier = 2f;
    internal const float MinRadius = 8f;
    internal const float MaxRadiusLimit = 64f;
    internal const float DefaultMaxRadius = 32f;
    internal const bool DefaultAreaMarkerRotationEnabled = true;
    internal const bool DefaultAutoCloseEnabled = true;
    internal const bool DefaultWarningSoundEnabled = true;
    internal const bool DefaultWarningFlashEnabled = true;
    private const float WarningEffectCooldownSeconds = 0.5f;
    private const float AreaMarkerBoundaryFlashSeconds = 0.5f;
    private const float AreaMarkerBoundaryHoldDistance = 0.75f;
    private const float PlacementBlockerHighlightSeconds = 1.5f;

    private const string RpcUpdateSettings = "STUWard_UpdateSettings";
    private const string RpcUpdateSettingsResponse = "STUWard_UpdateSettingsResponse";
    private const string RpcRemovePermitted = "STUWard_RemovePermitted";
    internal const string RadiusKey = "stuw_radius";
    private const string AreaMarkerRotationEnabledKey = "stuw_area_marker_rotation_enabled";
    private const string AutoCloseEnabledKey = "stuw_auto_close_enabled";
    private const string WarningSoundEnabledKey = "stuw_warning_sound_enabled";
    private const string WarningFlashEnabledKey = "stuw_warning_flash_enabled";
    private const string RestrictionOptionsKey = "stuw_restriction_options";
    private const float MinimumAreaMarkerBrightness = 0.35f;
    private const float FallbackNativeAreaMarkerSpeed = 0.1f;
    private const float EnabledAreaMarkerSpeedMultiplier = 0.5f;
    private static readonly string[] AreaMarkerColorProperties = { "_Color", "_BaseColor", "_TintColor" };

    private static readonly MaterialPropertyBlock AreaMarkerPropertyBlock = new();
    private static readonly List<PrivateArea> BoundaryFlashPreviousCandidates = new();
    private static readonly List<PrivateArea> BoundaryFlashCurrentCandidates = new();
    private static readonly HashSet<int> BoundaryFlashCandidateIds = new();
    private static readonly List<PrivateArea> ActiveAreaMarkerBrightnessAreas = new();
    private static bool _hasBoundaryFlashPlayerPosition;
    private static int _boundaryFlashPlayerInstanceId;
    private static Vector3 _boundaryFlashPlayerPosition;
    private static readonly WardRestrictionDefinition[] RestrictionDefinitionValues =
    {
        new(
            WardRestrictionOptions.Doors,
            "Doors",
            "Controls whether door interaction is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionDoorsToken,
            WardLocalization.UiRestrictionDoorsFallback),
        new(
            WardRestrictionOptions.Portals,
            "Portals",
            "Controls whether portal entry and TargetPortal routing are always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionPortalsToken,
            WardLocalization.UiRestrictionPortalsFallback),
        new(
            WardRestrictionOptions.Pickup,
            "Pickup",
            "Controls whether normal item pickup is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionPickupToken,
            WardLocalization.UiRestrictionPickupFallback),
        new(
            WardRestrictionOptions.PlacedConsumables,
            "Placed Consumables",
            "Controls whether eating hammer-placed consumables and feasts is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionPlacedConsumablesToken,
            WardLocalization.UiRestrictionPlacedConsumablesFallback),
        new(
            WardRestrictionOptions.ItemStands,
            "Item Stands",
            "Controls whether item stand interaction is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionItemStandsToken,
            WardLocalization.UiRestrictionItemStandsFallback),
        new(
            WardRestrictionOptions.ArmorStands,
            "Armor Stands",
            "Controls whether armor stand item placement is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionArmorStandsToken,
            WardLocalization.UiRestrictionArmorStandsFallback),
        new(
            WardRestrictionOptions.Containers,
            "Containers",
            "Controls whether container interaction and remote container access are always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionContainersToken,
            WardLocalization.UiRestrictionContainersFallback),
        new(
            WardRestrictionOptions.CraftingStations,
            "Crafting Stations",
            "Controls whether crafting station interaction is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionCraftingStationsToken,
            WardLocalization.UiRestrictionCraftingStationsFallback),
        new(
            WardRestrictionOptions.TameablesAndSaddles,
            "Tameables And Saddles",
            "Controls whether tameable and saddle interaction is always blocked by foreign enabled wards or can be turned off per ward.",
            WardLocalization.UiRestrictionTameablesAndSaddlesToken,
            WardLocalization.UiRestrictionTameablesAndSaddlesFallback)
    };

    private static long _nextConfigurationRequestId = 1L;

    internal static void RegisterRoutedRpcs(ZRoutedRpc routedRpc)
    {
        routedRpc.Register<ZPackage>(RpcUpdateSettings, HandleRoutedUpdateConfiguration);
        routedRpc.Register<ZPackage>(RpcUpdateSettingsResponse, HandleRoutedUpdateConfigurationResponse);
        routedRpc.Register<ZPackage>(RpcRemovePermitted, HandleRoutedRemovePermitted);
    }

    internal static IReadOnlyList<WardRestrictionDefinition> RestrictionDefinitions => RestrictionDefinitionValues;

    internal static float MaxRadius => Mathf.Clamp(
        Plugin.MaxWardRadius?.Value ?? DefaultMaxRadius,
        MinRadius,
        MaxRadiusLimit);

    internal static WardRestrictionOptions ForcedRestrictions
    {
        get
        {
            var restrictions = WardRestrictionOptions.None;
            for (var index = 0; index < RestrictionDefinitionValues.Length; index++)
            {
                var definition = RestrictionDefinitionValues[index];
                AddForcedRestriction(
                    ref restrictions,
                    definition.Restriction,
                    GetRestrictionConfigEntry(definition.Restriction));
            }

            return restrictions;
        }
    }

    internal static bool IsRestrictionForced(WardRestrictionOptions restriction)
    {
        return (ForcedRestrictions & restriction) != WardRestrictionOptions.None;
    }

    internal static bool HasRestriction(WardConfiguration configuration, WardRestrictionOptions restriction)
    {
        return (configuration.Restrictions & restriction) != WardRestrictionOptions.None;
    }

    internal static WardConfiguration WithRestriction(WardConfiguration configuration, WardRestrictionOptions restriction, bool enabled)
    {
        var restrictions = enabled
            ? configuration.Restrictions | restriction
            : configuration.Restrictions & ~restriction;
        return WithRestrictions(configuration, restrictions);
    }

    internal static WardConfiguration WithAutoCloseEnabled(WardConfiguration configuration, bool enabled)
    {
        return CopyConfiguration(
            configuration,
            autoCloseEnabled: enabled);
    }

    internal static WardConfiguration WithAreaMarkerRotationEnabled(WardConfiguration configuration, bool enabled)
    {
        return CopyConfiguration(
            configuration,
            areaMarkerRotationEnabled: enabled);
    }

    internal static WardConfiguration WithRestrictions(WardConfiguration configuration, WardRestrictionOptions restrictions)
    {
        return CopyConfiguration(
            configuration,
            restrictions: NormalizeRestrictions(restrictions));
    }

    private static WardConfiguration CopyConfiguration(
        WardConfiguration configuration,
        float? radius = null,
        bool? areaMarkerRotationEnabled = null,
        bool? autoCloseEnabled = null,
        bool? warningSoundEnabled = null,
        bool? warningFlashEnabled = null,
        WardRestrictionOptions? restrictions = null)
    {
        return new WardConfiguration(
            radius ?? configuration.Radius,
            areaMarkerRotationEnabled ?? configuration.AreaMarkerRotationEnabled,
            autoCloseEnabled ?? configuration.AutoCloseEnabled,
            warningSoundEnabled ?? configuration.WarningSoundEnabled,
            warningFlashEnabled ?? configuration.WarningFlashEnabled,
            restrictions ?? configuration.Restrictions);
    }

    private static void AddForcedRestriction(
        ref WardRestrictionOptions restrictions,
        WardRestrictionOptions restriction,
        BepInEx.Configuration.ConfigEntry<Plugin.RestrictionServerMode>? config)
    {
        if (config != null && config.Value == Plugin.RestrictionServerMode.ForcedOn)
        {
            restrictions |= restriction;
        }
    }

    private static BepInEx.Configuration.ConfigEntry<Plugin.RestrictionServerMode>? GetRestrictionConfigEntry(WardRestrictionOptions restriction)
    {
        return Plugin.RestrictionModes.TryGetValue(restriction, out var config) ? config : null;
    }

    internal static void CaptureNativeAreaMarkerSpeed(PrivateArea area)
    {
        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        if (context.HasNativeAreaMarkerSpeed)
        {
            return;
        }

        var marker = area.m_areaMarker;
        var speed = marker != null ? marker.m_speed : FallbackNativeAreaMarkerSpeed;
        if (float.IsNaN(speed) || float.IsInfinity(speed))
        {
            speed = FallbackNativeAreaMarkerSpeed;
        }

        context.NativeAreaMarkerSpeed = speed;
        context.HasNativeAreaMarkerSpeed = true;
    }

    private static float GetAreaMarkerSpeed(PrivateArea area, bool rotationEnabled)
    {
        if (!rotationEnabled)
        {
            return 0f;
        }

        var nativeSpeed = ManagedWardRuntimeContexts.TryGet(area, out var context) &&
                          context.HasNativeAreaMarkerSpeed
            ? context.NativeAreaMarkerSpeed
            : FallbackNativeAreaMarkerSpeed;
        return nativeSpeed * EnabledAreaMarkerSpeedMultiplier;
    }

    internal static void HandleMaxRadiusChanged()
    {
        ClampStoredRadiiToServerMaximum();
        ManagedWardRuntimeContexts.ClearConfigurationCaches();

        var allAreas = PrivateArea.m_allAreas;
        if (allAreas == null)
        {
            return;
        }

        for (var index = 0; index < allAreas.Count; index++)
        {
            var area = allAreas[index];
            var ward = ManagedWardRef.FromArea(area);
            if (!WardAccess.IsManagedWard(ward, false))
            {
                continue;
            }

            ApplyAreaState(ward);
        }

        ManagedWardMapStateService.InvalidateProjection();
    }

    internal static void ClampStoredRadiiToServerMaximum()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() || ZDOMan.instance == null)
        {
            return;
        }

        var maximumRadius = MaxRadius;
        foreach (var zdo in ZDOMan.instance.m_objectsByID.Values)
        {
            if (zdo == null ||
                !WardOwnership.IsManagedWardZdo(zdo) ||
                !WardOwnership.IsAcceptedManagedWard(zdo))
            {
                continue;
            }

            if (!zdo.GetFloat(RadiusKey, out var storedRadius) ||
                float.IsNaN(storedRadius) ||
                float.IsInfinity(storedRadius) ||
                storedRadius <= maximumRadius ||
                !WardOwnership.TryClaimManagedWardMutationOwnership(zdo))
            {
                continue;
            }

            zdo.Set(RadiusKey, maximumRadius);
            WardOwnership.CompleteAuthoritativeManagedWardMutation(zdo);
        }
    }

    internal static WardConfiguration GetConfiguration(PrivateArea area)
    {
        var zdo = GetZdo(area);
        var maxRadius = MaxRadius;
        var forcedRestrictions = ForcedRestrictions;
        if (zdo != null)
        {
            var revision = zdo.DataRevision;
            var context = ManagedWardRuntimeContexts.GetOrCreate(area);
            if (context.HasCachedConfiguration)
            {
                var cachedConfiguration = context.CachedConfiguration;
                if (cachedConfiguration.DataRevision == revision &&
                    Mathf.Approximately(cachedConfiguration.MaxRadius, maxRadius) &&
                    cachedConfiguration.ForcedRestrictions == forcedRestrictions)
                {
                    return cachedConfiguration.Configuration;
                }
            }
        }

        var configuration = ReadConfiguration(zdo, maxRadius, forcedRestrictions);
        if (zdo != null)
        {
            var context = ManagedWardRuntimeContexts.GetOrCreate(area);
            context.CachedConfiguration = new CachedWardConfiguration(zdo.DataRevision, maxRadius, forcedRestrictions, configuration);
            context.HasCachedConfiguration = true;
        }

        return configuration;
    }

    private static WardConfiguration GetConfiguration(ZDO? zdo)
    {
        return ReadConfiguration(zdo, MaxRadius, ForcedRestrictions);
    }

    private static WardConfiguration ReadConfiguration(
        ZDO? zdo,
        float maxRadius,
        WardRestrictionOptions forcedRestrictions)
    {
        var radius = Mathf.Min(GetStoredRadius(zdo), maxRadius);
        var areaMarkerRotationEnabled = zdo?.GetBool(
            AreaMarkerRotationEnabledKey,
            DefaultAreaMarkerRotationEnabled) ?? DefaultAreaMarkerRotationEnabled;
        var autoCloseEnabled = zdo?.GetBool(AutoCloseEnabledKey, DefaultAutoCloseEnabled) ?? DefaultAutoCloseEnabled;
        var warningSoundEnabled = zdo?.GetBool(WarningSoundEnabledKey, DefaultWarningSoundEnabled) ?? DefaultWarningSoundEnabled;
        var warningFlashEnabled = zdo?.GetBool(WarningFlashEnabledKey, DefaultWarningFlashEnabled) ?? DefaultWarningFlashEnabled;
        var restrictions = ApplyForcedRestrictions(
            NormalizeRestrictions((WardRestrictionOptions)(zdo?.GetInt(RestrictionOptionsKey, (int)WardRestrictionOptions.All) ?? (int)WardRestrictionOptions.All)),
            forcedRestrictions);

        return new WardConfiguration(
            radius,
            areaMarkerRotationEnabled,
            autoCloseEnabled,
            warningSoundEnabled,
            warningFlashEnabled,
            restrictions);
    }

    internal static void ApplyAreaState(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        ApplyAreaState(ward, GetConfiguration(area));
    }

    internal static void ApplyAreaState(ManagedWardRef ward, WardConfiguration configuration)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        var radiusChanged = !Mathf.Approximately(area.m_radius, configuration.Radius);
        if (radiusChanged)
        {
            area.m_radius = configuration.Radius;
        }

        var marker = area.m_areaMarker;
        if (marker == null)
        {
            InvalidateAreaMarkerVisuals(area);
        }
        else
        {
            var markerVisible = ShouldShowAreaMarker(area);
            if (marker.m_nrOfSegments != ManagedAreaMarkerSegments)
            {
                marker.m_nrOfSegments = ManagedAreaMarkerSegments;
            }

            if (!Mathf.Approximately(marker.m_radius, configuration.Radius))
            {
                marker.m_radius = configuration.Radius;
            }

            var markerSpeed = GetAreaMarkerSpeed(area, configuration.AreaMarkerRotationEnabled);
            if (!Mathf.Approximately(marker.m_speed, markerSpeed))
            {
                marker.m_speed = markerSpeed;
            }

            ApplyManagedAreaMarkerVisibility(area, markerVisible);
            if (ShouldRefreshAreaMarkerVisuals(area, marker, configuration))
            {
                ApplyAreaMarkerVisuals(area, marker, configuration);
                CacheAreaMarkerVisualState(area, marker, configuration);
            }
        }

        if (radiusChanged)
        {
            ManagedWardPresenceService.Invalidate();
            ManagedWardPlacementPreviewService.Invalidate();
            WardAccess.RefreshManagedWardSpatialIndexEntry(ward);
        }
    }

    internal static void ApplyPlacementGhostPreviewRadius(PrivateArea area)
    {
        if (area == null || !Player.IsPlacementGhost(area.gameObject))
        {
            return;
        }

        var player = Player.m_localPlayer;
        _ = WardAccess.TryGetAutomaticPlacementRadius(player, area.transform.position, out var availableRadius);
        var previewRadius = Mathf.Clamp(availableRadius, MinRadius, MaxRadius);
        if (!Mathf.Approximately(area.m_radius, previewRadius))
        {
            area.m_radius = previewRadius;
        }

        if (!area.m_areaMarker)
        {
            return;
        }

        if (area.m_areaMarker.m_nrOfSegments != ManagedAreaMarkerSegments)
        {
            area.m_areaMarker.m_nrOfSegments = ManagedAreaMarkerSegments;
        }

        if (!Mathf.Approximately(area.m_areaMarker.m_radius, previewRadius))
        {
            area.m_areaMarker.m_radius = previewRadius;
        }

        if (!Mathf.Approximately(area.m_areaMarker.m_speed, 0f))
        {
            area.m_areaMarker.m_speed = 0f;
        }
    }

    internal static bool ShouldShowAreaMarker(PrivateArea area)
    {
        return ShouldNormallyShowAreaMarker(area) || IsPlacementBlockerHighlightActive(area);
    }

    private static bool ShouldNormallyShowAreaMarker(PrivateArea area)
    {
        return Player.IsPlacementGhost(area.gameObject) || area.IsEnabled();
    }

    internal static void ShowManagedAreaMarker(PrivateArea area)
    {
        if (area == null || area.m_areaMarker == null)
        {
            return;
        }

        area.CancelInvoke(nameof(PrivateArea.HideMarker));
        area.m_areaMarker.gameObject.SetActive(true);
    }

    internal static void InvalidateAreaMarkerVisuals(PrivateArea? area)
    {
        ManagedWardRuntimeContexts.ClearAreaMarkerVisualState(area);
    }

    internal static void ResetLocalBoundaryFlashState()
    {
        _hasBoundaryFlashPlayerPosition = false;
        _boundaryFlashPlayerInstanceId = 0;
        _boundaryFlashPlayerPosition = default;
        BoundaryFlashPreviousCandidates.Clear();
        BoundaryFlashCurrentCandidates.Clear();
        BoundaryFlashCandidateIds.Clear();
        ActiveAreaMarkerBrightnessAreas.Clear();
    }

    internal static void HandleLocalBoundaryBrightenModeChanged()
    {
        var now = Time.unscaledTime;
        for (var index = ActiveAreaMarkerBrightnessAreas.Count - 1; index >= 0; index--)
        {
            var area = ActiveAreaMarkerBrightnessAreas[index];
            if (area == null || !ManagedWardRuntimeContexts.TryGet(area, out var context))
            {
                ActiveAreaMarkerBrightnessAreas.RemoveAt(index);
                continue;
            }

            context.AreaMarkerBoundaryFlashUntil = float.NegativeInfinity;
            if (now < context.AreaMarkerPlacementHighlightUntil)
            {
                continue;
            }

            context.AreaMarkerPlacementHighlightUntil = float.NegativeInfinity;
            ActiveAreaMarkerBrightnessAreas.RemoveAt(index);
            // Presence in the tracked list means the last applied marker state may
            // still be bright even when its deadline expired just before this event.
            RefreshAreaMarkerVisuals(area);
        }

        ResetBoundaryTrackingState();
    }

    internal static void UpdateLocalBoundaryFlash()
    {
        var player = Player.m_localPlayer;
        var playerId = player != null ? player.GetPlayerID() : 0L;
        var mode = GetBoundaryBrightenMode();
        UpdateActiveAreaMarkerBrightness(playerId, mode);

        if (player == null)
        {
            ResetBoundaryTrackingState();
            return;
        }

        if (player.IsTeleporting() || player.IsDead())
        {
            // Seed again after teleport/death so relocation is not mistaken for a
            // physical boundary crossing on the first playable frame.
            ResetBoundaryTrackingState();
            return;
        }

        if (!IsBoundaryBrightenModeEnabled(mode))
        {
            ResetBoundaryTrackingState();
            return;
        }

        var playerInstanceId = player.GetInstanceID();
        var currentPosition = player.transform.position;
        WardAccess.FillCandidateManagedWards(
            currentPosition,
            AreaMarkerBoundaryHoldDistance,
            requireEnabled: true,
            BoundaryFlashCurrentCandidates);
        MaintainBoundaryBrightness(BoundaryFlashCurrentCandidates, currentPosition, playerId, mode);

        if (!_hasBoundaryFlashPlayerPosition || _boundaryFlashPlayerInstanceId != playerInstanceId)
        {
            _hasBoundaryFlashPlayerPosition = true;
            _boundaryFlashPlayerInstanceId = playerInstanceId;
            _boundaryFlashPlayerPosition = currentPosition;
            return;
        }

        var previousPosition = _boundaryFlashPlayerPosition;
        _boundaryFlashPlayerPosition = currentPosition;
        var deltaX = currentPosition.x - previousPosition.x;
        var deltaZ = currentPosition.z - previousPosition.z;
        var movementSquared = (deltaX * deltaX) + (deltaZ * deltaZ);
        if (movementSquared <= 0.0001f)
        {
            return;
        }

        // Treat large discontinuities as teleports rather than walking across every
        // intervening ward. Normal movement remains far below this per-frame limit.
        var maximumSampleDistance = Mathf.Max(MinRadius, MaxRadius) * 2f;
        if (movementSquared > maximumSampleDistance * maximumSampleDistance)
        {
            return;
        }

        WardAccess.FillCandidateManagedWards(
            previousPosition,
            0f,
            requireEnabled: true,
            BoundaryFlashPreviousCandidates);

        BoundaryFlashCandidateIds.Clear();
        DetectBoundaryCrossings(BoundaryFlashPreviousCandidates, previousPosition, currentPosition, playerId, mode);
        DetectBoundaryCrossings(BoundaryFlashCurrentCandidates, previousPosition, currentPosition, playerId, mode);
    }

    private static void MaintainBoundaryBrightness(
        IReadOnlyList<PrivateArea> candidates,
        Vector3 playerPosition,
        long playerId,
        Plugin.BoundaryBrightenMode mode)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var area = candidates[index];
            if (area == null ||
                !area.IsEnabled() ||
                !ShouldBrightenBoundary(area, playerId, mode))
            {
                continue;
            }

            var areaPosition = area.transform.position;
            var deltaX = playerPosition.x - areaPosition.x;
            var deltaZ = playerPosition.z - areaPosition.z;
            var distanceSquared = (deltaX * deltaX) + (deltaZ * deltaZ);
            var radius = Mathf.Max(0f, area.m_radius);
            var innerRadius = Mathf.Max(0f, radius - AreaMarkerBoundaryHoldDistance);
            var outerRadius = radius + AreaMarkerBoundaryHoldDistance;
            if (distanceSquared < innerRadius * innerRadius || distanceSquared > outerRadius * outerRadius)
            {
                continue;
            }

            // Reuse the crossing flash as a short lease. While the player remains
            // on the boundary, only its deadline is extended; marker renderers are
            // refreshed once on entry and once after the player leaves.
            StartBoundaryFlash(area);
        }
    }

    private static void DetectBoundaryCrossings(
        IReadOnlyList<PrivateArea> candidates,
        Vector3 previousPosition,
        Vector3 currentPosition,
        long playerId,
        Plugin.BoundaryBrightenMode mode)
    {
        for (var index = 0; index < candidates.Count; index++)
        {
            var area = candidates[index];
            if (area == null ||
                !area.IsEnabled() ||
                !BoundaryFlashCandidateIds.Add(area.GetInstanceID()) ||
                !ShouldBrightenBoundary(area, playerId, mode) ||
                area.IsInside(previousPosition, 0f) == area.IsInside(currentPosition, 0f))
            {
                continue;
            }

            StartBoundaryFlash(area);
        }
    }

    private static void StartBoundaryFlash(PrivateArea area)
    {
        if (area.m_areaMarker == null || !ShouldNormallyShowAreaMarker(area))
        {
            return;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        var now = Time.unscaledTime;
        var wasActive = IsAreaMarkerBrightnessActive(area, context, now);
        context.AreaMarkerBoundaryFlashUntil = now + AreaMarkerBoundaryFlashSeconds;
        EnsureAreaMarkerBrightnessTracked(area);
        if (wasActive)
        {
            return;
        }

        RefreshAreaMarkerVisuals(area);
    }

    internal static void HighlightPlacementBlockingWard(PrivateArea? area)
    {
        if (area == null ||
            area.m_areaMarker == null ||
            !WardAccess.IsManagedWard(area, requireEnabled: false))
        {
            return;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        var now = Time.unscaledTime;
        var wasActive = IsAreaMarkerBrightnessActive(area, context, now);
        context.AreaMarkerPlacementHighlightUntil = now + PlacementBlockerHighlightSeconds;
        EnsureAreaMarkerBrightnessTracked(area);
        if (!wasActive)
        {
            RefreshAreaMarkerVisuals(area);
        }
    }

    private static void UpdateActiveAreaMarkerBrightness(long playerId, Plugin.BoundaryBrightenMode mode)
    {
        var now = Time.unscaledTime;
        for (var index = ActiveAreaMarkerBrightnessAreas.Count - 1; index >= 0; index--)
        {
            var area = ActiveAreaMarkerBrightnessAreas[index];
            if (area == null || !ManagedWardRuntimeContexts.TryGet(area, out var context))
            {
                ActiveAreaMarkerBrightnessAreas.RemoveAt(index);
                continue;
            }

            var boundaryActive = area.IsEnabled() &&
                                 now < context.AreaMarkerBoundaryFlashUntil &&
                                 ShouldNormallyShowAreaMarker(area) &&
                                 ShouldBrightenBoundary(area, playerId, mode);
            if (!boundaryActive)
            {
                context.AreaMarkerBoundaryFlashUntil = float.NegativeInfinity;
            }

            var placementHighlightActive = now < context.AreaMarkerPlacementHighlightUntil;
            if (boundaryActive || placementHighlightActive)
            {
                continue;
            }

            context.AreaMarkerBoundaryFlashUntil = float.NegativeInfinity;
            context.AreaMarkerPlacementHighlightUntil = float.NegativeInfinity;
            ActiveAreaMarkerBrightnessAreas.RemoveAt(index);
            RefreshAreaMarkerVisuals(area);
        }
    }

    private static void EnsureAreaMarkerBrightnessTracked(PrivateArea area)
    {
        if (!ActiveAreaMarkerBrightnessAreas.Contains(area))
        {
            ActiveAreaMarkerBrightnessAreas.Add(area);
        }
    }

    private static void ResetBoundaryTrackingState()
    {
        _hasBoundaryFlashPlayerPosition = false;
        _boundaryFlashPlayerInstanceId = 0;
        _boundaryFlashPlayerPosition = default;
        BoundaryFlashPreviousCandidates.Clear();
        BoundaryFlashCurrentCandidates.Clear();
        BoundaryFlashCandidateIds.Clear();
    }

    private static void RefreshAreaMarkerVisuals(PrivateArea area)
    {
        InvalidateAreaMarkerVisuals(area);
        ApplyAreaState(ManagedWardRef.FromArea(area));
    }

    private static bool IsAreaMarkerBrightnessActive(PrivateArea area)
    {
        return ManagedWardRuntimeContexts.TryGet(area, out var context) &&
               IsAreaMarkerBrightnessActive(area, context, Time.unscaledTime);
    }

    private static bool IsAreaMarkerBrightnessActive(
        PrivateArea area,
        ManagedWardRuntimeContext context,
        float now)
    {
        return (area.IsEnabled() && now < context.AreaMarkerBoundaryFlashUntil) ||
               now < context.AreaMarkerPlacementHighlightUntil;
    }

    private static bool IsPlacementBlockerHighlightActive(PrivateArea area)
    {
        return ManagedWardRuntimeContexts.TryGet(area, out var context) &&
               Time.unscaledTime < context.AreaMarkerPlacementHighlightUntil;
    }

    private static Plugin.BoundaryBrightenMode GetBoundaryBrightenMode()
    {
        return Plugin.WardBoundaryBrightenMode?.Value ?? Plugin.BoundaryBrightenMode.All;
    }

    private static bool IsBoundaryBrightenModeEnabled(Plugin.BoundaryBrightenMode mode)
    {
        return mode is Plugin.BoundaryBrightenMode.TrustedOnly or
            Plugin.BoundaryBrightenMode.UntrustedOnly or
            Plugin.BoundaryBrightenMode.All;
    }

    private static bool ShouldBrightenBoundary(
        PrivateArea area,
        long playerId,
        Plugin.BoundaryBrightenMode mode)
    {
        if (playerId == 0L)
        {
            return false;
        }

        return mode switch
        {
            Plugin.BoundaryBrightenMode.All => true,
            Plugin.BoundaryBrightenMode.TrustedOnly => WardAccess.HasManagedWardTrust(area, playerId),
            Plugin.BoundaryBrightenMode.UntrustedOnly => !WardAccess.HasManagedWardTrust(area, playerId),
            _ => false
        };
    }

    internal static float GetRadius(PrivateArea area)
    {
        return GetConfiguration(area).Radius;
    }

    internal static float GetStoredRadiusOrMin(PrivateArea area)
    {
        return area == null ? MinRadius : GetStoredRadius(GetZdo(area), MinRadius);
    }

    internal static float GetStoredRadius(ZDO? zdo, float defaultRadius = MinRadius)
    {
        var storedRadius = zdo?.GetFloat(RadiusKey, defaultRadius) ?? defaultRadius;
        if (float.IsNaN(storedRadius) || float.IsInfinity(storedRadius))
        {
            storedRadius = defaultRadius;
        }

        return Mathf.Clamp(storedRadius, MinRadius, MaxRadius);
    }

    internal static bool TryAssignAuthoritativePlacementRadius(ZDO? zdo, out float radius)
    {
        radius = MinRadius;
        if (zdo == null ||
            !zdo.IsValid() ||
            ZNet.instance == null ||
            !ZNet.instance.IsServer() ||
            zdo.GetOwner() != ZDOMan.GetSessionID())
        {
            return false;
        }

        var maxRadius = GetMaxNonOverlappingRadius(zdo);
        if (float.IsNaN(maxRadius) || float.IsInfinity(maxRadius) || maxRadius < MinRadius)
        {
            radius = maxRadius;
            return false;
        }

        radius = Mathf.Clamp(maxRadius, MinRadius, MaxRadius);
        zdo.Set(RadiusKey, radius);
        return true;
    }

    internal static bool IsDoorAutoCloseEnabledAt(Vector3 point)
    {
        var allAreas = WardAccess.GetCandidateManagedWards(point, 0f, requireEnabled: true);
        foreach (var area in allAreas)
        {
            if (area == null || !area.IsInside(point, 0f))
            {
                continue;
            }

            if (GetConfiguration(area).AutoCloseEnabled)
            {
                return true;
            }
        }

        return false;
    }

    internal static bool HandleManagedFlashEffect(PrivateArea area)
    {
        if (!WardAccess.IsManagedWard(area, false) || area.m_flashEffect == null)
        {
            return true;
        }

        var configuration = GetConfiguration(area);
        if (!configuration.WarningFlashEnabled && !configuration.WarningSoundEnabled)
        {
            return false;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        var now = Time.unscaledTime;
        if (now < context.WarningEffectCooldownUntil)
        {
            return false;
        }

        context.WarningEffectCooldownUntil = now + WarningEffectCooldownSeconds;
        if (configuration.WarningFlashEnabled && configuration.WarningSoundEnabled)
        {
            return true;
        }

        PlayManagedWarningEffect(area, configuration.WarningSoundEnabled, configuration.WarningFlashEnabled);
        return false;
    }

    private static void PlayManagedWarningEffect(PrivateArea area, bool warningSoundEnabled, bool warningFlashEnabled)
    {
        if (!warningFlashEnabled && !warningSoundEnabled)
        {
            return;
        }

        var instances = area.m_flashEffect.Create(area.transform.position, Quaternion.identity, null, 1f, -1);
        if (instances == null)
        {
            return;
        }

        for (var index = 0; index < instances.Length; index++)
        {
            var instance = instances[index];
            if (instance == null)
            {
                continue;
            }

            if (!warningSoundEnabled)
            {
                var audioSources = instance.GetComponentsInChildren<AudioSource>(true);
                for (var audioIndex = 0; audioIndex < audioSources.Length; audioIndex++)
                {
                    var audioSource = audioSources[audioIndex];
                    if (audioSource == null)
                    {
                        continue;
                    }

                    audioSource.mute = true;
                    audioSource.volume = 0f;
                    if (audioSource.isPlaying)
                    {
                        audioSource.Stop();
                    }
                }
            }

            if (warningFlashEnabled)
            {
                continue;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            for (var rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                var renderer = renderers[rendererIndex];
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
            }

            var lights = instance.GetComponentsInChildren<Light>(true);
            for (var lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                var light = lights[lightIndex];
                if (light != null)
                {
                    light.enabled = false;
                }
            }
        }
    }

    internal static WardConfiguration WithWarningSoundEnabled(WardConfiguration configuration, bool enabled)
    {
        return CopyConfiguration(configuration, warningSoundEnabled: enabled);
    }

    internal static WardConfiguration WithWarningFlashEnabled(WardConfiguration configuration, bool enabled)
    {
        return CopyConfiguration(configuration, warningFlashEnabled: enabled);
    }

    internal static WardConfigurationRequestSubmission RequestUpdateConfiguration(PrivateArea area, WardConfiguration configuration)
    {
        var nview = GetNView(area);
        var player = Player.m_localPlayer;
        var currentConfiguration = GetConfiguration(area);
        if (nview == null || player == null || !nview.IsValid())
        {
            return new WardConfigurationRequestSubmission(
                isPending: false,
                requestId: 0L,
                WardConfigurationRequestResultCode.InvalidState,
                currentConfiguration);
        }

        if (WardOwnership.CanApplyManagedWardStateLocally(nview))
        {
            if (!WardAccess.HasManagedWardTrust(area, player.GetPlayerID()))
            {
                return new WardConfigurationRequestSubmission(
                    isPending: false,
                    requestId: 0L,
                    WardConfigurationRequestResultCode.Denied,
                    currentConfiguration);
            }

            var zdo = nview.GetZDO();
            if (!WardOwnership.TryClaimManagedWardMutationOwnership(zdo))
            {
                return new WardConfigurationRequestSubmission(
                    isPending: false,
                    requestId: 0L,
                    WardConfigurationRequestResultCode.InvalidState,
                    currentConfiguration);
            }

            var localResult = ProcessConfigurationUpdate(zdo, configuration, currentConfiguration);
            return new WardConfigurationRequestSubmission(
                isPending: false,
                requestId: 0L,
                localResult.ResultCode,
                localResult.Configuration);
        }

        var requestId = AllocateConfigurationRequestId();
        var requestPackage = new ZPackage();
        requestPackage.Write(nview.GetZDO().m_uid);
        requestPackage.Write(requestId);
        WriteConfigurationPayload(requestPackage, configuration);
        if (!WardOwnership.TryInvokeServerRoutedRpc(RpcUpdateSettings, requestPackage))
        {
            return new WardConfigurationRequestSubmission(
                isPending: false,
                requestId: 0L,
                WardConfigurationRequestResultCode.InvalidState,
                currentConfiguration);
        }

        return new WardConfigurationRequestSubmission(
            isPending: true,
            requestId: requestId,
            WardConfigurationRequestResultCode.Applied,
            currentConfiguration);
    }

    internal static void RequestRemovePermitted(PrivateArea area, long targetPlayerId)
    {
        var nview = GetNView(area);
        var player = Player.m_localPlayer;
        if (nview == null || player == null || !nview.IsValid())
        {
            return;
        }

        if (WardOwnership.CanApplyManagedWardStateLocally(nview))
        {
            if (!WardAccess.HasManagedWardTrust(area, player.GetPlayerID()))
            {
                return;
            }

            var zdo = nview.GetZDO();
            if (WardOwnership.TryClaimManagedWardMutationOwnership(zdo) &&
                WardPrivateAreaSafeAccess.RemovePermittedPlayer(zdo, targetPlayerId))
            {
                WardOwnership.CompleteAuthoritativePermittedMutation(zdo);
            }

            return;
        }

        var requestPackage = new ZPackage();
        requestPackage.Write(nview.GetZDO().m_uid);
        requestPackage.Write(targetPlayerId);
        if (!WardOwnership.TryInvokeServerRoutedRpc(RpcRemovePermitted, requestPackage))
        {
            return;
        }

    }

    private static void HandleRoutedUpdateConfiguration(long sender, ZPackage? pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() ||
            !TryReadWardZdoId(pkg, out var wardZdoId))
        {
            return;
        }

        var zdo = ZDOMan.instance?.GetZDO(wardZdoId);
        var currentConfiguration = GetConfiguration(zdo);
        if (!TryReadConfigurationRequest(pkg, out var requestId, out var requestedConfiguration))
        {
            SendRoutedUpdateConfigurationResponse(
                sender,
                wardZdoId,
                0L,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.InvalidPayload,
                    currentConfiguration));
            return;
        }

        if (zdo == null || !zdo.IsValid() || !WardOwnership.IsManagedWardZdo(zdo))
        {
            SendRoutedUpdateConfigurationResponse(
                sender,
                wardZdoId,
                requestId,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.InvalidState,
                    requestedConfiguration));
            return;
        }

        if (!WardOwnership.TryResolveAuthoritativeManagedWardRequest(
                sender,
                wardZdoId,
                out zdo,
                out var requesterId) ||
            !WardAccess.HasManagedWardTrust(zdo, requesterId))
        {
            SendRoutedUpdateConfigurationResponse(
                sender,
                wardZdoId,
                requestId,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.Denied,
                    currentConfiguration));
            return;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(zdo))
        {
            SendRoutedUpdateConfigurationResponse(
                sender,
                wardZdoId,
                requestId,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.InvalidState,
                    currentConfiguration));
            return;
        }

        var result = ProcessConfigurationUpdate(zdo, requestedConfiguration, currentConfiguration);
        SendRoutedUpdateConfigurationResponse(sender, wardZdoId, requestId, result);
    }

    private static void HandleRoutedUpdateConfigurationResponse(long sender, ZPackage? pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender) || !TryReadWardZdoId(pkg, out var wardZdoId))
        {
            return;
        }

        var instance = ZNetScene.instance?.FindInstance(wardZdoId);
        var area = instance != null
            ? instance.GetComponent<PrivateArea>() ?? instance.GetComponentInChildren<PrivateArea>()
            : null;
        if (area != null)
        {
            HandleUpdateConfigurationResponse(area, sender, pkg!);
        }
    }

    private static void HandleRoutedRemovePermitted(long sender, ZPackage? pkg)
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer() ||
            !TryReadWardZdoId(pkg, out var wardZdoId) ||
            !TryReadRemovePermittedRequest(pkg, out var targetPlayerId) ||
            !WardOwnership.TryResolveAuthoritativeManagedWardRequest(
                sender,
                wardZdoId,
                out var zdo,
                out var requesterId))
        {
            return;
        }

        if (!WardAccess.HasManagedWardTrust(zdo, requesterId) ||
            !WardOwnership.TryClaimManagedWardMutationOwnership(zdo) ||
            !WardPrivateAreaSafeAccess.RemovePermittedPlayer(zdo, targetPlayerId))
        {
            return;
        }

        WardOwnership.CompleteAuthoritativePermittedMutation(zdo);
    }

    private static void HandleUpdateConfigurationResponse(PrivateArea area, long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender))
        {
            return;
        }

        if (!TryReadConfigurationResponse(area, pkg, out var requestId, out var resultCode, out var configuration))
        {
            return;
        }

        ShowConfigurationRequestFeedback(resultCode);
        WardGuiController.Instance?.HandleWardConfigurationResponse(area, requestId, resultCode, configuration);
    }

    private static bool TryCreateConfiguration(
        float radius,
        bool areaMarkerRotationEnabled,
        bool autoCloseEnabled,
        bool warningSoundEnabled,
        bool warningFlashEnabled,
        WardRestrictionOptions restrictions,
        out WardConfiguration configuration)
    {
        configuration = default;
        if (float.IsNaN(radius) || float.IsInfinity(radius))
        {
            return false;
        }

        configuration = new WardConfiguration(
            Mathf.Clamp(radius, MinRadius, MaxRadius),
            areaMarkerRotationEnabled,
            autoCloseEnabled,
            warningSoundEnabled,
            warningFlashEnabled,
            NormalizeRestrictions(restrictions));
        return true;
    }

    private static void WriteConfigurationPayload(ZPackage pkg, WardConfiguration configuration)
    {
        pkg.Write(configuration.Radius);
        pkg.Write(configuration.AreaMarkerRotationEnabled);
        pkg.Write(configuration.AutoCloseEnabled);
        pkg.Write(configuration.WarningSoundEnabled);
        pkg.Write(configuration.WarningFlashEnabled);
        pkg.Write((int)configuration.Restrictions);
    }

    private static void SaveConfiguration(
        ZDO zdo,
        WardConfiguration currentConfiguration,
        WardConfiguration configuration)
    {
        if (!Mathf.Approximately(currentConfiguration.Radius, configuration.Radius))
        {
            zdo.Set(RadiusKey, configuration.Radius);
        }

        if (currentConfiguration.AreaMarkerRotationEnabled != configuration.AreaMarkerRotationEnabled)
        {
            zdo.Set(AreaMarkerRotationEnabledKey, configuration.AreaMarkerRotationEnabled);
        }

        if (currentConfiguration.AutoCloseEnabled != configuration.AutoCloseEnabled)
        {
            zdo.Set(AutoCloseEnabledKey, configuration.AutoCloseEnabled);
        }

        if (currentConfiguration.WarningSoundEnabled != configuration.WarningSoundEnabled)
        {
            zdo.Set(WarningSoundEnabledKey, configuration.WarningSoundEnabled);
        }

        if (currentConfiguration.WarningFlashEnabled != configuration.WarningFlashEnabled)
        {
            zdo.Set(WarningFlashEnabledKey, configuration.WarningFlashEnabled);
        }

        if (currentConfiguration.Restrictions != configuration.Restrictions)
        {
            zdo.Set(RestrictionOptionsKey, (int)configuration.Restrictions);
        }
    }

    private static WardConfigurationUpdateResult ProcessConfigurationUpdate(
        ZDO zdo,
        WardConfiguration requestedConfiguration,
        WardConfiguration currentConfiguration)
    {
        var configuration = ClampConfiguration(zdo, requestedConfiguration);
        if (ConfigurationsMatch(currentConfiguration, configuration))
        {
            return new WardConfigurationUpdateResult(
                WardConfigurationRequestResultCode.Unchanged,
                currentConfiguration);
        }

        SaveConfiguration(zdo, currentConfiguration, configuration);
        WardOwnership.CompleteAuthoritativeManagedWardMutation(zdo);

        return new WardConfigurationUpdateResult(
            WardConfigurationRequestResultCode.Applied,
            configuration);
    }

    private static WardConfiguration ClampConfiguration(ZDO zdo, WardConfiguration configuration)
    {
        var maxRadius = GetMaxNonOverlappingRadius(zdo);
        var storedRadius = GetStoredRadius(zdo);
        var clampedRadius = Mathf.Clamp(Mathf.Min(storedRadius, maxRadius), MinRadius, MaxRadius);
        return CopyConfiguration(
            configuration,
            radius: clampedRadius,
            restrictions: ApplyForcedRestrictions(configuration.Restrictions));
    }

    private static float GetMaxNonOverlappingRadius(ZDO zdo)
    {
        var zdoMan = ZDOMan.instance;
        if (zdoMan == null)
        {
            return MaxRadius;
        }

        var position = zdo.GetPosition();
        var ownerPlayerId = zdo.GetLong(ZDOVars.s_creator, 0L);
        var guildsAvailable = GuildsCompat.IsAvailable();
        var guildId = guildsAvailable ? GuildsCompat.GetWardGuildId(zdo) : 0;
        var overlapAreas = new List<WardOverlapArea>();
        foreach (var candidate in zdoMan.m_objectsByID.Values)
        {
            if (candidate == null ||
                candidate.m_uid == zdo.m_uid ||
                !WardOwnership.IsManagedWardZdo(candidate) ||
                !WardOwnership.IsAcceptedManagedWard(candidate))
            {
                continue;
            }

            var candidatePosition = candidate.GetPosition();
            overlapAreas.Add(new WardOverlapArea(
                candidate.m_uid.GetHashCode(),
                candidatePosition.x,
                candidatePosition.z,
                GetStoredRadius(candidate),
                candidate.GetLong(ZDOVars.s_creator, 0L),
                guildId != 0 && guildsAvailable ? GuildsCompat.GetWardGuildId(candidate) : 0));
        }

        return WardOverlapPolicy.GetMaxNonOverlappingRadius(
            MaxRadius,
            new WardOverlapQuery(position.x, position.z, MaxRadius, ownerPlayerId, guildId),
            overlapAreas);
    }

    private static void ApplyAreaMarkerVisuals(
        PrivateArea area,
        CircleProjector marker,
        WardConfiguration configuration)
    {
        var segments = marker.m_segments;
        if (segments == null || segments.Count == 0)
        {
            return;
        }

        var baseScale = marker.m_prefab != null ? marker.m_prefab.transform.localScale : Vector3.one;
        var lengthScale = Mathf.Clamp(
            (configuration.Radius / MaxRadius) * ManagedAreaMarkerSegmentLengthMultiplier,
            0f,
            ManagedAreaMarkerSegmentLengthMultiplier);
        var brightness = IsAreaMarkerBrightnessActive(area) ? 1f : MinimumAreaMarkerBrightness;

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment == null)
            {
                continue;
            }

            segment.transform.localScale = ScaleMarkerSegment(baseScale, lengthScale);
            ApplyAreaMarkerBrightness(segment, brightness);
        }
    }

    private static Vector3 ScaleMarkerSegment(Vector3 baseScale, float lengthScale)
    {
        if (baseScale.x > baseScale.z)
        {
            return new Vector3(baseScale.x * lengthScale, baseScale.y, baseScale.z);
        }

        return new Vector3(baseScale.x, baseScale.y, baseScale.z * lengthScale);
    }

    private static void ApplyAreaMarkerBrightness(GameObject segment, float brightness)
    {
        var renderers = segment.GetComponentsInChildren<Renderer>(true);
        for (var index = 0; index < renderers.Length; index++)
        {
            var renderer = renderers[index];
            if (renderer == null)
            {
                continue;
            }

            var sharedMaterial = renderer.sharedMaterial;
            if (sharedMaterial == null)
            {
                continue;
            }

            AreaMarkerPropertyBlock.Clear();
            renderer.GetPropertyBlock(AreaMarkerPropertyBlock);
            var applied = false;
            for (var propertyIndex = 0; propertyIndex < AreaMarkerColorProperties.Length; propertyIndex++)
            {
                var colorProperty = AreaMarkerColorProperties[propertyIndex];
                if (!sharedMaterial.HasProperty(colorProperty))
                {
                    continue;
                }

                var color = sharedMaterial.GetColor(colorProperty);
                color.r *= brightness;
                color.g *= brightness;
                color.b *= brightness;
                AreaMarkerPropertyBlock.SetColor(colorProperty, color);
                applied = true;
            }

            if (applied)
            {
                renderer.SetPropertyBlock(AreaMarkerPropertyBlock);
            }
        }
    }

    private static void ApplyManagedAreaMarkerVisibility(PrivateArea area, bool visible)
    {
        if (visible)
        {
            area.CancelInvoke(nameof(PrivateArea.HideMarker));
        }

        var markerObject = area.m_areaMarker.gameObject;
        if (markerObject.activeSelf != visible)
        {
            markerObject.SetActive(visible);
        }
    }

    private static bool ShouldRefreshAreaMarkerVisuals(PrivateArea area, CircleProjector marker, WardConfiguration configuration)
    {
        if (!TryBuildAreaMarkerVisualState(marker, configuration, out var visualState))
        {
            ManagedWardRuntimeContexts.ClearAreaMarkerVisualState(area);
            return false;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        return !context.HasAreaMarkerVisualState ||
               !AreaMarkerVisualStatesMatch(context.AreaMarkerVisualState, visualState);
    }

    private static void CacheAreaMarkerVisualState(PrivateArea area, CircleProjector marker, WardConfiguration configuration)
    {
        if (!TryBuildAreaMarkerVisualState(marker, configuration, out var visualState))
        {
            ManagedWardRuntimeContexts.ClearAreaMarkerVisualState(area);
            return;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        context.AreaMarkerVisualState = visualState;
        context.HasAreaMarkerVisualState = true;
    }

    private static bool TryBuildAreaMarkerVisualState(
        CircleProjector marker,
        WardConfiguration configuration,
        out CachedAreaMarkerVisualState visualState)
    {
        visualState = default;
        var segments = marker.m_segments;
        if (segments == null || segments.Count == 0)
        {
            return false;
        }

        var firstSegment = segments[0];
        var lastSegment = segments[segments.Count - 1];
        visualState = new CachedAreaMarkerVisualState(
            marker.GetInstanceID(),
            segments.Count,
            firstSegment != null ? firstSegment.GetInstanceID() : 0,
            lastSegment != null ? lastSegment.GetInstanceID() : 0,
            MaxRadius,
            configuration.Radius);
        return true;
    }

    private static bool AreaMarkerVisualStatesMatch(CachedAreaMarkerVisualState left, CachedAreaMarkerVisualState right)
    {
        return left.MarkerInstanceId == right.MarkerInstanceId &&
               left.SegmentCount == right.SegmentCount &&
               left.FirstSegmentInstanceId == right.FirstSegmentInstanceId &&
               left.LastSegmentInstanceId == right.LastSegmentInstanceId &&
               Mathf.Approximately(left.MaxRadius, right.MaxRadius) &&
               Mathf.Approximately(left.Radius, right.Radius);
    }

    private static long AllocateConfigurationRequestId()
    {
        if (_nextConfigurationRequestId == long.MaxValue)
        {
            _nextConfigurationRequestId = 1L;
        }

        return _nextConfigurationRequestId++;
    }

    private static bool TryReadConfigurationRequest(ZPackage? pkg, out long requestId, out WardConfiguration configuration)
    {
        requestId = 0L;
        configuration = default;
        if (pkg == null)
        {
            return false;
        }

        try
        {
            requestId = pkg.ReadLong();
            return requestId != 0L && TryReadConfigurationPayload(pkg, out configuration);
        }
        catch
        {
            requestId = 0L;
            configuration = default;
            return false;
        }
    }

    private static bool TryReadWardZdoId(ZPackage? pkg, out ZDOID wardZdoId)
    {
        wardZdoId = ZDOID.None;
        if (pkg == null)
        {
            return false;
        }

        try
        {
            wardZdoId = pkg.ReadZDOID();
            return !wardZdoId.IsNone();
        }
        catch
        {
            wardZdoId = ZDOID.None;
            return false;
        }
    }

    private static bool TryReadRemovePermittedRequest(ZPackage? pkg, out long targetPlayerId)
    {
        targetPlayerId = 0L;
        if (pkg == null)
        {
            return false;
        }

        try
        {
            targetPlayerId = pkg.ReadLong();
            return targetPlayerId != 0L;
        }
        catch
        {
            targetPlayerId = 0L;
            return false;
        }
    }

    private static void SendRoutedUpdateConfigurationResponse(
        long receiverUid,
        ZDOID wardZdoId,
        long requestId,
        WardConfigurationUpdateResult result)
    {
        if (receiverUid == 0L || wardZdoId.IsNone())
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(wardZdoId);
        pkg.Write(requestId);
        pkg.Write((int)result.ResultCode);
        WriteConfigurationPayload(pkg, result.Configuration);
        ZRoutedRpc.instance?.InvokeRoutedRPC(receiverUid, RpcUpdateSettingsResponse, pkg);
    }

    private static bool TryReadConfigurationResponse(
        PrivateArea area,
        ZPackage? pkg,
        out long requestId,
        out WardConfigurationRequestResultCode resultCode,
        out WardConfiguration configuration)
    {
        requestId = 0L;
        resultCode = WardConfigurationRequestResultCode.InvalidState;
        configuration = GetConfiguration(area);
        if (pkg == null)
        {
            return false;
        }

        try
        {
            requestId = pkg.ReadLong();
            resultCode = (WardConfigurationRequestResultCode)pkg.ReadInt();
            return TryReadConfigurationPayload(pkg, out configuration);
        }
        catch
        {
            requestId = 0L;
            resultCode = WardConfigurationRequestResultCode.InvalidState;
            configuration = GetConfiguration(area);
            return false;
        }
    }

    private static bool TryReadConfigurationPayload(ZPackage pkg, out WardConfiguration configuration)
    {
        configuration = default;
        if (pkg == null)
        {
            return false;
        }

        try
        {
            var radius = pkg.ReadSingle();
            var areaMarkerRotationEnabled = pkg.ReadBool();
            var autoCloseEnabled = pkg.ReadBool();
            var warningSoundEnabled = pkg.ReadBool();
            var warningFlashEnabled = pkg.ReadBool();
            var restrictions = (WardRestrictionOptions)pkg.ReadInt();

            return TryCreateConfiguration(
                radius,
                areaMarkerRotationEnabled,
                autoCloseEnabled,
                warningSoundEnabled,
                warningFlashEnabled,
                restrictions,
                out configuration);
        }
        catch
        {
            configuration = default;
            return false;
        }
    }

    internal static void ShowConfigurationRequestFeedback(WardConfigurationRequestResultCode resultCode)
    {
        var player = Player.m_localPlayer;
        if (resultCode == WardConfigurationRequestResultCode.Denied)
        {
            WardAccess.ShowNoAccessMessage(player);
        }
    }

    internal static bool ConfigurationsMatch(WardConfiguration left, WardConfiguration right)
    {
        return Mathf.Approximately(left.Radius, right.Radius) &&
               left.AreaMarkerRotationEnabled == right.AreaMarkerRotationEnabled &&
               left.AutoCloseEnabled == right.AutoCloseEnabled &&
               left.WarningSoundEnabled == right.WarningSoundEnabled &&
               left.WarningFlashEnabled == right.WarningFlashEnabled &&
               left.Restrictions == right.Restrictions;
    }

    private static WardRestrictionOptions ApplyForcedRestrictions(WardRestrictionOptions restrictions)
    {
        return ApplyForcedRestrictions(restrictions, ForcedRestrictions);
    }

    private static WardRestrictionOptions ApplyForcedRestrictions(
        WardRestrictionOptions restrictions,
        WardRestrictionOptions forcedRestrictions)
    {
        return NormalizeRestrictions(restrictions) | forcedRestrictions;
    }

    private static WardRestrictionOptions NormalizeRestrictions(WardRestrictionOptions restrictions)
    {
        return restrictions & WardRestrictionOptions.All;
    }

    private static ZNetView? GetNView(PrivateArea area)
    {
        return WardPrivateAreaSafeAccess.GetNView(area);
    }

    private static ZDO? GetZdo(PrivateArea area)
    {
        return WardPrivateAreaSafeAccess.GetZdo(area);
    }
}
