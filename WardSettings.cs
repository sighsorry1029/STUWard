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
        bool showAreaMarker,
        float areaMarkerSpeedMultiplier,
        float areaMarkerAlpha,
        float radius,
        float autoCloseDelay,
        bool warningSoundEnabled,
        bool warningFlashEnabled,
        WardRestrictionOptions restrictions = WardRestrictionOptions.All)
    {
        ShowAreaMarker = showAreaMarker;
        AreaMarkerSpeedMultiplier = areaMarkerSpeedMultiplier;
        AreaMarkerAlpha = areaMarkerAlpha;
        Radius = radius;
        AutoCloseDelay = autoCloseDelay;
        WarningSoundEnabled = warningSoundEnabled;
        WarningFlashEnabled = warningFlashEnabled;
        Restrictions = restrictions;
    }

    internal bool ShowAreaMarker { get; }
    internal float AreaMarkerSpeedMultiplier { get; }
    internal float AreaMarkerAlpha { get; }
    internal float Radius { get; }
    internal float AutoCloseDelay { get; }
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
        float radius,
        float areaMarkerAlpha)
    {
        MarkerInstanceId = markerInstanceId;
        SegmentCount = segmentCount;
        FirstSegmentInstanceId = firstSegmentInstanceId;
        LastSegmentInstanceId = lastSegmentInstanceId;
        MaxRadius = maxRadius;
        Radius = radius;
        AreaMarkerAlpha = areaMarkerAlpha;
    }

    internal int MarkerInstanceId { get; }
    internal int SegmentCount { get; }
    internal int FirstSegmentInstanceId { get; }
    internal int LastSegmentInstanceId { get; }
    internal float MaxRadius { get; }
    internal float Radius { get; }
    internal float AreaMarkerAlpha { get; }
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
        WardConfiguration configuration,
        bool showOverlapMessage)
    {
        IsPending = isPending;
        RequestId = requestId;
        ResultCode = resultCode;
        Configuration = configuration;
        ShowOverlapMessage = showOverlapMessage;
    }

    internal bool IsPending { get; }
    internal long RequestId { get; }
    internal WardConfigurationRequestResultCode ResultCode { get; }
    internal WardConfiguration Configuration { get; }
    internal bool ShowOverlapMessage { get; }
}

internal readonly struct WardConfigurationUpdateResult
{
    internal WardConfigurationUpdateResult(
        WardConfigurationRequestResultCode resultCode,
        WardConfiguration configuration,
        bool showOverlapMessage)
    {
        ResultCode = resultCode;
        Configuration = configuration;
        ShowOverlapMessage = showOverlapMessage;
    }

    internal WardConfigurationRequestResultCode ResultCode { get; }
    internal WardConfiguration Configuration { get; }
    internal bool ShowOverlapMessage { get; }
}

internal sealed class WardSettingsRpcRegistrationState : MonoBehaviour
{
}

internal static class WardSettings
{
    internal const int ManagedAreaMarkerSegments = 36;
    private const float ManagedAreaMarkerSegmentLengthMultiplier = 2f;
    internal const float MinAreaMarkerSpeedMultiplier = 0f;
    internal const float MaxAreaMarkerSpeedMultiplier = 1f;
    internal const float DefaultAreaMarkerSpeedMultiplier = 0.5f;
    internal const float MinAreaMarkerAlpha = 0f;
    internal const float MaxAreaMarkerAlpha = 1f;
    internal const float DefaultAreaMarkerAlpha = 0.5f;
    internal const float MinRadius = 8f;
    internal const float MaxRadiusLimit = 64f;
    internal const float DefaultMaxRadius = 32f;
    internal const float MinAutoCloseDelay = 0f;
    internal const float MaxAutoCloseDelay = 10f;
    internal const float DefaultAutoCloseDelay = 4f;
    internal const bool DefaultWarningSoundEnabled = true;
    internal const bool DefaultWarningFlashEnabled = true;

