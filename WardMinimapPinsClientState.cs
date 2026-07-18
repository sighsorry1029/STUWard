using System;
using System.Collections.Generic;
using UnityEngine;

namespace STUWard;

internal static partial class WardMinimapPinsManager
{
    private const float DefaultSmallMapZoom = 0.01f;
    private static Minimap.PinType WardIconPinType = Minimap.PinType.Icon4;
    private static Minimap.PinType WardRangePinType = Minimap.PinType.EventArea;
    private static readonly TimeSpan RemoteSnapshotRequestInterval = TimeSpan.FromSeconds(0.5);
    private static readonly TimeSpan PendingSnapshotRetryInterval = TimeSpan.FromSeconds(2);
    private static readonly Dictionary<ZDOID, WardMinimapSnapshotEntry> LocalSnapshot = new();
    private static readonly Dictionary<ZDOID, Minimap.PinData> IconPins = new();
    private static readonly Dictionary<ZDOID, Minimap.PinData> ActiveRangePins = new();

    private static bool _pendingForceRefresh;
    private static bool? _lastCanSeeAllWards;
    private static ClientSnapshotState _snapshotState = ClientSnapshotState.Uninitialized;
    private static int _lastViewerRevisionToken;
    private static int _nextSnapshotRequestId;
    private static int _pendingSnapshotRequestId;
    private static DateTime _lastRemoteSnapshotRequestUtc = DateTime.MinValue;
    private static Minimap? _boundMinimap;
    private static Minimap? _customPinTypesMinimap;

    private enum ClientSnapshotState
    {
        Uninitialized,
        AwaitingFullSnapshot,
        Ready
    }

    private static void ResetClientRuntimeState()
    {
        WardMapRangeSprites.Reset();
        ClearLocalPins(clearSnapshot: true);
        _pendingForceRefresh = false;
        _snapshotState = ClientSnapshotState.AwaitingFullSnapshot;
        _lastCanSeeAllWards = null;
        _lastViewerRevisionToken = 0;
        _nextSnapshotRequestId = 0;
        _pendingSnapshotRequestId = 0;
        _lastRemoteSnapshotRequestUtc = DateTime.MinValue;
        _boundMinimap = null;
        _customPinTypesMinimap = null;
        WardIconPinType = Minimap.PinType.Icon4;
        WardRangePinType = Minimap.PinType.EventArea;
        LocalSnapshot.Clear();
        IconPins.Clear();
        ActiveRangePins.Clear();
    }

    internal static void HandleLocalConfigChanged()
    {
        WardMapRangeSprites.Reset();
        UpdateLocalState(Player.m_localPlayer, force: false, allowClosedMapRefresh: true);
    }

    internal static void NotifyLocalWardDataMayHaveChanged(bool refreshImmediatelyIfVisible = false)
    {
        QueueForceRefresh();
        if (!refreshImmediatelyIfVisible)
        {
            return;
        }

        UpdateLocalState(Player.m_localPlayer, force: true, allowClosedMapRefresh: true);
    }

    internal static void HandleMapModeChanged(Minimap? minimap, Minimap.MapMode mode)
    {
        if (minimap == null)
        {
            return;
        }

        if (!ReferenceEquals(_boundMinimap, minimap))
        {
            _boundMinimap = minimap;
        }

        if (mode == Minimap.MapMode.Large)
        {
            UpdateLocalState(Player.m_localPlayer, force: true);
        }
    }

    internal static void UpdateLocalState(Player? player, bool force = false, bool allowClosedMapRefresh = false)
    {
        if (player == null || player != Player.m_localPlayer)
        {
            return;
        }

        var minimap = Minimap.instance;
        if (!ReferenceEquals(_boundMinimap, minimap))
        {
            ClearLocalPins(clearSnapshot: false);
            _boundMinimap = minimap;
        }

        var canSeeAllWards = WardAdminDebugAccess.IsPlayerAdminDebugController(player.GetPlayerID());
        if (!_lastCanSeeAllWards.HasValue || _lastCanSeeAllWards.Value != canSeeAllWards)
        {
            if (_lastCanSeeAllWards.GetValueOrDefault() && !canSeeAllWards)
            {
                ClearLocalPins(clearSnapshot: true);
            }

            _lastCanSeeAllWards = canSeeAllWards;
            QueueRemoteSnapshotBootstrapRequest();
            _pendingForceRefresh = true;
            force = force || IsLargeMapOpen(minimap);
        }

        if (_pendingForceRefresh)
        {
            // Defer refresh until the large map is opened again.
        }

        if (!ShouldDisplayPins(player, canSeeAllWards))
        {
            ClearLocalPins(clearSnapshot: false);
            return;
        }

        RefreshLocalSnapshotIfNeeded(player, minimap, canSeeAllWards, force, allowClosedMapRefresh);
        ApplySnapshotToMinimap(minimap);
    }

