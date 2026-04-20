#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace CloneHeroSectionTracker.DesktopOverlay;

internal static class StatTrackDataPaths
{
    internal const string CurrentDirectoryName = "StatTrack";

    internal static string GetCurrentDataDirectory()
    {
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), CurrentDirectoryName);
    }
}

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int gameProcessId = 0;
        string dataDir = StatTrackDataPaths.GetCurrentDataDirectory();
        for (int i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--pid", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length &&
                int.TryParse(args[i + 1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsedPid))
            {
                gameProcessId = parsedPid;
                i++;
            }
            else if (string.Equals(args[i], "--data-dir", StringComparison.OrdinalIgnoreCase) &&
                i + 1 < args.Length)
            {
                dataDir = args[i + 1];
                i++;
            }
        }

        using var mutex = new Mutex(initiallyOwned: true, @"Local\CloneHeroDesktopOverlay", out bool createdNew);
        if (!createdNew)
        {
            return;
        }

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new DesktopOverlayForm(gameProcessId, dataDir));
    }
}

internal sealed class DesktopOverlayForm : Form
{
    private static readonly JavaScriptSerializer Json = new();
    private static DesktopOverlayStyle? _styleStatic;
    private readonly int _gameProcessId;
    private readonly string _statePath;
    private readonly string _configPath;
    private readonly string _stylePath;
    private readonly string _logPath;
    private readonly System.Windows.Forms.Timer _timer;
    private DateTime _lastStateWriteUtc;
    private DateTime _lastConfigWriteUtc;
    private DateTime _lastStyleWriteUtc;
    private OverlayTrackerState _state = new();
    private OverlayTrackerConfig _config = new();
    private DesktopOverlayStyle _style = new();
    private string? _lastVisibilityReason;
    private NoteSplitWindowForm? _noteSplitWindow;
    public DesktopOverlayForm(int gameProcessId, string dataDir)
    {
        _gameProcessId = gameProcessId;
        _statePath = Path.Combine(dataDir, "state.json");
        _configPath = Path.Combine(dataDir, "config.json");
        _stylePath = Path.Combine(dataDir, "desktop-style.json");
        _logPath = Path.Combine(dataDir, "desktop-overlay.log");

        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Magenta;
        TransparencyKey = Color.Magenta;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Bounds = new Rectangle(-2000, -2000, 16, 16);
        _styleStatic = _style;

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();
        EnsureNoteSplitWindow();

        Log("Desktop overlay started | pid=" + _gameProcessId);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            const int WsExToolWindow = 0x00000080;
            const int WsExTransparent = 0x00000020;
            CreateParams cp = base.CreateParams;
            cp.ExStyle |= WsExToolWindow | WsExTransparent;
            return cp;
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        if (_noteSplitWindow != null && !_noteSplitWindow.IsDisposed)
        {
            _noteSplitWindow.Close();
        }

        base.OnFormClosed(e);
    }

    private void OnTimerTick()
    {
        try
        {
            if (!TryRefreshGameProcess())
            {
                LogVisibility("game_process_missing");
                if (_noteSplitWindow != null && !_noteSplitWindow.IsDisposed)
                {
                    _noteSplitWindow.Hide();
                }
                Close();
                return;
            }

            bool stateChanged = RefreshState();
            bool configChanged = RefreshConfig();
            bool styleChanged = RefreshStyle();
            bool noteSplitVisible = IsNoteSplitWindowVisible(_state);
            bool hasGameClientBounds = TryGetGameClientBounds(out Rectangle gameClientBounds);
            EnsureNoteSplitWindow().Synchronize(
                _state,
                _style,
                noteSplitVisible,
                hasGameClientBounds ? gameClientBounds : Rectangle.Empty,
                stateChanged || styleChanged || configChanged);

            if (!TryGetVisibleClientBounds(out Rectangle clientBounds, out string visibilityReason))
            {
                LogVisibility(visibilityReason);
                Hide();
                return;
            }

            OverlaySongConfig? songConfig = ResolveSongConfig(_state);
            bool widgetVisible = IsWidgetOverlayVisible(_state, songConfig);
            if (!widgetVisible)
            {
                Region = null;
                LogVisibility(GetStateVisibilityReason());
                Hide();
                return;
            }

            bool boundsChanged = Bounds != clientBounds;
            if (Bounds != clientBounds)
            {
                Bounds = clientBounds;
                Log("Desktop overlay bounds | x=" + clientBounds.X + " | y=" + clientBounds.Y + " | w=" + clientBounds.Width + " | h=" + clientBounds.Height);
            }

            if (configChanged || Region == null || !Visible || boundsChanged)
            {
                UpdateOverlayRegion(songConfig);
            }

            bool becameVisible = false;
            if (!Visible)
            {
                NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
                LogVisibility("visible");
                becameVisible = true;
            }

            if (stateChanged || configChanged || styleChanged || boundsChanged || becameVisible)
            {
                Invalidate();
            }
        }
        catch (Exception ex)
        {
            LogVisibility("exception_hidden");
            Log("Desktop overlay exception | " + ex);
            if (_noteSplitWindow != null && !_noteSplitWindow.IsDisposed)
            {
                _noteSplitWindow.Hide();
            }
            Hide();
        }
    }