    private const string RpcUpdateSettings = "STUWard_UpdateSettings";
    private const string RpcUpdateSettingsResponse = "STUWard_UpdateSettingsResponse";
    private const string RpcRemovePermitted = "STUWard_RemovePermitted";
    private const string ShowAreaMarkerKey = "stuw_show_area_marker";
    private const string AreaMarkerSpeedMultiplierKey = "stuw_area_marker_speed_multiplier";
    private const string AreaMarkerAlphaKey = "stuw_area_marker_alpha";
    private const string RadiusKey = "stuw_radius";
    private const string AutoCloseDelayKey = "stuw_auto_close_delay";
    private const string WarningSoundEnabledKey = "stuw_warning_sound_enabled";
    private const string WarningFlashEnabledKey = "stuw_warning_flash_enabled";
    private const string RestrictionOptionsKey = "stuw_restriction_options";
    private const float FallbackAreaMarkerSpeed = 0.1f;
    private const float MinimumAreaMarkerBrightness = 0.35f;
    private const float AreaMarkerBrightnessGamma = 1.8f;
    private const float MinimumAreaMarkerBrightnessInput = 0.5f;
    private static readonly string[] AreaMarkerColorProperties = { "_Color", "_BaseColor", "_TintColor" };

    private static readonly MaterialPropertyBlock AreaMarkerPropertyBlock = new();
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

