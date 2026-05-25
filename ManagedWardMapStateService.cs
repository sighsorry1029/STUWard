using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal enum ManagedWardMapMutationKind
{
    IndexOnly,
    PinsOnly,
    IndexAndPins
}

internal static class ManagedWardMapStateService
{
    private enum ManagedWardMapEventKind
    {
        LiveWardMutation,
        ZdoWardMutation,
        WardObserved,
        WardRemoved,
        SyncedWardState,
        ViewerProjectionChanged,
        ProjectionInvalidated,
        LocalDisplayRefreshRequested,
        DisplayRefreshRequested
    }

    private sealed class ManagedWardMapEvent
    {
        internal ManagedWardMapEvent(ManagedWardMapEventKind kind, string reason)
        {
            Kind = kind;
            Reason = reason ?? string.Empty;
        }

        internal ManagedWardMapEventKind Kind { get; }
        internal string Reason { get; }
        internal PrivateArea? Area { get; set; }
        internal ZDO? Zdo { get; set; }
        internal ZDOID ZdoId { get; set; }
        internal ManagedWardMapMutationKind MutationKind { get; set; }
        internal bool LiveDisplayRefresh { get; set; }
        internal bool RefreshImmediatelyIfVisible { get; set; }
        internal bool FullRefresh { get; set; }
        internal HashSet<long>? RecipientPeerUids { get; set; }
        internal uint DataRevision { get; set; }
        internal long OwnerPlayerId { get; set; }
        internal int WardGuildId { get; set; }
        internal Vector3 Position { get; set; }
        internal float Radius { get; set; }
        internal bool IsEnabled { get; set; }
        internal long[] PermittedPlayerIds { get; set; } = [];
    }

