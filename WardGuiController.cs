using System.Collections.Generic;
using Jotunn.Managers;
using LocalizationManager;
using UnityEngine;
using UnityEngine.UI;

namespace STUWard;

internal sealed class WardGuiController : MonoBehaviour
{
    private const float ConfigurationPushDebounceSeconds = 0.15f;
    private const float ConfigurationRequestTimeoutSeconds = 5f;
    private const float RecentPlayersRequestTimeoutSeconds = 5f;

    internal static WardGuiController? Instance { get; private set; }

    private readonly Dictionary<long, PermittedRowView> _permittedRows = new();
    private readonly Dictionary<long, RecentPlayerRowView> _recentPlayerRows = new();
    private readonly Dictionary<long, WardPlayerActivityEntry> _registeredPlayerActivity = new();
    private readonly Dictionary<WardRestrictionOptions, RestrictionRowView> _restrictionRows = new();
    private readonly List<long> _permittedRowsToRemove = new();
    private readonly List<long> _recentPlayerRowsToRemove = new();

    private PrivateArea? _currentWard;
    private WardConfiguration _currentConfiguration;
    private WardConfiguration _authoritativeConfiguration;
    private WardConfiguration _pendingConfiguration;
    private GameObject? _root;
    private GameObject? _panel;
    private GameObject? _generalPageRoot;
    private GameObject? _restrictionsPageRoot;
    private RectTransform? _permittedContent;
    private RectTransform? _recentPlayersContent;
    private RectTransform? _restrictionsContent;
    private Text? _ownerValueText;
    private Text? _guildValueText;
    private Toggle? _autoCloseToggle;
    private Toggle? _warningSoundToggle;
    private Toggle? _warningFlashToggle;
    private Toggle? _areaMarkerRotationToggle;
    private Button? _previousPageButton;
    private Button? _nextPageButton;
    private Transform? _buildParent;
    private WardSettingsPage _currentPage = WardSettingsPage.General;
    private bool _visible;
    private bool _suppressUiEvents;
    private bool _configurationPushPending;
    private bool _closeRequested;
    private float _nextConfigurationPushTime;
    private float _pendingConfigurationRequestedAt;
    private int _lastPermittedRevision = int.MinValue;
    private int _permittedRefreshGeneration;
    private int _recentPlayersRefreshGeneration;
    private long _pendingConfigurationRequestId;
    private long _pendingRecentPlayersRequestId;
    private float _recentPlayersRequestedAt;
    private bool _recentPlayersRequestInProgress;
    private bool _hasDeferredRecentPlayersSnapshot;
    private WardRecentPlayersSnapshot _deferredRecentPlayersSnapshot;
    private StatusRowView? _emptyPermittedRow;
    private StatusRowView? _recentPlayersStatusRow;
    private string _registeredPlayersSearchQuery = string.Empty;
    private string _recentPlayersSearchQuery = string.Empty;
    private ZDOID _searchQueryWardZdoId = ZDOID.None;
    private RecentPlayersListState _recentPlayersListState;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);

        GUIManager.OnCustomGUIAvailable += BuildGui;
        WardRecentPlayers.SnapshotReceived += HandleRecentPlayersSnapshot;
        BuildGui();
    }

    private void OnDestroy()
    {
        GUIManager.OnCustomGUIAvailable -= BuildGui;
        WardRecentPlayers.SnapshotReceived -= HandleRecentPlayersSnapshot;
        GUIManager.BlockInput(false);

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (!_visible)
        {
            TryOpenHoveredWardUi();
            return;
        }

        if (_closeRequested)
        {
            if (_currentWard == null ||
                !WardAccess.IsManagedWard(ManagedWardRef.FromArea(_currentWard), false))
            {
                CompleteCloseWardUi();
                return;
            }

            if (HasPendingConfigurationRequest() &&
                Time.unscaledTime - _pendingConfigurationRequestedAt >= ConfigurationRequestTimeoutSeconds)
            {
                HandlePendingConfigurationRequestTimeout();
            }

            TryFlushDeferredConfigurationAfterRequestResolution();
            return;
        }

        if (_root != null && !_root.activeSelf)
        {
            _root.SetActive(true);
        }

        if (Input.GetKeyDown(KeyCode.Escape))
        {
            CloseWardUi();
            return;
        }

        if (_currentWard == null || !WardAccess.IsManagedWard(ManagedWardRef.FromArea(_currentWard), false))
        {
            CloseWardUi();
            return;
        }

        var localPlayer = Player.m_localPlayer;
        if (!WardAccess.CanConfigureWard(_currentWard, localPlayer) &&
            !WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(_currentWard, localPlayer))
        {
            CloseWardUi();
            return;
        }

        if (HasPendingConfigurationRequest() &&
            Time.unscaledTime - _pendingConfigurationRequestedAt >= ConfigurationRequestTimeoutSeconds)
        {
            HandlePendingConfigurationRequestTimeout();
        }

        if (!HasPendingConfigurationRequest())
        {
            RefreshAuthoritativeConfigurationFromWard();
        }

        if (!HasPendingConfigurationRequest() &&
            _configurationPushPending &&
            Time.unscaledTime >= _nextConfigurationPushTime)
        {
            PushPendingConfiguration();
        }

        if (WardPermittedSnapshots.GetRevision(_currentWard) != _lastPermittedRevision)
        {
            RefreshPermittedPlayers(force: false);
            RequestRecentPlayersSnapshot();
        }

        if (_pendingRecentPlayersRequestId != 0L &&
            Time.unscaledTime - _recentPlayersRequestedAt >= RecentPlayersRequestTimeoutSeconds)
        {
            _pendingRecentPlayersRequestId = 0L;
            SetRecentPlayerRowsInteractable(true);
            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiRecentPlayersErrorToken, WardLocalization.UiRecentPlayersErrorFallback),
                isError: true,
                RecentPlayersListState.Error);
        }
    }

    internal bool TryOpenHoveredWardUi()
    {
        if (!Plugin.IsWardSettingsShortcutDown())
        {
            return false;
        }

        var player = Player.m_localPlayer;
        var hovering = player != null ? player.m_hovering : null;
        if (hovering == null)
        {
            return false;
        }

        var ward = ManagedWardRef.FromArea(hovering.GetComponentInParent<PrivateArea>());
        if (!WardAccess.CanConfigureWard(ward, player) &&
            !WardAdminDebugAccess.CanLocallyAttemptAnyWardControl(ward.Area, player))
        {
            return false;
        }

        OpenWardUi(ward.Area!);
        return true;
    }

    internal void OpenWardUi(PrivateArea ward)
    {
        if (!TryGetWardZdoId(ward, out var wardZdoId) || wardZdoId != _searchQueryWardZdoId)
        {
            _registeredPlayersSearchQuery = string.Empty;
            _recentPlayersSearchQuery = string.Empty;
            _searchQueryWardZdoId = wardZdoId;
        }

        BuildGui();
        if (_root == null)
        {
            return;
        }

        _currentWard = ward;
        _closeRequested = false;
        _configurationPushPending = false;
        _lastPermittedRevision = int.MinValue;
        _currentPage = WardSettingsPage.General;
        _pendingRecentPlayersRequestId = 0L;
        _recentPlayersRequestInProgress = false;
        _hasDeferredRecentPlayersSnapshot = false;
        _registeredPlayerActivity.Clear();
        ClearPendingConfigurationRequest();
        _authoritativeConfiguration = WardSettings.GetConfiguration(ward);
        _currentConfiguration = _authoritativeConfiguration;
        RefreshStaticTexts();
        RefreshControls();
        RefreshPermittedPlayers(force: true);
        SetActivePage(WardSettingsPage.General);
        RequestRecentPlayersSnapshot();
        SetVisible(true);
    }

    internal void CloseWardUi()
    {
        _closeRequested = true;
        FlushPendingConfigurationPush();
        TryFlushDeferredConfigurationAfterRequestResolution();
        if (!_closeRequested)
        {
            return;
        }

        if (HasPendingConfigurationRequest() || _configurationPushPending)
        {
            if (_root != null)
            {
                _root.SetActive(false);
            }

            GUIManager.BlockInput(false);
            return;
        }

        CompleteCloseWardUi();
    }

    private void CompleteCloseWardUi()
    {
        _currentWard = null;
        _closeRequested = false;
        _configurationPushPending = false;
        _lastPermittedRevision = int.MinValue;
        _pendingRecentPlayersRequestId = 0L;
        _recentPlayersRequestInProgress = false;
        _hasDeferredRecentPlayersSnapshot = false;
        _registeredPlayerActivity.Clear();
        ClearPendingConfigurationRequest();
        SetVisible(false);
    }

    private void BuildGui()
    {
        if (GUIManager.CustomGUIFront == null)
        {
            return;
        }

        Localizer.ReloadCurrentLanguageIfAvailable();

        if (_root != null)
        {
            Destroy(_root);
        }

        ClearPermittedRows();
        ClearRecentPlayerRows();
        _emptyPermittedRow = null;
        _recentPlayersStatusRow = null;
        _lastPermittedRevision = int.MinValue;
        _restrictionRows.Clear();
        _generalPageRoot = null;
        _restrictionsPageRoot = null;
        _restrictionsContent = null;
        _recentPlayersContent = null;
        _previousPageButton = null;
        _nextPageButton = null;
        _buildParent = null;

        var gui = GUIManager.Instance;
        var panelSize = WardGuiLayoutSettings.GetPanelSize();
        _root = new GameObject("STUWardGUIRoot", typeof(RectTransform), typeof(Image));
        _root.transform.SetParent(GUIManager.CustomGUIFront.transform, false);

        var rootRect = _root.GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.anchoredPosition = Vector2.zero;

        var rootImage = _root.GetComponent<Image>();
        rootImage.color = new Color(0f, 0f, 0f, 0.6f);
        rootImage.raycastTarget = true;

        _panel = gui.CreateWoodpanel(
            _root.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            WardGuiLayoutSettings.GetPanelOffset(),
            panelSize.x,
            panelSize.y,
            false);
        _panel.name = "STUWardPanel";

        _generalPageRoot = CreatePageRoot("STUWardGeneralPage", panelSize);
        _restrictionsPageRoot = CreatePageRoot("STUWardRestrictionsPage", panelSize);

        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiTitleToken, WardLocalization.UiTitleFallback),
            WardGuiLayoutSettings.GetTitlePosition(),
            34,
            WardGuiLayoutSettings.GetTitleSize().x,
            WardGuiLayoutSettings.GetTitleSize().y,
            TextAnchor.MiddleCenter,
            gui.AveriaSerifBold,
            gui.ValheimOrange);
        var ownerGuildLabelSize = WardGuiLayoutSettings.GetOwnerGuildLabelSize();
        _ownerValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetOwnerPosition(), 22, ownerGuildLabelSize.x, ownerGuildLabelSize.y, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _guildValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetGuildPosition(), 20, ownerGuildLabelSize.x, ownerGuildLabelSize.y, TextAnchor.MiddleLeft, gui.AveriaSerif, gui.ValheimBeige);

        var closeButton = CreateButton(
            WardLocalization.Localize(WardLocalization.UiCloseToken, WardLocalization.UiCloseFallback),
            WardGuiLayoutSettings.GetCloseButtonPosition(),
            170f,
            42f);
        closeButton.onClick.AddListener(CloseWardUi);

        _previousPageButton = CreateButton("<", WardGuiLayoutSettings.GetPageArrowButtonPosition(), 54f, 42f);
        _previousPageButton.onClick.AddListener(() => SetActivePage(WardSettingsPage.General));
        StylePageArrowButton(_previousPageButton);
        _nextPageButton = CreateButton(">", WardGuiLayoutSettings.GetPageArrowButtonPosition(), 54f, 42f);
        _nextPageButton.onClick.AddListener(() => SetActivePage(WardSettingsPage.Restrictions));
        StylePageArrowButton(_nextPageButton);

        _buildParent = _generalPageRoot.transform;
        BuildTrustedPlayers(gui);
        BuildRecentPlayers(gui);
        _buildParent = _restrictionsPageRoot.transform;
        BuildTopControls(gui);
        BuildRestrictions();
        _buildParent = null;
        SetActivePage(_currentPage);
        SetVisible(_visible);
        if (_visible && _currentWard != null)
        {
            RefreshStaticTexts();
            RefreshControls();
            RefreshPermittedPlayers(force: true);
            if (_currentPage == WardSettingsPage.General)
            {
                RequestRecentPlayersSnapshot();
            }
        }
    }

    private void BuildTopControls(GUIManager gui)
    {
        var gridRoot = new GameObject("STUWardBehaviorControls", typeof(RectTransform), typeof(GridLayoutGroup));
        gridRoot.transform.SetParent(GetBuildParent(), false);
        var gridSize = WardGuiLayoutSettings.GetBehaviorControlsGridSize();
        ConfigureRect(
            gridRoot.GetComponent<RectTransform>(),
            WardGuiLayoutSettings.GetBehaviorControlsGridPosition(),
            gridSize.x,
            gridSize.y);

        var layout = gridRoot.GetComponent<GridLayoutGroup>();
        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Horizontal;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.cellSize = WardGuiLayoutSettings.GetRestrictionCellSize();
        layout.spacing = WardGuiLayoutSettings.GetRestrictionCellSpacing();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 2;

        _warningSoundToggle = CreateBehaviorToggleRow(
            gridRoot.transform,
            "WardAlertSound",
            WardLocalization.Localize(WardLocalization.UiWarningSoundToken, WardLocalization.UiWarningSoundFallback),
            gui);
        _warningSoundToggle.onValueChanged.AddListener(OnWarningSoundToggleChanged);

        _warningFlashToggle = CreateBehaviorToggleRow(
            gridRoot.transform,
            "WardAlertVisualEffect",
            WardLocalization.Localize(WardLocalization.UiWarningFlashToken, WardLocalization.UiWarningFlashFallback),
            gui);
        _warningFlashToggle.onValueChanged.AddListener(OnWarningFlashToggleChanged);

        _areaMarkerRotationToggle = CreateBehaviorToggleRow(
            gridRoot.transform,
            "WardRangeRotation",
            WardLocalization.Localize(WardLocalization.UiAreaMarkerRotationToken, WardLocalization.UiAreaMarkerRotationFallback),
            gui);
        _areaMarkerRotationToggle.onValueChanged.AddListener(OnAreaMarkerRotationToggleChanged);

        _autoCloseToggle = CreateBehaviorToggleRow(
            gridRoot.transform,
            "DoorAutoClose",
            WardLocalization.Localize(WardLocalization.UiAutoCloseToken, WardLocalization.UiAutoCloseFallback),
            gui);
        _autoCloseToggle.onValueChanged.AddListener(OnAutoCloseToggleChanged);
    }

    private Toggle CreateBehaviorToggleRow(Transform parent, string name, string labelText, GUIManager gui)
    {
        var cellSize = WardGuiLayoutSettings.GetRestrictionCellSize();
        var row = new GameObject(name, typeof(RectTransform), typeof(Image));
        row.transform.SetParent(parent, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cellSize.x);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cellSize.y);

        var image = row.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        var toggle = CreateCenteredToggle(
            row.transform,
            new Vector2(-cellSize.x * 0.5f + 28f, 0f),
            WardGuiLayoutSettings.GetBehaviorToggleSize());

        var labelObject = new GameObject("BehaviorName", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(row.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(-cellSize.x * 0.5f + 58f, 0f);
        labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, cellSize.x - 72f);
        labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, cellSize.y - 8f);

        var label = labelObject.GetComponent<Text>();
        gui.ApplyTextStyle(label, gui.AveriaSerifBold, gui.ValheimBeige, 20, false);
        label.text = labelText;
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;
        return toggle;
    }

    private void BuildTrustedPlayers(GUIManager gui)
    {
        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var permittedListPosition = WardGuiLayoutSettings.GetPermittedListPosition();
        var registeredPlayersHeaderPosition = WardGuiLayoutSettings.GetRegisteredPlayersHeaderPosition();
        var headerLabelSize = WardGuiLayoutSettings.GetPlayerListHeaderLabelSize();

        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiRegisteredPlayersToken, WardLocalization.UiRegisteredPlayersFallback),
            registeredPlayersHeaderPosition,
            24,
            headerLabelSize.x,
            headerLabelSize.y,
            TextAnchor.MiddleLeft,
            gui.AveriaSerifBold,
            gui.ValheimOrange);
        CreatePlayerSearchInput(
            WardGuiLayoutSettings.GetRegisteredPlayersSearchPosition(),
            _registeredPlayersSearchQuery,
            OnRegisteredPlayersSearchChanged,
            "STUWardRegisteredPlayersSearch");

        _permittedContent = CreatePlayerListContent(
            gui,
            "STUWardPermittedPlayers",
            permittedListPosition,
            permittedListSize);
        if (_permittedContent == null)
        {
            return;
        }
    }

    private void BuildRecentPlayers(GUIManager gui)
    {
        var listSize = WardGuiLayoutSettings.GetRecentPlayersListSize();
        var headerLabelSize = WardGuiLayoutSettings.GetPlayerListHeaderLabelSize();
        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiUnregisteredPlayersToken, WardLocalization.UiUnregisteredPlayersFallback),
            WardGuiLayoutSettings.GetRecentPlayersHeaderPosition(),
            24,
            headerLabelSize.x,
            headerLabelSize.y,
            TextAnchor.MiddleLeft,
            gui.AveriaSerifBold,
            gui.ValheimOrange);
        CreatePlayerSearchInput(
            WardGuiLayoutSettings.GetRecentPlayersSearchPosition(),
            _recentPlayersSearchQuery,
            OnRecentPlayersSearchChanged,
            "STUWardRecentPlayersSearch");

        _recentPlayersContent = CreatePlayerListContent(
            gui,
            "STUWardRecentPlayers",
            WardGuiLayoutSettings.GetRecentPlayersListPosition(),
            listSize);
        if (_recentPlayersContent == null)
        {
            return;
        }
        ShowRecentPlayersStatus(
            WardLocalization.Localize(WardLocalization.UiRecentPlayersLoadingToken, WardLocalization.UiRecentPlayersLoadingFallback),
            isError: false,
            RecentPlayersListState.Loading);
    }

    private RectTransform? CreatePlayerListContent(
        GUIManager gui,
        string objectName,
        Vector2 position,
        Vector2 size)
    {
        var scrollRoot = gui.CreateScrollView(
            _generalPageRoot!.transform,
            false,
            true,
            20f,
            6f,
            gui.ValheimScrollbarHandleColorBlock,
            new Color(0f, 0f, 0f, 0.35f),
            size.x,
            size.y);

        ConfigureRect(scrollRoot.GetComponent<RectTransform>(), position, size.x, size.y);
        scrollRoot.name = objectName;

        var content = scrollRoot.transform.Find("Scroll View/Viewport/Content") as RectTransform;
        if (content == null)
        {
            Plugin.Log.LogError($"Failed to find the Content object for {objectName}.");
            return null;
        }

        var layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            Plugin.Log.LogError($"Failed to find the VerticalLayoutGroup for {objectName}.");
            return null;
        }

        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 8, 8);
        return content;
    }

    private void RequestRecentPlayersSnapshot()
    {
        if (_currentWard == null || _recentPlayersContent == null)
        {
            return;
        }

        if (_recentPlayersListState != RecentPlayersListState.Loaded)
        {
            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiRecentPlayersLoadingToken, WardLocalization.UiRecentPlayersLoadingFallback),
                isError: false,
                RecentPlayersListState.Loading);
        }

        BeginRecentPlayersRequest();
        var requestId = WardRecentPlayers.RequestSnapshot(_currentWard);
        EndRecentPlayersRequest(requestId);
        if (requestId == 0L)
        {
            _pendingRecentPlayersRequestId = 0L;
            SetRecentPlayerRowsInteractable(true);
            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiRecentPlayersErrorToken, WardLocalization.UiRecentPlayersErrorFallback),
                isError: true,
                RecentPlayersListState.Error);
            return;
        }

        _pendingRecentPlayersRequestId = requestId;
        _recentPlayersRequestedAt = Time.unscaledTime;
        ApplyDeferredRecentPlayersSnapshot(requestId);
    }

    private void HandleRecentPlayersSnapshot(WardRecentPlayersSnapshot snapshot)
    {
        if (_recentPlayersRequestInProgress)
        {
            if (IsSnapshotForCurrentWard(snapshot))
            {
                _deferredRecentPlayersSnapshot = snapshot;
                _hasDeferredRecentPlayersSnapshot = true;
            }

            return;
        }

        ApplyRecentPlayersSnapshot(snapshot);
    }

    private void ApplyRecentPlayersSnapshot(WardRecentPlayersSnapshot snapshot)
    {
        if (_currentWard == null ||
            _recentPlayersContent == null ||
            snapshot.RequestId == 0L ||
            snapshot.RequestId != _pendingRecentPlayersRequestId ||
            !IsSnapshotForCurrentWard(snapshot))
        {
            return;
        }

        _pendingRecentPlayersRequestId = 0L;
        RefreshRegisteredPlayerActivity(snapshot.RegisteredActivity);
        RefreshRecentPlayers(snapshot.Players);
        SetRecentPlayerRowsInteractable(true);
        RefreshPermittedPlayers(force: true);
    }

    private void RefreshRegisteredPlayerActivity(IReadOnlyList<WardPlayerActivityEntry> activity)
    {
        _registeredPlayerActivity.Clear();
        for (var index = 0; index < activity.Count; index++)
        {
            var entry = activity[index];
            if (entry.PlayerId != 0L)
            {
                _registeredPlayerActivity[entry.PlayerId] = entry;
            }
        }
    }

    private bool IsSnapshotForCurrentWard(WardRecentPlayersSnapshot snapshot)
    {
        return _currentWard != null &&
               TryGetWardZdoId(_currentWard, out var currentWardZdoId) &&
               snapshot.WardZdoId == currentWardZdoId;
    }

    private void BeginRecentPlayersRequest()
    {
        _recentPlayersRequestInProgress = true;
        _hasDeferredRecentPlayersSnapshot = false;
        _deferredRecentPlayersSnapshot = default;
    }

    private void EndRecentPlayersRequest(long requestId)
    {
        _recentPlayersRequestInProgress = false;
        if (requestId == 0L)
        {
            _hasDeferredRecentPlayersSnapshot = false;
            _deferredRecentPlayersSnapshot = default;
        }
    }

    private void ApplyDeferredRecentPlayersSnapshot(long requestId)
    {
        if (!_hasDeferredRecentPlayersSnapshot)
        {
            return;
        }

        var snapshot = _deferredRecentPlayersSnapshot;
        _hasDeferredRecentPlayersSnapshot = false;
        _deferredRecentPlayersSnapshot = default;
        if (snapshot.RequestId == requestId)
        {
            ApplyRecentPlayersSnapshot(snapshot);
        }
    }

    private void RefreshRecentPlayers(IReadOnlyList<WardRecentPlayerEntry> players)
    {
        if (_recentPlayersContent == null)
        {
            return;
        }

        if (players.Count == 0)
        {
            foreach (var row in _recentPlayerRows.Values)
            {
                row.Root.SetActive(false);
            }

            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiNoUnregisteredPlayersToken, WardLocalization.UiNoUnregisteredPlayersFallback),
                isError: false,
                RecentPlayersListState.Empty);
            return;
        }

        if (_recentPlayersStatusRow != null)
        {
            _recentPlayersStatusRow.Root.SetActive(false);
        }

        _recentPlayersRefreshGeneration++;
        for (var index = 0; index < players.Count; index++)
        {
            var entry = players[index];
            if (!_recentPlayerRows.TryGetValue(entry.PlayerId, out var row))
            {
                row = CreateRecentPlayerRow(entry.PlayerId);
                _recentPlayerRows[entry.PlayerId] = row;
            }

            row.LastSeenGeneration = _recentPlayersRefreshGeneration;
            var displayName = string.IsNullOrWhiteSpace(entry.Name) ? entry.PlayerId.ToString() : entry.Name;
            row.NameText.text = BuildPlayerDisplayText(displayName, entry.GuildName, entry.AccountId);
            row.SearchText = BuildPlayerSearchText(entry.Name, entry.GuildName, entry.AccountId, entry.PlayerId);
            row.StatusText.text = BuildPlayerActivityStatus(entry.IsOnline, entry.LastSeenUtcTicks);
            row.Root.transform.SetSiblingIndex(index);
        }

        _recentPlayerRowsToRemove.Clear();
        foreach (var pair in _recentPlayerRows)
        {
            if (pair.Value.LastSeenGeneration != _recentPlayersRefreshGeneration)
            {
                _recentPlayerRowsToRemove.Add(pair.Key);
            }
        }

        for (var index = 0; index < _recentPlayerRowsToRemove.Count; index++)
        {
            var playerId = _recentPlayerRowsToRemove[index];
            if (_recentPlayerRows.TryGetValue(playerId, out var row))
            {
                Destroy(row.Root);
                _recentPlayerRows.Remove(playerId);
            }
        }

        _recentPlayersListState = RecentPlayersListState.Loaded;
        ApplyRecentPlayersFilter();
    }

    private void OnRecentPlayersSearchChanged(string query)
    {
        _recentPlayersSearchQuery = query ?? string.Empty;
        ApplyRecentPlayersFilter();
    }

    private void ApplyRecentPlayersFilter()
    {
        if (_recentPlayersContent == null || _recentPlayersListState != RecentPlayersListState.Loaded)
        {
            return;
        }

        if (_recentPlayersStatusRow != null)
        {
            _recentPlayersStatusRow.Root.SetActive(false);
        }

        var query = _recentPlayersSearchQuery.Trim();
        var visibleCount = 0;
        foreach (var row in _recentPlayerRows.Values)
        {
            var matches = MatchesPlayerSearch(row.SearchText, query);
            row.Root.SetActive(matches);
            if (matches)
            {
                visibleCount++;
            }
        }

        if (visibleCount == 0 && query.Length > 0)
        {
            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiNoMatchingPlayersToken, WardLocalization.UiNoMatchingPlayersFallback),
                isError: false,
                RecentPlayersListState.Loaded);
        }
    }

    private RecentPlayerRowView CreateRecentPlayerRow(long playerId)
    {
        var listSize = WardGuiLayoutSettings.GetRecentPlayersListSize();
        var rowWidth = Mathf.Max(560f, listSize.x - 72f);
        const float buttonWidth = 120f;

        var row = CreatePlayerRowRoot(_recentPlayersContent!, "RecentPlayerRow", rowWidth);

        var nameText = CreatePlayerRowText(
            row.transform,
            "PlayerName",
            new Vector2(-rowWidth * 0.5f + 10f, 0f),
            rowWidth - 318f,
            TextAnchor.MiddleLeft,
            GUIManager.Instance.ValheimBeige);
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var statusText = CreatePlayerRowText(
            row.transform,
            "PlayerStatus",
            new Vector2(rowWidth * 0.5f - buttonWidth - 164f, 0f),
            150f,
            TextAnchor.MiddleRight,
            GUIManager.Instance.ValheimYellow);

        var addButton = CreateAnchoredButton(
            row.transform,
            WardLocalization.Localize(WardLocalization.UiAddToken, WardLocalization.UiAddFallback),
            new Vector2(rowWidth * 0.5f - buttonWidth * 0.5f - 4f, 0f),
            buttonWidth,
            32f);
        addButton.onClick.AddListener(() => RequestAddRecentPlayer(playerId));

        return new RecentPlayerRowView(row, nameText, statusText, addButton);
    }

    private static GameObject CreatePlayerRowRoot(Transform parent, string name, float rowWidth)
    {
        const float rowHeight = 46f;
        var row = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(parent, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);
        row.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.18f);

        var rowLayout = row.GetComponent<LayoutElement>();
        rowLayout.preferredHeight = rowHeight;
        rowLayout.preferredWidth = rowWidth;
        return row;
    }

    private static Text CreatePlayerRowText(
        Transform parent,
        string name,
        Vector2 position,
        float width,
        TextAnchor alignment,
        Color color)
    {
        var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
        textObject.transform.SetParent(parent, false);
        var rect = textObject.GetComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0f, 0.5f);
        rect.anchoredPosition = position;
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 38f);

        var text = textObject.GetComponent<Text>();
        var gui = GUIManager.Instance;
        gui.ApplyTextStyle(text, gui.AveriaSerifBold, color, 18, false);
        text.alignment = alignment;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Truncate;
        return text;
    }

    private void RequestAddRecentPlayer(long playerId)
    {
        if (_currentWard == null)
        {
            return;
        }

        SetRecentPlayerRowsInteractable(false);
        BeginRecentPlayersRequest();
        var requestId = WardRecentPlayers.RequestAdd(_currentWard, playerId);
        EndRecentPlayersRequest(requestId);
        if (requestId == 0L)
        {
            _pendingRecentPlayersRequestId = 0L;
            SetRecentPlayerRowsInteractable(true);
            ShowRecentPlayersStatus(
                WardLocalization.Localize(WardLocalization.UiRecentPlayersErrorToken, WardLocalization.UiRecentPlayersErrorFallback),
                isError: true,
                RecentPlayersListState.Error);
            return;
        }

        _pendingRecentPlayersRequestId = requestId;
        _recentPlayersRequestedAt = Time.unscaledTime;
        ApplyDeferredRecentPlayersSnapshot(requestId);
    }

    private void ShowRecentPlayersStatus(string text, bool isError, RecentPlayersListState state)
    {
        if (_recentPlayersContent == null)
        {
            return;
        }

        foreach (var row in _recentPlayerRows.Values)
        {
            row.Root.SetActive(false);
        }

        EnsureRecentPlayersStatusRow();
        _recentPlayersStatusRow!.Text.text = text;
        _recentPlayersStatusRow.Text.color = isError
            ? new Color(0.85f, 0.35f, 0.25f)
            : GUIManager.Instance.ValheimBeige;
        _recentPlayersStatusRow.Root.transform.SetSiblingIndex(0);
        _recentPlayersStatusRow.Root.SetActive(true);
        _recentPlayersListState = state;
    }

    private void SetRecentPlayerRowsInteractable(bool interactable)
    {
        foreach (var row in _recentPlayerRows.Values)
        {
            row.AddButton.interactable = interactable;
        }
    }

    private void EnsureRecentPlayersStatusRow()
    {
        if (_recentPlayersStatusRow != null || _recentPlayersContent == null)
        {
            return;
        }

        var listSize = WardGuiLayoutSettings.GetRecentPlayersListSize();
        var rowWidth = Mathf.Max(560f, listSize.x - 72f);
        var root = CreatePlayerRowRoot(_recentPlayersContent!, "RecentPlayersStatusRow", rowWidth);

        var text = CreatePlayerRowText(
            root.transform,
            "Status",
            new Vector2(-rowWidth * 0.5f + 10f, 0f),
            rowWidth - 24f,
            TextAnchor.MiddleLeft,
            GUIManager.Instance.ValheimBeige);
        _recentPlayersStatusRow = new StatusRowView(root, text);
    }

    private static string BuildPlayerActivityStatus(bool isOnline, long lastSeenUtcTicks)
    {
        if (isOnline)
        {
            return WardLocalization.Localize(WardLocalization.UiOnlineToken, WardLocalization.UiOnlineFallback);
        }

        if (lastSeenUtcTicks <= 0L || lastSeenUtcTicks > System.DateTime.MaxValue.Ticks)
        {
            return WardLocalization.Localize(
                WardLocalization.UiLastSeenUnavailableToken,
                WardLocalization.UiLastSeenUnavailableFallback);
        }

        var lastSeenUtc = new System.DateTime(lastSeenUtcTicks, System.DateTimeKind.Utc);
        var elapsed = System.DateTime.UtcNow - lastSeenUtc;
        if (elapsed < System.TimeSpan.Zero || elapsed.TotalMinutes < 1d)
        {
            return WardLocalization.Localize(WardLocalization.UiLastSeenJustNowToken, WardLocalization.UiLastSeenJustNowFallback);
        }

        if (elapsed.TotalHours < 1d)
        {
            return WardLocalization.LocalizeFormat(
                WardLocalization.UiLastSeenMinutesAgoToken,
                WardLocalization.UiLastSeenMinutesAgoFallback,
                Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalMinutes)));
        }

        if (elapsed.TotalDays < 1d)
        {
            return WardLocalization.LocalizeFormat(
                WardLocalization.UiLastSeenHoursAgoToken,
                WardLocalization.UiLastSeenHoursAgoFallback,
                Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalHours)));
        }

        return WardLocalization.LocalizeFormat(
            WardLocalization.UiLastSeenDaysAgoToken,
            WardLocalization.UiLastSeenDaysAgoFallback,
            Mathf.Max(1, Mathf.FloorToInt((float)elapsed.TotalDays)));
    }

    private static bool TryGetWardZdoId(PrivateArea ward, out ZDOID wardZdoId)
    {
        wardZdoId = ZDOID.None;
        var zdo = ward.m_nview != null && ward.m_nview.IsValid() ? ward.m_nview.GetZDO() : null;
        if (zdo == null)
        {
            return false;
        }

        wardZdoId = zdo.m_uid;
        return wardZdoId != ZDOID.None;
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        if (_root != null)
        {
            _root.SetActive(visible);
        }

        GUIManager.BlockInput(visible);
    }

    private void RefreshStaticTexts()
    {
        if (_currentWard == null || _ownerValueText == null || _guildValueText == null)
        {
            return;
        }

        _ownerValueText.text = WardLocalization.LocalizeFormat(
            WardLocalization.UiOwnerToken,
            WardLocalization.UiOwnerFallback,
            WardPrivateAreaSafeAccess.GetCreatorName(_currentWard));
        var guildName = GuildsCompat.GetWardGuildName(_currentWard);
        _guildValueText.text = WardLocalization.LocalizeFormat(
            WardLocalization.UiGuildToken,
            WardLocalization.UiGuildFallback,
            string.IsNullOrWhiteSpace(guildName) ? "-" : guildName);
    }

    private void RefreshControls()
    {
        if (_autoCloseToggle == null ||
            _warningSoundToggle == null ||
            _warningFlashToggle == null ||
            _areaMarkerRotationToggle == null)
        {
            return;
        }

        _suppressUiEvents = true;
        _autoCloseToggle.isOn = _currentConfiguration.AutoCloseEnabled;
        _warningSoundToggle.isOn = _currentConfiguration.WarningSoundEnabled;
        _warningFlashToggle.isOn = _currentConfiguration.WarningFlashEnabled;
        _areaMarkerRotationToggle.isOn = _currentConfiguration.AreaMarkerRotationEnabled;
        RefreshRestrictionRows();
        _suppressUiEvents = false;
    }

    private void RefreshPermittedPlayers(bool force)
    {
        if (_currentWard == null || _permittedContent == null)
        {
            return;
        }

        var currentRevision = WardPermittedSnapshots.GetRevision(_currentWard);
        if (!force && currentRevision == _lastPermittedRevision)
        {
            return;
        }

        _lastPermittedRevision = currentRevision;
        var permittedPlayers = WardPrivateAreaSafeAccess.GetPermittedPlayers(_currentWard);
        if (permittedPlayers.Count == 0)
        {
            ClearPermittedRows();
            EnsureEmptyPermittedRow();
            UpdateText(
                _emptyPermittedRow!.Text,
                WardLocalization.Localize(WardLocalization.UiNoRegisteredPlayersToken, WardLocalization.UiNoRegisteredPlayersFallback));
            _emptyPermittedRow!.Root.SetActive(true);
            _emptyPermittedRow.Root.transform.SetSiblingIndex(0);
            return;
        }

        if (_emptyPermittedRow != null)
        {
            _emptyPermittedRow.Root.SetActive(false);
        }

        permittedPlayers.Sort((left, right) => string.Compare(left.Value, right.Value, System.StringComparison.OrdinalIgnoreCase));
        _permittedRefreshGeneration++;
        for (var index = 0; index < permittedPlayers.Count; index++)
        {
            var entry = permittedPlayers[index];
            if (!_permittedRows.TryGetValue(entry.Key, out var row))
            {
                row = CreatePermittedRow(entry.Key);
                _permittedRows[entry.Key] = row;
            }

            row.LastSeenGeneration = _permittedRefreshGeneration;
            GetPermittedPlayerIdentity(_currentWard, entry.Key, out var guildName, out var accountId);
            var displayName = string.IsNullOrWhiteSpace(entry.Value) ? entry.Key.ToString() : entry.Value;
            UpdateText(row.NameText, BuildPlayerDisplayText(displayName, guildName, accountId));
            row.StatusText.text = _registeredPlayerActivity.TryGetValue(entry.Key, out var activity)
                ? BuildPlayerActivityStatus(activity.IsOnline, activity.LastSeenUtcTicks)
                : BuildPlayerActivityStatus(isOnline: false, lastSeenUtcTicks: 0L);
            row.SearchText = BuildPlayerSearchText(entry.Value, guildName, accountId, entry.Key);
            row.Root.transform.SetSiblingIndex(index);
        }

        _permittedRowsToRemove.Clear();
        foreach (var pair in _permittedRows)
        {
            if (pair.Value.LastSeenGeneration != _permittedRefreshGeneration)
            {
                _permittedRowsToRemove.Add(pair.Key);
            }
        }

        for (var index = 0; index < _permittedRowsToRemove.Count; index++)
        {
            var playerId = _permittedRowsToRemove[index];
            if (!_permittedRows.TryGetValue(playerId, out var row))
            {
                continue;
            }

            Destroy(row.Root);
            _permittedRows.Remove(playerId);
        }

        ApplyRegisteredPlayersFilter();
    }

    private void OnRegisteredPlayersSearchChanged(string query)
    {
        _registeredPlayersSearchQuery = query ?? string.Empty;
        ApplyRegisteredPlayersFilter();
    }

    private void ApplyRegisteredPlayersFilter()
    {
        if (_permittedContent == null || _permittedRows.Count == 0)
        {
            return;
        }

        var query = _registeredPlayersSearchQuery.Trim();
        var visibleCount = 0;
        foreach (var row in _permittedRows.Values)
        {
            var matches = MatchesPlayerSearch(row.SearchText, query);
            row.Root.SetActive(matches);
            if (matches)
            {
                visibleCount++;
            }
        }

        if (_emptyPermittedRow == null)
        {
            EnsureEmptyPermittedRow();
        }

        if (visibleCount == 0 && query.Length > 0)
        {
            UpdateText(
                _emptyPermittedRow!.Text,
                WardLocalization.Localize(WardLocalization.UiNoMatchingPlayersToken, WardLocalization.UiNoMatchingPlayersFallback));
            _emptyPermittedRow!.Root.transform.SetSiblingIndex(_permittedRows.Count);
            _emptyPermittedRow.Root.SetActive(true);
        }
        else if (_emptyPermittedRow != null)
        {
            _emptyPermittedRow.Root.SetActive(false);
        }
    }

    private PermittedRowView CreatePermittedRow(long playerId)
    {
        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var rowWidth = Mathf.Max(560f, permittedListSize.x - 72f);
        const float buttonWidth = 130f;
        const float statusWidth = 150f;
        const float columnSpacing = 12f;
        const float statusButtonSpacing = 10f;

        var row = CreatePlayerRowRoot(_permittedContent!, "PermittedPlayerRow", rowWidth);

        const float leftPadding = 10f;
        var removeButtonPosition = WardGuiLayoutSettings.GetRegisteredPlayersRemoveButtonPosition();
        var clampedButtonX = Mathf.Clamp(
            removeButtonPosition.x,
            -rowWidth * 0.5f + buttonWidth * 0.5f + 10f,
            rowWidth * 0.5f - buttonWidth * 0.5f - 4f);
        var nameLeftEdge = -rowWidth * 0.5f + leftPadding;
        var statusRightEdge = clampedButtonX - buttonWidth * 0.5f - statusButtonSpacing;
        var statusLeftEdge = statusRightEdge - statusWidth;
        var nameWidth = Mathf.Max(220f, statusLeftEdge - columnSpacing - nameLeftEdge);
        var nameText = CreatePlayerRowText(
            row.transform,
            "PlayerName",
            new Vector2(nameLeftEdge, 0f),
            nameWidth,
            TextAnchor.MiddleLeft,
            GUIManager.Instance.ValheimBeige);
        nameText.horizontalOverflow = HorizontalWrapMode.Wrap;
        var statusText = CreatePlayerRowText(
            row.transform,
            "PlayerStatus",
            new Vector2(statusLeftEdge, 0f),
            statusWidth,
            TextAnchor.MiddleRight,
            GUIManager.Instance.ValheimYellow);
        statusText.resizeTextForBestFit = true;
        statusText.resizeTextMinSize = 13;
        statusText.resizeTextMaxSize = 18;

        var removeButton = CreateAnchoredButton(
            row.transform,
            WardLocalization.Localize(WardLocalization.UiRemoveToken, WardLocalization.UiRemoveFallback),
            new Vector2(clampedButtonX, removeButtonPosition.y),
            buttonWidth,
            32f);
        removeButton.onClick.AddListener(() =>
        {
            if (_currentWard == null)
            {
                return;
            }

            WardSettings.RequestRemovePermitted(_currentWard, playerId);
            RequestRecentPlayersSnapshot();
        });

        return new PermittedRowView(row, nameText, statusText);
    }

    private void EnsureEmptyPermittedRow()
    {
        if (_emptyPermittedRow != null)
        {
            return;
        }

        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var rowWidth = Mathf.Max(560f, permittedListSize.x - 72f);
        var row = CreatePlayerRowRoot(_permittedContent!, "PermittedPlayerRowEmpty", rowWidth);

        var nameText = CreatePlayerRowText(
            row.transform,
            "PlayerName",
            new Vector2(-rowWidth * 0.5f + 10f, 0f),
            rowWidth - 24f,
            TextAnchor.MiddleLeft,
            GUIManager.Instance.ValheimBeige);

        _emptyPermittedRow = new StatusRowView(row, nameText);
    }

    private static void UpdateText(Text target, string text)
    {
        if (!string.Equals(target.text, text, System.StringComparison.Ordinal))
        {
            target.text = text;
        }
    }

    private void ClearPermittedRows()
    {
        foreach (var row in _permittedRows.Values)
        {
            Destroy(row.Root);
        }

        _permittedRows.Clear();
        _permittedRowsToRemove.Clear();
        _permittedRefreshGeneration = 0;

        if (_emptyPermittedRow != null)
        {
            Destroy(_emptyPermittedRow.Root);
            _emptyPermittedRow = null;
        }
    }

    private void ClearRecentPlayerRows()
    {
        foreach (var row in _recentPlayerRows.Values)
        {
            Destroy(row.Root);
        }

        _recentPlayerRows.Clear();
        _recentPlayerRowsToRemove.Clear();
        _recentPlayersRefreshGeneration = 0;
        _pendingRecentPlayersRequestId = 0L;
        _recentPlayersRequestInProgress = false;
        _hasDeferredRecentPlayersSnapshot = false;
        _deferredRecentPlayersSnapshot = default;
        _recentPlayersListState = RecentPlayersListState.None;

        if (_recentPlayersStatusRow != null)
        {
            Destroy(_recentPlayersStatusRow.Root);
            _recentPlayersStatusRow = null;
        }
    }

    private void BuildRestrictions()
    {
        var gui = GUIManager.Instance;
        var listSize = WardGuiLayoutSettings.GetRestrictionListSize();
        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiRestrictionsToken, WardLocalization.UiRestrictionsFallback),
            WardGuiLayoutSettings.GetRestrictionsHeaderPosition(),
            24,
            listSize.x,
            40f,
            TextAnchor.MiddleCenter,
            gui.AveriaSerifBold,
            gui.ValheimOrange);

        var scrollRoot = gui.CreateScrollView(
            _restrictionsPageRoot!.transform,
            false,
            true,
            20f,
            6f,
            gui.ValheimScrollbarHandleColorBlock,
            new Color(0f, 0f, 0f, 0.35f),
            listSize.x,
            listSize.y);

        ConfigureRect(scrollRoot.GetComponent<RectTransform>(), WardGuiLayoutSettings.GetRestrictionListPosition(), listSize.x, listSize.y);
        scrollRoot.name = "STUWardRestrictions";

        _restrictionsContent = scrollRoot.transform.Find("Scroll View/Viewport/Content") as RectTransform;
        if (_restrictionsContent == null)
        {
            return;
        }

        var verticalLayout = _restrictionsContent.GetComponent<VerticalLayoutGroup>();
        if (verticalLayout != null)
        {
            verticalLayout.enabled = false;
            // Unity delays Destroy until the end of the frame, but only one
            // LayoutGroup may exist on this object. Remove Jotunn's generated
            // layout immediately before replacing it with the grid.
            DestroyImmediate(verticalLayout);
        }

        var layout = _restrictionsContent.gameObject.AddComponent<GridLayoutGroup>();
        if (layout == null)
        {
            Plugin.Log.LogError("Failed to create the ward restrictions grid layout.");
            return;
        }

        layout.startCorner = GridLayoutGroup.Corner.UpperLeft;
        layout.startAxis = GridLayoutGroup.Axis.Vertical;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.cellSize = WardGuiLayoutSettings.GetRestrictionCellSize();
        layout.spacing = WardGuiLayoutSettings.GetRestrictionCellSpacing();
        layout.padding = new RectOffset(8, 8, 8, 8);
        layout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        layout.constraintCount = 2;

        var definitions = WardSettings.RestrictionDefinitions;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            _restrictionRows[definition.Restriction] = CreateRestrictionRow(definition);
        }
    }

    private RestrictionRowView CreateRestrictionRow(WardRestrictionDefinition definition)
    {
        var cellSize = WardGuiLayoutSettings.GetRestrictionCellSize();
        var rowWidth = cellSize.x;
        var rowHeight = cellSize.y;

        var row = new GameObject("RestrictionRow", typeof(RectTransform), typeof(Image));
        row.transform.SetParent(_restrictionsContent, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);

        var image = row.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        var toggle = CreateCenteredToggle(row.transform, new Vector2(-rowWidth * 0.5f + 28f, 0f), 30f);
        var restriction = definition.Restriction;
        toggle.onValueChanged.AddListener(enabled => OnRestrictionToggleChanged(restriction, enabled));

        var labelObject = new GameObject("RestrictionName", typeof(RectTransform), typeof(Text));
        labelObject.transform.SetParent(row.transform, false);
        var labelRect = labelObject.GetComponent<RectTransform>();
        labelRect.anchorMin = new Vector2(0.5f, 0.5f);
        labelRect.anchorMax = new Vector2(0.5f, 0.5f);
        labelRect.pivot = new Vector2(0f, 0.5f);
        labelRect.anchoredPosition = new Vector2(-rowWidth * 0.5f + 58f, 0f);
        labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth - 220f);
        labelRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight - 8f);

        var label = labelObject.GetComponent<Text>();
        var gui = GUIManager.Instance;
        gui.ApplyTextStyle(label, gui.AveriaSerifBold, gui.ValheimBeige, 20, false);
        label.text = WardLocalization.Localize(definition.LocalizationToken, definition.LocalizationFallback);
        label.alignment = TextAnchor.MiddleLeft;
        label.horizontalOverflow = HorizontalWrapMode.Wrap;
        label.verticalOverflow = VerticalWrapMode.Truncate;

        var stateObject = new GameObject("RestrictionState", typeof(RectTransform), typeof(Text));
        stateObject.transform.SetParent(row.transform, false);
        var stateRect = stateObject.GetComponent<RectTransform>();
        stateRect.anchorMin = new Vector2(0.5f, 0.5f);
        stateRect.anchorMax = new Vector2(0.5f, 0.5f);
        stateRect.pivot = new Vector2(1f, 0.5f);
        stateRect.anchoredPosition = new Vector2(rowWidth * 0.5f - 14f, 0f);
        stateRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 130f);
        stateRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight - 8f);

        var stateText = stateObject.GetComponent<Text>();
        gui.ApplyTextStyle(stateText, gui.AveriaSerifBold, gui.ValheimYellow, 18, false);
        stateText.alignment = TextAnchor.MiddleRight;
        stateText.horizontalOverflow = HorizontalWrapMode.Overflow;
        stateText.verticalOverflow = VerticalWrapMode.Truncate;

        return new RestrictionRowView(row, toggle, label, stateText);
    }

    private void RefreshRestrictionRows()
    {
        var gui = GUIManager.Instance;
        var definitions = WardSettings.RestrictionDefinitions;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            if (!_restrictionRows.TryGetValue(definition.Restriction, out var row))
            {
                continue;
            }

            var forced = WardSettings.IsRestrictionForced(definition.Restriction);
            row.Toggle.isOn = WardSettings.HasRestriction(_currentConfiguration, definition.Restriction);
            row.Toggle.interactable = !forced;
            row.Label.color = forced ? new Color(0.65f, 0.62f, 0.55f) : gui.ValheimBeige;
            row.StateText.text = forced
                ? WardLocalization.Localize(WardLocalization.UiRestrictionForcedToken, WardLocalization.UiRestrictionForcedFallback)
                : string.Empty;
        }
    }

    private void OnRestrictionToggleChanged(WardRestrictionOptions restriction, bool enabled)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        if (WardSettings.IsRestrictionForced(restriction))
        {
            RefreshControls();
            return;
        }

        ApplyConfigurationDraft(WardSettings.WithRestriction(_currentConfiguration, restriction, enabled));
    }

    private void OnAutoCloseToggleChanged(bool enabled)
    {
        ApplyConfigurationDraft(WardSettings.WithAutoCloseEnabled(_currentConfiguration, enabled));
    }

    private void OnWarningSoundToggleChanged(bool enabled)
    {
        ApplyConfigurationDraft(WardSettings.WithWarningSoundEnabled(_currentConfiguration, enabled));
    }

    private void OnWarningFlashToggleChanged(bool enabled)
    {
        ApplyConfigurationDraft(WardSettings.WithWarningFlashEnabled(_currentConfiguration, enabled));
    }

    private void OnAreaMarkerRotationToggleChanged(bool enabled)
    {
        ApplyConfigurationDraft(WardSettings.WithAreaMarkerRotationEnabled(_currentConfiguration, enabled));
    }

    private void ApplyConfigurationDraft(WardConfiguration configuration)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = configuration;
        RefreshControls();
        ScheduleConfigurationPush();
    }

    private void PushConfiguration()
    {
        if (_currentWard == null)
        {
            return;
        }

        var submittedConfiguration = _currentConfiguration;
        _configurationPushPending = false;
        var submission = WardSettings.RequestUpdateConfiguration(_currentWard, submittedConfiguration);
        if (submission.IsPending)
        {
            BeginPendingConfigurationRequest(submission.RequestId, submittedConfiguration);
            return;
        }

        WardSettings.ShowConfigurationRequestFeedback(submission.ResultCode);
        ApplyConfigurationResponse(0L, submission.ResultCode, submission.Configuration);
    }

    private void ScheduleConfigurationPush()
    {
        if (_suppressUiEvents || _currentWard == null)
        {
            return;
        }

        _configurationPushPending = true;
        _nextConfigurationPushTime = Time.unscaledTime + ConfigurationPushDebounceSeconds;
    }

    private void FlushPendingConfigurationPush()
    {
        if (!_configurationPushPending)
        {
            return;
        }

        PushPendingConfiguration();
    }

    private void PushPendingConfiguration()
    {
        if (_suppressUiEvents ||
            _currentWard == null ||
            !_configurationPushPending ||
            HasPendingConfigurationRequest())
        {
            return;
        }

        PushConfiguration();
    }

    internal void HandleWardConfigurationResponse(
        PrivateArea ward,
        long requestId,
        WardConfigurationRequestResultCode resultCode,
        WardConfiguration configuration)
    {
        if (_currentWard == null || ward != _currentWard)
        {
            return;
        }

        ApplyConfigurationResponse(requestId, resultCode, configuration);
    }

    private bool HasPendingConfigurationRequest()
    {
        return _pendingConfigurationRequestId != 0L;
    }

    private void BeginPendingConfigurationRequest(long requestId, WardConfiguration submittedConfiguration)
    {
        _pendingConfigurationRequestId = requestId;
        _pendingConfiguration = submittedConfiguration;
        _pendingConfigurationRequestedAt = Time.unscaledTime;
    }

    private void ClearPendingConfigurationRequest()
    {
        _pendingConfigurationRequestId = 0L;
        _pendingConfiguration = default;
        _pendingConfigurationRequestedAt = 0f;
    }

    private void ApplyConfigurationResponse(
        long requestId,
        WardConfigurationRequestResultCode resultCode,
        WardConfiguration configuration)
    {
        var hadPendingRequest = HasPendingConfigurationRequest();
        if (requestId != 0L && (!hadPendingRequest || requestId != _pendingConfigurationRequestId))
        {
            return;
        }

        var draftChangedSinceRequest = hadPendingRequest &&
                                       !WardSettings.ConfigurationsMatch(_currentConfiguration, _pendingConfiguration);
        _authoritativeConfiguration = configuration;
        if (hadPendingRequest)
        {
            ClearPendingConfigurationRequest();
        }

        var failed = resultCode is WardConfigurationRequestResultCode.Denied or WardConfigurationRequestResultCode.InvalidPayload or WardConfigurationRequestResultCode.InvalidState;
        if (failed || !draftChangedSinceRequest)
        {
            _currentConfiguration = configuration;
        }

        if (failed)
        {
            _configurationPushPending = false;
        }

        RefreshControls();
        TryFlushDeferredConfigurationAfterRequestResolution();
    }

    private void RefreshAuthoritativeConfigurationFromWard()
    {
        if (_currentWard == null)
        {
            return;
        }

        var authoritativeConfiguration = WardSettings.GetConfiguration(_currentWard);
        if (WardSettings.ConfigurationsMatch(_authoritativeConfiguration, authoritativeConfiguration))
        {
            return;
        }

        _authoritativeConfiguration = authoritativeConfiguration;
        if (_configurationPushPending)
        {
            return;
        }

        if (!WardSettings.ConfigurationsMatch(_currentConfiguration, authoritativeConfiguration))
        {
            _currentConfiguration = authoritativeConfiguration;
            RefreshControls();
        }
    }

    private void HandlePendingConfigurationRequestTimeout()
    {
        if (_currentWard == null || !HasPendingConfigurationRequest())
        {
            return;
        }

        Plugin.Log.LogWarning($"Timed out waiting for ward configuration response for ward instanceId={_currentWard.GetInstanceID()} requestId={_pendingConfigurationRequestId}.");
        var draftChangedSinceRequest = !WardSettings.ConfigurationsMatch(_currentConfiguration, _pendingConfiguration);
        _authoritativeConfiguration = WardSettings.GetConfiguration(_currentWard);
        ClearPendingConfigurationRequest();
        if (!draftChangedSinceRequest)
        {
            _currentConfiguration = _authoritativeConfiguration;
            RefreshControls();
        }

        TryFlushDeferredConfigurationAfterRequestResolution();
    }

    private void TryFlushDeferredConfigurationAfterRequestResolution()
    {
        if (HasPendingConfigurationRequest())
        {
            return;
        }

        if (_configurationPushPending &&
            (_closeRequested || Time.unscaledTime >= _nextConfigurationPushTime))
        {
            PushPendingConfiguration();
        }


        if (_closeRequested &&
            !HasPendingConfigurationRequest() &&
            !_configurationPushPending)
        {
            CompleteCloseWardUi();
        }
    }

    private GameObject CreatePageRoot(string name, Vector2 panelSize)
    {
        var pageRoot = new GameObject(name, typeof(RectTransform));
        pageRoot.transform.SetParent(_panel!.transform, false);
        ConfigureRect(pageRoot.GetComponent<RectTransform>(), Vector2.zero, panelSize.x, panelSize.y);
        return pageRoot;
    }

    private Transform GetBuildParent()
    {
        return _buildParent != null ? _buildParent : _panel!.transform;
    }

    private Button CreateButton(string text, Vector2 position, float width, float height)
    {
        var buttonObject = GUIManager.Instance.CreateButton(
            text,
            _panel!.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            position,
            width,
            height);
        return buttonObject.GetComponent<Button>();
    }

    private Button CreateAnchoredButton(Transform parent, string text, Vector2 position, float width, float height)
    {
        var buttonObject = GUIManager.Instance.CreateButton(
            text,
            parent,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            position,
            width,
            height);
        return buttonObject.GetComponent<Button>();
    }

    private Toggle CreateCenteredToggle(Transform parent, Vector2 position, float boxSize)
    {
        return CreateAnchoredToggle(parent, position, boxSize, centerGraphic: true, graphicYOffset: 0f);
    }

    private Toggle CreateAnchoredToggle(Transform parent, Vector2 position, float boxSize, bool centerGraphic = false, float graphicYOffset = 0f)
    {
        var toggleObject = DefaultControls.CreateToggle(new DefaultControls.Resources());
        toggleObject.transform.SetParent(parent, false);
        toggleObject.name = "STUWardToggle";

        var toggleRect = toggleObject.GetComponent<RectTransform>();
        ConfigureRect(toggleRect, position, boxSize, boxSize);

        var toggle = toggleObject.GetComponent<Toggle>();
        var background = toggleObject.transform.Find("Background")?.GetComponent<Image>();
        if (background != null)
        {
            background.color = new Color(0f, 0f, 0f, 0.6f);
            if (background.transform is RectTransform backgroundRect)
            {
                if (centerGraphic)
                {
                    backgroundRect.anchorMin = new Vector2(0.5f, 0.5f);
                    backgroundRect.anchorMax = new Vector2(0.5f, 0.5f);
                    backgroundRect.pivot = new Vector2(0.5f, 0.5f);
                }

                backgroundRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, boxSize);
                backgroundRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, boxSize);
                backgroundRect.anchoredPosition = centerGraphic ? new Vector2(0f, graphicYOffset) : Vector2.zero;
            }
        }

        var checkmark = toggleObject.transform.Find("Background/Checkmark")?.GetComponent<Image>();
        if (checkmark != null)
        {
            checkmark.color = GUIManager.Instance.ValheimOrange;
            if (checkmark.transform is RectTransform checkmarkRect)
            {
                var innerSize = Mathf.Max(4f, boxSize - 6f);
                if (centerGraphic)
                {
                    checkmarkRect.anchorMin = new Vector2(0.5f, 0.5f);
                    checkmarkRect.anchorMax = new Vector2(0.5f, 0.5f);
                    checkmarkRect.pivot = new Vector2(0.5f, 0.5f);
                }

                checkmarkRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, innerSize);
                checkmarkRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, innerSize);
                checkmarkRect.anchoredPosition = Vector2.zero;
            }
        }

        var label = toggleObject.transform.Find("Label");
        if (label != null)
        {
            label.gameObject.SetActive(false);
        }

        return toggle;
    }

    private Text CreateLabel(
        string text,
        Vector2 position,
        int fontSize,
        float width,
        float height,
        TextAnchor alignment,
        Font font,
        Color color)
    {
        var labelObject = GUIManager.Instance.CreateText(
            text,
            GetBuildParent(),
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            position,
            font,
            fontSize,
            color,
            true,
            Color.black,
            width,
            height,
            false);

        var label = labelObject.GetComponent<Text>();
        label.alignment = alignment;
        return label;
    }

    private void CreatePlayerSearchInput(
        Vector2 position,
        string query,
        System.Action<string> onValueChanged,
        string objectName)
    {
        var size = WardGuiLayoutSettings.GetPlayerSearchSize();
        var inputObject = GUIManager.Instance.CreateInputField(
            _generalPageRoot!.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            position,
            InputField.ContentType.Standard,
            WardLocalization.Localize(WardLocalization.UiSearchPlayersToken, WardLocalization.UiSearchPlayersFallback),
            18,
            size.x,
            size.y);
        inputObject.name = objectName;
        var input = inputObject.GetComponent<InputField>();
        input.lineType = InputField.LineType.SingleLine;
        input.text = query;
        input.onValueChanged.AddListener(value => onValueChanged(value));
    }

    private void SetActivePage(WardSettingsPage page)
    {
        var returnedToGeneralPage = page == WardSettingsPage.General && _currentPage != WardSettingsPage.General;
        _currentPage = page;
        if (_generalPageRoot != null)
        {
            _generalPageRoot.SetActive(page == WardSettingsPage.General);
        }

        if (_restrictionsPageRoot != null)
        {
            _restrictionsPageRoot.SetActive(page == WardSettingsPage.Restrictions);
        }

        if (_previousPageButton != null)
        {
            _previousPageButton.gameObject.SetActive(page == WardSettingsPage.Restrictions);
        }

        if (_nextPageButton != null)
        {
            _nextPageButton.gameObject.SetActive(page == WardSettingsPage.General);
        }

        if (returnedToGeneralPage && _visible && _currentWard != null)
        {
            RequestRecentPlayersSnapshot();
        }
    }

    private static void StylePageArrowButton(Button? button)
    {
        var text = button != null ? button.GetComponentInChildren<Text>() : null;
        if (text == null)
        {
            return;
        }

        text.text = text.text.Trim();
        text.fontSize = 34;
        text.color = GUIManager.Instance.ValheimYellow;
        text.alignment = TextAnchor.MiddleCenter;
        text.rectTransform.anchoredPosition += new Vector2(0f, 1f);
    }

    private static void ConfigureRect(RectTransform? rectTransform, Vector2 position, float width, float height)
    {
        if (rectTransform == null)
        {
            return;
        }

        rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
        rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        rectTransform.pivot = new Vector2(0.5f, 0.5f);
        rectTransform.anchoredPosition = position;
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        rectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
    }

    private static string BuildPlayerDisplayText(string playerName, string guildName, string accountId)
    {
        var guildDisplay = string.IsNullOrWhiteSpace(guildName) ? "-" : guildName;
        var accountDisplay = string.IsNullOrWhiteSpace(accountId) ? "-" : accountId;
        return WardLocalization.LocalizeFormat(
            WardLocalization.UiRegisteredPlayerFormatToken,
            WardLocalization.UiRegisteredPlayerFormatFallback,
            playerName,
            guildDisplay,
            accountDisplay);
    }

    private static string BuildPlayerSearchText(string playerName, string guildName, string accountId, long playerId)
    {
        return $"{playerName}\n{guildName}\n{accountId}\n{playerId}";
    }

    private static bool MatchesPlayerSearch(string searchText, string query)
    {
        return query.Length == 0 ||
               searchText.IndexOf(query, System.StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void GetPermittedPlayerIdentity(
        PrivateArea? area,
        long playerId,
        out string guildName,
        out string accountId)
    {
        if (WardPermittedSnapshots.TryGet(area, playerId, out guildName, out accountId))
        {
            return;
        }

        guildName = GuildsCompat.GetPlayerGuildName(playerId);
        accountId = WardOwnership.GetPlayerSteamIdDisplay(playerId);
    }

    private sealed class PermittedRowView
    {
        internal PermittedRowView(GameObject root, Text nameText, Text statusText)
        {
            Root = root;
            NameText = nameText;
            StatusText = statusText;
        }

        internal GameObject Root { get; }

        internal Text NameText { get; }

        internal Text StatusText { get; }

        internal string SearchText { get; set; } = string.Empty;

        internal int LastSeenGeneration { get; set; }
    }

    private sealed class RecentPlayerRowView
    {
        internal RecentPlayerRowView(GameObject root, Text nameText, Text statusText, Button addButton)
        {
            Root = root;
            NameText = nameText;
            StatusText = statusText;
            AddButton = addButton;
        }

        internal GameObject Root { get; }
        internal Text NameText { get; }
        internal Text StatusText { get; }
        internal Button AddButton { get; }
        internal string SearchText { get; set; } = string.Empty;
        internal int LastSeenGeneration { get; set; }
    }

    private sealed class StatusRowView
    {
        internal StatusRowView(GameObject root, Text text)
        {
            Root = root;
            Text = text;
        }

        internal GameObject Root { get; }
        internal Text Text { get; }
    }

    private enum WardSettingsPage
    {
        General,
        Restrictions
    }

    private enum RecentPlayersListState
    {
        None,
        Loading,
        Loaded,
        Empty,
        Error
    }

    private sealed class RestrictionRowView
    {
        internal RestrictionRowView(GameObject root, Toggle toggle, Text label, Text stateText)
        {
            Root = root;
            Toggle = toggle;
            Label = label;
            StateText = stateText;
        }

        internal GameObject Root { get; }
        internal Toggle Toggle { get; }
        internal Text Label { get; }
        internal Text StateText { get; }
    }
}
