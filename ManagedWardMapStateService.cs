using System.Collections.Generic;

namespace STUWard;

internal static class ManagedWardMapStateService
{
    // Map refresh ownership rules:
    // - Live mutators update from the area.
    // - ZDO-only mutators update from the ZDO.
    // - Display-only requests do not mutate the visibility index.
    internal static void NotifyWardMutation(
        PrivateArea? area,
        bool liveDisplayRefresh = false)
    {
        if (WardMinimapVisibilityIndex.NotifyWardStateChanged(area))
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
        }
    }

    internal static void NotifyWardMutation(
        ZDO? zdo,
        bool notifyPins = true,
        bool liveDisplayRefresh = false)
    {
        if (WardMinimapVisibilityIndex.NotifyWardStateChanged(zdo) && notifyPins)
        {
            WardMinimapPinsManager.NotifyWardDataMayHaveChanged(liveDisplayRefresh);
        }
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
            if (fullRefresh || recipientPeerUids == null || recipientPeerUids.Count > 0)
            {
                WardMinimapPinsManager.QueueServerViewerRefreshRecipients(
                    fullRefresh ? null : recipientPeerUids);
            }
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
