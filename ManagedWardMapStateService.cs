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
        bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.NotifyWardStateChanged(area);
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
    }

    internal static void NotifyZdoWardMutation(
        ZDO? zdo,
        bool notifyPins = true,
        bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.NotifyWardStateChanged(zdo);

        if (notifyPins)
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
        }
    }

    internal static void NotifyWardObserved(ZDO? zdo, bool liveDisplayRefresh = false)
    {
        if (zdo == null)
        {
            return;
        }

        WardMinimapVisibilityIndex.ObserveManagedWard(zdo);
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
    }

    internal static void NotifyWardRemoved(ZDOID zdoId, bool liveDisplayRefresh = false)
    {
        if (!zdoId.IsNone() && WardMinimapVisibilityIndex.ForgetWard(zdoId))
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
        }
    }

    internal static void NotifyViewerProjectionChanged(
        bool fullRefresh,
        HashSet<long>? recipientPeerUids = null,
        bool refreshImmediatelyIfVisible = false)
    {
        WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(refreshImmediatelyIfVisible);
        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            WardMinimapPinsManager.QueueServerViewerRefreshRecipients(
                fullRefresh ? null : recipientPeerUids);
        }
    }

    internal static void InvalidateProjection(bool liveDisplayRefresh = false)
    {
        WardMinimapVisibilityIndex.InvalidateAll();
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
    }

    internal static void RequestLocalDisplayRefresh(bool refreshImmediatelyIfVisible = false)
    {
        WardMinimapPinsManager.NotifyLocalWardDataMayHaveChanged(refreshImmediatelyIfVisible);
    }

    internal static void RequestDisplayRefresh(bool liveDisplayRefresh = false)
    {
        WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
    }

}
