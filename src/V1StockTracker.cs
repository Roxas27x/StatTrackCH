#nullable enable

using Newtonsoft.Json;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using UnityEngine;

namespace CloneHeroSectionTracker.V1Stock
{
internal static class StatTrackDataPaths
{
    internal const string CurrentDirectoryName = "StatTrack";
    internal const string LegacyDirectoryName = "CloneHeroSectionTracker";

    internal static string GetCurrentDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrentDirectoryName);
    }

    internal static string EnsureDataDirectoryMigrated(Action<string>? log = null)
    {
        string currentDir = GetCurrentDataDirectory();
        string legacyDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), LegacyDirectoryName);
        if (!Directory.Exists(legacyDir))
        {
            return currentDir;
        }

        try
        {
            MergeDirectoryContents(legacyDir, currentDir);
            log?.Invoke("Merged legacy data directory into " + currentDir);
        }
        catch (Exception ex)
        {
            log?.Invoke("Legacy data directory merge failed | " + ex.Message);
        }

        return currentDir;
    }

    private static void MergeDirectoryContents(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            string destinationPath = Path.Combine(destinationDir, Path.GetFileName(filePath));
            string? destinationParent = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationParent))
            {
                Directory.CreateDirectory(destinationParent);
            }

            if (!File.Exists(destinationPath))
            {
                File.Copy(filePath, destinationPath, overwrite: false);
            }
        }

        foreach (string directoryPath in Directory.GetDirectories(sourceDir))
        {
            string directoryName = Path.GetFileName(directoryPath);
            if (string.IsNullOrEmpty(directoryName))
            {
                continue;
            }

            MergeDirectoryContents(directoryPath, Path.Combine(destinationDir, directoryName));
        }
    }
}

internal static class StockTrackerLog
{
    private static readonly object Sync = new();
    private static readonly string LogDir = StatTrackDataPaths.EnsureDataDirectoryMigrated();
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

