using System;
using System.Collections.Generic;

namespace STUWard;

internal readonly struct ManagedWardObservedState
{
    internal ManagedWardObservedState(bool enabled, uint dataRevision)
    {
        Enabled = enabled;
        DataRevision = dataRevision;
    }

    internal bool Enabled { get; }
    internal uint DataRevision { get; }
}

internal readonly struct ManagedWardHoverTextCacheEntry
{
    internal ManagedWardHoverTextCacheEntry(
        uint dataRevision,
        string originalText,
        long playerId,
        bool canConfigure,
        bool canAttemptAnyWardControl,
        bool hasSettingsShortcutBinding,
        string shortcutLabel,
        string guildName,
        string finalText)
    {
        DataRevision = dataRevision;
        OriginalText = originalText;
        PlayerId = playerId;
        CanConfigure = canConfigure;
        CanAttemptAnyWardControl = canAttemptAnyWardControl;
        HasSettingsShortcutBinding = hasSettingsShortcutBinding;
        ShortcutLabel = shortcutLabel;
        GuildName = guildName;
        FinalText = finalText;
    }

    internal uint DataRevision { get; }
    internal string OriginalText { get; }
    internal long PlayerId { get; }
    internal bool CanConfigure { get; }
    internal bool CanAttemptAnyWardControl { get; }
    internal bool HasSettingsShortcutBinding { get; }
    internal string ShortcutLabel { get; }
    internal string GuildName { get; }
    internal string FinalText { get; }

    internal bool Matches(ManagedWardHoverTextCacheEntry other)
    {
        return DataRevision == other.DataRevision &&
               string.Equals(OriginalText, other.OriginalText, StringComparison.Ordinal) &&
               PlayerId == other.PlayerId &&
               CanConfigure == other.CanConfigure &&
               CanAttemptAnyWardControl == other.CanAttemptAnyWardControl &&
               HasSettingsShortcutBinding == other.HasSettingsShortcutBinding &&
               string.Equals(ShortcutLabel, other.ShortcutLabel, StringComparison.Ordinal) &&
               string.Equals(GuildName, other.GuildName, StringComparison.Ordinal);
    }

    internal ManagedWardHoverTextCacheEntry WithFinalText(string finalText)
    {
        return new ManagedWardHoverTextCacheEntry(
            DataRevision,
            OriginalText,
            PlayerId,
            CanConfigure,
            CanAttemptAnyWardControl,
            HasSettingsShortcutBinding,
            ShortcutLabel,
            GuildName,
            finalText);
    }
}

internal sealed class ManagedWardRuntimeContext
{
    internal bool NetworkInitializationComplete;
    internal bool OwnershipObserved;

    internal bool HasDefaultAreaMarkerSpeed;
    internal float DefaultAreaMarkerSpeed;

    internal bool HasCachedConfiguration;
    internal CachedWardConfiguration CachedConfiguration;

    internal bool HasAreaMarkerVisualState;
    internal CachedAreaMarkerVisualState AreaMarkerVisualState;

    internal bool HasObservedState;
    internal ManagedWardObservedState ObservedState;
    internal bool HasPendingEnabledFanOutSuppression;
    internal bool PendingEnabledFanOutState;
    internal bool HasPendingDataRevisionFanOutSuppression;
    internal uint PendingDataRevisionFanOutBaseline;

    internal bool HasHoverText;
    internal ManagedWardHoverTextCacheEntry HoverText;

    internal float WarningEffectCooldownUntil;
    internal float PresenceLastTrustedNearbyTime = float.NegativeInfinity;

    internal void ClearConfigurationCaches()
    {
        HasCachedConfiguration = false;
        HasAreaMarkerVisualState = false;
    }

    internal void ClearAreaMarkerVisualState()
    {
        HasAreaMarkerVisualState = false;
    }

    internal void ClearObservedState()
    {
        HasObservedState = false;
        HasPendingEnabledFanOutSuppression = false;
        HasPendingDataRevisionFanOutSuppression = false;
    }

    internal void ClearHoverText()
    {
        HasHoverText = false;
    }

    internal void ResetPresenceState()
    {
        PresenceLastTrustedNearbyTime = float.NegativeInfinity;
    }
}

internal static class ManagedWardRuntimeContexts
{
    private static readonly Dictionary<int, ManagedWardRuntimeContext> Contexts = new();

    internal static ManagedWardRuntimeContext GetOrCreate(PrivateArea area)
    {
        var instanceId = area.GetInstanceID();
        if (!Contexts.TryGetValue(instanceId, out var context))
        {
            context = new ManagedWardRuntimeContext();
            Contexts[instanceId] = context;
        }

        return context;
    }

    internal static bool TryGet(PrivateArea? area, out ManagedWardRuntimeContext context)
    {
        context = null!;
        return area != null && Contexts.TryGetValue(area.GetInstanceID(), out context);
    }

    internal static void Forget(PrivateArea? area)
    {
        if (area == null)
        {
            return;
        }

        Contexts.Remove(area.GetInstanceID());
    }