    // Map refresh ownership rules:
    // - If a canonical live mutator exists (SetEnabled, AddPermitted, RemovePermitted), call NotifyLiveWardMutation from that mutator path only.
    // - If only ZDO data changed or the caller operates without a live area instance, call NotifyZdoWardMutation.
    // - UI/request/response flows that only need redraws without index mutation should call RequestDisplayRefresh.
    internal static void NotifyLiveWardMutation(
        PrivateArea? area,
        ManagedWardMapMutationKind kind,
        string reason,
        bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.LiveWardMutation, reason)
        {
            Area = area,
            MutationKind = kind,
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void NotifyZdoWardMutation(
        ZDO? zdo,
        ManagedWardMapMutationKind kind,
        string reason,
        bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.ZdoWardMutation, reason)
        {
            Zdo = zdo,
            MutationKind = kind,
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void NotifyWardObserved(ZDO? zdo, string reason, bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.WardObserved, reason)
        {
            Zdo = zdo,
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void NotifyWardRemoved(ZDOID zdoId, string reason, bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.WardRemoved, reason)
        {
            ZdoId = zdoId,
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void NotifySyncedWardState(
        ZDOID zdoId,
        uint dataRevision,
        long ownerPlayerId,
        int wardGuildId,
        Vector3 position,
        float radius,
        bool isEnabled,
        long[] permittedPlayerIds,
        string reason,
        bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.SyncedWardState, reason)
        {
            ZdoId = zdoId,
            DataRevision = dataRevision,
            OwnerPlayerId = ownerPlayerId,
            WardGuildId = wardGuildId,
            Position = position,
            Radius = radius,
            IsEnabled = isEnabled,
            PermittedPlayerIds = permittedPlayerIds ?? [],
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void NotifyViewerProjectionChanged(
        string reason,
        bool fullRefresh,
        HashSet<long>? recipientPeerUids = null,
        bool refreshImmediatelyIfVisible = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.ViewerProjectionChanged, reason)
        {
            FullRefresh = fullRefresh,
            RecipientPeerUids = recipientPeerUids,
            RefreshImmediatelyIfVisible = refreshImmediatelyIfVisible
        });
    }

    internal static void InvalidateProjection(string reason, bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.ProjectionInvalidated, reason)
        {
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    internal static void RequestLocalDisplayRefresh(string reason, bool refreshImmediatelyIfVisible = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.LocalDisplayRefreshRequested, reason)
        {
            RefreshImmediatelyIfVisible = refreshImmediatelyIfVisible
        });
    }

    internal static void RequestDisplayRefresh(string reason, bool liveDisplayRefresh = false)
    {
        Publish(new ManagedWardMapEvent(ManagedWardMapEventKind.DisplayRefreshRequested, reason)
        {
            LiveDisplayRefresh = liveDisplayRefresh
        });
    }

    private static void Publish(ManagedWardMapEvent mapEvent)
    {
        var visibilityIndexChanged = ApplyVisibilityIndexEvent(mapEvent);
        ApplyPinsEvent(mapEvent, visibilityIndexChanged);
    }

    private static bool ApplyVisibilityIndexEvent(ManagedWardMapEvent mapEvent)
    {
        switch (mapEvent.Kind)
        {
            case ManagedWardMapEventKind.LiveWardMutation:
                if (!ShouldUpdateIndex(mapEvent.MutationKind))
                {
                    return false;
                }

                WardMinimapVisibilityIndex.NotifyWardStateChanged(mapEvent.Area);
                return true;
            case ManagedWardMapEventKind.ZdoWardMutation:
                if (!ShouldUpdateIndex(mapEvent.MutationKind))
                {
                    return false;
                }

                WardMinimapVisibilityIndex.NotifyWardStateChanged(mapEvent.Zdo);
                return true;
            case ManagedWardMapEventKind.WardObserved:
                if (mapEvent.Zdo == null)
                {
                    return false;
                }

                WardMinimapVisibilityIndex.ObserveManagedWard(mapEvent.Zdo);
                return true;
            case ManagedWardMapEventKind.WardRemoved:
                return !mapEvent.ZdoId.IsNone() &&
                       WardMinimapVisibilityIndex.ForgetWard(mapEvent.ZdoId);
            case ManagedWardMapEventKind.SyncedWardState:
                WardMinimapVisibilityIndex.ObserveSyncedWardState(
                    mapEvent.ZdoId,
                    mapEvent.DataRevision,
                    mapEvent.OwnerPlayerId,
                    mapEvent.WardGuildId,
                    mapEvent.Position,
                    mapEvent.Radius,
                    mapEvent.IsEnabled,
                    mapEvent.PermittedPlayerIds);
                return true;
            case ManagedWardMapEventKind.ProjectionInvalidated:
                WardMinimapVisibilityIndex.InvalidateAll(mapEvent.Reason);
                return true;
            default:
                return false;
        }
    }

    private static void ApplyPinsEvent(ManagedWardMapEvent mapEvent, bool visibilityIndexChanged)
    {
        switch (mapEvent.Kind)
        {
            case ManagedWardMapEventKind.LiveWardMutation:
            case ManagedWardMapEventKind.ZdoWardMutation:
                if (ShouldUpdatePins(mapEvent.MutationKind))
                {
                    WardMinimapPinsManager.NotifyWardDataMayHaveChanged(mapEvent.Reason, mapEvent.LiveDisplayRefresh);
                }
                break;
            case ManagedWardMapEventKind.WardObserved:
                if (mapEvent.Zdo != null)
                {
                    WardMinimapPinsManager.NotifyWardDataMayHaveChanged(mapEvent.Reason, mapEvent.LiveDisplayRefresh);
                }
                break;
            case ManagedWardMapEventKind.WardRemoved:
                if (visibilityIndexChanged)
                {
                    WardMinimapPinsManager.NotifyWardDataMayHaveChanged(mapEvent.Reason, mapEvent.LiveDisplayRefresh);
                }
                break;
            case ManagedWardMapEventKind.SyncedWardState:
            case ManagedWardMapEventKind.ProjectionInvalidated:
            case ManagedWardMapEventKind.DisplayRefreshRequested:
                WardMinimapPinsManager.NotifyWardDataMayHaveChanged(mapEvent.Reason, mapEvent.LiveDisplayRefresh);
                break;
            case ManagedWardMapEventKind.ViewerProjectionChanged:
                WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(mapEvent.Reason, mapEvent.RefreshImmediatelyIfVisible);
                if (ZNet.instance != null && ZNet.instance.IsServer())
                {
                    WardMinimapPinsManager.QueueServerViewerRefreshRecipients(
                        mapEvent.FullRefresh ? null : mapEvent.RecipientPeerUids,
                        mapEvent.Reason);
                }
                break;
            case ManagedWardMapEventKind.LocalDisplayRefreshRequested:
                WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(mapEvent.Reason, mapEvent.RefreshImmediatelyIfVisible);
                break;
        }
    }

    private static bool ShouldUpdateIndex(ManagedWardMapMutationKind kind)
    {
        return kind == ManagedWardMapMutationKind.IndexOnly || kind == ManagedWardMapMutationKind.IndexAndPins;
    }

    private static bool ShouldUpdatePins(ManagedWardMapMutationKind kind)
    {
        return kind == ManagedWardMapMutationKind.PinsOnly || kind == ManagedWardMapMutationKind.IndexAndPins;
    }
}