    public static void OnOverlayGui()
    {
        try
        {
            lock (Sync)
            {
                Tracker.RenderOverlayGui();
            }
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
    private static readonly bool VerboseLoggingEnabled = false;
    private const float StateExportIntervalSeconds = 0.25f;
    private const float ObsExportIntervalSeconds = 0.25f;
    private const float StableRunRefreshIntervalSeconds = 5f;
    private const float NotesHitRefreshIntervalSeconds = 2f;
    private const float ResultStatsRefreshIntervalSeconds = 1f;
    private const string NoteSplitModeExportKey = "note_split_mode";

    private const string GlobalVariablesTypeName = "GlobalVariables";
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
    private readonly HashSet<string> _obsLegacySongCleanupKeys = new(StringComparer.Ordinal);
    private readonly object _fileWriteSync = new();
    private readonly object _exportWorkerSync = new();
    private readonly AutoResetEvent _exportSignal = new(false);
    private const string DesktopOverlayExeName = "StatTrackOverlay.exe";
    private const float DesktopOverlayCheckIntervalSeconds = 5f;
    private bool _memoryDirty;
    private bool _configDirty;
    private int _memoryVersion;
    private int _configVersion;
    private bool _initialized;
    private SectionSnapshotCache? _sectionSnapshotCache;
    private string _completedRunsSnapshotSongKey = string.Empty;
    private int _completedRunsSnapshotMemoryVersion = -1;
    private List<CompletedRunRecord> _completedRunsSnapshot = new();
    private Thread? _exportThread;
    private ExportWorkItem? _pendingExport;

    private Type? _gameManagerType;
    private Type? _globalVariablesType;
    private Type? _basePlayerType;
    private Type? _gameSettingType;
    private FieldInfo? _playersField;
    private FieldInfo? _mainPlayerField;
    private FieldInfo? _practiceUiField;
    private FieldInfo? _songDurationField;
    private FieldInfo? _songTimeField;
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
    private MethodInfo? _chartSectionsMethod;
    private MethodInfo? _chartSectionTimeMethod;
    private FieldInfo? _chartNamedSectionsField;
    private MethodInfo? _songEntryLoadChartMethod;
    private MethodInfo? _songEntryLoadChartWithFlagMethod;
    private PropertyInfo? _resultStatsArrayProperty;
    private Type? _playerStatsType;
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
    private bool _overlayEditorTransparencyDragging;
    private Texture2D? _overlayColorWheelTexture;
    private float _overlayColorWheelTextureValue = -1f;
    private Texture2D? _overlayEditorPanelTexture;
    private float _overlayEditorPanelTextureAlpha = -1f;
    private int _overlayEditorPanelTextureWidth;
    private int _overlayEditorPanelTextureHeight;
    private Texture2D? _overlaySliderTrackTexture;
    private Texture2D? _overlaySliderFillTexture;
    private Texture2D? _overlaySliderKnobTexture;
    private readonly Dictionary<string, Texture2D> _resizeCornerTextureCache = new();
    private string? _songResetConfirmKey;
    private float _songResetConfirmExpiresAt;
    private string? _overlayResetConfirmKey;
    private float _overlayResetConfirmExpiresAt;
    private float _wipeAllDataConfirmExpiresAt;
    private Process? _desktopOverlayProcess;
    private float _lastDesktopOverlayCheckAt = -999f;
    private bool _desktopOverlayLaunchFailed;
    private bool _obsLegacyCurrentCleanupCompleted;
    private string? _lastMenuOverlayStateFailureMessage;
    private const float OverlayWidgetDefaultWidth = 300f;
    private const float OverlayWidgetDefaultHeight = 90f;
    private const int OverlayWidgetResizeModeVersion = 2;

    public void Tick(object gameManager)
    {
        EnsureInitialized(gameManager.GetType().Assembly);
        _gameManagerType ??= gameManager.GetType();
        _activeGameManager = gameManager;
        CacheReflection();

        TrackerState state = BuildState(gameManager);
        _latestState = state;

        if (!state.IsInSong && Time.unscaledTime - _lastConfigReloadAt >= 1.5f)
        {
            _lastConfigReloadAt = Time.unscaledTime;
            _config = LoadJson(_configPath, _config);
            _config.DesktopOverlayStyle = GetMergedDesktopOverlayStyle();
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
    }

    public void HandleOverlayUpdate()
    {
        if (_overlayEditorVisible && Input.GetKeyDown(KeyCode.Escape))
        {
            _overlayEditorVisible = false;
            StockTrackerLog.WriteDebug("OverlayToggle | visible=0 | key=Escape");
            return;
        }

        bool controlHeld = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
        if ((controlHeld && Input.GetKeyDown(KeyCode.O)) ||
            Input.GetKeyDown(KeyCode.F8) ||
            Input.GetKeyDown(KeyCode.Home))
        {
            _overlayEditorVisible = !_overlayEditorVisible;
            string toggleKey = controlHeld
                ? "Ctrl+O"
                : (Input.GetKeyDown(KeyCode.Home) ? "Home" : "F8");
            StockTrackerLog.WriteDebug("OverlayToggle | visible=" + (_overlayEditorVisible ? "1" : "0") + " | key=" + toggleKey);
        }
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

        TrackerState state = _latestState ?? CreateIdleState();
        if (!state.IsInSong && _overlayEditorVisible)
        {
            state = BuildMenuOverlayState();
        }

        if (!state.IsInSong && !_overlayEditorVisible)
        {
            return;
        }

        SongConfig? songConfig = null;
        if (state.Song != null)
        {
            _config.Songs.TryGetValue(state.Song.OverlayLayoutKey ?? state.Song.SongKey, out songConfig);
            if (songConfig != null)
            {
                NormalizeSongOverlayWidgets(songConfig);
            }
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

        bool renderWidgetsInGame = state.IsInSong && (_overlayEditorVisible || !IsDesktopOverlayRunning());
        if (renderWidgetsInGame && state.Song != null && songConfig != null)
        {
            RenderOverlayWidgets(state, songConfig);
        }

        if (_overlayEditorVisible)
        {
            RenderOverlayEditor(state, songConfig);
        }

        if (songConfig != null)
        {
            RenderOverlayColorPicker(songConfig);
        }
    }

    private TrackerState BuildMenuOverlayState()
    {
        Dictionary<string, bool> defaultExports = new(EnsureDefaultEnabledTextExports(), StringComparer.Ordinal);
        TrackerState state = CreateIdleState();
        state.OverlayEditorVisible = _overlayEditorVisible;
        state.EnabledTextExports = defaultExports;
        return state;
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

    private void RenderOverlayWidgets(TrackerState state, SongConfig songConfig)
    {
        Dictionary<string, int> sectionOrder = BuildOverlaySectionOrder(state);
        List<KeyValuePair<string, OverlayWidgetConfig>> widgets = songConfig.OverlayWidgets
            .Where(pair => pair.Value != null && pair.Value.Enabled)
            .ToList();
        widgets.Sort((left, right) => CompareOverlayWidgetEntries(sectionOrder, left, right));

        for (int i = 0; i < widgets.Count; i++)
        {
            string widgetKey = widgets[i].Key;
            OverlayWidgetConfig widgetConfig = widgets[i].Value;
            if (!TryBuildOverlayWidget(state, widgetKey, out string title, out string content))
            {
                continue;
            }

            Rect rect = GetWidgetRect(widgetConfig, i);
            Rect updated = RenderOverlayWidgetPanel("widget:" + widgetKey, widgetConfig, rect, title, content);
            PersistWidgetRect(widgetConfig, updated);
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
            MarkConfigDirty();
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
                        MarkConfigDirty();
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool disableAllExportsClicked = GUILayout.Toggle(false, new GUIContent("DISABLE ALL EXPORTS"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (disableAllExportsClicked)
                {
                    DisableAllTextExports(defaultEnabledTextExports);
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
                        MarkConfigDirty();
                    }
                }

                GUILayout.Label(string.Empty, GUILayout.Height(8f));
                bool disableAllExportsClicked = GUILayout.Toggle(false, new GUIContent("DISABLE ALL EXPORTS"), GUI.skin.button, GUILayout.Width(Mathf.Min(220f, contentRect.width - 24f)));
                if (disableAllExportsClicked)
                {
                    DisableAllTextExports(defaultEnabledTextExports);
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
                            MarkConfigDirty();
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

    private void RenderOverlayEditorTransparencyControls(OverlayEditorConfig overlayConfig, float width)
    {
        GUILayout.Label("Overlay Transparency", GUILayout.Width(width));
        float alpha = Mathf.Clamp01(overlayConfig.BackgroundA);
        float updatedAlpha = RenderOverlayEditorTransparencySlider(alpha, width, 0.15f, 1f);
        if (Math.Abs(updatedAlpha - alpha) >= 0.001f)
        {
            overlayConfig.BackgroundA = updatedAlpha;
            MarkConfigDirty();
        }

        GUILayout.Label($"Background Opacity: {Mathf.RoundToInt(Mathf.Clamp01(overlayConfig.BackgroundA) * 100f)}%", GUILayout.Width(width));
    }

    private float RenderOverlayEditorTransparencySlider(float value, float width, float min, float max)
    {
        Rect sliderRect = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none, GUILayout.Width(width), GUILayout.Height(18f));
        sliderRect.width = width;
        sliderRect.height = 18f;

        if (_overlayEditorTransparencyDragging && !Input.GetMouseButton(0))
        {
            _overlayEditorTransparencyDragging = false;
        }

        Event? currentEvent = Event.current;
        if (currentEvent != null)
        {
            switch (currentEvent.type)
            {
                case EventType.MouseDown:
                    if (sliderRect.Contains(currentEvent.mousePosition))
                    {
                        _overlayEditorTransparencyDragging = true;
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseDrag:
                    if (_overlayEditorTransparencyDragging)
                    {
                        currentEvent.Use();
                    }
                    break;
                case EventType.MouseUp:
                    if (_overlayEditorTransparencyDragging)
                    {
                        _overlayEditorTransparencyDragging = false;
                        currentEvent.Use();
                    }
                    break;
            }
        }

        if (_overlayEditorTransparencyDragging && currentEvent != null)
        {
            float normalized = Mathf.InverseLerp(sliderRect.x, sliderRect.xMax, currentEvent.mousePosition.x);
            value = Mathf.Lerp(min, max, Mathf.Clamp01(normalized));
        }

        Rect trackRect = new Rect(sliderRect.x, sliderRect.y + 5f, sliderRect.width, 8f);
        GUI.Label(trackRect, new GUIContent(string.Empty, EnsureOverlaySliderTrackTexture(), string.Empty), GUI.skin.label);

        float fillWidth = Mathf.Lerp(0f, Mathf.Max(0f, trackRect.width - 2f), Mathf.InverseLerp(min, max, value));
        if (fillWidth > 0f)
        {
            Rect fillRect = new Rect(trackRect.x + 1f, trackRect.y + 1f, fillWidth, Mathf.Max(0f, trackRect.height - 2f));
            GUI.Label(fillRect, new GUIContent(string.Empty, EnsureOverlaySliderFillTexture(), string.Empty), GUI.skin.label);
        }

        float knobX = Mathf.Lerp(trackRect.x - 2f, trackRect.xMax - 14f, Mathf.InverseLerp(min, max, value));
        Rect knobRect = new Rect(knobX, sliderRect.y, 14f, 18f);
        GUI.Label(knobRect, new GUIContent(string.Empty, EnsureOverlaySliderKnobTexture(), string.Empty), GUI.skin.label);

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

    private Texture2D EnsureOverlaySliderTrackTexture()
    {
        if (_overlaySliderTrackTexture == null)
        {
            _overlaySliderTrackTexture = BuildSolidTexture(new Color(0f, 0f, 0f, 0.55f));
        }

        return _overlaySliderTrackTexture;
    }

    private Texture2D EnsureOverlaySliderFillTexture()
    {
        if (_overlaySliderFillTexture == null)
        {
            _overlaySliderFillTexture = BuildSolidTexture(new Color(0.2f, 0.72f, 0.9f, 0.75f));
        }

        return _overlaySliderFillTexture;
    }

    private Texture2D EnsureOverlaySliderKnobTexture()
    {
        if (_overlaySliderKnobTexture == null)
        {
            const int width = 14;
            const int height = 18;
            Texture2D texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
            Color[] pixels = new Color[width * height];
            Vector2 center = new Vector2((width - 1) * 0.5f, (height - 1) * 0.5f);
            float radiusX = (width - 2) * 0.5f;
            float radiusY = (height - 2) * 0.5f;
            Color fillColor = new Color(0.92f, 0.97f, 1f, 0.98f);
            Color edgeColor = new Color(0.1f, 0.16f, 0.2f, 0.92f);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    float dx = (x - center.x) / Math.Max(0.001f, radiusX);
                    float dy = (y - center.y) / Math.Max(0.001f, radiusY);
                    float distance = (dx * dx) + (dy * dy);
                    int pixelIndex = (y * width) + x;
                    if (distance > 1.06f)
                    {
                        pixels[pixelIndex] = new Color(0f, 0f, 0f, 0f);
                    }
                    else if (distance > 0.78f)
                    {
                        pixels[pixelIndex] = edgeColor;
                    }
                    else
                    {
                        pixels[pixelIndex] = fillColor;
                    }
                }
            }

            texture.SetPixels(0, 0, width, height, pixels);
            texture.Apply();
            _overlaySliderKnobTexture = texture;
        }

        return _overlaySliderKnobTexture;
    }

    private static Texture2D BuildSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
        texture.SetPixels(0, 0, 1, 1, new[] { color });
        texture.Apply();
        return texture;
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

    private Dictionary<string, bool> EnsureDefaultEnabledTextExports()
    {
        _config.DefaultEnabledTextExports ??= new Dictionary<string, bool>(StringComparer.Ordinal);
        if (MigrateLegacySectionTextExports(_config.DefaultEnabledTextExports))
        {
            MarkConfigDirty();
        }

        if (RemoveDeprecatedTextExports(_config.DefaultEnabledTextExports))
        {
            MarkConfigDirty();
        }

        return _config.DefaultEnabledTextExports;
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
        MarkConfigDirty();
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

    private void NormalizeSongOverlayWidgets(SongConfig songConfig)
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
            MarkConfigDirty();
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

    private void RenderOverlayColorPicker(SongConfig songConfig)
    {
        bool editingDesktopBorder = string.Equals(_overlayColorTargetKey, "desktop:border", StringComparison.Ordinal);
        OverlayWidgetConfig? widgetConfig = null;
        string targetKey = _overlayColorTargetKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(targetKey) ||
            (!editingDesktopBorder &&
             (!targetKey.StartsWith("widget:", StringComparison.Ordinal) ||
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
        GUI.Label(new Rect(_overlayColorPickerRect.x + 8f, _overlayColorPickerRect.y + 6f, _overlayColorPickerRect.width - 16f, 20f), editingDesktopBorder ? "Widget Border Color" : "Overlay Color", GUI.skin.label);

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
                UpdateOverlayColorFromWheel(widgetConfig, editingDesktopBorder, wheelRect, currentEvent.mousePosition);
                currentEvent.Use();
            }
            else if (currentEvent.type == EventType.MouseDrag &&
                _overlayColorPickerDragging &&
                Input.GetMouseButton(0))
            {
                UpdateOverlayColorFromWheel(widgetConfig, editingDesktopBorder, wheelRect, currentEvent.mousePosition);
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

        bool confirmClicked = GUI.Toggle(confirmRect, false, new GUIContent("CONFIRM"), GUI.skin.button);
        if (confirmClicked)
        {
            _overlayColorTargetKey = null;
            _overlayColorPickerDragging = false;
        }
    }

    private void UpdateOverlayColorFromWheel(OverlayWidgetConfig? config, bool editingDesktopBorder, Rect wheelRect, Vector2 mousePosition)
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
        ApplyOverlayColor(config, editingDesktopBorder);
    }

    private void ApplyOverlayColor(OverlayWidgetConfig? config, bool editingDesktopBorder)
    {
        Color color = HsvToRgb(_overlayColorPickerHue, _overlayColorPickerSaturation, _overlayColorPickerValue);
        if (editingDesktopBorder)
        {
            _config.DesktopOverlayStyle.BorderR = color.r;
            _config.DesktopOverlayStyle.BorderG = color.g;
            _config.DesktopOverlayStyle.BorderB = color.b;
            SaveDesktopOverlayStyle();
        }
        else if (config != null)
        {
            config.BackgroundR = color.r;
            config.BackgroundG = color.g;
            config.BackgroundB = color.b;
        }
        MarkConfigDirty();
        _overlayColorWheelTextureValue = -1f;
    }

    private Color GetDesktopBorderColor()
    {
        DesktopOverlayStyleConfig style = _config.DesktopOverlayStyle ?? new DesktopOverlayStyleConfig();
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
        int lineCount = Math.Max(1, content.Split('\n').Length);
        float baseTitleSize = isSectionWidget ? 17f : 15f;
        float baseContentSize = isSectionWidget ? 17f : (lineCount > 1 ? 16f : 18f);
        float maxTitleSize = Mathf.Max(13f, Mathf.Min(24f, (rect.height - 12f) * 0.34f));
        float availableContentHeight = Mathf.Max(22f, rect.height - 44f);
        float maxContentSize = Mathf.Max(12f, (availableContentHeight / lineCount) - 1f);
        titleFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(baseTitleSize, maxTitleSize)), 12, 28);
        contentFontSize = Mathf.Clamp(Mathf.RoundToInt(Mathf.Min(baseContentSize, maxContentSize)), 11, 34);
    }

    private static GUIStyle BuildWidgetTitleStyle(Rect rect, bool isSectionWidget, Color textColor)
    {
        GetOverlayWidgetFontSizes(rect, isSectionWidget, string.Empty, out int titleFontSize, out _);
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleLeft,
            fontStyle = FontStyle.Bold,
            fontSize = titleFontSize
        };
        style.normal.textColor = textColor;
        return style;
    }

    private static GUIStyle BuildWidgetContentStyle(Rect rect, bool isSectionWidget, string content, Color textColor)
    {
        GetOverlayWidgetFontSizes(rect, isSectionWidget, content, out _, out int contentFontSize);
        var style = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = contentFontSize
        };
        style.normal.textColor = textColor;
        return style;
    }

    private static void RenderSectionWidgetContent(Rect contentRect, string content, Color textColor)
    {
        string[] lines = content.Split('\n');
        string attemptsLine = lines.Length >= 1 ? lines[0] : string.Empty;
        string emphasisLine = lines.Length >= 2 ? lines[1] : string.Empty;

        GUIStyle attemptsStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontSize = 14
        };
        attemptsStyle.normal.textColor = textColor;

        GUIStyle emphasisStyle = new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.UpperLeft,
            fontStyle = FontStyle.Bold,
            fontSize = 17
        };
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
            "starts" => state.Starts.ToString(CultureInfo.InvariantCulture),
            "restarts" => state.Restarts.ToString(CultureInfo.InvariantCulture),
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

    private static bool IsTextExportEnabled(IReadOnlyDictionary<string, bool> enabledTextExports, string exportKey)
    {
        return enabledTextExports.TryGetValue(exportKey, out bool enabled) && enabled;
    }

    private static bool IsTextExportEnabled(TrackerState state, string exportKey)
    {
        return IsTextExportEnabled(state.EnabledTextExports, exportKey);
    }

    private static bool HasAnyTextExportEnabled(TrackerState state)
    {
        return state.EnabledTextExports.Any(pair =>
            !string.Equals(pair.Key, NoteSplitModeExportKey, StringComparison.Ordinal) &&
            pair.Value) ||
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
        MarkConfigDirty();
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
        if (IsTextExportEnabled(EnsureDefaultEnabledTextExports(), NoteSplitModeExportKey))
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

        _dataDir = StatTrackDataPaths.EnsureDataDirectoryMigrated(message => StockTrackerLog.WriteDebug(message));
        _statePath = Path.Combine(_dataDir, "state.json");
        _memoryPath = Path.Combine(_dataDir, "memory.json");
        _configPath = Path.Combine(_dataDir, "config.json");
        _desktopStylePath = Path.Combine(_dataDir, "desktop-style.json");
        _obsDir = Path.Combine(_dataDir, "obs");
        _obsStatePath = Path.Combine(_obsDir, "state.json");
        Directory.CreateDirectory(_dataDir);
        Directory.CreateDirectory(_obsDir);
        EnsureExportWorkerStarted();
        _memory = LoadJson(_memoryPath, new TrackerMemory());
        _config = LoadJson(_configPath, new TrackerConfig());
        _config.DesktopOverlayStyle = GetMergedDesktopOverlayStyle();
        _memoryDirty = !File.Exists(_memoryPath);
        _configDirty = !File.Exists(_configPath);
        _latestState = CreateIdleState();
        _overlayEditorVisible = false;
        _globalVariablesType = assemblyCSharp.GetType(GlobalVariablesTypeName);
        _basePlayerType = assemblyCSharp.GetType(BasePlayerTypeName);
        _gameSettingType = Type.GetType("StrikeCore.GameSetting, StrikeCore", throwOnError: false);
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
        _chartField ??= _gameManagerType.GetField(CurrentChartFieldName, AnyInstance)
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
        Dictionary<string, bool> enabledTextExports = EnsureDefaultEnabledTextExports();
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

    private static Dictionary<string, bool> CloneEnabledTextExports(IReadOnlyDictionary<string, bool> enabledTextExports)
    {
        var clone = new Dictionary<string, bool>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> pair in enabledTextExports)
        {
            clone[pair.Key] = pair.Value;
        }

        return clone;
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

    private TrackerState BuildState(object gameManager)
    {
        object? activeChartHint = _chartField?.GetValue(gameManager);
        bool stableCacheLooksValid =
            _runState.CachedSongEntry != null &&
            _runState.CachedPlayer != null &&
            _runState.CachedChart != null &&
            activeChartHint != null &&
            ReferenceEquals(activeChartHint, _runState.CachedChart);

        bool canUseStableRunCache = stableCacheLooksValid &&
            Time.unscaledTime - _runState.LastStableRefreshAt < StableRunRefreshIntervalSeconds;

        object? songEntry = canUseStableRunCache ? _runState.CachedSongEntry : null;
        object? player = canUseStableRunCache ? _runState.CachedPlayer : null;
        object? chart = canUseStableRunCache ? _runState.CachedChart : null;

        if (!canUseStableRunCache)
        {
            try
            {
                object? globalVariables = _globalVariablesSingletonField?.GetValue(null);
                songEntry = globalVariables == null ? null : _globalVariablesCurrentSongField?.GetValue(globalVariables);
            }
            catch (Exception ex)
            {
                StockTrackerLog.Write(ex);
            }

            player = GetPreferredActivePlayer(gameManager);
            chart = _chartField?.GetValue(gameManager);
        }

        if (player == null || songEntry == null || chart == null)
        {
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
        double songDuration = canUseStableRunCache
            ? _runState.CachedSongDuration
            : ConvertToDouble(_songDurationField?.GetValue(gameManager));
        bool isPractice = IsPracticeMode(gameManager);
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

        bool newSong = !string.Equals(_runState.SongKey, song.SongKey, StringComparison.Ordinal);
        bool restarted = _runState.SongKey == song.SongKey && songTime + 1.0 < _runState.LastSongTime;
        if ((newSong || restarted || !_runState.InRun) && (requirements.NeedRunTracking || requirements.NeedCompletedRunTracking || requirements.NeedMissedNotes))
        {
            ResetExactMissCounter(player);
        }

        int currentMissedNotes = requirements.NeedMissedNotes || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? ReadExactMissedNotesCount(player)
            : 0;
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

        List<SectionDescriptor> sections = requirements.NeedSections || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? BuildSections(chart, songEntry, song.SongKey)
            : new List<SectionDescriptor>();
        if (sections.Count > 0)
        {
            songConfig = EnsureSongConfig(song, sections);
            _runState.CachedSongConfig = songConfig;
            _runState.CachedSongConfigKey = song.SongKey;
        }

        string currentSectionName = requirements.NeedCurrentSection || requirements.NeedRunTracking || requirements.NeedCompletedRunTracking
            ? GetCurrentSectionName(sections, songTime)
            : string.Empty;
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
        Dictionary<string, bool> enabledTextExports = CloneEnabledTextExports(EnsureDefaultEnabledTextExports());
        bool noteSplitEnabled = IsTextExportEnabled(enabledTextExports, NoteSplitModeExportKey);
        SectionStatsState? currentSectionStats = string.IsNullOrWhiteSpace(currentSectionName)
            ? null
            : (sectionStatsByName.TryGetValue(currentSectionName, out SectionStatsState? currentStats) ? currentStats : null);
        List<NoteSplitSectionState> noteSplitSections = noteSplitEnabled
            ? BuildNoteSplitSections(sections, songMemory, currentSectionName)
            : new List<NoteSplitSectionState>();
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
        RecordNoteSplitExactMiss();
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

    private void RecordNoteSplitExactMiss()
    {
        if (!_runState.InRun)
        {
            return;
        }

        if (!TryResolveCurrentSectionNameForNoteSplitEvent(out string sectionName))
        {
            _runState.PendingNoteSplitMisses++;
            return;
        }

        ApplyPendingNoteSplitMisses(sectionName);
        AddNoteSplitMissToSection(sectionName, 1);
    }

    private bool TryResolveCurrentSectionNameForNoteSplitEvent(out string sectionName)
    {
        sectionName = string.Empty;
        if (_activeGameManager == null || _songTimeField == null)
        {
            return false;
        }

        string songKey =
            _runState.CachedSongDescriptor?.SongKey ??
            _latestState.Song?.SongKey ??
            _runState.SongKey;
        if (string.IsNullOrWhiteSpace(songKey))
        {
            return false;
        }

        List<SectionDescriptor>? sections = null;
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

        double songTime = ConvertToDouble(_songTimeField.GetValue(_activeGameManager));
        sectionName = GetCurrentSectionName(sections, songTime);
        return !string.IsNullOrWhiteSpace(sectionName);
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

    private void MarkMemoryDirty()
    {
        _memoryDirty = true;
        _memoryVersion++;
    }

    private void MarkConfigDirty()
    {
        _configDirty = true;
        _configVersion++;
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

    private List<SectionDescriptor> BuildSections(object chart, object songEntry, string songKey)
    {
        if (_songSectionsCache.TryGetValue(songKey, out List<SectionDescriptor>? cachedSections) && cachedSections.Count > 0)
        {
            return cachedSections;
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
            return CacheSections(songKey, sections);
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
            return CacheSections(songKey, sections);
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
                return CacheSections(songKey, sections);
            }
        }

        LogChartFields(chart);
        LogChartMethods(chart);
        List<SectionDescriptor> fallbackSections = BuildSectionsFromSng(songEntry, chart, songKey);
        if (fallbackSections.Count > 0)
        {
            return CacheSections(songKey, fallbackSections);
        }

        return sections;
    }

    private List<SectionDescriptor> CacheSections(string songKey, IEnumerable<SectionDescriptor> sections)
    {
        List<SectionDescriptor> orderedSections = sections
            .OrderBy(section => section.StartTime)
            .ThenBy(section => section.Index)
            .ToList();
        AssignSectionDisplayNames(orderedSections);
        _songSectionsCache[songKey] = orderedSections;
        _songSectionNamesCache[songKey] = orderedSections.Select(GetSectionDisplayName).ToList();
        return orderedSections;
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

    private List<SectionDescriptor> BuildSectionsFromSng(object songEntry, object runtimeChart, string songKey)
    {
        try
        {
            List<SectionDescriptor> sections = ExtractSectionsFromSongEntry(songEntry, songKey);
            if (sections.Count == 0)
            {
                return new List<SectionDescriptor>();
            }

            return CacheSections(songKey, sections);
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

    private List<double> GetRuntimeSectionTimes(object chart)
    {
        try
        {
            if (_chartSectionsMethod != null)
            {
                object? value = _chartSectionsMethod.Invoke(chart, null);
                if (value is IEnumerable enumerable && value is not string)
                {
                    List<double> numericTimes = enumerable.Cast<object?>()
                        .Where(item => item != null && IsNumericType(item.GetType()))
                        .Select(ConvertToDouble)
                        .ToList();
                    if (numericTimes.Count > 0)
                    {
                        return numericTimes;
                    }
                }
            }

            foreach (FieldInfo field in GetAllFields(chart.GetType()))
            {
                object? value = field.GetValue(chart);
                if (value is Array array && (field.FieldType == typeof(float[]) || field.FieldType == typeof(double[])))
                {
                    List<double> numericTimes = array.Cast<object?>()
                        .Where(item => item != null)
                        .Select(ConvertToDouble)
                        .ToList();
                    if (numericTimes.Count > 1)
                    {
                        return numericTimes;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            StockTrackerLog.Write(ex);
        }

        return new List<double>();
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
                    else if (metaType == 0x01 || metaType == 0x05 || metaType == 0x06)
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
            _sectionSnapshotCache.MemoryVersion == _memoryVersion &&
            _sectionSnapshotCache.ConfigVersion == _configVersion &&
            _sectionSnapshotCache.SectionCount == sections.Count)
        {
            return _sectionSnapshotCache;
        }

        List<TrackedSectionState> trackedSections = sections.Select(section => BuildTrackedSectionState(sections, section, songConfig, songMemory)).ToList();
        List<SectionStatsState> sectionStats = sections.Select(section => BuildSectionStatsState(sections, section, songConfig, songMemory)).ToList();
        _sectionSnapshotCache = new SectionSnapshotCache
        {
            SongKey = songKey,
            MemoryVersion = _memoryVersion,
            ConfigVersion = _configVersion,
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
        if (trimmed.StartsWith("[", StringComparison.Ordinal) && trimmed.EndsWith("]", StringComparison.Ordinal))
        {
            string bracketed = trimmed.Substring(1, trimmed.Length - 2).Trim();
            const string sectionPrefix = "section ";
            if (bracketed.StartsWith(sectionPrefix, StringComparison.OrdinalIgnoreCase))
            {
                bracketed = bracketed.Substring(sectionPrefix.Length).Trim();
            }

            if (string.IsNullOrWhiteSpace(bracketed))
            {
                return null;
            }

            string normalized = bracketed.Replace('_', ' ').Trim();
            if (ShouldIgnoreMidiSectionName(normalized))
            {
                return null;
            }

            return normalized;
        }

        return null;
    }

    private static bool ShouldIgnoreMidiSectionName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return true;
        }

        return string.Equals(name, "solo on", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "solo off", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "music start", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "end", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "wail on", StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("lighting ", StringComparison.OrdinalIgnoreCase);
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

    private static string GetCurrentSectionName(List<SectionDescriptor> sections, double songTime)
    {
        if (sections.Count == 0)
        {
            return string.Empty;
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
            LegacySongKey = BuildLegacySongKey(songEntry, songSpeed.Percent, difficulty),
            OverlayLayoutKey = BuildOverlayLayoutKey(songEntry),
            OverlayLegacyKey = BuildOverlayLegacyKey(songEntry)
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

    private static string BuildLegacySongKey(object songEntry, int songSpeedPercent, DifficultyInfo difficulty)
    {
        string speedSuffix = $"@{songSpeedPercent}%";
        string checksum = GetStringProperty(songEntry, "checksumString") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(checksum))
        {
            return difficulty.Code + ":" + checksum.Trim() + speedSuffix;
        }

        string folderPath = GetStringField(songEntry, "folderPath") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return difficulty.Code + ":" + folderPath.Trim() + speedSuffix;
        }

        string title = GetStringProperty(songEntry, "Name_StrippedTags") ?? "unknown-title";
        string artist = GetStringProperty(songEntry, "Artist_StrippedTags") ?? "unknown-artist";
        return $"{difficulty.Code}:{artist}::{title}{speedSuffix}";
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

    private static string? BuildOverlayLegacyKey(object songEntry)
    {
        string checksum = GetStringProperty(songEntry, "checksumString") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(checksum))
        {
            return checksum.Trim();
        }

        string folderPath = GetStringField(songEntry, "folderPath") ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(folderPath))
        {
            return folderPath.Trim();
        }

        return null;
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

    private SongMemory EnsureSongMemory(SongDescriptor song, IEnumerable<SectionDescriptor> sections)
    {
        List<SectionDescriptor> sectionList = sections.ToList();
        if (!_memory.Songs.TryGetValue(song.SongKey, out SongMemory? songMemory))
        {
            songMemory = TryMoveLegacyEntry(_memory.Songs, song.SongKey, song.LegacySongKey) ?? new SongMemory();
            _memory.Songs[song.SongKey] = songMemory;
            MarkMemoryDirty();
        }

        if (!string.Equals(songMemory.Title, song.Title, StringComparison.Ordinal))
        {
            songMemory.Title = song.Title;
            MarkMemoryDirty();
        }
        if (!string.Equals(songMemory.Artist, song.Artist, StringComparison.Ordinal))
        {
            songMemory.Artist = song.Artist;
            MarkMemoryDirty();
        }
        if (!string.Equals(songMemory.Charter, song.Charter, StringComparison.Ordinal))
        {
            songMemory.Charter = song.Charter;
            MarkMemoryDirty();
        }

        foreach (SectionDescriptor section in sectionList)
        {
            string sectionKey = BuildSectionOverlayKey(sectionList, section);
            EnsureSectionMemory(songMemory, sectionKey, section.Name);
        }

        return songMemory;
    }

    private SectionMemory EnsureSectionMemory(SongMemory songMemory, string sectionKey, string? legacySectionKey = null)
    {
        if (songMemory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory))
        {
            return sectionMemory;
        }

        string normalizedLegacySectionKey = legacySectionKey ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(normalizedLegacySectionKey) &&
            !string.Equals(sectionKey, normalizedLegacySectionKey, StringComparison.Ordinal) &&
            songMemory.Sections.TryGetValue(normalizedLegacySectionKey, out sectionMemory))
        {
            songMemory.Sections.Remove(normalizedLegacySectionKey);
            songMemory.Sections[sectionKey] = sectionMemory;
            MarkMemoryDirty();
            return sectionMemory;
        }

        sectionMemory = new SectionMemory();
        songMemory.Sections[sectionKey] = sectionMemory;
        MarkMemoryDirty();
        return sectionMemory;
    }

    private SongConfig EnsureSongConfig(SongDescriptor song, IEnumerable<SectionDescriptor> sections)
    {
        List<SectionDescriptor> sectionList = sections.ToList();
        string configKey = song.OverlayLayoutKey ?? song.SongKey;
        if (!_config.Songs.TryGetValue(configKey, out SongConfig? songConfig))
        {
            songConfig = TryMoveLegacyEntry(_config.Songs, configKey, song.OverlayLegacyKey)
                ?? TryMoveLegacyEntry(_config.Songs, configKey, song.SongKey)
                ?? TryMoveLegacyEntry(_config.Songs, configKey, song.LegacySongKey)
                ?? FindExistingSharedSongConfig(song, configKey)
                ?? new SongConfig();
            _config.Songs[configKey] = songConfig;
            MarkConfigDirty();
        }

        if (!string.Equals(songConfig.Title, song.Title, StringComparison.Ordinal))
        {
            songConfig.Title = song.Title;
            MarkConfigDirty();
        }
        if (!string.Equals(songConfig.Artist, song.Artist, StringComparison.Ordinal))
        {
            songConfig.Artist = song.Artist;
            MarkConfigDirty();
        }
        if (!string.Equals(songConfig.Charter, song.Charter, StringComparison.Ordinal))
        {
            songConfig.Charter = song.Charter;
            MarkConfigDirty();
        }

        foreach (SectionDescriptor section in sectionList)
        {
            string sectionKey = BuildSectionOverlayKey(sectionList, section);
            if (!songConfig.TrackedSections.ContainsKey(sectionKey))
            {
                bool migratedTracked = songConfig.TrackedSections.TryGetValue(section.Name, out bool legacyTracked) && legacyTracked;
                songConfig.TrackedSections[sectionKey] = migratedTracked;
                MarkConfigDirty();
            }
        }

        foreach (string deprecatedMetricKey in new[] { "score", "starts", "restarts" })
        {
            if (songConfig.OverlayWidgets.Remove(BuildMetricWidgetKey(deprecatedMetricKey)))
            {
                MarkConfigDirty();
            }
        }

        return songConfig;
    }

    private bool MigrateLegacySectionTextExports(Dictionary<string, bool> enabledTextExports)
    {
        bool changed = false;
        foreach (string legacyKey in new[] { "section_attempts", "section_fcs_past", "section_killed_the_run" })
        {
            if (enabledTextExports.Remove(legacyKey))
            {
                changed = true;
            }
        }

        return changed;
    }

    private static bool RemoveDeprecatedTextExports(Dictionary<string, bool> enabledTextExports)
    {
        bool changed = false;
        if (enabledTextExports.Remove("score"))
        {
            changed = true;
        }
        if (enabledTextExports.Remove("song_info"))
        {
            changed = true;
        }
        foreach (string removedKey in new[] { "starts", "restarts", "song_clock", "section_summary" })
        {
            if (enabledTextExports.Remove(removedKey))
            {
                changed = true;
            }
        }

        return changed;
    }

    private SongConfig? FindExistingSharedSongConfig(SongDescriptor song, string newKey)
    {
        string layoutKey = song.OverlayLayoutKey ?? string.Empty;
        foreach (KeyValuePair<string, SongConfig> pair in _config.Songs.ToList())
        {
            if (string.Equals(pair.Key, newKey, StringComparison.Ordinal))
            {
                continue;
            }

            if (!string.Equals(NormalizeExistingOverlayConfigKey(pair.Key), layoutKey, StringComparison.Ordinal))
            {
                continue;
            }

            _config.Songs.Remove(pair.Key);
            return pair.Value;
        }

        return null;
    }

    private static string NormalizeExistingOverlayConfigKey(string key)
    {
        string normalized = key.Trim();
        int speedSeparator = normalized.LastIndexOf(" @", StringComparison.Ordinal);
        if (speedSeparator >= 0)
        {
            normalized = normalized.Substring(0, speedSeparator).TrimEnd();
        }

        if (normalized.Length > 2 && normalized[1] == ' ')
        {
            normalized = normalized.Substring(2);
        }

        return normalized;
    }

    private void ResetSongOverlay(SongConfig songConfig)
    {
        songConfig.TrackedSections.Clear();
        songConfig.OverlayWidgets.Clear();
        _overlayColorTargetKey = null;
        _sectionSnapshotCache = null;
        MarkConfigDirty();
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
        _runState = new RunState();
        _latestState = CreateIdleState();
        _sectionSnapshotCache = null;
        _obsLegacyCurrentCleanupCompleted = false;
        _obsLegacySongCleanupKeys.Clear();
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
        _memoryVersion++;
        _configVersion++;

        lock (_exportWorkerSync)
        {
            _pendingExport = null;
        }

        lock (_fileWriteSync)
        {
            _fileWriteCache.Clear();
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

    private static TValue? TryMoveLegacyEntry<TValue>(Dictionary<string, TValue> dictionary, string newKey, string? legacyKey) where TValue : class
    {
        if (string.IsNullOrWhiteSpace(legacyKey) || string.Equals(newKey, legacyKey, StringComparison.Ordinal))
        {
            return null;
        }

        string lookupKey = legacyKey!.Trim();
        if (!dictionary.TryGetValue(lookupKey, out TValue? value))
        {
            return null;
        }

        dictionary.Remove(lookupKey);
        dictionary[newKey] = value;
        return value;
    }

    private void UpdateRunTracking(SongDescriptor song, SongMemory songMemory, SongConfig songConfig, string currentSectionName, double songTime, double songDuration, int score, int streak, int currentGhostNotes, int currentOverstrums, int currentMissedNotes, int currentNotesHit, PlayerStatsSnapshot? resultStats, bool isPractice, bool trackSongProgress, bool trackCompletedRuns)
    {
        bool newSong = !string.Equals(_runState.SongKey, song.SongKey, StringComparison.Ordinal);
        bool restarted = _runState.SongKey == song.SongKey && songTime + 1.0 < _runState.LastSongTime;
        bool startedFromSongSelect = !restarted &&
            !newSong &&
            _runState.InRun &&
            _runState.CompletedRunRecorded &&
            songTime <= 1.0;
        bool started = (newSong || !_runState.InRun || startedFromSongSelect) && !restarted;
        bool newRun = started || restarted;
        bool noteSplitEnabled = IsTextExportEnabled(EnsureDefaultEnabledTextExports(), NoteSplitModeExportKey);
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

        if (string.Equals(_runState.NoteSplitCurrentSection, currentSectionName, StringComparison.Ordinal))
        {
            ApplyPendingNoteSplitMisses(currentSectionName);
            return;
        }

        ApplyPendingNoteSplitMisses(currentSectionName);
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
            OverlayEditorVisible = _overlayEditorVisible
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
        bool exportStateJson = false;
        bool exportObs = false;
        if (Time.unscaledTime - _lastStateExportAt >= StateExportIntervalSeconds)
        {
            _lastStateExportAt = Time.unscaledTime;
            exportStateJson = true;
        }

        if (Time.unscaledTime - _lastObsExportAt >= ObsExportIntervalSeconds)
        {
            _lastObsExportAt = Time.unscaledTime;
            exportObs = true;
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
                ExportObs = exportObs
            };
        }

        _exportSignal.Set();
    }

    private void ExportObsState(TrackerState state, string stateJson)
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

        WriteTextFileCached(_obsStatePath, stateJson);

        string currentDir = Path.Combine(_obsDir, "current");
        Directory.CreateDirectory(currentDir);
        EnsureLegacyObsCurrentCleanup(currentDir);
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_section"), Path.Combine(currentDir, "current_section.txt"), FormatObsValue("Current Section", state.CurrentSection ?? string.Empty));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "streak"), Path.Combine(currentDir, "streak.txt"), FormatObsValue("Current Streak", state.Streak.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "best_streak"), Path.Combine(currentDir, "best_streak.txt"), FormatObsValue("Best FC Streak", state.BestStreak.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "starts"), Path.Combine(currentDir, "starts.txt"), FormatObsValue("Starts", state.Starts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "restarts"), Path.Combine(currentDir, "restarts.txt"), FormatObsValue("Restarts", state.Restarts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "attempts"), Path.Combine(currentDir, "attempts.txt"), FormatObsValue("Total Attempts", state.Attempts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_ghosted_notes"), Path.Combine(currentDir, "current_ghosted_notes.txt"), FormatObsValue("Current Ghosted Notes", state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_overstrums"), Path.Combine(currentDir, "current_overstrums.txt"), FormatObsValue("Current Overstrums", state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_missed_notes"), Path.Combine(currentDir, "current_missed_notes.txt"), FormatObsValue("Current Missed Notes", state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "lifetime_ghosted_notes"), Path.Combine(currentDir, "lifetime_ghosted_notes.txt"), FormatObsValue("Song Lifetime Ghosted Notes", state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "global_lifetime_ghosted_notes"), Path.Combine(currentDir, "global_lifetime_ghosted_notes.txt"), FormatObsValue("Global Lifetime Ghosted Notes", state.GlobalLifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "fc_achieved"), Path.Combine(currentDir, "fc_achieved.txt"), FormatObsValue("FC Achieved", state.FcAchieved ? "True" : "False"));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "song_clock"), Path.Combine(currentDir, "song_time.txt"), FormatObsValue("Song Time", state.SongTime.ToString("0.###", CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "song_clock"), Path.Combine(currentDir, "song_duration.txt"), FormatObsValue("Song Duration", state.SongDuration.ToString("0.###", CultureInfo.InvariantCulture)));

        bool exportTrackedSectionFiles = state.SectionStats.Any(candidate => candidate.Tracked);
        if (state.CurrentSectionStats != null && exportTrackedSectionFiles)
        {
            WriteObsText(Path.Combine(currentDir, "current_section_summary.txt"), BuildSectionSummary(state.CurrentSectionStats));
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
        EnsureLegacyObsSongCleanup(state.Song.SongKey, songDir);
        WriteOrDeleteObsText(IsTextExportEnabled(state, "starts"), Path.Combine(songDir, "starts.txt"), FormatObsValue("Starts", state.Starts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "restarts"), Path.Combine(songDir, "restarts.txt"), FormatObsValue("Restarts", state.Restarts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "attempts"), Path.Combine(songDir, "attempts.txt"), FormatObsValue("Total Attempts", state.Attempts.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_ghosted_notes"), Path.Combine(songDir, "current_ghosted_notes.txt"), FormatObsValue("Current Ghosted Notes", state.CurrentGhostedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_overstrums"), Path.Combine(songDir, "current_overstrums.txt"), FormatObsValue("Current Overstrums", state.CurrentOverstrums.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_missed_notes"), Path.Combine(songDir, "current_missed_notes.txt"), FormatObsValue("Current Missed Notes", state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture)));
        WriteOrDeleteObsText(IsTextExportEnabled(state, "lifetime_ghosted_notes"), Path.Combine(songDir, "lifetime_ghosted_notes.txt"), FormatObsValue("Song Lifetime Ghosted Notes", state.LifetimeGhostedNotes.ToString(CultureInfo.InvariantCulture)));
        string currentSectionExportName = state.CurrentSectionStats != null
            ? BuildSectionExportName(state.SectionStats, state.CurrentSectionStats)
            : state.CurrentSection ?? string.Empty;
        WriteOrDeleteObsText(IsTextExportEnabled(state, "current_section"), Path.Combine(songDir, "current_section.txt"), FormatObsValue("Current Section", currentSectionExportName));

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
                WriteObsText(Path.Combine(sectionDir, "name.txt"), FormatObsValue("Section Name", sectionExportName));
                WriteObsText(Path.Combine(sectionDir, "tracked.txt"), FormatObsValue("Tracked", section.Tracked ? "True" : "False"));
                WriteObsText(Path.Combine(sectionDir, "start_time.txt"), FormatObsValue("Start Time", section.StartTime.ToString("0.###", CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(sectionDir, "summary.txt"), BuildSectionSummary(section));
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
                WriteObsText(Path.Combine(runDir, "completed_at_utc.txt"), FormatObsValue("Completed At UTC", run.CompletedAtUtc ?? string.Empty));
                WriteObsText(Path.Combine(runDir, "percent.txt"), FormatObsValue("Percent", run.Percent.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "score.txt"), FormatObsValue("Score", run.Score.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "best_streak.txt"), FormatObsValue("Best Streak", run.BestStreak.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "first_miss_streak.txt"), FormatObsValue("First Miss Streak", run.FirstMissStreak.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "ghosted_notes.txt"), FormatObsValue("Ghosted Notes", run.GhostedNotes.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "overstrums.txt"), FormatObsValue("Overstrums", run.Overstrums.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "missed_notes.txt"), FormatObsValue("Missed Notes", run.MissedNotes.ToString(CultureInfo.InvariantCulture)));
                WriteObsText(Path.Combine(runDir, "fc_achieved.txt"), FormatObsValue("FC Achieved", run.FcAchieved ? "True" : "False"));
                WriteObsText(Path.Combine(runDir, "final_section.txt"), FormatObsValue("Final Section", run.FinalSection ?? string.Empty));
                WriteObsText(Path.Combine(runDir, "summary.txt"), FormatObsValue("Run Summary", BuildCompletedRunSummary(run)));
            }
        }
        else
        {
            DeleteObsDirectory(runsDir);
        }
    }

    private void EnsureLegacyObsCurrentCleanup(string currentDir)
    {
        if (_obsLegacyCurrentCleanupCompleted)
        {
            return;
        }

        _obsLegacyCurrentCleanupCompleted = true;
        DeleteObsText(Path.Combine(currentDir, "song_key.txt"));
        DeleteObsText(Path.Combine(currentDir, "song_title.txt"));
        DeleteObsText(Path.Combine(currentDir, "song_artist.txt"));
        DeleteObsText(Path.Combine(currentDir, "song_charter.txt"));
        DeleteObsText(Path.Combine(currentDir, "song_speed_percent.txt"));
        DeleteObsText(Path.Combine(currentDir, "song_speed_label.txt"));
        DeleteObsText(Path.Combine(currentDir, "score.txt"));
        DeleteObsText(Path.Combine(currentDir, "starts_plus_restarts.txt"));
        DeleteObsText(Path.Combine(currentDir, "run_had_miss.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_died.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_clears.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_runs_past.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_attempts.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_fcs_past.txt"));
        DeleteObsText(Path.Combine(currentDir, "current_section_killed_the_run.txt"));
    }

    private void EnsureLegacyObsSongCleanup(string songKey, string songDir)
    {
        if (!_obsLegacySongCleanupKeys.Add(songKey))
        {
            return;
        }

        DeleteObsText(Path.Combine(songDir, "title.txt"));
        DeleteObsText(Path.Combine(songDir, "artist.txt"));
        DeleteObsText(Path.Combine(songDir, "charter.txt"));
        DeleteObsText(Path.Combine(songDir, "song_speed_percent.txt"));
        DeleteObsText(Path.Combine(songDir, "song_speed_label.txt"));
        DeleteObsText(Path.Combine(songDir, "starts_plus_restarts.txt"));
        DeleteObsText(Path.Combine(songDir, "run_had_miss.txt"));

        string sectionsDir = Path.Combine(songDir, "sections");
        if (Directory.Exists(sectionsDir))
        {
            foreach (string sectionDir in Directory.GetDirectories(sectionsDir))
            {
                DeleteObsText(Path.Combine(sectionDir, "died.txt"));
                DeleteObsText(Path.Combine(sectionDir, "clears.txt"));
                DeleteObsText(Path.Combine(sectionDir, "runs_past.txt"));
                DeleteObsText(Path.Combine(sectionDir, "attempts.txt"));
                DeleteObsText(Path.Combine(sectionDir, "fcs_past.txt"));
                DeleteObsText(Path.Combine(sectionDir, "killed_the_run.txt"));
            }
        }

        string runsDir = Path.Combine(songDir, "runs");
        if (Directory.Exists(runsDir))
        {
            foreach (string runDir in Directory.GetDirectories(runsDir))
            {
                DeleteObsText(Path.Combine(runDir, "run_had_miss.txt"));
                DeleteObsText(Path.Combine(runDir, "song_speed_percent.txt"));
                DeleteObsText(Path.Combine(runDir, "song_speed_label.txt"));
                DeleteObsText(Path.Combine(runDir, "difficulty_code.txt"));
                DeleteObsText(Path.Combine(runDir, "difficulty_name.txt"));
            }
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

                    string stateJson = JsonConvert.SerializeObject(workItem.State);
                    if (workItem.ExportStateJson)
                    {
                        WriteTextFileCached(_statePath, stateJson);
                    }

                    if (workItem.ExportObs)
                    {
                        ExportObsState(workItem.State, stateJson);
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

        WriteTextFileCached(_memoryPath, JsonConvert.SerializeObject(_memory));
        _memoryDirty = false;
    }

    private void SaveConfig()
    {
        if (!_configDirty)
        {
            return;
        }

        _config.DesktopOverlayStyle = SaveDesktopOverlayStyle();
        WriteTextFileCached(_configPath, JsonConvert.SerializeObject(_config));
        _configDirty = false;
    }

    private DesktopOverlayStyleConfig SaveDesktopOverlayStyle()
    {
        DesktopOverlayStyleConfig style = GetMergedDesktopOverlayStyle();
        _config.DesktopOverlayStyle = style;
        if (string.IsNullOrWhiteSpace(_desktopStylePath))
        {
            return style;
        }

        WriteTextFileCached(_desktopStylePath, JsonConvert.SerializeObject(style));
        return style;
    }

    private DesktopOverlayStyleConfig GetMergedDesktopOverlayStyle()
    {
        DesktopOverlayStyleConfig configStyle = _config.DesktopOverlayStyle ?? new DesktopOverlayStyleConfig();
        if (string.IsNullOrWhiteSpace(_desktopStylePath) || !File.Exists(_desktopStylePath))
        {
            return CloneDesktopOverlayStyle(configStyle);
        }

        DesktopOverlayStyleConfig merged = LoadJson(_desktopStylePath, CloneDesktopOverlayStyle(configStyle));
        merged.BorderR = configStyle.BorderR;
        merged.BorderG = configStyle.BorderG;
        merged.BorderB = configStyle.BorderB;
        merged.BorderA = configStyle.BorderA;
        return merged;
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

    private List<NoteSplitSectionState> BuildNoteSplitSections(IReadOnlyList<SectionDescriptor> sections, SongMemory songMemory, string currentSectionName)
    {
        var rows = new List<NoteSplitSectionState>(sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            SectionDescriptor section = sections[i];
            string sectionKey = BuildSectionOverlayKey(sections, section);
            songMemory.Sections.TryGetValue(sectionKey, out SectionMemory? sectionMemory);
            _runState.NoteSplitSectionsThisRun.TryGetValue(sectionKey, out NoteSplitSectionRunState? runState);
            rows.Add(new NoteSplitSectionState
            {
                Order = section.Index,
                Key = sectionKey,
                Name = sectionKey,
                IsCurrent = string.Equals(sectionKey, currentSectionName, StringComparison.Ordinal),
                PersonalBestMissCount = sectionMemory?.BestMissCount,
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
    public Dictionary<string, bool> EnabledTextExports { get; set; } = new();
}

public sealed class SongDescriptor
{
    public string SongKey { get; set; } = string.Empty;
    public string? LegacySongKey { get; set; }
    public string? OverlayLayoutKey { get; set; }
    public string? OverlayLegacyKey { get; set; }
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
    public DesktopOverlayStyleConfig DesktopOverlayStyle { get; set; } = new();
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
    public float NoteSplitWidth { get; set; } = 360f;
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
    public int? BestMissCount { get; set; }
    public int? BestMissOverstrums { get; set; }

    [JsonProperty("Deaths")]
    private int LegacyDeaths
    {
        set
        {
            if (Attempts == 0)
            {
                Attempts = value;
            }

            if (KilledTheRun == 0)
            {
                KilledTheRun = value;
            }
        }
    }

    [JsonProperty("Clears")]
    private int LegacyClears
    {
        set
        {
            if (RunsPast == 0)
            {
                RunsPast = value;
            }
        }
    }
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
}