    internal static void Reset()
    {
        Contexts.Clear();
    }

    internal static void ClearConfigurationCaches()
    {
        foreach (var context in Contexts.Values)
        {
            context.ClearConfigurationCaches();
        }
    }

    internal static void ClearAreaMarkerVisualState(PrivateArea? area)
    {
        if (TryGet(area, out var context))
        {
            context.ClearAreaMarkerVisualState();
        }
    }

    internal static void ClearObservedState(PrivateArea? area)
    {
        if (TryGet(area, out var context))
        {
            context.ClearObservedState();
        }
    }

    internal static bool TryGetCurrentDataRevision(PrivateArea? area, out uint dataRevision)
    {
        dataRevision = 0u;
        if (area == null)
        {
            return false;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo == null || !zdo.IsValid())
        {
            return false;
        }

        dataRevision = zdo.DataRevision;
        return true;
    }

    internal static void ArmNextEnabledFanOutSuppression(PrivateArea? area, bool expectedEnabled)
    {
        if (area == null)
        {
            return;
        }

        var context = GetOrCreate(area);
        context.HasPendingEnabledFanOutSuppression = true;
        context.PendingEnabledFanOutState = expectedEnabled;
    }

    internal static void ArmNextDataRevisionFanOutSuppression(PrivateArea? area)
    {
        if (!TryGetCurrentDataRevision(area, out var dataRevision))
        {
            return;
        }

        ArmNextDataRevisionFanOutSuppression(area, dataRevision);
    }

    internal static void ArmNextDataRevisionFanOutSuppression(PrivateArea? area, uint baselineDataRevision)
    {
        if (area == null)
        {
            return;
        }

        var context = GetOrCreate(area);
        context.HasPendingDataRevisionFanOutSuppression = true;
        context.PendingDataRevisionFanOutBaseline = baselineDataRevision;
    }

    internal static void ArmNextDataRevisionFanOutSuppressionIfChanged(PrivateArea? area, uint baselineDataRevision)
    {
        if (!TryGetCurrentDataRevision(area, out var currentDataRevision) ||
            currentDataRevision == baselineDataRevision)
        {
            return;
        }

        ArmNextDataRevisionFanOutSuppression(area, baselineDataRevision);
    }

    internal static bool TryConsumeEnabledFanOutSuppression(PrivateArea? area, bool currentEnabled)
    {
        if (!TryGet(area, out var context) || !context.HasPendingEnabledFanOutSuppression)
        {
            return false;
        }

        context.HasPendingEnabledFanOutSuppression = false;
        return context.PendingEnabledFanOutState == currentEnabled;
    }

    internal static bool TryConsumeDataRevisionFanOutSuppression(PrivateArea? area, uint currentDataRevision)
    {
        if (!TryGet(area, out var context) || !context.HasPendingDataRevisionFanOutSuppression)
        {
            return false;
        }

        context.HasPendingDataRevisionFanOutSuppression = false;
        return context.PendingDataRevisionFanOutBaseline != currentDataRevision;
    }

    internal static void ClearHoverText(PrivateArea? area)
    {
        if (TryGet(area, out var context))
        {
            context.ClearHoverText();
        }
    }

    internal static void ResetPresenceState(PrivateArea? area)
    {
        if (TryGet(area, out var context))
        {
            context.ResetPresenceState();
        }
    }

    internal static void ResetPresenceStates()
    {
        foreach (var context in Contexts.Values)
        {
            context.ResetPresenceState();
        }
    }
}

internal static class WardRuntimeStateTracker
{
    internal static void Forget(PrivateArea? area)
    {
        ManagedWardRuntimeContexts.ClearObservedState(area);
    }

    internal static bool TryConsumeChanges(PrivateArea? area, out bool enabledChanged, out bool dataRevisionChanged)
    {
        enabledChanged = false;
        dataRevisionChanged = false;
        if (!TryBuildObservedState(area, out var currentState))
        {
            ManagedWardRuntimeContexts.ClearObservedState(area);
            return false;
        }

        var context = ManagedWardRuntimeContexts.GetOrCreate(area!);
        if (!context.HasObservedState)
        {
            context.ObservedState = currentState;
            context.HasObservedState = true;
            return false;
        }

        var previousState = context.ObservedState;
        enabledChanged = previousState.Enabled != currentState.Enabled;
        dataRevisionChanged = previousState.DataRevision != currentState.DataRevision;
        if (!enabledChanged && !dataRevisionChanged)
        {
            return false;
        }

        context.ObservedState = currentState;
        return true;
    }

    private static bool TryBuildObservedState(PrivateArea? area, out ManagedWardObservedState state)
    {
        state = default;
        if (area == null)
        {
            return false;
        }

        var zdo = WardPrivateAreaSafeAccess.GetZdo(area);
        if (zdo == null)
        {
            return false;
        }

        state = new ManagedWardObservedState(area.IsEnabled(), zdo.DataRevision);
        return true;
    }
}
