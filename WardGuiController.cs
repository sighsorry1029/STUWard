using System.Collections;
using System.Collections.Generic;
using Jotunn.Managers;
using LocalizationManager;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace STUWard;

internal sealed class WardGuiController : MonoBehaviour
{
    private const float ConfigurationPushDebounceSeconds = 0.15f;
    private const float ConfigurationRequestTimeoutSeconds = 5f;

    internal static WardGuiController? Instance { get; private set; }

    private readonly Dictionary<int, Coroutine> _doorCloseCoroutines = new();
    private readonly Dictionary<long, PermittedRowView> _permittedRows = new();
    private readonly Dictionary<WardRestrictionOptions, RestrictionRowView> _restrictionRows = new();
    private readonly List<long> _permittedRowsToRemove = new();

    private PrivateArea? _currentWard;
    private WardConfiguration _currentConfiguration;
    private WardConfiguration _authoritativeConfiguration;
    private WardConfiguration _pendingConfiguration;
    private GameObject? _root;
    private GameObject? _hintRoot;
    private GameObject? _panel;
    private GameObject? _generalPageRoot;
    private GameObject? _restrictionsPageRoot;
    private RectTransform? _permittedContent;
    private RectTransform? _restrictionsContent;
    private Text? _ownerValueText;
    private Text? _guildValueText;
    private Text? _shortcutHintText;
    private Text? _areaMarkerSpeedValueText;
    private Text? _areaMarkerAlphaValueText;
    private Text? _radiusValueText;
    private Text? _delayValueText;
    private Slider? _areaMarkerSpeedSlider;
    private Slider? _areaMarkerAlphaSlider;
    private Slider? _autoCloseDelaySlider;
    private Slider? _radiusSlider;
    private Toggle? _warningSoundToggle;
    private Toggle? _warningFlashToggle;
    private Button? _previousPageButton;
    private Button? _nextPageButton;
    private Image? _radiusLimitMarker;
    private Transform? _buildParent;
    private WardSettingsPage _currentPage = WardSettingsPage.General;
    private bool _visible;
    private bool _suppressUiEvents;
    private bool _configurationCommitPending;
    private bool _configurationPushPending;
    private bool _layoutRebuildPending;
    private float _nextConfigurationPushTime;
    private float _pendingConfigurationRequestedAt;
    private int _lastPermittedRevision = int.MinValue;
    private int _permittedRefreshGeneration;
    private long _pendingConfigurationRequestId;
    private PermittedRowView? _emptyPermittedRow;

