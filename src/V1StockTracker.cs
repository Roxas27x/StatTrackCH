#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace CloneHeroSectionTracker.V1Stock
{
internal static class StatTrackDataPaths
{
    internal const string CurrentDirectoryName = "StatTrack";

    internal static string GetCurrentDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrentDirectoryName);
    }
}

internal static class StockTrackerLog
{
    private static readonly object Sync = new();
    private static readonly string LogDir = StatTrackDataPaths.GetCurrentDataDirectory();
    private static readonly string LogPath = Path.Combine(LogDir, "v1-stock.log");
    private static readonly bool DebugLoggingEnabled = false;

    public static void Write(string message)
    {
        lock (Sync)
        {
            Directory.CreateDirectory(LogDir);
            File.AppendAllText(LogPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
    }

    public static void WriteDebug(string message)
    {
        if (!DebugLoggingEnabled)
        {
            return;
        }

        Write(message);
    }

    public static void Write(Exception ex) => Write(ex.ToString());
}

public static class StockTrackerHooks
{
    private static readonly object Sync = new();
    private static readonly V1StockTracker Tracker = new();
    private const float TickIntervalSeconds = 0.5f;
    private static float _lastTickAt;
    private static StockOverlayHost? _overlayHost;
    private static bool _overlayHostLogged;
    private static bool _gameManagerHookLogged;
    private static bool _mainMenuHookLogged;
    private static bool _overlayEnsureEnterLogged;
    private static bool _overlayEnsureExitLogged;

    public static void OnGameManagerUpdate(object gameManager)
    {
        try
        {
            if (gameManager == null)
            {
                return;
            }

            if (!_gameManagerHookLogged)
            {
                _gameManagerHookLogged = true;
                StockTrackerLog.WriteDebug("GameManagerUpdateHookEntered | type=" + gameManager.GetType().FullName);
            }

            EnsureOverlayHost();
            if (Time.unscaledTime - _lastTickAt < TickIntervalSeconds)
            {
                return;
            }

            _lastTickAt = Time.unscaledTime;
            lock (Sync)
            {
                Tracker.Tick(gameManager);
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    public static void OnMainMenuUpdate(object mainMenu)
    {
        try
        {
            if (mainMenu == null)
            {
                return;
            }

            if (!_mainMenuHookLogged)
            {
                _mainMenuHookLogged = true;
                StockTrackerLog.WriteDebug("MainMenuUpdateHookEntered | type=" + mainMenu.GetType().FullName);
            }

            lock (Sync)
            {
                Tracker.EnsureMenuReady(mainMenu);
            }

            EnsureOverlayHost();
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    public static void OnOverlayUpdate()
    {
        try
        {
            lock (Sync)
            {
                Tracker.HandleOverlayUpdate();
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    public static bool ShouldBlockMainMenuInput(object mainMenu)
    {
        try
        {
            if (mainMenu == null)
            {
                return false;
            }

            lock (Sync)
            {
                return Tracker.ShouldBlockMainMenuInput(mainMenu);
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
            return false;
        }
    }

    public static void OnOverlayGui()
    {
        try
        {
            Tracker.RenderOverlayGui();
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    public static void OnBasePlayerReset(object player)
    {
        try
        {
            if (player == null)
            {
                return;
            }

            lock (Sync)
            {
                Tracker.ResetExactMissCounter(player);
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    public static void OnBasePlayerNoteMiss(object player, object note)
    {
        try
        {
            if (player == null || note == null)
            {
                return;
            }

            lock (Sync)
            {
                Tracker.RecordExactMiss(player, note);
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private static void EnsureOverlayHost()
    {
        if (!_overlayEnsureEnterLogged)
        {
            _overlayEnsureEnterLogged = true;
            StockTrackerLog.WriteDebug("EnsureOverlayHostEnter");
        }

        if (_overlayHost != null)
        {
            if (!_overlayEnsureExitLogged)
            {
                _overlayEnsureExitLogged = true;
                StockTrackerLog.WriteDebug("EnsureOverlayHostExit | cached=1");
            }
            return;
        }

        var gameObject = GameObject.Find("CloneHeroSectionTrackerOverlay");
        if (gameObject == null)
        {
            gameObject = new GameObject("CloneHeroSectionTrackerOverlay");
            UnityEngine.Object.DontDestroyOnLoad(gameObject);
        }

        _overlayHost = gameObject.GetComponent<StockOverlayHost>();
        if (_overlayHost == null)
        {
            _overlayHost = gameObject.AddComponent<StockOverlayHost>();
        }

        if (!_overlayHostLogged)
        {
            _overlayHostLogged = true;
            StockTrackerLog.WriteDebug("OverlayHostReady | object=" + gameObject.name);
        }

        if (!_overlayEnsureExitLogged)
        {
            _overlayEnsureExitLogged = true;
            StockTrackerLog.WriteDebug("EnsureOverlayHostExit | cached=0 | component=" + (_overlayHost != null ? "1" : "0"));
        }
    }
}

internal sealed class StockOverlayHost : MonoBehaviour
{
    private bool _updateLogged;
    private bool _guiLogged;

    private void Awake()
    {
        DontDestroyOnLoad(gameObject);
        StockTrackerLog.WriteDebug("OverlayHostAwake");
    }

    private void Update()
    {
        if (!_updateLogged)
        {
            _updateLogged = true;
            StockTrackerLog.WriteDebug("OverlayHostUpdate");
        }

        StockTrackerHooks.OnOverlayUpdate();
    }

    private void OnGUI()
    {
        if (!_guiLogged && Event.current != null && (Event.current.type == EventType.Layout || Event.current.type == EventType.Repaint))
        {
            _guiLogged = true;
            StockTrackerLog.WriteDebug("OverlayHostOnGUI");
        }

        StockTrackerHooks.OnOverlayGui();
    }
}

internal sealed class V1StockTracker
{
    private const BindingFlags AnyInstance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private const BindingFlags AnyStatic = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly int AnimatedMenuWispGlobalColorRId = Shader.PropertyToID(AnimatedMenuWispGlobalColorRName);
    private static readonly int AnimatedMenuWispGlobalColorGId = Shader.PropertyToID(AnimatedMenuWispGlobalColorGName);
    private static readonly int AnimatedMenuWispGlobalColorBId = Shader.PropertyToID(AnimatedMenuWispGlobalColorBName);
    private static readonly int AnimatedMenuWispGlobalColorAId = Shader.PropertyToID(AnimatedMenuWispGlobalColorAName);
    private static readonly int AnimatedMenuWispGlobalSizeId = Shader.PropertyToID(AnimatedMenuWispGlobalSizeName);
    private static readonly int AnimatedMenuWispGlobalEnabledId = Shader.PropertyToID(AnimatedMenuWispGlobalEnabledName);
    private static readonly bool VerboseLoggingEnabled = false;
    private const float StateExportIntervalSeconds = 0.25f;
    private const float ObsExportIntervalSeconds = 0.25f;
    private const float StableRunRefreshIntervalSeconds = 5f;
    private const float NotesHitRefreshIntervalSeconds = 2f;
    private const float ResultStatsRefreshIntervalSeconds = 1f;
    private const float TimingDiagnosticsIntervalSeconds = 0.5f;
    private const string NoteSplitModeExportKey = "note_split_mode";
    private const string PublicVersionNumber = "1.0.4";
    private const string PublicVersionLabel = "StatTrack v1.0.4";
    private const string GitHubLatestReleaseApiUrl = "https://api.github.com/repos/Roxas27x/StatTrackCH/releases/latest";
    private const string GitHubApiAcceptHeader = "application/vnd.github+json";
    private const int GitHubReleaseCheckTimeoutMs = 5000;
    private const string MenuBackgroundSettingName = "menu_background";
    private const int AnimatedMenuBackgroundSettingValue = 0;
    private const string AnimatedMenuColorPickerTargetKey = "menu:animated";
    private const string AnimatedMenuWispColorPickerTargetKey = "menu:wisps";
    private const string AnimatedMenuTintOverlayObjectName = "StatTrackAnimatedMenuTint";
    private const string AnimatedMenuCanvasTintOverlayObjectName = "StatTrackAnimatedMenuCanvasTint";
    private const string AnimatedMenuWispGlobalColorRName = "_StatTrackMenuWispColorR";
    private const string AnimatedMenuWispGlobalColorGName = "_StatTrackMenuWispColorG";
    private const string AnimatedMenuWispGlobalColorBName = "_StatTrackMenuWispColorB";
    private const string AnimatedMenuWispGlobalColorAName = "_StatTrackMenuWispColorA";
    private const string AnimatedMenuWispGlobalSizeName = "_StatTrackMenuWispSize";
    private const string AnimatedMenuWispGlobalEnabledName = "_StatTrackMenuWispEnabled";
    private const string AnimatedMenuWispOverlayObjectName = "StatTrackAnimatedMenuWisps";

    private const string GlobalVariablesTypeName = "GlobalVariables";
    private const string ActiveChartFieldName = "\u02B4\u02BC\u02BF\u02BC\u02BA\u02B9\u02B8\u02B2\u02BD\u02BE\u02BD";
    private const string CurrentSectionTickFieldName = "\u02BA\u02BC\u02B2\u02BE\u02B7\u02C0\u02BD\u02B7\u02B2\u02B9\u02BF";
    private const string SongPlaybackTimeFieldName = "\u02b7\u02b4\u02ba\u02b5\u02b5\u02b8\u02be\u02b5\u02b3\u02b5\u02bb";
    private const string BasePlayerTypeName = "BasePlayer";
    private const string SngFileIdentifier = "SNGPKG";
    private const string PracticeUiFieldName = "ʳʺˀˁʴˀʳˁʴʴʽ";
    private const string PlayersFieldName = "ʾʸˁʳʽʹʷʴʽʵʽ";
    private const string SongDurationFieldName = "ʷʾˀʲʸˀʺˁʻʲʼ";
    private const string SongTimeFieldName = "ʲʻʺʹʺʲʷʾʻʺʹ";
    private const string CurrentChartFieldName = "<ʁʿʴʾʾʼʵʾʾʴʷ>k__BackingField";
    private const string BasePlayerControllerFieldName = "ʾʵʻʳʷʴʼʲʻʶʷ";
    private const string ControllerSettingsFieldName = "ʲʼʽʺʿʹʽʻʵʺʶ";
    private const string BasePlayerScoreFieldName = "ʼʼˁʴʿʸʿʲʺʳʵ";
    private const string BasePlayerComboFieldName = "ʷˀʾʻʲʲʳʼʽʴʾ";
    private const string GlobalVariablesSingletonFieldName = "ʷʲʺʷʻʳʾʶˁˀʷ";
    private const string GlobalVariablesCurrentSongFieldName = "ʺʹʽʺʴʴʿʽʸʳˁ";

    private const string NoteWasHitFieldName = "Ê¿Ê¿Ê·Ê¾Ê¹Ê¼Ê¸Ê·Ë€Ê¼Ê´";
    private const string NoteDisjointPropertyName = "Ê¿Ê´Ê½Ë€ÊµÊ³Ê¸Ê¸Ê¸Ê»Ë€";
    private const string NoteSlavePropertyName = "ÊµË€Ê³Ë€Ê¶ÊµÊ¸Ê½Ê¶Ê´Ê´";
    private const string NoteStartTimePropertyName = "\u02b2\u02b8\u02ba\u02ba\u02bc\u02b7\u02b5\u02b5\u02b6\u02b5\u02c1";
    private const string NoteTickPropertyName = "\u02b5\u02bf\u02b5\u02b2\u02bd\u02bd\u02bd\u02b5\u02b8\u02c0\u02be";

    private string _dataDir = string.Empty;
    private string _statePath = string.Empty;
    private string _memoryPath = string.Empty;
    private string _configPath = string.Empty;
    private string _desktopStylePath = string.Empty;
    private string _obsDir = string.Empty;
    private string _obsStatePath = string.Empty;
    private float _lastConfigReloadAt;
    private float _lastStateExportAt;
    private float _lastObsExportAt;
    private TrackerMemory _memory = new();
    private TrackerConfig _config = new();
    private RunState _runState = new();
    private readonly Dictionary<string, List<SectionDescriptor>> _songSectionsCache = new();
    private readonly Dictionary<string, List<string>> _songSectionNamesCache = new();
    private readonly Dictionary<string, string> _fileWriteCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _fileWriteSync = new();
    private readonly object _exportWorkerSync = new();
    private readonly AutoResetEvent _exportSignal = new(false);
    private readonly object _updateCheckSync = new();
    private const string DesktopOverlayExeName = "StatTrackOverlay.exe";
    private const float DesktopOverlayCheckIntervalSeconds = 5f;
    private const float DesktopOverlayStateExportGraceSeconds = 1f;
    private bool _memoryDirty;
    private bool _configDirty;
    private int _memoryVersion;
    private int _configVersion;
    private int _exportTemplateVersion;
    private int _sectionMemoryVersion;
    private int _sectionConfigVersion;
    private int _overlayConfigVersion;
    private int _latestStateVersion;
    private int _enabledTextExportsVersion;
    private bool _initialized;
    private SectionSnapshotCache? _sectionSnapshotCache;
    private NoteSplitSnapshotCache? _noteSplitSnapshotCache;
    private string _completedRunsSnapshotSongKey = string.Empty;
    private int _completedRunsSnapshotMemoryVersion = -1;
    private List<CompletedRunRecord> _completedRunsSnapshot = new();
    private Thread? _exportThread;
    private ExportWorkItem? _pendingExport;
    private bool _obsCleanupPending = true;
    private float _lastDesktopOverlayNeededAt = -999f;
    private bool _forceStateExportPending;
    private bool _forceObsExportPending;
    private bool _updateCheckStarted;
    private bool _updateAvailable;
    private string? _latestReleaseVersionLabel;
    private float _lastMenuPersistenceAt = -999f;

    private Type? _gameManagerType;
    private Type? _globalVariablesType;
    private Type? _basePlayerType;
    private Type? _gameSettingType;
    private Type? _blackMenuType;
    private FieldInfo? _mainMenuVersionLabelField;
    private FieldInfo? _playersField;
    private FieldInfo? _mainPlayerField;
    private FieldInfo? _practiceUiField;
    private FieldInfo? _songDurationField;
    private FieldInfo? _songTimeField;
    private FieldInfo? _songPlaybackTimeField;
    private FieldInfo? _currentSectionTickField;
    private FieldInfo? _chartField;
    private FieldInfo? _controllerField;
    private FieldInfo? _scoreField;
    private FieldInfo? _comboField;
    private FieldInfo? _ghostNotesField;
    private FieldInfo? _fretsBetweenNotesField;
    private FieldInfo? _overstrumsField;
    private PropertyInfo? _ghostInputsProperty;
    private FieldInfo? _notesHitField;
    private FieldInfo? _globalVariablesSingletonField;
    private FieldInfo? _globalVariablesCurrentSongField;
    private FieldInfo? _globalVariablesSongSpeedField;
    private FieldInfo? _playerSettingsField;
    private FieldInfo? _gameSettingNameField;
    private PropertyInfo? _gameSettingCurrentValueProperty;
    private PropertyInfo? _gameSettingPercentStringProperty;
    private FieldInfo? _songSpeedSettingField;
    private FieldInfo? _menuBackgroundSettingField;
    private FieldInfo? _blackMenuRawImageField;
    private MethodInfo? _chartSectionsMethod;
    private MethodInfo? _chartSectionTimeMethod;
    private FieldInfo? _chartNamedSectionsField;
    private MethodInfo? _runtimeChartSectionsMethod;
    private MethodInfo? _runtimeChartSectionTimeMethod;
    private MethodInfo? _songEntryLoadChartMethod;
    private MethodInfo? _songEntryLoadChartWithFlagMethod;
    private MethodInfo? _loadedChartTickToTimeMethod;
    private MethodInfo? _loadedChartMaxTickMethod;
    private PropertyInfo? _resultStatsArrayProperty;
    private Type? _playerStatsType;
    private PropertyInfo? _textComponentTextProperty;
    private FieldInfo? _playerStatsScoreField;
    private FieldInfo? _playerStatsNotesHitField;
    private FieldInfo? _playerStatsTotalNotesField;
    private FieldInfo? _playerStatsOverstrumsField;
    private FieldInfo? _playerStatsGhostNotesField;
    private FieldInfo? _playerStatsIsRemoteField;
    private PropertyInfo? _playerStatsAccuracyProperty;
    private PropertyInfo? _playerStatsAccuracyStringProperty;
    private float _lastResultStatsReflectionScanAt;
    private bool _resultStatsReflectionScanCompleted;
    private float _lastChartFieldDiagnosticsAt;
    private float _lastDiagnosticsAt;
    private float _lastTimingDiagnosticsAt = -999f;
    private string _lastTimingDiagnosticsSongKey = string.Empty;
    private string _lastTimingSectionDumpKey = string.Empty;
    private bool _ghostNotesFieldCalibrated;
    private string? _playerTypeCachedForStats;
    private readonly List<PlayerMissCounter> _exactMissCounters = new();
    private TrackerState _latestState = new();
    private object? _activeGameManager;
    private bool _overlayEditorVisible;
    private Vector2 _overlayEditorScroll;
    private string? _overlayDraggingKey;
    private Vector2 _overlayDragOffset;
    private string? _overlayResizingKey;
    private Vector2 _overlayResizeStartMouse;
    private Rect _overlayResizeStartRect;
    private string? _overlayColorTargetKey;
    private Rect _overlayColorPickerRect;
    private float _overlayColorPickerHue;
    private float _overlayColorPickerSaturation;
    private float _overlayColorPickerValue = 1f;
    private bool _overlayColorPickerDragging;
    private string? _lastWidgetColorClickKey;
    private float _lastWidgetColorClickAt = -999f;
    private string? _overlayEditorActiveSliderKey;
    private Texture2D? _overlayColorWheelTexture;
    private float _overlayColorWheelTextureValue = -1f;
    private Texture2D? _overlayEditorPanelTexture;
    private float _overlayEditorPanelTextureAlpha = -1f;
    private int _overlayEditorPanelTextureWidth;
    private int _overlayEditorPanelTextureHeight;
    private Texture2D? _overlayExportTemplatePanelTexture;
    private int _overlayExportTemplatePanelTextureWidth;
    private int _overlayExportTemplatePanelTextureHeight;
    private Texture2D? _overlaySliderTrackTexture;
    private int _overlaySliderTrackTextureWidth;
    private int _overlaySliderTrackTextureHeight;
    private Texture2D? _overlaySliderFillTexture;
    private int _overlaySliderFillTextureWidth;
    private int _overlaySliderFillTextureHeight;
    private Texture2D? _overlaySliderKnobTexture;
    private Texture2D? _overlaySliderCardTexture;
    private int _overlaySliderCardTextureWidth;
    private int _overlaySliderCardTextureHeight;
    private Texture2D? _overlaySliderWellTexture;
    private int _overlaySliderWellTextureWidth;
    private int _overlaySliderWellTextureHeight;
    private Texture2D? _overlaySliderTitleTexture;
    private int _overlaySliderTitleTextureWidth;
    private int _overlaySliderTitleTextureHeight;
    private Texture2D? _overlaySliderValueTexture;
    private int _overlaySliderValueTextureWidth;
    private int _overlaySliderValueTextureHeight;
    private Texture2D? _overlaySliderDividerTexture;
    private Texture2D? _animatedMenuOnGuiOverlayTexture;
    private Color _animatedMenuOnGuiOverlayTextureColor = new(-1f, -1f, -1f, -1f);
    private readonly Dictionary<string, Texture2D> _resizeCornerTextureCache = new();
    private readonly EnabledTextExportSnapshot _disabledTextExportSnapshot = new();
    private EnabledTextExportSnapshot _enabledTextExportSnapshot = new();
    private int _enabledTextExportSnapshotVersion = -1;
    private OverlayRenderSnapshot? _overlayRenderSnapshot;
    private int _overlayRenderSnapshotStateVersion = -1;
    private int _overlayRenderSnapshotConfigVersion = -1;
    private int _overlayRenderSnapshotScreenWidth = -1;
    private int _overlayRenderSnapshotScreenHeight = -1;
    private bool _overlayRenderSnapshotEditorVisible;
    private string? _songResetConfirmKey;
    private float _songResetConfirmExpiresAt;
    private string? _overlayResetConfirmKey;
    private float _overlayResetConfirmExpiresAt;
    private float _wipeAllDataConfirmExpiresAt;
    private bool _exportTemplateEditorVisible;
    private Vector2 _exportTemplateEditorListScroll;
    private Vector2 _exportTemplateEditorTokenScroll;
    private Vector2 _exportTemplateEditorPreviewScroll;
    private Vector2 _exportTemplateEditorTextScroll;
    private string? _selectedExportTemplateId;
    private string? _exportTemplateEditorActiveTemplateId;
    private int _exportTemplateEditorActiveLineIndex;
    private int _exportTemplateEditorCursorIndex;
    private readonly Dictionary<string, string> _exportTemplateEditorDrafts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CompiledExportTemplate> _compiledExportTemplates = new(StringComparer.Ordinal);
    private int _compiledExportTemplatesVersion = -1;
    private readonly Dictionary<string, CompiledExportTemplate> _workerCompiledExportTemplates = new(StringComparer.Ordinal);
    private int _workerCompiledExportTemplatesVersion = -1;
    private readonly object _exportTemplateLogSync = new();
    private readonly Dictionary<string, string> _loggedInvalidTemplateSources = new(StringComparer.Ordinal);
    private Process? _desktopOverlayProcess;
    private float _lastDesktopOverlayCheckAt = -999f;
    private bool _desktopOverlayLaunchFailed;
    private bool _practiceAttemptRollbackApplied;
    private readonly object _persistenceWriteSync = new();
    private readonly Dictionary<string, PersistenceWriteItem> _pendingPersistenceWrites = new(StringComparer.OrdinalIgnoreCase);
    private readonly AutoResetEvent _persistenceWriteSignal = new(false);
    private Thread? _persistenceWriteThread;
    private bool _persistenceWriteThreadStarted;
    private TrackerMemory? _memoryWriteSnapshot;
    private bool _memoryWriteSnapshotRequiresFullRefresh = true;
    private readonly HashSet<string> _dirtyMemorySongKeys = new(StringComparer.Ordinal);
    private TrackerConfig? _configWriteSnapshot;
    private bool _configWriteSnapshotRequiresFullRefresh = true;
    private readonly HashSet<string> _dirtyConfigSongKeys = new(StringComparer.Ordinal);
    private DesktopOverlayStyleConfig _mergedDesktopOverlayStyle = new();
    private string? _lastMenuOverlayStateFailureMessage;
    private const float OverlayWidgetDefaultWidth = 300f;
    private const float OverlayWidgetDefaultHeight = 90f;
    private const int OverlayWidgetResizeModeVersion = 2;
    private static readonly PropertyInfo? GuiPixelsPerPointProperty = typeof(GUIUtility).GetProperty("pixelsPerPoint", BindingFlags.Public | BindingFlags.Static);
    private GUIStyle? _widgetTitleStyle;
    private GUIStyle? _widgetContentStyle;
    private GUIStyle? _widgetSectionAttemptsStyle;
    private GUIStyle? _widgetSectionEmphasisStyle;
    private RawImage? _animatedMenuRawImage;
    private RawImage? _animatedMenuTintOverlayImage;
    private RawImage? _animatedMenuCanvasTintOverlayImage;
    private RawImage? _animatedMenuWispOverlayImage;
    private Texture2D? _animatedMenuWispTexture;
    private bool _animatedMenuTintApplied;

    public void Tick(object gameManager)
    {
        EnsureInitialized(gameManager.GetType().Assembly);
        _gameManagerType ??= gameManager.GetType();
        _activeGameManager = gameManager;
        CacheReflection();

        TrackerState state = BuildState(gameManager);
        _latestState = state;
        _latestStateVersion++;

        if (!state.IsInSong && Time.unscaledTime - _lastConfigReloadAt >= 1.5f)
        {
            _lastConfigReloadAt = Time.unscaledTime;
            if (_configDirty)
            {
                SaveConfig();
            }
            else
            {
                _config = LoadJson(_configPath, _config);
                RefreshMergedDesktopOverlayStyle();
                NormalizeLoadedExportTemplateOverrides();
                _configWriteSnapshotRequiresFullRefresh = true;
                _dirtyConfigSongKeys.Clear();
                _overlayConfigVersion++;
                _enabledTextExportsVersion++;
                _enabledTextExportSnapshotVersion = -1;
                _exportTemplateVersion++;
            }
        }

        if (ShouldUseDesktopOverlay(state) && (!state.IsInSong || _desktopOverlayProcess == null))
        {
            EnsureDesktopOverlayStarted();
        }

        ExportState(state);
    }

    public void EnsureMenuReady(object mainMenu)
    {
        EnsureInitialized(mainMenu.GetType().Assembly);
        _gameManagerType ??= mainMenu.GetType().Assembly.GetType("GameManager");
        CacheReflection();
        FlushPendingMenuPersistence();
        EnsureReleaseCheckStarted();
        ApplyMainMenuVersionText(mainMenu);
        ApplyAnimatedMenuTint();
    }

    public bool ShouldBlockMainMenuInput(object mainMenu)
    {
        return _overlayEditorVisible && _exportTemplateEditorVisible;
    }

    private void EnsureReleaseCheckStarted()
    {
        lock (_updateCheckSync)
        {
            if (_updateCheckStarted)
            {
                return;
            }

            _updateCheckStarted = true;
        }

        ThreadPool.QueueUserWorkItem(_ => CheckForLatestRelease());
    }

    private void CheckForLatestRelease()
    {
        try
        {
            string? failureDetail = null;
            string? json = TryFetchLatestReleaseJsonViaCurl(ref failureDetail);
            if (string.IsNullOrWhiteSpace(json))
            {
                json = TryFetchLatestReleaseJsonViaWebRequest(ref failureDetail);
            }

            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidOperationException(failureDetail ?? "Unable to fetch latest release metadata.");
            }

            GitHubLatestReleaseResponse? release = JsonConvert.DeserializeObject<GitHubLatestReleaseResponse>(json!);
            string? latestVersionLabel = ExtractReleaseVersionLabel(release?.TagName, release?.Name);
            Version? currentVersion = TryParseComparableVersion(PublicVersionNumber);
            Version? latestVersion = TryParseComparableVersion(latestVersionLabel);

            lock (_updateCheckSync)
            {
                _latestReleaseVersionLabel = latestVersionLabel;
                _updateAvailable =
                    latestVersion != null &&
                    currentVersion != null &&
                    latestVersion.CompareTo(currentVersion) > 0;
            }

            if (_updateAvailable && !string.IsNullOrWhiteSpace(latestVersionLabel))
            {
                StockTrackerLog.Write($"UpdateAvailable | current={PublicVersionLabel} latest={latestVersionLabel}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"ReleaseCheckFailure | {ex.GetType().Name} | {ex.Message}");
        }
    }

    private static string? TryFetchLatestReleaseJsonViaCurl(ref string? failureDetail)
    {
        try
        {
            string outputPath = Path.Combine(Path.GetTempPath(), "stattrack-release-check-out.json");
            string errorPath = Path.Combine(Path.GetTempPath(), "stattrack-release-check-err.txt");

            TryDeleteFile(outputPath);
            TryDeleteFile(errorPath);

            using var process = new Process();
            process.StartInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments =
                    "/c " +
                    $"curl.exe --silent --show-error --location --max-time {Math.Max(1, GitHubReleaseCheckTimeoutMs / 1000)} " +
                    $"--header \"Accept: {GitHubApiAcceptHeader}\" " +
                    $"--header \"User-Agent: StatTrackCH/{PublicVersionNumber}\" " +
                    $"\"{GitHubLatestReleaseApiUrl}\" " +
                    $"> \"{outputPath}\" 2> \"{errorPath}\"",
                UseShellExecute = false,
                CreateNoWindow = true
            };

            process.Start();
            if (!process.WaitForExit(GitHubReleaseCheckTimeoutMs + 1000))
            {
                failureDetail = "curl timed out while checking for updates.";
                return null;
            }

            string output = File.Exists(outputPath) ? File.ReadAllText(outputPath) : string.Empty;
            string error = File.Exists(errorPath) ? File.ReadAllText(errorPath) : string.Empty;
            if (process.ExitCode != 0)
            {
                failureDetail = string.IsNullOrWhiteSpace(error)
                    ? $"curl exited with code {process.ExitCode}."
                    : $"curl exited with code {process.ExitCode}: {error.Trim()}";
                return null;
            }

            if (string.IsNullOrWhiteSpace(output))
            {
                failureDetail = "curl returned an empty response while checking for updates.";
                return null;
            }

            return output;
        }
        catch (Exception ex)
        {
            failureDetail = $"curl unavailable: {ex.GetType().Name} | {ex.Message}";
            return null;
        }
        finally
        {
            TryDeleteFile(Path.Combine(Path.GetTempPath(), "stattrack-release-check-out.json"));
            TryDeleteFile(Path.Combine(Path.GetTempPath(), "stattrack-release-check-err.txt"));
        }
    }

    private static string? TryFetchLatestReleaseJsonViaWebRequest(ref string? failureDetail)
    {
        try
        {
            TrySetStaticNetProperty(typeof(ServicePointManager), "SecurityProtocol", SecurityProtocolType.Tls12);

            var request = (HttpWebRequest)WebRequest.Create(GitHubLatestReleaseApiUrl);
            request.Method = "GET";
            TrySetNetProperty(request, "Accept", GitHubApiAcceptHeader);
            TrySetNetProperty(request, "UserAgent", $"StatTrackCH/{PublicVersionNumber}");
            TrySetNetProperty(request, "Timeout", GitHubReleaseCheckTimeoutMs);
            TrySetNetProperty(request, "ReadWriteTimeout", GitHubReleaseCheckTimeoutMs);
            TrySetNetProperty(request, "AutomaticDecompression", DecompressionMethods.GZip | DecompressionMethods.Deflate);

            using var response = (HttpWebResponse)request.GetResponse();
            using var stream = response.GetResponseStream();
            if (stream == null)
            {
                failureDetail = "WebRequest returned no response stream.";
                return null;
            }

            using var reader = new StreamReader(stream, Encoding.UTF8);
            return reader.ReadToEnd();
        }
        catch (Exception ex)
        {
            failureDetail = $"WebRequest failed: {ex.GetType().Name} | {ex.Message}";
            return null;
        }
    }

    private void ApplyMainMenuVersionText(object mainMenu)
    {
        try
        {
            object? versionLabel = ResolveMainMenuVersionLabel(mainMenu);
            if (versionLabel == null)
            {
                return;
            }

            PropertyInfo? textProperty = ResolveTextProperty(versionLabel.GetType());
            if (textProperty == null)
            {
                return;
            }

            string desiredText = BuildMainMenuVersionText();
            string currentText = SafeGetPropertyValue(textProperty, versionLabel)?.ToString() ?? string.Empty;
            if (string.Equals(currentText, desiredText, StringComparison.Ordinal))
            {
                return;
            }

            textProperty.SetValue(versionLabel, desiredText, null);
        }
        catch (Exception ex)
        {
            StockTrackerLog.WriteDebug($"MainMenuVersionTextFailure | {ex.GetType().Name} | {ex.Message}");
        }
    }

    private object? ResolveMainMenuVersionLabel(object mainMenu)
    {
        Type mainMenuType = mainMenu.GetType();
        if (_mainMenuVersionLabelField == null || _mainMenuVersionLabelField.DeclaringType != mainMenuType)
        {
            _mainMenuVersionLabelField = GetAllFields(mainMenuType).FirstOrDefault(field =>
                string.Equals(field.FieldType.FullName, "TMPro.TextMeshProUGUI", StringComparison.Ordinal));
        }

        return _mainMenuVersionLabelField?.GetValue(mainMenu);
    }

    private PropertyInfo? ResolveTextProperty(Type textComponentType)
    {
        if (_textComponentTextProperty != null && _textComponentTextProperty.DeclaringType == textComponentType)
        {
            return _textComponentTextProperty;
        }

        _textComponentTextProperty = GetAllProperties(textComponentType).FirstOrDefault(property =>
            string.Equals(property.Name, "text", StringComparison.Ordinal) &&
            property.CanWrite &&
            property.PropertyType == typeof(string));
        return _textComponentTextProperty;
    }

    private string BuildMainMenuVersionText()
    {
        string versionText =
            PublicVersionLabel + "\n" +
            "<size=90%>Mod by Roxas27x</size>\n" +
            "<size=85%>Home / Ctrl +O / F8 to open the overlay</size>";

        lock (_updateCheckSync)
        {
            if (_updateAvailable && !string.IsNullOrWhiteSpace(_latestReleaseVersionLabel))
            {
                versionText += "\n" +
                    $"<size=85%><color=#F2C94C>Update available: {_latestReleaseVersionLabel}</color></size>";
            }
        }

        return versionText;
    }

    private void ApplyAnimatedMenuTint()
    {
        bool shouldApplyTint = IsAnimatedMenuTintEnabled();
        if (!shouldApplyTint)
        {
            if (_animatedMenuTintApplied)
            {
                HideAnimatedMenuTintOverlay(_animatedMenuTintOverlayImage);
                HideAnimatedMenuTintOverlay(_animatedMenuCanvasTintOverlayImage);
                HideAnimatedMenuTintOverlay(_animatedMenuWispOverlayImage);
                if (_animatedMenuRawImage != null)
                {
                    _animatedMenuRawImage.color = Color.white;
                    Material? rawMaterial = _animatedMenuRawImage.material;
                    if (rawMaterial != null && rawMaterial.HasProperty("_Color"))
                    {
                        rawMaterial.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
                    }
                    _animatedMenuRawImage.SetVerticesDirty();
                    _animatedMenuRawImage.SetMaterialDirty();
                }
            }

            ApplyAnimatedMenuWispShaderGlobals(useConfig: false);

            _animatedMenuTintApplied = false;
            _animatedMenuRawImage = null;
            _animatedMenuTintOverlayImage = null;
            _animatedMenuCanvasTintOverlayImage = null;
            _animatedMenuWispOverlayImage = null;
            return;
        }

        RawImage? rawImage = ResolveAnimatedMenuRawImage();
        if (rawImage == null)
        {
            return;
        }

        RawImage? overlayImage = EnsureAnimatedMenuTintOverlay(rawImage);
        if (overlayImage == null)
        {
            return;
        }

        RawImage? canvasOverlayImage = EnsureAnimatedMenuCanvasTintOverlay(rawImage);
        Color tint = GetAnimatedMenuTintColor();

        float childOverlayStrength = Mathf.Clamp01(_config.AnimatedMenuTintBackgroundOverlayStrength);
        if (childOverlayStrength > 0.001f)
        {
            ApplyAnimatedMenuTintOverlay(overlayImage, new Color(tint.r, tint.g, tint.b, childOverlayStrength));
        }
        else
        {
            HideAnimatedMenuTintOverlay(overlayImage);
        }

        if (canvasOverlayImage != null)
        {
            float canvasOverlayStrength = Mathf.Clamp01(_config.AnimatedMenuTintCanvasOverlayStrength);
            if (canvasOverlayStrength > 0.001f)
            {
                ApplyAnimatedMenuTintOverlay(canvasOverlayImage, new Color(tint.r, tint.g, tint.b, canvasOverlayStrength));
            }
            else
            {
                HideAnimatedMenuTintOverlay(canvasOverlayImage);
            }
        }

        HideAnimatedMenuTintOverlay(_animatedMenuWispOverlayImage);
        ApplyAnimatedMenuWispShaderGlobals(useConfig: true);

        rawImage.color = Color.white;
        rawImage.SetVerticesDirty();
        Material? material = rawImage.material;
        if (material != null && material.HasProperty("_Color"))
        {
            material.SetColor("_Color", new Color(1f, 1f, 1f, 0f));
            rawImage.SetMaterialDirty();
        }

        _animatedMenuRawImage = rawImage;
        _animatedMenuTintOverlayImage = overlayImage;
        _animatedMenuCanvasTintOverlayImage = canvasOverlayImage;
        _animatedMenuTintApplied = true;
    }

    private bool IsAnimatedMenuBackgroundSelected()
    {
        try
        {
            object? setting = _menuBackgroundSettingField?.GetValue(null);
            if (setting == null)
            {
                return false;
            }

            int currentValue = ConvertToInt32(_gameSettingCurrentValueProperty?.GetValue(setting, null));
            return currentValue == AnimatedMenuBackgroundSettingValue;
        }
        catch
        {
            return false;
        }
    }

    private RawImage? ResolveAnimatedMenuRawImage()
    {
        if (_animatedMenuRawImage != null)
        {
            return _animatedMenuRawImage;
        }

        if (_blackMenuType == null)
        {
            return null;
        }

        object? blackMenu = UnityEngine.Object.FindObjectOfType(_blackMenuType, false);
        if (blackMenu == null)
        {
            return null;
        }

        _blackMenuRawImageField ??= GetAllFields(_blackMenuType)
            .FirstOrDefault(field => typeof(RawImage).IsAssignableFrom(field.FieldType));
        if (_blackMenuRawImageField?.GetValue(blackMenu) is RawImage fieldRawImage)
        {
            _animatedMenuRawImage = fieldRawImage;
            return fieldRawImage;
        }

        if (blackMenu is Component component)
        {
            _animatedMenuRawImage = component.GetComponent<RawImage>();
            return _animatedMenuRawImage;
        }

        return null;
    }

    private bool IsAnimatedMenuTintEnabled()
    {
        return Mathf.Clamp01(_config.AnimatedMenuTintA) > 0.01f;
    }

    private Color GetAnimatedMenuTintColor()
    {
        return new Color(
            Mathf.Clamp01(_config.AnimatedMenuTintR),
            Mathf.Clamp01(_config.AnimatedMenuTintG),
            Mathf.Clamp01(_config.AnimatedMenuTintB),
            Mathf.Clamp01(_config.AnimatedMenuTintA));
    }

    private Color GetAnimatedMenuWispColor()
    {
        return new Color(
            Mathf.Clamp01(_config.AnimatedMenuWispR),
            Mathf.Clamp01(_config.AnimatedMenuWispG),
            Mathf.Clamp01(_config.AnimatedMenuWispB),
            Mathf.Clamp01(_config.AnimatedMenuWispA));
    }

    private void ApplyAnimatedMenuWispShaderGlobals(bool useConfig)
    {
        Color color = useConfig
            ? GetAnimatedMenuWispColor()
            : new Color(1f, 1f, 1f, 1f);
        float size = useConfig ? GetAnimatedMenuWispShaderSize(_config.AnimatedMenuWispSize) : 0.68f;
        float enabled = useConfig ? 1f : 0f;
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalColorRId, color.r);
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalColorGId, color.g);
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalColorBId, color.b);
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalColorAId, Mathf.Clamp01(color.a));
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalSizeId, size);
        Shader.SetGlobalFloat(AnimatedMenuWispGlobalEnabledId, enabled);
    }

    private static float GetAnimatedMenuWispShaderSize(float sliderValue)
    {
        return Mathf.Lerp(0.68f, 1.55f, Mathf.Clamp01(sliderValue));
    }

    private RawImage? EnsureAnimatedMenuTintOverlay(RawImage rawImage)
    {
        if (_animatedMenuTintOverlayImage != null)
        {
            return _animatedMenuTintOverlayImage;
        }

        Transform parentTransform = rawImage.transform;
        Transform? existingTransform = parentTransform.Find(AnimatedMenuTintOverlayObjectName);
        GameObject overlayObject;
        if (existingTransform != null)
        {
            overlayObject = existingTransform.gameObject;
        }
        else
        {
            overlayObject = new GameObject(AnimatedMenuTintOverlayObjectName);
            overlayObject.transform.SetParent(parentTransform, false);
            overlayObject.transform.SetAsLastSibling();
        }

        RectTransform? rectTransform = overlayObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = overlayObject.AddComponent<RectTransform>();
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;

        RawImage? overlayImage = overlayObject.GetComponent<RawImage>();
        if (overlayImage == null)
        {
            overlayImage = overlayObject.AddComponent<RawImage>();
        }

        overlayImage.texture = Texture2D.whiteTexture;
        overlayImage.raycastTarget = false;
        _animatedMenuTintOverlayImage = overlayImage;
        return overlayImage;
    }

    private RawImage? EnsureAnimatedMenuCanvasTintOverlay(RawImage rawImage)
    {
        if (_animatedMenuCanvasTintOverlayImage != null)
        {
            return _animatedMenuCanvasTintOverlayImage;
        }

        Canvas? canvas = rawImage.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return null;
        }

        Transform overlayParent = rawImage.transform.parent ?? canvas.transform;
        Transform? existingTransform = overlayParent.Find(AnimatedMenuCanvasTintOverlayObjectName);
        GameObject overlayObject;
        if (existingTransform != null)
        {
            overlayObject = existingTransform.gameObject;
        }
        else
        {
            overlayObject = new GameObject(AnimatedMenuCanvasTintOverlayObjectName);
            overlayObject.transform.SetParent(overlayParent, false);
        }

        if (!ReferenceEquals(overlayObject.transform.parent, overlayParent))
        {
            overlayObject.transform.SetParent(overlayParent, false);
        }

        int overlaySiblingIndex = Math.Min(rawImage.transform.GetSiblingIndex() + 1, Math.Max(0, overlayParent.childCount - 1));
        overlayObject.transform.SetSiblingIndex(overlaySiblingIndex);

        RectTransform? rectTransform = overlayObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = overlayObject.AddComponent<RectTransform>();
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;

        RawImage? overlayImage = overlayObject.GetComponent<RawImage>();
        if (overlayImage == null)
        {
            overlayImage = overlayObject.AddComponent<RawImage>();
        }

        overlayImage.texture = Texture2D.whiteTexture;
        overlayImage.raycastTarget = false;
        _animatedMenuCanvasTintOverlayImage = overlayImage;
        return overlayImage;
    }

    private RawImage? EnsureAnimatedMenuWispOverlay(RawImage rawImage)
    {
        if (_animatedMenuWispOverlayImage != null)
        {
            return _animatedMenuWispOverlayImage;
        }

        Canvas? canvas = rawImage.GetComponentInParent<Canvas>();
        if (canvas == null)
        {
            return null;
        }

        Transform overlayParent = rawImage.transform.parent ?? canvas.transform;
        Transform? existingTransform = overlayParent.Find(AnimatedMenuWispOverlayObjectName);
        GameObject overlayObject;
        if (existingTransform != null)
        {
            overlayObject = existingTransform.gameObject;
        }
        else
        {
            overlayObject = new GameObject(AnimatedMenuWispOverlayObjectName);
            overlayObject.transform.SetParent(overlayParent, false);
        }

        if (!ReferenceEquals(overlayObject.transform.parent, overlayParent))
        {
            overlayObject.transform.SetParent(overlayParent, false);
        }

        int overlaySiblingIndex = rawImage.transform.GetSiblingIndex() + 1;
        if (_animatedMenuCanvasTintOverlayImage != null &&
            ReferenceEquals(_animatedMenuCanvasTintOverlayImage.transform.parent, overlayParent))
        {
            overlaySiblingIndex = _animatedMenuCanvasTintOverlayImage.transform.GetSiblingIndex() + 1;
        }
        overlayObject.transform.SetSiblingIndex(Math.Min(overlaySiblingIndex, Math.Max(0, overlayParent.childCount - 1)));

        RectTransform? rectTransform = overlayObject.GetComponent<RectTransform>();
        if (rectTransform == null)
        {
            rectTransform = overlayObject.AddComponent<RectTransform>();
        }

        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = Vector2.zero;
        rectTransform.offsetMax = Vector2.zero;
        rectTransform.anchoredPosition = Vector2.zero;
        rectTransform.localScale = Vector3.one;

        RawImage? overlayImage = overlayObject.GetComponent<RawImage>();
        if (overlayImage == null)
        {
            overlayImage = overlayObject.AddComponent<RawImage>();
        }

        overlayImage.texture = EnsureAnimatedMenuWispTexture();
        overlayImage.raycastTarget = false;
        _animatedMenuWispOverlayImage = overlayImage;
        return overlayImage;
    }

    private static void ApplyAnimatedMenuTintOverlay(RawImage overlayImage, Color color)
    {
        overlayImage.color = color;
        overlayImage.enabled = true;
        overlayImage.SetVerticesDirty();
        overlayImage.SetMaterialDirty();
    }

    private void ApplyAnimatedMenuWispOverlay(RawImage overlayImage, Color color, float size)
    {
        overlayImage.texture = EnsureAnimatedMenuWispTexture();
        float clampedSize = Mathf.Clamp01(size);
        float appliedAlpha = Mathf.Clamp01((0.28f + (color.a * 1.1f)) * Mathf.Lerp(0.9f, 1.25f, clampedSize));
        overlayImage.color = new Color(color.r, color.g, color.b, appliedAlpha);
        float uvWidth = Mathf.Lerp(3.2f, 0.3f, clampedSize);
        float uvHeight = Mathf.Lerp(2.5f, 0.38f, clampedSize);
        float time = Time.unscaledTime;
        overlayImage.uvRect = new Rect(time * 0.018f, time * 0.011f, uvWidth, uvHeight);
        overlayImage.enabled = true;
        overlayImage.SetVerticesDirty();
        overlayImage.SetMaterialDirty();
    }

    private static void HideAnimatedMenuTintOverlay(RawImage? overlayImage)
    {
        if (overlayImage == null)
        {
            return;
        }

        overlayImage.enabled = false;
        overlayImage.color = Color.clear;
        overlayImage.uvRect = new Rect(0f, 0f, 1f, 1f);
        overlayImage.SetVerticesDirty();
        overlayImage.SetMaterialDirty();
    }

    private static string? ExtractReleaseVersionLabel(params string?[] candidates)
    {
        foreach (string? candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate))
            {
                continue;
            }

            Match match = Regex.Match(candidate, @"(?i)\bv?(\d+(?:\.\d+){1,3})\b");
            if (match.Success)
            {
                return "v" + match.Groups[1].Value;
            }
        }

        return null;
    }

    private static Version? TryParseComparableVersion(string? versionText)
    {
        if (string.IsNullOrWhiteSpace(versionText))
        {
            return null;
        }

        Match match = Regex.Match(versionText, @"(?i)\bv?(\d+(?:\.\d+){1,3})\b");
        if (!match.Success)
        {
            return null;
        }

        string[] parts = match.Groups[1].Value.Split('.');
        int[] values = new int[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out values[i]))
            {
                return null;
            }
        }

        try
        {
            return values.Length switch
            {
                2 => new Version(values[0], values[1]),
                3 => new Version(values[0], values[1], values[2]),
                4 => new Version(values[0], values[1], values[2], values[3]),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static void TrySetNetProperty(object instance, string propertyName, object value)
    {
        try
        {
            instance.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)?.SetValue(instance, value, null);
        }
        catch
        {
        }
    }

    private static void TrySetStaticNetProperty(Type type, string propertyName, object value)
    {
        try
        {
            type.GetProperty(propertyName, BindingFlags.Static | BindingFlags.Public)?.SetValue(null, value, null);
        }
        catch
        {
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch
        {
        }
    }

    public void HandleOverlayUpdate()
    {
        FlushPendingMenuPersistence();

        if (_overlayEditorVisible && _exportTemplateEditorVisible)
        {
            return;
        }

        if (_overlayEditorVisible && Input.GetKeyDown(KeyCode.Escape))
        {
            _overlayEditorVisible = false;
            _exportTemplateEditorVisible = false;
            _exportTemplateEditorActiveTemplateId = null;
            StockTrackerLog.WriteDebug("OverlayToggle | visible=0 | key=Escape");
            return;
        }

        bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if ((controlHeld && Input.GetKeyDown(KeyCode.O)) ||
            Input.GetKeyDown(KeyCode.F8) ||
            Input.GetKeyDown(KeyCode.Home))
        {
            _overlayEditorVisible = !_overlayEditorVisible;
            if (!_overlayEditorVisible)
            {
                _exportTemplateEditorVisible = false;
                _exportTemplateEditorActiveTemplateId = null;
            }
            string toggleKey = controlHeld
                ? "Ctrl+O"
                : (Input.GetKeyDown(KeyCode.Home) ? "Home" : "F8");
            StockTrackerLog.WriteDebug("OverlayToggle | visible=" + (_overlayEditorVisible ? "1" : "0") + " | key=" + toggleKey);
        }
    }

    private void FlushPendingMenuPersistence()
    {
        if ((!_configDirty && !_memoryDirty) ||
            Time.unscaledTime - _lastMenuPersistenceAt < 0.5f)
        {
            return;
        }

        _lastMenuPersistenceAt = Time.unscaledTime;
        SaveConfig();
        SaveMemory();
    }

    public void RenderOverlayGui()
    {
        if (!_initialized)
        {
            return;
        }

        if (!Input.GetMouseButton(0))
        {
            _overlayDraggingKey = null;
            _overlayResizingKey = null;
        }

        OverlayRenderSnapshot snapshot = GetOrBuildOverlayRenderSnapshot();
        if (!snapshot.ShouldRender)
        {
            return;
        }

        Event? currentEvent = Event.current;
        if (!ShouldProcessOverlayGuiEvent(snapshot, currentEvent?.type))
        {
            return;
        }

        if (_songResetConfirmKey != null && Time.unscaledTime > _songResetConfirmExpiresAt)
        {
            _songResetConfirmKey = null;
            _songResetConfirmExpiresAt = 0f;
        }
        if (_overlayResetConfirmKey != null && Time.unscaledTime > _overlayResetConfirmExpiresAt)
        {
            _overlayResetConfirmKey = null;
            _overlayResetConfirmExpiresAt = 0f;
        }
        if (_wipeAllDataConfirmExpiresAt > 0f && Time.unscaledTime > _wipeAllDataConfirmExpiresAt)
        {
            _wipeAllDataConfirmExpiresAt = 0f;
        }

        bool exportTemplateModalVisible = _overlayEditorVisible && _exportTemplateEditorVisible;

        if (!exportTemplateModalVisible &&
            snapshot.RenderWidgetsInGame &&
            snapshot.WidgetEntries.Count > 0)
        {
            RenderOverlayWidgets(snapshot);
        }

        if (_overlayEditorVisible && !exportTemplateModalVisible)
        {
            RenderOverlayEditor(snapshot.State, snapshot.SongConfig);
        }

        if (exportTemplateModalVisible)
        {
            RenderExportTemplateEditor(snapshot.State);
            return;
        }

        if (snapshot.SongConfig != null || HasGlobalOverlayColorTarget())
        {
            RenderOverlayColorPicker(snapshot.SongConfig);
        }
    }

    private bool HasGlobalOverlayColorTarget()
    {
        return string.Equals(_overlayColorTargetKey, "desktop:border", StringComparison.Ordinal) ||
            string.Equals(_overlayColorTargetKey, AnimatedMenuColorPickerTargetKey, StringComparison.Ordinal) ||
            string.Equals(_overlayColorTargetKey, AnimatedMenuWispColorPickerTargetKey, StringComparison.Ordinal);
    }

    private TrackerState BuildMenuOverlayState()
    {
        EnabledTextExportSnapshot defaultExports = GetEnabledTextExportsSnapshot();
        TrackerState state = CreateIdleState();
        state.OverlayEditorVisible = _overlayEditorVisible;
        state.EnabledTextExports = defaultExports;
        return state;
    }

    private OverlayRenderSnapshot GetOrBuildOverlayRenderSnapshot()
    {
        TrackerState state = _latestState;
        if (!state.IsInSong && _overlayEditorVisible)
        {
            state = BuildMenuOverlayState();
        }

        int screenWidth = Screen.width;
        int screenHeight = Screen.height;
        if (_overlayRenderSnapshot != null &&
            _overlayRenderSnapshotStateVersion == _latestStateVersion &&
            _overlayRenderSnapshotConfigVersion == _overlayConfigVersion &&
            _overlayRenderSnapshotScreenWidth == screenWidth &&
            _overlayRenderSnapshotScreenHeight == screenHeight &&
            _overlayRenderSnapshotEditorVisible == _overlayEditorVisible)
        {
            return _overlayRenderSnapshot;
        }

        SongConfig? songConfig = state.Song == null ? null : TryGetSongConfig(state.Song);
        var snapshot = new OverlayRenderSnapshot
        {
            State = state,
            SongConfig = songConfig,
            ShouldRender = state.IsInSong || _overlayEditorVisible,
            OverlayEditorVisible = _overlayEditorVisible,
            RenderWidgetsInGame = state.IsInSong && (_overlayEditorVisible || !IsDesktopOverlayRunning())
        };

        if (snapshot.RenderWidgetsInGame && songConfig != null)
        {
            snapshot.WidgetEntries = BuildOverlayWidgetEntries(state, songConfig);
        }

        _overlayRenderSnapshot = snapshot;
        _overlayRenderSnapshotStateVersion = _latestStateVersion;
        _overlayRenderSnapshotConfigVersion = _overlayConfigVersion;
        _overlayRenderSnapshotScreenWidth = screenWidth;
        _overlayRenderSnapshotScreenHeight = screenHeight;
        _overlayRenderSnapshotEditorVisible = _overlayEditorVisible;
        return snapshot;
    }

    private static bool ShouldProcessOverlayGuiEvent(OverlayRenderSnapshot snapshot, EventType? eventType)
    {
        if (eventType == null)
        {
            return false;
        }

        if (snapshot.OverlayEditorVisible)
        {
            return eventType == EventType.Layout ||
                eventType == EventType.Repaint ||
                eventType == EventType.KeyDown ||
                eventType == EventType.MouseDown ||
                eventType == EventType.MouseDrag ||
                eventType == EventType.MouseUp ||
                eventType == EventType.ScrollWheel;
        }

        if (snapshot.WidgetEntries.Count == 0)
        {
            return false;
        }

        return eventType == EventType.Repaint ||
            eventType == EventType.MouseDown ||
            eventType == EventType.MouseDrag ||
            eventType == EventType.MouseUp;
    }

    private void RenderAnimatedMenuDiagnosticOnGuiOverlay()
    {
        if (_latestState.IsInSong || !IsAnimatedMenuTintEnabled())
        {
            return;
        }

        float strength = Mathf.Clamp01(_config.AnimatedMenuTintOnGuiOverlayStrength);
        if (strength <= 0.001f)
        {
            return;
        }

        Color tint = GetAnimatedMenuTintColor();
        Color overlayColor = new Color(tint.r, tint.g, tint.b, strength);
        GUI.Label(
            new Rect(0f, 0f, Screen.width, Screen.height),
            new GUIContent(string.Empty, EnsureAnimatedMenuOnGuiOverlayTexture(overlayColor), string.Empty),
            GUI.skin.label);
    }

    private List<OverlayWidgetRenderEntry> BuildOverlayWidgetEntries(TrackerState state, SongConfig songConfig)
    {
        Dictionary<string, int> sectionOrder = BuildOverlaySectionOrder(state);
        List<KeyValuePair<string, OverlayWidgetConfig>> widgets = songConfig.OverlayWidgets
            .Where(pair => pair.Value != null && pair.Value.Enabled)
            .ToList();
        widgets.Sort((left, right) => CompareOverlayWidgetEntries(sectionOrder, left, right));

        var entries = new List<OverlayWidgetRenderEntry>(widgets.Count);
        for (int i = 0; i < widgets.Count; i++)
        {
            string widgetKey = widgets[i].Key;
            OverlayWidgetConfig widgetConfig = widgets[i].Value;
            if (!TryBuildOverlayWidget(state, widgetKey, out string title, out string content))
            {
                continue;
            }

            entries.Add(new OverlayWidgetRenderEntry
            {
                WidgetKey = widgetKey,
                Config = widgetConfig,
                Title = title,
                Content = content,
                DefaultIndex = i
            });
        }

        return entries;
    }

    private void LogMenuOverlayStateFailure(Exception ex)
    {
        Exception detail = ex.InnerException ?? ex;
        string message = ex.GetType().Name + " | " + ex.Message;
        if (!ReferenceEquals(detail, ex))
        {
            message += " | inner=" + detail.GetType().Name + " | " + detail.Message;
        }

        if (string.Equals(_lastMenuOverlayStateFailureMessage, message, StringComparison.Ordinal))
        {
            return;
        }

        _lastMenuOverlayStateFailureMessage = message;
        StockTrackerLog.Write("MenuOverlayStateFailure | " + message);
    }

    private void RenderOverlayWidgets(OverlayRenderSnapshot snapshot)
    {
        for (int i = 0; i < snapshot.WidgetEntries.Count; i++)
        {
            OverlayWidgetRenderEntry widget = snapshot.WidgetEntries[i];
            Rect rect = GetWidgetRect(widget.Config, widget.DefaultIndex);
            Rect updated = RenderOverlayWidgetPanel("widget:" + widget.WidgetKey, widget.Config, rect, widget.Title, widget.Content);
            PersistWidgetRect(widget.Config, updated);
        }
    }

    private void RenderOverlayEditor(TrackerState state, SongConfig? songConfig)
    {
        OverlayEditorConfig overlayConfig = EnsureOverlayEditorConfig();
        Dictionary<string, bool> defaultEnabledTextExports = EnsureDefaultEnabledTextExports();
        Rect rect = GetEditorRect(overlayConfig);
        bool resizeHandleVisible = !overlayConfig.ResizeHandleHidden;
        Rect updated = RenderOverlayPanel("editor", rect, "StatTrack Overlay", resizeHandleVisible, visible =>
        {
            if (overlayConfig.ResizeHandleHidden == !visible)
            {
                return;
            }

            overlayConfig.ResizeHandleHidden = !visible;
            MarkConfigDirty(affectsGlobalSnapshot: true);
        }, contentRect =>
        {
            GUILayout.BeginArea(contentRect);
            const float headerHeight = 58f;
            float infoWidth = Mathf.Max(160f, Mathf.Min(280f, contentRect.width * 0.48f));
            float infoX = Mathf.Max(0f, contentRect.width - infoWidth);
            GUIStyle songTitleStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperRight,
                fontStyle = FontStyle.Bold
            };
            GUIStyle songMetaStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.UpperRight
            };
            GUIStyle obsFolderButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = Mathf.Max(10, GUI.skin.button.fontSize - 1),
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(new Rect(0f, 0f, Mathf.Max(0f, contentRect.width - infoWidth - 12f), 20f), "Created by Roxas27x", GUI.skin.label);
            GUI.Label(new Rect(0f, 22f, Mathf.Max(0f, contentRect.width - infoWidth - 12f), 20f), "Esc / Home / Ctrl+O / F8 closes this editor.", GUI.skin.label);

            if (state.Song == null || songConfig == null)
            {
                GUI.Label(new Rect(infoX, 0f, infoWidth, 20f), "No song selected.", songTitleStyle);
                GUI.Label(new Rect(infoX, 22f, infoWidth, 36f), "Export defaults", songMetaStyle);
                GUILayout.Label(string.Empty, GUILayout.Height(headerHeight));
                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                float scrollHeight = Mathf.Max(110f, contentRect.height - 72f);
                _overlayEditorScroll = GUILayout.BeginScrollView(_overlayEditorScroll, GUILayout.Width(contentRect.width), GUILayout.Height(scrollHeight));
                GUILayout.BeginHorizontal(GUILayout.Width(contentRect.width - 24f));
                GUILayout.Label("Exports / Desktop Modes", GUILayout.Width(Mathf.Max(120f, contentRect.width - 180f)));
                bool openObsExportFolderClicked = GUILayout.Toggle(false, new GUIContent("OBS EXPORT FOLDER"), obsFolderButtonStyle, GUILayout.Width(Mathf.Min(150f, contentRect.width - 24f)));
                if (openObsExportFolderClicked)
                {
                    OpenTrackerDataFolder();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Enabled checkboxes here control OBS exports and desktop overlay modes.", GUILayout.Width(contentRect.width - 24f));
                GUIStyle exportWarningStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = new Color(1f, 0.35f, 0.35f, 1f) }
                };
                GUILayout.Label("More enabled at once = stronger hitching potential.", exportWarningStyle, GUILayout.Width(contentRect.width - 24f));
                foreach (TextExportDefinition exportDefinition in TextExportDefinition.All)
                {
                    bool enabled = defaultEnabledTextExports.TryGetValue(exportDefinition.Key, out bool defaultEnabled) && defaultEnabled;
                    bool updatedEnabled = GUILayout.Toggle(enabled, new GUIContent(exportDefinition.Label), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                    if (updatedEnabled != enabled)
                    {
                        defaultEnabledTextExports[exportDefinition.Key] = updatedEnabled;
                        MarkConfigDirty(affectsTextExports: true, affectsGlobalSnapshot: true);
                        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: true);
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool disableAllExportsClicked = GUILayout.Toggle(false, new GUIContent("DISABLE ALL EXPORTS"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (disableAllExportsClicked)
                {
                    DisableAllTextExports(defaultEnabledTextExports);
                }

                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                bool openExportTemplatesClicked = GUILayout.Toggle(false, new GUIContent("EXPORT TEMPLATES"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (openExportTemplatesClicked)
                {
                    _exportTemplateEditorVisible = true;
                    EnsureSelectedExportTemplateId();
                }

                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                GUILayout.Label("Main Menu Animated Tint", GUILayout.Width(contentRect.width - 24f));
                bool animatedMenuTintEnabled = IsAnimatedMenuTintEnabled();
                bool updatedAnimatedMenuTintEnabled = GUILayout.Toggle(animatedMenuTintEnabled, new GUIContent("ENABLE LIVE ANIMATED MENU TINT"), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                if (updatedAnimatedMenuTintEnabled != animatedMenuTintEnabled)
                {
                    _config.AnimatedMenuTintA = updatedAnimatedMenuTintEnabled ? 1f : 0f;
                    MarkConfigDirty(affectsGlobalSnapshot: true);
                    ApplyAnimatedMenuTint();
                }

                bool previousGuiEnabled = GUI.enabled;
                GUI.enabled = updatedAnimatedMenuTintEnabled;
                bool animatedMenuTintClicked = GUILayout.Toggle(false, new GUIContent("ANIMATED MENU COLOR"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                GUI.enabled = previousGuiEnabled;
                if (animatedMenuTintClicked)
                {
                    OpenAnimatedMenuColorPicker(rect);
                }

                GUI.enabled = updatedAnimatedMenuTintEnabled;
                bool animatedMenuWispClicked = GUILayout.Toggle(false, new GUIContent("WISP COLOR"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                GUI.enabled = previousGuiEnabled;
                if (animatedMenuWispClicked)
                {
                    OpenAnimatedMenuWispColorPicker(rect);
                }

                if (updatedAnimatedMenuTintEnabled)
                {
                    RenderAnimatedMenuTintControls(contentRect.width - 24f);
                }
                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                RenderOverlayEditorTransparencyControls(overlayConfig, contentRect.width - 24f);
                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                GUILayout.Label("These export settings are global and apply to every song.", GUILayout.Width(contentRect.width - 24f));
                GUILayout.Label(string.Empty, GUILayout.Height(24f));
                GUILayout.EndScrollView();
            }
            else
            {
                GUI.Label(new Rect(infoX, 0f, infoWidth, 20f), GetOverlaySongTitle(state.Song), songTitleStyle);
                GUI.Label(new Rect(infoX, 22f, infoWidth, 36f), $"{state.Song.Artist ?? "Unknown Artist"} | {state.Song.DifficultyName} | {state.Song.SongSpeedLabel}", songMetaStyle);
                GUILayout.Label(string.Empty, GUILayout.Height(headerHeight));
                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                float scrollHeight = Mathf.Max(110f, contentRect.height - 72f);
                _overlayEditorScroll = GUILayout.BeginScrollView(_overlayEditorScroll, GUILayout.Width(contentRect.width), GUILayout.Height(scrollHeight));
                GUILayout.BeginHorizontal(GUILayout.Width(contentRect.width - 24f));
                GUILayout.Label("Exports / Desktop Modes", GUILayout.Width(Mathf.Max(120f, contentRect.width - 180f)));
                bool openObsExportFolderClicked = GUILayout.Toggle(false, new GUIContent("OBS EXPORT FOLDER"), obsFolderButtonStyle, GUILayout.Width(Mathf.Min(150f, contentRect.width - 24f)));
                if (openObsExportFolderClicked)
                {
                    OpenTrackerDataFolder();
                }
                GUILayout.EndHorizontal();
                GUILayout.Label("Enabled checkboxes here control OBS exports and desktop overlay modes.", GUILayout.Width(contentRect.width - 24f));
                GUIStyle exportWarningStyle = new GUIStyle(GUI.skin.label)
                {
                    normal = { textColor = new Color(1f, 0.35f, 0.35f, 1f) }
                };
                GUILayout.Label("More enabled at once = stronger hitching potential.", exportWarningStyle, GUILayout.Width(contentRect.width - 24f));
                foreach (TextExportDefinition exportDefinition in TextExportDefinition.All)
                {
                    bool enabled = defaultEnabledTextExports.TryGetValue(exportDefinition.Key, out bool defaultEnabled) && defaultEnabled;
                    bool updatedEnabled = GUILayout.Toggle(enabled, new GUIContent(exportDefinition.Label), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                    if (updatedEnabled != enabled)
                    {
                        defaultEnabledTextExports[exportDefinition.Key] = updatedEnabled;
                        MarkConfigDirty(affectsTextExports: true, affectsGlobalSnapshot: true);
                        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: true);
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool disableAllExportsClicked = GUILayout.Toggle(false, new GUIContent("DISABLE ALL EXPORTS"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (disableAllExportsClicked)
                {
                    DisableAllTextExports(defaultEnabledTextExports);
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool openExportTemplatesClicked = GUILayout.Toggle(false, new GUIContent("EXPORT TEMPLATES"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (openExportTemplatesClicked)
                {
                    _exportTemplateEditorVisible = true;
                    EnsureSelectedExportTemplateId();
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                GUILayout.Label("Main Menu Animated Tint", GUILayout.Width(contentRect.width - 24f));
                bool animatedMenuTintEnabled = IsAnimatedMenuTintEnabled();
                bool updatedAnimatedMenuTintEnabled = GUILayout.Toggle(animatedMenuTintEnabled, new GUIContent("ENABLE LIVE ANIMATED MENU TINT"), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                if (updatedAnimatedMenuTintEnabled != animatedMenuTintEnabled)
                {
                    _config.AnimatedMenuTintA = updatedAnimatedMenuTintEnabled ? 1f : 0f;
                    MarkConfigDirty(affectsGlobalSnapshot: true);
                    ApplyAnimatedMenuTint();
                }

                bool previousGuiEnabled = GUI.enabled;
                GUI.enabled = updatedAnimatedMenuTintEnabled;
                bool animatedMenuTintClicked = GUILayout.Toggle(false, new GUIContent("ANIMATED MENU COLOR"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                GUI.enabled = previousGuiEnabled;
                if (animatedMenuTintClicked)
                {
                    OpenAnimatedMenuColorPicker(rect);
                }

                GUI.enabled = updatedAnimatedMenuTintEnabled;
                bool animatedMenuWispClicked = GUILayout.Toggle(false, new GUIContent("WISP COLOR"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                GUI.enabled = previousGuiEnabled;
                if (animatedMenuWispClicked)
                {
                    OpenAnimatedMenuWispColorPicker(rect);
                }

                if (updatedAnimatedMenuTintEnabled)
                {
                    RenderAnimatedMenuTintControls(contentRect.width - 24f);
                }
                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool showTrackedSections = HasAnyTrackedSectionExportEnabled(songConfig) || _overlayEditorVisible;
                if (showTrackedSections)
                {
                    GUILayout.Label("Live Section Export Select", GUILayout.Width(contentRect.width - 24f));
                    GUIStyle sectionHelpStyle = new GUIStyle(GUI.skin.label)
                    {
                        fontSize = Mathf.Max(10, GUI.skin.label.fontSize - 2)
                    };
                    GUILayout.Label("Checking these boxes will allow their current Attempts, FC's Past, and Killed the Run values to be actively exported to OBS. When at least one section is checked, current_section_summary.txt will also stay active.", sectionHelpStyle, GUILayout.Width(contentRect.width - 24f));
                    foreach (SectionStatsState section in GetOverlaySectionsForOverlay(state))
                    {
                        string sectionOverlayKey = BuildSectionOverlayKey(state.SectionStats, section);
                        bool tracked = songConfig.TrackedSections.TryGetValue(sectionOverlayKey, out bool trackedValue) && trackedValue;
                        bool updatedTracked = GUILayout.Toggle(tracked, new GUIContent(BuildEditorSectionLabel(section)), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                        if (updatedTracked != tracked)
                        {
                            songConfig.TrackedSections[sectionOverlayKey] = updatedTracked;
                            MarkConfigDirty(affectsSectionSnapshots: true);
                            RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: true);
                        }
                    }

                    GUILayout.Label(string.Empty, GUILayout.Height(8f));
                }

                GUILayout.Label("Section Widgets", GUILayout.Width(contentRect.width - 24f));
                foreach (SectionStatsState section in GetOverlaySectionsForOverlay(state))
                {
                    string sectionOverlayKey = BuildSectionOverlayKey(state.SectionStats, section);
                    bool enabled = IsWidgetEnabled(songConfig, BuildSectionWidgetKey(sectionOverlayKey));
                    bool updatedEnabled = GUILayout.Toggle(enabled, new GUIContent(BuildEditorSectionLabel(section)), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                    if (updatedEnabled != enabled)
                    {
                        SetWidgetEnabled(songConfig, BuildSectionWidgetKey(sectionOverlayKey), updatedEnabled);
                        MarkConfigDirty();
                        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: false);
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                GUILayout.Label("Stat Widgets", GUILayout.Width(contentRect.width - 24f));
                foreach (OverlayMetricDefinition metric in OverlayMetricDefinition.All)
                {
                    bool enabled = IsWidgetEnabled(songConfig, BuildMetricWidgetKey(metric.Key));
                    bool updatedEnabled = GUILayout.Toggle(enabled, new GUIContent(metric.Label), GUI.skin.toggle, GUILayout.Width(contentRect.width - 24f));
                    if (updatedEnabled != enabled)
                    {
                        SetWidgetEnabled(songConfig, BuildMetricWidgetKey(metric.Key), updatedEnabled);
                        MarkConfigDirty();
                        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: false);
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                bool desktopBorderClicked = GUILayout.Toggle(false, new GUIContent("WIDGET BORDER COLOR"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (desktopBorderClicked)
                {
                    OpenDesktopBorderColorPicker(rect);
                }

                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                RenderOverlayEditorTransparencyControls(overlayConfig, contentRect.width - 24f);
                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                string resetOverlayLabel = string.Equals(_overlayResetConfirmKey, state.Song.SongKey, StringComparison.Ordinal) &&
                    Time.unscaledTime <= _overlayResetConfirmExpiresAt
                    ? "Are you sure?"
                    : "RESET OVERLAY";
                bool resetOverlayClicked = GUILayout.Toggle(false, new GUIContent(resetOverlayLabel), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (resetOverlayClicked)
                {
                    if (string.Equals(_overlayResetConfirmKey, state.Song.SongKey, StringComparison.Ordinal) &&
                        Time.unscaledTime <= _overlayResetConfirmExpiresAt)
                    {
                        ResetSongOverlay(songConfig);
                        _overlayResetConfirmKey = null;
                        _overlayResetConfirmExpiresAt = 0f;
                    }
                    else
                    {
                        _overlayResetConfirmKey = state.Song.SongKey;
                        _overlayResetConfirmExpiresAt = Time.unscaledTime + 5f;
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                GUILayout.BeginHorizontal();
                string resetLabel = string.Equals(_songResetConfirmKey, state.Song.SongKey, StringComparison.Ordinal) &&
                    Time.unscaledTime <= _songResetConfirmExpiresAt
                    ? "Are you sure?"
                    : "RESET SONG STATS";
                bool resetClicked = GUILayout.Toggle(false, new GUIContent(resetLabel), GUI.skin.button, GUILayout.Width(Mathf.Min(200f, (contentRect.width - 36f) * 0.5f)));
                if (resetClicked)
                {
                    if (string.Equals(_songResetConfirmKey, state.Song.SongKey, StringComparison.Ordinal) &&
                        Time.unscaledTime <= _songResetConfirmExpiresAt)
                    {
                        ResetSongStats(state);
                        _songResetConfirmKey = null;
                        _songResetConfirmExpiresAt = 0f;
                    }
                    else
                    {
                        _songResetConfirmKey = state.Song.SongKey;
                        _songResetConfirmExpiresAt = Time.unscaledTime + 5f;
                    }
                }

                string wipeAllLabel = Time.unscaledTime <= _wipeAllDataConfirmExpiresAt
                    ? "Are you sure?"
                    : "WIPE ALL MOD DATA";
                bool wipeAllClicked = GUILayout.Toggle(false, new GUIContent(wipeAllLabel), GUI.skin.button, GUILayout.Width(Mathf.Min(200f, (contentRect.width - 36f) * 0.5f)));
                if (wipeAllClicked)
                {
                    if (Time.unscaledTime <= _wipeAllDataConfirmExpiresAt)
                    {
                        WipeAllModData();
                        _wipeAllDataConfirmExpiresAt = 0f;
                    }
                    else
                    {
                        _wipeAllDataConfirmExpiresAt = Time.unscaledTime + 5f;
                    }
                }
                GUILayout.EndHorizontal();
                GUILayout.Label(string.Empty, GUILayout.Height(10f));
                GUILayout.Label("Selected widgets remain draggable after you close this editor.", GUILayout.Width(contentRect.width - 24f));
                GUILayout.Label(string.Empty, GUILayout.Height(24f));
                GUILayout.EndScrollView();
            }
            GUILayout.EndArea();
        });

        PersistEditorRect(overlayConfig, updated);
    }

    private void RenderExportTemplateEditor(TrackerState state)
    {
        EnsureSelectedExportTemplateId();
        ExportTemplateEditorConfig panelConfig = EnsureExportTemplateEditorConfig();
        Rect rect = GetEditorRect(panelConfig);
        bool resizeHandleVisible = !panelConfig.ResizeHandleHidden;
        Rect updated = RenderOverlayPanel("export-templates", rect, "Export Templates", resizeHandleVisible, visible =>
        {
            if (panelConfig.ResizeHandleHidden == !visible)
            {
                return;
            }

            panelConfig.ResizeHandleHidden = !visible;
            MarkConfigDirty(affectsGlobalSnapshot: true);
        }, contentRect =>
        {
            RenderExportTemplateEditorContent(state, contentRect);
        });

        PersistEditorRect(panelConfig, updated);
    }

    private void RenderExportTemplateEditorContent(TrackerState state, Rect contentRect)
    {
        ExportTemplateDefinition? selectedDefinition = GetSelectedExportTemplateDefinition();
        if (selectedDefinition == null)
        {
            return;
        }

        string draftText = GetExportTemplateDraftText(selectedDefinition);
        string savedText = GetEffectiveExportTemplateSource(selectedDefinition);
        bool hasSavedOverride = EnsureExportTemplateOverrides().ContainsKey(selectedDefinition.TemplateId);
        bool isDirty = !string.Equals(draftText, savedText, StringComparison.Ordinal);
        bool differsFromDefault = !string.Equals(draftText, selectedDefinition.DefaultTemplate, StringComparison.Ordinal);
        bool templateValid = ExportTemplateEngine.TryCompile(selectedDefinition, draftText, out CompiledExportTemplate? compiledTemplate, out string? validationError, out int validationLine);

        string previewText;
        string statusText;
        Color statusColor;
        if (!templateValid || compiledTemplate == null)
        {
            previewText = "Preview unavailable until the template validates.";
            statusText = $"Invalid template on line {validationLine.ToString(CultureInfo.InvariantCulture)}: {validationError}";
            statusColor = new Color(1f, 0.45f, 0.45f, 1f);
        }
        else if (TryBuildExportTemplatePreview(selectedDefinition, compiledTemplate, state, out string? generatedPreview, out string? unavailableMessage))
        {
            previewText = generatedPreview ?? string.Empty;
            statusText = isDirty ? "Valid template. Unsaved changes are local to this editor until you press SAVE." : "Valid template. Saved output is live.";
            statusColor = isDirty
                ? new Color(1f, 0.86f, 0.48f, 1f)
                : new Color(0.55f, 0.95f, 0.67f, 1f);
        }
        else
        {
            previewText = unavailableMessage ?? "Preview unavailable for the current state.";
            statusText = isDirty ? "Valid template. Unsaved changes are local to this editor until you press SAVE." : "Valid template. Saved output is live.";
            statusColor = isDirty
                ? new Color(1f, 0.86f, 0.48f, 1f)
                : new Color(0.55f, 0.95f, 0.67f, 1f);
        }

        GUIStyle introStyle = new GUIStyle(GUI.skin.label)
        {
        };
        introStyle.normal.textColor = new Color(0.82f, 0.9f, 1f, 0.95f);

        GUIStyle statusStyle = new GUIStyle(GUI.skin.label)
        {
        };
        statusStyle.normal.textColor = statusColor;

        GUIStyle helpStyle = new GUIStyle(GUI.skin.label)
        {
        };
        helpStyle.normal.textColor = new Color(0.8f, 0.88f, 0.98f, 0.96f);

        GUIStyle sectionHeaderStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };
        sectionHeaderStyle.normal.textColor = Color.white;

        GUIStyle previewStyle = new GUIStyle(GUI.skin.label)
        {
        };
        previewStyle.normal.textColor = new Color(0.93f, 0.97f, 1f, 0.98f);

        GUIStyle categoryStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold
        };
        categoryStyle.normal.textColor = new Color(0.71f, 0.84f, 1f, 0.98f);

        float actionHeight = 28f;
        float gap = 8f;
        float headerInfoHeight = 42f;
        float previewHeight = Mathf.Clamp(contentRect.height * 0.26f, 130f, 200f);
        float upperY = actionHeight + gap + headerInfoHeight + gap;
        float upperHeight = Mathf.Max(180f, contentRect.height - upperY - previewHeight - gap);
        float leftWidth = Mathf.Clamp(contentRect.width * 0.22f, 180f, 250f);
        float rightWidth = Mathf.Clamp(contentRect.width * 0.24f, 210f, 300f);
        float centerWidth = Mathf.Max(220f, contentRect.width - leftWidth - rightWidth - (gap * 2f));

        Rect saveRect = new Rect(contentRect.x, contentRect.y, 110f, actionHeight);
        Rect revertRect = new Rect(saveRect.xMax + gap, contentRect.y, 140f, actionHeight);
        Rect resetRect = new Rect(revertRect.xMax + gap, contentRect.y, 140f, actionHeight);
        Rect closeRect = new Rect(contentRect.xMax - 100f, contentRect.y, 100f, actionHeight);
        Rect infoRect = new Rect(contentRect.x, contentRect.y + actionHeight + gap, contentRect.width, headerInfoHeight);
        Rect listRect = new Rect(contentRect.x, contentRect.y + upperY, leftWidth, upperHeight);
        Rect editorRect = new Rect(listRect.xMax + gap, listRect.y, centerWidth, upperHeight);
        Rect helpRect = new Rect(editorRect.xMax + gap, listRect.y, rightWidth, upperHeight);
        Rect previewRect = new Rect(contentRect.x, listRect.yMax + gap, contentRect.width, previewHeight);

        bool previousGuiEnabled = GUI.enabled;
        GUI.enabled = templateValid && isDirty;
        if (GUI.Toggle(saveRect, false, new GUIContent("SAVE"), GUI.skin.button))
        {
            SaveExportTemplateDraft(selectedDefinition, draftText);
        }

        GUI.enabled = isDirty;
        if (GUI.Toggle(revertRect, false, new GUIContent("REVERT CHANGES"), GUI.skin.button))
        {
            RevertExportTemplateDraft(selectedDefinition.TemplateId);
            draftText = GetExportTemplateDraftText(selectedDefinition);
        }

        GUI.enabled = hasSavedOverride || differsFromDefault;
        if (GUI.Toggle(resetRect, false, new GUIContent("RESET TO DEFAULT"), GUI.skin.button))
        {
            ResetExportTemplateOverride(selectedDefinition);
            draftText = GetExportTemplateDraftText(selectedDefinition);
        }

        GUI.enabled = previousGuiEnabled;
        if (GUI.Toggle(closeRect, false, new GUIContent("CLOSE"), GUI.skin.button))
        {
            _exportTemplateEditorVisible = false;
        }

        GUI.Label(infoRect, $"Configure the text inside exported .txt files. Use {{token}} replacements and [[if token]] / [[ifnot token]] line guards.{Environment.NewLine}Changes stay local to this panel until you press SAVE.", introStyle);

        GUI.Box(listRect, GUIContent.none, GUI.skin.box);
        GUI.Box(editorRect, GUIContent.none, GUI.skin.box);
        GUI.Box(helpRect, GUIContent.none, GUI.skin.box);
        GUI.Box(previewRect, GUIContent.none, GUI.skin.box);

        RenderExportTemplateList(listRect, categoryStyle);
        string updatedDraftText = RenderExportTemplateEditorTextArea(editorRect, selectedDefinition, draftText, sectionHeaderStyle);
        if (!string.Equals(updatedDraftText, draftText, StringComparison.Ordinal))
        {
            SetExportTemplateDraftText(selectedDefinition.TemplateId, updatedDraftText);
            draftText = updatedDraftText;
        }

        RenderExportTemplateHelp(helpRect, selectedDefinition, sectionHeaderStyle, helpStyle);
        RenderExportTemplatePreview(previewRect, statusText, previewText, sectionHeaderStyle, statusStyle, previewStyle);
    }

    private void RenderExportTemplateList(Rect listRect, GUIStyle categoryStyle)
    {
        Rect headerRect = new Rect(listRect.x + 10f, listRect.y + 8f, listRect.width - 20f, 20f);
        GUI.Label(headerRect, "Template List", categoryStyle);

        Rect scrollRect = new Rect(listRect.x + 8f, listRect.y + 30f, Mathf.Max(0f, listRect.width - 16f), Mathf.Max(0f, listRect.height - 38f));
        GUILayout.BeginArea(scrollRect);
        _exportTemplateEditorListScroll = GUILayout.BeginScrollView(
            _exportTemplateEditorListScroll,
            GUILayout.Width(scrollRect.width),
            GUILayout.Height(scrollRect.height));
        foreach (ExportTemplateCategory category in Enum.GetValues(typeof(ExportTemplateCategory)))
        {
            GUILayout.Label(ExportTemplateCatalog.GetCategoryLabel(category), categoryStyle, GUILayout.Width(Mathf.Max(0f, scrollRect.width - 26f)));
            foreach (ExportTemplateDefinition definition in ExportTemplateCatalog.All.Where(candidate => candidate.Category == category))
            {
                bool selected = string.Equals(_selectedExportTemplateId, definition.TemplateId, StringComparison.Ordinal);
                bool clicked = GUILayout.Toggle(selected, new GUIContent(definition.Label), GUI.skin.button, GUILayout.Width(Mathf.Max(0f, scrollRect.width - 26f)));
                if (clicked && !selected)
                {
                    _selectedExportTemplateId = definition.TemplateId;
                }
            }

            GUILayout.Label(string.Empty, GUILayout.Height(4f));
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private string RenderExportTemplateEditorTextArea(Rect editorRect, ExportTemplateDefinition definition, string draftText, GUIStyle sectionHeaderStyle)
    {
        Rect titleRect = new Rect(editorRect.x + 10f, editorRect.y + 8f, editorRect.width - 20f, 20f);
        Rect subtitleRect = new Rect(editorRect.x + 10f, editorRect.y + 28f, editorRect.width - 20f, 18f);
        Rect scrollRect = new Rect(editorRect.x + 8f, editorRect.y + 50f, Mathf.Max(0f, editorRect.width - 16f), Mathf.Max(0f, editorRect.height - 58f));

        GUIStyle subtitleStyle = new GUIStyle(GUI.skin.label);
        subtitleStyle.normal.textColor = new Color(0.72f, 0.82f, 0.93f, 0.95f);

        GUIStyle lineStyle = new GUIStyle(GUI.skin.textField);
        GUIStyle lineNumberStyle = new GUIStyle(GUI.skin.label);
        lineNumberStyle.alignment = TextAnchor.MiddleRight;
        lineNumberStyle.normal.textColor = new Color(0.68f, 0.8f, 0.94f, 0.94f);

        GUI.Label(titleRect, definition.Label, sectionHeaderStyle);
        GUI.Label(subtitleRect, definition.TemplateId, subtitleStyle);

        string normalizedDraft = (draftText ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        List<string> lines = normalizedDraft.Split(new[] { '\n' }, StringSplitOptions.None).ToList();
        if (lines.Count == 0)
        {
            lines.Add(string.Empty);
        }

        EnsureExportTemplateEditorSelection(definition.TemplateId, lines);

        GUILayout.BeginArea(scrollRect);
        _exportTemplateEditorTextScroll = GUILayout.BeginScrollView(
            _exportTemplateEditorTextScroll,
            GUILayout.Width(scrollRect.width),
            GUILayout.Height(scrollRect.height));

        bool changed = false;
        float lineWidth = Mathf.Max(80f, scrollRect.width - 88f);
        for (int i = 0; i < lines.Count; i++)
        {
            Rect rowRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Width(Mathf.Max(0f, scrollRect.width - 26f)), GUILayout.Height(24f));
            Rect lineNumberRect = new Rect(rowRect.x, rowRect.y + 3f, 26f, 18f);
            Rect fieldRect = new Rect(rowRect.x + 30f, rowRect.y + 2f, lineWidth, 20f);
            Rect removeRect = new Rect(fieldRect.xMax + 4f, rowRect.y + 1f, 24f, 22f);
            bool lineSelected =
                string.Equals(_exportTemplateEditorActiveTemplateId, definition.TemplateId, StringComparison.Ordinal) &&
                _exportTemplateEditorActiveLineIndex == i;
            Event? currentEvent = Event.current;

            if (currentEvent != null &&
                currentEvent.type == EventType.MouseDown &&
                fieldRect.Contains(currentEvent.mousePosition))
            {
                _exportTemplateEditorActiveTemplateId = definition.TemplateId;
                _exportTemplateEditorActiveLineIndex = i;
                _exportTemplateEditorCursorIndex = (lines[i] ?? string.Empty).Length;
                currentEvent.Use();
                lineSelected = true;
            }

            if (lineSelected)
            {
                GUI.Box(rowRect, GUIContent.none, GUI.skin.box);
            }

            GUI.Label(lineNumberRect, (i + 1).ToString(CultureInfo.InvariantCulture), lineNumberStyle);
            string displayText = lineSelected
                ? BuildTemplateEditorDisplayLine(lines[i] ?? string.Empty, _exportTemplateEditorCursorIndex)
                : (lines[i] ?? string.Empty);
            GUI.Box(fieldRect, GUIContent.none, GUI.skin.box);
            GUI.Label(new Rect(fieldRect.x + 4f, fieldRect.y + 1f, Mathf.Max(0f, fieldRect.width - 8f), fieldRect.height - 2f), displayText, lineStyle);

            bool removeClicked = GUI.Toggle(removeRect, false, new GUIContent("X"), GUI.skin.button);
            if (removeClicked && lines.Count > 1)
            {
                lines.RemoveAt(i);
                if (_exportTemplateEditorActiveLineIndex >= lines.Count)
                {
                    _exportTemplateEditorActiveLineIndex = lines.Count - 1;
                }

                if (_exportTemplateEditorActiveLineIndex < 0)
                {
                    _exportTemplateEditorActiveLineIndex = 0;
                }

                _exportTemplateEditorCursorIndex = Mathf.Clamp(_exportTemplateEditorCursorIndex, 0, lines[Mathf.Clamp(_exportTemplateEditorActiveLineIndex, 0, lines.Count - 1)].Length);
                changed = true;
                i--;
            }
        }

        if (HandleExportTemplateEditorKeyboard(definition.TemplateId, lines))
        {
            changed = true;
        }

        GUILayout.Label(string.Empty, GUILayout.Height(4f));
        bool addLineClicked = GUILayout.Toggle(false, new GUIContent("ADD LINE"), GUI.skin.button, GUILayout.Width(Mathf.Min(120f, Mathf.Max(120f, scrollRect.width - 26f))));
        if (addLineClicked)
        {
            lines.Add(string.Empty);
            _exportTemplateEditorActiveTemplateId = definition.TemplateId;
            _exportTemplateEditorActiveLineIndex = lines.Count - 1;
            _exportTemplateEditorCursorIndex = 0;
            changed = true;
        }

        GUILayout.EndScrollView();
        GUILayout.EndArea();
        return changed
            ? string.Join(Environment.NewLine, lines.ToArray())
            : draftText ?? string.Empty;
    }

    private void EnsureExportTemplateEditorSelection(string templateId, List<string> lines)
    {
        if (!string.Equals(_exportTemplateEditorActiveTemplateId, templateId, StringComparison.Ordinal))
        {
            _exportTemplateEditorActiveTemplateId = templateId;
            _exportTemplateEditorActiveLineIndex = 0;
            _exportTemplateEditorCursorIndex = lines.Count > 0 ? lines[0].Length : 0;
            return;
        }

        if (lines.Count == 0)
        {
            _exportTemplateEditorActiveLineIndex = 0;
            _exportTemplateEditorCursorIndex = 0;
            return;
        }

        _exportTemplateEditorActiveLineIndex = Mathf.Clamp(_exportTemplateEditorActiveLineIndex, 0, lines.Count - 1);
        _exportTemplateEditorCursorIndex = Mathf.Clamp(_exportTemplateEditorCursorIndex, 0, lines[_exportTemplateEditorActiveLineIndex].Length);
    }

    private bool HandleExportTemplateEditorKeyboard(string templateId, List<string> lines)
    {
        if (!string.Equals(_exportTemplateEditorActiveTemplateId, templateId, StringComparison.Ordinal) ||
            lines.Count == 0)
        {
            return false;
        }

        Event? currentEvent = Event.current;
        if (currentEvent == null || currentEvent.type != EventType.KeyDown)
        {
            return false;
        }

        int activeLineIndex = Mathf.Clamp(_exportTemplateEditorActiveLineIndex, 0, lines.Count - 1);
        string activeLine = lines[activeLineIndex] ?? string.Empty;
        int cursorIndex = Mathf.Clamp(_exportTemplateEditorCursorIndex, 0, activeLine.Length);
        bool handled = false;

        switch (currentEvent.keyCode)
        {
            case KeyCode.LeftArrow:
                if (cursorIndex > 0)
                {
                    cursorIndex--;
                }
                else if (activeLineIndex > 0)
                {
                    activeLineIndex--;
                    cursorIndex = lines[activeLineIndex].Length;
                }
                handled = true;
                break;
            case KeyCode.RightArrow:
                if (cursorIndex < activeLine.Length)
                {
                    cursorIndex++;
                }
                else if (activeLineIndex < lines.Count - 1)
                {
                    activeLineIndex++;
                    cursorIndex = 0;
                }
                handled = true;
                break;
            case KeyCode.UpArrow:
                if (activeLineIndex > 0)
                {
                    activeLineIndex--;
                    cursorIndex = Mathf.Clamp(cursorIndex, 0, lines[activeLineIndex].Length);
                }
                handled = true;
                break;
            case KeyCode.DownArrow:
                if (activeLineIndex < lines.Count - 1)
                {
                    activeLineIndex++;
                    cursorIndex = Mathf.Clamp(cursorIndex, 0, lines[activeLineIndex].Length);
                }
                handled = true;
                break;
            case KeyCode.Home:
                cursorIndex = 0;
                handled = true;
                break;
            case KeyCode.End:
                cursorIndex = activeLine.Length;
                handled = true;
                break;
            case KeyCode.Return:
            case KeyCode.KeypadEnter:
                string trailingText = activeLine.Substring(cursorIndex);
                lines[activeLineIndex] = activeLine.Substring(0, cursorIndex);
                lines.Insert(activeLineIndex + 1, trailingText);
                activeLineIndex++;
                cursorIndex = 0;
                handled = true;
                break;
            case KeyCode.Backspace:
                if (cursorIndex > 0)
                {
                    lines[activeLineIndex] = activeLine.Remove(cursorIndex - 1, 1);
                    cursorIndex--;
                }
                else if (activeLineIndex > 0)
                {
                    int previousLength = lines[activeLineIndex - 1].Length;
                    lines[activeLineIndex - 1] = lines[activeLineIndex - 1] + activeLine;
                    lines.RemoveAt(activeLineIndex);
                    activeLineIndex--;
                    cursorIndex = previousLength;
                }
                handled = true;
                break;
            case KeyCode.Delete:
                if (cursorIndex < activeLine.Length)
                {
                    lines[activeLineIndex] = activeLine.Remove(cursorIndex, 1);
                }
                else if (activeLineIndex < lines.Count - 1)
                {
                    lines[activeLineIndex] = activeLine + lines[activeLineIndex + 1];
                    lines.RemoveAt(activeLineIndex + 1);
                }
                handled = true;
                break;
            case KeyCode.Tab:
                lines[activeLineIndex] = activeLine.Insert(cursorIndex, "    ");
                cursorIndex += 4;
                handled = true;
                break;
        }

        if (!handled && !char.IsControl(currentEvent.character))
        {
            lines[activeLineIndex] = activeLine.Insert(cursorIndex, currentEvent.character.ToString());
            cursorIndex++;
            handled = true;
        }

        if (!handled)
        {
            return false;
        }

        _exportTemplateEditorActiveLineIndex = Mathf.Clamp(activeLineIndex, 0, lines.Count - 1);
        _exportTemplateEditorCursorIndex = Mathf.Clamp(cursorIndex, 0, lines[_exportTemplateEditorActiveLineIndex].Length);
        currentEvent.Use();
        return true;
    }

    private static string BuildTemplateEditorDisplayLine(string line, int cursorIndex)
    {
        string safeLine = line ?? string.Empty;
        int clampedCursor = Mathf.Clamp(cursorIndex, 0, safeLine.Length);
        return safeLine.Substring(0, clampedCursor) + "|" + safeLine.Substring(clampedCursor);
    }

    private void RenderExportTemplateHelp(Rect helpRect, ExportTemplateDefinition definition, GUIStyle sectionHeaderStyle, GUIStyle helpStyle)
    {
        Rect titleRect = new Rect(helpRect.x + 10f, helpRect.y + 8f, helpRect.width - 20f, 20f);
        Rect scrollRect = new Rect(helpRect.x + 8f, helpRect.y + 30f, Mathf.Max(0f, helpRect.width - 16f), Mathf.Max(0f, helpRect.height - 38f));

        GUI.Label(titleRect, "Tokens & Syntax", sectionHeaderStyle);

        List<string> helpLines = new();
        helpLines.Add("Allowed tokens");
        foreach (string token in definition.AllowedTokens)
        {
            helpLines.Add("{{" + token + "}}");
        }

        helpLines.Add(string.Empty);
        helpLines.Add("Conditional lines");
        helpLines.Add("[[if token_name]]Only show this line when token_name is true or has a value");
        helpLines.Add("[[ifnot token_name]]Only show this line when token_name is false, empty, or missing");
        helpLines.Add(string.Empty);
        helpLines.Add("Notes");
        helpLines.Add("Conditionals only apply to the current line.");
        helpLines.Add("Unknown tokens or bad directives block SAVE.");
        helpLines.Add("Empty templates are allowed and export an empty file.");
        string helpText = string.Join(Environment.NewLine, helpLines.ToArray());

        GUILayout.BeginArea(scrollRect);
        _exportTemplateEditorTokenScroll = GUILayout.BeginScrollView(
            _exportTemplateEditorTokenScroll,
            GUILayout.Width(scrollRect.width),
            GUILayout.Height(scrollRect.height));
        GUILayout.Label(helpText, helpStyle, GUILayout.Width(Mathf.Max(0f, scrollRect.width - 26f)));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void RenderExportTemplatePreview(Rect previewRect, string statusText, string previewText, GUIStyle sectionHeaderStyle, GUIStyle statusStyle, GUIStyle previewStyle)
    {
        Rect titleRect = new Rect(previewRect.x + 10f, previewRect.y + 8f, previewRect.width - 20f, 20f);
        Rect statusRect = new Rect(previewRect.x + 10f, previewRect.y + 30f, previewRect.width - 20f, 34f);
        Rect scrollRect = new Rect(previewRect.x + 8f, previewRect.y + 66f, Mathf.Max(0f, previewRect.width - 16f), Mathf.Max(0f, previewRect.height - 74f));

        GUI.Label(titleRect, "Preview", sectionHeaderStyle);
        GUI.Label(statusRect, statusText, statusStyle);

        string previewValue = string.IsNullOrEmpty(previewText) ? "(empty output)" : previewText;
        GUILayout.BeginArea(scrollRect);
        _exportTemplateEditorPreviewScroll = GUILayout.BeginScrollView(
            _exportTemplateEditorPreviewScroll,
            GUILayout.Width(scrollRect.width),
            GUILayout.Height(scrollRect.height));
        GUILayout.Label(previewValue, previewStyle, GUILayout.Width(Mathf.Max(0f, scrollRect.width - 26f)));
        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }

    private void EnsureSelectedExportTemplateId()
    {
        string? selectedTemplateId = _selectedExportTemplateId;
        if (selectedTemplateId != null &&
            selectedTemplateId.Length > 0 &&
            ExportTemplateCatalog.TryGet(selectedTemplateId) != null)
        {
            return;
        }

        _selectedExportTemplateId = ExportTemplateCatalog.All.FirstOrDefault()?.TemplateId;
    }

    private ExportTemplateDefinition? GetSelectedExportTemplateDefinition()
    {
        EnsureSelectedExportTemplateId();
        string? selectedTemplateId = _selectedExportTemplateId;
        if (selectedTemplateId == null || selectedTemplateId.Length == 0)
        {
            return null;
        }

        return ExportTemplateCatalog.TryGet(selectedTemplateId);
    }

    private string GetExportTemplateDraftText(ExportTemplateDefinition definition)
    {
        return _exportTemplateEditorDrafts.TryGetValue(definition.TemplateId, out string? draft)
            ? draft
            : GetEffectiveExportTemplateSource(definition);
    }

    private void SetExportTemplateDraftText(string templateId, string draftText)
    {
        _exportTemplateEditorDrafts[templateId] = draftText ?? string.Empty;
    }

    private void RevertExportTemplateDraft(string templateId)
    {
        _exportTemplateEditorDrafts.Remove(templateId);
    }

    private void SaveExportTemplateDraft(ExportTemplateDefinition definition, string draftText)
    {
        Dictionary<string, string> overrides = EnsureExportTemplateOverrides();
        string normalizedDraft = draftText ?? string.Empty;
        if (string.Equals(normalizedDraft, definition.DefaultTemplate, StringComparison.Ordinal))
        {
            overrides.Remove(definition.TemplateId);
        }
        else
        {
            overrides[definition.TemplateId] = normalizedDraft;
        }

        _exportTemplateEditorDrafts.Remove(definition.TemplateId);
        MarkConfigDirty(affectsGlobalSnapshot: true, affectsExportTemplates: true);
        RequestImmediateExportRefresh(includeStateExport: false, includeObsExport: true);
    }

    private void ResetExportTemplateOverride(ExportTemplateDefinition definition)
    {
        bool removed = EnsureExportTemplateOverrides().Remove(definition.TemplateId);
        _exportTemplateEditorDrafts.Remove(definition.TemplateId);
        if (removed)
        {
            MarkConfigDirty(affectsGlobalSnapshot: true, affectsExportTemplates: true);
            RequestImmediateExportRefresh(includeStateExport: false, includeObsExport: true);
        }
    }

    private bool TryBuildExportTemplatePreview(ExportTemplateDefinition definition, CompiledExportTemplate compiledTemplate, TrackerState state, out string? previewText, out string? unavailableMessage)
    {
        if (!TryCreateExportTemplatePreviewContext(definition, state, out ExportTemplateRenderContext? previewContext, out unavailableMessage) ||
            previewContext == null)
        {
            previewText = null;
            return false;
        }

        previewText = ExportTemplateEngine.Render(compiledTemplate, previewContext);
        return true;
    }

    private bool TryCreateExportTemplatePreviewContext(ExportTemplateDefinition definition, TrackerState state, out ExportTemplateRenderContext? context, out string? unavailableMessage)
    {
        switch (definition.PreviewContextKind)
        {
            case ExportTemplatePreviewContextKind.Metric:
                string metricLabel;
                string metricValue;
                switch (definition.TemplateId)
                {
                    case "metric.current_section":
                        metricLabel = "Current Section";
                        metricValue = state.CurrentSectionStats != null
                            ? BuildSectionExportName(state.SectionStats, state.CurrentSectionStats)
                            : (state.CurrentSection ?? string.Empty);
                        break;
                    case "metric.streak":
                        metricLabel = "Current Streak";
                        metricValue = state.Streak.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.best_streak":
                        metricLabel = "Best FC Streak";
                        metricValue = state.BestStreak.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.attempts":
                        metricLabel = "Total Attempts";
                        metricValue = state.Attempts.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.current_ghosted_notes":
                        metricLabel = "Current Ghosted Notes";
                        metricValue = state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.current_overstrums":
                        metricLabel = "Current Overstrums";
                        metricValue = state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.current_missed_notes":
                        metricLabel = "Current Missed Notes";
                        metricValue = state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.lifetime_ghosted_notes":
                        metricLabel = "Song Lifetime Ghosted Notes";
                        metricValue = state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.global_lifetime_ghosted_notes":
                        metricLabel = "Global Lifetime Ghosted Notes";
                        metricValue = state.GlobalLifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "metric.fc_achieved":
                        metricLabel = "FC Achieved";
                        metricValue = state.FcAchieved ? "True" : "False";
                        break;
                    default:
                        context = null;
                        unavailableMessage = "Preview unavailable for that metric template.";
                        return false;
                }

                context = CreateMetricTemplateContext(state, metricLabel, metricValue);
                unavailableMessage = null;
                return true;

            case ExportTemplatePreviewContextKind.Section:
                SectionStatsState? previewSection = state.CurrentSectionStats ?? state.SectionStats.FirstOrDefault();
                if (previewSection == null)
                {
                    context = null;
                    unavailableMessage = "Preview unavailable until a song section is available.";
                    return false;
                }

                string sectionExportName = state.SectionStats.Count > 0
                    ? BuildSectionExportName(state.SectionStats, previewSection)
                    : previewSection.Name;
                string sectionLabel;
                string sectionValue;
                switch (definition.TemplateId)
                {
                    case "section.current_summary":
                        sectionLabel = "Current Section Summary";
                        sectionValue = string.Empty;
                        break;
                    case "section.name":
                        sectionLabel = "Section Name";
                        sectionValue = sectionExportName;
                        break;
                    case "section.summary":
                        sectionLabel = "Section Summary";
                        sectionValue = string.Empty;
                        break;
                    case "section.attempts":
                        sectionLabel = "Attempts";
                        sectionValue = previewSection.Attempts.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "section.fcs_past":
                        sectionLabel = "FCs Past";
                        sectionValue = previewSection.RunsPast.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "section.killed_the_run":
                        sectionLabel = "Killed the Run";
                        sectionValue = previewSection.KilledTheRun.ToString(CultureInfo.InvariantCulture);
                        break;
                    default:
                        sectionLabel = definition.Label;
                        sectionValue = string.Empty;
                        break;
                }

                context = CreateSectionTemplateContext(state, previewSection, sectionExportName, sectionLabel, sectionValue);
                unavailableMessage = null;
                return true;

            case ExportTemplatePreviewContextKind.Run:
                CompletedRunRecord? previewRun = state.CompletedRuns.Count > 0
                    ? state.CompletedRuns[state.CompletedRuns.Count - 1]
                    : null;
                if (previewRun == null)
                {
                    context = null;
                    unavailableMessage = "Preview unavailable until a completed run exists for the current song.";
                    return false;
                }

                string runLabel;
                string runValue;
                switch (definition.TemplateId)
                {
                    case "run.completed_at_utc":
                        runLabel = "Completed At UTC";
                        runValue = previewRun.CompletedAtUtc ?? string.Empty;
                        break;
                    case "run.percent":
                        runLabel = "Percent";
                        runValue = previewRun.Percent.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.score":
                        runLabel = "Score";
                        runValue = previewRun.Score.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.best_streak":
                        runLabel = "Best Streak";
                        runValue = previewRun.BestStreak.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.first_miss_streak":
                        runLabel = "First Miss Streak";
                        runValue = previewRun.FirstMissStreak.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.ghosted_notes":
                        runLabel = "Ghosted Notes";
                        runValue = previewRun.GhostedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.overstrums":
                        runLabel = "Overstrums";
                        runValue = previewRun.Overstrums.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.missed_notes":
                        runLabel = "Missed Notes";
                        runValue = previewRun.MissedNotes.ToString(CultureInfo.InvariantCulture);
                        break;
                    case "run.fc_achieved":
                        runLabel = "FC Achieved";
                        runValue = previewRun.FcAchieved ? "True" : "False";
                        break;
                    case "run.final_section":
                        runLabel = "Final Section";
                        runValue = previewRun.FinalSection ?? string.Empty;
                        break;
                    case "run.summary":
                        runLabel = "Run Summary";
                        runValue = string.Empty;
                        break;
                    default:
                        runLabel = definition.Label;
                        runValue = string.Empty;
                        break;
                }

                context = CreateRunTemplateContext(state, previewRun, runLabel, runValue);
                unavailableMessage = null;
                return true;

            default:
                context = null;
                unavailableMessage = "Preview unavailable for this template.";
                return false;
        }
    }

    private void RenderOverlayEditorTransparencyControls(OverlayEditorConfig overlayConfig, float width)
    {
        RenderAnimatedMenuSliderCard(
            "Overlay Transparency",
            width,
            Mathf.Clamp01(overlayConfig.BackgroundA),
            value => overlayConfig.BackgroundA = value,
            "Controls the editor panel background opacity.",
            0.15f,
            1f,
            false);
    }

    private void RenderAnimatedMenuTintControls(float width)
    {
        GUILayout.Label("Animated Menu Layers", GUILayout.Width(width));

        RenderAnimatedMenuSliderCard(
            "Background Tint Strength",
            width,
            _config.AnimatedMenuTintBackgroundOverlayStrength,
            value => _config.AnimatedMenuTintBackgroundOverlayStrength = value,
            null);

        RenderAnimatedMenuSliderCard(
            "Foreground Glow Strength",
            width,
            _config.AnimatedMenuTintCanvasOverlayStrength,
            value => _config.AnimatedMenuTintCanvasOverlayStrength = value,
            null);

        float wispSize = Mathf.Clamp01(_config.AnimatedMenuWispSize);
        RenderAnimatedMenuSliderCard(
            "Wisp Size",
            width,
            wispSize,
            value => _config.AnimatedMenuWispSize = value,
            null);
    }

    private void RenderAnimatedMenuSliderCard(string label, float width, float value, Action<float> applyValue, string? description, float min = 0f, float max = 1f, bool applyAnimatedMenuTint = true)
    {
        GUIStyle titleStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };
        titleStyle.normal.textColor = new Color(0.93f, 0.97f, 1f, 0.98f);

        GUIStyle valueStyle = new GUIStyle(GUI.skin.label)
        {
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleCenter
        };
        valueStyle.normal.textColor = Color.white;

        GUIStyle helpStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = Mathf.Max(10, GUI.skin.label.fontSize - 2)
        };
        helpStyle.normal.textColor = new Color(0.74f, 0.83f, 0.92f, 0.92f);
        float clampedValue = Mathf.Clamp(value, min, max);
        bool hasDescription = !string.IsNullOrWhiteSpace(description);
        float cardHeight = hasDescription ? 90f : 72f;
        Rect cardRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Width(width), GUILayout.Height(cardHeight));
        GUI.Label(cardRect, new GUIContent(string.Empty, EnsureOverlaySliderCardTexture(GetOverlayTexturePixelSize(cardRect.width), GetOverlayTexturePixelSize(cardRect.height)), string.Empty), GUIStyle.none);

        float contentX = cardRect.x + 12f;
        float contentWidth = Mathf.Max(0f, cardRect.width - 24f);
        float valueWidth = 62f;
        float gap = 8f;
        float labelWidth = Mathf.Max(0f, contentWidth - valueWidth - gap);
        Rect titleRect = new Rect(contentX, cardRect.y + 10f, labelWidth, 26f);
        Rect valueRect = new Rect(contentX + labelWidth + gap, cardRect.y + 10f, valueWidth, 26f);
        Rect descriptionRect = new Rect(contentX, cardRect.y + 40f, contentWidth, 16f);
        float sliderWellY = hasDescription ? cardRect.y + 58f : cardRect.y + 38f;
        Rect sliderWellRect = new Rect(contentX, sliderWellY, contentWidth, 20f);
        Rect sliderRect = new Rect(sliderWellRect.x + 8f, sliderWellRect.y + 1f, Mathf.Max(0f, sliderWellRect.width - 16f), 18f);

        GUI.Label(titleRect, new GUIContent(string.Empty, EnsureOverlaySliderTitleTexture(GetOverlayTexturePixelSize(titleRect.width), GetOverlayTexturePixelSize(titleRect.height)), string.Empty), GUIStyle.none);
        GUI.Label(new Rect(titleRect.x + 10f, titleRect.y, Mathf.Max(0f, titleRect.width - 20f), titleRect.height), label, titleStyle);
        if (hasDescription)
        {
            GUI.Label(descriptionRect, description!, helpStyle);
        }
        GUI.Label(sliderWellRect, new GUIContent(string.Empty, EnsureOverlaySliderWellTexture(GetOverlayTexturePixelSize(sliderWellRect.width), GetOverlayTexturePixelSize(sliderWellRect.height)), string.Empty), GUIStyle.none);

        float updatedValue = RenderOverlayEditorTransparencySlider("animated-menu:" + label, sliderRect, clampedValue, min, max);
        GUI.Label(valueRect, new GUIContent(string.Empty, EnsureOverlaySliderValueTexture(GetOverlayTexturePixelSize(valueRect.width), GetOverlayTexturePixelSize(valueRect.height)), string.Empty), GUIStyle.none);
        GUI.Label(valueRect, $"{Mathf.RoundToInt(updatedValue * 100f)}%", valueStyle);
        if (Math.Abs(updatedValue - clampedValue) >= 0.001f)
        {
            applyValue(updatedValue);
            MarkConfigDirty(affectsGlobalSnapshot: true);
            if (applyAnimatedMenuTint)
            {
                ApplyAnimatedMenuTint();
            }
        }

        GUILayout.Label(string.Empty, GUILayout.Height(8f));
    }

    private float RenderOverlayEditorTransparencySlider(string sliderKey, float value, float width, float min, float max)
    {
        Rect sliderRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Width(width), GUILayout.Height(24f));
        sliderRect.width = width;
        sliderRect.height = 24f;
        return RenderOverlayEditorTransparencySlider(sliderKey, sliderRect, value, min, max);
    }

    private float RenderOverlayEditorTransparencySlider(string sliderKey, Rect sliderRect, float value, float min, float max)
    {
        sliderRect.height = 18f;

        if (_overlayEditorActiveSliderKey != null && !Input.GetMouseButton(0))
        {
            _overlayEditorActiveSliderKey = null;
        }

        Event? currentEvent = Event.current;
        if (currentEvent != null)
        {
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(currentEvent.mousePosition))
                    {
                        _overlayEditorActiveSliderKey = sliderKey;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (string.Equals(_overlayEditorActiveSliderKey, sliderKey, StringComparison.Ordinal))
                    {
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (string.Equals(_overlayEditorActiveSliderKey, sliderKey, StringComparison.Ordinal))
                    {
                        _overlayEditorActiveSliderKey = null;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        if (string.Equals(_overlayEditorActiveSliderKey, sliderKey, StringComparison.Ordinal) && currentEvent != null)
        {
            float normalized = Mathf.InverseLerp(sliderRect.x, sliderRect.xMax, currentEvent.mousePosition.x);
            value = Mathf.Lerp(min, max, Mathf.Clamp01(normalized));
        }

        Rect trackRect = new Rect(sliderRect.x + 1f, sliderRect.y + 3f, Mathf.Max(0f, sliderRect.width - 2f), 12f);
        GUI.Label(trackRect, new GUIContent(string.Empty, EnsureOverlaySliderTrackTexture(GetOverlayTexturePixelSize(trackRect.width), GetOverlayTexturePixelSize(trackRect.height)), string.Empty), GUIStyle.none);

        float fillWidth = Mathf.Lerp(0f, Mathf.Max(0f, trackRect.width), Mathf.InverseLerp(min, max, value));
        if (fillWidth > 0f)
        {
            Rect fillRect = new Rect(trackRect.x, trackRect.y, fillWidth, trackRect.height);
            GUI.Label(fillRect, new GUIContent(string.Empty, EnsureOverlaySliderFillTexture(GetOverlayTexturePixelSize(fillRect.width), GetOverlayTexturePixelSize(fillRect.height)), string.Empty), GUIStyle.none);
        }

        float knobCenterX = Mathf.Lerp(trackRect.x, trackRect.xMax, Mathf.InverseLerp(min, max, value));
        Rect knobRect = new Rect(knobCenterX - 12f, sliderRect.y - 3f, 24f, 24f);
        GUI.Label(knobRect, new GUIContent(string.Empty, EnsureOverlaySliderKnobTexture(), string.Empty), GUIStyle.none);

        return Mathf.Clamp(value, min, max);
    }

    private Rect RenderOverlayPanel(string panelKey, Rect rect, string title, bool resizeHandleVisible, Action<bool>? setResizeHandleVisible, Action<Rect> renderContent)
    {
        const float headerHeight = 28f;
        const float padding = 8f;
        const float resizeHandleSize = 14f;

        rect = ClampOverlayRect(rect);
        Rect dragRect = new Rect(rect.x, rect.y, rect.width, headerHeight);
        Rect resizeRect = new Rect(
            rect.x + Mathf.Max(0f, rect.width - resizeHandleSize),
            rect.y + Mathf.Max(0f, rect.height - resizeHandleSize),
            resizeHandleSize,
            resizeHandleSize);
        HandleOverlayPanelInteraction(rect, resizeHandleVisible, setResizeHandleVisible);
        HandleOverlayDrag(panelKey, ref rect, dragRect);
        if (resizeHandleVisible)
        {
            HandleOverlayResize(panelKey, ref rect, resizeRect, GetOverlayMinimumSize(panelKey));
        }

        float panelAlpha = string.Equals(panelKey, "editor", StringComparison.Ordinal)
            ? Mathf.Clamp01(EnsureOverlayEditorConfig().BackgroundA)
            : 1f;
        if (string.Equals(panelKey, "editor", StringComparison.Ordinal))
        {
            Texture2D panelTexture = EnsureOverlayEditorPanelTexture(rect, panelAlpha);
            GUI.Label(rect, new GUIContent(string.Empty, panelTexture, string.Empty), GUI.skin.label);
        }
        else if (string.Equals(panelKey, "export-templates", StringComparison.Ordinal))
        {
            Texture2D panelTexture = EnsureOverlayExportTemplatePanelTexture(rect);
            GUI.Label(rect, new GUIContent(string.Empty, panelTexture, string.Empty), GUI.skin.label);
        }
        else
        {
            GUI.Box(rect, GUIContent.none, GUI.skin.box);
        }
        GUI.Label(new Rect(rect.x + 8f, rect.y + 1f, Mathf.Max(0f, rect.width - 16f), headerHeight - 2f), title, GUI.skin.label);

        Rect contentRect = new Rect(
            rect.x + padding,
            rect.y + headerHeight + padding,
            Mathf.Max(0f, rect.width - (padding * 2f)),
            Mathf.Max(0f, rect.height - headerHeight - (padding * 2f)));

        renderContent(contentRect);
        if (resizeHandleVisible)
        {
            resizeRect = new Rect(
                rect.x + Mathf.Max(0f, rect.width - resizeHandleSize),
                rect.y + Mathf.Max(0f, rect.height - resizeHandleSize),
                resizeHandleSize,
                resizeHandleSize);
            DrawResizeCornerIndicator(resizeRect, Color.white);
        }

        return rect;
    }

    private Texture2D EnsureOverlayEditorPanelTexture(Rect rect, float alpha)
    {
        alpha = Mathf.Clamp01(alpha);
        int width = Math.Max(1, Mathf.RoundToInt(rect.width));
        int height = Math.Max(1, Mathf.RoundToInt(rect.height));
        if (_overlayEditorPanelTexture != null &&
            _overlayEditorPanelTextureWidth == width &&
            _overlayEditorPanelTextureHeight == height &&
            Math.Abs(_overlayEditorPanelTextureAlpha - alpha) < 0.001f)
        {
            return _overlayEditorPanelTexture;
        }

        _overlayEditorPanelTextureAlpha = alpha;
        _overlayEditorPanelTextureWidth = width;
        _overlayEditorPanelTextureHeight = height;
        _overlayEditorPanelTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color fillColor = new Color(0.02f, 0.07f, 0.1f, Mathf.Lerp(0.14f, 0.86f, alpha));
        Color headerColor = new Color(0.03f, 0.09f, 0.13f, Mathf.Lerp(0.2f, 0.92f, alpha));
        Color borderColor = new Color(0.12f, 0.22f, 0.28f, Mathf.Lerp(0.5f, 0.95f, alpha));
        Color dividerColor = new Color(0.08f, 0.16f, 0.21f, Mathf.Lerp(0.42f, 0.85f, alpha));
        Color[] pixels = new Color[width * height];
        int headerHeight = Math.Min(28, height);
        int radius = Math.Min(10, Math.Min(width, height) / 6);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                if (IsOutsideRoundedCorner(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                bool isBorder = IsRoundedBorderPixel(x, y, width, height, radius);
                bool isDivider = y == headerHeight;
                if (isBorder)
                {
                    pixels[pixelIndex] = borderColor;
                }
                else if (isDivider)
                {
                    pixels[pixelIndex] = dividerColor;
                }
                else if (y < headerHeight)
                {
                    pixels[pixelIndex] = headerColor;
                }
                else
                {
                    pixels[pixelIndex] = fillColor;
                }
            }
        }
        _overlayEditorPanelTexture.SetPixels(0, 0, width, height, pixels);
        _overlayEditorPanelTexture.Apply();
        return _overlayEditorPanelTexture;
    }

    private Texture2D EnsureOverlayExportTemplatePanelTexture(Rect rect)
    {
        int width = Math.Max(1, Mathf.RoundToInt(rect.width));
        int height = Math.Max(1, Mathf.RoundToInt(rect.height));
        if (_overlayExportTemplatePanelTexture != null &&
            _overlayExportTemplatePanelTextureWidth == width &&
            _overlayExportTemplatePanelTextureHeight == height)
        {
            return _overlayExportTemplatePanelTexture;
        }

        _overlayExportTemplatePanelTextureWidth = width;
        _overlayExportTemplatePanelTextureHeight = height;
        _overlayExportTemplatePanelTexture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color fillColor = new Color(0f, 0f, 0f, 0.96f);
        Color headerColor = new Color(0.02f, 0.02f, 0.02f, 0.98f);
        Color borderColor = new Color(0.14f, 0.27f, 0.42f, 0.96f);
        Color dividerColor = new Color(0.08f, 0.18f, 0.28f, 0.92f);
        Color[] pixels = new Color[width * height];
        int headerHeight = Math.Min(28, height);
        int radius = Math.Min(10, Math.Min(width, height) / 6);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                if (IsOutsideRoundedCorner(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                bool isBorder = IsRoundedBorderPixel(x, y, width, height, radius);
                bool isHeaderDivider = y == height - headerHeight - 1;
                if (isBorder)
                {
                    pixels[pixelIndex] = borderColor;
                }
                else if (isHeaderDivider)
                {
                    pixels[pixelIndex] = dividerColor;
                }
                else if (y >= height - headerHeight)
                {
                    pixels[pixelIndex] = headerColor;
                }
                else
                {
                    pixels[pixelIndex] = fillColor;
                }
            }
        }

        _overlayExportTemplatePanelTexture.SetPixels(0, 0, width, height, pixels);
        _overlayExportTemplatePanelTexture.filterMode = FilterMode.Bilinear;
        _overlayExportTemplatePanelTexture.wrapMode = TextureWrapMode.Clamp;
        _overlayExportTemplatePanelTexture.Apply();
        return _overlayExportTemplatePanelTexture;
    }

    private Texture2D EnsureOverlaySliderTrackTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderTrackTexture == null
            || _overlaySliderTrackTextureWidth != width
            || _overlaySliderTrackTextureHeight != height)
        {
            _overlaySliderTrackTexture = BuildRoundedRectTexture(
                width,
                height,
                Math.Max(1, height / 2),
                new Color(0.03f, 0.07f, 0.13f, 0.98f),
                new Color(0.17f, 0.27f, 0.42f, 0.95f));
            _overlaySliderTrackTextureWidth = width;
            _overlaySliderTrackTextureHeight = height;
        }

        return _overlaySliderTrackTexture;
    }

    private Texture2D EnsureOverlaySliderFillTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderFillTexture == null
            || _overlaySliderFillTextureWidth != width
            || _overlaySliderFillTextureHeight != height)
        {
            _overlaySliderFillTexture = BuildHorizontalGradientRoundedTexture(
                width,
                height,
                Math.Max(1, height / 2),
                new Color(0.04f, 0.36f, 0.88f, 0.99f),
                new Color(0.16f, 0.63f, 1f, 0.99f),
                new Color(0.44f, 0.82f, 1f, 0.94f));
            _overlaySliderFillTextureWidth = width;
            _overlaySliderFillTextureHeight = height;
        }

        return _overlaySliderFillTexture;
    }

    private Texture2D EnsureOverlaySliderKnobTexture()
    {
        if (_overlaySliderKnobTexture == null)
        {
            const int width = 24;
            const int height = 24;
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[width * height];
            Vector2 center = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);
            float shadowRadius = (width - 1) * 0.5f;
            float radius = (width - 5) * 0.5f;
            Color fillColor = new Color(0.14f, 0.54f, 0.98f, 0.99f);
            Color highlightColor = new Color(0.57f, 0.87f, 1f, 0.99f);
            Color edgeColor = new Color(0.03f, 0.18f, 0.43f, 0.98f);
            Color shadowColor = new Color(0f, 0f, 0f, 0.24f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = x - center.x;
                    float dy = y - center.y;
                    float distance = Mathf.Sqrt((dx * dx) + (dy * dy));
                    int pixelIndex = (y * width) + x;
                    if (distance > shadowRadius)
                    {
                        pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                    }
                    else if (distance > radius + 1.5f)
                    {
                        float shadowAlpha = Mathf.InverseLerp(shadowRadius, radius + 1.5f, distance) * shadowColor.a;
                        pixels[pixelIndex] = new Color(shadowColor.r, shadowColor.g, shadowColor.b, shadowAlpha);
                    }
                    else if (distance > radius - 1f)
                    {
                        pixels[pixelIndex] = edgeColor;
                    }
                    else
                    {
                        float highlight = Mathf.Clamp01((center.y - y) / Math.Max(1f, radius));
                        pixels[pixelIndex] = Color.Lerp(fillColor, highlightColor, highlight * 0.45f);
                    }
                }
            }

            texture.SetPixels(0, 0, width, height, pixels);
            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.Apply();
            _overlaySliderKnobTexture = texture;
        }

        return _overlaySliderKnobTexture;
    }

    private Texture2D EnsureOverlaySliderCardTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderCardTexture == null
            || _overlaySliderCardTextureWidth != width
            || _overlaySliderCardTextureHeight != height)
        {
            _overlaySliderCardTexture = BuildVerticalGradientRoundedTexture(
                width,
                height,
                Math.Max(6, Math.Min(width, height) / 7),
                new Color(0.03f, 0.08f, 0.14f, 0.86f),
                new Color(0.02f, 0.06f, 0.11f, 0.82f),
                new Color(0.16f, 0.29f, 0.45f, 0.95f));
            _overlaySliderCardTextureWidth = width;
            _overlaySliderCardTextureHeight = height;
        }

        return _overlaySliderCardTexture;
    }

    private Texture2D EnsureOverlaySliderWellTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderWellTexture == null
            || _overlaySliderWellTextureWidth != width
            || _overlaySliderWellTextureHeight != height)
        {
            _overlaySliderWellTexture = BuildRoundedRectTexture(
                width,
                height,
                Math.Max(1, height / 2),
                new Color(0.02f, 0.05f, 0.1f, 0.98f),
                new Color(0.14f, 0.24f, 0.39f, 0.95f));
            _overlaySliderWellTextureWidth = width;
            _overlaySliderWellTextureHeight = height;
        }

        return _overlaySliderWellTexture;
    }

    private Texture2D EnsureOverlaySliderTitleTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderTitleTexture == null
            || _overlaySliderTitleTextureWidth != width
            || _overlaySliderTitleTextureHeight != height)
        {
            _overlaySliderTitleTexture = BuildRoundedRectTexture(
                width,
                height,
                Math.Max(6, Math.Min(width, height) / 3),
                new Color(0.06f, 0.12f, 0.2f, 0.92f),
                new Color(0.18f, 0.31f, 0.47f, 0.94f));
            _overlaySliderTitleTextureWidth = width;
            _overlaySliderTitleTextureHeight = height;
        }

        return _overlaySliderTitleTexture;
    }

    private Texture2D EnsureOverlaySliderValueTexture(int width, int height)
    {
        width = Math.Max(1, width);
        height = Math.Max(1, height);
        if (_overlaySliderValueTexture == null
            || _overlaySliderValueTextureWidth != width
            || _overlaySliderValueTextureHeight != height)
        {
            _overlaySliderValueTexture = BuildRoundedRectTexture(
                width,
                height,
                Math.Max(6, Math.Min(width, height) / 3),
                new Color(0.07f, 0.29f, 0.62f, 0.97f),
                new Color(0.34f, 0.67f, 0.98f, 0.97f));
            _overlaySliderValueTextureWidth = width;
            _overlaySliderValueTextureHeight = height;
        }

        return _overlaySliderValueTexture;
    }

    private Texture2D EnsureOverlaySliderDividerTexture()
    {
        if (_overlaySliderDividerTexture == null)
        {
            _overlaySliderDividerTexture = BuildSolidTexture(new Color(0.16f, 0.33f, 0.52f, 0.38f));
        }

        return _overlaySliderDividerTexture;
    }

    private static int GetOverlayTexturePixelSize(float guiSize)
    {
        float scale = 1f;
        if (GuiPixelsPerPointProperty != null)
        {
            try
            {
                object? value = GuiPixelsPerPointProperty.GetValue(null, null);
                if (value is float floatValue && floatValue > 0.01f)
                {
                    scale = floatValue;
                }
            }
            catch
            {
            }
        }

        return Math.Max(1, Mathf.CeilToInt(guiSize * scale));
    }

    private static Texture2D BuildSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        texture.SetPixels(0, 0, 1, 1, new[] { color });
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply();
        return texture;
    }

    private static Texture2D BuildRoundedRectTexture(int width, int height, int radius, Color fillColor, Color borderColor)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                if (IsOutsideRoundedCorner(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                }
                else if (IsRoundedBorderPixel(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = borderColor;
                }
                else
                {
                    pixels[pixelIndex] = fillColor;
                }
            }
        }

        texture.SetPixels(0, 0, width, height, pixels);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply();
        return texture;
    }

    private static Texture2D BuildHorizontalGradientRoundedTexture(int width, int height, int radius, Color leftColor, Color rightColor, Color borderColor)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                if (IsOutsideRoundedCorner(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                }
                else if (IsRoundedBorderPixel(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = borderColor;
                }
                else
                {
                    float t = width <= 1 ? 0f : x / (float)(width - 1);
                    pixels[pixelIndex] = Color.Lerp(leftColor, rightColor, t);
                }
            }
        }

        texture.SetPixels(0, 0, width, height, pixels);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply();
        return texture;
    }

    private static Texture2D BuildVerticalGradientRoundedTexture(int width, int height, int radius, Color topColor, Color bottomColor, Color borderColor)
    {
        Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        Color[] pixels = new Color[width * height];
        for (int y = 0; y < height; y++)
        {
            float t = height <= 1 ? 0f : y / (float)(height - 1);
            Color rowColor = Color.Lerp(topColor, bottomColor, t);
            for (int x = 0; x < width; x++)
            {
                int pixelIndex = (y * width) + x;
                if (IsOutsideRoundedCorner(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                }
                else if (IsRoundedBorderPixel(x, y, width, height, radius))
                {
                    pixels[pixelIndex] = borderColor;
                }
                else
                {
                    pixels[pixelIndex] = rowColor;
                }
            }
        }

        texture.SetPixels(0, 0, width, height, pixels);
        texture.wrapMode = TextureWrapMode.Clamp;
        texture.filterMode = FilterMode.Bilinear;
        texture.Apply();
        return texture;
    }

    private Texture2D EnsureAnimatedMenuWispTexture()
    {
        if (_animatedMenuWispTexture != null)
        {
            return _animatedMenuWispTexture;
        }

        const int size = 256;
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false)
        {
            wrapMode = TextureWrapMode.Repeat,
            filterMode = FilterMode.Bilinear
        };

        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            float v = (y + 0.5f) / size;
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size;
                float alpha = EvaluateAnimatedMenuWispAlpha(u, v);
                pixels[(y * size) + x] = new Color(1f, 1f, 1f, alpha);
            }
        }

        texture.SetPixels(0, 0, size, size, pixels);
        texture.Apply();
        _animatedMenuWispTexture = texture;
        return texture;
    }

    private static float EvaluateAnimatedMenuWispAlpha(float u, float v)
    {
        float warpA = (FractalTileableNoise(u + 0.13f, v + 0.41f, 3, 3) - 0.5f) * 0.28f;
        float warpB = (FractalTileableNoise(u + 0.67f, v + 0.23f, 4, 3) - 0.5f) * 0.2f;
        float broadField = FractalTileableNoise((u + warpA) * 1.45f, (v + warpB) * 0.8f, 4, 5);
        float narrowField = FractalTileableNoise((u - warpB) * 3.6f, (v + warpA) * 1.9f, 5, 4);
        float ridgeField = 1f - Mathf.Abs(narrowField * 2f - 1f);

        float body = Mathf.Clamp01((broadField - 0.42f) / 0.28f);
        body = Mathf.Pow(body, 1.2f);
        float strands = Mathf.Clamp01((ridgeField - 0.34f) / 0.46f);
        strands = Mathf.Pow(strands, 1.55f);

        float wisps = Mathf.Clamp01((body * 0.75f) + (body * strands * 1.15f));
        wisps = Mathf.SmoothStep(0.02f, 0.94f, wisps);
        return Mathf.Clamp01(wisps);
    }

    private static float FractalTileableNoise(float u, float v, int basePeriod, int octaves)
    {
        float total = 0f;
        float amplitude = 1f;
        float amplitudeSum = 0f;
        int period = Math.Max(1, basePeriod);
        for (int octave = 0; octave < octaves; octave++)
        {
            total += TileableValueNoise(u * period, v * period, period) * amplitude;
            amplitudeSum += amplitude;
            amplitude *= 0.5f;
            period *= 2;
        }

        return amplitudeSum > 0.0001f ? total / amplitudeSum : 0f;
    }

    private static float TileableValueNoise(float x, float y, int period)
    {
        int x0 = Mathf.FloorToInt(x);
        int y0 = Mathf.FloorToInt(y);
        int x1 = x0 + 1;
        int y1 = y0 + 1;
        float tx = x - x0;
        float ty = y - y0;
        tx = tx * tx * (3f - (2f * tx));
        ty = ty * ty * (3f - (2f * ty));

        float v00 = HashToUnitFloat(PositiveMod(x0, period), PositiveMod(y0, period));
        float v10 = HashToUnitFloat(PositiveMod(x1, period), PositiveMod(y0, period));
        float v01 = HashToUnitFloat(PositiveMod(x0, period), PositiveMod(y1, period));
        float v11 = HashToUnitFloat(PositiveMod(x1, period), PositiveMod(y1, period));

        float a = Mathf.Lerp(v00, v10, tx);
        float b = Mathf.Lerp(v01, v11, tx);
        return Mathf.Lerp(a, b, ty);
    }

    private static int PositiveMod(int value, int divisor)
    {
        if (divisor <= 0)
        {
            return 0;
        }

        int result = value % divisor;
        return result < 0 ? result + divisor : result;
    }

    private static float HashToUnitFloat(int x, int y)
    {
        unchecked
        {
            uint hash = 2166136261u;
            hash = (hash ^ (uint)x) * 16777619u;
            hash = (hash ^ (uint)y) * 16777619u;
            hash += hash << 13;
            hash ^= hash >> 7;
            hash += hash << 3;
            hash ^= hash >> 17;
            hash += hash << 5;
            return (hash & 0x00FFFFFF) / 16777215f;
        }
    }

    private Texture2D EnsureAnimatedMenuOnGuiOverlayTexture(Color color)
    {
        if (_animatedMenuOnGuiOverlayTexture != null &&
            Mathf.Abs(_animatedMenuOnGuiOverlayTextureColor.r - color.r) < 0.001f &&
            Mathf.Abs(_animatedMenuOnGuiOverlayTextureColor.g - color.g) < 0.001f &&
            Mathf.Abs(_animatedMenuOnGuiOverlayTextureColor.b - color.b) < 0.001f &&
            Mathf.Abs(_animatedMenuOnGuiOverlayTextureColor.a - color.a) < 0.001f)
        {
            return _animatedMenuOnGuiOverlayTexture;
        }

        _animatedMenuOnGuiOverlayTextureColor = color;
        _animatedMenuOnGuiOverlayTexture = BuildSolidTexture(color);
        return _animatedMenuOnGuiOverlayTexture;
    }

    private void DrawResizeCornerIndicator(Rect rect, Color color)
    {
        GUI.Label(rect, new GUIContent(string.Empty, EnsureResizeCornerTexture(color), string.Empty), GUI.skin.label);
    }

    private Texture2D EnsureResizeCornerTexture(Color color)
    {
        string key = $"{Mathf.RoundToInt(color.r * 255f)}-{Mathf.RoundToInt(color.g * 255f)}-{Mathf.RoundToInt(color.b * 255f)}-{Mathf.RoundToInt(color.a * 255f)}";
        if (_resizeCornerTextureCache.TryGetValue(key, out Texture2D cached))
        {
            return cached;
        }

        const int size = 14;
        Texture2D texture = new Texture2D(size, size, TextureFormat.ARGB32, false);
        Color[] pixels = new Color[size * size];
        Color clear = new Color(0f, 0f, 0f, 0f);
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = clear;
        }

        Color edge = new Color(color.r, color.g, color.b, Math.Max(0.7f, color.a));
        for (int x = 4; x < size; x++)
        {
            pixels[((size - 2) * size) + x] = edge;
            pixels[((size - 3) * size) + x] = edge;
        }

        for (int y = 4; y < size; y++)
        {
            pixels[(y * size) + (size - 2)] = edge;
            pixels[(y * size) + (size - 3)] = edge;
        }

        texture.SetPixels(0, 0, size, size, pixels);
        texture.Apply();
        _resizeCornerTextureCache[key] = texture;
        return texture;
    }

    private static bool IsOutsideRoundedCorner(int x, int y, int width, int height, int radius)
    {
        if (radius <= 1)
        {
            return false;
        }

        if (x < radius && y < radius)
        {
            return DistanceSquared(x, y, radius - 1, radius - 1) > radius * radius;
        }

        if (x >= width - radius && y < radius)
        {
            return DistanceSquared(x, y, width - radius, radius - 1) > radius * radius;
        }

        if (x < radius && y >= height - radius)
        {
            return DistanceSquared(x, y, radius - 1, height - radius) > radius * radius;
        }

        if (x >= width - radius && y >= height - radius)
        {
            return DistanceSquared(x, y, width - radius, height - radius) > radius * radius;
        }

        return false;
    }

    private static bool IsRoundedBorderPixel(int x, int y, int width, int height, int radius)
    {
        bool basicBorder = x == 0 || y == 0 || x == width - 1 || y == height - 1;
        if (basicBorder)
        {
            return true;
        }

        if (radius <= 1)
        {
            return false;
        }

        bool nearTopLeft = x < radius && y < radius;
        bool nearTopRight = x >= width - radius && y < radius;
        bool nearBottomLeft = x < radius && y >= height - radius;
        bool nearBottomRight = x >= width - radius && y >= height - radius;
        if (!(nearTopLeft || nearTopRight || nearBottomLeft || nearBottomRight))
        {
            return false;
        }

        int cx = nearTopLeft || nearBottomLeft ? radius - 1 : width - radius;
        int cy = nearTopLeft || nearTopRight ? radius - 1 : height - radius;
        int outer = radius * radius;
        int innerRadius = Math.Max(1, radius - 1);
        int inner = innerRadius * innerRadius;
        int distance = DistanceSquared(x, y, cx, cy);
        return distance <= outer && distance >= inner;
    }

    private static int DistanceSquared(int x, int y, int cx, int cy)
    {
        int dx = x - cx;
        int dy = y - cy;
        return (dx * dx) + (dy * dy);
    }

    private Rect RenderOverlayWidgetPanel(string panelKey, OverlayWidgetConfig config, Rect rect, string title, string content)
    {
        const float headerHeight = 28f;
        const float padding = 8f;

        rect = ClampOverlayRect(rect);
        Color textColor = GetWidgetAccentColor(config);
        bool isSectionWidget = panelKey.StartsWith("widget:section:", StringComparison.Ordinal);

        Rect dragRect = new Rect(rect.x, rect.y, rect.width, headerHeight);

        HandleOverlayBringToFront(panelKey, rect);
        HandleOverlayDrag(panelKey, ref rect, dragRect);

        dragRect = new Rect(rect.x, rect.y, rect.width, headerHeight);

        Rect contentRect = new Rect(
            rect.x + padding,
            rect.y + headerHeight + padding,
            Mathf.Max(0f, rect.width - (padding * 2f)),
            Mathf.Max(0f, rect.height - headerHeight - (padding * 2f)));

        HandleOverlayWidgetInteraction(panelKey, config, rect, dragRect, contentRect);

        GUIStyle titleStyle = BuildWidgetTitleStyle(rect, isSectionWidget, textColor);

        GUI.Box(rect, GUIContent.none, GUI.skin.box);

        GUI.Label(new Rect(rect.x + 8f, rect.y + 1f, Mathf.Max(0f, rect.width - 16f), headerHeight - 2f), title, titleStyle);
        if (isSectionWidget)
        {
            RenderSectionWidgetContent(contentRect, content, textColor);
        }
        else
        {
            GUIStyle contentStyle = BuildWidgetContentStyle(rect, false, content, textColor);
            GUI.Label(contentRect, content, contentStyle);
        }

        return rect;
    }

    private void HandleOverlayWidgetInteraction(string panelKey, OverlayWidgetConfig config, Rect rect, Rect dragRect, Rect contentRect)
    {
        Event? currentEvent = Event.current;
        if (currentEvent == null || currentEvent.type != EventType.MouseDown || !rect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (!Input.GetMouseButtonDown(0))
        {
            return;
        }

        if (dragRect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        if (contentRect.Contains(currentEvent.mousePosition))
        {
            bool isDoubleClick = string.Equals(_lastWidgetColorClickKey, panelKey, StringComparison.Ordinal) &&
                (Time.unscaledTime - _lastWidgetColorClickAt) <= 0.35f;
            _lastWidgetColorClickKey = panelKey;
            _lastWidgetColorClickAt = Time.unscaledTime;

            if (isDoubleClick)
            {
                OpenOverlayColorPicker(panelKey, rect, config);
                currentEvent.Use();
            }
        }
    }

    private void HandleOverlayBringToFront(string panelKey, Rect rect)
    {
        if (!panelKey.StartsWith("widget:", StringComparison.Ordinal))
        {
            return;
        }

        Event? currentEvent = Event.current;
        if (currentEvent == null ||
            currentEvent.type != EventType.MouseDown ||
            !Input.GetMouseButtonDown(0) ||
            !rect.Contains(currentEvent.mousePosition))
        {
            return;
        }

        BringWidgetToFront(panelKey);
    }

    private void HandleOverlayDrag(string panelKey, ref Rect rect, Rect dragRect)
    {
        Event? currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        if (_overlayResizingKey != null && !string.Equals(_overlayResizingKey, panelKey, StringComparison.Ordinal))
        {
            return;
        }

        Vector2 mouse = currentEvent.mousePosition;
        switch (currentEvent.type)
        {
            case EventType.MouseDown:
                if (dragRect.Contains(mouse))
                {
                    _overlayDraggingKey = panelKey;
                    _overlayDragOffset = new Vector2(mouse.x - rect.x, mouse.y - rect.y);
                    currentEvent.Use();
                }
                break;
            case EventType.MouseDrag:
                if (string.Equals(_overlayDraggingKey, panelKey, StringComparison.Ordinal))
                {
                    rect.x = mouse.x - _overlayDragOffset.x;
                    rect.y = mouse.y - _overlayDragOffset.y;
                    rect = SnapOverlayRect(panelKey, rect);
                    rect = ClampOverlayRect(rect);
                    currentEvent.Use();
                }
                break;
            case EventType.MouseUp:
                if (string.Equals(_overlayDraggingKey, panelKey, StringComparison.Ordinal))
                {
                    _overlayDraggingKey = null;
                    currentEvent.Use();
                }
                break;
        }
    }

    private void HandleOverlayResize(string panelKey, ref Rect rect, Rect resizeRect, Vector2 minimumSize)
    {
        Event? currentEvent = Event.current;
        if (currentEvent == null)
        {
            return;
        }

        if (_overlayDraggingKey != null && !string.Equals(_overlayDraggingKey, panelKey, StringComparison.Ordinal))
        {
            return;
        }

        Vector2 mouse = currentEvent.mousePosition;
        switch (currentEvent.type)
        {
            case EventType.MouseDown:
                if (resizeRect.Contains(mouse))
                {
                    _overlayResizingKey = panelKey;
                    _overlayResizeStartMouse = mouse;
                    _overlayResizeStartRect = rect;
                    currentEvent.Use();
                }
                break;
            case EventType.MouseDrag:
                if (string.Equals(_overlayResizingKey, panelKey, StringComparison.Ordinal))
                {
                    float maxWidth = Mathf.Max(minimumSize.x, Screen.width - rect.x);
                    float maxHeight = Mathf.Max(minimumSize.y, Screen.height - rect.y);
                    rect.width = Mathf.Clamp(_overlayResizeStartRect.width + (mouse.x - _overlayResizeStartMouse.x), minimumSize.x, maxWidth);
                    rect.height = Mathf.Clamp(_overlayResizeStartRect.height + (mouse.y - _overlayResizeStartMouse.y), minimumSize.y, maxHeight);
                    rect = ClampOverlayRect(rect);
                    currentEvent.Use();
                }
                break;
            case EventType.MouseUp:
                if (string.Equals(_overlayResizingKey, panelKey, StringComparison.Ordinal))
                {
                    _overlayResizingKey = null;
                    currentEvent.Use();
                }
                break;
        }
    }

    private static Vector2 GetOverlayMinimumSize(string panelKey)
    {
        if (string.Equals(panelKey, "editor", StringComparison.Ordinal))
        {
            return new Vector2(340f, 300f);
        }

        if (string.Equals(panelKey, "export-templates", StringComparison.Ordinal))
        {
            return new Vector2(840f, 520f);
        }

        return new Vector2(180f, 74f);
    }

    private static Rect ClampOverlayRect(Rect rect)
    {
        rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
        rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
        return rect;
    }

    private Rect SnapOverlayRect(string panelKey, Rect rect)
    {
        float? snappedX = null;
        float? snappedY = null;
        float bestXDistance = 12.01f;
        float bestYDistance = 12.01f;

        foreach (Rect other in GetOverlaySnapTargets(panelKey))
        {
            TrySnapAxis(rect.x, other.x, ref snappedX, ref bestXDistance);
            TrySnapAxis(rect.x, other.xMax, ref snappedX, ref bestXDistance);
            TrySnapAxis(rect.x, other.x - rect.width, ref snappedX, ref bestXDistance);
            TrySnapAxis(rect.x, other.xMax - rect.width, ref snappedX, ref bestXDistance);

            TrySnapAxis(rect.y, other.y, ref snappedY, ref bestYDistance);
            TrySnapAxis(rect.y, other.yMax, ref snappedY, ref bestYDistance);
            TrySnapAxis(rect.y, other.y - rect.height, ref snappedY, ref bestYDistance);
            TrySnapAxis(rect.y, other.yMax - rect.height, ref snappedY, ref bestYDistance);
        }

        if (snappedX.HasValue)
        {
            rect.x = snappedX.Value;
        }
        if (snappedY.HasValue)
        {
            rect.y = snappedY.Value;
        }

        return rect;
    }

    private IEnumerable<Rect> GetOverlaySnapTargets(string panelKey)
    {
        if (!string.Equals(panelKey, "editor", StringComparison.Ordinal))
        {
            yield return GetEditorRect(EnsureOverlayEditorConfig());
        }

        TrackerState? state = _latestState;
        SongConfig? songConfig = null;
        if (state?.Song != null)
        {
            _config.Songs.TryGetValue(state.Song.OverlayLayoutKey ?? state.Song.SongKey, out songConfig);
        }

        if (songConfig == null)
        {
            yield break;
        }

        int defaultIndex = 0;
        foreach (KeyValuePair<string, OverlayWidgetConfig> pair in songConfig.OverlayWidgets)
        {
            if (pair.Value == null || !pair.Value.Enabled)
            {
                continue;
            }

            string otherKey = "widget:" + pair.Key;
            if (string.Equals(otherKey, panelKey, StringComparison.Ordinal))
            {
                defaultIndex++;
                continue;
            }

            yield return GetWidgetRect(pair.Value, defaultIndex);
            defaultIndex++;
        }
    }

    private static void TrySnapAxis(float value, float target, ref float? snappedValue, ref float bestDistance)
    {
        float distance = Mathf.Abs(value - target);
        if (distance > 12f || distance >= bestDistance)
        {
            return;
        }

        snappedValue = target;
        bestDistance = distance;
    }

    private void HandleOverlayPanelInteraction(Rect rect, bool resizeHandleVisible, Action<bool>? setResizeHandleVisible)
    {
        if (setResizeHandleVisible == null)
        {
            return;
        }

        Event? currentEvent = Event.current;
        if (currentEvent == null ||
            currentEvent.type != EventType.MouseDown ||
            !rect.Contains(currentEvent.mousePosition) ||
            !Input.GetMouseButtonDown(1))
        {
            return;
        }

        setResizeHandleVisible(!resizeHandleVisible);
        currentEvent.Use();
    }

    private OverlayEditorConfig EnsureOverlayEditorConfig()
    {
        _config.OverlayEditor ??= new OverlayEditorConfig();
        return _config.OverlayEditor;
    }

    private ExportTemplateEditorConfig EnsureExportTemplateEditorConfig()
    {
        _config.ExportTemplateEditor ??= new ExportTemplateEditorConfig();
        return _config.ExportTemplateEditor;
    }

    private Dictionary<string, bool> EnsureDefaultEnabledTextExports()
    {
        _config.DefaultEnabledTextExports ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        return _config.DefaultEnabledTextExports;
    }

    private Dictionary<string, string> EnsureExportTemplateOverrides()
    {
        _config.ExportTemplateOverrides ??= new Dictionary<string, string>(StringComparer.Ordinal);
        return _config.ExportTemplateOverrides;
    }

    private void NormalizeLoadedExportTemplateOverrides()
    {
        Dictionary<string, string> normalized = new(StringComparer.Ordinal);
        if (_config.ExportTemplateOverrides != null)
        {
            foreach (KeyValuePair<string, string> pair in _config.ExportTemplateOverrides)
            {
                if (string.IsNullOrWhiteSpace(pair.Key))
                {
                    continue;
                }

                normalized[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        _config.ExportTemplateOverrides = normalized;
        _compiledExportTemplatesVersion = -1;
        _loggedInvalidTemplateSources.Clear();
    }

    private EnabledTextExportSnapshot GetEnabledTextExportsSnapshot()
    {
        if (_enabledTextExportSnapshotVersion == _enabledTextExportsVersion)
        {
            return _enabledTextExportSnapshot;
        }

        Dictionary<string, bool> rawExports = EnsureDefaultEnabledTextExports();
        Dictionary<string, bool> snapshot = new(StringComparer.Ordinal);
        foreach (TextExportDefinition exportDefinition in TextExportDefinition.All)
        {
            snapshot[exportDefinition.Key] = rawExports.TryGetValue(exportDefinition.Key, out bool enabled) && enabled;
        }

        _enabledTextExportSnapshot = new EnabledTextExportSnapshot(snapshot);
        _enabledTextExportSnapshotVersion = _enabledTextExportsVersion;
        return _enabledTextExportSnapshot;
    }

    private static Rect GetEditorRect(OverlayEditorConfig config)
    {
        float width = config.Width > 0f ? config.Width : 430f;
        float height = config.Height > 0f ? config.Height : 560f;
        float x = Mathf.Clamp(config.X, 0f, Mathf.Max(0f, Screen.width - width));
        float y = config.Y >= 0f ? config.Y : Mathf.Max(40f, Screen.height * 0.22f);
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));
        return new Rect(x, y, width, height);
    }

    private static Rect GetEditorRect(ExportTemplateEditorConfig config)
    {
        float width = config.Width > 0f ? config.Width : 960f;
        float height = config.Height > 0f ? config.Height : 680f;
        float x = Mathf.Clamp(config.X, 0f, Mathf.Max(0f, Screen.width - width));
        float y = config.Y >= 0f ? config.Y : Mathf.Max(20f, Screen.height * 0.12f);
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));
        return new Rect(x, y, width, height);
    }

    private void PersistEditorRect(OverlayEditorConfig config, Rect rect)
    {
        if (Math.Abs(config.X - rect.x) < 0.01f &&
            Math.Abs(config.Y - rect.y) < 0.01f &&
            Math.Abs(config.Width - rect.width) < 0.01f &&
            Math.Abs(config.Height - rect.height) < 0.01f)
        {
            return;
        }

        config.X = rect.x;
        config.Y = rect.y;
        config.Width = rect.width;
        config.Height = rect.height;
        MarkConfigDirty(affectsGlobalSnapshot: true);
    }

    private void PersistEditorRect(ExportTemplateEditorConfig config, Rect rect)
    {
        if (Math.Abs(config.X - rect.x) < 0.01f &&
            Math.Abs(config.Y - rect.y) < 0.01f &&
            Math.Abs(config.Width - rect.width) < 0.01f &&
            Math.Abs(config.Height - rect.height) < 0.01f)
        {
            return;
        }

        config.X = rect.x;
        config.Y = rect.y;
        config.Width = rect.width;
        config.Height = rect.height;
        MarkConfigDirty(affectsGlobalSnapshot: true);
    }

    private static Rect GetWidgetRect(OverlayWidgetConfig config, int defaultIndex)
    {
        float width = config.Width > 0f ? config.Width : OverlayWidgetDefaultWidth;
        float height = config.Height > 0f ? config.Height : OverlayWidgetDefaultHeight;
        float x = Mathf.Clamp(config.X, 0f, Mathf.Max(0f, Screen.width - width));
        float y = config.Y >= 0f ? config.Y : Mathf.Max(60f, Screen.height * 0.2f + defaultIndex * OverlayWidgetDefaultHeight);
        y = Mathf.Clamp(y, 0f, Mathf.Max(0f, Screen.height - height));
        return new Rect(x, y, width, height);
    }

    private void PersistWidgetRect(OverlayWidgetConfig config, Rect rect)
    {
        if (Math.Abs(config.X - rect.x) < 0.01f &&
            Math.Abs(config.Y - rect.y) < 0.01f &&
            Math.Abs(config.Width - rect.width) < 0.01f &&
            Math.Abs(config.Height - rect.height) < 0.01f)
        {
            return;
        }

        config.X = rect.x;
        config.Y = rect.y;
        config.Width = rect.width;
        config.Height = rect.height;
        MarkConfigDirty();
    }

    private void NormalizeSongOverlayWidgets(SongConfig songConfig, string? songKey = null)
    {
        bool changed = false;
        foreach (OverlayWidgetConfig? widget in songConfig.OverlayWidgets.Values)
        {
            if (widget != null && NormalizeOverlayWidgetConfig(widget))
            {
                changed = true;
            }
        }

        if (changed)
        {
            MarkConfigDirty(songKey: songKey);
        }
    }

    private static bool NormalizeOverlayWidgetConfig(OverlayWidgetConfig config)
    {
        bool changed = false;
        if (config.ResizeModeVersion < OverlayWidgetResizeModeVersion)
        {
            config.Width = OverlayWidgetDefaultWidth;
            config.Height = OverlayWidgetDefaultHeight;
            config.FontScale = 1f;
            config.ResizeModeVersion = OverlayWidgetResizeModeVersion;
            changed = true;
        }

        if (Math.Abs(config.FontScale - 1f) >= 0.001f)
        {
            config.FontScale = 1f;
            changed = true;
        }

        if (config.Width <= 0f)
        {
            config.Width = OverlayWidgetDefaultWidth;
            changed = true;
        }

        if (config.Height <= 0f)
        {
            config.Height = OverlayWidgetDefaultHeight;
            changed = true;
        }

        return changed;
    }

    private static int GetOverlayWidgetZIndex(OverlayWidgetConfig config)
    {
        return config.ZIndex;
    }

    private void OpenOverlayColorPicker(string panelKey, Rect panelRect, OverlayWidgetConfig config)
    {
        _overlayColorTargetKey = panelKey;
        _overlayColorPickerDragging = false;
        Color currentColor = GetWidgetAccentColor(config);
        RgbToHsv(currentColor, out _overlayColorPickerHue, out _overlayColorPickerSaturation, out _overlayColorPickerValue);
        if (_overlayColorPickerValue <= 0.01f)
        {
            _overlayColorPickerValue = 1f;
        }

        float width = 220f;
        float height = 292f;
        float desiredX = panelRect.xMax + 12f;
        if (desiredX + width > Screen.width)
        {
            desiredX = Mathf.Max(0f, panelRect.x - width - 12f);
        }

        float desiredY = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, Screen.height - height));
        _overlayColorPickerRect = new Rect(desiredX, desiredY, width, height);
    }

    private void OpenDesktopBorderColorPicker(Rect panelRect)
    {
        _overlayColorTargetKey = "desktop:border";
        _overlayColorPickerDragging = false;
        Color currentColor = GetDesktopBorderColor();
        RgbToHsv(currentColor, out _overlayColorPickerHue, out _overlayColorPickerSaturation, out _overlayColorPickerValue);
        _overlayColorPickerValue = 1f;

        float width = 220f;
        float height = 292f;
        float desiredX = panelRect.xMax + 12f;
        if (desiredX + width > Screen.width)
        {
            desiredX = Mathf.Max(0f, panelRect.x - width - 12f);
        }

        float desiredY = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, Screen.height - height));
        _overlayColorPickerRect = new Rect(desiredX, desiredY, width, height);
    }

    private void OpenAnimatedMenuColorPicker(Rect panelRect)
    {
        _overlayColorTargetKey = AnimatedMenuColorPickerTargetKey;
        _overlayColorPickerDragging = false;
        Color currentColor = GetAnimatedMenuTintColor();
        RgbToHsv(currentColor, out _overlayColorPickerHue, out _overlayColorPickerSaturation, out _overlayColorPickerValue);
        if (_overlayColorPickerValue <= 0.01f)
        {
            _overlayColorPickerValue = 1f;
        }

        float width = 220f;
        float height = 292f;
        float desiredX = panelRect.xMax + 12f;
        if (desiredX + width > Screen.width)
        {
            desiredX = Mathf.Max(0f, panelRect.x - width - 12f);
        }

        float desiredY = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, Screen.height - height));
        _overlayColorPickerRect = new Rect(desiredX, desiredY, width, height);
    }

    private void OpenAnimatedMenuWispColorPicker(Rect panelRect)
    {
        _overlayColorTargetKey = AnimatedMenuWispColorPickerTargetKey;
        _overlayColorPickerDragging = false;
        Color currentColor = GetAnimatedMenuWispColor();
        RgbToHsv(currentColor, out _overlayColorPickerHue, out _overlayColorPickerSaturation, out _overlayColorPickerValue);
        if (_overlayColorPickerValue <= 0.01f)
        {
            _overlayColorPickerValue = 1f;
        }

        float width = 220f;
        float height = 292f;
        float desiredX = panelRect.xMax + 12f;
        if (desiredX + width > Screen.width)
        {
            desiredX = Mathf.Max(0f, panelRect.x - width - 12f);
        }

        float desiredY = Mathf.Clamp(panelRect.y, 0f, Mathf.Max(0f, Screen.height - height));
        _overlayColorPickerRect = new Rect(desiredX, desiredY, width, height);
    }

    private void RenderOverlayColorPicker(SongConfig? songConfig)
    {
        bool editingDesktopBorder = string.Equals(_overlayColorTargetKey, "desktop:border", StringComparison.Ordinal);
        bool editingAnimatedMenu = string.Equals(_overlayColorTargetKey, AnimatedMenuColorPickerTargetKey, StringComparison.Ordinal);
        bool editingAnimatedMenuWisp = string.Equals(_overlayColorTargetKey, AnimatedMenuWispColorPickerTargetKey, StringComparison.Ordinal);
        OverlayWidgetConfig? widgetConfig = null;
        string targetKey = _overlayColorTargetKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetKey) ||
            (!editingDesktopBorder &&
             !editingAnimatedMenu &&
             !editingAnimatedMenuWisp &&
             (!targetKey.StartsWith("widget:", StringComparison.Ordinal) ||
              songConfig == null ||
              !songConfig.OverlayWidgets.TryGetValue(targetKey.Substring("widget:".Length), out widgetConfig))))
        {
            _overlayColorTargetKey = null;
            _overlayColorPickerDragging = false;
            return;
        }

        Event? currentEvent = Event.current;
        if (currentEvent != null &&
            currentEvent.type == EventType.MouseDown &&
            Input.GetMouseButtonDown(0) &&
            !_overlayColorPickerRect.Contains(currentEvent.mousePosition))
        {
            _overlayColorTargetKey = null;
            _overlayColorPickerDragging = false;
            return;
        }

        EnsureColorWheelTexture();

        GUI.Box(_overlayColorPickerRect, GUIContent.none, GUI.skin.box);
        string pickerTitle = editingDesktopBorder
            ? "Widget Border Color"
            : editingAnimatedMenu
                ? "Animated Menu Tint"
                : editingAnimatedMenuWisp
                    ? "Animated Menu Wisps"
                : "Overlay Color";
        GUI.Label(new Rect(_overlayColorPickerRect.x + 8f, _overlayColorPickerRect.y + 6f, _overlayColorPickerRect.width - 16f, 20f), pickerTitle, GUI.skin.label);

        Rect wheelRect = new Rect(_overlayColorPickerRect.x + 10f, _overlayColorPickerRect.y + 30f, 160f, 160f);
        Rect previewRect = new Rect(_overlayColorPickerRect.x + 10f, _overlayColorPickerRect.y + 198f, _overlayColorPickerRect.width - 20f, 24f);
        Rect confirmRect = new Rect(_overlayColorPickerRect.x + 10f, _overlayColorPickerRect.y + 230f, _overlayColorPickerRect.width - 20f, 28f);

        if (_overlayColorWheelTexture != null)
        {
            GUI.Label(wheelRect, new GUIContent(string.Empty, _overlayColorWheelTexture, string.Empty), GUI.skin.label);
        }
        GUI.Box(wheelRect, GUIContent.none, GUI.skin.box);

        if (currentEvent != null)
        {
            if (currentEvent.type == EventType.MouseDown &&
                Input.GetMouseButtonDown(0) &&
                wheelRect.Contains(currentEvent.mousePosition))
            {
                _overlayColorPickerDragging = true;
                UpdateOverlayColorFromWheel(widgetConfig, editingDesktopBorder, editingAnimatedMenu, editingAnimatedMenuWisp, wheelRect, currentEvent.mousePosition);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag &&
                _overlayColorPickerDragging &&
                Input.GetMouseButton(0))
            {
                UpdateOverlayColorFromWheel(widgetConfig, editingDesktopBorder, editingAnimatedMenu, editingAnimatedMenuWisp, wheelRect, currentEvent.mousePosition);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseUp || !Input.GetMouseButton(0))
            {
                _overlayColorPickerDragging = false;
            }
        }

        Vector2 marker = GetColorWheelMarkerPosition(wheelRect, _overlayColorPickerHue, _overlayColorPickerSaturation);
        Rect markerRect = new Rect(marker.x - 4f, marker.y - 4f, 8f, 8f);
        GUI.Box(markerRect, GUIContent.none, GUI.skin.box);

        GUI.Box(previewRect, GUIContent.none, GUI.skin.box);
        if (editingDesktopBorder)
        {
            GUIStyle previewStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(previewRect, "Press Home/F8/Ctrl+O to preview", previewStyle);
        }
        else if (editingAnimatedMenu)
        {
            GUIStyle previewStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(previewRect, "Live on the main menu when Animated is selected", previewStyle);
        }
        else if (editingAnimatedMenuWisp)
        {
            GUIStyle previewStyle = new GUIStyle(GUI.skin.label)
            {
                alignment = TextAnchor.MiddleCenter
            };
            GUI.Label(previewRect, "Colors the live wisps behind the controls", previewStyle);
        }

        bool confirmClicked = GUI.Toggle(confirmRect, false, new GUIContent("CONFIRM"), GUI.skin.button);
        if (confirmClicked)
        {
            _overlayColorTargetKey = null;
            _overlayColorPickerDragging = false;
        }
    }

    private void UpdateOverlayColorFromWheel(OverlayWidgetConfig? config, bool editingDesktopBorder, bool editingAnimatedMenu, bool editingAnimatedMenuWisp, Rect wheelRect, Vector2 mousePosition)
    {
        Vector2 center = new Vector2(wheelRect.x + wheelRect.width * 0.5f, wheelRect.y + wheelRect.height * 0.5f);
        Vector2 offset = mousePosition - center;
        offset.y = -offset.y;
        float radius = wheelRect.width * 0.5f;
        if (offset.sqrMagnitude > radius * radius)
        {
            float magnitude = offset.magnitude;
            if (magnitude > 0.0001f)
            {
                offset = new Vector2(offset.x / magnitude, offset.y / magnitude) * radius;
            }
        }

        float saturation = Mathf.Clamp01(offset.magnitude / radius);
        float angle = Mathf.Atan2(offset.y, offset.x);
        float hue = angle / ((float)Math.PI * 2f);
        if (hue < 0f)
        {
            hue += 1f;
        }

        _overlayColorPickerHue = hue;
        _overlayColorPickerSaturation = saturation;
        ApplyOverlayColor(config, editingDesktopBorder, editingAnimatedMenu, editingAnimatedMenuWisp);
    }

    private void ApplyOverlayColor(OverlayWidgetConfig? config, bool editingDesktopBorder, bool editingAnimatedMenu, bool editingAnimatedMenuWisp)
    {
        Color color = HsvToRgb(_overlayColorPickerHue, _overlayColorPickerSaturation, _overlayColorPickerValue);
        if (editingDesktopBorder)
        {
            _config.DesktopOverlayStyle.BorderR = color.r;
            _config.DesktopOverlayStyle.BorderG = color.g;
            _config.DesktopOverlayStyle.BorderB = color.b;
            _mergedDesktopOverlayStyle.BorderR = color.r;
            _mergedDesktopOverlayStyle.BorderG = color.g;
            _mergedDesktopOverlayStyle.BorderB = color.b;
            SaveDesktopOverlayStyle();
        }
        else if (editingAnimatedMenu)
        {
            _config.AnimatedMenuTintR = color.r;
            _config.AnimatedMenuTintG = color.g;
            _config.AnimatedMenuTintB = color.b;
            _config.AnimatedMenuTintA = 1f;
            ApplyAnimatedMenuTint();
        }
        else if (editingAnimatedMenuWisp)
        {
            _config.AnimatedMenuWispR = color.r;
            _config.AnimatedMenuWispG = color.g;
            _config.AnimatedMenuWispB = color.b;
            ApplyAnimatedMenuTint();
        }
        else if (config != null)
        {
            config.BackgroundR = color.r;
            config.BackgroundG = color.g;
            config.BackgroundB = color.b;
        }
        MarkConfigDirty(affectsGlobalSnapshot: editingDesktopBorder || editingAnimatedMenu || editingAnimatedMenuWisp);
        _overlayColorWheelTextureValue = -1f;
    }

    private Color GetDesktopBorderColor()
    {
        DesktopOverlayStyleConfig style = _mergedDesktopOverlayStyle ?? new DesktopOverlayStyleConfig();
        return new Color(
            Mathf.Clamp01(style.BorderR),
            Mathf.Clamp01(style.BorderG),
            Mathf.Clamp01(style.BorderB),
            Mathf.Clamp01(style.BorderA));
    }

    private void EnsureColorWheelTexture()
    {
        if (_overlayColorWheelTexture != null && Math.Abs(_overlayColorWheelTextureValue - _overlayColorPickerValue) < 0.001f)
        {
            return;
        }

        const int size = 160;
        if (_overlayColorWheelTexture == null)
        {
            _overlayColorWheelTexture = new Texture2D(size, size, TextureFormat.ARGB32, false);
            _overlayColorWheelTexture.wrapMode = TextureWrapMode.Clamp;
        }

        float radius = size * 0.5f;
        Color[] pixels = new Color[size * size];
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = x - radius + 0.5f;
                float dy = y - radius + 0.5f;
                float distance = Mathf.Sqrt(dx * dx + dy * dy);
                int pixelIndex = (y * size) + x;
                if (distance > radius)
                {
                    pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                    continue;
                }

                float saturation = Mathf.Clamp01(distance / radius);
                float hue = Mathf.Atan2(dy, dx) / ((float)Math.PI * 2f);
                if (hue < 0f)
                {
                    hue += 1f;
                }

                Color color = HsvToRgb(hue, saturation, _overlayColorPickerValue);
                color.a = 1f;
                pixels[pixelIndex] = color;
            }
        }

        _overlayColorWheelTexture.SetPixels(0, 0, size, size, pixels);
        _overlayColorWheelTexture.Apply();
        _overlayColorWheelTextureValue = _overlayColorPickerValue;
    }

    private static Vector2 GetColorWheelMarkerPosition(Rect wheelRect, float hue, float saturation)
    {
        float angle = hue * (float)Math.PI * 2f;
        float radius = wheelRect.width * 0.5f * saturation;
        Vector2 center = new Vector2(wheelRect.x + wheelRect.width * 0.5f, wheelRect.y + wheelRect.height * 0.5f);
        return new Vector2(center.x + Mathf.Cos(angle) * radius, center.y - (Mathf.Sin(angle) * radius));
    }

    private static void GetOverlayWidgetFontSizes(Rect rect, bool isSectionWidget, string content, out int titleFontSize, out int contentFontSize)
    {
        int lineCount = 1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
            {
                lineCount++;
            }
        }

        float baseTitleSize = isSectionWidget ? 17f : 15f;
        float baseContentSize = isSectionWidget ? 17f : (lineCount > 1 ? 16f : 18f);
        float maxTitleSize = Mathf.Max(13f, Mathf.Min(24f, (rect.height - 12f) * 0.34f));
        float availableContentHeight = Mathf.Max(22f, rect.height - 44f);
        float maxContentSize = Mathf.Max(12f, (availableContentHeight / lineCount) - 1f);
        titleFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(baseTitleSize, maxTitleSize)), 12, 28);
        contentFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(baseContentSize, maxContentSize)), 11, 34);
    }

    private GUIStyle BuildWidgetTitleStyle(Rect rect, bool isSectionWidget, Color textColor)
    {
        GetOverlayWidgetFontSizes(rect, isSectionWidget, string.Empty, out int titleFontSize, out _);
        _widgetTitleStyle ??= new GUIStyle(GUI.skin.label);
        GUIStyle style = _widgetTitleStyle;
        style.alignment = TextAnchor.MiddleLeft;
        style.fontStyle = FontStyle.Bold;
        style.fontSize = titleFontSize;
        style.normal.textColor = textColor;
        return style;
    }

    private GUIStyle BuildWidgetContentStyle(Rect rect, bool isSectionWidget, string content, Color textColor)
    {
        GetOverlayWidgetFontSizes(rect, isSectionWidget, content, out _, out int contentFontSize);
        _widgetContentStyle ??= new GUIStyle(GUI.skin.label);
        GUIStyle style = _widgetContentStyle;
        style.alignment = TextAnchor.UpperLeft;
        style.fontStyle = FontStyle.Normal;
        style.fontSize = contentFontSize;
        style.normal.textColor = textColor;
        return style;
    }

    private void RenderSectionWidgetContent(Rect contentRect, string content, Color textColor)
    {
        string[] lines = content.Split('\n');
        string attemptsLine = lines.Length >= 1 ? lines[0] : string.Empty;
        string emphasisLine = lines.Length >= 2 ? lines[1] : string.Empty;

        _widgetSectionAttemptsStyle ??= new GUIStyle(GUI.skin.label);
        GUIStyle attemptsStyle = _widgetSectionAttemptsStyle;
        attemptsStyle.alignment = TextAnchor.UpperLeft;
        attemptsStyle.fontStyle = FontStyle.Normal;
        attemptsStyle.fontSize = 14;
        attemptsStyle.normal.textColor = textColor;

        _widgetSectionEmphasisStyle ??= new GUIStyle(GUI.skin.label);
        GUIStyle emphasisStyle = _widgetSectionEmphasisStyle;
        emphasisStyle.alignment = TextAnchor.UpperLeft;
        emphasisStyle.fontStyle = FontStyle.Bold;
        emphasisStyle.fontSize = 17;
        emphasisStyle.normal.textColor = textColor;

        float attemptsHeight = Mathf.Max(18f, attemptsStyle.CalcHeight(new GUIContent(attemptsLine), contentRect.width));
        Rect attemptsRect = new Rect(contentRect.x, contentRect.y, contentRect.width, attemptsHeight);
        Rect emphasisRect = new Rect(contentRect.x, contentRect.y + attemptsHeight + 2f, contentRect.width, Mathf.Max(0f, contentRect.height - attemptsHeight - 2f));
        GUI.Label(attemptsRect, attemptsLine, attemptsStyle);
        GUI.Label(emphasisRect, emphasisLine, emphasisStyle);
    }

    private static GUIStyle BuildResizeHandleStyle(Color textColor)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            fontStyle = FontStyle.Bold,
            fontSize = 18
        };
        style.normal.textColor = textColor;
        return style;
    }

    private static Color GetWidgetAccentColor(OverlayWidgetConfig config)
    {
        if (config.BackgroundR <= 0.01f &&
            config.BackgroundG <= 0.01f &&
            config.BackgroundB <= 0.01f)
        {
            return Color.white;
        }

        return new Color(config.BackgroundR, config.BackgroundG, config.BackgroundB, 1f);
    }

    private static Color GetReadableTextColor(Color background)
    {
        float luminance = (background.r * 0.299f) + (background.g * 0.587f) + (background.b * 0.114f);
        return luminance >= 0.65f ? Color.black : Color.white;
    }


    private static Color HsvToRgb(float hue, float saturation, float value)
    {
        hue = Mathf.Repeat(hue, 1f);
        saturation = Mathf.Clamp01(saturation);
        value = Mathf.Clamp01(value);
        if (saturation <= 0.0001f)
        {
            return new Color(value, value, value, 1f);
        }

        float scaled = hue * 6f;
        int sector = Mathf.FloorToInt(scaled);
        float fraction = scaled - sector;
        float p = value * (1f - saturation);
        float q = value * (1f - (saturation * fraction));
        float t = value * (1f - (saturation * (1f - fraction)));

        return sector switch
        {
            0 => new Color(value, t, p, 1f),
            1 => new Color(q, value, p, 1f),
            2 => new Color(p, value, t, 1f),
            3 => new Color(p, q, value, 1f),
            4 => new Color(t, p, value, 1f),
            _ => new Color(value, p, q, 1f)
        };
    }

    private static void RgbToHsv(Color color, out float hue, out float saturation, out float value)
    {
        float max = Mathf.Max(color.r, Mathf.Max(color.g, color.b));
        float min = Mathf.Min(color.r, Mathf.Min(color.g, color.b));
        float delta = max - min;

        value = max;
        saturation = max <= 0.0001f ? 0f : delta / max;
        if (delta <= 0.0001f)
        {
            hue = 0f;
            return;
        }

        if (Math.Abs(max - color.r) < 0.0001f)
        {
            hue = ((color.g - color.b) / delta) % 6f;
        }
        else if (Math.Abs(max - color.g) < 0.0001f)
        {
            hue = ((color.b - color.r) / delta) + 2f;
        }
        else
        {
            hue = ((color.r - color.g) / delta) + 4f;
        }

        hue /= 6f;
        if (hue < 0f)
        {
            hue += 1f;
        }
    }

    private bool TryBuildOverlayWidget(TrackerState state, string widgetKey, out string title, out string content)
    {
        title = string.Empty;
        content = string.Empty;

        if (widgetKey.StartsWith("section:", StringComparison.Ordinal))
        {
            string sectionKey = widgetKey.Substring("section:".Length);
            SectionStatsState? section = ResolveOverlaySection(state, sectionKey);
            if (section == null)
            {
                return false;
            }

            title = section.Name;
            content = $"Attempts: {section.Attempts} | FCs Past: {section.RunsPast}\nKilled the Run: {section.KilledTheRun}";
            return true;
        }

        if (!widgetKey.StartsWith("metric:", StringComparison.Ordinal))
        {
            return false;
        }

        string metricKey = widgetKey.Substring("metric:".Length);
        OverlayMetricDefinition? metric = OverlayMetricDefinition.All.FirstOrDefault(candidate => string.Equals(candidate.Key, metricKey, StringComparison.Ordinal));
        if (metric == null)
        {
            return false;
        }

        title = metric.Label;
        content = GetMetricValueText(state, metric.Key);
        return true;
    }

    private static string GetMetricValueText(TrackerState state, string metricKey)
    {
        return metricKey switch
        {
            "streak" => state.Streak.ToString(CultureInfo.InvariantCulture),
            "best_streak" => state.BestStreak.ToString(CultureInfo.InvariantCulture),
            "attempts" => state.Attempts.ToString(CultureInfo.InvariantCulture),
            "current_missed_notes" => state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture),
            "current_overstrums" => state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture),
            "current_ghosted_notes" => state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture),
            "lifetime_ghosted_notes" => state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture),
            "global_lifetime_ghosted_notes" => state.GlobalLifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture),
            "fc_achieved" => state.FcAchieved ? "True" : "False",
            _ => string.Empty
        };
    }

    private static string GetOverlaySongTitle(SongDescriptor song)
    {
        return song.Title ?? song.SongKey;
    }

    private static IEnumerable<SectionStatsState> GetOverlaySectionsForOverlay(TrackerState state)
    {
        return state.SectionStats
            .OrderBy(section => section.Index)
            .ToList();
    }

    private static Dictionary<string, int> BuildOverlaySectionOrder(TrackerState state)
    {
        var order = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        foreach (SectionStatsState section in GetOverlaySectionsForOverlay(state))
        {
            order[BuildSectionOverlayKey(state.SectionStats, section)] = index++;
        }

        return order;
    }

    private static int CompareOverlayWidgetEntries(Dictionary<string, int> sectionOrder, KeyValuePair<string, OverlayWidgetConfig> left, KeyValuePair<string, OverlayWidgetConfig> right)
    {
        int zOrder = GetOverlayWidgetZIndex(left.Value).CompareTo(GetOverlayWidgetZIndex(right.Value));
        if (zOrder != 0)
        {
            return zOrder;
        }

        return CompareOverlayWidgetKeys(sectionOrder, left.Key, right.Key);
    }

    private static int CompareOverlayWidgetKeys(Dictionary<string, int> sectionOrder, string leftKey, string rightKey)
    {
        bool leftIsSection = leftKey.StartsWith("section:", StringComparison.Ordinal);
        bool rightIsSection = rightKey.StartsWith("section:", StringComparison.Ordinal);

        if (leftIsSection && rightIsSection)
        {
            string leftSection = leftKey.Substring("section:".Length);
            string rightSection = rightKey.Substring("section:".Length);
            bool leftHasOrder = sectionOrder.TryGetValue(leftSection, out int leftIndex);
            bool rightHasOrder = sectionOrder.TryGetValue(rightSection, out int rightIndex);

            if (leftHasOrder && rightHasOrder && leftIndex != rightIndex)
            {
                return leftIndex.CompareTo(rightIndex);
            }

            if (leftHasOrder != rightHasOrder)
            {
                return leftHasOrder ? -1 : 1;
            }

            return string.Compare(leftSection, rightSection, StringComparison.Ordinal);
        }

        if (leftIsSection != rightIsSection)
        {
            return leftIsSection ? -1 : 1;
        }

        return string.Compare(leftKey, rightKey, StringComparison.Ordinal);
    }

    private static string BuildEditorSectionLabel(SectionStatsState section)
    {
        return $"{section.Name} | FCs past: {section.RunsPast} | died: {section.Attempts}";
    }

    private static bool IsWidgetEnabled(SongConfig songConfig, string widgetKey)
    {
        return songConfig.OverlayWidgets.TryGetValue(widgetKey, out OverlayWidgetConfig? widget) && widget.Enabled;
    }

    private static bool IsTextExportEnabled(EnabledTextExportSnapshot enabledTextExports, string exportKey)
    {
        return enabledTextExports.IsEnabled(exportKey);
    }

    private static bool IsTextExportEnabled(TrackerState state, string exportKey)
    {
        return IsTextExportEnabled(state.EnabledTextExports, exportKey);
    }

    private static bool HasAnyTextExportEnabled(TrackerState state)
    {
        return state.EnabledTextExports.HasAnyObsTextExport ||
            state.SectionStats.Any(section => section.Tracked);
    }

    private static bool HasAnyTrackedSectionExportEnabled(SongConfig songConfig)
    {
        return songConfig.TrackedSections.Any(pair => pair.Value);
    }

    private void DisableAllTextExports(Dictionary<string, bool> enabledTextExports)
    {
        if (enabledTextExports.Count == 0)
        {
            return;
        }

        enabledTextExports.Clear();
        MarkConfigDirty(affectsTextExports: true, affectsGlobalSnapshot: true);
        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: true);
    }

    private void SetWidgetEnabled(SongConfig songConfig, string widgetKey, bool enabled)
    {
        if (!songConfig.OverlayWidgets.TryGetValue(widgetKey, out OverlayWidgetConfig? widget))
        {
            widget = new OverlayWidgetConfig();
            songConfig.OverlayWidgets[widgetKey] = widget;
        }

        NormalizeOverlayWidgetConfig(widget);
        widget.Enabled = enabled;
        if (widget.Y < 0f)
        {
            int enabledIndex = songConfig.OverlayWidgets
                .Where(pair => pair.Value != null && pair.Value.Enabled)
                .Count();
            widget.X = 20f;
            widget.Y = Mathf.Max(60f, Screen.height * 0.2f + enabledIndex * OverlayWidgetDefaultHeight);
            widget.Width = widget.Width > 0f ? widget.Width : OverlayWidgetDefaultWidth;
            widget.Height = widget.Height > 0f ? widget.Height : OverlayWidgetDefaultHeight;
        }

        if (enabled)
        {
            widget.ZIndex = GetNextWidgetZIndex(songConfig);
        }
    }

    private void BringWidgetToFront(string panelKey)
    {
        if (!panelKey.StartsWith("widget:", StringComparison.Ordinal))
        {
            return;
        }

        SongConfig? songConfig = TryGetActiveSongConfig();
        if (songConfig == null)
        {
            return;
        }

        string widgetKey = panelKey.Substring("widget:".Length);
        if (!songConfig.OverlayWidgets.TryGetValue(widgetKey, out OverlayWidgetConfig? widget) || widget == null)
        {
            return;
        }

        NormalizeOverlayWidgetConfig(widget);
        int nextZIndex = GetNextWidgetZIndex(songConfig);
        if (widget.ZIndex >= nextZIndex - 1)
        {
            return;
        }

        widget.ZIndex = nextZIndex;
        MarkConfigDirty();
    }

    private SongConfig? TryGetActiveSongConfig()
    {
        TrackerState? state = _latestState;
        if (state?.Song == null)
        {
            return null;
        }

        _config.Songs.TryGetValue(state.Song.OverlayLayoutKey ?? state.Song.SongKey, out SongConfig? songConfig);
        return songConfig;
    }

    private bool ShouldUseDesktopOverlay(TrackerState state)
    {
        if (IsTextExportEnabled(GetEnabledTextExportsSnapshot(), NoteSplitModeExportKey))
        {
            return true;
        }

        SongConfig? songConfig = state.Song == null ? null : TryGetSongConfig(state.Song);
        return songConfig != null && songConfig.OverlayWidgets.Values.Any(widget => widget != null && widget.Enabled);
    }

    private SongConfig? TryGetSongConfig(SongDescriptor? song)
    {
        if (song == null)
        {
            return null;
        }

        _config.Songs.TryGetValue(song.OverlayLayoutKey ?? song.SongKey, out SongConfig? songConfig);
        return songConfig;
    }

    private static int GetNextWidgetZIndex(SongConfig songConfig)
    {
        int maxZIndex = 0;
        foreach (OverlayWidgetConfig? widget in songConfig.OverlayWidgets.Values)
        {
            if (widget != null && widget.ZIndex > maxZIndex)
            {
                maxZIndex = widget.ZIndex;
            }
        }

        return maxZIndex + 1;
    }

    private static string BuildSectionWidgetKey(string sectionName) => "section:" + sectionName;

    private static string BuildSectionOverlayKey(IReadOnlyList<SectionStatsState> sections, SectionStatsState target)
    {
        return BuildSectionExportName(sections, target);
    }

    private static string BuildSectionOverlayKey(IReadOnlyList<SectionDescriptor> sections, SectionDescriptor target)
    {
        string displayName = GetSectionDisplayName(target);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        int occurrence = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            SectionDescriptor section = sections[i];
            if (!string.Equals(section.Name, target.Name, StringComparison.Ordinal))
            {
                continue;
            }

            occurrence++;
            if (section.Index == target.Index)
            {
                return occurrence <= 1 ? target.Name : $"{target.Name} ({occurrence})";
            }
        }

        return target.Name;
    }

    private static SectionStatsState? ResolveOverlaySection(TrackerState state, string sectionKey)
    {
        if (state.SectionStatsByName.TryGetValue(sectionKey, out SectionStatsState? section))
        {
            return section;
        }

        return state.SectionStats.FirstOrDefault(candidate => string.Equals(candidate.Name, sectionKey, StringComparison.Ordinal));
    }
    private static string BuildMetricWidgetKey(string metricKey) => "metric:" + metricKey;

    private static int GetStableWindowId(string value, int seed)
    {
        unchecked
        {
            int hash = seed;
            for (int i = 0; i < value.Length; i++)
            {
                hash = hash * 31 + value[i];
            }

            return hash & 0x7fffffff;
        }
    }

    private void EnsureInitialized(Assembly assemblyCSharp)
    {
        if (_initialized)
        {
            return;
        }

        _dataDir = StatTrackDataPaths.GetCurrentDataDirectory();
        _statePath = Path.Combine(_dataDir, "state.json");
        _memoryPath = Path.Combine(_dataDir, "memory.json");
        _configPath = Path.Combine(_dataDir, "config.json");
        _desktopStylePath = Path.Combine(_dataDir, "desktop-style.json");
        _obsDir = Path.Combine(_dataDir, "obs");
        _obsStatePath = Path.Combine(_obsDir, "state.json");
        Directory.CreateDirectory(_dataDir);
        _globalVariablesType = assemblyCSharp.GetType(GlobalVariablesTypeName);
        _basePlayerType = assemblyCSharp.GetType(BasePlayerTypeName);
        _gameSettingType = Type.GetType("StrikeCore.GameSetting, StrikeCore", throwOnError: false);
        Directory.CreateDirectory(_obsDir);
        EnsureExportWorkerStarted();
        _memory = LoadJson(_memoryPath, new TrackerMemory());
        _config = LoadJson(_configPath, new TrackerConfig());
        RefreshMergedDesktopOverlayStyle();
        NormalizeLoadedExportTemplateOverrides();
        _memoryDirty = !File.Exists(_memoryPath);
        _configDirty = !File.Exists(_configPath);
        _latestState = CreateIdleState();
        _overlayEditorVisible = false;
        SaveDesktopOverlayStyle();
        SaveConfig();
        SaveMemory();
        _initialized = true;
        StockTrackerLog.WriteDebug("Initialized stock tracker.");
    }

    private void CacheReflection()
    {
        if (_gameManagerType == null || _basePlayerType == null)
        {
            return;
        }

        _playersField ??= _gameManagerType.GetField(PlayersFieldName, AnyInstance)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(field => field.FieldType == _basePlayerType.MakeArrayType());
        _mainPlayerField ??= _gameManagerType.GetFields(AnyInstance)
            .FirstOrDefault(field => field.FieldType == _basePlayerType);
        _practiceUiField ??= _gameManagerType.GetField(PracticeUiFieldName, AnyInstance)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(field => string.Equals(field.FieldType.Name, "PracticeUI", StringComparison.Ordinal));
        _songDurationField ??= _gameManagerType.GetField(SongDurationFieldName, AnyInstance)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(field => field.FieldType == typeof(double));
        _songTimeField ??= _gameManagerType.GetField(SongTimeFieldName, AnyInstance)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(field => field.FieldType == typeof(double) && field != _songDurationField);
        _songPlaybackTimeField ??= _gameManagerType.GetField(SongPlaybackTimeFieldName, AnyInstance);
        _currentSectionTickField ??= _gameManagerType.GetField(CurrentSectionTickFieldName, AnyInstance);
        _chartField ??= _gameManagerType.GetField(ActiveChartFieldName, AnyInstance)
            ?? _gameManagerType.GetField(CurrentChartFieldName, AnyInstance)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(field => field.Name.IndexOf("k__BackingField", StringComparison.Ordinal) >= 0)
            ?? _gameManagerType.GetFields(AnyInstance).FirstOrDefault(IsChartLikeField);

        _controllerField ??= _basePlayerType.GetField(BasePlayerControllerFieldName, AnyInstance);
        _scoreField ??= _basePlayerType.GetField(BasePlayerScoreFieldName, AnyInstance);
        _comboField ??= _basePlayerType.GetField(BasePlayerComboFieldName, AnyInstance);
        _ghostNotesField ??= _basePlayerType.GetField("Ê²Ë€ÊµÊ»Ê¿Ê´Ê¹ÊµÊ¿Ê¼Ê»", AnyInstance);
        _overstrumsField ??= _basePlayerType.GetField("Ê·Ê¸Ê½Ë€Ê¾Ê»Ê¹Ê·ÊºË€Ê½", AnyInstance);
        _comboField = _basePlayerType.GetField("ʲʺʺʿʳʲʾʲʽʼʿ", AnyInstance) ?? _comboField;
        _ghostNotesField = _basePlayerType.GetField("ʲˀʵʻʿʴʹʵʿʼʻ", AnyInstance) ?? _ghostNotesField;
        _overstrumsField = _basePlayerType.GetField("ʷʸʽˀʾʻʹʷʺˀʽ", AnyInstance) ?? _overstrumsField;
        _globalVariablesSingletonField ??= _globalVariablesType?.GetField(GlobalVariablesSingletonFieldName, AnyStatic)
            ?? _globalVariablesType?.GetFields(AnyStatic).FirstOrDefault(field => field.FieldType == _globalVariablesType);
        _globalVariablesCurrentSongField ??= _globalVariablesType?.GetField(GlobalVariablesCurrentSongFieldName, AnyInstance)
            ?? _globalVariablesType?.GetFields(AnyInstance).FirstOrDefault(field => string.Equals(field.FieldType.Name, "SongEntry", StringComparison.Ordinal));
        _globalVariablesSongSpeedField ??= _globalVariablesType?.GetField("ʹʷʾʿʴˁʼʲʼʸʴ", AnyInstance);
        _gameSettingNameField ??= _gameSettingType?.GetField("name", AnyInstance);
        _gameSettingCurrentValueProperty ??= _gameSettingType?.GetProperty("CurrentValue", AnyInstance);
        _gameSettingPercentStringProperty ??= _gameSettingType?.GetProperty("GetPercentString", AnyInstance);
        _songSpeedSettingField ??= FindSongSpeedSettingField();
        _menuBackgroundSettingField ??= FindMenuBackgroundSettingField();
        _blackMenuType ??= _gameManagerType.Assembly.GetType("BlackMenu");

        if (_controllerField?.FieldType != null)
        {
            _playerSettingsField ??= _controllerField.FieldType.GetField(ControllerSettingsFieldName, AnyInstance);
        }
        else if (_basePlayerType != null)
        {
            _controllerField ??= _basePlayerType.GetFields(AnyInstance)
                .FirstOrDefault(field => HasRemotePlayerSettings(field.FieldType));
            if (_controllerField?.FieldType != null)
            {
                _playerSettingsField ??= _controllerField.FieldType.GetFields(AnyInstance)
                    .FirstOrDefault(field => HasIsRemoteMember(field.FieldType));
            }
        }

        if (_chartField?.FieldType != null)
        {
            _chartSectionsMethod ??= GetAllMethods(_chartField.FieldType)
                .FirstOrDefault(method => method.GetParameters().Length == 0 && typeof(IEnumerable).IsAssignableFrom(method.ReturnType));
            _chartSectionTimeMethod ??= GetAllMethods(_chartField.FieldType)
                .FirstOrDefault(method =>
                    method.GetParameters().Length == 1 &&
                    method.GetParameters()[0].ParameterType == typeof(int) &&
                    (method.ReturnType == typeof(double) || method.ReturnType == typeof(float)));
            _chartNamedSectionsField ??= GetAllFields(_chartField.FieldType)
                .FirstOrDefault(field =>
                    typeof(IEnumerable).IsAssignableFrom(field.FieldType) &&
                    !field.FieldType.IsArray &&
                    field.Name == "Ê²ÊµÊ¹Ê¼Ê²Ê¾Ê¿Ê¼ÊµÊ½Ë");
        }
    }

    private TrackingRequirements BuildTrackingRequirements(SongConfig songConfig)
    {
        EnabledTextExportSnapshot enabledTextExports = GetEnabledTextExportsSnapshot();
        bool needSectionWidgets = songConfig.OverlayWidgets.Any(pair =>
            pair.Value != null &&
            pair.Value.Enabled &&
            pair.Key.StartsWith("section:", StringComparison.Ordinal));
        bool needSectionExports = HasAnyTrackedSectionExportEnabled(songConfig);
        bool needCompletedRuns = IsTextExportEnabled(enabledTextExports, "completed_runs");
        bool needRunTracking =
            IsTextExportEnabled(enabledTextExports, "best_streak") ||
            IsTextExportEnabled(enabledTextExports, "attempts") ||
            IsTextExportEnabled(enabledTextExports, "lifetime_ghosted_notes") ||
            IsTextExportEnabled(enabledTextExports, "global_lifetime_ghosted_notes") ||
            IsTextExportEnabled(enabledTextExports, "fc_achieved") ||
            needSectionExports ||
            IsWidgetEnabled(songConfig, BuildMetricWidgetKey("best_streak")) ||
            IsWidgetEnabled(songConfig, BuildMetricWidgetKey("attempts")) ||
            IsWidgetEnabled(songConfig, BuildMetricWidgetKey("lifetime_ghosted_notes")) ||
            IsWidgetEnabled(songConfig, BuildMetricWidgetKey("global_lifetime_ghosted_notes")) ||
            IsWidgetEnabled(songConfig, BuildMetricWidgetKey("fc_achieved")) ||
            needSectionWidgets;
        bool noteSplitEnabled = IsTextExportEnabled(enabledTextExports, NoteSplitModeExportKey);
        bool needAnyRunTracking = needRunTracking || needCompletedRuns || noteSplitEnabled;
        bool needCurrentSection = needAnyRunTracking ||
            IsTextExportEnabled(enabledTextExports, "current_section") ||
            needSectionExports ||
            needSectionWidgets ||
            _overlayEditorVisible;

        return new TrackingRequirements
        {
            NeedScore = needCompletedRuns,
            NeedStreak = needAnyRunTracking ||
                IsTextExportEnabled(enabledTextExports, "streak") ||
                IsWidgetEnabled(songConfig, BuildMetricWidgetKey("streak")),
            NeedGhostNotes = needAnyRunTracking ||
                IsTextExportEnabled(enabledTextExports, "current_ghosted_notes") ||
                IsWidgetEnabled(songConfig, BuildMetricWidgetKey("current_ghosted_notes")),
            NeedOverstrums = needAnyRunTracking ||
                IsTextExportEnabled(enabledTextExports, "current_overstrums") ||
                IsWidgetEnabled(songConfig, BuildMetricWidgetKey("current_overstrums")),
            NeedMissedNotes = needAnyRunTracking ||
                IsTextExportEnabled(enabledTextExports, "current_missed_notes") ||
                IsWidgetEnabled(songConfig, BuildMetricWidgetKey("current_missed_notes")),
            NeedSongTiming = needAnyRunTracking ||
                IsTextExportEnabled(enabledTextExports, "current_section") ||
                needSectionExports ||
                needSectionWidgets ||
                _overlayEditorVisible,
            NeedCurrentSection = needCurrentSection,
            NeedSections = needAnyRunTracking || needSectionExports || needSectionWidgets || needCurrentSection || _overlayEditorVisible,
            NeedRunTracking = needRunTracking || noteSplitEnabled,
            NeedCompletedRunTracking = needCompletedRuns,
            NeedSongMemory = needAnyRunTracking,
            NeedNotesHit = needCompletedRuns,
            NeedResultStats = needCompletedRuns,
            NeedCompletedRuns = needCompletedRuns
        };
    }

    private void UpdateMinimalRunState(string songKey, string currentSectionName, double songTime, int streak, int currentGhostNotes, int currentOverstrums, int currentMissedNotes)
    {
        _runState.InRun = true;
        _runState.SongKey = songKey;
        _runState.LastSection = currentSectionName;
        _runState.LastSongTime = songTime;
        _runState.LastStreak = streak;
        _runState.LastGhostNotes = currentGhostNotes;
        _runState.LastOverstrums = currentOverstrums;
        _runState.LastMissedNotes = currentMissedNotes;
    }

    private bool ShouldPreserveInSongStateDuringTransientContextLoss()
    {
        return _runState.InRun &&
            _latestState.IsInSong &&
            (Mathf.Abs(Time.timeScale) <= 0.001f ||
             Time.unscaledTime - _runState.LastStableRefreshAt < StableRunRefreshIntervalSeconds);
    }

    private TrackerState BuildTransientInSongState()
    {
        TrackerState preservedState = _latestState ?? CreateIdleState();
        preservedState.OverlayEditorVisible = _overlayEditorVisible;
        return preservedState;
    }

    private static bool LooksLikeActualRestart(double songTime, int score, int streak, int currentGhostNotes, int currentOverstrums, int currentMissedNotes)
    {
        if (songTime > 2.0d)
        {
            return false;
        }

        return score <= 0 &&
            streak <= 0 &&
            currentGhostNotes <= 0 &&
            currentOverstrums <= 0 &&
            currentMissedNotes <= 0;
    }

    private static bool LooksLikeReturnToSongStart(double previousSongTime, double currentSongTime)
    {
        return currentSongTime <= 2.0d &&
            previousSongTime >= currentSongTime + 2.0d;
    }

    private TrackerState BuildState(object gameManager)
    {
        object? currentSongEntryHint = null;
        try
        {
            object? globalVariables = _globalVariablesSingletonField?.GetValue(null);
            currentSongEntryHint = globalVariables == null ? null : _globalVariablesCurrentSongField?.GetValue(globalVariables);
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }

        bool stableCacheLooksValid =
            _runState.CachedSongEntry != null &&
            _runState.CachedPlayer != null &&
            _runState.CachedChart != null &&
            currentSongEntryHint != null &&
            ReferenceEquals(currentSongEntryHint, _runState.CachedSongEntry);

        bool canUseStableRunCache = stableCacheLooksValid &&
            Time.unscaledTime - _runState.LastStableRefreshAt < StableRunRefreshIntervalSeconds;

        object? songEntry = canUseStableRunCache ? _runState.CachedSongEntry : null;
        object? player = canUseStableRunCache ? _runState.CachedPlayer : null;
        object? chart = canUseStableRunCache ? _runState.CachedChart : null;

        if (!canUseStableRunCache)
        {
            songEntry = currentSongEntryHint;
            player = GetPreferredActivePlayer(gameManager);
            chart = _chartField?.GetValue(gameManager);
            if (songEntry != null)
            {
                chart ??= LoadChartFromSongEntry(songEntry);
            }
        }

        if (player == null || songEntry == null || chart == null)
        {
            if (ShouldPreserveInSongStateDuringTransientContextLoss())
            {
                return BuildTransientInSongState();
            }

            LogDiagnostics(gameManager, songEntry, player, chart);
            ResetRunIfNeeded();
            return CreateIdleState();
        }
        bool botEnabled;
        if (canUseStableRunCache)
        {
            botEnabled = _runState.CachedBotEnabled;
        }
        else
        {
            botEnabled = IsBotEnabled(player);
            _runState.CachedBotEnabled = botEnabled;
        }

        if (botEnabled)
        {
            ResetExactMissCounter(player);
            ResetRunIfNeeded();
            return CreateIdleState();
        }

        double songTime = ConvertToDouble(_songTimeField?.GetValue(gameManager));
        double currentSectionSongTime = ReadCurrentSectionSongTime(gameManager, songTime);
        int currentChartTick = ReadCurrentChartTick(gameManager);
        double songDuration = canUseStableRunCache
            ? _runState.CachedSongDuration
            : ConvertToDouble(_songDurationField?.GetValue(gameManager));
        bool isPractice = IsPracticeMode(gameManager);
        if (isPractice)
        {
            if (!_practiceAttemptRollbackApplied)
            {
                SongMemory? practiceSongMemory = _runState.CachedSongMemory;
                if (practiceSongMemory == null &&
                    !string.IsNullOrWhiteSpace(_runState.SongKey) &&
                    _memory.Songs.TryGetValue(_runState.SongKey, out SongMemory? existingSongMemory))
                {
                    practiceSongMemory = existingSongMemory;
                }

                if (practiceSongMemory != null && practiceSongMemory.Attempts > 0)
                {
                    practiceSongMemory.Attempts--;
                    MarkMemoryDirty();
                }

                _practiceAttemptRollbackApplied = true;
            }

            ResetExactMissCounter(player);
            ResetRunIfNeeded();
            return CreateIdleState();
        }
        _practiceAttemptRollbackApplied = false;
        SongDescriptor song;
        if (canUseStableRunCache && _runState.CachedSongDescriptor != null)
        {
            song = _runState.CachedSongDescriptor;
        }
        else
        {
            SongSpeedInfo songSpeed = ReadSongSpeed();
            DifficultyInfo difficulty = ReadDifficultyInfo(player);
            song = BuildSongDescriptor(songEntry, songSpeed, difficulty);
            _runState.CachedSongSpeed = songSpeed;
            _runState.CachedDifficulty = difficulty;
            _runState.CachedSongDescriptor = song;
            _runState.CachedSongDuration = songDuration;
            _runState.CachedSongEntry = songEntry;
            _runState.CachedPlayer = player;
            _runState.CachedChart = chart;
            _runState.LastStableRefreshAt = Time.unscaledTime;
        }

        if (songDuration <= 0d)
        {
            songDuration = ConvertToDouble(_songDurationField?.GetValue(gameManager));
        }
        SongConfig songConfig =
            _runState.CachedSongConfig != null &&
            string.Equals(_runState.CachedSongConfigKey, song.SongKey, StringComparison.Ordinal)
                ? _runState.CachedSongConfig
                : EnsureSongConfig(song, Array.Empty<SectionDescriptor>());
        _runState.CachedSongConfig = songConfig;
        _runState.CachedSongConfigKey = song.SongKey;

        TrackingRequirements requirements = BuildTrackingRequirements(songConfig);
        CachePlayerStatsFields(player);
        int score = requirements.NeedScore || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? ConvertToInt32(_scoreField?.GetValue(player))
            : 0;
        int streak = requirements.NeedStreak || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? ConvertToInt32(_comboField?.GetValue(player))
            : 0;
        int currentGhostNotes = 0;
        if (requirements.NeedGhostNotes || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking)
        {
            currentGhostNotes = ConvertToInt32(_ghostNotesField?.GetValue(player));
            currentGhostNotes = Math.Max(currentGhostNotes, ConvertToInt32(_ghostInputsProperty?.GetValue(player, null)));
            currentGhostNotes = Math.Max(currentGhostNotes, ConvertToInt32(_fretsBetweenNotesField?.GetValue(player)));
        }

        int currentOverstrums = requirements.NeedOverstrums || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? ConvertToInt32(_overstrumsField?.GetValue(player))
            : 0;
        bool shouldRefreshNotesHit = requirements.NeedNotesHit &&
            (!_runState.HasCachedNotesHit ||
             Time.unscaledTime - _runState.LastNotesHitRefreshAt >= NotesHitRefreshIntervalSeconds);
        int currentNotesHit = shouldRefreshNotesHit
            ? ConvertToInt32(_notesHitField?.GetValue(player))
            : _runState.CachedNotesHit;
        if (shouldRefreshNotesHit)
        {
            _runState.CachedNotesHit = currentNotesHit;
            _runState.LastNotesHitRefreshAt = Time.unscaledTime;
            _runState.HasCachedNotesHit = true;
        }

        int currentMissedNotes = requirements.NeedMissedNotes || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? ReadExactMissedNotesCount(player)
            : 0;
        bool newSong = !string.Equals(_runState.SongKey, song.SongKey, StringComparison.Ordinal);
        bool songTimeWentBackwards = _runState.SongKey == song.SongKey && songTime + 1.0 < _runState.LastSongTime;
        bool restarted = songTimeWentBackwards &&
            (LooksLikeActualRestart(songTime, score, streak, currentGhostNotes, currentOverstrums, currentMissedNotes) ||
             LooksLikeReturnToSongStart(_runState.LastSongTime, songTime));
        if ((newSong || restarted || !_runState.InRun) && (requirements.NeedRunTracking || requirements.NeedCompletedRunTracking || requirements.NeedMissedNotes))
        {
            ResetExactMissCounter(player);
            if (restarted)
            {
                currentMissedNotes = 0;
            }
        }

        bool isNearSongEnd = songDuration > 1d && songTime >= songDuration - 1.5d;
        bool shouldReadResultStats = requirements.NeedResultStats &&
            isNearSongEnd &&
            (!_runState.HasCachedResultStats ||
             Time.unscaledTime - _runState.LastResultStatsRefreshAt >= ResultStatsRefreshIntervalSeconds);
        PlayerStatsSnapshot? resultStats = shouldReadResultStats
            ? TryReadResultStats(score)
            : (requirements.NeedResultStats && isNearSongEnd ? _runState.CachedResultStats : null);
        if (shouldReadResultStats)
        {
            _runState.CachedResultStats = resultStats;
            _runState.LastResultStatsRefreshAt = Time.unscaledTime;
            _runState.HasCachedResultStats = resultStats != null;
        }
        if (resultStats != null)
        {
            score = Math.Max(score, resultStats.Score);
            if (requirements.NeedGhostNotes || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking)
            {
                MaybeCalibrateGhostNotesField(player, resultStats.GhostNotes);
                currentGhostNotes = ConvertToInt32(_ghostNotesField?.GetValue(player));
                currentGhostNotes = Math.Max(currentGhostNotes, ConvertToInt32(_ghostInputsProperty?.GetValue(player, null)));
                currentGhostNotes = Math.Max(currentGhostNotes, ConvertToInt32(_fretsBetweenNotesField?.GetValue(player)));
                if (_ghostNotesFieldCalibrated &&
                    resultStats.GhostNotes > 0 &&
                    currentGhostNotes != resultStats.GhostNotes)
                {
                    _ghostNotesFieldCalibrated = false;
                    MaybeCalibrateGhostNotesField(player, resultStats.GhostNotes);
                    currentGhostNotes = ConvertToInt32(_ghostNotesField?.GetValue(player));
                }
                currentGhostNotes = Math.Max(currentGhostNotes, resultStats.GhostNotes);
            }

            if (requirements.NeedOverstrums || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking)
            {
                currentOverstrums = Math.Max(currentOverstrums, resultStats.Overstrums);
            }
        }

        List<SectionDescriptor> sections;
        if (requirements.NeedSections || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking)
        {
            sections = BuildSections(chart, songEntry, song.SongKey, song.SongSpeedPercent, songDuration);
        }
        else
        {
            sections = new List<SectionDescriptor>();
        }
        if (sections.Count > 0)
        {
            songConfig = EnsureSongConfig(song, sections);
            _runState.CachedSongConfig = songConfig;
            _runState.CachedSongConfigKey = song.SongKey;
        }

        string currentSectionName = requirements.NeedCurrentSection || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? GetCurrentSectionName(sections, currentSectionSongTime, currentChartTick)
            : string.Empty;
        LogTimingDiagnostics(song, sections, songTime, currentSectionSongTime, songDuration, isPractice, currentChartTick);
        SongMemory songMemory =
            requirements.NeedSongMemory
                ? (_runState.CachedSongMemory != null &&
                    string.Equals(_runState.CachedSongMemoryKey, song.SongKey, StringComparison.Ordinal)
                        ? _runState.CachedSongMemory
                        : EnsureSongMemory(song, sections))
                : new SongMemory
                {
                    Title = song.Title,
                    Artist = song.Artist,
                    Charter = song.Charter
                };
        if (requirements.NeedSongMemory)
        {
            _runState.CachedSongMemory = songMemory;
            _runState.CachedSongMemoryKey = song.SongKey;
        }

        if (requirements.NeedRunTracking || requirements.NeedCompletedRunTracking)
        {
            UpdateRunTracking(song, songMemory, songConfig, currentSectionName, songTime, songDuration, score, streak, currentGhostNotes, currentOverstrums, currentMissedNotes, currentNotesHit, resultStats, isPractice, requirements.NeedRunTracking, requirements.NeedCompletedRunTracking);
            SaveMemory();
        }
        else
        {
            UpdateMinimalRunState(song.SongKey, currentSectionName, songTime, streak, currentGhostNotes, currentOverstrums, currentMissedNotes);
        }

        SaveConfig();
        SectionSnapshotCache sectionSnapshot = requirements.NeedSections
            ? GetOrBuildSectionSnapshot(song.SongKey, sections, songConfig, songMemory)
            : new SectionSnapshotCache();
        List<TrackedSectionState> trackedSections = sectionSnapshot.TrackedSections;
        List<SectionStatsState> sectionStats = sectionSnapshot.SectionStats;
        Dictionary<string, SectionStatsState> sectionStatsByName = sectionSnapshot.SectionStatsByName;
        EnabledTextExportSnapshot enabledTextExports = GetEnabledTextExportsSnapshot();
        bool noteSplitEnabled = IsTextExportEnabled(enabledTextExports, NoteSplitModeExportKey);
        SectionStatsState? currentSectionStats = string.IsNullOrWhiteSpace(currentSectionName)
            ? null
            : (sectionStatsByName.TryGetValue(currentSectionName, out SectionStatsState? currentStats) ? currentStats : null);
        List<NoteSplitSectionState> noteSplitSections;
        if (noteSplitEnabled)
        {
            noteSplitSections = GetOrBuildNoteSplitSections(song.SongKey, sections, songMemory, currentSectionName);
        }
        else
        {
            noteSplitSections = new List<NoteSplitSectionState>();
        }
        GetSongPersonalBestRun(songMemory, out int? songPersonalBestMissCount, out int? songPersonalBestOverstrums);

        return new TrackerState
        {
            IsInSong = true,
            OverlayEditorVisible = _overlayEditorVisible,
            IsPracticeMode = isPractice,
            Score = score,
            Streak = streak,
            BestStreak = songMemory.BestStreak,
            CurrentSection = currentSectionName,
            SongTime = songTime,
            SongDuration = songDuration,
            Song = song,
            Sections = sections,
            TrackedSections = trackedSections,
            SectionStats = sectionStats,
            SectionStatsByName = sectionStatsByName,
            CurrentSectionStats = currentSectionStats,
            CompletedRuns = requirements.NeedCompletedRuns ? GetOrBuildCompletedRunsSnapshot(song.SongKey, songMemory) : new List<CompletedRunRecord>(),
            Attempts = songMemory.Attempts,
            Starts = songMemory.Starts,
            Restarts = songMemory.Restarts,
            CurrentGhostedNotes = currentGhostNotes,
            CurrentOverstrums = currentOverstrums,
            CurrentMissedNotes = currentMissedNotes,
            LifetimeGhostedNotes = songMemory.LifetimeGhostedNotes,
            GlobalLifetimeGhostedNotes = _memory.LifetimeGhostedNotes,
            FcAchieved = songMemory.FcAchieved,
            NoteSplitModeEnabled = noteSplitEnabled,
            PreviousSection = _runState.NoteSplitPreviousSection,
            PreviousSectionMissCount = _runState.NoteSplitPreviousSectionMissCount,
            SongPersonalBestMissCount = songPersonalBestMissCount,
            SongPersonalBestOverstrums = songPersonalBestOverstrums,
            PreviousSectionResultKind = _runState.NoteSplitPreviousSectionResultKind,
            NoteSplitSections = noteSplitSections,
            EnabledTextExports = enabledTextExports
        };
    }

    private object? GetPreferredActivePlayer(object gameManager)
    {
        object? mainPlayer = _mainPlayerField?.GetValue(gameManager);
        if (mainPlayer != null && IsLocalEnabledPlayer(mainPlayer))
        {
            return mainPlayer;
        }

        Array? players = _playersField?.GetValue(gameManager) as Array;
        if (players == null)
        {
            return mainPlayer;
        }

        foreach (object? player in players)
        {
            if (player != null && IsLocalEnabledPlayer(player))
            {
                return player;
            }
        }

        return players.Cast<object?>().FirstOrDefault(player => player != null);
    }

    private void CachePlayerStatsFields(object player)
    {
        Type playerType = player.GetType();
        string typeName = playerType.FullName ?? playerType.Name;
        if (string.Equals(_playerTypeCachedForStats, typeName, StringComparison.Ordinal) &&
            _scoreField != null &&
            _comboField != null &&
            _ghostNotesField != null &&
            _overstrumsField != null &&
            _notesHitField != null)
        {
            return;
        }
        // In v1, different instruments/players can be subclasses of BasePlayer with the obfuscated stat fields.
        // Resolve these against the runtime type so we don't get stuck reading null/0 forever.
        FieldInfo? score = FindField(playerType,
            BasePlayerScoreFieldName,
            "\u00ca\u00bc\u00ca\u00bc\u00cb\u0081\u00ca\u00b4\u00ca\u00bf\u00ca\u00b8\u00ca\u00bf\u00ca\u00b2\u00ca\u00ba\u00ca\u00b3\u00ca\u00b5");
        FieldInfo? combo = FindField(playerType,
            "\u00ca\u00b2\u00ca\u00ba\u00ca\u00ba\u00ca\u00bf\u00ca\u00b3\u00ca\u00b2\u00ca\u00be\u00ca\u00b2\u00ca\u00bd\u00ca\u00bc\u00ca\u00bf",
            BasePlayerComboFieldName);
        FieldInfo? ghosts = FindField(playerType,
            "\u02b2\u02c0\u02b5\u02bb\u02bf\u02b4\u02b9\u02b5\u02bf\u02bc\u02bb",
            "\u00ca\u00b2\u00cb\u20ac\u00ca\u00b5\u00ca\u00bb\u00ca\u00bf\u00ca\u00b4\u00ca\u00b9\u00ca\u00b5\u00ca\u00bf\u00ca\u00bc\u00ca\u00bb");
        FieldInfo? fretsBetweenNotes = FindField(playerType,
            "\u02b7\u02c0\u02b3\u02b2\u02b6\u02b7\u02b4\u02bc\u02b6\u02ba\u02c0",
            "\u00ca\u00b7\u00cb\u20ac\u00ca\u00b3\u00ca\u00b2\u00ca\u00b6\u00ca\u00b7\u00ca\u00b4\u00ca\u00bc\u00ca\u00b6\u00ca\u00ba\u00cb\u20ac");
        FieldInfo? overs = FindField(playerType,
            "\u00ca\u00b7\u00ca\u00b8\u00ca\u00bd\u00cb\u20ac\u00ca\u00be\u00ca\u00bb\u00ca\u00b9\u00ca\u00b7\u00ca\u00ba\u00cb\u20ac\u00ca\u00bd",
            "ʷʸʽˀʾʻʹʷʺˀʽ",
            "overstrums");
        FieldInfo? notesHit = FindField(playerType,
            "\u02b3\u02b5\u02c1\u02b7\u02c1\u02c1\u02ba\u02b2\u02b5\u02bd\u02b7",
            "\u00ca\u00b3\u00ca\u00b5\u00cb\u0081\u00ca\u00b7\u00cb\u0081\u00cb\u0081\u00ca\u00ba\u00ca\u00b2\u00ca\u00b5\u00ca\u00bd\u00ca\u00b7",
            "ʳʵˁʷˁˁʺʲʵʽʷ");
        PropertyInfo? ghostInputs = FindProperty(playerType,
            "\u02bb\u02bd\u02b5\u02b2\u02bf\u02b8\u02c1\u02b3\u02b4\u02b3\u02b7");
        if (score != null) _scoreField = score;
        if (combo != null) _comboField = combo;
        if (ghosts != null) _ghostNotesField = ghosts;
        if (fretsBetweenNotes != null) _fretsBetweenNotesField = fretsBetweenNotes;
        if (overs != null) _overstrumsField = overs;
        if (notesHit != null) _notesHitField = notesHit;
        if (ghostInputs != null) _ghostInputsProperty = ghostInputs;
        _playerTypeCachedForStats = typeName;
        StockTrackerLog.WriteDebug(
            "PlayerStatsFields | type=" + typeName +
            " | score=" + (_scoreField?.Name ?? "<null>") +
            " | combo=" + (_comboField?.Name ?? "<null>") +
            " | ghosts=" + (_ghostNotesField?.Name ?? "<null>") +
            " | fretsBetweenNotes=" + (_fretsBetweenNotesField?.Name ?? "<null>") +
            " | ghostInputsProp=" + (_ghostInputsProperty?.Name ?? "<null>") +
            " | overstrums=" + (_overstrumsField?.Name ?? "<null>") +
            " | notesHit=" + (_notesHitField?.Name ?? "<null>"));
    }

    public void ResetExactMissCounter(object player)
    {
        if (player == null)
        {
            return;
        }

        FindOrCreateExactMissCounter(player).MissedNotes = 0;
    }

    public void RecordExactMiss(object player, object note)
    {
        if (player == null || note == null || !ShouldCountExactMiss(note))
        {
            return;
        }

        FindOrCreateExactMissCounter(player).MissedNotes++;
        RecordNoteSplitExactMiss(note);
    }

    private int ReadExactMissedNotesCount(object player)
    {
        if (player == null)
        {
            return 0;
        }

        for (int i = _exactMissCounters.Count - 1; i >= 0; i--)
        {
            PlayerMissCounter counter = _exactMissCounters[i];
            if (!counter.Player.TryGetTarget(out object? target) || target == null)
            {
                _exactMissCounters.RemoveAt(i);
                continue;
            }

            if (ReferenceEquals(target, player))
            {
                return counter.MissedNotes;
            }
        }

        return 0;
    }

    private PlayerMissCounter FindOrCreateExactMissCounter(object player)
    {
        for (int i = _exactMissCounters.Count - 1; i >= 0; i--)
        {
            PlayerMissCounter counter = _exactMissCounters[i];
            if (!counter.Player.TryGetTarget(out object? target) || target == null)
            {
                _exactMissCounters.RemoveAt(i);
                continue;
            }

            if (ReferenceEquals(target, player))
            {
                return counter;
            }
        }

        var created = new PlayerMissCounter(player);
        _exactMissCounters.Add(created);
        return created;
    }

    private void RecordNoteSplitExactMiss(object note)
    {
        if (!_runState.InRun)
        {
            return;
        }

        if (!TryResolveSectionNameForNoteSplitEvent(note, out string sectionName))
        {
            _runState.PendingNoteSplitMisses++;
            return;
        }

        ApplyPendingNoteSplitMisses(sectionName);
        AddNoteSplitMissToSection(sectionName, 1);
    }

    private bool TryResolveSectionNameForNoteSplitEvent(object? note, out string sectionName)
    {
        sectionName = string.Empty;
        if (!TryGetNoteSplitSectionsForEvent(out IReadOnlyList<SectionDescriptor> sections))
        {
            return false;
        }

        if (TryResolveSectionNameForNoteSplitEventFromNote(note, sections, out sectionName))
        {
            return true;
        }

        if (_activeGameManager == null)
        {
            return false;
        }

        double rawSongTime = ConvertToDouble(_songTimeField?.GetValue(_activeGameManager));
        double songTime = ReadCurrentSectionSongTime(_activeGameManager, rawSongTime);
        int currentChartTick = ReadCurrentChartTick(_activeGameManager);
        sectionName = GetCurrentSectionName(sections, songTime, currentChartTick);
        return !string.IsNullOrWhiteSpace(sectionName);
    }

    private bool TryGetNoteSplitSectionsForEvent(out IReadOnlyList<SectionDescriptor> sections)
    {
        sections = Array.Empty<SectionDescriptor>();
        string songKey =
            _runState.CachedSongDescriptor?.SongKey ??
            _latestState.Song?.SongKey ??
            _runState.SongKey;
        if (string.IsNullOrWhiteSpace(songKey))
        {
            return false;
        }

        if (_songSectionsCache.TryGetValue(songKey, out List<SectionDescriptor>? cachedSections) &&
            cachedSections != null &&
            cachedSections.Count > 0)
        {
            sections = cachedSections;
        }
        else if (_latestState.Sections.Count > 0)
        {
            sections = _latestState.Sections;
        }

        if (sections == null || sections.Count == 0)
        {
            return false;
        }

        return true;
    }

    private bool TryResolveSectionNameForNoteSplitEventFromNote(object? note, IReadOnlyList<SectionDescriptor> sections, out string sectionName)
    {
        sectionName = string.Empty;
        if (note == null)
        {
            return false;
        }

        int noteTick = TryReadInt32Member(note, NoteTickPropertyName) ?? -1;
        double noteSongTime = TryReadDoubleMember(note, NoteStartTimePropertyName) ?? -1d;
        if (noteTick < 0 &&
            (double.IsNaN(noteSongTime) || double.IsInfinity(noteSongTime) || noteSongTime < 0d))
        {
            return false;
        }

        sectionName = GetCurrentSectionName(sections, noteSongTime, noteTick);
        return !string.IsNullOrWhiteSpace(sectionName);
    }

    private double ReadCurrentSectionSongTime(object gameManager, double fallbackSongTime)
    {
        try
        {
            double playbackSongTime = ConvertToDouble(_songPlaybackTimeField?.GetValue(gameManager));
            if (!double.IsNaN(playbackSongTime) &&
                !double.IsInfinity(playbackSongTime) &&
                playbackSongTime >= 0d)
            {
                return playbackSongTime;
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"PlaybackSongTimeReadFailure | {ex.Message}");
        }

        return fallbackSongTime;
    }

    private int ReadCurrentChartTick(object gameManager)
    {
        try
        {
            object? rawValue = _currentSectionTickField?.GetValue(gameManager);
            int chartTick = rawValue switch
            {
                uint value => checked((int)value),
                int value => value,
                _ => -1
            };

            return chartTick >= 0 ? chartTick : -1;
        }
        catch (Exception ex)
        {
            LogVerbose($"CurrentChartTickReadFailure | {ex.Message}");
            return -1;
        }
    }

    private void LogTimingDiagnostics(SongDescriptor song, IReadOnlyList<SectionDescriptor> sections, double progressSongTime, double playbackSongTime, double songDuration, bool isPractice, int currentChartTick)
    {
        if (song.SongSpeedPercent == 100 && !isPractice)
        {
            return;
        }

        string timingKey = $"{song.SongKey}|{song.SongSpeedPercent}|{(isPractice ? 1 : 0)}";
        if (!string.Equals(_lastTimingSectionDumpKey, timingKey, StringComparison.Ordinal))
        {
            _lastTimingSectionDumpKey = timingKey;
            StockTrackerLog.Write(
                "TimingDiagSections | " +
                $"songKey={song.SongKey} " +
                $"title={song.Title} " +
                $"speed={song.SongSpeedPercent} " +
                $"practice={(isPractice ? 1 : 0)} " +
                $"count={sections.Count}");

            for (int i = 0; i < sections.Count; i++)
            {
                SectionDescriptor section = sections[i];
                StockTrackerLog.Write(
                    "TimingDiagSection | " +
                    $"songKey={song.SongKey} " +
                    $"speed={song.SongSpeedPercent} " +
                    $"index={section.Index} " +
                    $"name={GetSectionDisplayName(section)} " +
                    $"tick={section.Tick} " +
                    $"start={section.StartTime.ToString("0.000", CultureInfo.InvariantCulture)}");
            }
        }

        if (string.Equals(_lastTimingDiagnosticsSongKey, timingKey, StringComparison.Ordinal) &&
            Time.unscaledTime - _lastTimingDiagnosticsAt < TimingDiagnosticsIntervalSeconds)
        {
            return;
        }

        _lastTimingDiagnosticsSongKey = timingKey;
        _lastTimingDiagnosticsAt = Time.unscaledTime;

        string progressSection = GetCurrentSectionName(sections, progressSongTime, -1);
        string playbackSection = GetCurrentSectionName(sections, playbackSongTime, -1);
        string tickSection = GetCurrentSectionName(sections, playbackSongTime, currentChartTick);
        double delta = playbackSongTime - progressSongTime;
        string clockSource = _songPlaybackTimeField != null ? "playback" : "progress_fallback";
        StockTrackerLog.Write(
            "TimingDiagTick | " +
            $"songKey={song.SongKey} " +
            $"speed={song.SongSpeedPercent} " +
            $"practice={(isPractice ? 1 : 0)} " +
            $"clockSource={clockSource} " +
            $"tick={currentChartTick} " +
            $"progress={progressSongTime.ToString("0.000", CultureInfo.InvariantCulture)} " +
            $"playback={playbackSongTime.ToString("0.000", CultureInfo.InvariantCulture)} " +
            $"delta={delta.ToString("0.000", CultureInfo.InvariantCulture)} " +
            $"duration={songDuration.ToString("0.000", CultureInfo.InvariantCulture)} " +
            $"progressSection={progressSection} " +
            $"playbackSection={playbackSection} " +
            $"tickSection={tickSection}");
    }

    private void ApplyPendingNoteSplitMisses(string sectionName)
    {
        if (_runState.PendingNoteSplitMisses <= 0 || string.IsNullOrWhiteSpace(sectionName))
        {
            return;
        }

        AddNoteSplitMissToSection(sectionName, _runState.PendingNoteSplitMisses);
        _runState.PendingNoteSplitMisses = 0;
    }

    private void AddNoteSplitMissToSection(string sectionName, int missCount)
    {
        if (string.IsNullOrWhiteSpace(sectionName) || missCount <= 0)
        {
            return;
        }

        if (_runState.NoteSplitMissCountsBySectionThisRun.TryGetValue(sectionName, out int existingMissCount))
        {
            _runState.NoteSplitMissCountsBySectionThisRun[sectionName] = existingMissCount + missCount;
        }
        else
        {
            _runState.NoteSplitMissCountsBySectionThisRun[sectionName] = missCount;
        }
    }

    private static bool ShouldCountExactMiss(object note)
    {
        return !TryGetBooleanMember(note, NoteSlavePropertyName);
    }

    private static bool TryGetBooleanMember(object obj, string encodedName, string? fallbackName = null)
    {
        string[] names = string.IsNullOrEmpty(fallbackName) ? new[] { encodedName } : new[] { encodedName, fallbackName! };
        FieldInfo? field = FindField(obj.GetType(), names);
        if (field != null)
        {
            return ConvertToBoolean(field.GetValue(obj));
        }

        PropertyInfo? property = FindProperty(obj.GetType(), names);
        if (property != null)
        {
            return ConvertToBoolean(SafeGetPropertyValue(property, obj));
        }

        return false;
    }

    private static int? TryReadInt32Member(object obj, string encodedName, string? fallbackName = null)
    {
        string[] names = string.IsNullOrEmpty(fallbackName) ? new[] { encodedName } : new[] { encodedName, fallbackName! };
        FieldInfo? field = FindField(obj.GetType(), names);
        if (field != null)
        {
            return ConvertToInt32(field.GetValue(obj));
        }

        PropertyInfo? property = FindProperty(obj.GetType(), names);
        if (property != null)
        {
            return ConvertToInt32(SafeGetPropertyValue(property, obj));
        }

        return null;
    }

    private static double? TryReadDoubleMember(object obj, string encodedName, string? fallbackName = null)
    {
        string[] names = string.IsNullOrEmpty(fallbackName) ? new[] { encodedName } : new[] { encodedName, fallbackName! };
        FieldInfo? field = FindField(obj.GetType(), names);
        if (field != null)
        {
            return ConvertToDouble(field.GetValue(obj));
        }

        PropertyInfo? property = FindProperty(obj.GetType(), names);
        if (property != null)
        {
            return ConvertToDouble(SafeGetPropertyValue(property, obj));
        }

        return null;
    }

    private static FieldInfo? FindField(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            FieldInfo? field = GetAllFields(type).FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (field != null)
            {
                return field;
            }
        }

        return null;
    }

    private static PropertyInfo? FindProperty(Type type, params string[] names)
    {
        foreach (string name in names)
        {
            PropertyInfo? property = GetAllProperties(type).FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (property != null)
            {
                return property;
            }
        }

        return null;
    }

    private PlayerStatsSnapshot? TryReadResultStats(int score)
    {
        if (score <= 0)
        {
            return null;
        }

        CacheResultStatsReflection();
        if (_resultStatsArrayProperty == null || _playerStatsType == null)
        {
            return null;
        }

        try
        {
            if (_resultStatsArrayProperty.GetValue(null, null) is not Array statsArray)
            {
                return null;
            }

            List<PlayerStatsSnapshot> candidates = new();
            foreach (object? entry in statsArray)
            {
                if (entry == null || !_playerStatsType.IsInstanceOfType(entry))
                {
                    continue;
                }

                bool isRemote = ConvertToBoolean(_playerStatsIsRemoteField?.GetValue(entry));
                if (isRemote)
                {
                    continue;
                }

                int candidateScore = ConvertToInt32(_playerStatsScoreField?.GetValue(entry));
                PlayerStatsSnapshot snapshot = new()
                {
                    Score = candidateScore,
                    NotesHit = ConvertToInt32(_playerStatsNotesHitField?.GetValue(entry)),
                    TotalNotes = ConvertToInt32(_playerStatsTotalNotesField?.GetValue(entry)),
                    Overstrums = ConvertToInt32(_playerStatsOverstrumsField?.GetValue(entry)),
                    GhostNotes = ConvertToInt32(_playerStatsGhostNotesField?.GetValue(entry)),
                    Accuracy = ConvertToInt32(_playerStatsAccuracyProperty == null ? null : SafeGetPropertyValue(_playerStatsAccuracyProperty, entry)),
                    AccuracyString = _playerStatsAccuracyStringProperty == null ? null : SafeGetPropertyValue(_playerStatsAccuracyStringProperty, entry) as string
                };

                if (candidateScore == score)
                {
                    return snapshot;
                }

                candidates.Add(snapshot);
            }

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (score > 0)
            {
                PlayerStatsSnapshot best = candidates[0];
                int bestDiff = Math.Abs(best.Score - score);
                for (int i = 1; i < candidates.Count; i++)
                {
                    PlayerStatsSnapshot candidate = candidates[i];
                    int diff = Math.Abs(candidate.Score - score);
                    if (diff < bestDiff || (diff == bestDiff && candidate.TotalNotes > best.TotalNotes))
                    {
                        best = candidate;
                        bestDiff = diff;
                    }
                }

                return best;
            }

            PlayerStatsSnapshot bestNoScore = candidates[0];
            for (int i = 1; i < candidates.Count; i++)
            {
                PlayerStatsSnapshot candidate = candidates[i];
                if (candidate.TotalNotes > bestNoScore.TotalNotes ||
                    (candidate.TotalNotes == bestNoScore.TotalNotes && candidate.Score > bestNoScore.Score))
                {
                    bestNoScore = candidate;
                }
            }

            return bestNoScore;
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"ResultStatsReadFailure | {ex.Message}");
        }

        return null;
    }

    private void CacheResultStatsReflection()
    {
        if (_resultStatsReflectionScanCompleted)
        {
            return;
        }

        if (_resultStatsArrayProperty != null && _playerStatsType != null)
        {
            _resultStatsReflectionScanCompleted = true;
            return;
        }

        if (Time.unscaledTime - _lastResultStatsReflectionScanAt < 2f)
        {
            return;
        }

        _lastResultStatsReflectionScanAt = Time.unscaledTime;

        Assembly? assembly = _gameManagerType?.Assembly;
        if (assembly == null)
        {
            return;
        }

        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = GetReflectionTypeLoadableTypes(ex);
            Exception? firstLoaderException = GetFirstLoaderException(ex);
            if (firstLoaderException != null)
            {
                StockTrackerLog.Write($"ResultStatsTypeLoadPartial | {firstLoaderException.GetType().Name} | {firstLoaderException.Message}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"ResultStatsTypeLoadFailure | {ex.GetType().Name} | {ex.Message}");
            _resultStatsReflectionScanCompleted = true;
            return;
        }

        foreach (Type type in types)
        {
            IEnumerable<PropertyInfo> properties;
            try
            {
                List<PropertyInfo> all = new();
                foreach (PropertyInfo property in GetAllProperties(type))
                {
                    all.Add(property);
                }

                properties = all;
            }
            catch
            {
                continue;
            }

            foreach (PropertyInfo property in properties)
            {
                MethodInfo? getter = null;
                try
                {
                    getter = property.GetGetMethod(true);
                }
                catch
                {
                    getter = null;
                }

                if (getter == null || !getter.IsStatic)
                {
                    continue;
                }

                if (!property.PropertyType.IsArray)
                {
                    continue;
                }

                Type? elementType = property.PropertyType.GetElementType();
                if (!LooksLikePlayerStatsType(elementType))
                {
                    continue;
                }

                _resultStatsArrayProperty = property;
                _playerStatsType = elementType;
                _playerStatsScoreField = elementType!.GetField("score", AnyInstance);
                _playerStatsNotesHitField = elementType.GetField("notesHit", AnyInstance);
                _playerStatsTotalNotesField = elementType.GetField("totalNotes", AnyInstance);
                _playerStatsOverstrumsField = elementType.GetField("overstrums", AnyInstance);
                _playerStatsGhostNotesField = elementType.GetField("ghostNotes", AnyInstance);
                _playerStatsIsRemoteField = elementType.GetField("isRemotePlayer", AnyInstance);
                _playerStatsAccuracyProperty = elementType.GetProperty("Accuracy", AnyInstance);
                _playerStatsAccuracyStringProperty = elementType.GetProperty("AccuracyString", AnyInstance);
                StockTrackerLog.WriteDebug($"ResultStatsArrayPropertyFound | {type.FullName}.{property.Name}");
                _resultStatsReflectionScanCompleted = true;
                return;
            }
        }

        _resultStatsReflectionScanCompleted = true;
    }

    private static Type[] GetReflectionTypeLoadableTypes(ReflectionTypeLoadException ex)
    {
        try
        {
            PropertyInfo? typesProperty = ex.GetType().GetProperty("Types", BindingFlags.Instance | BindingFlags.Public);
            if (typesProperty?.GetValue(ex, null) is Array typesArray)
            {
                List<Type> loadedTypes = new();
                foreach (object? candidate in typesArray)
                {
                    if (candidate is Type type)
                    {
                        loadedTypes.Add(type);
                    }
                }

                return loadedTypes.ToArray();
            }
        }
        catch
        {
        }

        return Array.Empty<Type>();
    }

    private static Exception? GetFirstLoaderException(ReflectionTypeLoadException ex)
    {
        try
        {
            PropertyInfo? loaderExceptionsProperty = ex.GetType().GetProperty("LoaderExceptions", BindingFlags.Instance | BindingFlags.Public);
            if (loaderExceptionsProperty?.GetValue(ex, null) is Array loaderExceptions)
            {
                foreach (object? candidate in loaderExceptions)
                {
                    if (candidate is Exception loaderException)
                    {
                        return loaderException;
                    }
                }
            }
        }
        catch
        {
        }

        return null;
    }

    private void MaybeCalibrateGhostNotesField(object player, int expectedGhostNotes)
    {
        if (_ghostNotesFieldCalibrated || expectedGhostNotes <= 0 || _basePlayerType == null)
        {
            return;
        }

        try
        {
            List<FieldInfo> matches = new();
            foreach (FieldInfo field in GetAllFields(_basePlayerType))
            {
                if (field.FieldType != typeof(int))
                {
                    continue;
                }

                int value = ConvertToInt32(field.GetValue(player));
                if (value == expectedGhostNotes)
                {
                    matches.Add(field);
                }
            }

            if (matches.Count == 0)
            {
                return;
            }

            FieldInfo? selected = matches.FirstOrDefault(field =>
                field != _scoreField &&
                field != _comboField &&
                field != _overstrumsField);

            if (selected == null && matches.Count >= 1)
            {
                selected = matches[0];
                StockTrackerLog.WriteDebug($"GhostNotesFieldAmbiguous | candidates={matches.Count} | selected={selected.Name}");
            }

            if (selected != null && selected != _ghostNotesField)
            {
                _ghostNotesField = selected;
                StockTrackerLog.WriteDebug($"GhostNotesFieldCalibrated | {_ghostNotesField.Name}");
            }
            else if (selected != null)
            {
                StockTrackerLog.WriteDebug($"GhostNotesFieldConfirmed | {_ghostNotesField?.Name ?? "<null>"}");
            }

            if (selected != null)
            {
                _ghostNotesFieldCalibrated = true;
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"GhostNotesFieldCalibrationFailure | {ex.Message}");
        }
    }

    private static bool LooksLikePlayerStatsType(Type? type)
    {
        if (type == null)
        {
            return false;
        }

        return type.GetField("score", AnyInstance) != null
            && type.GetField("notesHit", AnyInstance) != null
            && type.GetField("totalNotes", AnyInstance) != null
            && type.GetField("overstrums", AnyInstance) != null
            && type.GetField("ghostNotes", AnyInstance) != null;
    }

    private bool IsLocalEnabledPlayer(object player)
    {
        if (player is not Behaviour behaviour || !behaviour.enabled)
        {
            return false;
        }

        object? controller = _controllerField?.GetValue(player);
        if (controller == null)
        {
            return false;
        }

        object? settings = _playerSettingsField?.GetValue(controller);
        if (settings == null)
        {
            return true;
        }

        object? isRemote = settings.GetType().GetProperty("isRemotePlayer", AnyInstance)?.GetValue(settings, null)
            ?? settings.GetType().GetField("isRemotePlayer", AnyInstance)?.GetValue(settings);
        return !(isRemote is bool remote && remote);
    }

    private static void LogVerbose(string message)
    {
        if (VerboseLoggingEnabled)
        {
            StockTrackerLog.Write(message);
        }
    }

    private static void LogVerbose(Exception ex)
    {
        if (VerboseLoggingEnabled)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private void MarkMemoryDirty(string? songKey = null, bool affectsSectionSnapshots = true)
    {
        _memoryDirty = true;
        _memoryVersion++;
        if (affectsSectionSnapshots)
        {
            _sectionMemoryVersion++;
        }

        string? dirtySongKey = ResolveDirtyMemorySongKey(songKey);
        if (string.IsNullOrWhiteSpace(dirtySongKey))
        {
            _memoryWriteSnapshotRequiresFullRefresh = true;
            return;
        }

        _dirtyMemorySongKeys.Add(dirtySongKey!);
    }

    private void MarkConfigDirty(string? songKey = null, bool affectsSectionSnapshots = false, bool affectsTextExports = false, bool affectsGlobalSnapshot = false, bool affectsExportTemplates = false)
    {
        _configDirty = true;
        _configVersion++;
        _overlayConfigVersion++;
        if (affectsSectionSnapshots)
        {
            _sectionConfigVersion++;
        }

        if (affectsTextExports)
        {
            _enabledTextExportsVersion++;
            _enabledTextExportSnapshotVersion = -1;
        }

        if (affectsExportTemplates)
        {
            _exportTemplateVersion++;
        }

        string? configKey = songKey ?? TryGetActiveSongConfigKey();
        if (affectsGlobalSnapshot || string.IsNullOrWhiteSpace(configKey))
        {
            _configWriteSnapshotRequiresFullRefresh = true;
            return;
        }

        _dirtyConfigSongKeys.Add(configKey!);
    }

    private string? ResolveDirtyMemorySongKey(string? songKey = null)
    {
        if (!string.IsNullOrWhiteSpace(songKey))
        {
            return songKey;
        }

        if (!string.IsNullOrWhiteSpace(_runState.CachedSongMemoryKey))
        {
            return _runState.CachedSongMemoryKey;
        }

        if (!string.IsNullOrWhiteSpace(_runState.SongKey))
        {
            return _runState.SongKey;
        }

        return null;
    }

    private string? TryGetActiveSongConfigKey()
    {
        if (_latestState?.Song != null)
        {
            return _latestState.Song.OverlayLayoutKey ?? _latestState.Song.SongKey;
        }

        if (!string.IsNullOrWhiteSpace(_runState.CachedSongConfigKey))
        {
            return _runState.CachedSongConfigKey;
        }

        if (_runState.CachedSongDescriptor != null)
        {
            return _runState.CachedSongDescriptor.OverlayLayoutKey ?? _runState.CachedSongDescriptor.SongKey;
        }

        return null;
    }

    private DifficultyInfo ReadDifficultyInfo(object? player)
    {
        if (player == null)
        {
            return DifficultyInfo.Default;
        }

        try
        {
            object? controller = _controllerField?.GetValue(player);
            object? settings = controller != null
                ? _playerSettingsField?.GetValue(controller)
                : null;
            if (settings == null)
            {
                return DifficultyInfo.Default;
            }

            object? difficultyValue = settings.GetType().GetProperty("difficulty", AnyInstance)?.GetValue(settings, null)
                ?? settings.GetType().GetField("difficulty", AnyInstance)?.GetValue(settings);
            int difficulty = ConvertToInt32(difficultyValue);
            return DifficultyInfo.FromValue(difficulty);
        }
        catch
        {
            return DifficultyInfo.Default;
        }
    }

    private bool IsBotEnabled(object? player)
    {
        if (player == null)
        {
            return false;
        }

        try
        {
            object? controller = _controllerField?.GetValue(player);
            object? settings = controller != null
                ? _playerSettingsField?.GetValue(controller)
                : null;
            if (settings == null)
            {
                return false;
            }

            object? isBotSetting = settings.GetType().GetProperty("isBot", AnyInstance)?.GetValue(settings, null)
                ?? settings.GetType().GetField("isBot", AnyInstance)?.GetValue(settings);
            if (isBotSetting == null)
            {
                return false;
            }

            object? currentValue = isBotSetting.GetType().GetProperty("CurrentValue", AnyInstance)?.GetValue(isBotSetting, null)
                ?? isBotSetting.GetType().GetField("CurrentValue", AnyInstance)?.GetValue(isBotSetting);

            return ConvertToInt32(currentValue) != 0;
        }
        catch
        {
            return false;
        }
    }

    private void LogDiagnostics(object gameManager, object? songEntry, object? player, object? chart)
    {
        if (!VerboseLoggingEnabled)
        {
            return;
        }

        if (Time.unscaledTime - _lastDiagnosticsAt < 2f)
        {
            return;
        }

        _lastDiagnosticsAt = Time.unscaledTime;
        StockTrackerLog.Write(
            "Diagnostics | " +
            $"songEntry={songEntry != null} " +
            $"player={player != null} " +
            $"chart={chart != null} " +
            $"playersField={_playersField?.Name ?? "<null>"} " +
            $"chartField={_chartField?.Name ?? "<null>"} " +
            $"gvSingleton={_globalVariablesSingletonField?.Name ?? "<null>"} " +
            $"gvSong={_globalVariablesCurrentSongField?.Name ?? "<null>"} " +
            $"controllerField={_controllerField?.Name ?? "<null>"} " +
            $"settingsField={_playerSettingsField?.Name ?? "<null>"}");

        try
        {
            foreach (FieldInfo field in gameManager.GetType().GetFields(AnyInstance))
            {
                object? value = null;
                try
                {
                    value = field.GetValue(gameManager);
                }
                catch
                {
                }

                if (value == null)
                {
                    continue;
                }

                string valueText = value is Array array
                    ? $"array[{array.Length}]<{field.FieldType.FullName}>"
                    : value.GetType().FullName ?? value.ToString() ?? "<value>";
                StockTrackerLog.Write($"GameManagerField | {field.Name} | {field.FieldType.FullName} | {valueText}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private bool IsPracticeMode(object gameManager)
    {
        try
        {
            object? practiceUi = _practiceUiField?.GetValue(gameManager);
            if (practiceUi == null)
            {
                return false;
            }

            if (practiceUi is Behaviour behaviour)
            {
                return behaviour.isActiveAndEnabled ||
                    (behaviour.gameObject != null && behaviour.gameObject.activeInHierarchy);
            }

            PropertyInfo? activeProperty = practiceUi.GetType().GetProperty("isActiveAndEnabled", AnyInstance)
                ?? practiceUi.GetType().GetProperty("enabled", AnyInstance)
                ?? practiceUi.GetType().GetProperty("activeSelf", AnyInstance)
                ?? practiceUi.GetType().GetProperty("activeInHierarchy", AnyInstance);
            if (activeProperty != null)
            {
                return ConvertToBoolean(activeProperty.GetValue(practiceUi, null));
            }

            FieldInfo? activeField = practiceUi.GetType().GetField("enabled", AnyInstance)
                ?? practiceUi.GetType().GetField("activeSelf", AnyInstance)
                ?? practiceUi.GetType().GetField("activeInHierarchy", AnyInstance);
            if (activeField != null)
            {
                return ConvertToBoolean(activeField.GetValue(practiceUi));
            }

            return true;
        }
        catch (Exception ex)
        {
            LogVerbose($"PracticeModeReadFailure | {ex.Message}");
            return false;
        }
    }

    private List<SectionDescriptor> BuildSections(object chart, object songEntry, string songKey, int songSpeedPercent, double liveSongDuration)
    {
        if (_songSectionsCache.TryGetValue(songKey, out List<SectionDescriptor>? cachedSections) && cachedSections.Count > 0)
        {
            return cachedSections;
        }

        List<SectionDescriptor> runtimeChartSections = TryBuildSectionsFromRuntimeChartSections(chart);
        if (runtimeChartSections.Count > 0)
        {
            return CacheSections(songKey, runtimeChartSections, songSpeedPercent, liveSongDuration, chart, applySpeedScaling: false);
        }

        List<SectionDescriptor> runtimeTimedSections = TryBuildSectionsFromLoadedChartTiming(chart, songEntry, songKey);
        if (runtimeTimedSections.Count > 0)
        {
            return CacheSections(songKey, runtimeTimedSections, songSpeedPercent, liveSongDuration, chart);
        }

        var sections = new List<SectionDescriptor>();
        IEnumerable? namedSections = _chartNamedSectionsField?.GetValue(chart) as IEnumerable;
        if (namedSections == null)
        {
            namedSections = FindNamedSectionsEnumerable(chart);
        }

        if (namedSections != null)
        {
            int index = 0;
            foreach (object? section in namedSections)
            {
                if (section != null)
                {
                    sections.Add(new SectionDescriptor
                    {
                        Index = index,
                        Name = ExtractSectionName(section, index),
                        StartTime = TryGetChartSectionTime(chart, index, section)
                    });
                }

                index++;
            }
        }

        if (sections.Count > 0)
        {
            return CacheSections(songKey, sections, songSpeedPercent, liveSongDuration, chart);
        }

        if (_chartSectionsMethod != null && _chartSectionTimeMethod != null)
        {
            if (_chartSectionsMethod.Invoke(chart, null) is IEnumerable rawSections)
            {
                int index = 0;
                foreach (object? section in rawSections)
                {
                    if (section != null)
                    {
                        sections.Add(new SectionDescriptor
                        {
                            Index = index,
                            Name = ExtractSectionName(section, index),
                            StartTime = TryGetChartSectionTime(chart, index, section)
                        });
                    }

                    index++;
                }
            }
        }

        if (sections.Count > 0)
        {
            return CacheSections(songKey, sections, songSpeedPercent, liveSongDuration, chart);
        }

        foreach (FieldInfo field in GetAllFields(chart.GetType()))
        {
            object? value = field.GetValue(chart);
            if (value is not IEnumerable enumerable || value is string)
            {
                continue;
            }

            var candidates = new List<object>();
            foreach (object? item in enumerable)
            {
                if (item != null)
                {
                    candidates.Add(item);
                }
            }

            if (candidates.Count == 0)
            {
                continue;
            }

            if (!LooksLikeSectionList(candidates[0].GetType()))
            {
                continue;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                sections.Add(new SectionDescriptor
                {
                    Index = i,
                    Name = ExtractSectionName(candidates[i], i),
                    StartTime = ExtractSectionTime(candidates[i], i)
                });
            }

            if (sections.Count > 0)
            {
                return CacheSections(songKey, sections, songSpeedPercent, liveSongDuration, chart);
            }
        }

        LogChartFields(chart);
        LogChartMethods(chart);
        List<SectionDescriptor> fallbackSections = BuildSectionsFromSng(songEntry, chart, songKey, songSpeedPercent, liveSongDuration);
        if (fallbackSections.Count > 0)
        {
            return CacheSections(songKey, fallbackSections, songSpeedPercent, liveSongDuration, chart);
        }

        return sections;
    }

    private List<SectionDescriptor> CacheSections(string songKey, IEnumerable<SectionDescriptor> sections, int songSpeedPercent, double liveSongDuration, object? chart = null, bool applySpeedScaling = true)
    {
        List<SectionDescriptor> normalizedSections = applySpeedScaling
            ? NormalizeSectionsForPlayback(sections, songSpeedPercent, liveSongDuration, TryGetLoadedChartBaseDuration(chart))
            : CloneSections(sections);
        List<SectionDescriptor> orderedSections = normalizedSections
            .OrderBy(section => section.StartTime)
            .ThenBy(section => section.Index)
            .ToList();
        AssignSectionDisplayNames(orderedSections);
        _songSectionsCache[songKey] = orderedSections;
        _songSectionNamesCache[songKey] = orderedSections.Select(GetSectionDisplayName).ToList();
        return orderedSections;
    }

    private static List<SectionDescriptor> NormalizeSectionsForPlayback(IEnumerable<SectionDescriptor> sections, int songSpeedPercent, double liveSongDuration, double? baseSongDuration)
    {
        double scale = 1d;
        if (baseSongDuration.GetValueOrDefault() > 1d && liveSongDuration > 1d)
        {
            scale = liveSongDuration / baseSongDuration!.Value;
        }
        else
        {
            double speedMultiplier = songSpeedPercent > 0
                ? songSpeedPercent / 100d
                : 1d;
            if (speedMultiplier > 0d)
            {
                scale = 1d / speedMultiplier;
            }
        }

        var adjustedSections = new List<SectionDescriptor>();
        foreach (SectionDescriptor section in sections)
        {
            adjustedSections.Add(new SectionDescriptor
            {
                Index = section.Index,
                Name = section.Name,
                Tick = section.Tick,
                StartTime = section.StartTime * scale
            });
        }

        return adjustedSections;
    }

    private static List<SectionDescriptor> CloneSections(IEnumerable<SectionDescriptor> sections)
    {
        var clonedSections = new List<SectionDescriptor>();
        foreach (SectionDescriptor section in sections)
        {
            clonedSections.Add(new SectionDescriptor
            {
                Index = section.Index,
                Name = section.Name,
                Tick = section.Tick,
                StartTime = section.StartTime
            });
        }

        return clonedSections;
    }

    private List<SectionDescriptor> TryBuildSectionsFromRuntimeChartSections(object chart)
    {
        IEnumerable? runtimeSections = TryGetRuntimeChartSections(chart);
        if (runtimeSections == null)
        {
            return new List<SectionDescriptor>();
        }

        var sections = new List<SectionDescriptor>();
        int index = 0;
        foreach (object? section in runtimeSections)
        {
            if (section == null)
            {
                index++;
                continue;
            }

            string name = ExtractSectionName(section, index);
            if (string.IsNullOrWhiteSpace(name))
            {
                index++;
                continue;
            }

            double startTime = TryGetRuntimeChartSectionTime(section);
            if (double.IsNaN(startTime) || double.IsInfinity(startTime))
            {
                return new List<SectionDescriptor>();
            }

            sections.Add(new SectionDescriptor
            {
                Index = index,
                Name = name,
                Tick = ExtractRuntimeChartSectionTick(section),
                StartTime = startTime
            });

            index++;
        }

        return sections;
    }

    private IEnumerable? TryGetRuntimeChartSections(object chart)
    {
        try
        {
            MethodInfo? sectionsMethod = ResolveRuntimeChartSectionsMethod(chart.GetType());
            return sectionsMethod?.Invoke(chart, null) as IEnumerable;
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
            return null;
        }
    }

    private MethodInfo? ResolveRuntimeChartSectionsMethod(Type chartType)
    {
        if (_runtimeChartSectionsMethod?.DeclaringType == chartType)
        {
            return _runtimeChartSectionsMethod;
        }

        _runtimeChartSectionsMethod = GetAllMethods(chartType)
            .Where(method => !method.IsStatic && method.GetParameters().Length == 0)
            .FirstOrDefault(method =>
            {
                if (!typeof(IEnumerable).IsAssignableFrom(method.ReturnType))
                {
                    return false;
                }

                Type? itemType = GetEnumerableItemType(method.ReturnType);
                return itemType != null && LooksLikeRuntimeChartSectionItem(itemType);
            });

        return _runtimeChartSectionsMethod;
    }

    private MethodInfo? ResolveRuntimeChartSectionTimeMethod(Type sectionType)
    {
        if (_runtimeChartSectionTimeMethod != null &&
            _runtimeChartSectionTimeMethod.DeclaringType != null &&
            _runtimeChartSectionTimeMethod.DeclaringType.IsAssignableFrom(sectionType))
        {
            return _runtimeChartSectionTimeMethod;
        }

        _runtimeChartSectionTimeMethod = GetAllMethods(sectionType)
            .FirstOrDefault(method =>
                !method.IsStatic &&
                method.GetParameters().Length == 0 &&
                (method.ReturnType == typeof(float) || method.ReturnType == typeof(double)));

        return _runtimeChartSectionTimeMethod;
    }

    private static bool LooksLikeRuntimeChartSectionItem(Type sectionType)
    {
        bool hasName = GetAllFields(sectionType).Any(field => field.FieldType == typeof(string)) ||
            GetAllProperties(sectionType).Any(property => property.PropertyType == typeof(string));
        if (!hasName)
        {
            return false;
        }

        bool hasTick = GetAllFields(sectionType).Any(field => field.FieldType == typeof(uint) || field.FieldType == typeof(int));
        if (!hasTick)
        {
            return false;
        }

        bool hasTimeMethod = GetAllMethods(sectionType).Any(method =>
            !method.IsStatic &&
            method.GetParameters().Length == 0 &&
            (method.ReturnType == typeof(float) || method.ReturnType == typeof(double)));
        if (!hasTimeMethod)
        {
            return false;
        }

        bool hasEnumFields = GetAllFields(sectionType).Any(field => field.FieldType.IsEnum);
        return !hasEnumFields;
    }

    private double TryGetRuntimeChartSectionTime(object section)
    {
        try
        {
            MethodInfo? timeMethod = ResolveRuntimeChartSectionTimeMethod(section.GetType());
            if (timeMethod != null)
            {
                return ConvertToDouble(timeMethod.Invoke(section, null));
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }

        return ExtractSectionTime(section, 0);
    }

    private static int ExtractRuntimeChartSectionTick(object section)
    {
        object? tickValue = GetAllFields(section.GetType())
            .Where(field => field.FieldType == typeof(uint))
            .Select(field => field.GetValue(section))
            .FirstOrDefault();
        if (tickValue != null)
        {
            return ConvertToInt32(tickValue);
        }

        tickValue = GetAllFields(section.GetType())
            .Where(field => field.FieldType == typeof(int))
            .Select(field => field.GetValue(section))
            .FirstOrDefault();
        return tickValue != null ? ConvertToInt32(tickValue) : -1;
    }

    private List<SectionDescriptor> TryBuildSectionsFromLoadedChartTiming(object chart, object songEntry, string songKey)
    {
        MethodInfo? tickToTimeMethod = ResolveLoadedChartTickToTimeMethod(chart.GetType());
        if (tickToTimeMethod == null)
        {
            return new List<SectionDescriptor>();
        }

        List<SectionDescriptor> sourceSections = ExtractSectionsFromSongEntry(songEntry, songKey)
            .Where(section => !string.IsNullOrWhiteSpace(section.Name) && section.Tick >= 0)
            .ToList();
        if (sourceSections.Count == 0)
        {
            return new List<SectionDescriptor>();
        }

        var sections = new List<SectionDescriptor>(sourceSections.Count);
        foreach (SectionDescriptor section in sourceSections)
        {
            object? rawStartTime = tickToTimeMethod.Invoke(chart, new object[] { (uint)section.Tick });
            double startTime = ConvertToDouble(rawStartTime);
            if (double.IsNaN(startTime) || double.IsInfinity(startTime))
            {
                return new List<SectionDescriptor>();
            }

            sections.Add(new SectionDescriptor
            {
                Index = section.Index,
                Name = section.Name,
                Tick = section.Tick,
                StartTime = startTime
            });
        }

        return sections;
    }

    private MethodInfo? ResolveLoadedChartTickToTimeMethod(Type chartType)
    {
        if (_loadedChartTickToTimeMethod?.DeclaringType == chartType)
        {
            return _loadedChartTickToTimeMethod;
        }

        _loadedChartTickToTimeMethod = GetAllMethods(chartType)
            .FirstOrDefault(method =>
                !method.IsStatic &&
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(uint) &&
                (method.ReturnType == typeof(float) || method.ReturnType == typeof(double)));

        return _loadedChartTickToTimeMethod;
    }

    private MethodInfo? ResolveLoadedChartMaxTickMethod(Type chartType)
    {
        if (_loadedChartMaxTickMethod?.DeclaringType == chartType)
        {
            return _loadedChartMaxTickMethod;
        }

        _loadedChartMaxTickMethod = GetAllMethods(chartType)
            .FirstOrDefault(method =>
                !method.IsStatic &&
                method.GetParameters().Length == 0 &&
                (method.ReturnType == typeof(uint) || method.ReturnType == typeof(int)));

        return _loadedChartMaxTickMethod;
    }

    private double? TryGetLoadedChartBaseDuration(object? chart)
    {
        if (chart == null)
        {
            return null;
        }

        try
        {
            MethodInfo? tickToTimeMethod = ResolveLoadedChartTickToTimeMethod(chart.GetType());
            MethodInfo? maxTickMethod = ResolveLoadedChartMaxTickMethod(chart.GetType());
            if (tickToTimeMethod == null || maxTickMethod == null)
            {
                return null;
            }

            object? rawMaxTick = maxTickMethod.Invoke(chart, null);
            uint maxTick = rawMaxTick switch
            {
                uint value => value,
                int value when value >= 0 => (uint)value,
                _ => 0u
            };

            if (maxTick == 0u)
            {
                return null;
            }

            double baseDuration = ConvertToDouble(tickToTimeMethod.Invoke(chart, new object[] { maxTick }));
            if (double.IsNaN(baseDuration) || double.IsInfinity(baseDuration) || baseDuration <= 0d)
            {
                return null;
            }

            return baseDuration;
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
            return null;
        }
    }

    private static void AssignSectionDisplayNames(List<SectionDescriptor> sections)
    {
        var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SectionDescriptor section in sections)
        {
            string baseName = string.IsNullOrWhiteSpace(section.Name) ? "Section" : section.Name;
            int occurrence = duplicateCounts.TryGetValue(baseName, out int count) ? count + 1 : 1;
            duplicateCounts[baseName] = occurrence;
            section.DisplayName = occurrence <= 1 ? baseName : $"{baseName} ({occurrence})";
        }
    }

    private static string GetSectionDisplayName(SectionDescriptor section)
    {
        return string.IsNullOrWhiteSpace(section.DisplayName) ? section.Name : section.DisplayName;
    }

    private IEnumerable? FindNamedSectionsEnumerable(object chart)
    {
        foreach (FieldInfo field in GetAllFields(chart.GetType()))
        {
            object? value = field.GetValue(chart);
            if (value is not IEnumerable enumerable || value is string || value is Array)
            {
                continue;
            }

            Type? itemType = GetEnumerableItemType(field.FieldType);
            if (itemType == null || !LooksLikeSectionList(itemType))
            {
                continue;
            }

            _chartNamedSectionsField = field;
            return enumerable;
        }

        return null;
    }

    private List<SectionDescriptor> BuildSectionsFromSng(object songEntry, object runtimeChart, string songKey, int songSpeedPercent, double liveSongDuration)
    {
        try
        {
            List<SectionDescriptor> sections = ExtractSectionsFromSongEntry(songEntry, songKey);
            if (sections.Count == 0)
            {
                return new List<SectionDescriptor>();
            }

            return CacheSections(songKey, sections, songSpeedPercent, liveSongDuration, runtimeChart);
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
            return new List<SectionDescriptor>();
        }
    }

    private List<string> GetSectionNamesFromSongEntry(object songEntry, string songKey)
    {
        if (_songSectionNamesCache.TryGetValue(songKey, out List<string>? cachedNames) && cachedNames.Count > 0)
        {
            return cachedNames;
        }

        string? folderPath = GetStringField(songEntry, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            LogVerbose($"SngLookup | songKey={songKey} folderPath=<empty>");
            return new List<string>();
        }

        string resolvedFolderPath = folderPath!;
        LogVerbose($"SngLookup | songKey={songKey} folderPath={resolvedFolderPath}");
        List<string> names;
        if (resolvedFolderPath.EndsWith(".sng", StringComparison.OrdinalIgnoreCase))
        {
            names = ExtractSectionsFromSongEntryContainer(songEntry, songKey)
                .Select(section => section.Name)
                .ToList();
            if (names.Count == 0)
            {
                names = ExtractSectionNamesFromSng(resolvedFolderPath);
            }
        }
        else
        {
            names = ExtractSectionNamesFromChartFolder(resolvedFolderPath);
        }

        LogVerbose($"SngLookupResult | songKey={songKey} count={names.Count}");
        if (names.Count > 0)
        {
            _songSectionNamesCache[songKey] = names;
        }

        return names;
    }

    private List<SectionDescriptor> ExtractSectionsFromSongEntry(object songEntry, string songKey)
    {
        if (_songSectionsCache.TryGetValue(songKey, out List<SectionDescriptor>? cachedSections) && cachedSections.Count > 0)
        {
            return cachedSections;
        }

        string? folderPath = GetStringField(songEntry, "folderPath");
        if (string.IsNullOrWhiteSpace(folderPath))
        {
            return new List<SectionDescriptor>();
        }

        string resolvedFolderPath = folderPath!;
        List<SectionDescriptor> sections;
        if (resolvedFolderPath.EndsWith(".sng", StringComparison.OrdinalIgnoreCase))
        {
            sections = ExtractSectionsFromSongEntryContainer(songEntry, songKey);
            if (sections.Count == 0)
            {
                List<string> names = ExtractSectionNamesFromSng(resolvedFolderPath);
                sections = names.Select((name, index) => new SectionDescriptor
                {
                    Index = index,
                    Name = name,
                    Tick = -1,
                    StartTime = 0d
                }).ToList();
            }
        }
        else
        {
            sections = ExtractSectionsFromChartFolder(resolvedFolderPath);
        }

        if (sections.Count > 0)
        {
            AssignSectionDisplayNames(sections);
            _songSectionNamesCache[songKey] = sections.Select(GetSectionDisplayName).ToList();
        }

        return sections;
    }

    private List<string> ExtractSectionNamesFromChartFolder(string folderPath)
    {
        string chartPath = Path.Combine(folderPath, "notes.chart");
        if (File.Exists(chartPath))
        {
            List<string> names = ParseSectionNamesFromChartText(File.ReadAllText(chartPath));
            LogVerbose($"ChartFolderParsed | chartPath={chartPath} count={names.Count}");
            return names;
        }

        string midiPath = Path.Combine(folderPath, "notes.mid");
        if (File.Exists(midiPath))
        {
            List<string> names = ParseSectionsFromMidiFile(midiPath).Select(section => section.Name).ToList();
            LogVerbose($"MidiFolderParsed | midiPath={midiPath} count={names.Count}");
            return names;
        }

        LogVerbose($"ChartFolderMissing | chartPath={chartPath}");
        return new List<string>();
    }

    private List<SectionDescriptor> ExtractSectionsFromChartFolder(string folderPath)
    {
        string chartPath = Path.Combine(folderPath, "notes.chart");
        if (File.Exists(chartPath))
        {
            string chartText = File.ReadAllText(chartPath);
            List<SectionDescriptor> sections = ParseSectionsFromChartText(chartText);
            LogVerbose($"ChartFolderSectionsParsed | chartPath={chartPath} count={sections.Count}");
            return sections;
        }

        string midiPath = Path.Combine(folderPath, "notes.mid");
        if (File.Exists(midiPath))
        {
            List<SectionDescriptor> sections = ParseSectionsFromMidiFile(midiPath);
            LogVerbose($"MidiFolderSectionsParsed | midiPath={midiPath} count={sections.Count}");
            return sections;
        }

        LogVerbose($"ChartFolderMissing | chartPath={chartPath}");
        return new List<SectionDescriptor>();
    }

    private List<string> ExtractSectionNamesFromSng(string sngPath)
    {
        if (!File.Exists(sngPath))
        {
            LogVerbose($"SngMissing | path={sngPath}");
            return new List<string>();
        }

        try
        {
            using var stream = new FileStream(sngPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: false);

            string identifier = Encoding.ASCII.GetString(reader.ReadBytes(6));
            uint version = reader.ReadUInt32();
            byte[] xorMask = reader.ReadBytes(16);
            LogVerbose($"SngHeader | path={sngPath} ident={identifier} version={version} length={stream.Length} xor={BitConverter.ToString(xorMask)}");

            if (!string.Equals(identifier, SngFileIdentifier, StringComparison.Ordinal))
            {
                LogVerbose($"SngHeaderInvalid | path={sngPath} ident={identifier}");
                return new List<string>();
            }

            long metadataLengthOffset = stream.Position;
            ulong metadataLen = reader.ReadUInt64();
            long metadataStart = stream.Position;
            long fileIndexLengthOffset = metadataStart + (long)metadataLen;
            LogVerbose($"SngMetadata | path={sngPath} metadataLen={metadataLen} metadataStart={metadataStart} fileIndexLengthOffset={fileIndexLengthOffset}");

            if (fileIndexLengthOffset + sizeof(ulong) > stream.Length)
            {
                LogVerbose($"SngMetadataOutOfRange | path={sngPath} metadataLen={metadataLen} streamLength={stream.Length} metadataLengthOffset={metadataLengthOffset}");
                return new List<string>();
            }

            stream.Position = fileIndexLengthOffset;
            ulong fileIndexLen = reader.ReadUInt64();
            long fileIndexStart = stream.Position;
            LogVerbose($"SngFileIndexHeader | path={sngPath} fileIndexLen={fileIndexLen} fileIndexStart={fileIndexStart}");

            if (fileIndexStart + (long)fileIndexLen > stream.Length)
            {
                LogVerbose($"SngFileIndexOutOfRange | path={sngPath} fileIndexLen={fileIndexLen} streamLength={stream.Length}");
                return new List<string>();
            }

            ulong fileCount = reader.ReadUInt64();
            LogVerbose($"SngFileIndexCount | path={sngPath} fileCount={fileCount}");

            (ulong Index, ulong Length, string Name)? chartMeta = null;
            (ulong Index, ulong Length, string Name)? midiMeta = null;
            for (ulong i = 0; i < fileCount; i++)
            {
                int filenameLength = reader.ReadByte();
                string filename = Encoding.UTF8.GetString(reader.ReadBytes(filenameLength));
                ulong contentsLen = reader.ReadUInt64();
                ulong contentsIndex = reader.ReadUInt64();
                LogVerbose($"SngFileEntry | path={sngPath} index={i} name={filename} nameLen={filenameLength} contentsLen={contentsLen} contentsIndex={contentsIndex}");
                if (string.Equals(filename, "notes.chart", StringComparison.OrdinalIgnoreCase))
                {
                    chartMeta = (contentsIndex, contentsLen, filename);
                }
                else if (string.Equals(filename, "notes.mid", StringComparison.OrdinalIgnoreCase))
                {
                    midiMeta = (contentsIndex, contentsLen, filename);
                }
            }

            long fileDataLengthOffset = fileIndexStart + (long)fileIndexLen;
            if (fileDataLengthOffset + sizeof(ulong) > stream.Length)
            {
                LogVerbose($"SngFileDataLengthOutOfRange | path={sngPath} fileDataLengthOffset={fileDataLengthOffset} streamLength={stream.Length}");
                return new List<string>();
            }

            stream.Position = fileDataLengthOffset;
            ulong fileDataLen = reader.ReadUInt64();
            long fileDataStart = stream.Position;
            LogVerbose($"SngFileDataHeader | path={sngPath} fileDataLen={fileDataLen} fileDataStart={fileDataStart}");

            if (chartMeta == null && midiMeta == null)
            {
                LogVerbose($"SngChartMissing | path={sngPath}");
                return new List<string>();
            }

            if (chartMeta != null)
            {
                long chartOffset = fileDataStart + (long)chartMeta.Value.Index;
                if (chartOffset < 0 || chartOffset + (long)chartMeta.Value.Length > stream.Length)
                {
                    LogVerbose($"SngChartOutOfRange | path={sngPath} name={chartMeta.Value.Name} chartOffset={chartOffset} chartLen={chartMeta.Value.Length} streamLength={stream.Length}");
                    return new List<string>();
                }

                stream.Position = chartOffset;
                byte[] maskedBytes = reader.ReadBytes((int)chartMeta.Value.Length);
                UnmaskSngBytes(maskedBytes, xorMask);
                string chartText = Encoding.UTF8.GetString(maskedBytes);
                List<string> names = ParseSectionNamesFromChartText(chartText);
                LogVerbose($"SngSectionsParsed | path={sngPath} chartName={chartMeta.Value.Name} count={names.Count} preview={string.Join("|", names.Take(6))}");
                return names;
            }

            long midiOffset = fileDataStart + (long)midiMeta!.Value.Index;
            if (midiOffset < 0 || midiOffset + (long)midiMeta.Value.Length > stream.Length)
            {
                LogVerbose($"SngMidiOutOfRange | path={sngPath} name={midiMeta.Value.Name} midiOffset={midiOffset} midiLen={midiMeta.Value.Length} streamLength={stream.Length}");
                return new List<string>();
            }

            stream.Position = midiOffset;
            byte[] midiBytes = reader.ReadBytes((int)midiMeta.Value.Length);
            UnmaskSngBytes(midiBytes, xorMask);
            List<string> midiNames = ParseSectionsFromMidiBytes(midiBytes).Select(section => section.Name).ToList();
            LogVerbose($"SngMidiSectionsParsed | path={sngPath} midiName={midiMeta.Value.Name} count={midiNames.Count} preview={string.Join("|", midiNames.Take(6))}");
            return midiNames;
        }
        catch (Exception ex)
        {
            LogVerbose($"SngParseFailure | path={sngPath}");
            LogVerbose(ex);
            return new List<string>();
        }
    }

    private List<SectionDescriptor> ExtractSectionsFromSongEntryContainer(object songEntry, string songKey)
    {
        try
        {
            string chartName = GetStringField(songEntry, "chartName") ?? string.Empty;
            bool isEnc = TryGetBooleanField(songEntry, "isEnc");
            LogVerbose($"SongEncLookup | songKey={songKey} chartName={chartName} isEnc={isEnc}");
            bool preferMidi = chartName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase);

            object? songEnc = songEntry.GetType().GetField("songEnc", AnyInstance)?.GetValue(songEntry);
            if (songEnc == null)
            {
                LogVerbose($"SongEncMissing | songKey={songKey}");
                return new List<SectionDescriptor>();
            }

            foreach (FieldInfo field in GetAllFields(songEnc.GetType()))
            {
                if (field.FieldType != typeof(byte[]))
                {
                    continue;
                }

                byte[]? bytes = field.GetValue(songEnc) as byte[];
                int length = bytes?.Length ?? 0;
                LogVerbose($"SongEncByteField | songKey={songKey} field={field.Name} length={length}");
                if (bytes == null || bytes.Length == 0)
                {
                    continue;
                }

                List<SectionDescriptor> sections = TryExtractSectionsFromPackedBytes(bytes, songKey, field.Name, preferMidi, out bool lookedLikeStructuredChart);
                if (sections.Count > 0)
                {
                    return sections;
                }

                if (lookedLikeStructuredChart)
                {
                    LogVerbose($"SongEncChartNoSections | songKey={songKey} field={field.Name} length={bytes.Length}");
                }
            }
        }
        catch (Exception ex)
        {
            LogVerbose($"SongEncParseFailure | songKey={songKey}");
            LogVerbose(ex);
        }

        return new List<SectionDescriptor>();
    }

    private List<SectionDescriptor> TryExtractSectionsFromPackedBytes(byte[] bytes, string songKey, string sourceName, bool preferMidi, out bool lookedLikeStructuredChart)
    {
        lookedLikeStructuredChart = false;

        if (preferMidi)
        {
            List<SectionDescriptor> midiSections = TryExtractSectionsFromMidiBytes(bytes, songKey, sourceName);
            if (midiSections.Count > 0)
            {
                return midiSections;
            }
        }

        List<SectionDescriptor> chartSections = TryExtractSectionsFromChartBytes(bytes, songKey, sourceName, out lookedLikeStructuredChart);
        if (chartSections.Count > 0)
        {
            return chartSections;
        }

        if (!preferMidi)
        {
            List<SectionDescriptor> midiSections = TryExtractSectionsFromMidiBytes(bytes, songKey, sourceName);
            if (midiSections.Count > 0)
            {
                return midiSections;
            }
        }

        return new List<SectionDescriptor>();
    }

    private List<SectionDescriptor> TryExtractSectionsFromChartBytes(byte[] bytes, string songKey, string sourceName, out bool lookedLikeChart)
    {
        lookedLikeChart = false;

        try
        {
            string chartText = Encoding.UTF8.GetString(bytes);
            lookedLikeChart =
                chartText.IndexOf("[Song]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                chartText.IndexOf("[SyncTrack]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                chartText.IndexOf("[Events]", StringComparison.OrdinalIgnoreCase) >= 0 ||
                chartText.IndexOf("Resolution =", StringComparison.OrdinalIgnoreCase) >= 0 ||
                chartText.IndexOf("section ", StringComparison.OrdinalIgnoreCase) >= 0;

            if (!lookedLikeChart)
            {
                return new List<SectionDescriptor>();
            }

            DumpChartText(songKey, sourceName, chartText);
            List<SectionDescriptor> sections = ParseSectionsFromChartText(chartText);
            LogVerbose($"SongEncSectionsParsed | songKey={songKey} source={sourceName} count={sections.Count} preview={string.Join("|", sections.Select(section => section.Name).Take(6))}");
            return sections;
        }
        catch (Exception ex)
        {
            LogVerbose($"SongEncChartDecodeFailure | songKey={songKey} source={sourceName} length={bytes.Length}");
            LogVerbose(ex);
            return new List<SectionDescriptor>();
        }
    }

    private List<SectionDescriptor> TryExtractSectionsFromMidiBytes(byte[] bytes, string songKey, string sourceName)
    {
        try
        {
            List<SectionDescriptor> sections = ParseSectionsFromMidiBytes(bytes);
            if (sections.Count > 0)
            {
                LogVerbose($"SongEncMidiSectionsParsed | songKey={songKey} source={sourceName} count={sections.Count} preview={string.Join("|", sections.Select(section => section.Name).Take(6))}");
            }

            return sections;
        }
        catch (Exception ex)
        {
            LogVerbose($"SongEncMidiDecodeFailure | songKey={songKey} source={sourceName} length={bytes.Length}");
            LogVerbose(ex);
            return new List<SectionDescriptor>();
        }
    }

    private void DumpChartText(string songKey, string sourceName, string chartText)
    {
        if (!VerboseLoggingEnabled)
        {
            return;
        }

        try
        {
            string dumpsDirectory = Path.Combine(_dataDir, "dumps");
            Directory.CreateDirectory(dumpsDirectory);
            string filename = $"{SanitizeFileName(songKey)}-{SanitizeFileName(sourceName)}.chart.txt";
            string dumpPath = Path.Combine(dumpsDirectory, filename);
            File.WriteAllText(dumpPath, chartText);
            LogVerbose($"SongEncChartDumped | path={dumpPath} length={chartText.Length}");
        }
        catch (Exception ex)
        {
            LogVerbose("SongEncChartDumpFailed");
            LogVerbose(ex);
        }
    }

    private static void UnmaskSngBytes(byte[] data, byte[] xorMask)
    {
        for (int i = 0; i < data.Length; i++)
        {
            byte xorKey = (byte)(xorMask[i & 0xF] ^ (i & 0xFF));
            data[i] ^= xorKey;
        }
    }

    private static List<string> ParseSectionNamesFromChartText(string chartText)
    {
        return ParseSectionsFromChartText(chartText).Select(section => section.Name).ToList();
    }

    private static List<SectionDescriptor> ParseSectionsFromChartText(string chartText)
    {
        int resolution = 192;
        var tempoEvents = new List<TempoEvent>();
        var sectionEvents = new List<(int Tick, string Name)>();
        using var reader = new StringReader(chartText);
        string? line;
        string currentBlock = string.Empty;
        while ((line = reader.ReadLine()) != null)
        {
            string trimmed = line.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
            {
                currentBlock = trimmed;
                continue;
            }

            if (trimmed == "{" || trimmed == "}")
            {
                continue;
            }

            if (string.Equals(currentBlock, "[Song]", StringComparison.OrdinalIgnoreCase))
            {
                int equalsIndex = trimmed.IndexOf('=');
                if (equalsIndex > 0)
                {
                    string key = trimmed.Substring(0, equalsIndex).Trim();
                    string value = trimmed.Substring(equalsIndex + 1).Trim();
                    if (string.Equals(key, "Resolution", StringComparison.OrdinalIgnoreCase)
                        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedResolution)
                        && parsedResolution > 0)
                    {
                        resolution = parsedResolution;
                    }
                }

                continue;
            }

            if (string.Equals(currentBlock, "[SyncTrack]", StringComparison.OrdinalIgnoreCase))
            {
                if (TryParseChartTickEvent(trimmed, out int tick, out string eventValue)
                    && eventValue.StartsWith("B ", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(eventValue.Substring(2).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int bpmValue)
                    && bpmValue > 0)
                {
                    tempoEvents.Add(new TempoEvent { Tick = tick, BpmTimes1000 = bpmValue });
                }

                continue;
            }

            if (!string.Equals(currentBlock, "[Events]", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int eventIndex = line.IndexOf("E \"section ", StringComparison.OrdinalIgnoreCase);
            if (eventIndex < 0)
            {
                continue;
            }

            int nameStart = eventIndex + "E \"section ".Length;
            int nameEnd = line.IndexOf('"', nameStart);
            if (nameEnd <= nameStart)
            {
                continue;
            }

            string name = line.Substring(nameStart, nameEnd - nameStart).Replace('_', ' ').Trim();
            if (!string.IsNullOrWhiteSpace(name))
            {
                string tickText = line.Substring(0, eventIndex).Trim();
                int equalsIndex = tickText.IndexOf('=');
                if (equalsIndex > 0)
                {
                    tickText = tickText.Substring(0, equalsIndex).Trim();
                }

                if (int.TryParse(tickText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int sectionTick))
                {
                    sectionEvents.Add((sectionTick, name));
                }
            }
        }

        if (tempoEvents.Count == 0)
        {
            tempoEvents.Add(new TempoEvent { Tick = 0, BpmTimes1000 = 120000 });
        }

        tempoEvents = tempoEvents
            .OrderBy(tempo => tempo.Tick)
            .GroupBy(tempo => tempo.Tick)
            .Select(group => group.Last())
            .ToList();

        var sections = new List<SectionDescriptor>();
        for (int index = 0; index < sectionEvents.Count; index++)
        {
            (int tick, string name) = sectionEvents[index];
            sections.Add(new SectionDescriptor
            {
                Index = index,
                Name = name,
                Tick = tick,
                StartTime = ConvertChartTickToSeconds(tick, resolution, tempoEvents)
            });
        }

        return sections;
    }

    private static List<SectionDescriptor> ParseSectionsFromMidiFile(string midiPath)
    {
        try
        {
            using var stream = new FileStream(midiPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            MidiSectionParseResult parsed = ParseMidiSections(stream);
            List<SectionDescriptor> sections = parsed.Sections;
            LogVerbose($"MidiSectionsParsed | midiPath={midiPath} format={parsed.Format} tracks={parsed.TrackCount} count={sections.Count} preview={string.Join("|", sections.Select(section => section.Name).Take(6))}");
            return sections;
        }
        catch (Exception ex)
        {
            LogVerbose($"MidiParseFailure | midiPath={midiPath}");
            LogVerbose(ex);
            return new List<SectionDescriptor>();
        }
    }

    private static List<SectionDescriptor> ParseSectionsFromMidiBytes(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return ParseMidiSections(stream).Sections;
    }

    private static MidiSectionParseResult ParseMidiSections(Stream stream)
    {
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MThd")
        {
            return MidiSectionParseResult.Empty;
        }

        int headerLength = ReadInt32BigEndian(reader);
        short format = ReadInt16BigEndian(reader);
        short trackCount = ReadInt16BigEndian(reader);
        short division = ReadInt16BigEndian(reader);
        if (headerLength > 6)
        {
            reader.ReadBytes(headerLength - 6);
        }

        int resolution = division > 0 ? division : 480;
        List<TempoEvent> tempoEvents = new() { new TempoEvent { Tick = 0, BpmTimes1000 = 120000 } };
        List<(int Tick, string Name)> sectionEvents = new();

        for (int trackIndex = 0; trackIndex < trackCount && stream.Position < stream.Length; trackIndex++)
        {
            if (Encoding.ASCII.GetString(reader.ReadBytes(4)) != "MTrk")
            {
                break;
            }

            int trackLength = ReadInt32BigEndian(reader);
            long trackEnd = stream.Position + trackLength;
            int absoluteTick = 0;
            int runningStatus = 0;
            bool isEventsTrack = false;

            while (stream.Position < trackEnd)
            {
                absoluteTick += ReadVariableLengthQuantity(reader);
                int statusByte = reader.ReadByte();
                if (statusByte < 0x80)
                {
                    if (runningStatus == 0)
                    {
                        break;
                    }

                    stream.Position--;
                    statusByte = runningStatus;
                }
                else
                {
                    runningStatus = statusByte;
                }

                if (statusByte == 0xFF)
                {
                    int metaType = reader.ReadByte();
                    int dataLength = ReadVariableLengthQuantity(reader);
                    byte[] data = reader.ReadBytes(dataLength);

                    if (metaType == 0x51 && data.Length == 3)
                    {
                        int microsecondsPerQuarter = (data[0] << 16) | (data[1] << 8) | data[2];
                        if (microsecondsPerQuarter > 0)
                        {
                            int bpmTimes1000 = RoundToIntAwayFromZero(60000000000d / microsecondsPerQuarter);
                            tempoEvents.Add(new TempoEvent { Tick = absoluteTick, BpmTimes1000 = bpmTimes1000 });
                        }
                    }
                    else if (metaType == 0x03)
                    {
                        string trackName = Encoding.UTF8.GetString(data).Trim();
                        isEventsTrack = string.Equals(trackName, "EVENTS", StringComparison.OrdinalIgnoreCase);
                    }
                    else if (isEventsTrack && (metaType == 0x01 || metaType == 0x05 || metaType == 0x06))
                    {
                        string? sectionName = ParseMidiSectionName(Encoding.UTF8.GetString(data));
                        if (!string.IsNullOrWhiteSpace(sectionName))
                        {
                            sectionEvents.Add((absoluteTick, sectionName!));
                        }
                    }

                    continue;
                }

                if (statusByte == 0xF0 || statusByte == 0xF7)
                {
                    int sysexLength = ReadVariableLengthQuantity(reader);
                    reader.ReadBytes(sysexLength);
                    continue;
                }

                int highNibble = statusByte & 0xF0;
                int dataBytes = (highNibble == 0xC0 || highNibble == 0xD0) ? 1 : 2;
                reader.ReadBytes(dataBytes);
            }

            stream.Position = trackEnd;
        }

        tempoEvents = tempoEvents
            .OrderBy(tempo => tempo.Tick)
            .GroupBy(tempo => tempo.Tick)
            .Select(group => group.Last())
            .ToList();

        List<SectionDescriptor> sections = new();
        foreach ((int tick, string name) in sectionEvents
            .OrderBy(section => section.Tick)
            .GroupBy(section => section.Tick)
            .Select(group => group.First()))
        {
            sections.Add(new SectionDescriptor
            {
                Index = sections.Count,
                Name = name,
                Tick = tick,
                StartTime = ConvertChartTickToSeconds(tick, resolution, tempoEvents)
            });
        }

        return new MidiSectionParseResult(format, trackCount, sections);
    }

    private static int RoundToIntAwayFromZero(double value)
    {
        return value >= 0d
            ? (int)Math.Floor(value + 0.5d)
            : (int)Math.Ceiling(value - 0.5d);
    }

    private static Dictionary<string, SectionStatsState> BuildSectionStatsLookup(IEnumerable<SectionStatsState> sectionStats)
    {
        var lookup = new Dictionary<string, SectionStatsState>(StringComparer.Ordinal);
        var duplicateCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (SectionStatsState section in sectionStats)
        {
            if (!lookup.ContainsKey(section.Name))
            {
                lookup[section.Name] = section;
                duplicateCounts[section.Name] = 1;
                continue;
            }

            int nextIndex = duplicateCounts.TryGetValue(section.Name, out int count) ? count + 1 : 2;
            duplicateCounts[section.Name] = nextIndex;
            lookup[$"{section.Name} ({nextIndex})"] = section;
        }

        return lookup;
    }

    private SectionSnapshotCache GetOrBuildSectionSnapshot(string songKey, List<SectionDescriptor> sections, SongConfig songConfig, SongMemory songMemory)
    {
        if (_sectionSnapshotCache != null &&
            string.Equals(_sectionSnapshotCache.SongKey, songKey, StringComparison.Ordinal) &&
            _sectionSnapshotCache.MemoryVersion == _sectionMemoryVersion &&
            _sectionSnapshotCache.ConfigVersion == _sectionConfigVersion &&
            _sectionSnapshotCache.SectionCount == sections.Count)
        {
            return _sectionSnapshotCache;
        }

        List<TrackedSectionState> trackedSections = sections.Select(section => BuildTrackedSectionState(sections, section, songConfig, songMemory)).ToList();
        List<SectionStatsState> sectionStats = sections.Select(section => BuildSectionStatsState(sections, section, songConfig, songMemory)).ToList();
        _sectionSnapshotCache = new SectionSnapshotCache
        {
            SongKey = songKey,
            MemoryVersion = _sectionMemoryVersion,
            ConfigVersion = _sectionConfigVersion,
            SectionCount = sections.Count,
            TrackedSections = trackedSections,
            SectionStats = sectionStats,
            SectionStatsByName = BuildSectionStatsLookup(sectionStats)
        };

        return _sectionSnapshotCache;
    }

    private List<CompletedRunRecord> GetOrBuildCompletedRunsSnapshot(string songKey, SongMemory songMemory)
    {
        if (string.Equals(_completedRunsSnapshotSongKey, songKey, StringComparison.Ordinal) &&
            _completedRunsSnapshotMemoryVersion == _memoryVersion)
        {
            return _completedRunsSnapshot;
        }

        _completedRunsSnapshotSongKey = songKey;
        _completedRunsSnapshotMemoryVersion = _memoryVersion;
        _completedRunsSnapshot = songMemory.CompletedRuns.Select(run => run.Clone()).ToList();
        return _completedRunsSnapshot;
    }

    private static string? ParseMidiSectionName(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        string trimmed = text.Trim();
        const string rb2SectionPrefix = "[section ";
        const string rb3SectionPrefix = "[prc_";

        if (trimmed.StartsWith(rb2SectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int endBracketIndex = trimmed.IndexOf(']');
            if (endBracketIndex <= rb2SectionPrefix.Length)
            {
                return null;
            }

            string sectionName = trimmed.Substring(rb2SectionPrefix.Length, endBracketIndex - rb2SectionPrefix.Length);
            return NormalizeMidiSectionName(sectionName);
        }

        if (trimmed.StartsWith(rb3SectionPrefix, StringComparison.OrdinalIgnoreCase))
        {
            int endBracketIndex = trimmed.IndexOf(']');
            if (endBracketIndex <= rb3SectionPrefix.Length)
            {
                return null;
            }

            int firstQuoteIndex = trimmed.IndexOf('"', endBracketIndex + 1);
            if (firstQuoteIndex >= 0)
            {
                int lastQuoteIndex = trimmed.LastIndexOf('"');
                if (lastQuoteIndex > firstQuoteIndex)
                {
                    string displayName = trimmed.Substring(firstQuoteIndex + 1, lastQuoteIndex - firstQuoteIndex - 1);
                    return NormalizeMidiSectionName(displayName);
                }
            }

            string sectionName = trimmed.Substring(rb3SectionPrefix.Length, endBracketIndex - rb3SectionPrefix.Length);
            return NormalizeMidiSectionName(sectionName);
        }

        return null;
    }

    private static string? NormalizeMidiSectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        string normalized = name.Replace('_', ' ').Trim();
        return string.IsNullOrWhiteSpace(normalized) ? null : normalized;
    }

    private static int ReadVariableLengthQuantity(BinaryReader reader)
    {
        int value = 0;
        while (true)
        {
            int current = reader.ReadByte();
            value = (value << 7) | (current & 0x7F);
            if ((current & 0x80) == 0)
            {
                return value;
            }
        }
    }

    private static short ReadInt16BigEndian(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(2);
        if (bytes.Length < 2)
        {
            throw new EndOfStreamException();
        }

        return (short)((bytes[0] << 8) | bytes[1]);
    }

    private static int ReadInt32BigEndian(BinaryReader reader)
    {
        byte[] bytes = reader.ReadBytes(4);
        if (bytes.Length < 4)
        {
            throw new EndOfStreamException();
        }

        return (bytes[0] << 24) | (bytes[1] << 16) | (bytes[2] << 8) | bytes[3];
    }

    private static bool TryParseChartTickEvent(string line, out int tick, out string eventValue)
    {
        tick = 0;
        eventValue = string.Empty;

        int equalsIndex = line.IndexOf('=');
        if (equalsIndex <= 0)
        {
            return false;
        }

        string tickText = line.Substring(0, equalsIndex).Trim();
        if (!int.TryParse(tickText, NumberStyles.Integer, CultureInfo.InvariantCulture, out tick))
        {
            return false;
        }

        eventValue = line.Substring(equalsIndex + 1).Trim();
        return true;
    }

    private static double ConvertChartTickToSeconds(int targetTick, int resolution, List<TempoEvent> tempoEvents)
    {
        if (resolution <= 0)
        {
            resolution = 192;
        }

        double seconds = 0d;
        int currentTick = 0;
        int currentBpmTimes1000 = tempoEvents[0].BpmTimes1000;

        if (tempoEvents[0].Tick > 0)
        {
            seconds += ConvertTickDeltaToSeconds(tempoEvents[0].Tick, resolution, currentBpmTimes1000);
            currentTick = tempoEvents[0].Tick;
        }

        foreach (TempoEvent tempo in tempoEvents)
        {
            if (tempo.Tick <= currentTick)
            {
                currentBpmTimes1000 = tempo.BpmTimes1000;
                continue;
            }

            if (targetTick <= tempo.Tick)
            {
                seconds += ConvertTickDeltaToSeconds(targetTick - currentTick, resolution, currentBpmTimes1000);
                return seconds;
            }

            seconds += ConvertTickDeltaToSeconds(tempo.Tick - currentTick, resolution, currentBpmTimes1000);
            currentTick = tempo.Tick;
            currentBpmTimes1000 = tempo.BpmTimes1000;
        }

        if (targetTick > currentTick)
        {
            seconds += ConvertTickDeltaToSeconds(targetTick - currentTick, resolution, currentBpmTimes1000);
        }

        return seconds;
    }

    private static double ConvertTickDeltaToSeconds(int tickDelta, int resolution, int bpmTimes1000)
    {
        if (tickDelta <= 0 || resolution <= 0 || bpmTimes1000 <= 0)
        {
            return 0d;
        }

        return tickDelta * 60d * 1000d / (resolution * bpmTimes1000);
    }

    private static string SanitizeFileName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "unknown";
        }

        var builder = new StringBuilder(value.Length);
        foreach (char character in value)
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), character) >= 0 ? '_' : character);
        }

        return builder.ToString();
    }

    private object? LoadChartFromSongEntry(object songEntry)
    {
        Type songEntryType = songEntry.GetType();
        _songEntryLoadChartMethod ??= GetAllMethods(songEntryType)
            .FirstOrDefault(method =>
                method.GetParameters().Length == 0 &&
                LooksLikeLoadedChartType(method.ReturnType));
        _songEntryLoadChartWithFlagMethod ??= GetAllMethods(songEntryType)
            .FirstOrDefault(method =>
                method.GetParameters().Length == 1 &&
                method.GetParameters()[0].ParameterType == typeof(bool) &&
                LooksLikeLoadedChartType(method.ReturnType));

        if (_songEntryLoadChartMethod != null)
        {
            return _songEntryLoadChartMethod.Invoke(songEntry, null);
        }

        if (_songEntryLoadChartWithFlagMethod != null)
        {
            return _songEntryLoadChartWithFlagMethod.Invoke(songEntry, new object[] { false });
        }

        return null;
    }

    private void LogSongEntryChartDiagnostics(object parsedChart)
    {
        if (!VerboseLoggingEnabled)
        {
            return;
        }

        try
        {
            StockTrackerLog.Write($"SongEntryChartDiagnostics | type={parsedChart.GetType().FullName}");
            foreach (FieldInfo field in GetAllFields(parsedChart.GetType()))
            {
                object? value = null;
                try
                {
                    value = field.GetValue(parsedChart);
                }
                catch
                {
                }

                string valueText = value switch
                {
                    null => "<null>",
                    Array array => $"array[{array.Length}]<{field.FieldType.FullName}>",
                    IEnumerable when value is not string => $"enumerable<{field.FieldType.FullName}>",
                    _ => value.GetType().FullName ?? value.ToString() ?? "<value>"
                };
                StockTrackerLog.Write($"SongEntryChartField | {field.DeclaringType?.Name}.{field.Name} | {field.FieldType.FullName} | {valueText}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private double TryGetChartSectionTime(object chart, int index, object? section)
    {
        try
        {
            if (_chartSectionTimeMethod != null)
            {
                object? value = _chartSectionTimeMethod.Invoke(chart, new object[] { index });
                if (value != null)
                {
                    return ConvertToDouble(value);
                }
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }

        return ExtractSectionTime(section, index);
    }

    private void LogChartMethods(object chart)
    {
        if (!VerboseLoggingEnabled)
        {
            return;
        }

        if (Time.unscaledTime - _lastDiagnosticsAt < 2f)
        {
            return;
        }

        _lastDiagnosticsAt = Time.unscaledTime;
        try
        {
            Type chartType = chart.GetType();
            StockTrackerLog.Write(
                "ChartDiagnostics | " +
                $"chartType={chartType.FullName} " +
                $"chartField={_chartField?.Name ?? "<null>"} " +
                $"sectionsMethod={_chartSectionsMethod?.Name ?? "<null>"} " +
                $"sectionTimeMethod={_chartSectionTimeMethod?.Name ?? "<null>"}");

            foreach (MethodInfo method in chartType.GetMethods(AnyInstance))
            {
                string parameters = string.Join(",", method.GetParameters().Select(p => p.ParameterType.Name));
                StockTrackerLog.Write($"ChartMethod | {method.Name} | returns={method.ReturnType.FullName} | params={parameters}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private void LogChartFields(object chart)
    {
        if (!VerboseLoggingEnabled)
        {
            return;
        }

        if (Time.unscaledTime - _lastChartFieldDiagnosticsAt < 2f)
        {
            return;
        }

        _lastChartFieldDiagnosticsAt = Time.unscaledTime;
        try
        {
            foreach (FieldInfo field in GetAllFields(chart.GetType()))
            {
                object? value = null;
                try
                {
                    value = field.GetValue(chart);
                }
                catch
                {
                }

                string valueText = value switch
                {
                    null => "<null>",
                    Array array => $"array[{array.Length}]<{field.FieldType.FullName}>",
                    IEnumerable when value is not string => $"enumerable<{field.FieldType.FullName}>",
                    _ => value.GetType().FullName ?? value.ToString() ?? "<value>"
                };

                StockTrackerLog.Write($"ChartField | {field.DeclaringType?.Name}.{field.Name} | {field.FieldType.FullName} | {valueText}");
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private static string ExtractSectionName(object section, int index)
    {
        string? fromProperty = GetAllProperties(section.GetType())
            .Where(property => property.PropertyType == typeof(string))
            .Select(property => property.GetValue(section, null) as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (!string.IsNullOrWhiteSpace(fromProperty))
        {
            return fromProperty!.Trim();
        }

        string? fromField = GetAllFields(section.GetType())
            .Where(field => field.FieldType == typeof(string))
            .Select(field => field.GetValue(section) as string)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        return string.IsNullOrWhiteSpace(fromField) ? $"Section {index + 1}" : fromField!.Trim();
    }

    private static double ExtractSectionTime(object? section, int index)
    {
        if (section == null)
        {
            return index;
        }

        object? fromField = GetAllFields(section.GetType())
            .Where(field => field.FieldType == typeof(float) || field.FieldType == typeof(double))
            .OrderBy(field => field.Name)
            .Select(field => field.GetValue(section))
            .FirstOrDefault();
        if (fromField != null)
        {
            return ConvertToDouble(fromField);
        }

        object? fromProperty = GetAllProperties(section.GetType())
            .Where(property => property.PropertyType == typeof(float) || property.PropertyType == typeof(double))
            .OrderBy(property => property.Name)
            .Select(property => property.GetValue(section, null))
            .FirstOrDefault();
        return fromProperty != null ? ConvertToDouble(fromProperty) : index;
    }

    private static string GetCurrentSectionName(IReadOnlyList<SectionDescriptor> sections, double songTime, int currentChartTick = -1)
    {
        if (sections.Count == 0)
        {
            return string.Empty;
        }

        if (currentChartTick >= 0)
        {
            SectionDescriptor? tickCurrent = null;
            SectionDescriptor? firstTickSection = null;
            foreach (SectionDescriptor section in sections)
            {
                if (section.Tick < 0)
                {
                    continue;
                }

                firstTickSection ??= section;
                if (section.Tick <= currentChartTick)
                {
                    tickCurrent = section;
                }
                else
                {
                    break;
                }
            }

            if (tickCurrent != null)
            {
                return GetSectionDisplayName(tickCurrent);
            }

            if (firstTickSection != null)
            {
                return GetSectionDisplayName(firstTickSection);
            }
        }

        SectionDescriptor current = sections[0];
        foreach (SectionDescriptor section in sections)
        {
            if (section.StartTime <= songTime + 0.001)
            {
                current = section;
            }
            else
            {
                break;
            }
        }

        return GetSectionDisplayName(current);
    }

    private SongDescriptor BuildSongDescriptor(object songEntry, SongSpeedInfo songSpeed, DifficultyInfo difficulty)
    {
        string? title = GetStringProperty(songEntry, "Name_StrippedTags") ?? GetStringProperty(songEntry, "Name");
        string displayTitle = string.IsNullOrWhiteSpace(title)
            ? "Unknown Song"
            : title!.Trim();
        return new SongDescriptor
        {
            Title = displayTitle,
            Artist = GetStringProperty(songEntry, "Artist_StrippedTags") ?? GetStringProperty(songEntry, "Artist"),
            Charter = GetStringProperty(songEntry, "Charter_StrippedTags") ?? GetStringProperty(songEntry, "Charter"),
            SongSpeedPercent = songSpeed.Percent,
            SongSpeedLabel = songSpeed.Label,
            DifficultyCode = difficulty.Code,
            DifficultyName = difficulty.Name,
            SongKey = BuildSongKey(songEntry, songSpeed.Percent, difficulty),
            OverlayLayoutKey = BuildOverlayLayoutKey(songEntry)
        };
    }

    private static string BuildSongKey(object songEntry, int songSpeedPercent, DifficultyInfo difficulty)
    {
        string artist = NormalizeSongKeyPart(GetStringProperty(songEntry, "Artist_StrippedTags") ?? GetStringProperty(songEntry, "Artist"));
        string title = NormalizeSongKeyPart(GetStringProperty(songEntry, "Name_StrippedTags") ?? GetStringProperty(songEntry, "Name"));
        string charter = NormalizeSongKeyPart(GetStringProperty(songEntry, "Charter_StrippedTags") ?? GetStringProperty(songEntry, "Charter"));
        string checksum = (GetStringProperty(songEntry, "checksumString") ?? string.Empty).Trim();
        string speedSuffix = $"@{songSpeedPercent}%";

        string readable = $"{difficulty.Code} {artist} - {title}";
        if (!string.IsNullOrWhiteSpace(charter))
        {
            readable += $" [{charter}]";
        }

        if (!string.IsNullOrWhiteSpace(checksum))
        {
            readable += $" [{checksum.Substring(0, Math.Min(8, checksum.Length))}]";
        }

        return readable + " " + speedSuffix;
    }

    private static string BuildOverlayLayoutKey(object songEntry)
    {
        string artist = NormalizeSongKeyPart(GetStringProperty(songEntry, "Artist_StrippedTags") ?? GetStringProperty(songEntry, "Artist"));
        string title = NormalizeSongKeyPart(GetStringProperty(songEntry, "Name_StrippedTags") ?? GetStringProperty(songEntry, "Name"));
        string charter = NormalizeSongKeyPart(GetStringProperty(songEntry, "Charter_StrippedTags") ?? GetStringProperty(songEntry, "Charter"));
        string checksum = (GetStringProperty(songEntry, "checksumString") ?? string.Empty).Trim();

        string readable = $"{artist} - {title}";
        if (!string.IsNullOrWhiteSpace(charter))
        {
            readable += $" [{charter}]";
        }

        if (!string.IsNullOrWhiteSpace(checksum))
        {
            readable += $" [{checksum.Substring(0, Math.Min(8, checksum.Length))}]";
        }

        return readable;
    }

    private static string NormalizeSongKeyPart(string? value)
    {
        string text = string.IsNullOrWhiteSpace(value) ? "unknown" : value!.Trim();
        return Regex.Replace(text, "\\s+", " ");
    }

    private SongSpeedInfo ReadSongSpeed()
    {
        try
        {
            object? globalVariables = _globalVariablesSingletonField?.GetValue(null);
            int globalSongSpeed = ConvertToInt32(_globalVariablesSongSpeedField?.GetValue(globalVariables));

            object? setting = _songSpeedSettingField?.GetValue(null);
            if (setting == null)
            {
                return new SongSpeedInfo
                {
                    Percent = globalSongSpeed > 0 ? globalSongSpeed : 100,
                    Label = (globalSongSpeed > 0 ? globalSongSpeed : 100).ToString(CultureInfo.InvariantCulture) + "%"
                };
            }

            int percent = ConvertToInt32(_gameSettingCurrentValueProperty?.GetValue(setting, null));
            if (globalSongSpeed > 0)
            {
                percent = globalSongSpeed;
            }

            if (percent <= 0)
            {
                percent = 100;
            }

            string label = _gameSettingPercentStringProperty?.GetValue(setting, null) as string ?? string.Empty;
            if (string.IsNullOrWhiteSpace(label))
            {
                label = percent.ToString(CultureInfo.InvariantCulture) + "%";
            }

            return new SongSpeedInfo
            {
                Percent = percent,
                Label = label
            };
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"SongSpeedReadFailure | {ex.Message}");
            return new SongSpeedInfo();
        }
    }

    private FieldInfo? FindSongSpeedSettingField()
    {
        if (_gameManagerType?.Assembly == null || _gameSettingType == null || _gameSettingNameField == null)
        {
            return null;
        }

        try
        {
            foreach (Type type in _gameManagerType.Assembly.GetTypes())
            {
                foreach (FieldInfo field in type.GetFields(AnyStatic))
                {
                    if (field.FieldType != _gameSettingType)
                    {
                        continue;
                    }

                    object? value = null;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch
                    {
                    }

                    if (value == null)
                    {
                        continue;
                    }

                    string? settingName = _gameSettingNameField.GetValue(value) as string;
                    if (string.Equals(settingName, "song_speed", StringComparison.Ordinal))
                    {
                        StockTrackerLog.WriteDebug($"SongSpeedSettingResolved | type={type.FullName} field={field.Name}");
                        return field;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"SongSpeedSettingResolveFailure | {ex.Message}");
        }

        return null;
    }

    private FieldInfo? FindMenuBackgroundSettingField()
    {
        if (_gameManagerType?.Assembly == null || _gameSettingType == null || _gameSettingNameField == null)
        {
            return null;
        }

        try
        {
            foreach (Type type in _gameManagerType.Assembly.GetTypes())
            {
                foreach (FieldInfo field in type.GetFields(AnyStatic))
                {
                    if (field.FieldType != _gameSettingType)
                    {
                        continue;
                    }

                    object? value = null;
                    try
                    {
                        value = field.GetValue(null);
                    }
                    catch
                    {
                    }

                    if (value == null)
                    {
                        continue;
                    }

                    string? settingName = _gameSettingNameField.GetValue(value) as string;
                    if (string.Equals(settingName, MenuBackgroundSettingName, StringComparison.Ordinal))
                    {
                        return field;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write($"MenuBackgroundSettingResolveFailure | {ex.Message}");
        }

        return null;
    }

    private SongMemory EnsureSongMemory(SongDescriptor song, IEnumerable<SectionDescriptor> sections)
    {
        List<SectionDescriptor> sectionList = sections.ToList();
        if (!_memory.Songs.TryGetValue(song.SongKey, out SongMemory? songMemory))
        {
            songMemory = new SongMemory();
            _memory.Songs[song.SongKey] = songMemory;
            MarkMemoryDirty(song.SongKey);
        }

        if (!string.Equals(songMemory.Title, song.Title, StringComparison.Ordinal))
        {
            songMemory.Title = song.Title;
            MarkMemoryDirty(song.SongKey);
        }
        if (!string.Equals(songMemory.Artist, song.Artist, StringComparison.Ordinal))
        {
            songMemory.Artist = song.Artist;
            MarkMemoryDirty(song.SongKey);
        }
        if (!string.Equals(songMemory.Charter, song.Charter, StringComparison.Ordinal))
        {
            songMemory.Charter = song.Charter;
            MarkMemoryDirty(song.SongKey);
        }

        foreach (SectionDescriptor section in sectionList)
        {
            string sectionKey = BuildSectionOverlayKey(sectionList, section);
            EnsureSectionMemory(songMemory, sectionKey, song.SongKey);
        }

        return songMemory;
    }

    private SectionMemory EnsureSectionMemory(SongMemory songMemory, string sectionKey)
    {
        return EnsureSectionMemory(songMemory, sectionKey, ResolveDirtyMemorySongKey() ?? string.Empty);
    }

    private SectionMemory EnsureSectionMemory(SongMemory songMemory, string sectionKey, string songKey)
    {
        if (songMemory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory))
        {
            return sectionMemory;
        }

        sectionMemory = new SectionMemory();
        songMemory.Sections[sectionKey] = sectionMemory;
        MarkMemoryDirty(songKey);
        return sectionMemory;
    }

    private SongConfig EnsureSongConfig(SongDescriptor song, IEnumerable<SectionDescriptor> sections)
    {
        List<SectionDescriptor> sectionList = sections.ToList();
        string configKey = song.OverlayLayoutKey ?? song.SongKey;
        if (!_config.Songs.TryGetValue(configKey, out SongConfig? songConfig))
        {
            songConfig = new SongConfig();
            _config.Songs[configKey] = songConfig;
            MarkConfigDirty(songKey: configKey);
        }

        if (!string.Equals(songConfig.Title, song.Title, StringComparison.Ordinal))
        {
            songConfig.Title = song.Title;
            MarkConfigDirty(songKey: configKey);
        }
        if (!string.Equals(songConfig.Artist, song.Artist, StringComparison.Ordinal))
        {
            songConfig.Artist = song.Artist;
            MarkConfigDirty(songKey: configKey);
        }
        if (!string.Equals(songConfig.Charter, song.Charter, StringComparison.Ordinal))
        {
            songConfig.Charter = song.Charter;
            MarkConfigDirty(songKey: configKey);
        }

        foreach (SectionDescriptor section in sectionList)
        {
            string sectionKey = BuildSectionOverlayKey(sectionList, section);
            if (!songConfig.TrackedSections.ContainsKey(sectionKey))
            {
                songConfig.TrackedSections[sectionKey] = false;
                MarkConfigDirty(songKey: configKey, affectsSectionSnapshots: true);
            }
        }

        NormalizeSongOverlayWidgets(songConfig, configKey);
        return songConfig;
    }

    private void ResetSongOverlay(SongConfig songConfig)
    {
        songConfig.TrackedSections.Clear();
        songConfig.OverlayWidgets.Clear();
        _overlayColorTargetKey = null;
        _sectionSnapshotCache = null;
        MarkConfigDirty(affectsSectionSnapshots: true);
        RequestImmediateExportRefresh(includeStateExport: true, includeObsExport: true);
    }

    private void ResetSongStats(TrackerState state)
    {
        if (state.Song == null)
        {
            return;
        }

        if (_memory.Songs.TryGetValue(state.Song.SongKey, out SongMemory? existingSongMemory))
        {
            _memory.LifetimeGhostedNotes = Math.Max(0, _memory.LifetimeGhostedNotes - existingSongMemory.LifetimeGhostedNotes);
            _memory.Songs.Remove(state.Song.SongKey);
            MarkMemoryDirty();
        }

        if (_sectionSnapshotCache != null && string.Equals(_sectionSnapshotCache.SongKey, state.Song.SongKey, StringComparison.Ordinal))
        {
            _sectionSnapshotCache = null;
        }

        if (_runState.InRun && string.Equals(_runState.SongKey, state.Song.SongKey, StringComparison.Ordinal))
        {
            _runState = new RunState
            {
                InRun = true,
                SongKey = state.Song.SongKey,
                CachedSongEntry = _runState.CachedSongEntry,
                CachedPlayer = _runState.CachedPlayer,
                CachedChart = _runState.CachedChart,
                CachedSongDescriptor = _runState.CachedSongDescriptor,
                CachedSongSpeed = _runState.CachedSongSpeed,
                CachedDifficulty = _runState.CachedDifficulty,
                CachedSongDuration = _runState.CachedSongDuration,
                CachedBotEnabled = _runState.CachedBotEnabled,
                LastStableRefreshAt = _runState.LastStableRefreshAt,
                CachedSongMemory = _runState.CachedSongMemory,
                CachedSongMemoryKey = _runState.CachedSongMemoryKey,
                CachedSongConfig = _runState.CachedSongConfig,
                CachedSongConfigKey = _runState.CachedSongConfigKey,
                CachedNotesHit = _runState.CachedNotesHit,
                LastNotesHitRefreshAt = _runState.LastNotesHitRefreshAt,
                HasCachedNotesHit = _runState.HasCachedNotesHit,
                CachedResultStats = _runState.CachedResultStats,
                LastResultStatsRefreshAt = _runState.LastResultStatsRefreshAt,
                HasCachedResultStats = _runState.HasCachedResultStats,
                LastSection = state.CurrentSection,
                LastSongTime = state.SongTime,
                LastStreak = state.Streak,
                LastGhostNotes = state.CurrentGhostedNotes,
                LastOverstrums = state.CurrentOverstrums,
                LastMissedNotes = state.CurrentMissedNotes,
                MissedNotesBaseline = state.CurrentMissedNotes,
                BestStreakThisRun = state.Streak,
                FirstMissStreak = 0,
                HadMiss = _runState.HadMiss
            };
        }

        SaveMemory();
    }

    private void WipeAllModData()
    {
        _desktopOverlayProcess = null;
        _desktopOverlayLaunchFailed = false;

        _memory = new TrackerMemory();
        _config = new TrackerConfig();
        _mergedDesktopOverlayStyle = CloneDesktopOverlayStyle(_config.DesktopOverlayStyle);
        _config.DesktopOverlayStyle = CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle);
        _runState = new RunState();
        _latestState = CreateIdleState();
        _latestStateVersion++;
        _sectionSnapshotCache = null;
        _noteSplitSnapshotCache = null;
        _overlayRenderSnapshot = null;
        _songSectionsCache.Clear();
        _songSectionNamesCache.Clear();
        _completedRunsSnapshotSongKey = string.Empty;
        _completedRunsSnapshotMemoryVersion = -1;
        _completedRunsSnapshot = new List<CompletedRunRecord>();
        _overlayColorTargetKey = null;
        _overlayColorPickerDragging = false;
        _songResetConfirmKey = null;
        _songResetConfirmExpiresAt = 0f;
        _overlayResetConfirmKey = null;
        _overlayResetConfirmExpiresAt = 0f;
        _wipeAllDataConfirmExpiresAt = 0f;
        _exportTemplateEditorVisible = false;
        _selectedExportTemplateId = null;
        _exportTemplateEditorDrafts.Clear();
        _compiledExportTemplates.Clear();
        _compiledExportTemplatesVersion = -1;
        _workerCompiledExportTemplates.Clear();
        _workerCompiledExportTemplatesVersion = -1;
        _loggedInvalidTemplateSources.Clear();
        _memoryVersion++;
        _configVersion++;
        _exportTemplateVersion++;
        _sectionMemoryVersion++;
        _sectionConfigVersion++;
        _overlayConfigVersion++;
        _enabledTextExportsVersion++;
        _enabledTextExportSnapshotVersion = -1;
        _memoryWriteSnapshot = null;
        _memoryWriteSnapshotRequiresFullRefresh = true;
        _dirtyMemorySongKeys.Clear();
        _configWriteSnapshot = null;
        _configWriteSnapshotRequiresFullRefresh = true;
        _dirtyConfigSongKeys.Clear();

        lock (_exportWorkerSync)
        {
            _pendingExport = null;
        }

        lock (_fileWriteSync)
        {
            _fileWriteCache.Clear();
        }

        lock (_persistenceWriteSync)
        {
            _pendingPersistenceWrites.Clear();
        }

        DeleteIfExists(_statePath);
        DeleteIfExists(_memoryPath);
        DeleteIfExists(_configPath);
        DeleteIfExists(_desktopStylePath);
        DeleteIfExists(Path.Combine(_dataDir, "v1-stock.log"));
        DeleteIfExists(Path.Combine(_dataDir, "desktop-overlay.log"));
        DeleteIfExists(_obsStatePath);
        DeleteDirectoryIfExists(_obsDir);

        _memoryDirty = true;
        _configDirty = true;
        SaveMemory();
        SaveConfig();
        WriteTextFileCached(_statePath, JsonConvert.SerializeObject(_latestState));
    }

    private void OpenTrackerDataFolder()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(_dataDir))
            {
                return;
            }

            Directory.CreateDirectory(_dataDir);
            Process.Start("explorer.exe", "\"" + _dataDir + "\"");
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private void UpdateRunTracking(SongDescriptor song, SongMemory songMemory, SongConfig songConfig, string currentSectionName, double songTime, double songDuration, int score, int streak, int currentGhostNotes, int currentOverstrums, int currentMissedNotes, int currentNotesHit, PlayerStatsSnapshot? resultStats, bool isPractice, bool trackSongProgress, bool trackCompletedRuns)
    {
        bool newSong = !string.Equals(_runState.SongKey, song.SongKey, StringComparison.Ordinal);
        bool songTimeWentBackwards = _runState.SongKey == song.SongKey && songTime + 1.0 < _runState.LastSongTime;
        bool restarted = songTimeWentBackwards &&
            (LooksLikeActualRestart(songTime, score, streak, currentGhostNotes, currentOverstrums, currentMissedNotes) ||
             LooksLikeReturnToSongStart(_runState.LastSongTime, songTime));
        bool startedFromSongSelect = !restarted &&
            !newSong &&
            _runState.InRun &&
            _runState.CompletedRunRecorded &&
            songTime <= 1.0;
        bool started = (newSong || !_runState.InRun || startedFromSongSelect) && !restarted;
        bool newRun = started || restarted;
        bool noteSplitEnabled = IsTextExportEnabled(GetEnabledTextExportsSnapshot(), NoteSplitModeExportKey);
        if (newRun && noteSplitEnabled && _runState.InRun && !newSong && !isPractice)
        {
            SnapshotNoteSplitPreviousValidRun(songMemory);
        }
        if (newRun)
        {
            _runState = new RunState
            {
                InRun = true,
                SongKey = song.SongKey,
                CachedSongEntry = _runState.CachedSongEntry,
                CachedPlayer = _runState.CachedPlayer,
                CachedChart = _runState.CachedChart,
                CachedSongDescriptor = _runState.CachedSongDescriptor,
                CachedSongSpeed = _runState.CachedSongSpeed,
                CachedDifficulty = _runState.CachedDifficulty,
                CachedSongDuration = _runState.CachedSongDuration,
                CachedBotEnabled = _runState.CachedBotEnabled,
                LastStableRefreshAt = _runState.LastStableRefreshAt,
                CachedSongMemory = songMemory,
                CachedSongMemoryKey = song.SongKey,
                CachedSongConfig = songConfig,
                CachedSongConfigKey = song.SongKey,
                CachedNotesHit = currentNotesHit,
                LastNotesHitRefreshAt = Time.unscaledTime,
                HasCachedNotesHit = true,
                LastSection = currentSectionName,
                LastSongTime = songTime,
                LastStreak = streak,
                LastGhostNotes = currentGhostNotes,
                LastOverstrums = currentOverstrums,
                LastMissedNotes = currentMissedNotes,
                MissedNotesBaseline = _runState.MissedNotesBaseline,
                HadMiss = false,
                BestStreakThisRun = streak,
                FirstMissStreak = 0,
                CachedResultStats = null,
                LastResultStatsRefreshAt = 0f,
                HasCachedResultStats = false,
                NoteSplitCurrentSection = currentSectionName,
                NoteSplitPreviousSectionResultKind = NoteSplitResultKind.None
            };
            if (!isPractice && trackSongProgress)
            {
                songMemory.Attempts++;
                MarkMemoryDirty();
                if (restarted)
                {
                    songMemory.Restarts++;
                    MarkMemoryDirty();
                }
                else
                {
                    songMemory.Starts++;
                    MarkMemoryDirty();
                }
            }
        }
        if (noteSplitEnabled)
        {
            EnsureNoteSplitRunStateInitialized(currentSectionName);
            AdvanceNoteSplitTracking(songMemory, currentSectionName, currentOverstrums, !isPractice);
        }
        if (!isPractice)
        {
            if (trackSongProgress && currentGhostNotes > _runState.LastGhostNotes)
            {
                int ghostDelta = currentGhostNotes - _runState.LastGhostNotes;
                songMemory.LifetimeGhostedNotes += ghostDelta;
                _memory.LifetimeGhostedNotes += ghostDelta;
                MarkMemoryDirty();
            }
            bool hadExplicitMiss = currentOverstrums > _runState.LastOverstrums || currentMissedNotes > _runState.LastMissedNotes;
            if (hadExplicitMiss)
            {
                if (!_runState.HadMiss)
                {
                    if (_runState.FirstMissStreak <= 0)
                    {
                        _runState.FirstMissStreak = _runState.LastStreak;
                    }
                    if (trackSongProgress)
                    {
                        CountSectionAttempt(songMemory, currentSectionName);
                        CountClearedSectionAttempts(songMemory);
                    }
                }
                _runState.HadMiss = true;
            }
            if (!_runState.HadMiss && _runState.LastStreak > 0 && streak < _runState.LastStreak)
            {
                if (_runState.FirstMissStreak <= 0)
                {
                    _runState.FirstMissStreak = _runState.LastStreak;
                }
                if (trackSongProgress)
                {
                    CountSectionAttempt(songMemory, currentSectionName);
                    CountClearedSectionAttempts(songMemory);
                }
                _runState.HadMiss = true;
            }
            if (trackSongProgress &&
                !string.IsNullOrWhiteSpace(_runState.LastSection) &&
                !string.Equals(_runState.LastSection, currentSectionName, StringComparison.Ordinal) &&
                !_runState.HadMiss)
            {
                CountSectionClear(songMemory, _runState.LastSection);
            }
        }
        _runState.InRun = true;
        _runState.SongKey = song.SongKey;
        _runState.LastSection = currentSectionName;
        _runState.LastSongTime = songTime;
        _runState.LastStreak = streak;
        _runState.LastGhostNotes = currentGhostNotes;
        _runState.LastOverstrums = currentOverstrums;
        _runState.LastMissedNotes = currentMissedNotes;
        if (streak > _runState.BestStreakThisRun)
        {
            _runState.BestStreakThisRun = streak;
        }
        if (trackSongProgress && !isPractice && !_runState.HadMiss && streak > songMemory.BestStreak)
        {
            songMemory.BestStreak = streak;
            MarkMemoryDirty();
        }
        bool finishedSong = songDuration > 1 &&
            (songTime >= songDuration - 0.35 || songTime > songDuration);
        bool fcThisRun =
            !_runState.HadMiss &&
            currentMissedNotes == 0 &&
            currentOverstrums == 0;
        if (noteSplitEnabled && finishedSong)
        {
            FinalizeNoteSplitCurrentSection(songMemory, currentSectionName, currentOverstrums, !isPractice);
        }
        if (finishedSong && _runState.FirstMissStreak <= 0)
        {
            _runState.FirstMissStreak = Math.Max(_runState.BestStreakThisRun, streak);
        }
        if (trackSongProgress && !isPractice && finishedSong && fcThisRun)
        {
            CountSectionClear(songMemory, currentSectionName);
        }
        if (trackSongProgress && !isPractice && finishedSong)
        {
            bool songPersonalBestImproved = TryUpdateSongPersonalBestRun(songMemory, currentMissedNotes, currentOverstrums);
            if (noteSplitEnabled && songPersonalBestImproved)
            {
                ApplyNoteSplitSongPersonalBestSnapshot(songMemory);
            }
        }
        if (trackCompletedRuns && !isPractice && finishedSong && !_runState.CompletedRunRecorded)
        {
            int percent = CalculateRunPercent(resultStats, currentNotesHit, currentMissedNotes, currentOverstrums, fcThisRun);
            songMemory.CompletedRuns.Add(new CompletedRunRecord
            {
                Index = songMemory.CompletedRuns.Count + 1,
                CompletedAtUtc = DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Score = score,
                BestStreak = _runState.BestStreakThisRun,
                FirstMissStreak = _runState.FirstMissStreak,
                GhostedNotes = currentGhostNotes,
                Overstrums = currentOverstrums,
                MissedNotes = currentMissedNotes,
                Percent = percent,
                FcAchieved = fcThisRun,
                FinalSection = currentSectionName
            });
            _runState.CompletedRunRecorded = true;
            MarkMemoryDirty();
        }
        if (trackSongProgress && !isPractice && !songMemory.FcAchieved && finishedSong && fcThisRun)
        {
            songMemory.FcAchieved = true;
            MarkMemoryDirty();
        }
        else if (trackSongProgress && !isPractice && (_runState.HadMiss || (finishedSong && !fcThisRun)))
        {
            if (songMemory.FcAchieved)
            {
                songMemory.FcAchieved = false;
                MarkMemoryDirty();
            }
        }
    }

    private void EnsureNoteSplitRunStateInitialized(string currentSectionName)
    {
        if (!string.IsNullOrWhiteSpace(_runState.NoteSplitCurrentSection))
        {
            ApplyPendingNoteSplitMisses(_runState.NoteSplitCurrentSection);
            return;
        }

        _runState.NoteSplitCurrentSection = currentSectionName;
        ApplyPendingNoteSplitMisses(currentSectionName);
    }

    private void AdvanceNoteSplitTracking(SongMemory songMemory, string currentSectionName, int currentOverstrums, bool allowMemoryUpdates)
    {
        if (string.IsNullOrWhiteSpace(currentSectionName))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(_runState.NoteSplitCurrentSection))
        {
            _runState.NoteSplitCurrentSection = currentSectionName;
            ApplyPendingNoteSplitMisses(currentSectionName);
            return;
        }

        if (Mathf.Abs(Time.timeScale) <= 0.001f)
        {
            ApplyPendingNoteSplitMisses(_runState.NoteSplitCurrentSection);
            return;
        }

        if (string.Equals(_runState.NoteSplitCurrentSection, currentSectionName, StringComparison.Ordinal))
        {
            ApplyPendingNoteSplitMisses(currentSectionName);
            return;
        }

        if (_runState.NoteSplitSectionsThisRun.ContainsKey(currentSectionName))
        {
            ApplyPendingNoteSplitMisses(_runState.NoteSplitCurrentSection);
            return;
        }

        ApplyPendingNoteSplitMisses(_runState.NoteSplitCurrentSection);
        CommitNoteSplitSection(songMemory, _runState.NoteSplitCurrentSection, updateFooterSnapshot: true);
        _runState.NoteSplitCurrentSection = currentSectionName;
    }

    private void FinalizeNoteSplitCurrentSection(SongMemory songMemory, string currentSectionName, int currentOverstrums, bool allowMemoryUpdates)
    {
        string sectionName = string.IsNullOrWhiteSpace(_runState.NoteSplitCurrentSection)
            ? currentSectionName
            : _runState.NoteSplitCurrentSection;
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return;
        }

        ApplyPendingNoteSplitMisses(sectionName);
        CommitNoteSplitSection(songMemory, sectionName, updateFooterSnapshot: false);
    }

    private void CommitNoteSplitSection(SongMemory songMemory, string sectionName, bool updateFooterSnapshot)
    {
        if (string.IsNullOrWhiteSpace(sectionName) ||
            _runState.NoteSplitSectionsThisRun.ContainsKey(sectionName))
        {
            return;
        }

        int sectionMissCount = _runState.NoteSplitMissCountsBySectionThisRun.TryGetValue(sectionName, out int trackedMissCount)
            ? Math.Max(0, trackedMissCount)
            : 0;
        SectionMemory sectionMemory = EnsureSectionMemory(songMemory, sectionName);
        string resultKind = DetermineNoteSplitResultKind(sectionMemory.BestMissCount, sectionMissCount);

        _runState.NoteSplitSectionsThisRun[sectionName] = new NoteSplitSectionRunState
        {
            MissCount = sectionMissCount,
            ResultKind = resultKind
        };

        if (updateFooterSnapshot)
        {
            _runState.NoteSplitPreviousSection = sectionName;
            _runState.NoteSplitPreviousSectionMissCount = sectionMissCount;
            _runState.NoteSplitPreviousSectionResultKind = resultKind;
        }
    }

    private static string DetermineNoteSplitResultKind(int? bestMissCount, int currentMissCount)
    {
        if (!bestMissCount.HasValue)
        {
            return NoteSplitResultKind.FirstScan;
        }

        if (currentMissCount < bestMissCount.Value)
        {
            return currentMissCount == 0 && bestMissCount.Value > 0
                ? NoteSplitResultKind.PerfectImprovement
                : NoteSplitResultKind.Improved;
        }

        if (currentMissCount == bestMissCount.Value)
        {
            return NoteSplitResultKind.Tie;
        }

        return NoteSplitResultKind.Worse;
    }

    private bool TryUpdateSongPersonalBestRun(SongMemory songMemory, int currentMissedNotes, int currentOverstrums)
    {
        int clampedMissedNotes = Math.Max(0, currentMissedNotes);
        int clampedOverstrums = Math.Max(0, currentOverstrums);
        GetSongPersonalBestRun(songMemory, out int? bestMissCount, out int? bestOverstrums);
        bool improved =
            !bestMissCount.HasValue ||
            clampedMissedNotes < bestMissCount.Value ||
            (clampedMissedNotes == bestMissCount.Value &&
             (!bestOverstrums.HasValue || clampedOverstrums < bestOverstrums.Value));
        if (!improved)
        {
            return false;
        }

        songMemory.BestRunMissedNotes = clampedMissedNotes;
        songMemory.BestRunOverstrums = clampedOverstrums;
        MarkMemoryDirty();
        return true;
    }

    private void ApplyNoteSplitSongPersonalBestSnapshot(SongMemory songMemory)
    {
        foreach (KeyValuePair<string, NoteSplitSectionRunState> pair in _runState.NoteSplitSectionsThisRun)
        {
            SectionMemory sectionMemory = EnsureSectionMemory(songMemory, pair.Key);
            sectionMemory.BestMissCount = pair.Value.MissCount;
        }

        MarkMemoryDirty();
    }

    private static void GetSongPersonalBestRun(SongMemory songMemory, out int? bestMissCount, out int? bestOverstrums)
    {
        bestMissCount = songMemory.BestRunMissedNotes;
        bestOverstrums = songMemory.BestRunOverstrums;

        foreach (CompletedRunRecord run in songMemory.CompletedRuns)
        {
            int runMissedNotes = Math.Max(0, run.MissedNotes);
            int runOverstrums = Math.Max(0, run.Overstrums);
            bool improved =
                !bestMissCount.HasValue ||
                runMissedNotes < bestMissCount.Value ||
                (runMissedNotes == bestMissCount.Value &&
                 (!bestOverstrums.HasValue || runOverstrums < bestOverstrums.Value));
            if (!improved)
            {
                continue;
            }

            bestMissCount = runMissedNotes;
            bestOverstrums = runOverstrums;
        }
    }

    private void CountSectionAttempt(SongMemory songMemory, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return;
        }

        SectionMemory sectionMemory = EnsureSectionMemory(songMemory, sectionName);
        sectionMemory.Attempts++;
        sectionMemory.KilledTheRun++;
        MarkMemoryDirty();
    }

    private void CountClearedSectionAttempts(SongMemory songMemory)
    {
        foreach (string sectionName in _runState.CountedSectionsThisRun)
        {
            if (_runState.CountedSectionAttemptsThisRun.Add(sectionName))
            {
                SectionMemory sectionMemory = EnsureSectionMemory(songMemory, sectionName);
                sectionMemory.Attempts++;
                MarkMemoryDirty();
            }
        }
    }

    private void CountSectionClear(SongMemory songMemory, string sectionName)
    {
        if (string.IsNullOrWhiteSpace(sectionName))
        {
            return;
        }
        if (!_runState.CountedSectionsThisRun.Add(sectionName))
        {
            return;
        }
        SectionMemory sectionMemory = EnsureSectionMemory(songMemory, sectionName);
        sectionMemory.RunsPast++;
        MarkMemoryDirty();
    }

    private static int CalculateRunPercent(PlayerStatsSnapshot? resultStats, int currentNotesHit, int currentMissedNotes, int currentOverstrums, bool fcThisRun)
    {
        if (resultStats != null && resultStats.Accuracy > 0)
        {
            return Math.Max(0, Math.Min(100, resultStats.Accuracy));
        }

        if (resultStats != null && resultStats.TotalNotes > 0)
        {
            double percent = (double)resultStats.NotesHit * 100d / resultStats.TotalNotes;
            return Math.Max(0, Math.Min(100, (int)Math.Floor(percent)));
        }

        int derivedTotalNotes = currentNotesHit + currentMissedNotes;
        if (derivedTotalNotes > 0)
        {
            double percent = (double)currentNotesHit * 100d / derivedTotalNotes;
            return Math.Max(0, Math.Min(100, (int)Math.Floor(percent)));
        }

        if (fcThisRun)
        {
            return 100;
        }

        return currentMissedNotes <= 0 && currentOverstrums <= 0 ? 100 : 0;
    }

    private void SnapshotNoteSplitPreviousValidRun(SongMemory songMemory)
    {
        foreach (SectionMemory sectionMemory in songMemory.Sections.Values)
        {
            sectionMemory.PreviousValidRunMissCount = null;
        }

        foreach (KeyValuePair<string, int> pair in BuildPreviousValidNoteSplitRunSnapshot())
        {
            EnsureSectionMemory(songMemory, pair.Key).PreviousValidRunMissCount = pair.Value;
        }

        MarkMemoryDirty();
    }

    private Dictionary<string, int> BuildPreviousValidNoteSplitRunSnapshot()
    {
        var snapshot = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, NoteSplitSectionRunState> pair in _runState.NoteSplitSectionsThisRun)
        {
            snapshot[pair.Key] = pair.Value.MissCount;
        }

        if (!string.IsNullOrWhiteSpace(_runState.NoteSplitCurrentSection))
        {
            int missCount = _runState.NoteSplitMissCountsBySectionThisRun.TryGetValue(_runState.NoteSplitCurrentSection, out int trackedMissCount)
                ? trackedMissCount
                : 0;
            if (_runState.PendingNoteSplitMisses > 0)
            {
                missCount += _runState.PendingNoteSplitMisses;
            }

            snapshot[_runState.NoteSplitCurrentSection] = missCount;
        }

        return snapshot;
    }

    private void ResetRunIfNeeded()
    {
        if (_runState.InRun)
        {
            _runState = new RunState();
            _ghostNotesFieldCalibrated = false;
            _playerTypeCachedForStats = null;
        }
    }

    private TrackerState CreateIdleState()
    {
        return new TrackerState
        {
            IsInSong = false,
            OverlayEditorVisible = _overlayEditorVisible,
            EnabledTextExports = _disabledTextExportSnapshot
        };
    }

    private void EnsureDesktopOverlayStarted()
    {
        if (!_initialized)
        {
            return;
        }

        if (Time.unscaledTime - _lastDesktopOverlayCheckAt < DesktopOverlayCheckIntervalSeconds)
        {
            return;
        }

        _lastDesktopOverlayCheckAt = Time.unscaledTime;

        if (_desktopOverlayProcess != null)
        {
            try
            {
                if (!_desktopOverlayProcess.HasExited)
                {
                    return;
                }
            }
            catch
            {
            }

            try
            {
                _desktopOverlayProcess.Dispose();
            }
            catch
            {
            }

            _desktopOverlayProcess = null;
        }

        string overlayExePath = GetDesktopOverlayExePath();
        if (!File.Exists(overlayExePath))
        {
            if (!_desktopOverlayLaunchFailed)
            {
                _desktopOverlayLaunchFailed = true;
                StockTrackerLog.Write("DesktopOverlayMissing | path=" + overlayExePath);
            }

            return;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = overlayExePath,
                Arguments = $"--pid {Process.GetCurrentProcess().Id} --data-dir \"{_dataDir}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            _desktopOverlayProcess = Process.Start(startInfo);
            _desktopOverlayLaunchFailed = _desktopOverlayProcess == null;
            StockTrackerLog.WriteDebug("DesktopOverlayStart | path=" + overlayExePath + " | started=" + (_desktopOverlayProcess != null ? "1" : "0"));
        }
        catch (Exception ex)
        {
            _desktopOverlayLaunchFailed = true;
            StockTrackerLog.Write("DesktopOverlayStartFailed | path=" + overlayExePath);
            StockTrackerLog.Write(ex);
        }
    }

    private bool IsDesktopOverlayRunning()
    {
        if (_desktopOverlayProcess == null)
        {
            return false;
        }

        try
        {
            return !_desktopOverlayProcess.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private static string GetDesktopOverlayExePath()
    {
        string assemblyLocation = Assembly.GetExecutingAssembly().Location;
        if (!string.IsNullOrWhiteSpace(assemblyLocation))
        {
            string assemblyDir = Path.GetDirectoryName(assemblyLocation) ?? string.Empty;
            string assemblyPath = Path.Combine(assemblyDir, DesktopOverlayExeName);
            if (File.Exists(assemblyPath))
            {
                return assemblyPath;
            }
        }

        string currentDir = Environment.CurrentDirectory ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(currentDir))
        {
            string managedPath = Path.Combine(currentDir, "Clone Hero_Data", "Managed", DesktopOverlayExeName);
            if (File.Exists(managedPath))
            {
                return managedPath;
            }

            string currentDirPath = Path.Combine(currentDir, DesktopOverlayExeName);
            if (File.Exists(currentDirPath))
            {
                return currentDirPath;
            }

            return managedPath;
        }

        return DesktopOverlayExeName;
    }

    private void ExportState(TrackerState state)
    {
        bool shouldExportStateJson = ShouldExportStateJson(state);
        bool shouldExportObs = ShouldExportObs(state);
        bool forceStateExport = _forceStateExportPending;
        bool forceObsExport = _forceObsExportPending;
        bool exportStateJson = false;
        bool exportObs = false;
        if (forceStateExport ||
            (shouldExportStateJson &&
            Time.unscaledTime - _lastStateExportAt >= StateExportIntervalSeconds))
        {
            _lastStateExportAt = Time.unscaledTime;
            exportStateJson = true;
            _forceStateExportPending = false;
        }

        if (forceObsExport ||
            (shouldExportObs &&
            Time.unscaledTime - _lastObsExportAt >= ObsExportIntervalSeconds))
        {
            _lastObsExportAt = Time.unscaledTime;
            exportObs = true;
            _forceObsExportPending = false;
        }

        if (!exportStateJson && !exportObs)
        {
            return;
        }

        lock (_exportWorkerSync)
        {
            if (_pendingExport != null)
            {
                exportStateJson |= _pendingExport.ExportStateJson;
                exportObs |= _pendingExport.ExportObs;
            }

            _pendingExport = new ExportWorkItem
            {
                State = state,
                ExportStateJson = exportStateJson,
                ExportObs = exportObs,
                ExportTemplateOverrides = CloneStringDictionary(_config.ExportTemplateOverrides),
                ExportTemplateVersion = _exportTemplateVersion
            };
        }

        _exportSignal.Set();
    }

    private void RequestImmediateExportRefresh(bool includeStateExport, bool includeObsExport)
    {
        if (includeStateExport)
        {
            _forceStateExportPending = true;
        }

        if (includeObsExport)
        {
            _forceObsExportPending = true;
            _obsCleanupPending = true;
        }
    }

    private bool ShouldExportStateJson(TrackerState state)
    {
        if (ShouldUseDesktopOverlay(state))
        {
            _lastDesktopOverlayNeededAt = Time.unscaledTime;
            return true;
        }

        return Time.unscaledTime - _lastDesktopOverlayNeededAt < DesktopOverlayStateExportGraceSeconds;
    }

    private bool ShouldExportObs(TrackerState state)
    {
        if (HasAnyTextExportEnabled(state))
        {
            _obsCleanupPending = true;
            return true;
        }

        if (_obsCleanupPending)
        {
            _obsCleanupPending = false;
            return true;
        }

        return false;
    }

    private void ExportObsState(TrackerState state, string? stateJson, IReadOnlyDictionary<string, string> exportTemplateOverrides, int exportTemplateVersion)
    {
        if (!HasAnyTextExportEnabled(state))
        {
            DeleteObsText(_obsStatePath);
            DeleteObsDirectory(Path.Combine(_obsDir, "current"));
            if (state.Song != null)
            {
                DeleteObsDirectory(Path.Combine(_obsDir, "songs", SanitizeFileName(state.Song.SongKey)));
            }

            return;
        }

        stateJson ??= JsonConvert.SerializeObject(state);
        WriteTextFileCached(_obsStatePath, stateJson);

        string currentDir = Path.Combine(_obsDir, "current");
        Directory.CreateDirectory(currentDir);
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_section"), Path.Combine(currentDir, "current_section.txt"), RenderExportTemplate("metric.current_section", CreateMetricTemplateContext(state, "Current Section", state.CurrentSection ?? string.Empty), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "streak"), Path.Combine(currentDir, "streak.txt"), RenderExportTemplate("metric.streak", CreateMetricTemplateContext(state, "Current Streak", state.Streak.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "best_streak"), Path.Combine(currentDir, "best_streak.txt"), RenderExportTemplate("metric.best_streak", CreateMetricTemplateContext(state, "Best FC Streak", state.BestStreak.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "attempts"), Path.Combine(currentDir, "attempts.txt"), RenderExportTemplate("metric.attempts", CreateMetricTemplateContext(state, "Total Attempts", state.Attempts.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_ghosted_notes"), Path.Combine(currentDir, "current_ghosted_notes.txt"), RenderExportTemplate("metric.current_ghosted_notes", CreateMetricTemplateContext(state, "Current Ghosted Notes", state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_overstrums"), Path.Combine(currentDir, "current_overstrums.txt"), RenderExportTemplate("metric.current_overstrums", CreateMetricTemplateContext(state, "Current Overstrums", state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_missed_notes"), Path.Combine(currentDir, "current_missed_notes.txt"), RenderExportTemplate("metric.current_missed_notes", CreateMetricTemplateContext(state, "Current Missed Notes", state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "lifetime_ghosted_notes"), Path.Combine(currentDir, "lifetime_ghosted_notes.txt"), RenderExportTemplate("metric.lifetime_ghosted_notes", CreateMetricTemplateContext(state, "Song Lifetime Ghosted Notes", state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "global_lifetime_ghosted_notes"), Path.Combine(currentDir, "global_lifetime_ghosted_notes.txt"), RenderExportTemplate("metric.global_lifetime_ghosted_notes", CreateMetricTemplateContext(state, "Global Lifetime Ghosted Notes", state.GlobalLifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "fc_achieved"), Path.Combine(currentDir, "fc_achieved.txt"), RenderExportTemplate("metric.fc_achieved", CreateMetricTemplateContext(state, "FC Achieved", state.FcAchieved ? "True" : "False"), exportTemplateOverrides, exportTemplateVersion));

        bool exportTrackedSectionFiles = state.SectionStats.Any(candidate => candidate.Tracked);
        if (state.CurrentSectionStats != null && exportTrackedSectionFiles)
        {
            WriteObsText(
                Path.Combine(currentDir, "current_section_summary.txt"),
                RenderExportTemplate(
                    "section.current_summary",
                    CreateSectionTemplateContext(state, state.CurrentSectionStats, state.CurrentSectionStats.Name, "Current Section Summary", string.Empty),
                    exportTemplateOverrides,
                    exportTemplateVersion));
        }
        else
        {
            DeleteObsText(Path.Combine(currentDir, "current_section_summary.txt"));
        }

        if (state.Song == null)
        {
            return;
        }

        string songDir = Path.Combine(_obsDir, "songs", SanitizeFileName(state.Song.SongKey));
        Directory.CreateDirectory(songDir);
        WriteOrDeleteObsText(IsTextExportEnabled(state, "attempts"), Path.Combine(songDir, "attempts.txt"), RenderExportTemplate("metric.attempts", CreateMetricTemplateContext(state, "Total Attempts", state.Attempts.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_ghosted_notes"), Path.Combine(songDir, "current_ghosted_notes.txt"), RenderExportTemplate("metric.current_ghosted_notes", CreateMetricTemplateContext(state, "Current Ghosted Notes", state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_overstrums"), Path.Combine(songDir, "current_overstrums.txt"), RenderExportTemplate("metric.current_overstrums", CreateMetricTemplateContext(state, "Current Overstrums", state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_missed_notes"), Path.Combine(songDir, "current_missed_notes.txt"), RenderExportTemplate("metric.current_missed_notes", CreateMetricTemplateContext(state, "Current Missed Notes", state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "lifetime_ghosted_notes"), Path.Combine(songDir, "lifetime_ghosted_notes.txt"), RenderExportTemplate("metric.lifetime_ghosted_notes", CreateMetricTemplateContext(state, "Song Lifetime Ghosted Notes", state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
        string currentSectionExportName = state.CurrentSectionStats != null
            ? BuildSectionExportName(state.SectionStats, state.CurrentSectionStats)
            : state.CurrentSection ?? string.Empty;
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_section"), Path.Combine(songDir, "current_section.txt"), RenderExportTemplate("metric.current_section", CreateMetricTemplateContext(state, "Current Section", currentSectionExportName), exportTemplateOverrides, exportTemplateVersion));

        string sectionsDir = Path.Combine(songDir, "sections");
        bool exportAnySectionFiles = exportTrackedSectionFiles;
        if (exportAnySectionFiles)
        {
            Directory.CreateDirectory(sectionsDir);
            foreach (SectionStatsState section in state.SectionStats.Where(candidate => candidate.Tracked))
            {
                string sectionExportName = BuildSectionExportName(state.SectionStats, section);
                string sectionDir = Path.Combine(sectionsDir, SanitizeFileName(sectionExportName));
                Directory.CreateDirectory(sectionDir);
                WriteObsText(Path.Combine(sectionDir, "name.txt"), RenderExportTemplate("section.name", CreateSectionTemplateContext(state, section, sectionExportName, "Section Name", sectionExportName), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(sectionDir, "summary.txt"), RenderExportTemplate("section.summary", CreateSectionTemplateContext(state, section, sectionExportName, "Section Summary", string.Empty), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(sectionDir, "attempts.txt"), RenderExportTemplate("section.attempts", CreateSectionTemplateContext(state, section, sectionExportName, "Attempts", section.Attempts.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(sectionDir, "fcs_past.txt"), RenderExportTemplate("section.fcs_past", CreateSectionTemplateContext(state, section, sectionExportName, "FCs Past", section.RunsPast.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(sectionDir, "killed_the_run.txt"), RenderExportTemplate("section.killed_the_run", CreateSectionTemplateContext(state, section, sectionExportName, "Killed the Run", section.KilledTheRun.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                DeleteObsText(Path.Combine(sectionDir, "tracked.txt"));
                DeleteObsText(Path.Combine(sectionDir, "start_time.txt"));
            }
        }
        else
        {
            DeleteObsDirectory(sectionsDir);
        }

        string runsDir = Path.Combine(songDir, "runs");
        if (IsTextExportEnabled(state, "completed_runs"))
        {
            Directory.CreateDirectory(runsDir);
            foreach (CompletedRunRecord run in state.CompletedRuns)
            {
                string runDir = Path.Combine(runsDir, BuildCompletedRunDirectoryName(run));
                Directory.CreateDirectory(runDir);
                WriteObsText(Path.Combine(runDir, "completed_at_utc.txt"), RenderExportTemplate("run.completed_at_utc", CreateRunTemplateContext(state, run, "Completed At UTC", run.CompletedAtUtc ?? string.Empty), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "percent.txt"), RenderExportTemplate("run.percent", CreateRunTemplateContext(state, run, "Percent", run.Percent.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "score.txt"), RenderExportTemplate("run.score", CreateRunTemplateContext(state, run, "Score", run.Score.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "best_streak.txt"), RenderExportTemplate("run.best_streak", CreateRunTemplateContext(state, run, "Best Streak", run.BestStreak.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "first_miss_streak.txt"), RenderExportTemplate("run.first_miss_streak", CreateRunTemplateContext(state, run, "First Miss Streak", run.FirstMissStreak.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "ghosted_notes.txt"), RenderExportTemplate("run.ghosted_notes", CreateRunTemplateContext(state, run, "Ghosted Notes", run.GhostedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "overstrums.txt"), RenderExportTemplate("run.overstrums", CreateRunTemplateContext(state, run, "Overstrums", run.Overstrums.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "missed_notes.txt"), RenderExportTemplate("run.missed_notes", CreateRunTemplateContext(state, run, "Missed Notes", run.MissedNotes.ToString(CultureInfo.InvariantCulture)), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "fc_achieved.txt"), RenderExportTemplate("run.fc_achieved", CreateRunTemplateContext(state, run, "FC Achieved", run.FcAchieved ? "True" : "False"), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "final_section.txt"), RenderExportTemplate("run.final_section", CreateRunTemplateContext(state, run, "Final Section", run.FinalSection ?? string.Empty), exportTemplateOverrides, exportTemplateVersion));
                WriteObsText(Path.Combine(runDir, "summary.txt"), RenderExportTemplate("run.summary", CreateRunTemplateContext(state, run, "Run Summary", string.Empty), exportTemplateOverrides, exportTemplateVersion));
            }
        }
        else
        {
            DeleteObsDirectory(runsDir);
        }
    }

    private void EnsureExportWorkerStarted()
    {
        if (_exportThread != null)
        {
            return;
        }

        _exportThread = new Thread(ExportWorkerLoop)
        {
            IsBackground = true,
            Name = "CloneHeroSectionTracker.ExportWorker"
        };
        _exportThread.Start();
    }

    private void ExportWorkerLoop()
    {
        while (true)
        {
            try
            {
                _exportSignal.WaitOne();

                while (true)
                {
                    ExportWorkItem? workItem;
                    lock (_exportWorkerSync)
                    {
                        workItem = _pendingExport;
                        _pendingExport = null;
                    }

                    if (workItem == null)
                    {
                        break;
                    }

                    string? stateJson = null;
                    if (workItem.ExportStateJson)
                    {
                        stateJson = JsonConvert.SerializeObject(workItem.State);
                        WriteTextFileCached(_statePath, stateJson);
                    }

                    if (workItem.ExportObs)
                    {
                        ExportObsState(workItem.State, stateJson, workItem.ExportTemplateOverrides, workItem.ExportTemplateVersion);
                    }
                }
            }
            catch (Exception ex)
            {
                StockTrackerLog.Write(ex);
            }
        }
    }

    private void SaveMemory()
    {
        if (!_memoryDirty)
        {
            return;
        }

        QueuePersistenceWrite(_memoryPath, BuildMemoryWriteSnapshot());
        _memoryDirty = false;
    }

    private void SaveConfig()
    {
        if (!_configDirty)
        {
            return;
        }

        _config.DesktopOverlayStyle = SaveDesktopOverlayStyle();
        QueuePersistenceWrite(_configPath, BuildConfigWriteSnapshot());
        _configDirty = false;
    }

    private DesktopOverlayStyleConfig SaveDesktopOverlayStyle()
    {
        DesktopOverlayStyleConfig style = GetMergedDesktopOverlayStyle();
        _config.DesktopOverlayStyle = CloneDesktopOverlayStyle(style);
        if (string.IsNullOrWhiteSpace(_desktopStylePath))
        {
            return style;
        }

        QueuePersistenceWrite(_desktopStylePath, CloneDesktopOverlayStyle(style));
        return style;
    }

    private TrackerMemory BuildMemoryWriteSnapshot()
    {
        if (_memoryWriteSnapshot == null || _memoryWriteSnapshotRequiresFullRefresh)
        {
            _memoryWriteSnapshot = CloneTrackerMemory(_memory);
            _dirtyMemorySongKeys.Clear();
            _memoryWriteSnapshotRequiresFullRefresh = false;
            return _memoryWriteSnapshot;
        }

        TrackerMemory next = new TrackerMemory
        {
            LifetimeGhostedNotes = _memory.LifetimeGhostedNotes
        };
        foreach (KeyValuePair<string, SongMemory> pair in _memoryWriteSnapshot.Songs)
        {
            next.Songs[pair.Key] = pair.Value;
        }

        foreach (string songKey in _dirtyMemorySongKeys)
        {
            if (_memory.Songs.TryGetValue(songKey, out SongMemory? songMemory))
            {
                next.Songs[songKey] = songMemory == null ? new SongMemory() : CloneSongMemory(songMemory);
            }
            else
            {
                next.Songs.Remove(songKey);
            }
        }

        _dirtyMemorySongKeys.Clear();
        _memoryWriteSnapshot = next;
        return next;
    }

    private TrackerConfig BuildConfigWriteSnapshot()
    {
        if (_configWriteSnapshot == null || _configWriteSnapshotRequiresFullRefresh)
        {
            _configWriteSnapshot = CloneTrackerConfig(_config);
            _configWriteSnapshot.DesktopOverlayStyle = CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle);
            _dirtyConfigSongKeys.Clear();
            _configWriteSnapshotRequiresFullRefresh = false;
            return _configWriteSnapshot;
        }

        TrackerConfig next = new TrackerConfig
        {
            OverlayEditor = CloneOverlayEditorConfig(_config.OverlayEditor ?? new OverlayEditorConfig()),
            ExportTemplateEditor = CloneExportTemplateEditorConfig(_config.ExportTemplateEditor ?? new ExportTemplateEditorConfig()),
            DesktopOverlayStyle = CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle),
            ExportTemplateOverrides = CloneStringDictionary(_config.ExportTemplateOverrides),
            AnimatedMenuTintR = _config.AnimatedMenuTintR,
            AnimatedMenuTintG = _config.AnimatedMenuTintG,
            AnimatedMenuTintB = _config.AnimatedMenuTintB,
            AnimatedMenuTintA = _config.AnimatedMenuTintA,
            AnimatedMenuWispR = _config.AnimatedMenuWispR,
            AnimatedMenuWispG = _config.AnimatedMenuWispG,
            AnimatedMenuWispB = _config.AnimatedMenuWispB,
            AnimatedMenuWispA = _config.AnimatedMenuWispA,
            AnimatedMenuWispSize = _config.AnimatedMenuWispSize,
            AnimatedMenuTintBackgroundOverlayStrength = _config.AnimatedMenuTintBackgroundOverlayStrength,
            AnimatedMenuTintCanvasOverlayStrength = _config.AnimatedMenuTintCanvasOverlayStrength,
            AnimatedMenuTintRawImageStrength = _config.AnimatedMenuTintRawImageStrength,
            AnimatedMenuTintMaterialStrength = _config.AnimatedMenuTintMaterialStrength,
            AnimatedMenuTintOnGuiOverlayStrength = _config.AnimatedMenuTintOnGuiOverlayStrength
        };
        foreach (KeyValuePair<string, bool> pair in EnsureDefaultEnabledTextExports())
        {
            next.DefaultEnabledTextExports[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, SongConfig> pair in _configWriteSnapshot.Songs)
        {
            next.Songs[pair.Key] = pair.Value;
        }

        foreach (string songKey in _dirtyConfigSongKeys)
        {
            if (_config.Songs.TryGetValue(songKey, out SongConfig? songConfig))
            {
                next.Songs[songKey] = songConfig == null ? new SongConfig() : CloneSongConfig(songConfig);
            }
            else
            {
                next.Songs.Remove(songKey);
            }
        }

        _dirtyConfigSongKeys.Clear();
        _configWriteSnapshot = next;
        return next;
    }

    private void RefreshMergedDesktopOverlayStyle()
    {
        DesktopOverlayStyleConfig configStyle = _config.DesktopOverlayStyle ?? new DesktopOverlayStyleConfig();
        if (string.IsNullOrWhiteSpace(_desktopStylePath) || !File.Exists(_desktopStylePath))
        {
            _mergedDesktopOverlayStyle = CloneDesktopOverlayStyle(configStyle);
            _config.DesktopOverlayStyle = CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle);
            return;
        }

        DesktopOverlayStyleConfig merged = LoadJson(_desktopStylePath, CloneDesktopOverlayStyle(configStyle));
        merged.BorderR = configStyle.BorderR;
        merged.BorderG = configStyle.BorderG;
        merged.BorderB = configStyle.BorderB;
        merged.BorderA = configStyle.BorderA;
        _mergedDesktopOverlayStyle = merged;
        _config.DesktopOverlayStyle = CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle);
    }

    private DesktopOverlayStyleConfig GetMergedDesktopOverlayStyle()
    {
        return CloneDesktopOverlayStyle(_mergedDesktopOverlayStyle);
    }

    private static DesktopOverlayStyleConfig CloneDesktopOverlayStyle(DesktopOverlayStyleConfig style)
    {
        return new DesktopOverlayStyleConfig
        {
            BorderR = style.BorderR,
            BorderG = style.BorderG,
            BorderB = style.BorderB,
            BorderA = style.BorderA,
            NoteSplitX = style.NoteSplitX,
            NoteSplitY = style.NoteSplitY,
            NoteSplitWidth = style.NoteSplitWidth,
            NoteSplitHeight = style.NoteSplitHeight,
            NoteSplitFontFamily = style.NoteSplitFontFamily,
            NoteSplitFontScale = style.NoteSplitFontScale,
            NoteSplitTopMost = style.NoteSplitTopMost
        };
    }

    private static OverlayEditorConfig CloneOverlayEditorConfig(OverlayEditorConfig config)
    {
        return new OverlayEditorConfig
        {
            X = config.X,
            Y = config.Y,
            Width = config.Width,
            Height = config.Height,
            BackgroundA = config.BackgroundA,
            ResizeHandleHidden = config.ResizeHandleHidden
        };
    }

    private static ExportTemplateEditorConfig CloneExportTemplateEditorConfig(ExportTemplateEditorConfig config)
    {
        return new ExportTemplateEditorConfig
        {
            X = config.X,
            Y = config.Y,
            Width = config.Width,
            Height = config.Height,
            ResizeHandleHidden = config.ResizeHandleHidden
        };
    }

    private static OverlayWidgetConfig CloneOverlayWidgetConfig(OverlayWidgetConfig config)
    {
        return new OverlayWidgetConfig
        {
            Enabled = config.Enabled,
            X = config.X,
            Y = config.Y,
            Width = config.Width,
            Height = config.Height,
            FontScale = config.FontScale,
            ZIndex = config.ZIndex,
            ResizeModeVersion = config.ResizeModeVersion,
            ResizeHandleHidden = config.ResizeHandleHidden,
            BackgroundR = config.BackgroundR,
            BackgroundG = config.BackgroundG,
            BackgroundB = config.BackgroundB,
            BackgroundA = config.BackgroundA
        };
    }

    private static SongConfig CloneSongConfig(SongConfig config)
    {
        SongConfig clone = new SongConfig
        {
            Title = config.Title,
            Artist = config.Artist,
            Charter = config.Charter
        };

        foreach (KeyValuePair<string, bool> pair in config.TrackedSections)
        {
            clone.TrackedSections[pair.Key] = pair.Value;
        }

        foreach (KeyValuePair<string, OverlayWidgetConfig> pair in config.OverlayWidgets)
        {
            clone.OverlayWidgets[pair.Key] = pair.Value == null ? new OverlayWidgetConfig() : CloneOverlayWidgetConfig(pair.Value);
        }

        return clone;
    }

    private static TrackerConfig CloneTrackerConfig(TrackerConfig config)
    {
        TrackerConfig clone = new TrackerConfig
        {
            OverlayEditor = CloneOverlayEditorConfig(config.OverlayEditor ?? new OverlayEditorConfig()),
            ExportTemplateEditor = CloneExportTemplateEditorConfig(config.ExportTemplateEditor ?? new ExportTemplateEditorConfig()),
            DesktopOverlayStyle = CloneDesktopOverlayStyle(config.DesktopOverlayStyle ?? new DesktopOverlayStyleConfig()),
            ExportTemplateOverrides = CloneStringDictionary(config.ExportTemplateOverrides),
            AnimatedMenuTintR = config.AnimatedMenuTintR,
            AnimatedMenuTintG = config.AnimatedMenuTintG,
            AnimatedMenuTintB = config.AnimatedMenuTintB,
            AnimatedMenuTintA = config.AnimatedMenuTintA,
            AnimatedMenuWispR = config.AnimatedMenuWispR,
            AnimatedMenuWispG = config.AnimatedMenuWispG,
            AnimatedMenuWispB = config.AnimatedMenuWispB,
            AnimatedMenuWispA = config.AnimatedMenuWispA,
            AnimatedMenuWispSize = config.AnimatedMenuWispSize,
            AnimatedMenuTintBackgroundOverlayStrength = config.AnimatedMenuTintBackgroundOverlayStrength,
            AnimatedMenuTintCanvasOverlayStrength = config.AnimatedMenuTintCanvasOverlayStrength,
            AnimatedMenuTintRawImageStrength = config.AnimatedMenuTintRawImageStrength,
            AnimatedMenuTintMaterialStrength = config.AnimatedMenuTintMaterialStrength,
            AnimatedMenuTintOnGuiOverlayStrength = config.AnimatedMenuTintOnGuiOverlayStrength
        };

        foreach (KeyValuePair<string, SongConfig> pair in config.Songs)
        {
            clone.Songs[pair.Key] = pair.Value == null ? new SongConfig() : CloneSongConfig(pair.Value);
        }

        foreach (KeyValuePair<string, bool> pair in config.DefaultEnabledTextExports)
        {
            clone.DefaultEnabledTextExports[pair.Key] = pair.Value;
        }

        return clone;
    }

    private static Dictionary<string, string> CloneStringDictionary(Dictionary<string, string>? source)
    {
        Dictionary<string, string> clone = new(StringComparer.Ordinal);
        if (source == null)
        {
            return clone;
        }

        foreach (KeyValuePair<string, string> pair in source)
        {
            clone[pair.Key] = pair.Value ?? string.Empty;
        }

        return clone;
    }

    private static SectionMemory CloneSectionMemory(SectionMemory memory)
    {
        return new SectionMemory
        {
            Tracked = memory.Tracked,
            RunsPast = memory.RunsPast,
            Attempts = memory.Attempts,
            KilledTheRun = memory.KilledTheRun,
            PreviousValidRunMissCount = memory.PreviousValidRunMissCount,
            BestMissCount = memory.BestMissCount
        };
    }

    private static SongMemory CloneSongMemory(SongMemory memory)
    {
        SongMemory clone = new SongMemory
        {
            Title = memory.Title,
            Artist = memory.Artist,
            Charter = memory.Charter,
            Attempts = memory.Attempts,
            Starts = memory.Starts,
            Restarts = memory.Restarts,
            LifetimeGhostedNotes = memory.LifetimeGhostedNotes,
            BestStreak = memory.BestStreak,
            BestRunMissedNotes = memory.BestRunMissedNotes,
            BestRunOverstrums = memory.BestRunOverstrums,
            FcAchieved = memory.FcAchieved
        };

        foreach (KeyValuePair<string, SectionMemory> pair in memory.Sections)
        {
            clone.Sections[pair.Key] = pair.Value == null ? new SectionMemory() : CloneSectionMemory(pair.Value);
        }

        foreach (CompletedRunRecord run in memory.CompletedRuns)
        {
            clone.CompletedRuns.Add(run.Clone());
        }

        return clone;
    }

    private static TrackerMemory CloneTrackerMemory(TrackerMemory memory)
    {
        TrackerMemory clone = new TrackerMemory
        {
            LifetimeGhostedNotes = memory.LifetimeGhostedNotes
        };

        foreach (KeyValuePair<string, SongMemory> pair in memory.Songs)
        {
            clone.Songs[pair.Key] = pair.Value == null ? new SongMemory() : CloneSongMemory(pair.Value);
        }

        return clone;
    }

    private static T LoadJson<T>(string path, T fallback)
    {
        try
        {
            if (!File.Exists(path))
            {
                return fallback;
            }

            T? data = JsonConvert.DeserializeObject<T>(File.ReadAllText(path));
            return data == null ? fallback : data;
        }
        catch
        {
            return fallback;
        }
    }

    private static void WriteJsonFile(string path, string json)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                File.WriteAllText(path, json);
                return;
            }
            catch (IOException)
            {
                if (attempt == 2)
                {
                    throw;
                }

                Thread.Sleep(25);
            }
        }
    }

    private void QueuePersistenceWrite(string path, object snapshot)
    {
        if (string.IsNullOrWhiteSpace(path) || snapshot == null)
        {
            return;
        }

        lock (_persistenceWriteSync)
        {
            if (!_persistenceWriteThreadStarted)
            {
                _persistenceWriteThreadStarted = true;
                _persistenceWriteThread = new Thread(PersistenceWriteLoop)
                {
                    IsBackground = true,
                    Name = "StatTrackPersistenceWriter"
                };
                _persistenceWriteThread.Start();
            }

            _pendingPersistenceWrites[path] = new PersistenceWriteItem
            {
                Path = path,
                Snapshot = snapshot
            };
        }

        _persistenceWriteSignal.Set();
    }

    private void PersistenceWriteLoop()
    {
        try
        {
            while (true)
            {
                _persistenceWriteSignal.WaitOne();

                PersistenceWriteItem[] pendingWrites;
                lock (_persistenceWriteSync)
                {
                    if (_pendingPersistenceWrites.Count == 0)
                    {
                        continue;
                    }

                    pendingWrites = _pendingPersistenceWrites.Values.ToArray();
                    _pendingPersistenceWrites.Clear();
                }

                foreach (PersistenceWriteItem pendingWrite in pendingWrites)
                {
                    try
                    {
                        string content = JsonConvert.SerializeObject(pendingWrite.Snapshot);
                        lock (_fileWriteSync)
                        {
                            if (_fileWriteCache.TryGetValue(pendingWrite.Path, out string? previousContent) &&
                                string.Equals(previousContent, content, StringComparison.Ordinal))
                            {
                                continue;
                            }

                            WriteJsonFile(pendingWrite.Path, content);
                            _fileWriteCache[pendingWrite.Path] = content;
                        }
                    }
                    catch (Exception ex)
                    {
                        StockTrackerLog.Write(ex);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }
    }

    private void WriteObsText(string path, string text)
    {
        WriteTextFileCached(path, text ?? string.Empty);
    }

    private static string FormatObsValue(string label, string value)
    {
        return string.IsNullOrWhiteSpace(label)
            ? (value ?? string.Empty)
            : label + ": " + (value ?? string.Empty);
    }

    private string RenderExportTemplate(string templateId, ExportTemplateRenderContext context)
    {
        return ExportTemplateEngine.Render(GetCompiledExportTemplateForMain(templateId), context);
    }

    private string RenderExportTemplate(string templateId, ExportTemplateRenderContext context, IReadOnlyDictionary<string, string> templateOverrides, int templateVersion)
    {
        return ExportTemplateEngine.Render(GetCompiledExportTemplateForWorker(templateId, templateOverrides, templateVersion), context);
    }

    private CompiledExportTemplate GetCompiledExportTemplateForMain(string templateId)
    {
        if (_compiledExportTemplatesVersion != _exportTemplateVersion)
        {
            _compiledExportTemplates.Clear();
            _compiledExportTemplatesVersion = _exportTemplateVersion;
        }

        if (_compiledExportTemplates.TryGetValue(templateId, out CompiledExportTemplate? compiledTemplate))
        {
            return compiledTemplate;
        }

        ExportTemplateDefinition definition = ExportTemplateCatalog.TryGet(templateId)
            ?? throw new InvalidOperationException("Unknown export template id: " + templateId);

        string source = GetEffectiveExportTemplateSource(definition);
        CompiledExportTemplate compiled = CompileExportTemplateWithFallback(definition, source);
        _compiledExportTemplates[templateId] = compiled;
        return compiled;
    }

    private CompiledExportTemplate GetCompiledExportTemplateForWorker(string templateId, IReadOnlyDictionary<string, string> templateOverrides, int templateVersion)
    {
        if (_workerCompiledExportTemplatesVersion != templateVersion)
        {
            _workerCompiledExportTemplates.Clear();
            _workerCompiledExportTemplatesVersion = templateVersion;
        }

        if (_workerCompiledExportTemplates.TryGetValue(templateId, out CompiledExportTemplate? compiledTemplate))
        {
            return compiledTemplate;
        }

        ExportTemplateDefinition definition = ExportTemplateCatalog.TryGet(templateId)
            ?? throw new InvalidOperationException("Unknown export template id: " + templateId);

        string source = templateOverrides.TryGetValue(definition.TemplateId, out string? templateOverride)
            ? (templateOverride ?? string.Empty)
            : definition.DefaultTemplate;
        CompiledExportTemplate compiled = CompileExportTemplateWithFallback(definition, source);
        _workerCompiledExportTemplates[templateId] = compiled;
        return compiled;
    }

    private CompiledExportTemplate CompileExportTemplateWithFallback(ExportTemplateDefinition definition, string source)
    {
        if (!ExportTemplateEngine.TryCompile(definition, source, out CompiledExportTemplate? compiled, out string? errorMessage, out int errorLineNumber))
        {
            LogInvalidExportTemplate(definition, source, errorMessage, errorLineNumber);
            if (!ExportTemplateEngine.TryCompile(definition, definition.DefaultTemplate, out compiled, out errorMessage, out errorLineNumber) || compiled == null)
            {
                throw new InvalidOperationException("Failed to compile default export template: " + definition.TemplateId);
            }
        }

        return compiled!;
    }

    private string GetEffectiveExportTemplateSource(ExportTemplateDefinition definition)
    {
        Dictionary<string, string> overrides = EnsureExportTemplateOverrides();
        return overrides.TryGetValue(definition.TemplateId, out string? templateOverride)
            ? (templateOverride ?? string.Empty)
            : definition.DefaultTemplate;
    }

    private void LogInvalidExportTemplate(ExportTemplateDefinition definition, string source, string? errorMessage, int errorLineNumber)
    {
        lock (_exportTemplateLogSync)
        {
            if (_loggedInvalidTemplateSources.TryGetValue(definition.TemplateId, out string? previousSource) &&
                string.Equals(previousSource, source, StringComparison.Ordinal))
            {
                return;
            }

            _loggedInvalidTemplateSources[definition.TemplateId] = source;
        }
        StockTrackerLog.Write($"InvalidExportTemplate | id={definition.TemplateId} | line={errorLineNumber.ToString(CultureInfo.InvariantCulture)} | error={errorMessage ?? "Unknown error"}");
    }

    private ExportTemplateRenderContext CreateMetricTemplateContext(TrackerState state, string label, string value)
    {
        ExportTemplateRenderContext context = CreateCommonExportTemplateContext(state);
        context.Set("label", label);
        context.Set("value", value);
        return context;
    }

    private ExportTemplateRenderContext CreateSectionTemplateContext(TrackerState state, SectionStatsState section, string sectionExportName, string label, string value)
    {
        ExportTemplateRenderContext context = CreateCommonExportTemplateContext(state);
        context.Set("label", label);
        context.Set("value", value);
        context.Set("section_name", sectionExportName);
        context.Set("attempts", section.Attempts);
        context.Set("fcs_past", section.RunsPast);
        context.Set("killed_the_run", section.KilledTheRun);
        context.SetBool("tracked", section.Tracked);
        context.SetBool("has_best_miss_count", section.BestMissCount.HasValue);
        context.Set("best_miss_count", section.BestMissCount?.ToString(CultureInfo.InvariantCulture) ?? string.Empty);
        return context;
    }

    private ExportTemplateRenderContext CreateRunTemplateContext(TrackerState state, CompletedRunRecord run, string label, string value)
    {
        ExportTemplateRenderContext context = CreateCommonExportTemplateContext(state);
        context.Set("label", label);
        context.Set("value", value);
        context.Set("run_index", run.Index);
        context.Set("completed_at_utc", run.CompletedAtUtc ?? string.Empty);
        context.Set("percent", run.Percent);
        context.Set("score", run.Score);
        context.Set("best_streak", run.BestStreak);
        context.Set("first_miss_streak", run.FirstMissStreak);
        context.Set("ghosted_notes", run.GhostedNotes);
        context.Set("overstrums", run.Overstrums);
        context.Set("missed_notes", run.MissedNotes);
        context.SetBool("fc_achieved", run.FcAchieved);
        context.Set("fc_yes_no", run.FcAchieved ? "Yes" : "No");
        context.Set("final_section", run.FinalSection ?? string.Empty);
        context.SetBool("has_final_section", !string.IsNullOrWhiteSpace(run.FinalSection));
        return context;
    }

    private ExportTemplateRenderContext CreateCommonExportTemplateContext(TrackerState state)
    {
        ExportTemplateRenderContext context = new();
        SongDescriptor? song = state.Song;
        context.Set("song_key", song?.SongKey ?? string.Empty);
        context.Set("title", song?.Title ?? string.Empty);
        context.Set("artist", song?.Artist ?? string.Empty);
        context.Set("charter", song?.Charter ?? string.Empty);
        context.Set("difficulty_name", song?.DifficultyName ?? string.Empty);
        context.Set("song_speed_label", song?.SongSpeedLabel ?? string.Empty);
        return context;
    }

    private void DeleteObsText(string path)
    {
        lock (_fileWriteSync)
        {
            _fileWriteCache.Remove(path);
            if (!File.Exists(path))
            {
                return;
            }

            File.Delete(path);
        }
    }

    private void DeleteObsDirectory(string path)
    {
        lock (_fileWriteSync)
        {
            List<string> keysToRemove = _fileWriteCache.Keys
                .Where(key => key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (string key in keysToRemove)
            {
                _fileWriteCache.Remove(key);
            }

            if (!Directory.Exists(path))
            {
                return;
            }

            Directory.Delete(path, true);
        }
    }

    private void DeleteIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_fileWriteSync)
        {
            _fileWriteCache.Remove(path);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        lock (_persistenceWriteSync)
        {
            _pendingPersistenceWrites.Remove(path);
        }
    }

    private void DeleteDirectoryIfExists(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (_fileWriteSync)
        {
            List<string> keysToRemove = _fileWriteCache.Keys
                .Where(key => key.StartsWith(path, StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (string key in keysToRemove)
            {
                _fileWriteCache.Remove(key);
            }

            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }
        }
    }

    private void WriteOrDeleteObsText(bool enabled, string path, string text)
    {
        if (enabled)
        {
            WriteObsText(path, text);
        }
        else
        {
            DeleteObsText(path);
        }
    }

    private void WriteTextFileCached(string path, string content)
    {
        lock (_fileWriteSync)
        {
            content ??= string.Empty;
            if (_fileWriteCache.TryGetValue(path, out string? previousContent) &&
                string.Equals(previousContent, content, StringComparison.Ordinal))
            {
                return;
            }

            WriteJsonFile(path, content);
            _fileWriteCache[path] = content;
        }
    }

    private static bool IsChartLikeField(FieldInfo field)
    {
        MethodInfo[] methods = GetAllMethods(field.FieldType).ToArray();
        bool hasSectionsGetter = methods.Any(method => method.GetParameters().Length == 0 && typeof(IEnumerable).IsAssignableFrom(method.ReturnType));
        bool hasSectionTimeGetter = methods.Any(method =>
            method.GetParameters().Length == 1 &&
            method.GetParameters()[0].ParameterType == typeof(int) &&
            (method.ReturnType == typeof(double) || method.ReturnType == typeof(float)));
        return hasSectionsGetter && hasSectionTimeGetter;
    }

    private static bool LooksLikeSectionList(Type type)
    {
        bool hasName = GetAllFields(type).Any(field => field.FieldType == typeof(string))
            || GetAllProperties(type).Any(property => property.PropertyType == typeof(string));
        bool hasTime = GetAllFields(type).Any(field => field.FieldType == typeof(float) || field.FieldType == typeof(double))
            || GetAllProperties(type).Any(property => property.PropertyType == typeof(float) || property.PropertyType == typeof(double));
        return hasName && hasTime;
    }

    private static bool LooksLikeLoadedChartType(Type type)
    {
        return GetAllFields(type).Any(field => field.Name.Contains("Ê²ÊµÊ¹Ê¼") || (typeof(IEnumerable).IsAssignableFrom(field.FieldType) && !field.FieldType.IsArray))
            || GetAllProperties(type).Any(property => typeof(IEnumerable).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(string));
    }

    private static bool ContainsSectionObjects(IEnumerable enumerable)
    {
        foreach (object? item in enumerable)
        {
            if (item != null)
            {
                return LooksLikeSectionList(item.GetType());
            }
        }

        return false;
    }

    private static bool IsNumericType(Type type)
    {
        Type codeType = Nullable.GetUnderlyingType(type) ?? type;
        return codeType == typeof(byte)
            || codeType == typeof(sbyte)
            || codeType == typeof(short)
            || codeType == typeof(ushort)
            || codeType == typeof(int)
            || codeType == typeof(uint)
            || codeType == typeof(long)
            || codeType == typeof(ulong)
            || codeType == typeof(float)
            || codeType == typeof(double)
            || codeType == typeof(decimal);
    }

    private static bool HasRemotePlayerSettings(Type type)
    {
        return GetAllFields(type).Any(field => HasIsRemoteMember(field.FieldType))
            || HasIsRemoteMember(type);
    }

    private static bool HasIsRemoteMember(Type type)
    {
        return type.GetProperty("isRemotePlayer", AnyInstance) != null
            || type.GetField("isRemotePlayer", AnyInstance) != null;
    }

    private static TrackedSectionState BuildTrackedSectionState(IReadOnlyList<SectionDescriptor> sections, SectionDescriptor section, SongConfig config, SongMemory memory)
    {
        string sectionKey = BuildSectionOverlayKey(sections, section);
        bool tracked = config.TrackedSections.TryGetValue(sectionKey, out bool value) && value;
        memory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory);

        return new TrackedSectionState
        {
            Index = section.Index,
            Name = sectionKey,
            StartTime = section.StartTime,
            Tracked = tracked,
            RunsPast = sectionMemory?.RunsPast ?? 0,
            KilledTheRun = sectionMemory?.KilledTheRun ?? 0
        };
    }

    private static SectionStatsState BuildSectionStatsState(IReadOnlyList<SectionDescriptor> sections, SectionDescriptor section, SongConfig config, SongMemory memory)
    {
        string sectionKey = BuildSectionOverlayKey(sections, section);
        bool tracked = config.TrackedSections.TryGetValue(sectionKey, out bool value) && value;
        memory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory);

        return new SectionStatsState
        {
            Index = section.Index,
            Name = sectionKey,
            StartTime = section.StartTime,
            Tracked = tracked,
            RunsPast = sectionMemory?.RunsPast ?? 0,
            Attempts = sectionMemory?.Attempts ?? 0,
            KilledTheRun = sectionMemory?.KilledTheRun ?? 0,
            BestMissCount = sectionMemory?.BestMissCount
        };
    }

    private List<NoteSplitSectionState> GetOrBuildNoteSplitSections(string songKey, IReadOnlyList<SectionDescriptor> sections, SongMemory songMemory, string currentSectionName)
    {
        if (_noteSplitSnapshotCache == null ||
            !string.Equals(_noteSplitSnapshotCache.SongKey, songKey, StringComparison.Ordinal) ||
            _noteSplitSnapshotCache.MemoryVersion != _sectionMemoryVersion ||
            _noteSplitSnapshotCache.SectionCount != sections.Count)
        {
            var cachedRows = new List<CachedNoteSplitSectionRow>(sections.Count);
            for (int i = 0; i < sections.Count; i++)
            {
                SectionDescriptor section = sections[i];
                string sectionKey = BuildSectionOverlayKey(sections, section);
                songMemory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory);
                cachedRows.Add(new CachedNoteSplitSectionRow
                {
                    Order = section.Index,
                    Key = sectionKey,
                    Name = sectionKey,
                    PreviousValidRunMissCount = sectionMemory?.PreviousValidRunMissCount,
                    PersonalBestMissCount = sectionMemory?.BestMissCount
                });
            }

            _noteSplitSnapshotCache = new NoteSplitSnapshotCache
            {
                SongKey = songKey,
                MemoryVersion = _sectionMemoryVersion,
                SectionCount = sections.Count,
                Rows = cachedRows
            };
        }

        var rows = new List<NoteSplitSectionState>(_noteSplitSnapshotCache.Rows.Count);
        for (int i = 0; i < _noteSplitSnapshotCache.Rows.Count; i++)
        {
            CachedNoteSplitSectionRow row = _noteSplitSnapshotCache.Rows[i];
            _runState.NoteSplitSectionsThisRun.TryGetValue(row.Key, out NoteSplitSectionRunState? runState);
            rows.Add(new NoteSplitSectionState
            {
                Order = row.Order,
                Key = row.Key,
                Name = row.Name,
                IsCurrent = string.Equals(row.Key, currentSectionName, StringComparison.Ordinal),
                PreviousValidRunMissCount = row.PreviousValidRunMissCount,
                PersonalBestMissCount = row.PersonalBestMissCount,
                CurrentRunMissCount = runState?.MissCount,
                ResultKind = runState?.ResultKind ?? NoteSplitResultKind.None
            });
        }

        return rows;
    }

    private static string BuildSectionSummary(SectionStatsState section)
    {
        return
            $"Section: {section.Name}{Environment.NewLine}" +
            $"Attempts: {section.Attempts}{Environment.NewLine}" +
            $"FCs Past: {section.RunsPast}{Environment.NewLine}" +
            $"Killed the Run: {section.KilledTheRun}";
    }

    private static string BuildCompletedRunSummary(CompletedRunRecord run)
    {
        return
            $"run {run.Index} | percent: {run.Percent} | misses: {run.MissedNotes} | first miss streak: {run.FirstMissStreak} | score: {run.Score} | best streak: {run.BestStreak} | ghosts: {run.GhostedNotes} | overstrums: {run.Overstrums} | FC: {(run.FcAchieved ? "Yes" : "No")}";
    }

    private static string BuildCompletedRunDirectoryName(CompletedRunRecord run)
    {
        return $"run{run.Index.ToString("000", CultureInfo.InvariantCulture)}_{run.Percent.ToString(CultureInfo.InvariantCulture)}_{run.MissedNotes.ToString(CultureInfo.InvariantCulture)}_{run.FirstMissStreak.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string BuildSectionExportName(IReadOnlyList<SectionStatsState> sections, SectionStatsState target)
    {
        int occurrence = 0;
        for (int i = 0; i < sections.Count; i++)
        {
            SectionStatsState section = sections[i];
            if (!string.Equals(section.Name, target.Name, StringComparison.Ordinal))
            {
                continue;
            }

            occurrence++;
            if (section.Index == target.Index)
            {
                return occurrence <= 1 ? target.Name : $"{target.Name} ({occurrence})";
            }
        }

        return target.Name;
    }

    private static int ConvertToInt32(object? value) => value == null ? 0 : Convert.ToInt32(value, CultureInfo.InvariantCulture);
    private static double ConvertToDouble(object? value) => value == null ? 0d : Convert.ToDouble(value, CultureInfo.InvariantCulture);
    private static bool ConvertToBoolean(object? value) => value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture);
    private static bool TryGetBooleanField(object obj, string fieldName)
    {
        object? value = obj.GetType().GetField(fieldName, AnyInstance)?.GetValue(obj);
        if (value == null)
        {
            return false;
        }

        try
        {
            return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
        }
        catch
        {
            return false;
        }
    }

    private static bool HasSongIdentity(object songEntry)
    {
        return !string.IsNullOrWhiteSpace(GetStringProperty(songEntry, "Name_StrippedTags")) ||
            !string.IsNullOrWhiteSpace(GetStringProperty(songEntry, "Name")) ||
            !string.IsNullOrWhiteSpace(GetStringProperty(songEntry, "checksumString")) ||
            !string.IsNullOrWhiteSpace(GetStringField(songEntry, "folderPath"));
    }

    private static string? GetStringProperty(object obj, string propertyName)
    {
        try
        {
            return obj.GetType().GetProperty(propertyName, AnyInstance)?.GetValue(obj, null)?.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string? GetStringField(object obj, string fieldName)
    {
        try
        {
            return obj.GetType().GetField(fieldName, AnyInstance)?.GetValue(obj)?.ToString();
        }
        catch
        {
            return null;
        }
    }
    private static object? SafeGetPropertyValue(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance, null);
        }
        catch
        {
            return null;
        }
    }

    private static IEnumerable<FieldInfo> GetAllFields(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            foreach (FieldInfo field in current.GetFields(AnyInstance | BindingFlags.DeclaredOnly))
            {
                yield return field;
            }
        }
    }

    private static IEnumerable<PropertyInfo> GetAllProperties(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            foreach (PropertyInfo property in current.GetProperties(AnyInstance | BindingFlags.DeclaredOnly))
            {
                yield return property;
            }
        }
    }

    private static IEnumerable<MethodInfo> GetAllMethods(Type type)
    {
        for (Type? current = type; current != null; current = current.BaseType)
        {
            foreach (MethodInfo method in current.GetMethods(AnyInstance | BindingFlags.DeclaredOnly))
            {
                yield return method;
            }
        }
    }

    private static Type? GetEnumerableItemType(Type enumerableType)
    {
        if (enumerableType.IsArray)
        {
            return enumerableType.GetElementType();
        }

        Type? genericEnumerable = enumerableType.GetInterfaces()
            .Concat(new[] { enumerableType })
            .FirstOrDefault(type => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>));
        return genericEnumerable?.GetGenericArguments().FirstOrDefault();
    }
}

public sealed class EnabledTextExportSnapshot
{
    public EnabledTextExportSnapshot()
    {
    }

    public EnabledTextExportSnapshot(IReadOnlyDictionary<string, bool> values)
    {
        NoteSplitMode = GetValue(values, "note_split_mode");
        CurrentSection = GetValue(values, "current_section");
        Streak = GetValue(values, "streak");
        BestStreak = GetValue(values, "best_streak");
        Attempts = GetValue(values, "attempts");
        CurrentGhostedNotes = GetValue(values, "current_ghosted_notes");
        CurrentOverstrums = GetValue(values, "current_overstrums");
        CurrentMissedNotes = GetValue(values, "current_missed_notes");
        LifetimeGhostedNotes = GetValue(values, "lifetime_ghosted_notes");
        GlobalLifetimeGhostedNotes = GetValue(values, "global_lifetime_ghosted_notes");
        FcAchieved = GetValue(values, "fc_achieved");
        CompletedRuns = GetValue(values, "completed_runs");
    }

    public bool NoteSplitMode { get; }
    public bool CurrentSection { get; }
    public bool Streak { get; }
    public bool BestStreak { get; }
    public bool Attempts { get; }
    public bool CurrentGhostedNotes { get; }
    public bool CurrentOverstrums { get; }
    public bool CurrentMissedNotes { get; }
    public bool LifetimeGhostedNotes { get; }
    public bool GlobalLifetimeGhostedNotes { get; }
    public bool FcAchieved { get; }
    public bool CompletedRuns { get; }

    [JsonIgnore]
    public bool HasAnyObsTextExport =>
        CurrentSection ||
        Streak ||
        BestStreak ||
        Attempts ||
        CurrentGhostedNotes ||
        CurrentOverstrums ||
        CurrentMissedNotes ||
        LifetimeGhostedNotes ||
        GlobalLifetimeGhostedNotes ||
        FcAchieved ||
        CompletedRuns;

    public bool IsEnabled(string exportKey)
    {
        return exportKey switch
        {
            "note_split_mode" => NoteSplitMode,
            "current_section" => CurrentSection,
            "streak" => Streak,
            "best_streak" => BestStreak,
            "attempts" => Attempts,
            "current_ghosted_notes" => CurrentGhostedNotes,
            "current_overstrums" => CurrentOverstrums,
            "current_missed_notes" => CurrentMissedNotes,
            "lifetime_ghosted_notes" => LifetimeGhostedNotes,
            "global_lifetime_ghosted_notes" => GlobalLifetimeGhostedNotes,
            "fc_achieved" => FcAchieved,
            "completed_runs" => CompletedRuns,
            _ => false
        };
    }

    private static bool GetValue(IReadOnlyDictionary<string, bool> values, string key)
    {
        return values.TryGetValue(key, out bool enabled) && enabled;
    }
}

public sealed class TrackerState
{
    public bool IsInSong { get; set; }
    public bool OverlayEditorVisible { get; set; }
    [JsonIgnore]
    public bool IsPracticeMode { get; set; }
    public int Score { get; set; }
    public int Streak { get; set; }
    public int BestStreak { get; set; }
    public int Starts { get; set; }
    public int Restarts { get; set; }
    public int Attempts { get; set; }
    public int CurrentGhostedNotes { get; set; }
    public int CurrentOverstrums { get; set; }
    public int CurrentMissedNotes { get; set; }
    public int LifetimeGhostedNotes { get; set; }
    public int GlobalLifetimeGhostedNotes { get; set; }
    public bool NoteSplitModeEnabled { get; set; }
    public string PreviousSection { get; set; } = string.Empty;
    public int? PreviousSectionMissCount { get; set; }
    public int? SongPersonalBestMissCount { get; set; }
    public int? SongPersonalBestOverstrums { get; set; }
    public string PreviousSectionResultKind { get; set; } = NoteSplitResultKind.None;
    public List<NoteSplitSectionState> NoteSplitSections { get; set; } = new();
    [JsonIgnore]
    public string CurrentSection { get; set; } = string.Empty;
    [JsonIgnore]
    public double SongTime { get; set; }
    [JsonIgnore]
    public double SongDuration { get; set; }
    public bool FcAchieved { get; set; }
    public SongDescriptor? Song { get; set; }
    [JsonIgnore]
    public List<SectionDescriptor> Sections { get; set; } = new();
    [JsonIgnore]
    public List<TrackedSectionState> TrackedSections { get; set; } = new();
    [JsonIgnore]
    public List<SectionStatsState> SectionStats { get; set; } = new();
    public Dictionary<string, SectionStatsState> SectionStatsByName { get; set; } = new(StringComparer.Ordinal);
    [JsonIgnore]
    public SectionStatsState? CurrentSectionStats { get; set; }
    [JsonIgnore]
    public List<CompletedRunRecord> CompletedRuns { get; set; } = new();
    [JsonIgnore]
    public EnabledTextExportSnapshot EnabledTextExports { get; set; } = new();
}

public sealed class SongDescriptor
{
    public string SongKey { get; set; } = string.Empty;
    public string? OverlayLayoutKey { get; set; }
    [JsonIgnore]
    public int SongSpeedPercent { get; set; } = 100;
    [JsonIgnore]
    public string SongSpeedLabel { get; set; } = "100%";
    [JsonIgnore]
    public string DifficultyCode { get; set; } = "X";
    [JsonIgnore]
    public string DifficultyName { get; set; } = "Expert";
    public string? Title { get; set; }
    public string? Artist { get; set; }
    [JsonIgnore]
    public string? Charter { get; set; }
}

public sealed class SectionDescriptor
{
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public string DisplayName { get; set; } = string.Empty;
    public int Tick { get; set; } = -1;
    public double StartTime { get; set; }
}

internal sealed class TempoEvent
{
    public int Tick { get; set; }
    public int BpmTimes1000 { get; set; }
}

internal sealed class SongSpeedInfo
{
    public int Percent { get; set; } = 100;
    public string Label { get; set; } = "100%";
}

internal sealed class SectionSnapshotCache
{
    public string SongKey { get; set; } = string.Empty;
    public int MemoryVersion { get; set; }
    public int ConfigVersion { get; set; }
    public int SectionCount { get; set; }
    public List<TrackedSectionState> TrackedSections { get; set; } = new();
    public List<SectionStatsState> SectionStats { get; set; } = new();
    public Dictionary<string, SectionStatsState> SectionStatsByName { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class OverlayRenderSnapshot
{
    public TrackerState State { get; set; } = new();
    public SongConfig? SongConfig { get; set; }
    public bool ShouldRender { get; set; }
    public bool OverlayEditorVisible { get; set; }
    public bool RenderWidgetsInGame { get; set; }
    public List<OverlayWidgetRenderEntry> WidgetEntries { get; set; } = new();
}

internal sealed class OverlayWidgetRenderEntry
{
    public string WidgetKey { get; set; } = string.Empty;
    public OverlayWidgetConfig Config { get; set; } = new();
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
    public int DefaultIndex { get; set; }
}

internal sealed class NoteSplitSnapshotCache
{
    public string SongKey { get; set; } = string.Empty;
    public int MemoryVersion { get; set; }
    public int SectionCount { get; set; }
    public List<CachedNoteSplitSectionRow> Rows { get; set; } = new();
}

internal sealed class CachedNoteSplitSectionRow
{
    public int Order { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int? PreviousValidRunMissCount { get; set; }
    public int? PersonalBestMissCount { get; set; }
}

internal sealed class DifficultyInfo
{
    public static readonly DifficultyInfo Default = new()
    {
        Code = "X",
        Name = "Expert"
    };

    public string Code { get; set; } = "X";
    public string Name { get; set; } = "Expert";

    public static DifficultyInfo FromValue(int value)
    {
        return value switch
        {
            0 => new DifficultyInfo { Code = "E", Name = "Easy" },
            1 => new DifficultyInfo { Code = "M", Name = "Medium" },
            2 => new DifficultyInfo { Code = "H", Name = "Hard" },
            _ => new DifficultyInfo { Code = "X", Name = "Expert" }
        };
    }

    public string PrefixedTitle(string title)
    {
        return $"{Code} {title}";
    }
}

public sealed class TrackedSectionState
{
    [JsonIgnore]
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public double StartTime { get; set; }
    [JsonIgnore]
    public bool Tracked { get; set; }
    public int RunsPast { get; set; }
    [JsonIgnore]
    public int KilledTheRun { get; set; }
}

public sealed class SectionStatsState
{
    [JsonIgnore]
    public int Index { get; set; }
    public string Name { get; set; } = string.Empty;
    [JsonIgnore]
    public double StartTime { get; set; }
    [JsonIgnore]
    public bool Tracked { get; set; }
    public int RunsPast { get; set; }
    public int Attempts { get; set; }
    public int KilledTheRun { get; set; }
    public int? BestMissCount { get; set; }
}

public sealed class TrackerConfig
{
    public Dictionary<string, SongConfig> Songs { get; set; } = new();
    public Dictionary<string, bool> DefaultEnabledTextExports { get; set; } = new();
    public OverlayEditorConfig OverlayEditor { get; set; } = new();
    public ExportTemplateEditorConfig ExportTemplateEditor { get; set; } = new();
    public DesktopOverlayStyleConfig DesktopOverlayStyle { get; set; } = new();
    public Dictionary<string, string> ExportTemplateOverrides { get; set; } = new();
    public float AnimatedMenuTintR { get; set; } = 0.6470588f;
    public float AnimatedMenuTintG { get; set; } = 0.2784314f;
    public float AnimatedMenuTintB { get; set; } = 0.9882353f;
    public float AnimatedMenuTintA { get; set; }
    public float AnimatedMenuWispR { get; set; } = 1f;
    public float AnimatedMenuWispG { get; set; } = 1f;
    public float AnimatedMenuWispB { get; set; } = 1f;
    public float AnimatedMenuWispA { get; set; } = 0.58f;
    public float AnimatedMenuWispSize { get; set; } = 0.68f;
    public float AnimatedMenuTintBackgroundOverlayStrength { get; set; } = 0.35f;
    public float AnimatedMenuTintCanvasOverlayStrength { get; set; }
    public float AnimatedMenuTintRawImageStrength { get; set; }
    public float AnimatedMenuTintMaterialStrength { get; set; }
    public float AnimatedMenuTintOnGuiOverlayStrength { get; set; }
}

public sealed class SongConfig
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Charter { get; set; }
    public Dictionary<string, bool> TrackedSections { get; set; } = new();
    public Dictionary<string, OverlayWidgetConfig> OverlayWidgets { get; set; } = new();
}

public sealed class OverlayEditorConfig
{
    public float X { get; set; } = 465f;
    public float Y { get; set; } = 211.6f;
    public float Width { get; set; } = 765f;
    public float Height { get; set; } = 567f;
    public float BackgroundA { get; set; } = 0.82f;
    public bool ResizeHandleHidden { get; set; }
}

public sealed class ExportTemplateEditorConfig
{
    public float X { get; set; } = 540f;
    public float Y { get; set; } = 150f;
    public float Width { get; set; } = 1080f;
    public float Height { get; set; } = 700f;
    public bool ResizeHandleHidden { get; set; }
}

public sealed class OverlayWidgetConfig
{
    public bool Enabled { get; set; }
    public float X { get; set; } = 20f;
    public float Y { get; set; } = -1f;
    public float Width { get; set; } = 300f;
    public float Height { get; set; } = 90f;
    public float FontScale { get; set; } = 1f;
    public int ZIndex { get; set; }
    public int ResizeModeVersion { get; set; }
    public bool ResizeHandleHidden { get; set; }
    public float BackgroundR { get; set; }
    public float BackgroundG { get; set; }
    public float BackgroundB { get; set; }
    public float BackgroundA { get; set; } = 0.82f;
}

public sealed class DesktopOverlayStyleConfig
{
    public float BorderR { get; set; } = 0.235f;
    public float BorderG { get; set; } = 0.235f;
    public float BorderB { get; set; } = 0.235f;
    public float BorderA { get; set; } = 0.70f;
    public float NoteSplitX { get; set; } = -1f;
    public float NoteSplitY { get; set; } = -1f;
    public float NoteSplitWidth { get; set; } = 420f;
    public float NoteSplitHeight { get; set; } = 720f;
    public string NoteSplitFontFamily { get; set; } = "Segoe UI";
    public float NoteSplitFontScale { get; set; } = 1f;
    public bool NoteSplitTopMost { get; set; } = true;
}

public sealed class TrackerMemory
{
    public int LifetimeGhostedNotes { get; set; }
    public Dictionary<string, SongMemory> Songs { get; set; } = new();
}

public sealed class SongMemory
{
    public string? Title { get; set; }
    public string? Artist { get; set; }
    public string? Charter { get; set; }
    public int Attempts { get; set; }
    public int Starts { get; set; }
    public int Restarts { get; set; }
    public int LifetimeGhostedNotes { get; set; }
    public int BestStreak { get; set; }
    public int? BestRunMissedNotes { get; set; }
    public int? BestRunOverstrums { get; set; }
    public bool FcAchieved { get; set; }
    public Dictionary<string, SectionMemory> Sections { get; set; } = new();
    public List<CompletedRunRecord> CompletedRuns { get; set; } = new();
}

public sealed class CompletedRunRecord
{
    public int Index { get; set; }
    public string? CompletedAtUtc { get; set; }
    public int Percent { get; set; }
    public int Score { get; set; }
    public int BestStreak { get; set; }
    public int FirstMissStreak { get; set; }
    public int GhostedNotes { get; set; }
    public int Overstrums { get; set; }
    public int MissedNotes { get; set; }
    public bool FcAchieved { get; set; }
    public string? FinalSection { get; set; }

    public CompletedRunRecord Clone()
    {
        return new CompletedRunRecord
        {
            Index = Index,
            CompletedAtUtc = CompletedAtUtc,
            Percent = Percent,
            Score = Score,
            BestStreak = BestStreak,
            FirstMissStreak = FirstMissStreak,
            GhostedNotes = GhostedNotes,
            Overstrums = Overstrums,
            MissedNotes = MissedNotes,
            FcAchieved = FcAchieved,
            FinalSection = FinalSection
        };
    }
}

public sealed class SectionMemory
{
    public bool Tracked { get; set; }
    public int RunsPast { get; set; }
    public int Attempts { get; set; }
    public int KilledTheRun { get; set; }
    public int? PreviousValidRunMissCount { get; set; }
    public int? BestMissCount { get; set; }
}

internal sealed class RunState
{
    public bool InRun { get; set; }
    public string SongKey { get; set; } = string.Empty;
    public object? CachedSongEntry { get; set; }
    public object? CachedPlayer { get; set; }
    public object? CachedChart { get; set; }
    public SongDescriptor? CachedSongDescriptor { get; set; }
    public SongSpeedInfo? CachedSongSpeed { get; set; }
    public DifficultyInfo? CachedDifficulty { get; set; }
    public double CachedSongDuration { get; set; }
    public bool CachedBotEnabled { get; set; }
    public float LastStableRefreshAt { get; set; }
    public SongMemory? CachedSongMemory { get; set; }
    public string CachedSongMemoryKey { get; set; } = string.Empty;
    public SongConfig? CachedSongConfig { get; set; }
    public string CachedSongConfigKey { get; set; } = string.Empty;
    public int CachedNotesHit { get; set; }
    public float LastNotesHitRefreshAt { get; set; }
    public bool HasCachedNotesHit { get; set; }
    public PlayerStatsSnapshot? CachedResultStats { get; set; }
    public float LastResultStatsRefreshAt { get; set; }
    public bool HasCachedResultStats { get; set; }
    public string LastSection { get; set; } = string.Empty;
    public double LastSongTime { get; set; }
    public int LastStreak { get; set; }
    public int LastGhostNotes { get; set; }
    public int LastOverstrums { get; set; }
    public int LastMissedNotes { get; set; }
    public int MissedNotesBaseline { get; set; }
    public int BestStreakThisRun { get; set; }
    public int FirstMissStreak { get; set; }
    public bool HadMiss { get; set; }
    public bool CompletedRunRecorded { get; set; }
    public string NoteSplitCurrentSection { get; set; } = string.Empty;
    public int PendingNoteSplitMisses { get; set; }
    public string NoteSplitPreviousSection { get; set; } = string.Empty;
    public int? NoteSplitPreviousSectionMissCount { get; set; }
    public int? NoteSplitPreviousSectionPersonalBestMissCount { get; set; }
    public int? NoteSplitPreviousSectionPersonalBestOverstrums { get; set; }
    public string NoteSplitPreviousSectionResultKind { get; set; } = NoteSplitResultKind.None;
    public Dictionary<string, int> NoteSplitMissCountsBySectionThisRun { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, NoteSplitSectionRunState> NoteSplitSectionsThisRun { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, PendingNoteSplitBestOverstrumBackfill> PendingNoteSplitBestOverstrumBackfills { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CountedSectionsThisRun { get; } = new(StringComparer.Ordinal);
    public HashSet<string> CountedSectionAttemptsThisRun { get; } = new(StringComparer.Ordinal);
}

public sealed class NoteSplitSectionState
{
    public int Order { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public bool IsCurrent { get; set; }
    public int? PreviousValidRunMissCount { get; set; }
    public int? PersonalBestMissCount { get; set; }
    public int? CurrentRunMissCount { get; set; }
    public string ResultKind { get; set; } = NoteSplitResultKind.None;
}

internal sealed class NoteSplitSectionRunState
{
    public int MissCount { get; set; }
    public string ResultKind { get; set; } = NoteSplitResultKind.None;
}

internal sealed class PendingNoteSplitBestOverstrumBackfill
{
    public int MissCount { get; set; }
    public string Mode { get; set; } = NoteSplitBestOverstrumBackfillMode.FillMissingOverstrums;
}

internal static class NoteSplitBestOverstrumBackfillMode
{
    public const string NewBestMiss = "new_best_miss";
    public const string FillMissingOverstrums = "fill_missing_overstrums";
    public const string BetterOverstrumsSameMiss = "better_overstrums_same_miss";
}

internal static class NoteSplitResultKind
{
    public const string None = "none";
    public const string FirstScan = "first_scan";
    public const string Improved = "improved";
    public const string PerfectImprovement = "perfect_improvement";
    public const string Tie = "tie";
    public const string Worse = "worse";
}

internal sealed class PlayerStatsSnapshot
{
    public int Score { get; set; }
    public int NotesHit { get; set; }
    public int TotalNotes { get; set; }
    public int Overstrums { get; set; }
    public int GhostNotes { get; set; }
    public int Accuracy { get; set; }
    public string? AccuracyString { get; set; }
}

internal sealed class OverlayMetricDefinition
{
    public static readonly OverlayMetricDefinition[] All =
    {
        new("streak", "Current Streak"),
        new("best_streak", "Best FC Streak"),
        new("current_missed_notes", "Current Missed Notes"),
        new("current_overstrums", "Current Overstrums"),
        new("current_ghosted_notes", "Current Ghosted Notes"),
        new("lifetime_ghosted_notes", "Song Lifetime Ghosts"),
        new("global_lifetime_ghosted_notes", "Global Lifetime Ghosts"),
        new("attempts", "Total Attempts"),
        new("fc_achieved", "FC Achieved")
    };

    public OverlayMetricDefinition(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
}

internal sealed class TextExportDefinition
{
    public static readonly TextExportDefinition[] All =
    {
        new("note_split_mode", "NoteSplit Mode"),
        new("current_section", "Current Section"),
        new("streak", "Current Streak"),
        new("best_streak", "Best FC Streak"),
        new("attempts", "Total Attempts"),
        new("current_ghosted_notes", "Current Ghosted Notes"),
        new("current_overstrums", "Current Overstrums"),
        new("current_missed_notes", "Current Missed Notes"),
        new("lifetime_ghosted_notes", "Song Lifetime Ghosts"),
        new("global_lifetime_ghosted_notes", "Global Lifetime Ghosts"),
        new("fc_achieved", "FC Achieved"),
        new("completed_runs", "Completed Runs")
    };

    public TextExportDefinition(string key, string label)
    {
        Key = key;
        Label = label;
    }

    public string Key { get; }
    public string Label { get; }
}

internal sealed class TrackingRequirements
{
    public bool NeedScore { get; set; }
    public bool NeedStreak { get; set; }
    public bool NeedGhostNotes { get; set; }
    public bool NeedOverstrums { get; set; }
    public bool NeedMissedNotes { get; set; }
    public bool NeedSongTiming { get; set; }
    public bool NeedCurrentSection { get; set; }
    public bool NeedSections { get; set; }
    public bool NeedRunTracking { get; set; }
    public bool NeedCompletedRunTracking { get; set; }
    public bool NeedSongMemory { get; set; }
    public bool NeedNotesHit { get; set; }
    public bool NeedResultStats { get; set; }
    public bool NeedCompletedRuns { get; set; }
}

internal sealed class ExportWorkItem
{
    public TrackerState State { get; set; } = new();
    public bool ExportStateJson { get; set; }
    public bool ExportObs { get; set; }
    public Dictionary<string, string> ExportTemplateOverrides { get; set; } = new(StringComparer.Ordinal);
    public int ExportTemplateVersion { get; set; }
}

internal sealed class PersistenceWriteItem
{
    public string Path { get; set; } = string.Empty;
    public object Snapshot { get; set; } = string.Empty;
}

internal sealed class PlayerMissCounter
{
    public PlayerMissCounter(object player)
    {
        Player = new WeakReference<object>(player);
    }

    public WeakReference<object> Player { get; }
    public int MissedNotes { get; set; }
}

internal sealed class MidiSectionParseResult
{
    public static MidiSectionParseResult Empty { get; } = new(0, 0, new List<SectionDescriptor>());

    public MidiSectionParseResult(int format, int trackCount, List<SectionDescriptor> sections)
    {
        Format = format;
        TrackCount = trackCount;
        Sections = sections;
    }

    public int Format { get; }
    public int TrackCount { get; }
    public List<SectionDescriptor> Sections { get; }
}

internal sealed class GitHubLatestReleaseResponse
{
    [JsonProperty("tag_name")]
    public string? TagName { get; set; }

    [JsonProperty("name")]
    public string? Name { get; set; }
}
}