    private bool TryRefreshGameProcess()
    {
        if (_gameProcessId <= 0)
        {
            return false;
        }

        try
        {
            using Process process = Process.GetProcessById(_gameProcessId);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    private bool RefreshState()
    {
        if (!File.Exists(_statePath))
        {
            return false;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(_statePath);
        if (writeTime == _lastStateWriteUtc)
        {
            return false;
        }

        try
        {
            OverlayTrackerState? loaded = Json.Deserialize<OverlayTrackerState>(File.ReadAllText(_statePath));
            if (loaded != null)
            {
                _state = loaded;
                _lastStateWriteUtc = writeTime;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private bool RefreshConfig()
    {
        if (!File.Exists(_configPath))
        {
            return false;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(_configPath);
        if (writeTime == _lastConfigWriteUtc)
        {
            return false;
        }

        try
        {
            OverlayTrackerConfig? loaded = Json.Deserialize<OverlayTrackerConfig>(File.ReadAllText(_configPath));
            if (loaded != null)
            {
                _config = loaded;
                _lastConfigWriteUtc = writeTime;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private bool RefreshStyle()
    {
        if (!File.Exists(_stylePath))
        {
            TryWriteDefaultStyle();
            return true;
        }

        DateTime writeTime = File.GetLastWriteTimeUtc(_stylePath);
        if (writeTime == _lastStyleWriteUtc)
        {
            return false;
        }

        try
        {
            DesktopOverlayStyle? loaded = Json.Deserialize<DesktopOverlayStyle>(File.ReadAllText(_stylePath));
            if (loaded != null)
            {
                _style = loaded;
                _styleStatic = loaded;
                _lastStyleWriteUtc = writeTime;
                return true;
            }
        }
        catch
        {
        }

        return false;
    }

    private void TryWriteDefaultStyle()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_stylePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (!File.Exists(_stylePath))
            {
                File.WriteAllText(_stylePath, Json.Serialize(_style));
            }

            _styleStatic = _style;
            _lastStyleWriteUtc = File.Exists(_stylePath) ? File.GetLastWriteTimeUtc(_stylePath) : DateTime.MinValue;
        }
        catch
        {
        }
    }

    private NoteSplitWindowForm EnsureNoteSplitWindow()
    {
        if (_noteSplitWindow != null && !_noteSplitWindow.IsDisposed)
        {
            return _noteSplitWindow;
        }

        _noteSplitWindow = new NoteSplitWindowForm(CloneStyle(_style), SaveStyle, Log, SetNoteSplitDialogActive);
        return _noteSplitWindow;
    }

    private void SetNoteSplitDialogActive(bool active)
    {
        if (active)
        {
            if (_timer.Enabled)
            {
                _timer.Stop();
            }

            return;
        }

        if (!_timer.Enabled)
        {
            _timer.Start();
        }

        OnTimerTick();
    }

    private void SaveStyle(DesktopOverlayStyle updatedStyle)
    {
        _style = CloneStyle(updatedStyle);
        _styleStatic = _style;
        WriteStyleFile();
    }

    private void WriteStyleFile()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_stylePath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(_stylePath, Json.Serialize(_style));
            _lastStyleWriteUtc = File.GetLastWriteTimeUtc(_stylePath);
        }
        catch
        {
        }
    }

    private static DesktopOverlayStyle CloneStyle(DesktopOverlayStyle style)
    {
        return new DesktopOverlayStyle
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

    private bool TryGetGameClientBounds(out Rectangle bounds)
    {
        bounds = Rectangle.Empty;
        IntPtr targetWindow = GetGameWindowHandle();
        if (targetWindow == IntPtr.Zero || NativeMethods.IsIconic(targetWindow))
        {
            return false;
        }

        if (!NativeMethods.GetClientRect(targetWindow, out NativeMethods.RECT clientRect))
        {
            return false;
        }

        NativeMethods.POINT topLeft = new() { X = clientRect.Left, Y = clientRect.Top };
        NativeMethods.POINT bottomRight = new() { X = clientRect.Right, Y = clientRect.Bottom };
        if (!NativeMethods.ClientToScreen(targetWindow, ref topLeft) ||
            !NativeMethods.ClientToScreen(targetWindow, ref bottomRight))
        {
            return false;
        }

        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        return bounds.Width > 0 && bounds.Height > 0;
    }


    private bool TryGetVisibleClientBounds(out Rectangle bounds, out string reason)
    {
        bounds = Rectangle.Empty;
        reason = "unknown";
        IntPtr foreground = NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
        {
            reason = "no_foreground";
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(foreground, out uint processId);
        if (processId != (uint)_gameProcessId)
        {
            reason = "game_not_foreground";
            return false;
        }

        if (!TryGetGameClientBounds(out bounds))
        {
            reason = "client_bounds_failed";
            return false;
        }

        reason = "visible";
        return true;
    }

    private IntPtr GetGameWindowHandle()
    {
        if (_gameProcessId <= 0)
        {
            return IntPtr.Zero;
        }

        try
        {
            using Process process = Process.GetProcessById(_gameProcessId);
            return process.MainWindowHandle;
        }
        catch
        {
            return IntPtr.Zero;
        }
    }

    private string GetStateVisibilityReason()
    {
        if (_state == null)
        {
            return "state_null";
        }

        if (!_state.IsInSong)
        {
            return "not_in_song";
        }

        if (_state.OverlayEditorVisible)
        {
            return "ingame_editor_visible";
        }

        if (_state.IsPracticeMode)
        {
            return "practice_mode";
        }

        if (_state.Song == null)
        {
            return "song_missing";
        }

        return HasEnabledWidgets(ResolveSongConfig(_state)) ? "visible" : "no_overlay_content";
    }

    private void LogVisibility(string reason)
    {
        if (string.Equals(_lastVisibilityReason, reason, StringComparison.Ordinal))
        {
            return;
        }

        _lastVisibilityReason = reason;
        Log("Desktop overlay state | " + reason);
    }

    private void Log(string message)
    {
        try
        {
            string? dir = Path.GetDirectoryName(_logPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.AppendAllText(_logPath, $"[{DateTime.UtcNow:O}] {message}{Environment.NewLine}");
        }
        catch
        {
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

        if (_state == null ||
            !_state.IsInSong ||
            _state.OverlayEditorVisible ||
            _state.Song == null)
        {
            return;
        }

        OverlaySongConfig? songConfig = ResolveSongConfig(_state);
        bool widgetVisible = IsWidgetOverlayVisible(_state, songConfig);
        if (!widgetVisible)
        {
            return;
        }

        if (songConfig == null)
        {
            return;
        }

        foreach (KeyValuePair<string, OverlayWidgetConfig> pair in GetOrderedWidgets(songConfig))
        {
            string widgetKey = pair.Key;
            OverlayWidgetConfig widgetConfig = pair.Value;
            if (widgetConfig == null || !widgetConfig.Enabled)
            {
                continue;
            }

            if (!TryBuildWidget(_state, widgetKey, out string title, out string content))
            {
                continue;
            }

            RectangleF rect = GetWidgetRectangle(widgetConfig);
            DrawWidget(e.Graphics, rect, title, content, widgetConfig, GetWidgetTextColor(widgetConfig), false, widgetKey.StartsWith("section:", StringComparison.Ordinal));
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    private OverlaySongConfig? ResolveSongConfig(OverlayTrackerState state)
    {
        if (state.Song == null)
        {
            return null;
        }

        string[] keys =
        {
            state.Song.OverlayLayoutKey ?? string.Empty,
            state.Song.SongKey ?? string.Empty
        };

        foreach (string key in keys)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            if (_config.Songs.TryGetValue(key, out OverlaySongConfig? config) && config != null)
            {
                return config;
            }
        }

        return null;
    }

    private static RectangleF GetWidgetRectangle(OverlayWidgetConfig config)
    {
        float width = config.Width > 0f ? config.Width : 300f;
        float height = config.Height > 0f ? config.Height : 90f;
        float x = config.X >= 0f ? config.X : 20f;
        float y = config.Y >= 0f ? config.Y : 60f;
        return new RectangleF(x, y, width, height);
    }

    private static bool TryBuildWidget(OverlayTrackerState state, string widgetKey, out string title, out string content)
    {
        title = string.Empty;
        content = string.Empty;

        if (widgetKey.StartsWith("section:", StringComparison.Ordinal))
        {
            string sectionKey = widgetKey.Substring("section:".Length);
            if (!state.SectionStatsByName.TryGetValue(sectionKey, out OverlaySectionStatsState? section) || section == null)
            {
                return false;
            }

            title = section.Name ?? sectionKey;
            content = $"Attempts: {section.Attempts} | FCs Past: {section.RunsPast}\nKilled the Run: {section.KilledTheRun}";
            return true;
        }

        if (!widgetKey.StartsWith("metric:", StringComparison.Ordinal))
        {
            return false;
        }

        string metricKey = widgetKey.Substring("metric:".Length);
        if (!MetricLabels.TryGetValue(metricKey, out string? label))
        {
            return false;
        }

        title = label;
        content = GetMetricValueText(state, metricKey);
        return true;
    }

    private static string GetMetricValueText(OverlayTrackerState state, string metricKey)
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

    private static bool IsWidgetOverlayVisible(OverlayTrackerState state, OverlaySongConfig? songConfig)
    {
        return state.IsInSong &&
            !state.IsPracticeMode &&
            !state.OverlayEditorVisible &&
            state.Song != null &&
            HasEnabledWidgets(songConfig);
    }

    private static bool HasEnabledWidgets(OverlaySongConfig? songConfig)
    {
        return songConfig != null &&
            songConfig.OverlayWidgets.Values.Any(widget => widget != null && widget.Enabled);
    }

    private static bool IsNoteSplitWindowVisible(OverlayTrackerState state)
    {
        return state.IsInSong &&
            !state.IsPracticeMode &&
            !state.OverlayEditorVisible &&
            state.Song != null &&
            state.NoteSplitModeEnabled;
    }

    private void UpdateOverlayRegion(OverlaySongConfig? songConfig)
    {
        using GraphicsPath path = new();
        bool hasContent = false;

        if (songConfig != null)
        {
            foreach (KeyValuePair<string, OverlayWidgetConfig> pair in GetOrderedWidgets(songConfig))
            {
                OverlayWidgetConfig widgetConfig = pair.Value;
                if (widgetConfig == null || !widgetConfig.Enabled)
                {
                    continue;
                }

                RectangleF widgetRect = GetWidgetRectangle(widgetConfig);
                if (widgetRect.Width <= 0f || widgetRect.Height <= 0f)
                {
                    continue;
                }

                path.AddRectangle(Rectangle.Round(widgetRect));
                hasContent = true;
            }
        }

        if (!hasContent)
        {
            Region = null;
            return;
        }

        Region = new Region(path);
    }

    private static void DrawWidget(Graphics graphics, RectangleF rect, string title, string content, OverlayWidgetConfig config, Color textColor, bool showResizeHandle, bool isSectionWidget)
    {
        const float headerHeight = 28f;
        const float padding = 8f;
        using var backgroundBrush = new SolidBrush(GetWidgetBackgroundColor(config));
        using var borderPen = new Pen(GetWidgetBorderColor(), 1f);
        graphics.FillRectangle(backgroundBrush, rect);
        graphics.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1f, rect.Height - 1f);

        GetWidgetFontSizes(rect, isSectionWidget, content, out float titleFontSize, out float contentFontSize);

        using var titleFont = new Font("Segoe UI", titleFontSize, FontStyle.Bold, GraphicsUnit.Pixel);
        using var titleBrush = new SolidBrush(textColor);
        using var titleFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center };

        RectangleF titleRect = new(rect.X + 8f, rect.Y + 1f, Math.Max(0f, rect.Width - 16f), headerHeight - 2f);
        RectangleF contentRect = new(rect.X + padding, rect.Y + headerHeight + padding, Math.Max(0f, rect.Width - (padding * 2f)), Math.Max(0f, rect.Height - headerHeight - (padding * 2f)));
        graphics.DrawString(title, titleFont, titleBrush, titleRect, titleFormat);
        if (isSectionWidget)
        {
            DrawSectionWidgetContent(graphics, contentRect, content, textColor);
        }
        else
        {
            using var contentFont = new Font("Segoe UI", contentFontSize, FontStyle.Regular, GraphicsUnit.Pixel);
            using var contentBrush = new SolidBrush(textColor);
            using var contentFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
            graphics.DrawString(content, contentFont, contentBrush, contentRect, contentFormat);
        }

        if (showResizeHandle && !config.ResizeHandleHidden)
        {
            using var resizeFont = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var resizeBrush = new SolidBrush(textColor);
            RectangleF resizeRect = new(rect.Right - 16f, rect.Bottom - 16f, 12f, 12f);
            graphics.DrawString(".", resizeFont, resizeBrush, resizeRect);
        }
    }

    private static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static IEnumerable<KeyValuePair<string, OverlayWidgetConfig>> GetOrderedWidgets(OverlaySongConfig songConfig)
    {
        return songConfig.OverlayWidgets
            .Where(pair => pair.Value != null && pair.Value.Enabled)
            .OrderBy(pair => GetWidgetZIndex(pair.Value))
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .ToList();
    }

    private static int GetWidgetZIndex(OverlayWidgetConfig config)
    {
        return config.ZIndex;
    }

    private static void GetWidgetFontSizes(RectangleF rect, bool isSectionWidget, string content, out float titleFontSize, out float contentFontSize)
    {
        int lineCount = Math.Max(1, content.Split('\n').Length);
        float baseTitleSize = isSectionWidget ? 17f : 15f;
        float baseContentSize = isSectionWidget ? 17f : (lineCount > 1 ? 16f : 18f);
        float maxTitleSize = Math.Max(13f, Math.Min(24f, (rect.Height - 12f) * 0.34f));
        float availableContentHeight = Math.Max(22f, rect.Height - 44f);
        float maxContentSize = Math.Max(12f, (availableContentHeight / lineCount) - 1f);
        titleFontSize = Clamp((float)Math.Round(Math.Min(baseTitleSize, maxTitleSize)), 12f, 28f);
        contentFontSize = Clamp((float)Math.Round(Math.Min(baseContentSize, maxContentSize)), 11f, 34f);
    }

    private static void DrawSectionWidgetContent(Graphics graphics, RectangleF contentRect, string content, Color textColor)
    {
        string[] lines = content.Split('\n');
        string attemptsLine = lines.Length >= 1 ? lines[0] : string.Empty;
        string emphasisLine = lines.Length >= 2 ? lines[1] : string.Empty;

        using var attemptsFont = new Font("Segoe UI", 14f, FontStyle.Regular, GraphicsUnit.Pixel);
        using var emphasisFont = new Font("Segoe UI", 17f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var contentBrush = new SolidBrush(textColor);
        using var lineFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };

        SizeF attemptsSize = graphics.MeasureString(attemptsLine, attemptsFont, new SizeF(contentRect.Width, contentRect.Height), lineFormat);
        RectangleF attemptsRect = new(contentRect.X, contentRect.Y, contentRect.Width, Math.Max(18f, attemptsSize.Height));
        RectangleF emphasisRect = new(contentRect.X, contentRect.Y + attemptsRect.Height + 2f, contentRect.Width, Math.Max(0f, contentRect.Height - attemptsRect.Height - 2f));
        graphics.DrawString(attemptsLine, attemptsFont, contentBrush, attemptsRect, lineFormat);
        graphics.DrawString(emphasisLine, emphasisFont, contentBrush, emphasisRect, lineFormat);
    }

    private static Color GetWidgetBackgroundColor(OverlayWidgetConfig config)
    {
        if (config.PanelR <= 0.01f &&
            config.PanelG <= 0.01f &&
            config.PanelB <= 0.01f)
        {
            return Color.FromArgb(255, 8, 8, 8);
        }

        int r = (int)Clamp(config.PanelR * 255f, 0f, 255f);
        int g = (int)Clamp(config.PanelG * 255f, 0f, 255f);
        int b = (int)Clamp(config.PanelB * 255f, 0f, 255f);
        return Color.FromArgb(255, r, g, b);
    }

    private static Color GetWidgetBorderColor()
    {
        DesktopOverlayStyle style = _styleStatic ?? new DesktopOverlayStyle();
        int a = (int)Clamp(style.BorderA * 255f, 0f, 255f);
        int r = (int)Clamp(style.BorderR * 255f, 0f, 255f);
        int g = (int)Clamp(style.BorderG * 255f, 0f, 255f);
        int b = (int)Clamp(style.BorderB * 255f, 0f, 255f);
        return Color.FromArgb(a, r, g, b);
    }

    private static Color GetWidgetTextColor(OverlayWidgetConfig config)
    {
        if (config.TextR > 0.01f ||
            config.TextG > 0.01f ||
            config.TextB > 0.01f)
        {
            int explicitR = (int)Clamp(config.TextR * 255f, 0f, 255f);
            int explicitG = (int)Clamp(config.TextG * 255f, 0f, 255f);
            int explicitB = (int)Clamp(config.TextB * 255f, 0f, 255f);
            return Color.FromArgb(255, explicitR, explicitG, explicitB);
        }

        if (config.BackgroundR <= 0.01f &&
            config.BackgroundG <= 0.01f &&
            config.BackgroundB <= 0.01f)
        {
            return Color.White;
        }

        int r = (int)Clamp(config.BackgroundR * 255f, 0f, 255f);
        int g = (int)Clamp(config.BackgroundG * 255f, 0f, 255f);
        int b = (int)Clamp(config.BackgroundB * 255f, 0f, 255f);
        return Color.FromArgb(255, r, g, b);
    }

    private static readonly Dictionary<string, string> MetricLabels = new(StringComparer.Ordinal)
    {
        ["streak"] = "Current Streak",
        ["best_streak"] = "Best FC Streak",
        ["current_missed_notes"] = "Current Missed Notes",
        ["current_overstrums"] = "Current Overstrums",
        ["current_ghosted_notes"] = "Current Ghosted Notes",
        ["lifetime_ghosted_notes"] = "Song Lifetime Ghosts",
        ["global_lifetime_ghosted_notes"] = "Global Lifetime Ghosts",
        ["attempts"] = "Total Attempts",
        ["fc_achieved"] = "FC Achieved"
    };
}

internal static class NoteSplitRenderer
{
    public static void DrawPanel(Graphics graphics, RectangleF rect, OverlayTrackerState state, DesktopOverlayStyle style)
    {
        const float padding = 10f;
        const float headerGap = 4f;
        const float footerGap = 4f;
        const float minimumHeaderHeight = 58f;
        const float minimumFooterHeight = 112f;
        using var backgroundBrush = new SolidBrush(Color.FromArgb(238, 8, 8, 8));
        using var borderPen = new Pen(GetBorderColor(style), 1f);
        graphics.FillRectangle(backgroundBrush, rect);
        graphics.DrawRectangle(borderPen, rect.X, rect.Y, rect.Width - 1f, rect.Height - 1f);

        string title = GetSongTitle(state);
        string artist = state.Song?.Artist ?? string.Empty;
        string attemptsText = state.Attempts.ToString(CultureInfo.InvariantCulture);
        string personalBestValue = FormatPersonalBestSummary(state.SongPersonalBestMissCount, state.SongPersonalBestOverstrums);
        string previousLabel = string.IsNullOrWhiteSpace(state.PreviousSection)
            ? "Previous Section"
            : "Previous Section: " + state.PreviousSection;
        string previousValue = state.PreviousSectionMissCount.HasValue
            ? FormatSectionValue(state.PreviousSectionMissCount.Value)
            : "--";

        using var titleFont = CreateFont(style, Clamp(rect.Width * 0.065f, 12f, 20f), FontStyle.Bold);
        using var subtitleFont = CreateFont(style, 12f, FontStyle.Regular);
        using var attemptsFont = CreateFont(style, 18f, FontStyle.Bold);
        using var smallLabelFont = CreateFont(style, 10f, FontStyle.Regular);
        using var totalFont = CreateFont(style, Clamp(rect.Width * 0.17f, 28f, 52f), FontStyle.Bold);
        using var personalBestFont = CreateFont(style, 11f, FontStyle.Regular);
        using var personalBestValueFont = CreateFont(style, 12f, FontStyle.Bold);
        using var previousFont = CreateFont(style, 11f, FontStyle.Regular);
        using var previousValueFont = CreateFont(style, 12f, FontStyle.Bold);
        using var whiteBrush = new SolidBrush(Color.White);
        using var mutedBrush = new SolidBrush(Color.FromArgb(255, 182, 182, 182));
        using var headerLeftFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near, Trimming = StringTrimming.EllipsisCharacter };
        using var headerRightFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Near };
        using var totalBrush = new SolidBrush(state.CurrentMissedNotes == 0 ? Color.FromArgb(255, 255, 214, 84) : Color.FromArgb(255, 236, 86, 86));
        using var totalFormat = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
        using var footerLabelBrush = new SolidBrush(Color.FromArgb(255, 194, 194, 194));
        using var footerValueBrush = new SolidBrush(Color.White);
        using var previousValueBrush = new SolidBrush(GetCurrentRunColor(state.PreviousSectionResultKind, state.PreviousSectionMissCount));
        using var footerLeftFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
        using var footerRightFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
        {
            float titleHeight = Math.Max(1f, titleFont.GetHeight(graphics));
            float subtitleHeight = Math.Max(1f, subtitleFont.GetHeight(graphics));
            float attemptsHeight = Math.Max(1f, attemptsFont.GetHeight(graphics));
            float attemptsLabelHeight = Math.Max(1f, smallLabelFont.GetHeight(graphics));
            float headerTitleBlockHeight = titleHeight + (!string.IsNullOrWhiteSpace(artist) ? headerGap + subtitleHeight : 0f);
            float attemptsBlockHeight = attemptsHeight + headerGap + attemptsLabelHeight;
            float attemptsColumnWidth = Math.Max(
                60f,
                Math.Max(
                    graphics.MeasureString(attemptsText, attemptsFont).Width,
                    graphics.MeasureString("Attempts", smallLabelFont).Width) + 10f);
            float headerHeight = Math.Max(minimumHeaderHeight, padding + Math.Max(headerTitleBlockHeight, attemptsBlockHeight) + padding);

            float totalHeight = Math.Max(1f, totalFont.GetHeight(graphics));
            float personalBestHeight = Math.Max(personalBestFont.GetHeight(graphics), personalBestValueFont.GetHeight(graphics));
            float previousHeight = Math.Max(previousFont.GetHeight(graphics), previousValueFont.GetHeight(graphics));
            float totalRectHeight = totalHeight + 10f;
            float personalBestRectHeight = personalBestHeight + 2f;
            float previousRectHeight = previousHeight + 2f;
            float footerHeight = Math.Max(
                minimumFooterHeight,
                padding + totalRectHeight + footerGap + personalBestRectHeight + footerGap + previousRectHeight + padding);

            float headerTop = rect.Y + padding;
            float attemptsX = rect.Right - padding - attemptsColumnWidth;
            RectangleF titleRect = new RectangleF(rect.X + padding, headerTop, Math.Max(40f, attemptsX - rect.X - (padding * 2f)), titleHeight + 2f);
            RectangleF artistRect = new RectangleF(rect.X + padding, titleRect.Bottom + headerGap, titleRect.Width, subtitleHeight + 2f);
            RectangleF attemptsRect = new RectangleF(attemptsX, headerTop, attemptsColumnWidth, attemptsHeight + 2f);
            RectangleF attemptsLabelRect = new RectangleF(attemptsX, attemptsRect.Bottom + headerGap, attemptsColumnWidth, attemptsLabelHeight + 2f);

            RectangleF listRect = new RectangleF(rect.X + 1f, rect.Y + headerHeight, rect.Width - 2f, Math.Max(0f, rect.Height - headerHeight - footerHeight));
            float footerTop = rect.Bottom - footerHeight + padding;
            RectangleF totalRect = new RectangleF(rect.X + padding, footerTop, rect.Width - (padding * 2f), totalRectHeight);
            RectangleF personalBestRect = new RectangleF(rect.X + padding, totalRect.Bottom + footerGap, rect.Width - (padding * 2f), personalBestRectHeight);
            RectangleF previousRect = new RectangleF(rect.X + padding, personalBestRect.Bottom + footerGap, rect.Width - (padding * 2f), previousRectHeight);

            graphics.DrawString(title, titleFont, whiteBrush, titleRect, headerLeftFormat);
            if (!string.IsNullOrWhiteSpace(artist))
            {
                graphics.DrawString(artist, subtitleFont, mutedBrush, artistRect, headerLeftFormat);
            }

            graphics.DrawString(attemptsText, attemptsFont, whiteBrush, attemptsRect, headerRightFormat);
            graphics.DrawString("Attempts", smallLabelFont, mutedBrush, attemptsLabelRect, headerRightFormat);

            List<OverlayNoteSplitSectionState> rows = state.NoteSplitSections
                .OrderBy(section => section.Order)
                .ToList();
            if (rows.Count == 0)
            {
                using var emptyFont = CreateFont(style, 14f, FontStyle.Regular);
                using var emptyBrush = new SolidBrush(Color.FromArgb(255, 182, 182, 182));
                using var centered = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };
                graphics.DrawString("No chart sections found.", emptyFont, emptyBrush, listRect, centered);
            }
            else
            {
                float targetRowHeight = Math.Max(11f, listRect.Height / Math.Max(1, rows.Count));
                float nameFontSize = Clamp(targetRowHeight * 0.58f, 9f, 15f);
                float valueFontSize = Clamp(targetRowHeight * 0.62f, 10f, 16f);
                using var sampleNameFont = CreateFont(style, nameFontSize, FontStyle.Regular);
                using var sampleValueFont = CreateFont(style, valueFontSize, FontStyle.Bold);
                float measuredTextHeight = Math.Max(sampleNameFont.GetHeight(graphics), sampleValueFont.GetHeight(graphics));
                float rowPaddingY = Math.Max(1f, measuredTextHeight * 0.18f);
                float rowHeight = Math.Max(targetRowHeight, measuredTextHeight + (rowPaddingY * 2f));
                float previousColumnWidth = 56f;
                float currentColumnWidth = 56f;
                float bestColumnWidth = 56f;
                GraphicsState clipState = graphics.Save();
                graphics.SetClip(listRect);
                for (int i = 0; i < rows.Count; i++)
                {
                    OverlayNoteSplitSectionState row = rows[i];
                    RectangleF rowRect = new RectangleF(listRect.X, listRect.Y + (rowHeight * i), listRect.Width, rowHeight);
                    Color rowBackground = row.IsCurrent
                        ? Color.FromArgb(255, 42, 91, 180)
                        : (i % 2 == 0 ? Color.FromArgb(255, 18, 18, 18) : Color.FromArgb(255, 12, 12, 12));
                    using var rowBrush = new SolidBrush(rowBackground);
                    using var rowDividerPen = new Pen(Color.FromArgb(255, 28, 28, 28), 1f);
                    graphics.FillRectangle(rowBrush, rowRect);
                    graphics.DrawLine(rowDividerPen, rowRect.X, rowRect.Bottom - 1f, rowRect.Right, rowRect.Bottom - 1f);

                    RectangleF bestRect = new RectangleF(rowRect.Right - bestColumnWidth - 8f, rowRect.Y + 1f, bestColumnWidth, rowRect.Height - 2f);
                    RectangleF currentRunRect = new RectangleF(bestRect.X - currentColumnWidth - 4f, rowRect.Y + 1f, currentColumnWidth, rowRect.Height - 2f);
                    RectangleF previousRunRect = new RectangleF(currentRunRect.X - previousColumnWidth - 4f, rowRect.Y + 1f, previousColumnWidth, rowRect.Height - 2f);
                    RectangleF nameRect = new RectangleF(rowRect.X + 10f, rowRect.Y + 1f, Math.Max(40f, previousRunRect.X - rowRect.X - 14f), rowRect.Height - 2f);
                    using var nameFont = CreateFont(style, nameFontSize, row.IsCurrent ? FontStyle.Bold : FontStyle.Regular);
                    using var valueFont = CreateFont(style, valueFontSize, FontStyle.Bold);
                    using var nameBrush = new SolidBrush(Color.White);
                    using var previousBrush = new SolidBrush(GetPreviousRunColor(row.PreviousValidRunMissCount));
                    using var currentBrush = new SolidBrush(GetCurrentRunColor(row.ResultKind, row.CurrentRunMissCount));
                    using var bestBrush = new SolidBrush(GetPersonalBestColor(row));
                    using var nameFormat = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter };
                    using var valueFormat = new StringFormat { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center };
                    nameRect = new RectangleF(nameRect.X, rowRect.Y + rowPaddingY, nameRect.Width, Math.Max(1f, rowRect.Height - (rowPaddingY * 2f)));
                    previousRunRect = new RectangleF(previousRunRect.X, rowRect.Y + rowPaddingY, previousRunRect.Width, Math.Max(1f, rowRect.Height - (rowPaddingY * 2f)));
                    currentRunRect = new RectangleF(currentRunRect.X, rowRect.Y + rowPaddingY, currentRunRect.Width, Math.Max(1f, rowRect.Height - (rowPaddingY * 2f)));
                    bestRect = new RectangleF(bestRect.X, rowRect.Y + rowPaddingY, bestRect.Width, Math.Max(1f, rowRect.Height - (rowPaddingY * 2f)));
                    graphics.DrawString(row.Name ?? string.Empty, nameFont, nameBrush, nameRect, nameFormat);
                    if (row.PreviousValidRunMissCount.HasValue)
                    {
                        graphics.DrawString(FormatSectionValue(row.PreviousValidRunMissCount.Value), valueFont, previousBrush, previousRunRect, valueFormat);
                    }
                    if (row.CurrentRunMissCount.HasValue)
                    {
                        graphics.DrawString(FormatSectionValue(row.CurrentRunMissCount.Value), valueFont, currentBrush, currentRunRect, valueFormat);
                    }

                    string personalBestText = row.PersonalBestMissCount.HasValue
                        ? FormatSectionValue(row.PersonalBestMissCount.Value)
                        : "--";
                    graphics.DrawString(personalBestText, valueFont, bestBrush, bestRect, valueFormat);
                }
                graphics.Restore(clipState);
            }
            graphics.DrawString(state.CurrentMissedNotes.ToString(CultureInfo.InvariantCulture), totalFont, totalBrush, totalRect, totalFormat);
            RectangleF personalBestLabelRect = new RectangleF(personalBestRect.X, personalBestRect.Y, Math.Max(40f, personalBestRect.Width - 120f), personalBestRect.Height);
            RectangleF personalBestValueRect = new RectangleF(personalBestRect.Right - 112f, personalBestRect.Y, 112f, personalBestRect.Height);
            graphics.DrawString("PB:", personalBestFont, footerLabelBrush, personalBestLabelRect, footerLeftFormat);
            graphics.DrawString(personalBestValue, personalBestValueFont, footerValueBrush, personalBestValueRect, footerRightFormat);

            RectangleF previousLabelRect = new RectangleF(previousRect.X, previousRect.Y, Math.Max(40f, previousRect.Width - 72f), previousRect.Height);
            RectangleF previousValueRect = new RectangleF(previousRect.Right - 64f, previousRect.Y, 64f, previousRect.Height);
            graphics.DrawString(previousLabel, previousFont, footerLabelBrush, previousLabelRect, footerLeftFormat);
            graphics.DrawString(previousValue, previousValueFont, previousValueBrush, previousValueRect, footerRightFormat);
        }
    }

    public static float Clamp(float value, float min, float max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static Color GetBorderColor(DesktopOverlayStyle style)
    {
        int a = (int)Clamp(style.BorderA * 255f, 0f, 255f);
        int r = (int)Clamp(style.BorderR * 255f, 0f, 255f);
        int g = (int)Clamp(style.BorderG * 255f, 0f, 255f);
        int b = (int)Clamp(style.BorderB * 255f, 0f, 255f);
        return Color.FromArgb(a, r, g, b);
    }

    private static Font CreateFont(DesktopOverlayStyle style, float baseSize, FontStyle fontStyle)
    {
        float fontScale = style.NoteSplitFontScale > 0f
            ? Clamp(style.NoteSplitFontScale, 0.65f, 2.5f)
            : 1f;
        float size = Math.Max(8f, baseSize * fontScale);
        string family = string.IsNullOrWhiteSpace(style.NoteSplitFontFamily)
            ? DesktopOverlayStyle.DefaultNoteSplitFontFamily
            : style.NoteSplitFontFamily;

        try
        {
            return new Font(family, size, fontStyle, GraphicsUnit.Pixel);
        }
        catch
        {
            return new Font(DesktopOverlayStyle.DefaultNoteSplitFontFamily, size, fontStyle, GraphicsUnit.Pixel);
        }
    }

    private static string GetSongTitle(OverlayTrackerState state)
    {
        OverlaySongDescriptor? song = state.Song;
        string title = song?.Title ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title;
        }

        string songKey = song?.SongKey ?? string.Empty;
        return !string.IsNullOrWhiteSpace(songKey)
            ? songKey
            : "Unknown Song";
    }

    private static string FormatSectionValue(int missCount)
    {
        return missCount <= 0
            ? "0"
            : "-" + missCount.ToString(CultureInfo.InvariantCulture);
    }

    private static string FormatPersonalBestSummary(int? bestMissCount, int? bestRunOverstrums)
    {
        if (!bestMissCount.HasValue)
        {
            return "--";
        }

        string overstrumText = bestRunOverstrums.HasValue
            ? bestRunOverstrums.Value.ToString(CultureInfo.InvariantCulture)
            : "--";
        return FormatSectionValue(bestMissCount.Value) + " " + overstrumText + " OS";
    }

    private static Color GetCurrentRunColor(string? resultKind, int? missCount)
    {
        const int alpha = 255;
        if (!missCount.HasValue)
        {
            return Color.White;
        }

        if (missCount.Value == 0)
        {
            return Color.FromArgb(alpha, 255, 214, 84);
        }

        if (string.Equals(resultKind, OverlayNoteSplitResultKind.PerfectImprovement, StringComparison.Ordinal))
        {
            return Color.FromArgb(alpha, 255, 214, 84);
        }

        if (string.Equals(resultKind, OverlayNoteSplitResultKind.Improved, StringComparison.Ordinal))
        {
            return Color.FromArgb(alpha, 66, 221, 111);
        }

        if (string.Equals(resultKind, OverlayNoteSplitResultKind.FirstScan, StringComparison.Ordinal))
        {
            return missCount.Value == 0
                ? Color.FromArgb(alpha, 66, 221, 111)
                : Color.FromArgb(alpha, 236, 86, 86);
        }

        if (string.Equals(resultKind, OverlayNoteSplitResultKind.Worse, StringComparison.Ordinal))
        {
            return Color.FromArgb(alpha, 236, 86, 86);
        }

        return Color.White;
    }

    private static Color GetPreviousRunColor(int? missCount)
    {
        if (!missCount.HasValue)
        {
            return Color.FromArgb(255, 165, 165, 165);
        }

        return missCount.Value == 0
            ? Color.FromArgb(255, 255, 214, 84)
            : Color.FromArgb(255, 236, 86, 86);
    }

    private static Color GetPersonalBestColor(OverlayNoteSplitSectionState row)
    {
        if (!row.PersonalBestMissCount.HasValue)
        {
            return Color.FromArgb(255, 165, 165, 165);
        }

        return row.PersonalBestMissCount.Value == 0
            ? Color.White
            : Color.FromArgb(255, 236, 86, 86);
    }
}

internal sealed class NoteSplitWindowForm : Form
{
    private const int MinimumNoteSplitWidth = 240;
    private const int MinimumNoteSplitHeight = 320;
    private const int MaximumNoteSplitWidth = 1200;
    private const int MaximumNoteSplitHeight = 1600;
    private const int ResizeGripSize = 18;
    private readonly Action<DesktopOverlayStyle> _saveStyle;
    private readonly Action<string> _log;
    private readonly Action<bool> _setDialogActive;
    private readonly ContextMenuStrip _contextMenu;
    private OverlayTrackerState _state = new();
    private DesktopOverlayStyle _style = new();
    private Rectangle _gameClientBounds = Rectangle.Empty;
    private bool _dragging;
    private bool _resizing;
    private bool _settingsDialogOpen;
    private Point _dragCursorOrigin;
    private Point _dragWindowOrigin;
    private Point _resizeCursorOrigin;
    private Size _resizeWindowOrigin;

    public NoteSplitWindowForm(DesktopOverlayStyle initialStyle, Action<DesktopOverlayStyle> saveStyle, Action<string> log, Action<bool> setDialogActive)
    {
        _style = CloneStyle(initialStyle);
        _saveStyle = saveStyle;
        _log = log;
        _setDialogActive = setDialogActive;

        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.FromArgb(8, 8, 8);
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = true;
        ShowIcon = false;
        StartPosition = FormStartPosition.Manual;
        TopMost = _style.NoteSplitTopMost;
        DoubleBuffered = true;
        Text = "Clone Hero NoteSplit";
        MinimumSize = new Size(MinimumNoteSplitWidth, MinimumNoteSplitHeight);
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

        _contextMenu = BuildContextMenu();
        ContextMenuStrip = _contextMenu;
        Size = new Size(
            (int)Math.Round(NoteSplitRenderer.Clamp(_style.NoteSplitWidth, MinimumNoteSplitWidth, MaximumNoteSplitWidth)),
            (int)Math.Round(NoteSplitRenderer.Clamp(_style.NoteSplitHeight, MinimumNoteSplitHeight, MaximumNoteSplitHeight)));
        Location = new Point(-2000, -2000);

        MouseDown += HandleMouseDown;
        MouseMove += HandleMouseMove;
        MouseUp += HandleMouseUp;
    }

    protected override bool ShowWithoutActivation => false;

    public void Synchronize(OverlayTrackerState state, DesktopOverlayStyle style, bool visible, Rectangle gameClientBounds, bool invalidate)
    {
        _state = state ?? new OverlayTrackerState();
        _style = CloneStyle(style);
        _gameClientBounds = gameClientBounds;
        TopMost = !_settingsDialogOpen && _style.NoteSplitTopMost;
        ApplyConfiguredBounds();

        if (!visible)
        {
            if (Visible)
            {
                Hide();
            }

            return;
        }

        bool becameVisible = false;
        if (!Visible)
        {
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOWNOACTIVATE);
            becameVisible = true;
        }

        if (invalidate || becameVisible)
        {
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.None;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.Half;
        e.Graphics.CompositingQuality = CompositingQuality.HighSpeed;
        e.Graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        NoteSplitRenderer.DrawPanel(e.Graphics, ClientRectangle, _state, _style);
        DrawResizeGrip(e.Graphics);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
    }

    private ContextMenuStrip BuildContextMenu()
    {
        var menu = new ContextMenuStrip();
        var settingsItem = new ToolStripMenuItem("Settings...");
        settingsItem.Click += (_, _) => OpenSettingsDialog();
        var resetPositionItem = new ToolStripMenuItem("Reset Position");
        resetPositionItem.Click += (_, _) => ResetPosition();
        var topMostItem = new ToolStripMenuItem("Always On Top") { CheckOnClick = true };
        topMostItem.Click += (_, _) =>
        {
            _style.NoteSplitTopMost = topMostItem.Checked;
            TopMost = _style.NoteSplitTopMost;
            SaveStyleFromCurrentBounds();
        };
        menu.Opening += (_, _) => topMostItem.Checked = _style.NoteSplitTopMost;
        menu.Items.Add(settingsItem);
        menu.Items.Add(resetPositionItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(topMostItem);
        return menu;
    }

    private void OpenSettingsDialog()
    {
        ActivateForInteraction();
        using var dialog = new NoteSplitSettingsForm(_style);
        _settingsDialogOpen = true;
        _setDialogActive(true);
        TopMost = false;
        try
        {
            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            _style = CloneStyle(dialog.ResultStyle);
            if (dialog.ResetPositionRequested)
            {
                _style.NoteSplitX = -1f;
                _style.NoteSplitY = -1f;
            }

            ApplyConfiguredBounds();
            SaveStyleFromCurrentBounds();
            Invalidate();
        }
        finally
        {
            _settingsDialogOpen = false;
            TopMost = _style.NoteSplitTopMost;
            _setDialogActive(false);
        }
    }

    private void ResetPosition()
    {
        _style.NoteSplitX = -1f;
        _style.NoteSplitY = -1f;
        ApplyConfiguredBounds();
        SaveStyleFromCurrentBounds();
        Invalidate();
    }

    private void HandleMouseDown(object? sender, MouseEventArgs e)
    {
        ActivateForInteraction();
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        if (GetResizeGripRectangle().Contains(e.Location))
        {
            _resizing = true;
            _resizeCursorOrigin = Cursor.Position;
            _resizeWindowOrigin = Size;
            Cursor = Cursors.SizeNWSE;
            return;
        }

        _dragging = true;
        _dragCursorOrigin = Cursor.Position;
        _dragWindowOrigin = Location;
    }

    private void HandleMouseMove(object? sender, MouseEventArgs e)
    {
        if (_resizing)
        {
            Point resizeCursor = Cursor.Position;
            Rectangle workingArea = GetWorkingArea(new Rectangle(Location, _resizeWindowOrigin));
            int maxWidth = Math.Min(MaximumNoteSplitWidth, Math.Max(MinimumNoteSplitWidth, workingArea.Right - Left));
            int maxHeight = Math.Min(MaximumNoteSplitHeight, Math.Max(MinimumNoteSplitHeight, workingArea.Bottom - Top));
            int width = ClampInt(_resizeWindowOrigin.Width + (resizeCursor.X - _resizeCursorOrigin.X), MinimumNoteSplitWidth, maxWidth);
            int height = ClampInt(_resizeWindowOrigin.Height + (resizeCursor.Y - _resizeCursorOrigin.Y), MinimumNoteSplitHeight, maxHeight);
            Size = new Size(width, height);
            Invalidate();
            return;
        }

        if (!_dragging)
        {
            Cursor = GetResizeGripRectangle().Contains(e.Location)
                ? Cursors.SizeNWSE
                : Cursors.Default;
            return;
        }

        Point cursor = Cursor.Position;
        int offsetX = cursor.X - _dragCursorOrigin.X;
        int offsetY = cursor.Y - _dragCursorOrigin.Y;
        Location = new Point(_dragWindowOrigin.X + offsetX, _dragWindowOrigin.Y + offsetY);
    }

    private void HandleMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        bool changedBounds = _dragging || _resizing;
        _dragging = false;
        _resizing = false;
        Cursor = GetResizeGripRectangle().Contains(PointToClient(Cursor.Position))
            ? Cursors.SizeNWSE
            : Cursors.Default;
        if (!changedBounds)
        {
            return;
        }

        SaveStyleFromCurrentBounds();
    }

    private void ApplyConfiguredBounds()
    {
        if (_dragging || _resizing)
        {
            return;
        }

        Rectangle targetBounds = GetConfiguredBounds();
        if (Bounds != targetBounds)
        {
            Bounds = targetBounds;
        }
    }

    private Rectangle GetConfiguredBounds()
    {
        int width = (int)Math.Round(NoteSplitRenderer.Clamp(_style.NoteSplitWidth, MinimumNoteSplitWidth, MaximumNoteSplitWidth));
        int height = (int)Math.Round(NoteSplitRenderer.Clamp(_style.NoteSplitHeight, MinimumNoteSplitHeight, MaximumNoteSplitHeight));
        Point location = _style.NoteSplitX >= 0f && _style.NoteSplitY >= 0f
            ? new Point((int)Math.Round(_style.NoteSplitX), (int)Math.Round(_style.NoteSplitY))
            : GetDefaultLocation(width, height);

        Rectangle workingArea = GetWorkingArea(new Rectangle(location, new Size(width, height)));
        int clampedX = ClampInt(location.X, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        int clampedY = ClampInt(location.Y, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));
        return new Rectangle(clampedX, clampedY, width, height);
    }

    private Point GetDefaultLocation(int width, int height)
    {
        Rectangle fallback = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(80, 80, 1280, 720);
        if (_gameClientBounds.Width <= 0 || _gameClientBounds.Height <= 0)
        {
            return new Point(fallback.Left + 40, fallback.Top + 40);
        }

        Rectangle workingArea = Screen.FromRectangle(_gameClientBounds).WorkingArea;
        int preferredY = ClampInt(_gameClientBounds.Top + 16, workingArea.Top, Math.Max(workingArea.Top, workingArea.Bottom - height));
        int rightX = _gameClientBounds.Right + 16;
        if (rightX + width <= workingArea.Right)
        {
            return new Point(rightX, preferredY);
        }

        int leftX = _gameClientBounds.Left - width - 16;
        if (leftX >= workingArea.Left)
        {
            return new Point(leftX, preferredY);
        }

        int clampedX = ClampInt(_gameClientBounds.Left + 16, workingArea.Left, Math.Max(workingArea.Left, workingArea.Right - width));
        return new Point(clampedX, preferredY);
    }

    private Rectangle GetWorkingArea(Rectangle bounds)
    {
        if (bounds.Width > 0 && bounds.Height > 0)
        {
            return Screen.FromRectangle(bounds).WorkingArea;
        }

        if (_gameClientBounds.Width > 0 && _gameClientBounds.Height > 0)
        {
            return Screen.FromRectangle(_gameClientBounds).WorkingArea;
        }

        return Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1920, 1080);
    }

    private void SaveStyleFromCurrentBounds()
    {
        _style.NoteSplitX = Left;
        _style.NoteSplitY = Top;
        _style.NoteSplitWidth = Width;
        _style.NoteSplitHeight = Height;
        _saveStyle(CloneStyle(_style));
        _log("NoteSplit window updated | x=" + Left + " | y=" + Top + " | w=" + Width + " | h=" + Height);
    }

    private void ActivateForInteraction()
    {
        try
        {
            NativeMethods.ShowWindow(Handle, NativeMethods.SW_SHOW);
            NativeMethods.SetForegroundWindow(Handle);
            Activate();
        }
        catch
        {
        }
    }

    private Rectangle GetResizeGripRectangle()
    {
        return new Rectangle(
            Math.Max(0, ClientSize.Width - ResizeGripSize),
            Math.Max(0, ClientSize.Height - ResizeGripSize),
            ResizeGripSize,
            ResizeGripSize);
    }

    private void DrawResizeGrip(Graphics graphics)
    {
        Rectangle grip = GetResizeGripRectangle();
        using var gripPen = new Pen(Color.FromArgb(190, 186, 186, 186), 1f);
        int right = grip.Right - 4;
        int bottom = grip.Bottom - 4;
        graphics.DrawLine(gripPen, right - 10, bottom, right, bottom - 10);
        graphics.DrawLine(gripPen, right - 7, bottom, right, bottom - 7);
        graphics.DrawLine(gripPen, right - 4, bottom, right, bottom - 4);
    }

    private static int ClampInt(int value, int min, int max)
    {
        if (value < min)
        {
            return min;
        }

        return value > max ? max : value;
    }

    private static DesktopOverlayStyle CloneStyle(DesktopOverlayStyle style)
    {
        return new DesktopOverlayStyle
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
}

internal sealed class NoteSplitSettingsForm : Form
{
    private readonly CheckBox _topMostInput;
    private readonly Label _fontPreviewLabel;
    private string _fontFamily;
    private float _fontScale;

    public DesktopOverlayStyle ResultStyle { get; private set; }
    public bool ResetPositionRequested { get; private set; }

    public NoteSplitSettingsForm(DesktopOverlayStyle currentStyle)
    {
        ResultStyle = CloneStyle(currentStyle);
        _fontFamily = string.IsNullOrWhiteSpace(currentStyle.NoteSplitFontFamily)
            ? DesktopOverlayStyle.DefaultNoteSplitFontFamily
            : currentStyle.NoteSplitFontFamily;
        _fontScale = currentStyle.NoteSplitFontScale > 0f
            ? currentStyle.NoteSplitFontScale
            : 1f;

        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        Text = "NoteSplit Settings";
        ClientSize = new Size(420, 188);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 4,
            Padding = new Padding(12)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _topMostInput = new CheckBox
        {
            Text = "Keep NoteSplit above other windows",
            Checked = currentStyle.NoteSplitTopMost,
            Dock = DockStyle.Fill
        };
        _fontPreviewLabel = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        UpdateFontPreview();

        var fontPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        var chooseFontButton = new Button { Text = "Choose Font...", AutoSize = true };
        chooseFontButton.Click += (_, _) => ChooseFont();
        fontPanel.Controls.Add(_fontPreviewLabel);
        fontPanel.Controls.Add(chooseFontButton);

        var resizeHintLabel = new Label
        {
            Text = "Resize NoteSplit by dragging the lower-right corner of the window.",
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        };
        layout.Controls.Add(resizeHintLabel, 0, 0);
        layout.SetColumnSpan(resizeHintLabel, 2);
        layout.Controls.Add(new Label { Text = "Font", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 1);
        layout.Controls.Add(fontPanel, 1, 1);
        layout.Controls.Add(new Label { Text = string.Empty, Dock = DockStyle.Fill }, 0, 2);
        layout.Controls.Add(_topMostInput, 1, 2);

        var buttonPanel = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        var saveButton = new Button { Text = "Save", AutoSize = true };
        saveButton.Click += (_, _) => SaveAndClose();
        var cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        var resetDefaultsButton = new Button { Text = "Reset Defaults", AutoSize = true };
        resetDefaultsButton.Click += (_, _) => ResetDefaults();
        var resetPositionButton = new Button { Text = "Reset Position", AutoSize = true };
        resetPositionButton.Click += (_, _) => ResetPositionRequested = true;
        buttonPanel.Controls.Add(saveButton);
        buttonPanel.Controls.Add(cancelButton);
        buttonPanel.Controls.Add(resetDefaultsButton);
        buttonPanel.Controls.Add(resetPositionButton);
        layout.Controls.Add(buttonPanel, 0, 3);
        layout.SetColumnSpan(buttonPanel, 2);

        Controls.Add(layout);
        AcceptButton = saveButton;
        CancelButton = cancelButton;
    }

    private void ChooseFont()
    {
        using var dialog = new FontDialog
        {
            Font = new Font(_fontFamily, Math.Max(8f, 12f * _fontScale), FontStyle.Regular, GraphicsUnit.Point),
            ShowColor = false,
            ShowEffects = false
        };

        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _fontFamily = dialog.Font.FontFamily.Name;
        _fontScale = NoteSplitRenderer.Clamp(dialog.Font.SizeInPoints / 12f, 0.65f, 2.5f);
        UpdateFontPreview();
    }

    private void UpdateFontPreview()
    {
        _fontPreviewLabel.Text = _fontFamily + " (" + _fontScale.ToString("0.##", CultureInfo.InvariantCulture) + "x)";
    }

    private void ResetDefaults()
    {
        DesktopOverlayStyle defaults = new();
        ResultStyle.NoteSplitWidth = defaults.NoteSplitWidth;
        ResultStyle.NoteSplitHeight = defaults.NoteSplitHeight;
        _fontFamily = defaults.NoteSplitFontFamily;
        _fontScale = defaults.NoteSplitFontScale;
        _topMostInput.Checked = defaults.NoteSplitTopMost;
        ResetPositionRequested = true;
        UpdateFontPreview();
    }

    private void SaveAndClose()
    {
        ResultStyle = CloneStyle(ResultStyle);
        ResultStyle.NoteSplitFontFamily = _fontFamily;
        ResultStyle.NoteSplitFontScale = _fontScale;
        ResultStyle.NoteSplitTopMost = _topMostInput.Checked;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static DesktopOverlayStyle CloneStyle(DesktopOverlayStyle style)
    {
        return new DesktopOverlayStyle
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
}

internal sealed class OverlayTrackerState
{
    public bool IsInSong { get; set; }
    public bool OverlayEditorVisible { get; set; }
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
    public bool FcAchieved { get; set; }
    public bool NoteSplitModeEnabled { get; set; }
    public string? PreviousSection { get; set; }
    public int? PreviousSectionMissCount { get; set; }
    public int? SongPersonalBestMissCount { get; set; }
    public int? SongPersonalBestOverstrums { get; set; }
    public string? PreviousSectionResultKind { get; set; }
    public List<OverlayNoteSplitSectionState> NoteSplitSections { get; set; } = new();
    public OverlaySongDescriptor? Song { get; set; }
    public Dictionary<string, OverlaySectionStatsState> SectionStatsByName { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class OverlaySongDescriptor
{
    public string? SongKey { get; set; }
    public string? OverlayLayoutKey { get; set; }
    public string? Title { get; set; }
    public string? Artist { get; set; }
}

internal sealed class OverlaySectionStatsState
{
    public string? Name { get; set; }
    public int RunsPast { get; set; }
    public int Attempts { get; set; }
    public int KilledTheRun { get; set; }
}

internal sealed class OverlayNoteSplitSectionState
{
    public int Order { get; set; }
    public string? Key { get; set; }
    public string? Name { get; set; }
    public bool IsCurrent { get; set; }
    public int? PreviousValidRunMissCount { get; set; }
    public int? PersonalBestMissCount { get; set; }
    public int? CurrentRunMissCount { get; set; }
    public string? ResultKind { get; set; }
}

internal static class OverlayNoteSplitResultKind
{
    public const string None = "none";
    public const string FirstScan = "first_scan";
    public const string Improved = "improved";
    public const string PerfectImprovement = "perfect_improvement";
    public const string Tie = "tie";
    public const string Worse = "worse";
}

internal sealed class OverlayTrackerConfig
{
    public Dictionary<string, OverlaySongConfig> Songs { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class OverlaySongConfig
{
    public Dictionary<string, OverlayWidgetConfig> OverlayWidgets { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class DesktopOverlayStyle
{
    public const string DefaultNoteSplitFontFamily = "Segoe UI";
    public float BorderR { get; set; } = 0.235f;
    public float BorderG { get; set; } = 0.235f;
    public float BorderB { get; set; } = 0.235f;
    public float BorderA { get; set; } = 0.70f;
    public float NoteSplitX { get; set; } = -1f;
    public float NoteSplitY { get; set; } = -1f;
    public float NoteSplitWidth { get; set; } = 420f;
    public float NoteSplitHeight { get; set; } = 720f;
    public string NoteSplitFontFamily { get; set; } = DefaultNoteSplitFontFamily;
    public float NoteSplitFontScale { get; set; } = 1f;
    public bool NoteSplitTopMost { get; set; } = true;
}

internal sealed class OverlayWidgetConfig
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
    public float TextR { get; set; }
    public float TextG { get; set; }
    public float TextB { get; set; }
    public float PanelR { get; set; }
    public float PanelG { get; set; }
    public float PanelB { get; set; }
    public float PanelA { get; set; } = 0.82f;
}

internal static class NativeMethods
{
    public const int SW_SHOW = 5;
    public const int SW_SHOWNOACTIVATE = 4;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }
}
