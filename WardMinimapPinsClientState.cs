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
    private static bool _loggedMissingWardIcon;
    private static bool _loggedHiddenWardIconPinType;
    private static bool _loggedHiddenWardRangePinType;
    private static bool _hasLastLoggedMapMode;
    private static bool? _lastCanSeeAllWards;
    private static ClientSnapshotState _snapshotState = ClientSnapshotState.Uninitialized;
    private static int _lastViewerRevisionToken;
    private static int _nextSnapshotRequestId;
    private static int _pendingSnapshotRequestId;
    private static DateTime _lastRemoteSnapshotRequestUtc = DateTime.MinValue;
    private static Minimap? _boundMinimap;
    private static Minimap? _customPinTypesMinimap;
    private static Minimap.MapMode _lastLoggedMapMode;
    private static string? _lastDisplayDecision;
    private static string? _lastApplySummary;
    private static string? _lastPendingRefreshReason;
    private static string? _lastRemoteSnapshotBootstrapReason;
    private static string? _lastScanSummary;

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
        _loggedMissingWardIcon = false;
        _loggedHiddenWardIconPinType = false;
        _loggedHiddenWardRangePinType = false;
        _hasLastLoggedMapMode = false;
        _lastCanSeeAllWards = null;
        _lastViewerRevisionToken = 0;
        _nextSnapshotRequestId = 0;
        _pendingSnapshotRequestId = 0;
        _lastRemoteSnapshotRequestUtc = DateTime.MinValue;
        _boundMinimap = null;
        _customPinTypesMinimap = null;
        WardIconPinType = Minimap.PinType.Icon4;
        WardRangePinType = Minimap.PinType.EventArea;
        _lastDisplayDecision = null;
        _lastApplySummary = null;
        _lastPendingRefreshReason = null;
        _lastRemoteSnapshotBootstrapReason = "znet awake";
        _lastScanSummary = null;
        LocalSnapshot.Clear();
        IconPins.Clear();
        ActiveRangePins.Clear();
    }

    internal static void HandleLocalConfigChanged()
    {
        WardMapRangeSprites.Reset();
        UpdateLocalState(Player.m_localPlayer, force: false, allowClosedMapRefresh: true);
        Plugin.LogWardDiagnosticVerbose(
            "WardPins.State",
            $"Local ward pin config changed. pinScale={Plugin.WardMinimapPinScale?.Value}, ranges={Plugin.WardMinimapActiveRanges?.Value}");
    }

    internal static void NotifyLocalWardDataMayHaveChanged(string reason, bool refreshImmediatelyIfVisible = false)
    {
        QueueForceRefresh(reason);
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

        if (!_hasLastLoggedMapMode || _lastLoggedMapMode != mode)
        {
            _hasLastLoggedMapMode = true;
            _lastLoggedMapMode = mode;
            Plugin.LogWardDiagnosticVerbose("WardPins.State", $"Minimap map mode changed. mode={mode}");
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
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.State",
                $"Minimap binding changed. hadBoundMinimap={_boundMinimap != null}, hasMinimap={minimap != null}");
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
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.State",
                $"Admin debug visibility changed. playerId={player.GetPlayerID()}, canSeeAllWards={canSeeAllWards}");
            QueueRemoteSnapshotBootstrapRequest("admin debug visibility changed");
            _pendingForceRefresh = true;
            _lastPendingRefreshReason ??= "admin debug visibility changed";
            force = force || IsLargeMapOpen(minimap);
        }

        if (_pendingForceRefresh)
        {
            // Defer refresh until the large map is opened again.
        }

        if (!ShouldDisplayPins(player, canSeeAllWards, out var displayReason))
        {
            LogDisplayDecision(player, minimap, canSeeAllWards, false, displayReason);
            ClearLocalPins(clearSnapshot: false);
            return;
        }

        LogDisplayDecision(player, minimap, canSeeAllWards, true, "display-enabled");
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

    private static bool ShouldDisplayPins(Player player, bool canSeeAllWards, out string reason)
    {
        var showIconPins = GetWardPinScale() > 0;
        var showActiveRanges = Plugin.WardMinimapActiveRanges != null &&
                               Plugin.WardMinimapActiveRanges.Value == Plugin.Toggle.On;
        if (!showIconPins && !showActiveRanges)
        {
            reason = "pins-and-ranges-config-off";
            return false;
        }

        if (Game.m_noMap && !canSeeAllWards)
        {
            reason = "nomap-blocked";
            return false;
        }

        if (ZNet.instance == null)
        {
            reason = "znet-unavailable";
            return false;
        }

        if (ZDOMan.instance == null)
        {
            reason = "zdoman-unavailable";
            return false;
        }

        if (player != Player.m_localPlayer)
        {
            reason = "not-local-player";
            return false;
        }

        reason = "display-enabled";
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
        if (!WardMinimapVisibilityIndex.TryPrepare(ZDOMan.instance, "ward minimap refresh"))
        {
            LocalSnapshot.Clear();
            _lastViewerRevisionToken = 0;
            QueueRemoteSnapshotBootstrapRequest("visibility index unavailable during local refresh");
            LogScanSummary("visibility-index-unavailable");
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
            QueueRemoteSnapshotBootstrapRequest(force ? "forced remote snapshot request" : "remote snapshot request needs full snapshot");
        }

        routedRpc.InvokeRoutedRPC(RequestWardPinsRpc, pkg);
        if (Plugin.ShouldLogWardDiagnosticVerbose())
        {
            Plugin.LogWardDiagnosticVerbose(
                "WardPins.Request",
                $"Requested remote ward minimap snapshot. requestId={requestId}, requestFullSnapshot={requestFullSnapshot}, snapshotState={_snapshotState}, knownViewerRevisionToken={knownViewerRevisionToken}, localViewerRevisionToken={_lastViewerRevisionToken}, playerId={player.GetPlayerID()}, canSeeAllWards={canSeeAllWards}, force={force}, cachedSnapshotCount={LocalSnapshot.Count}, bootstrapReason='{_lastRemoteSnapshotBootstrapReason ?? string.Empty}'");
        }
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

        if (Plugin.ShouldLogWardDiagnosticVerbose())
        {
            LogScanSummary(
                $"playerId={playerId}, canSeeAllWards={canSeeAllWards}, indexedWardCount={snapshot.IndexedWardCount}, candidateWardCount={snapshot.CandidateWardCount}, visibleWardCount={snapshot.VisibleWardCount}, enabledWardCount={snapshot.EnabledWardCount}{DescribeFirstEntry(snapshot.FirstEntry)}");
        }
    }
}