    internal static WardConfiguration WithAreaMarkerSpeedMultiplier(WardConfiguration configuration, float value)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            Mathf.Clamp(value, MinAreaMarkerSpeedMultiplier, MaxAreaMarkerSpeedMultiplier),
            configuration.AreaMarkerAlpha,
            configuration.Radius,
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            configuration.Restrictions);
    }

    internal static WardConfiguration WithAreaMarkerAlpha(WardConfiguration configuration, float value)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            Mathf.Clamp(value, MinAreaMarkerAlpha, MaxAreaMarkerAlpha),
            configuration.Radius,
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            configuration.Restrictions);
    }

    internal static WardConfiguration WithRadius(WardConfiguration configuration, float value)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            Mathf.Clamp(value, MinRadius, MaxRadius),
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            configuration.Restrictions);
    }

    internal static WardConfiguration WithAutoCloseDelay(WardConfiguration configuration, float value)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            configuration.Radius,
            Mathf.Clamp(value, MinAutoCloseDelay, MaxAutoCloseDelay),
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            configuration.Restrictions);
    }

    internal static WardConfiguration WithRestrictions(WardConfiguration configuration, WardRestrictionOptions restrictions)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            configuration.Radius,
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            NormalizeRestrictions(restrictions));
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

    internal static void CaptureAreaDefaults(PrivateArea area)
    {
        var marker = area.m_areaMarker;
        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        context.DefaultAreaMarkerSpeed = marker != null ? Mathf.Max(marker.m_speed, 0f) : FallbackAreaMarkerSpeed;
        context.HasDefaultAreaMarkerSpeed = true;
    }

    internal static void InitializeArea(PrivateArea area)
    {
        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        if (!context.HasDefaultAreaMarkerSpeed)
        {
            CaptureAreaDefaults(area);
        }
    }

    internal static void HandleMaxRadiusChanged()
    {
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

        ManagedWardMapStateService.InvalidateProjection("max ward radius config changed");
    }

    internal static WardConfiguration GetConfiguration(PrivateArea area)
    {
        InitializeArea(area);
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

        const float defaultRadius = MinRadius;

        var showAreaMarker = zdo?.GetBool(ShowAreaMarkerKey, true) ?? true;
        var areaMarkerSpeedMultiplier = Mathf.Clamp01(
            zdo?.GetFloat(AreaMarkerSpeedMultiplierKey, DefaultAreaMarkerSpeedMultiplier) ?? DefaultAreaMarkerSpeedMultiplier);
        var areaMarkerAlpha = Mathf.Clamp01(
            zdo?.GetFloat(AreaMarkerAlphaKey, DefaultAreaMarkerAlpha) ?? DefaultAreaMarkerAlpha);
        var radius = Mathf.Clamp(zdo?.GetFloat(RadiusKey, defaultRadius) ?? defaultRadius, MinRadius, maxRadius);
        var autoCloseDelay = Mathf.Clamp(
            zdo?.GetFloat(AutoCloseDelayKey, DefaultAutoCloseDelay) ?? DefaultAutoCloseDelay,
            MinAutoCloseDelay,
            MaxAutoCloseDelay);
        var warningSoundEnabled = zdo?.GetBool(WarningSoundEnabledKey, DefaultWarningSoundEnabled) ?? DefaultWarningSoundEnabled;
        var warningFlashEnabled = zdo?.GetBool(WarningFlashEnabledKey, DefaultWarningFlashEnabled) ?? DefaultWarningFlashEnabled;
        var restrictions = ApplyForcedRestrictions(
            NormalizeRestrictions((WardRestrictionOptions)(zdo?.GetInt(RestrictionOptionsKey, (int)WardRestrictionOptions.All) ?? (int)WardRestrictionOptions.All)),
            forcedRestrictions);

        var configuration = new WardConfiguration(
            showAreaMarker,
            areaMarkerSpeedMultiplier,
            areaMarkerAlpha,
            radius,
            autoCloseDelay,
            warningSoundEnabled,
            warningFlashEnabled,
            restrictions);
        if (zdo != null)
        {
            var context = ManagedWardRuntimeContexts.GetOrCreate(area);
            context.CachedConfiguration = new CachedWardConfiguration(zdo.DataRevision, maxRadius, forcedRestrictions, configuration);
            context.HasCachedConfiguration = true;
        }

        return configuration;
    }

    internal static void ApplyAreaState(PrivateArea area)
    {
        ApplyAreaState(ManagedWardRef.FromArea(area));
    }

    internal static void ApplyAreaState(PrivateArea area, WardConfiguration configuration)
    {
        ApplyAreaState(ManagedWardRef.FromArea(area), configuration);
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

        InitializeArea(area);
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
            var showAreaMarker = ShouldShowAreaMarker(area, configuration);
            var desiredSpeed = GetDefaultAreaMarkerSpeed(area) * configuration.AreaMarkerSpeedMultiplier;
            if (marker.m_nrOfSegments != ManagedAreaMarkerSegments)
            {
                marker.m_nrOfSegments = ManagedAreaMarkerSegments;
            }

            if (!Mathf.Approximately(marker.m_radius, configuration.Radius))
            {
                marker.m_radius = configuration.Radius;
            }

            if (!Mathf.Approximately(marker.m_speed, desiredSpeed))
            {
                marker.m_speed = desiredSpeed;
            }

            ApplyManagedAreaMarkerVisibility(area, showAreaMarker);
            if (ShouldRefreshAreaMarkerVisuals(area, marker, configuration))
            {
                ApplyAreaMarkerVisuals(marker, configuration);
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

        InitializeArea(area);
        var previewRadius = MaxRadius;
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
    }

    internal static bool ShouldShowAreaMarker(PrivateArea area)
    {
        return ShouldShowAreaMarker(area, GetConfiguration(area));
    }

    internal static bool ShouldShowAreaMarker(PrivateArea area, WardConfiguration configuration)
    {
        return Player.IsPlacementGhost(area.gameObject) || (area.IsEnabled() && configuration.ShowAreaMarker);
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
        return Mathf.Clamp(zdo?.GetFloat(RadiusKey, defaultRadius) ?? defaultRadius, MinRadius, MaxRadius);
    }

    internal static bool TryGetAutoCloseDoorDelay(Vector3 point, out float delay)
    {
        var allAreas = WardAccess.GetCandidateManagedWards(point, 0f, requireEnabled: true);
        if (allAreas.Count == 0)
        {
            delay = 0f;
            return false;
        }

        var found = false;
        var selectedDelay = float.MaxValue;

        foreach (var area in allAreas)
        {
            if (area == null || !area.IsInside(point, 0f))
            {
                continue;
            }

            var configuration = GetConfiguration(area);
            if (configuration.AutoCloseDelay <= 0f)
            {
                continue;
            }

            found = true;
            if (configuration.AutoCloseDelay < selectedDelay)
            {
                selectedDelay = configuration.AutoCloseDelay;
            }
        }

        delay = found ? selectedDelay : 0f;
        return found;
    }

    internal static bool HandleManagedFlashEffect(PrivateArea area)
    {
        if (!WardAccess.IsManagedWard(area, false) || area.m_flashEffect == null)
        {
            return true;
        }

        var configuration = GetConfiguration(area);
        if (configuration.WarningFlashEnabled && configuration.WarningSoundEnabled)
        {
            return true;
        }

        if (!configuration.WarningFlashEnabled && !configuration.WarningSoundEnabled)
        {
            return false;
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
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            configuration.Radius,
            configuration.AutoCloseDelay,
            enabled,
            configuration.WarningFlashEnabled,
            configuration.Restrictions);
    }

    internal static WardConfiguration WithWarningFlashEnabled(WardConfiguration configuration, bool enabled)
    {
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            configuration.Radius,
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            enabled,
            configuration.Restrictions);
    }

    internal static float GetMaxNonOverlappingRadius(PrivateArea area)
    {
        return Mathf.Max(MinRadius, WardAccess.GetMaxNonOverlappingRadius(area, MaxRadius));
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
                currentConfiguration,
                showOverlapMessage: false);
        }

        if (WardOwnership.CanApplyManagedWardStateLocally(nview))
        {
            if (!CanControlWard(area, player.GetPlayerID()))
            {
                return new WardConfigurationRequestSubmission(
                    isPending: false,
                    requestId: 0L,
                    WardConfigurationRequestResultCode.Denied,
                    currentConfiguration,
                    showOverlapMessage: false);
            }

            var localResult = ProcessConfigurationUpdate(area, configuration, currentConfiguration);
            return new WardConfigurationRequestSubmission(
                isPending: false,
                requestId: 0L,
                localResult.ResultCode,
                localResult.Configuration,
                localResult.ShowOverlapMessage);
        }

        var requestId = AllocateConfigurationRequestId();
        var requestPackage = new ZPackage();
        requestPackage.Write(requestId);
        WriteConfiguration(requestPackage, configuration);
        if (!WardOwnership.TryInvokeManagedWardStateRpcOnServer(nview, RpcUpdateSettings, requestPackage))
        {
            Plugin.LogWardDiagnosticFailure(
                "UpdateSettings.Send",
                $"Failed to route per-ward UpdateSettings RPC to the server. playerId={player.GetPlayerID()}, requestId={requestId}, {WardDiagnosticInfo.DescribeWard(area)}");
            return new WardConfigurationRequestSubmission(
                isPending: false,
                requestId: 0L,
                WardConfigurationRequestResultCode.InvalidState,
                currentConfiguration,
                showOverlapMessage: false);
        }

        Plugin.LogWardDiagnosticVerbose(
            "UpdateSettings.Send",
            $"Sent per-ward UpdateSettings RPC to the server. playerId={player.GetPlayerID()}, requestId={requestId}, {WardDiagnosticInfo.DescribeWard(area)}");
        return new WardConfigurationRequestSubmission(
            isPending: true,
            requestId: requestId,
            WardConfigurationRequestResultCode.Applied,
            currentConfiguration,
            showOverlapMessage: false);
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
            if (!CanControlWard(area, player.GetPlayerID()))
            {
                return;
            }

            var hadBaselineRevision = ManagedWardRuntimeContexts.TryGetCurrentDataRevision(area, out var baselineDataRevision);
            area.RemovePermitted(targetPlayerId);
            if (hadBaselineRevision)
            {
                ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppressionIfChanged(area, baselineDataRevision);
            }

            return;
        }

        var requestPackage = new ZPackage();
        requestPackage.Write(targetPlayerId);
        if (!WardOwnership.TryInvokeManagedWardStateRpcOnServer(nview, RpcRemovePermitted, requestPackage))
        {
            Plugin.LogWardDiagnosticFailure(
                "RemovePermitted.Send",
                $"Failed to route per-ward RemovePermitted RPC to the server. playerId={player.GetPlayerID()}, targetPlayerId={targetPlayerId}, {WardDiagnosticInfo.DescribeWard(area)}");
            return;
        }

        Plugin.LogWardDiagnosticVerbose(
            "RemovePermitted.Send",
            $"Sent per-ward RemovePermitted RPC to the server. playerId={player.GetPlayerID()}, targetPlayerId={targetPlayerId}, {WardDiagnosticInfo.DescribeWard(area)}");
    }

    internal static void RegisterRpcHandlers(PrivateArea area)
    {
        RegisterRpcHandlers(ManagedWardRef.FromArea(area));
    }

    internal static void RegisterRpcHandlers(ManagedWardRef ward)
    {
        var area = ward.Area;
        if (area == null)
        {
            return;
        }

        var nview = ward.NView ?? GetNView(area);
        if (nview == null || !nview.IsValid())
        {
            return;
        }

        if (area.GetComponent<WardSettingsRpcRegistrationState>() != null)
        {
            return;
        }

        area.gameObject.AddComponent<WardSettingsRpcRegistrationState>();

        nview.Register<ZPackage>(RpcUpdateSettings, (sender, pkg) =>
        {
            HandleUpdateConfiguration(area, sender, pkg);
        });
        nview.Register<ZPackage>(RpcUpdateSettingsResponse, (sender, pkg) =>
        {
            HandleUpdateConfigurationResponse(area, sender, pkg);
        });
        nview.Register<ZPackage>(RpcRemovePermitted, (sender, pkg) =>
        {
            HandleRemovePermitted(area, sender, pkg);
        });
    }

    private static void HandleUpdateConfiguration(PrivateArea area, long sender, ZPackage pkg)
    {
        var nview = GetNView(area);
        if (nview == null || !WardOwnership.CanHandleManagedWardStateRpc(nview))
        {
            return;
        }

        var currentConfiguration = GetConfiguration(area);

        if (!TryReadConfigurationRequest(pkg, out var requestId, out var configuration))
        {
            SendUpdateConfigurationResponse(
                nview,
                sender,
                0L,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.InvalidPayload,
                    currentConfiguration,
                    showOverlapMessage: false));
            return;
        }

        if (!WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, "UpdateSettings.Request", out var requesterId) ||
            !CanControlWard(area, requesterId))
        {
            SendUpdateConfigurationResponse(
                nview,
                sender,
                requestId,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.Denied,
                    currentConfiguration,
                    showOverlapMessage: false));
            return;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(area, "UpdateSettings.Request"))
        {
            SendUpdateConfigurationResponse(
                nview,
                sender,
                requestId,
                new WardConfigurationUpdateResult(
                    WardConfigurationRequestResultCode.InvalidState,
                    currentConfiguration,
                    showOverlapMessage: false));
            return;
        }

        var result = ProcessConfigurationUpdate(area, configuration, currentConfiguration);
        SendUpdateConfigurationResponse(nview, sender, requestId, result);
    }

    private static void HandleUpdateConfigurationResponse(PrivateArea area, long sender, ZPackage pkg)
    {
        if (!WardOwnership.IsAuthoritativeServerSender(sender))
        {
            Plugin.LogWardDiagnosticFailure(
                "UpdateSettings.Response",
                $"Rejected ward configuration response from an unauthorized sender. sender={sender}, {WardDiagnosticInfo.DescribeWard(area)}");
            return;
        }

        if (!TryReadConfigurationResponse(area, pkg, out var requestId, out var resultCode, out var configuration, out var showOverlapMessage))
        {
            return;
        }

        ShowConfigurationRequestFeedback(resultCode, showOverlapMessage);
        WardGuiController.Instance?.HandleWardConfigurationResponse(area, requestId, resultCode, configuration);
    }

    private static void HandleRemovePermitted(PrivateArea area, long sender, ZPackage? pkg)
    {
        var nview = GetNView(area);
        if (!TryReadRemovePermittedRequest(pkg, out var targetPlayerId))
        {
            return;
        }

        if (nview == null || !WardOwnership.CanHandleManagedWardStateRpc(nview) ||
            !WardOwnership.TryResolveAuthoritativePlayerIdFromSender(sender, "RemovePermitted.Request", out var requesterId) ||
            !CanControlWard(area, requesterId))
        {
            return;
        }

        if (!WardOwnership.TryClaimManagedWardMutationOwnership(area, "RemovePermitted.Request"))
        {
            return;
        }

        var hadBaselineRevision = ManagedWardRuntimeContexts.TryGetCurrentDataRevision(area, out var baselineDataRevision);
        area.RemovePermitted(targetPlayerId);
        if (hadBaselineRevision)
        {
            ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppressionIfChanged(area, baselineDataRevision);
        }
    }

    private static bool TryCreateConfiguration(
        bool showAreaMarker,
        float areaMarkerSpeedMultiplier,
        float areaMarkerAlpha,
        float radius,
        float autoCloseDelay,
        bool warningSoundEnabled,
        bool warningFlashEnabled,
        WardRestrictionOptions restrictions,
        out WardConfiguration configuration)
    {
        configuration = default;
        if (float.IsNaN(areaMarkerSpeedMultiplier) || float.IsInfinity(areaMarkerSpeedMultiplier) ||
            float.IsNaN(areaMarkerAlpha) || float.IsInfinity(areaMarkerAlpha) ||
            float.IsNaN(radius) || float.IsInfinity(radius) ||
            float.IsNaN(autoCloseDelay) || float.IsInfinity(autoCloseDelay))
        {
            return false;
        }

        configuration = new WardConfiguration(
            showAreaMarker,
            Mathf.Clamp01(areaMarkerSpeedMultiplier),
            Mathf.Clamp01(areaMarkerAlpha),
            Mathf.Clamp(radius, MinRadius, MaxRadius),
            Mathf.Clamp(autoCloseDelay, MinAutoCloseDelay, MaxAutoCloseDelay),
            warningSoundEnabled,
            warningFlashEnabled,
            NormalizeRestrictions(restrictions));
        return true;
    }

    private static void WriteConfiguration(ZPackage pkg, WardConfiguration configuration)
    {
        pkg.Write(configuration.ShowAreaMarker);
        pkg.Write(configuration.AreaMarkerSpeedMultiplier);
        pkg.Write(configuration.AreaMarkerAlpha);
        pkg.Write(configuration.Radius);
        pkg.Write(configuration.AutoCloseDelay);
        pkg.Write(configuration.WarningSoundEnabled);
        pkg.Write(configuration.WarningFlashEnabled);
        pkg.Write((int)configuration.Restrictions);
    }

    private static void SaveConfiguration(PrivateArea area, WardConfiguration currentConfiguration, WardConfiguration configuration)
    {
        var zdo = GetZdo(area);
        if (zdo == null)
        {
            return;
        }

        if (currentConfiguration.ShowAreaMarker != configuration.ShowAreaMarker)
        {
            zdo.Set(ShowAreaMarkerKey, configuration.ShowAreaMarker);
        }

        if (!Mathf.Approximately(currentConfiguration.AreaMarkerSpeedMultiplier, configuration.AreaMarkerSpeedMultiplier))
        {
            zdo.Set(AreaMarkerSpeedMultiplierKey, configuration.AreaMarkerSpeedMultiplier);
        }

        if (!Mathf.Approximately(currentConfiguration.AreaMarkerAlpha, configuration.AreaMarkerAlpha))
        {
            zdo.Set(AreaMarkerAlphaKey, configuration.AreaMarkerAlpha);
        }

        if (!Mathf.Approximately(currentConfiguration.Radius, configuration.Radius))
        {
            zdo.Set(RadiusKey, configuration.Radius);
        }

        if (!Mathf.Approximately(currentConfiguration.AutoCloseDelay, configuration.AutoCloseDelay))
        {
            zdo.Set(AutoCloseDelayKey, configuration.AutoCloseDelay);
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

        var context = ManagedWardRuntimeContexts.GetOrCreate(area);
        context.CachedConfiguration = new CachedWardConfiguration(zdo.DataRevision, MaxRadius, ForcedRestrictions, configuration);
        context.HasCachedConfiguration = true;
    }

    private static WardConfigurationUpdateResult ProcessConfigurationUpdate(
        PrivateArea area,
        WardConfiguration requestedConfiguration,
        WardConfiguration currentConfiguration)
    {
        var configuration = ClampConfiguration(area, requestedConfiguration);
        var radiusChanged = !Mathf.Approximately(currentConfiguration.Radius, configuration.Radius);
        var showOverlapMessage = configuration.Radius < requestedConfiguration.Radius;
        if (ConfigurationsMatch(currentConfiguration, configuration))
        {
            return new WardConfigurationUpdateResult(
                WardConfigurationRequestResultCode.Unchanged,
                currentConfiguration,
                showOverlapMessage);
        }

        ManagedWardRuntimeContexts.ArmNextDataRevisionFanOutSuppression(area);
        SaveConfiguration(area, currentConfiguration, configuration);
        var ward = ManagedWardRef.FromArea(area);
        ApplyAreaState(ward, configuration);
        if (radiusChanged)
        {
            ManagedWardMapStateService.NotifyLiveWardMutation(
                area,
                "ward radius updated");
            WardOwnership.ForceSyncManagedWardZdoToServer(ward, "UpdateSettings.Sync");
        }

        return new WardConfigurationUpdateResult(
            WardConfigurationRequestResultCode.Applied,
            configuration,
            showOverlapMessage);
    }

    private static WardConfiguration ClampConfiguration(PrivateArea area, WardConfiguration configuration)
    {
        var maxRadius = WardAccess.GetMaxNonOverlappingRadius(area, MaxRadius);
        var clampedRadius = Mathf.Clamp(Mathf.Min(configuration.Radius, maxRadius), MinRadius, MaxRadius);
        return new WardConfiguration(
            configuration.ShowAreaMarker,
            configuration.AreaMarkerSpeedMultiplier,
            configuration.AreaMarkerAlpha,
            clampedRadius,
            configuration.AutoCloseDelay,
            configuration.WarningSoundEnabled,
            configuration.WarningFlashEnabled,
            ApplyForcedRestrictions(configuration.Restrictions));
    }

    private static void ApplyAreaMarkerVisuals(CircleProjector marker, WardConfiguration configuration)
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

        for (var index = 0; index < segments.Count; index++)
        {
            var segment = segments[index];
            if (segment == null)
            {
                continue;
            }

            segment.transform.localScale = ScaleMarkerSegment(baseScale, lengthScale);
            ApplyAreaMarkerAlpha(segment, configuration.AreaMarkerAlpha);
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

    private static void ApplyAreaMarkerAlpha(GameObject segment, float alpha)
    {
        var normalizedAlpha = Mathf.Clamp01(alpha);
        var remappedAlpha = Mathf.Lerp(MinimumAreaMarkerBrightnessInput, 1f, normalizedAlpha);
        var brightness = Mathf.Lerp(
            MinimumAreaMarkerBrightness,
            1f,
            Mathf.Pow(remappedAlpha, AreaMarkerBrightnessGamma));

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
            configuration.Radius,
            configuration.AreaMarkerAlpha);
        return true;
    }

    private static bool AreaMarkerVisualStatesMatch(CachedAreaMarkerVisualState left, CachedAreaMarkerVisualState right)
    {
        return left.MarkerInstanceId == right.MarkerInstanceId &&
               left.SegmentCount == right.SegmentCount &&
               left.FirstSegmentInstanceId == right.FirstSegmentInstanceId &&
               left.LastSegmentInstanceId == right.LastSegmentInstanceId &&
               Mathf.Approximately(left.MaxRadius, right.MaxRadius) &&
               Mathf.Approximately(left.Radius, right.Radius) &&
               Mathf.Approximately(left.AreaMarkerAlpha, right.AreaMarkerAlpha);
    }

    private static float GetDefaultAreaMarkerSpeed(PrivateArea area)
    {
        return ManagedWardRuntimeContexts.TryGet(area, out var context) && context.HasDefaultAreaMarkerSpeed
            ? context.DefaultAreaMarkerSpeed
            : FallbackAreaMarkerSpeed;
    }

    private static bool CanControlWard(PrivateArea area, long playerId)
    {
        return WardAccess.CanControlManagedWard(area, playerId);
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

    private static void SendUpdateConfigurationResponse(
        ZNetView? nview,
        long receiverUid,
        long requestId,
        WardConfigurationUpdateResult result)
    {
        if (nview == null || receiverUid == 0L)
        {
            return;
        }

        var pkg = new ZPackage();
        pkg.Write(requestId);
        pkg.Write((int)result.ResultCode);
        pkg.Write(result.ShowOverlapMessage);
        WriteConfiguration(pkg, result.Configuration);
        nview.InvokeRPC(receiverUid, RpcUpdateSettingsResponse, new object[] { pkg });
    }

    private static bool TryReadConfigurationResponse(
        PrivateArea area,
        ZPackage? pkg,
        out long requestId,
        out WardConfigurationRequestResultCode resultCode,
        out WardConfiguration configuration,
        out bool showOverlapMessage)
    {
        requestId = 0L;
        resultCode = WardConfigurationRequestResultCode.InvalidState;
        configuration = GetConfiguration(area);
        showOverlapMessage = false;
        if (pkg == null)
        {
            return false;
        }

        try
        {
            requestId = pkg.ReadLong();
            resultCode = (WardConfigurationRequestResultCode)pkg.ReadInt();
            showOverlapMessage = pkg.ReadBool();
            return TryReadConfigurationPayload(pkg, out configuration);
        }
        catch
        {
            requestId = 0L;
            resultCode = WardConfigurationRequestResultCode.InvalidState;
            configuration = GetConfiguration(area);
            showOverlapMessage = false;
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
            var showAreaMarker = pkg.ReadBool();
            var areaMarkerSpeedMultiplier = pkg.ReadSingle();
            var areaMarkerAlpha = pkg.ReadSingle();
            var radius = pkg.ReadSingle();
            var autoCloseDelay = pkg.ReadSingle();
            var warningSoundEnabled = pkg.ReadBool();
            var warningFlashEnabled = pkg.ReadBool();
            var restrictions = (WardRestrictionOptions)pkg.ReadInt();
            return TryCreateConfiguration(
                showAreaMarker,
                areaMarkerSpeedMultiplier,
                areaMarkerAlpha,
                radius,
                autoCloseDelay,
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

    internal static void ShowConfigurationRequestFeedback(
        WardConfigurationRequestResultCode resultCode,
        bool showOverlapMessage)
    {
        var player = Player.m_localPlayer;
        if (showOverlapMessage)
        {
            WardAccess.ShowWardOverlapMessage(player);
        }

        if (resultCode == WardConfigurationRequestResultCode.Denied)
        {
            WardAccess.ShowNoAccessMessage(player);
        }
    }

    internal static bool ConfigurationsMatch(WardConfiguration left, WardConfiguration right)
    {
        return left.ShowAreaMarker == right.ShowAreaMarker &&
               Mathf.Approximately(left.AreaMarkerSpeedMultiplier, right.AreaMarkerSpeedMultiplier) &&
               Mathf.Approximately(left.AreaMarkerAlpha, right.AreaMarkerAlpha) &&
               Mathf.Approximately(left.Radius, right.Radius) &&
               Mathf.Approximately(left.AutoCloseDelay, right.AutoCloseDelay) &&
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