    internal static void UpdatePendingRemoteState(Player? player)
    {
        if (player == null ||
            player != Player.m_localPlayer ||
            !IsLargeMapOpen(Minimap.instance) ||
            (!_pendingForceRefresh && _snapshotState == ClientSnapshotState.Ready && _pendingSnapshotRequestId == 0))
        {
            return;
        }

        UpdateLocalState(player, force: false);
    }

    private static bool ShouldDisplayPins(Player player, bool canSeeAllWards)
    {
        var showIconPins = GetWardPinScale() > 0;
        var showActiveRanges = Plugin.WardMinimapActiveRanges != null &&
                               Plugin.WardMinimapActiveRanges.Value == Plugin.Toggle.On;
        if (!showIconPins && !showActiveRanges)
        {
            return false;
        }

        if (Game.m_noMap && !canSeeAllWards)
        {
            return false;
        }

        if (ZNet.instance == null)
        {
            return false;
        }

        if (ZDOMan.instance == null)
        {
            return false;
        }

        if (player != Player.m_localPlayer)
        {
            return false;
        }

        return true;
    }

    private static void RefreshLocalSnapshotIfNeeded(
        Player player,
        Minimap? minimap,
        bool canSeeAllWards,
        bool force,
        bool allowClosedMapRefresh)
    {
        if (!allowClosedMapRefresh && !IsLargeMapOpen(minimap))
        {
            return;
        }

        if (ZNet.instance != null && ZNet.instance.IsServer())
        {
            RefreshLocalSnapshotFromLocalIndex(player, canSeeAllWards, force);
            return;
        }

        RequestRemoteSnapshotIfNeeded(player, canSeeAllWards, force);
    }

    private static void RefreshLocalSnapshotFromLocalIndex(Player player, bool canSeeAllWards, bool force)
    {
        if (!WardMinimapVisibilityIndex.TryPrepare(ZDOMan.instance))
        {
            LocalSnapshot.Clear();
            _lastViewerRevisionToken = 0;
            QueueRemoteSnapshotBootstrapRequest();
            return;
        }

        var playerId = player.GetPlayerID();
        var playerGuildId = GuildsCompat.GetPlayerGuildId(playerId);
        var viewerRevisionToken = WardMinimapVisibilityIndex.GetViewerRevisionToken(playerId, playerGuildId, canSeeAllWards);
        if (!force &&
            !_pendingForceRefresh &&
            _snapshotState == ClientSnapshotState.Ready &&
            viewerRevisionToken == _lastViewerRevisionToken)
        {
            return;
        }

        ClearPendingForceRefresh();
        _lastViewerRevisionToken = viewerRevisionToken;
        RebuildLocalSnapshot(playerId, playerGuildId, canSeeAllWards);
    }

    private static void RequestRemoteSnapshotIfNeeded(Player player, bool canSeeAllWards, bool force)
    {
        RegisterRpcs();

        var now = DateTime.UtcNow;
        var shouldRetryPendingRequest = _pendingSnapshotRequestId != 0 &&
                                        now - _lastRemoteSnapshotRequestUtc >= PendingSnapshotRetryInterval;
        var requestFullSnapshot = force ||
                                  _pendingForceRefresh ||
                                  _snapshotState != ClientSnapshotState.Ready ||
                                  _lastViewerRevisionToken == 0;
        var shouldRequest = requestFullSnapshot || shouldRetryPendingRequest;
        if (!shouldRequest)
        {
            return;
        }

        var requestInterval = force
            ? RemoteSnapshotRequestInterval
            : _pendingSnapshotRequestId != 0
            ? PendingSnapshotRetryInterval
            : RemoteSnapshotRequestInterval;
        if (now - _lastRemoteSnapshotRequestUtc < requestInterval)
        {
            return;
        }

        var routedRpc = ZRoutedRpc.instance;
        if (routedRpc == null)
        {
            return;
        }

        var requestId = _nextSnapshotRequestId == int.MaxValue ? 1 : _nextSnapshotRequestId + 1;
        _nextSnapshotRequestId = requestId;
        _pendingSnapshotRequestId = requestId;
        _lastRemoteSnapshotRequestUtc = now;

        var pkg = new ZPackage();
        pkg.Write(requestId);
        var knownViewerRevisionToken = requestFullSnapshot ? 0 : _lastViewerRevisionToken;
        pkg.Write(knownViewerRevisionToken);
        pkg.Write(requestFullSnapshot);
        if (requestFullSnapshot)
        {
            QueueRemoteSnapshotBootstrapRequest();
        }

        routedRpc.InvokeRoutedRPC(RequestWardPinsRpc, pkg);
    }

    private static void RebuildLocalSnapshot(long playerId, int playerGuildId, bool canSeeAllWards)
    {
        var snapshot = WardMinimapViewerSnapshotBuilder.Build(
            playerId,
            playerGuildId,
            canSeeAllWards,
            _lastViewerRevisionToken,
            includeEntries: true,
            includeVisibleWardDataRevisions: false);
        ReplaceLocalSnapshot(snapshot.Entries);
        _pendingSnapshotRequestId = 0;
        ClearPendingForceRefresh();
        ClearPendingRemoteSnapshotBootstrapRequest();
    }
}