    internal bool IsVisible => _visible;

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
        BuildGui();
    }

    private void OnDestroy()
    {
        GUIManager.OnCustomGUIAvailable -= BuildGui;
        GUIManager.BlockInput(false);
        _doorCloseCoroutines.Clear();

        if (Instance == this)
        {
            Instance = null;
        }
    }

    private void Update()
    {
        if (_layoutRebuildPending && !IsTextInputFocused())
        {
            _layoutRebuildPending = false;
            RebuildLayout();
        }

        if (!_visible)
        {
            SetShortcutHintVisible(false);
            TryOpenHoveredWardUi();
            return;
        }

        SetShortcutHintVisible(false);

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
            (_configurationCommitPending || _configurationPushPending) &&
            Time.unscaledTime >= _nextConfigurationPushTime)
        {
            CommitPendingConfiguration();
        }

        if (WardPermittedSnapshots.GetRevision(_currentWard) != _lastPermittedRevision)
        {
            RefreshPermittedPlayers(force: false);
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
        BuildGui();
        if (_root == null)
        {
            return;
        }

        _currentWard = ward;
        _configurationCommitPending = false;
        _configurationPushPending = false;
        _lastPermittedRevision = int.MinValue;
        _currentPage = WardSettingsPage.General;
        ClearPendingConfigurationRequest();
        _authoritativeConfiguration = WardSettings.GetConfiguration(ward);
        _currentConfiguration = _authoritativeConfiguration;
        RefreshStaticTexts();
        RefreshControls();
        RefreshPermittedPlayers(force: true);
        SetVisible(true);
    }

    internal void CloseWardUi()
    {
        FlushPendingConfigurationPush();
        _currentWard = null;
        _configurationCommitPending = false;
        _configurationPushPending = false;
        _lastPermittedRevision = int.MinValue;
        ClearPendingConfigurationRequest();
        SetVisible(false);
    }

    internal void ScheduleDoorAutoClose(Door door)
    {
        if (door == null || door.m_canNotBeClosed)
        {
            return;
        }

        if (!WardSettings.TryGetAutoCloseDoorDelay(door.transform.position, out var delay))
        {
            CancelDoorAutoClose(door);
            return;
        }

        var key = door.GetInstanceID();
        if (_doorCloseCoroutines.TryGetValue(key, out var existing))
        {
            StopCoroutine(existing);
        }

        _doorCloseCoroutines[key] = StartCoroutine(CloseDoorAfterDelay(door, delay));
    }

    internal void CancelDoorAutoClose(Door door)
    {
        if (door == null)
        {
            return;
        }

        var key = door.GetInstanceID();
        if (!_doorCloseCoroutines.TryGetValue(key, out var coroutine))
        {
            return;
        }

        StopCoroutine(coroutine);
        _doorCloseCoroutines.Remove(key);
    }

    private IEnumerator CloseDoorAfterDelay(Door door, float delay)
    {
        var key = door.GetInstanceID();
        yield return new WaitForSeconds(delay);

        _doorCloseCoroutines.Remove(key);

        if (door == null || door.m_canNotBeClosed)
        {
            yield break;
        }

        var nview = door.m_nview != null ? door.m_nview : door.GetComponent<ZNetView>();
        if (nview == null || !nview.IsValid())
        {
            yield break;
        }

        if (!WardSettings.TryGetAutoCloseDoorDelay(door.transform.position, out _))
        {
            yield break;
        }

        var state = nview.GetZDO()?.GetInt(ZDOVars.s_state, 0) ?? 0;
        if (state == 0)
        {
            yield break;
        }

        nview.InvokeRPC("UseDoor", new object[] { true });
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

        if (_hintRoot != null)
        {
            Destroy(_hintRoot);
        }

        ClearPermittedRows();
        _emptyPermittedRow = null;
        _lastPermittedRevision = int.MinValue;
        _restrictionRows.Clear();
        _restrictionsContent = null;
        _generalPageRoot = null;
        _restrictionsPageRoot = null;
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

        var hintObject = gui.CreateText(
            string.Empty,
            GUIManager.CustomGUIFront.transform,
            new Vector2(0.5f, 0.5f),
            new Vector2(0.5f, 0.5f),
            new Vector2(0f, -150f),
            gui.AveriaSerifBold,
            18,
            gui.ValheimBeige,
            true,
            Color.black,
            460f,
            84f,
            false);
        hintObject.name = "STUWardShortcutHint";
        _hintRoot = hintObject;
        _shortcutHintText = hintObject.GetComponent<Text>();
        if (_shortcutHintText != null)
        {
            _shortcutHintText.alignment = TextAnchor.MiddleCenter;
        }
        SetShortcutHintVisible(false);

        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiTitleToken, WardLocalization.UiTitleFallback),
            WardGuiLayoutSettings.GetTitlePosition(),
            34,
            560f,
            56f,
            TextAnchor.MiddleCenter,
            gui.AveriaSerifBold,
            gui.ValheimOrange);
        _ownerValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetOwnerPosition(), 22, 800f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _guildValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetGuildPosition(), 20, 800f, 32f, TextAnchor.MiddleLeft, gui.AveriaSerif, gui.ValheimBeige);

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

        BuildGeneralPage(gui);
        _buildParent = _restrictionsPageRoot.transform;
        BuildRestrictionsPage();
        _buildParent = null;
        SetActivePage(_currentPage);
        SetVisible(_visible);
        if (_visible && _currentWard != null)
        {
            RefreshStaticTexts();
            RefreshControls();
            RefreshPermittedPlayers(force: true);
        }
    }

    private void BuildGeneralPage(GUIManager gui)
    {
        if (_generalPageRoot == null)
        {
            return;
        }

        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var permittedListPosition = WardGuiLayoutSettings.GetPermittedListPosition();
        var registeredPlayersHeaderPosition = WardGuiLayoutSettings.GetRegisteredPlayersHeaderPosition();

        _buildParent = _generalPageRoot.transform;
        CreateLabel(WardLocalization.Localize(WardLocalization.UiRadiusToken, WardLocalization.UiRadiusFallback), WardGuiLayoutSettings.GetRadiusLabelPosition(), 21, 240f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _radiusSlider = CreateSlider(
            WardGuiLayoutSettings.GetRadiusSliderPosition(),
            520f,
            WardSettings.MinRadius,
            WardSettings.MaxRadius,
            true,
            commitOnRelease: true);
        _radiusSlider.onValueChanged.AddListener(OnRadiusSliderChanged);
        _radiusLimitMarker = CreateSliderLimitMarker(_radiusSlider, new Color(0.82f, 0.22f, 0.18f, 0.95f));
        _radiusValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetRadiusValuePosition(), 21, 120f, 36f, TextAnchor.MiddleCenter, gui.AveriaSerifBold, gui.ValheimYellow);

        CreateLabel(WardLocalization.Localize(WardLocalization.UiRangeSpeedToken, WardLocalization.UiRangeSpeedFallback), WardGuiLayoutSettings.GetAreaMarkerSpeedLabelPosition(), 21, 240f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _areaMarkerSpeedSlider = CreateSlider(
            WardGuiLayoutSettings.GetAreaMarkerSpeedSliderPosition(),
            520f,
            WardSettings.MinAreaMarkerSpeedMultiplier,
            WardSettings.MaxAreaMarkerSpeedMultiplier,
            false,
            commitOnRelease: true);
        _areaMarkerSpeedSlider.onValueChanged.AddListener(OnAreaMarkerSpeedSliderChanged);
        _areaMarkerSpeedValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetAreaMarkerSpeedValuePosition(), 21, 120f, 36f, TextAnchor.MiddleCenter, gui.AveriaSerifBold, gui.ValheimYellow);

        CreateLabel(WardLocalization.Localize(WardLocalization.UiRangeBrightnessToken, WardLocalization.UiRangeBrightnessFallback), WardGuiLayoutSettings.GetAreaMarkerAlphaLabelPosition(), 21, 240f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _areaMarkerAlphaSlider = CreateSlider(
            WardGuiLayoutSettings.GetAreaMarkerAlphaSliderPosition(),
            520f,
            WardSettings.MinAreaMarkerAlpha,
            WardSettings.MaxAreaMarkerAlpha,
            false,
            commitOnRelease: true);
        _areaMarkerAlphaSlider.onValueChanged.AddListener(OnAreaMarkerAlphaSliderChanged);
        _areaMarkerAlphaValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetAreaMarkerAlphaValuePosition(), 21, 120f, 36f, TextAnchor.MiddleCenter, gui.AveriaSerifBold, gui.ValheimYellow);

        CreateLabel(WardLocalization.Localize(WardLocalization.UiDoorCloseDelayToken, WardLocalization.UiDoorCloseDelayFallback), WardGuiLayoutSettings.GetAutoCloseDelayLabelPosition(), 21, 240f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        _autoCloseDelaySlider = CreateSlider(
            WardGuiLayoutSettings.GetAutoCloseDelaySliderPosition(),
            520f,
            WardSettings.MinAutoCloseDelay,
            WardSettings.MaxAutoCloseDelay,
            true,
            commitOnRelease: true);
        _autoCloseDelaySlider.onValueChanged.AddListener(OnAutoCloseDelaySliderChanged);
        _delayValueText = CreateLabel(string.Empty, WardGuiLayoutSettings.GetAutoCloseDelayValuePosition(), 21, 120f, 36f, TextAnchor.MiddleCenter, gui.AveriaSerifBold, gui.ValheimYellow);

        CreateLabel(WardLocalization.Localize(WardLocalization.UiWarningEffectsToken, WardLocalization.UiWarningEffectsFallback), WardGuiLayoutSettings.GetWarningEffectsLabelPosition(), 21, 240f, 36f, TextAnchor.MiddleLeft, gui.AveriaSerifBold, gui.ValheimBeige);
        var warningToggleSize = GetSliderHandleHeight(_radiusSlider);
        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiWarningSoundToken, WardLocalization.UiWarningSoundFallback),
            WardGuiLayoutSettings.GetWarningSoundLabelPosition(warningToggleSize),
            21,
            120f,
            36f,
            TextAnchor.MiddleLeft,
            gui.AveriaSerifBold,
            gui.ValheimBeige);
        _warningSoundToggle = CreateCenteredToggle(
            GetBuildParent(),
            WardGuiLayoutSettings.GetWarningSoundTogglePosition(warningToggleSize),
            warningToggleSize);
        _warningSoundToggle.onValueChanged.AddListener(OnWarningSoundToggleChanged);
        CreateLabel(
            WardLocalization.Localize(WardLocalization.UiWarningFlashToken, WardLocalization.UiWarningFlashFallback),
            WardGuiLayoutSettings.GetWarningFlashLabelPosition(warningToggleSize),
            21,
            120f,
            36f,
            TextAnchor.MiddleLeft,
            gui.AveriaSerifBold,
            gui.ValheimBeige);
        _warningFlashToggle = CreateCenteredToggle(
            GetBuildParent(),
            WardGuiLayoutSettings.GetWarningFlashTogglePosition(warningToggleSize),
            warningToggleSize);
        _warningFlashToggle.onValueChanged.AddListener(OnWarningFlashToggleChanged);

        CreateLabel(WardLocalization.Localize(WardLocalization.UiRegisteredPlayersToken, WardLocalization.UiRegisteredPlayersFallback), registeredPlayersHeaderPosition, 24, permittedListSize.x, 40f, TextAnchor.MiddleCenter, gui.AveriaSerifBold, gui.ValheimOrange);

        var scrollRoot = gui.CreateScrollView(
            _generalPageRoot.transform,
            false,
            true,
            20f,
            6f,
            gui.ValheimScrollbarHandleColorBlock,
            new Color(0f, 0f, 0f, 0.35f),
            permittedListSize.x,
            permittedListSize.y);

        ConfigureRect(scrollRoot.GetComponent<RectTransform>(), permittedListPosition, permittedListSize.x, permittedListSize.y);
        scrollRoot.name = "STUWardPermittedPlayers";

        _permittedContent = scrollRoot.transform.Find("Scroll View/Viewport/Content") as RectTransform;
        if (_permittedContent == null)
        {
            return;
        }

        var layout = _permittedContent.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            return;
        }

        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        layout.spacing = 6f;
        layout.padding = new RectOffset(8, 8, 8, 8);
    }

    internal void RebuildLayout()
    {
        BuildGui();
    }

    internal void ScheduleLayoutRebuild()
    {
        _layoutRebuildPending = true;
    }

    private void SetVisible(bool visible)
    {
        _visible = visible;
        if (_root != null)
        {
            _root.SetActive(visible);
        }

        if (visible)
        {
            SetShortcutHintVisible(false);
        }

        GUIManager.BlockInput(visible);
    }

    private void SetShortcutHintVisible(bool visible)
    {
        if (_hintRoot != null)
        {
            _hintRoot.SetActive(visible);
        }
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
        if (_areaMarkerSpeedSlider == null || _areaMarkerSpeedValueText == null || _areaMarkerAlphaSlider == null || _areaMarkerAlphaValueText == null || _autoCloseDelaySlider == null || _radiusSlider == null || _radiusValueText == null || _delayValueText == null || _warningSoundToggle == null || _warningFlashToggle == null)
        {
            return;
        }

        var maxRadius = _currentWard != null
            ? WardSettings.GetMaxNonOverlappingRadius(_currentWard)
            : WardSettings.MaxRadius;
        var displayedRadius = Mathf.Clamp(_currentConfiguration.Radius, WardSettings.MinRadius, WardSettings.MaxRadius);

        _suppressUiEvents = true;
        _areaMarkerSpeedSlider.value = _currentConfiguration.AreaMarkerSpeedMultiplier;
        _areaMarkerAlphaSlider.value = _currentConfiguration.AreaMarkerAlpha;
        _autoCloseDelaySlider.value = _currentConfiguration.AutoCloseDelay;
        _warningSoundToggle.isOn = _currentConfiguration.WarningSoundEnabled;
        _warningFlashToggle.isOn = _currentConfiguration.WarningFlashEnabled;
        _radiusSlider.maxValue = WardSettings.MaxRadius;
        _radiusSlider.value = displayedRadius;
        _areaMarkerSpeedValueText.text = $"{Mathf.RoundToInt(_currentConfiguration.AreaMarkerSpeedMultiplier * 100f)}%";
        _areaMarkerAlphaValueText.text = $"{Mathf.RoundToInt(_currentConfiguration.AreaMarkerAlpha * 100f)}%";
        _radiusValueText.text = WardLocalization.LocalizeFormat(
            WardLocalization.UiRadiusValueToken,
            WardLocalization.UiRadiusValueFallback,
            Mathf.RoundToInt(displayedRadius));
        _delayValueText.text = Mathf.Approximately(_currentConfiguration.AutoCloseDelay, 0f)
            ? WardLocalization.Localize(WardLocalization.UiOffToken, WardLocalization.UiOffFallback)
            : WardLocalization.LocalizeFormat(
                WardLocalization.UiDelayValueToken,
                WardLocalization.UiDelayValueFallback,
                Mathf.RoundToInt(_currentConfiguration.AutoCloseDelay));
        RefreshRestrictionRows();
        _suppressUiEvents = false;
        UpdateRadiusLimitMarker(maxRadius);
        UpdateRadiusValueVisuals(maxRadius);
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
            UpdatePermittedRowText(
                _emptyPermittedRow!,
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
            UpdatePermittedRowText(row, BuildPermittedPlayerDisplayText(_currentWard, entry.Key, entry.Value));
            row.Root.transform.SetSiblingIndex(index);
            row.Root.SetActive(true);
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
    }

    private PermittedRowView CreatePermittedRow(long playerId)
    {
        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var rowWidth = Mathf.Max(560f, permittedListSize.x - 72f);
        var rowHeight = 46f;
        var buttonWidth = 130f;

        var row = new GameObject("PermittedPlayerRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(_permittedContent, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);

        var image = row.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = rowHeight;
        layoutElement.preferredWidth = rowWidth;

        var nameObject = new GameObject("PlayerName", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        nameObject.transform.SetParent(row.transform, false);
        var nameRect = nameObject.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.5f, 0.5f);
        nameRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameRect.pivot = new Vector2(0f, 0.5f);

        var leftPadding = 10f;
        var removeButtonPosition = WardGuiLayoutSettings.GetRegisteredPlayersRemoveButtonPosition();
        var clampedButtonX = Mathf.Clamp(
            removeButtonPosition.x,
            -rowWidth * 0.5f + buttonWidth * 0.5f + 10f,
            rowWidth * 0.5f - buttonWidth * 0.5f - 4f);
        var nameRightEdge = clampedButtonX - buttonWidth * 0.5f - 12f;
        var nameWidth = Mathf.Max(340f, nameRightEdge - (-rowWidth * 0.5f + leftPadding));
        nameRect.anchoredPosition = new Vector2(-rowWidth * 0.5f + leftPadding, 0f);
        nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, nameWidth);
        nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight - 8f);

        var nameText = nameObject.GetComponent<Text>();
        var gui = GUIManager.Instance;
        gui.ApplyTextStyle(nameText, gui.AveriaSerifBold, gui.ValheimBeige, 18, false);
        nameText.text = string.Empty;
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        nameText.verticalOverflow = VerticalWrapMode.Truncate;

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
        });

        return new PermittedRowView(row, nameText);
    }

    private void EnsureEmptyPermittedRow()
    {
        if (_emptyPermittedRow != null)
        {
            return;
        }

        var row = new GameObject("PermittedPlayerRowEmpty", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(_permittedContent, false);

        var rowRect = row.GetComponent<RectTransform>();
        var permittedListSize = WardGuiLayoutSettings.GetPermittedListSize();
        var rowWidth = Mathf.Max(560f, permittedListSize.x - 72f);
        var rowHeight = 46f;
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);

        var image = row.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = rowHeight;
        layoutElement.preferredWidth = rowWidth;

        var nameObject = new GameObject("PlayerName", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        nameObject.transform.SetParent(row.transform, false);

        var nameRect = nameObject.GetComponent<RectTransform>();
        nameRect.anchorMin = new Vector2(0.5f, 0.5f);
        nameRect.anchorMax = new Vector2(0.5f, 0.5f);
        nameRect.pivot = new Vector2(0f, 0.5f);
        nameRect.anchoredPosition = new Vector2(-rowWidth * 0.5f + 10f, 0f);
        nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth - 24f);
        nameRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight - 8f);

        var nameText = nameObject.GetComponent<Text>();
        var gui = GUIManager.Instance;
        gui.ApplyTextStyle(nameText, gui.AveriaSerifBold, gui.ValheimBeige, 18, false);
        nameText.alignment = TextAnchor.MiddleLeft;
        nameText.horizontalOverflow = HorizontalWrapMode.Overflow;
        nameText.verticalOverflow = VerticalWrapMode.Truncate;

        _emptyPermittedRow = new PermittedRowView(row, nameText);
    }

    private void UpdatePermittedRowText(PermittedRowView row, string text)
    {
        if (!string.Equals(row.NameText.text, text, System.StringComparison.Ordinal))
        {
            row.NameText.text = text;
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

    private void BuildRestrictionsPage()
    {
        if (_restrictionsPageRoot == null)
        {
            return;
        }

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
            _restrictionsPageRoot.transform,
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

        var layout = _restrictionsContent.GetComponent<VerticalLayoutGroup>();
        if (layout != null)
        {
            layout.childAlignment = TextAnchor.UpperLeft;
            layout.childControlWidth = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            layout.spacing = 6f;
            layout.padding = new RectOffset(8, 8, 8, 8);
        }

        var definitions = WardSettings.RestrictionDefinitions;
        for (var index = 0; index < definitions.Count; index++)
        {
            var definition = definitions[index];
            _restrictionRows[definition.Restriction] = CreateRestrictionRow(definition);
        }
    }

    private RestrictionRowView CreateRestrictionRow(WardRestrictionDefinition definition)
    {
        var listSize = WardGuiLayoutSettings.GetRestrictionListSize();
        var rowWidth = Mathf.Max(560f, listSize.x - 72f);
        var rowHeight = 48f;

        var row = new GameObject("RestrictionRow", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        row.transform.SetParent(_restrictionsContent, false);

        var rowRect = row.GetComponent<RectTransform>();
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, rowWidth);
        rowRect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, rowHeight);

        var image = row.GetComponent<Image>();
        image.color = new Color(0f, 0f, 0f, 0.18f);

        var layoutElement = row.GetComponent<LayoutElement>();
        layoutElement.preferredHeight = rowHeight;
        layoutElement.preferredWidth = rowWidth;

        var toggle = CreateCenteredToggle(row.transform, new Vector2(-rowWidth * 0.5f + 28f, 0f), 30f);
        var restriction = definition.Restriction;
        toggle.onValueChanged.AddListener(enabled => OnRestrictionToggleChanged(restriction, enabled));

        var labelObject = new GameObject("RestrictionName", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
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

        var stateObject = new GameObject("RestrictionState", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
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

        _currentConfiguration = WardSettings.WithRestriction(_currentConfiguration, restriction, enabled);
        RefreshControls();
        ScheduleConfigurationPush();
    }

    private void OnAreaMarkerSpeedSliderChanged(float value)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithAreaMarkerSpeedMultiplier(_currentConfiguration, value);
        RefreshControls();
        ScheduleConfigurationCommit();
    }

    private void OnAreaMarkerAlphaSliderChanged(float value)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithAreaMarkerAlpha(_currentConfiguration, value);
        RefreshControls();
        ScheduleConfigurationCommit();
    }

    private void OnRadiusSliderChanged(float value)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithRadius(_currentConfiguration, value);
        ScheduleConfigurationCommit();
        UpdateRadiusTexts();
    }

    private void OnAutoCloseDelaySliderChanged(float value)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithAutoCloseDelay(_currentConfiguration, value);
        RefreshControls();
        ScheduleConfigurationCommit();
    }

    private void OnWarningSoundToggleChanged(bool enabled)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithWarningSoundEnabled(_currentConfiguration, enabled);
        RefreshControls();
        ScheduleConfigurationPush();
    }

    private void OnWarningFlashToggleChanged(bool enabled)
    {
        if (_suppressUiEvents)
        {
            return;
        }

        _currentConfiguration = WardSettings.WithWarningFlashEnabled(_currentConfiguration, enabled);
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
        _configurationCommitPending = false;
        _configurationPushPending = false;
        var submission = WardSettings.RequestUpdateConfiguration(_currentWard, submittedConfiguration);
        if (submission.IsPending)
        {
            BeginPendingConfigurationRequest(submission.RequestId, submittedConfiguration);
            return;
        }

        WardSettings.ShowConfigurationRequestFeedback(submission.ResultCode, submission.ShowOverlapMessage);
        ApplyConfigurationResponse(0L, submission.ResultCode, submission.Configuration);
    }

    private void ScheduleConfigurationCommit()
    {
        if (_suppressUiEvents || _currentWard == null)
        {
            return;
        }

        _configurationCommitPending = true;
        _configurationPushPending = false;
        _nextConfigurationPushTime = float.PositiveInfinity;
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
        if (!_configurationCommitPending && !_configurationPushPending)
        {
            return;
        }

        CommitPendingConfiguration();
    }

    private void CommitPendingConfiguration()
    {
        if (_suppressUiEvents ||
            _currentWard == null ||
            (!_configurationCommitPending && !_configurationPushPending) ||
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
            _configurationCommitPending = false;
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
        if (_configurationCommitPending || _configurationPushPending)
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

        if (_configurationCommitPending)
        {
            CommitPendingConfiguration();
            return;
        }

        if (_configurationPushPending && Time.unscaledTime >= _nextConfigurationPushTime)
        {
            CommitPendingConfiguration();
        }
    }

    private void UpdateRadiusTexts()
    {
        if (_radiusValueText == null)
        {
            return;
        }

        var maxRadius = _currentWard != null
            ? WardSettings.GetMaxNonOverlappingRadius(_currentWard)
            : WardSettings.MaxRadius;
        _radiusValueText.text = WardLocalization.LocalizeFormat(
            WardLocalization.UiRadiusValueToken,
            WardLocalization.UiRadiusValueFallback,
            Mathf.RoundToInt(_currentConfiguration.Radius));
        UpdateRadiusLimitMarker(maxRadius);
        UpdateRadiusValueVisuals(maxRadius);
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

    private Slider CreateSlider(Vector2 position, float width, float minValue, float maxValue, bool wholeNumbers, bool commitOnRelease = false)
    {
        var sliderObject = DefaultControls.CreateSlider(new DefaultControls.Resources());
        sliderObject.transform.SetParent(GetBuildParent(), false);
        sliderObject.name = "STUWardSlider";

        var sliderRect = sliderObject.GetComponent<RectTransform>();
        ConfigureRect(sliderRect, position, width, 34f);

        var slider = sliderObject.GetComponent<Slider>();
        slider.direction = Slider.Direction.LeftToRight;
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.wholeNumbers = wholeNumbers;

        GUIManager.Instance.ApplySliderStyle(slider);
        ShrinkSliderHandle(sliderObject.transform);

        if (commitOnRelease)
        {
            var commitHandler = sliderObject.AddComponent<SliderCommitHandler>();
            commitHandler.OnCommit = CommitPendingConfiguration;
        }

        return slider;
    }

    private Toggle CreateToggle(Vector2 position, float boxSize)
    {
        return CreateAnchoredToggle(GetBuildParent(), position, boxSize);
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

    private static Image? CreateSliderLimitMarker(Slider slider, Color color)
    {
        var sliderRect = slider.transform as RectTransform;
        if (sliderRect == null)
        {
            return null;
        }

        var markerObject = new GameObject("STUWardLimitMarker", typeof(RectTransform), typeof(Image));
        markerObject.transform.SetParent(sliderRect, false);
        markerObject.transform.SetAsLastSibling();

        var markerRect = markerObject.GetComponent<RectTransform>();
        markerRect.anchorMin = new Vector2(1f, 0.5f);
        markerRect.anchorMax = new Vector2(1f, 0.5f);
        markerRect.pivot = new Vector2(0.5f, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;
        markerRect.sizeDelta = new Vector2(4f, GetSliderTrackHeight(slider));

        var handleSlideArea = sliderRect.Find("Handle Slide Area");
        if (handleSlideArea != null)
        {
            markerObject.transform.SetSiblingIndex(handleSlideArea.GetSiblingIndex());
        }
        else
        {
            markerObject.transform.SetAsLastSibling();
        }

        var markerImage = markerObject.GetComponent<Image>();
        markerImage.color = color;
        markerImage.raycastTarget = false;
        return markerImage;
    }

    private void UpdateRadiusLimitMarker(float maxRadius)
    {
        if (_radiusSlider == null || _radiusLimitMarker == null)
        {
            return;
        }

        var clampedRadius = Mathf.Clamp(maxRadius, _radiusSlider.minValue, _radiusSlider.maxValue);
        var shouldShowMarker = clampedRadius < _radiusSlider.maxValue - 0.01f;
        _radiusLimitMarker.gameObject.SetActive(shouldShowMarker);
        if (!shouldShowMarker)
        {
            return;
        }

        var normalized = Mathf.InverseLerp(_radiusSlider.minValue, _radiusSlider.maxValue, clampedRadius);
        var markerRect = _radiusLimitMarker.rectTransform;
        markerRect.anchorMin = new Vector2(normalized, 0.5f);
        markerRect.anchorMax = new Vector2(normalized, 0.5f);
        markerRect.anchoredPosition = Vector2.zero;
        markerRect.sizeDelta = new Vector2(markerRect.sizeDelta.x, GetSliderTrackHeight(_radiusSlider));
    }

    private void UpdateRadiusValueVisuals(float maxRadius)
    {
        if (_radiusValueText == null)
        {
            return;
        }

        _radiusValueText.color = _currentConfiguration.Radius > maxRadius + 0.01f
            ? new Color(0.85f, 0.2f, 0.2f)
            : GUIManager.Instance.ValheimYellow;
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

    private void SetActivePage(WardSettingsPage page)
    {
        _currentPage = page;
        if (_generalPageRoot != null)
        {
            _generalPageRoot.SetActive(page == WardSettingsPage.General);
        }

        if (_restrictionsPageRoot != null)
        {
            _restrictionsPageRoot.SetActive(page == WardSettingsPage.Restrictions);
        }

        UpdatePageButtonVisuals();
    }

    private void UpdatePageButtonVisuals()
    {
        if (_previousPageButton != null)
        {
            _previousPageButton.gameObject.SetActive(_currentPage == WardSettingsPage.Restrictions);
        }

        if (_nextPageButton != null)
        {
            _nextPageButton.gameObject.SetActive(_currentPage == WardSettingsPage.General);
        }
    }

    private static void StylePageArrowButton(Button? button)
    {
        var text = button != null ? button.GetComponentInChildren<Text>() : null;
        if (text != null)
        {
            text.text = text.text.Trim();
            text.fontSize = 34;
            text.color = GUIManager.Instance.ValheimYellow;
            text.alignment = TextAnchor.MiddleCenter;
            text.rectTransform.anchoredPosition += new Vector2(0f, 1f);
        }
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

    private static void ShrinkSliderHandle(Transform sliderTransform)
    {
        var handle = sliderTransform.Find("Handle Slide Area/Handle") as RectTransform;
        if (handle == null)
        {
            return;
        }

        handle.localScale = new Vector3(0.5f, 0.8f, 1f);
    }

    private static float GetSliderTrackHeight(Slider slider)
    {
        var background = slider.transform.Find("Background") as RectTransform;
        if (background == null)
        {
            return 14f;
        }

        if (background.rect.height > 0.01f)
        {
            return background.rect.height;
        }

        return background.sizeDelta.y > 0.01f ? background.sizeDelta.y : 14f;
    }

    private static float GetSliderHandleHeight(Slider? slider)
    {
        if (slider == null)
        {
            return 18f;
        }

        var handle = slider.transform.Find("Handle Slide Area/Handle") as RectTransform;
        if (handle == null)
        {
            return 18f;
        }

        var baseHeight = handle.rect.height > 0.01f ? handle.rect.height : handle.sizeDelta.y;
        if (baseHeight <= 0.01f)
        {
            baseHeight = 18f;
        }

        var scaledHeight = baseHeight * Mathf.Abs(handle.localScale.y);
        return Mathf.Max(12f, scaledHeight);
    }

    private static string BuildPermittedPlayerDisplayText(PrivateArea? area, long playerId, string playerName)
    {
        var guildName = GetPermittedPlayerGuildName(area, playerId);
        var platformId = GetPermittedPlayerPlatformId(area, playerId);
        var guildDisplay = string.IsNullOrWhiteSpace(guildName) ? "-" : guildName;
        var platformDisplay = string.IsNullOrWhiteSpace(platformId) ? "-" : platformId;
        return WardLocalization.LocalizeFormat(
            WardLocalization.UiRegisteredPlayerFormatToken,
            WardLocalization.UiRegisteredPlayerFormatFallback,
            playerName,
            guildDisplay,
            platformDisplay);
    }

    private static string GetPermittedPlayerGuildName(PrivateArea? area, long playerId)
    {
        if (WardPermittedSnapshots.TryGet(area, playerId, out var guildName, out _))
        {
            return guildName;
        }

        return GuildsCompat.GetPlayerGuildName(playerId);
    }

    private static string GetPermittedPlayerPlatformId(PrivateArea? area, long playerId)
    {
        if (WardPermittedSnapshots.TryGet(area, playerId, out _, out var platformId))
        {
            return platformId;
        }

        return WardOwnership.GetPlayerSteamIdDisplay(playerId);
    }

    private sealed class PermittedRowView
    {
        internal PermittedRowView(GameObject root, Text nameText)
        {
            Root = root;
            NameText = nameText;
        }

        internal GameObject Root { get; }

        internal Text NameText { get; }

        internal int LastSeenGeneration { get; set; }
    }

    private enum WardSettingsPage
    {
        General,
        Restrictions
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

    private static bool IsTextInputFocused()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
        {
            return false;
        }

        if (selected.GetComponent<InputField>() != null)
        {
            return true;
        }

        var components = selected.GetComponents<Component>();
        for (var index = 0; index < components.Length; index++)
        {
            var component = components[index];
            if (component != null && component.GetType().Name.Contains("InputField", System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private sealed class SliderCommitHandler : MonoBehaviour, IEndDragHandler, IPointerUpHandler
    {
        internal System.Action? OnCommit { get; set; }

        public void OnEndDrag(PointerEventData eventData)
        {
            OnCommit?.Invoke();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            OnCommit?.Invoke();
        }
    }
}
