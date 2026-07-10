using System.Collections.Generic;

namespace STUWard;

internal static class ManagedWardMapStateService
{
    // Map refresh ownership rules:
    // - Live mutators update from the area.
    // - ZDO-only mutators update from the ZDO.
    // - Display-only requests do not mutate the visibility index.
    internal static void NotifyLiveWardMutation(
        PrivateArea? area,
        string reason,
        bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.NotifyWardStateChanged(area);
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
    }

    internal static void NotifyZdoWardMutation(
        ZDO? zdo,
        string reason,
        bool notifyPins = true,
        bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.NotifyWardStateChanged(zdo);

        if (notifyPins)
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
        }
    }

    internal static void NotifyWardObserved(ZDO? zdo, string reason, bool liveDisplayRefresh = false)
    {
        if (zdo == null)
        {
            return;
        }

        WardMinimapVisibilityIndex.ObserveManagedWard(zdo);
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
    }

    internal static void NotifyWardRemoved(ZDOID zdoId, string reason, bool liveDisplayRefresh = false)
    {
        if (!zdoId.IsNone() && WardMinimapVisibilityIndex.ForgetWard(zdoId))
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
        }
    }

    internal static void NotifyViewerProjectionChanged(
        string reason,
        bool fullRefresh,
        HashSet<long>? recipientPeerUids = null,
        bool refreshImmediatelyIfVisible = false)
    {
        WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(reason, refreshImmediatelyIfVisible);
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            WardMinimapPinsManager.QueueServerViewerRefreshRecipients(
                fullRefresh ? null : recipientPeerUids,
                reason);
        }
    }

    internal static void InvalidateProjection(string reason, bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.InvalidateAll(reason);
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
    }

    internal static void RequestLocalDisplayRefresh(string reason, bool refreshImmediatelyIfVisible = false)
    {
        WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(reason, refreshImmediatelyIfVisible);
    }

    internal static void RequestDisplayRefresh(string reason, bool liveDisplayRefresh = false)
    {
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(reason, liveDisplayRefresh);
    }

}
