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

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int gameProcessId = 0;
        string dataDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CloneHeroSectionTracker");
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

        _timer = new System.Windows.Forms.Timer { Interval = 100 };
        _timer.Tick += (_, _) => OnTimerTick();
        _timer.Start();

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

    private void OnTimerTick()
    {
        try
        {
            if (!TryRefreshGameProcess())
            {
                LogVisibility("game_process_missing");
                Close();
                return;
            }

            bool stateChanged = RefreshState();
            bool configChanged = RefreshConfig();
            bool styleChanged = RefreshStyle();

            if (!TryGetVisibleClientBounds(out Rectangle clientBounds, out string visibilityReason))
            {
                LogVisibility(visibilityReason);
                Hide();
                return;
            }

            if (_state == null ||
                !_state.IsInSong ||
                _state.OverlayEditorVisible ||
                _state.Song == null ||
                ResolveSongConfig(_state) == null)
            {
                Region = null;
                LogVisibility(GetStateVisibilityReason());
                Hide();
                return;
            }

            OverlaySongConfig songConfig = ResolveSongConfig(_state)!;
            if (configChanged || Region == null || !Visible)
            {
                UpdateOverlayRegion(songConfig, false);
            }

            bool boundsChanged = Bounds != clientBounds;
            if (Bounds != clientBounds)
            {
                Bounds = clientBounds;
                Log("Desktop overlay bounds | x=" + clientBounds.X + " | y=" + clientBounds.Y + " | w=" + clientBounds.Width + " | h=" + clientBounds.Height);
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

        IntPtr targetWindow = GetGameWindowHandle();
        if (targetWindow == IntPtr.Zero)
        {
            targetWindow = foreground;
        }

        if (NativeMethods.IsIconic(targetWindow))
        {
            reason = "game_minimized";
            return false;
        }

        if (!NativeMethods.GetClientRect(targetWindow, out NativeMethods.RECT clientRect))
        {
            reason = "client_rect_failed";
            return false;
        }

        NativeMethods.POINT topLeft = new() { X = clientRect.Left, Y = clientRect.Top };
        NativeMethods.POINT bottomRight = new() { X = clientRect.Right, Y = clientRect.Bottom };
        if (!NativeMethods.ClientToScreen(targetWindow, ref topLeft) ||
            !NativeMethods.ClientToScreen(targetWindow, ref bottomRight))
        {
            reason = "client_to_screen_failed";
            return false;
        }

        bounds = Rectangle.FromLTRB(topLeft.X, topLeft.Y, bottomRight.X, bottomRight.Y);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            reason = "empty_bounds";
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

        if (_state.Song == null)
        {
            return "song_missing";
        }

        return ResolveSongConfig(_state) == null ? "song_config_missing" : "visible";
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
            state.Song.SongKey ?? string.Empty,
            state.Song.OverlayLegacyKey ?? string.Empty,
            state.Song.LegacySongKey ?? string.Empty
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
            "score" => state.Score.ToString(CultureInfo.InvariantCulture),
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

    private void UpdateOverlayRegion(OverlaySongConfig songConfig, bool includeEditor)
    {
        using GraphicsPath path = new();
        bool hasWidget = false;
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
            hasWidget = true;
        }

        if (!hasWidget)
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
        ["score"] = "Score",
        ["streak"] = "Current Streak",
        ["best_streak"] = "Best Streak",
        ["current_missed_notes"] = "Current Missed Notes",
        ["current_overstrums"] = "Current Overstrums",
        ["current_ghosted_notes"] = "Current Ghosted Notes",
        ["lifetime_ghosted_notes"] = "Song Lifetime Ghosts",
        ["global_lifetime_ghosted_notes"] = "Global Lifetime Ghosts",
        ["starts"] = "Starts",
        ["restarts"] = "Restarts",
        ["attempts"] = "Total Attempts",
        ["fc_achieved"] = "FC Achieved"
    };
}

internal sealed class OverlayTrackerState
{
    public bool IsInSong { get; set; }
    public bool OverlayEditorVisible { get; set; }
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
    public OverlaySongDescriptor? Song { get; set; }
    public Dictionary<string, OverlaySectionStatsState> SectionStatsByName { get; set; } = new(StringComparer.Ordinal);
}

internal sealed class OverlaySongDescriptor
{
    public string? SongKey { get; set; }
    public string? LegacySongKey { get; set; }
    public string? OverlayLayoutKey { get; set; }
    public string? OverlayLegacyKey { get; set; }
}

internal sealed class OverlaySectionStatsState
{
    public string? Name { get; set; }
    public int RunsPast { get; set; }
    public int Attempts { get; set; }
    public int KilledTheRun { get; set; }
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
    public float BorderR { get; set; } = 0.235f;
    public float BorderG { get; set; } = 0.235f;
    public float BorderB { get; set; } = 0.235f;
    public float BorderA { get; set; } = 0.70f;
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
    public const int SW_SHOWNOACTIVATE = 4;
    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

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
