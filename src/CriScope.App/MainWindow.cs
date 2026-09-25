using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CriScope.Core;

namespace CriScope.App;

public sealed class MainWindow : Window
{
    private readonly Collector _collector;
    private Palette _p = new(false);
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Session, ViewState> _views = [];
    private readonly Dictionary<Session, long> _sessionOrder = [];
    private Session? _session;
    private TimelineControl _timeline = null!;
    private StackPanel _sessions = null!;
    private StackPanel _details = null!;
    private ListBox _events = null!;
    private TextBlock _identity = null!, _states = null!, _status = null!, _range = null!;
    private TextBox _filter = null!;
    private Button _record = null!, _live = null!;
    private Grid _body = null!;
    private Border _inspector = null!;
    private string _sessionSignature = "!";
    private string _mode = "Timeline";
    private WireEvent[] _snapshot = [];
    private WireEvent? _selected;
    private string? _error;
    private long _lastTotal = -1;
    private bool _rebuilding;
    private bool _showInspector;
    private bool _playing;
    private bool _autoRecorded;
    private bool _diagnostics;
    private TextBlock _serverStatus = null!, _savedNotice = null!;
    private StackPanel _savedPanel = null!;
    private string? _lastSavedPath;
    private string[] _savedPaths = [];
    private readonly HashSet<(string Client, string Capture)> _recordingGroups = [];
    public Func<Session, Task<string>>? ExportProblemAsync { get; set; }
    private Grid _main = null!;

    private sealed class ViewState
    {
        public string Filter = "";
        public double End = 30, Span = 30;
        public bool Live = true;
        public WireEvent? Selected;
        public double? SelectionStart, SelectionEnd;
    }

    public MainWindow(Collector collector)
    {
        _collector = collector;
        Title = "CriScope · Audio observatory";
        ExtendClientAreaToDecorationsHint = true;
        WindowDecorations = WindowDecorations.BorderOnly;
        ExtendClientAreaTitleBarHeightHint = 36;
        CanResize = true;
        ShowInTaskbar = true;
        Icon = new WindowIcon(Avalonia.Platform.AssetLoader.Open(new Uri("avares://CriScope/Assets/criscope.ico")));
        Width = 1480; Height = 900; MinWidth = 980; MinHeight = 650;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var launch = Environment.GetCommandLineArgs();
        _p = new Palette(launch.Contains("--light"));
        var modeIndex = Array.IndexOf(launch, "--view");
        if (modeIndex >= 0 && modeIndex + 1 < launch.Length && new[] { "Timeline", "AISAC", "Performance", "Location", "Mixing" }.Contains(launch[modeIndex + 1])) _mode = launch[modeIndex + 1];
        Build();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Responsive();
        Opened += async (_, _) =>
        {
            var args = Environment.GetCommandLineArgs();
            var index = Array.IndexOf(args, "--open");
            if (index >= 0 && index + 1 < args.Length) Load(args[index + 1]);
            var rangeIndex = Array.IndexOf(args, "--range");
            if (rangeIndex >= 0 && rangeIndex + 1 < args.Length)
            {
                var parts = args[rangeIndex + 1].Split(':');
                if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start)
                    && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end) && double.IsFinite(start) && double.IsFinite(end) && end > start)
                { _timeline.End = end; _timeline.Span = Math.Clamp(end-start,.25,86400); _timeline.Live = false; }
            }
            Refresh();
            var screenshot = Array.IndexOf(args, "--screenshot");
            if (screenshot >= 0 && screenshot + 1 < args.Length)
            {
                await Task.Delay(1200);
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height), new Vector(96, 96));
                bitmap.Render(this);
                bitmap.Save(args[screenshot + 1], PngBitmapEncoderOptions.Default);
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F) { _filter.Focus(); e.Handled = true; }
            else if (e.Key == Key.Home && e.Source is not TextBox) { Fit(); e.Handled = true; }
            else if (e.Key == Key.L && e.Source is not TextBox) { _timeline.Live = true; Refresh(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _filter.Text = ""; _timeline.SetSelection(null, null); UpdateEvents(); e.Handled = true; }
        };
    }

    private TextBlock Label(string text, double size = 12, IBrush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? _p.Text, VerticalAlignment = VerticalAlignment.Center };
    private Button Action(string text, Action action, bool accent = false)
    {
        var b = new Button { Content = ButtonContent(text, accent), FontSize = 12, Padding = new Thickness(12, 7),
            Background = accent ? _p.Selection : Brushes.Transparent, Foreground = accent ? _p.Canvas : _p.Text,
            BorderBrush = accent ? _p.Selection : _p.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) };
        b.PointerEntered += (_, _) => { if (!accent) b.Background = _p.Hover; };
        b.PointerExited += (_, _) => { if (!accent) b.Background = Brushes.Transparent; };
        ToolTip.SetTip(b, text);
        b.Click += (_, _) => action(); return b;
    }
    private Control ButtonContent(string text, bool accent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var path = text.Contains("导出") ? "M12,16 V3 M7,8 L12,3 L17,8 M4,14 V21 H20 V14" : text.Contains("开始记录") ? "M12,4 A8,8 0 1 0 12,20 A8,8 0 1 0 12,4" : (text.Contains("记录") || text.Contains("保存")) ? "M8,8 H16 V16 H8 Z" : text.Contains("打开") ? "M3,7 H10 L12,9 H21 L18,19 H3 Z M3,7 V5 H10 L12,7" :
            (text.Contains("播放") || text.Contains("声音")) ? "M7,4 L20,12 L7,20 Z" : text.Contains("控制") ? "M5,3 V21 M12,3 V21 M19,3 V21 M2,8 H8 M9,16 H15 M16,6 H22" :
            text.Contains("混音") ? "M4,9 V18 M9,4 V20 M14,7 V17 M19,2 V22" : text.Contains("空间") ? "M12,3 L21,8 V17 L12,22 L3,17 V8 Z M3,8 L12,13 L21,8 M12,13 V22" :
            text.Contains("资源") ? "M4,4 H20 V9 H4 Z M4,14 H20 V19 H4 Z" : text.Contains("连接") ? "M8,5 L4,9 V15 L8,19 M16,5 L20,9 V15 L16,19 M8,12 H16" :
            text.Contains("诊断") ? "M4,3 H20 V21 H4 Z M8,8 H16 M8,12 H16 M8,16 H13" : text.Contains("断开") ? "M5,5 L19,19 M19,5 L5,19" :
            text.Contains("详情") ? "M3,4 H21 V20 H3 Z M15,4 V20" : text.Contains("全览") ? "M3,9 V3 H9 M15,3 H21 V9 M21,15 V21 H15 M9,21 H3 V15" :
            text.Contains("实时") || text.Contains("Live") ? "M4,12 H8 L11,4 L15,20 L18,12 H22" : "M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,3 V21";
        panel.Children.Add(new Avalonia.Controls.Shapes.Path { Data = Geometry.Parse(path), Stroke = accent ? _p.Canvas : _p.Text, StrokeThickness = 1.5, Width = 15, Height = 15, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center });
        panel.Children.Add(Label(text,12,accent ? _p.Canvas : _p.Text)); return panel;
    }
    private Border Surface(Control child, IBrush background, Thickness? padding = null) => new()
    { Child = child, Background = background, Padding = padding ?? new Thickness(0), BorderBrush = _p.Border, BorderThickness = new Thickness(0, 0, 0, 1) };

    private void Build()
    {
        _rebuilding = true;
        RequestedThemeVariant = _p.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        Background = _p.Canvas;
        var root = new Grid { RowDefinitions = new RowDefinitions("36,58,*,Auto") };
        root.Children.Add(new WindowTitleBar(this, _p));
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("208,*,Auto"), Margin = new Thickness(16, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Label("采集与日志", 17));
        top.Children.Add(brand);
        var identity = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        _identity = Label("音频观测台", 13);
        _states = Label("未连接  ·  录制空闲  ·  Live 跟随", 11, _p.Muted);
        identity.Children.Add(_identity); identity.Children.Add(_states); Grid.SetColumn(identity, 1); top.Children.Add(identity);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _record = Action("开始记录日志", Record);
        actions.Children.Add(_record);
        actions.Children.Add(Action("打开日志", async () =>
        {
            try
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(_collector.RecordingsDirectory);
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开 CriScope 日志", AllowMultiple = false, SuggestedStartLocation = folder,
                    FileTypeFilter = new[] { new FilePickerFileType("CriScope 日志") { Patterns = new[] { "*.criscope", "*.jsonl" } }, FilePickerFileTypes.All } });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Load(path);
            }
            catch (Exception ex) { _error = ex.Message; }
        }));
        actions.Children.Add(Action("导出问题包", async () => await ExportProblem()));
        actions.Children.Add(Action(_p.Light ? "暗色" : "浅色", () => { SaveView(); _p = new Palette(!_p.Light); Build(); RestoreView(); Refresh(); }));
        Grid.SetColumn(actions, 2); top.Children.Add(actions); var topSurface = Surface(top, _p.Shell); Grid.SetRow(topSurface, 1); root.Children.Add(topSurface);

        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("208,*,282") };
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var sideHeading = new StackPanel { Margin = new Thickness(18, 23, 12, 18), Spacing = 6 };
        sideHeading.Children.Add(Label("会话 / SESSIONS", 11, _p.Muted));
        sideHeading.Children.Add(Label("客户端与录制", 17));
        _serverStatus = new TextBlock { Text = "等待游戏接入 · 端口 18961", TextWrapping = TextWrapping.Wrap, Foreground = _p.Good, FontSize = 11, Margin = new Thickness(0,8,0,0) };
        sideHeading.Children.Add(_serverStatus);
        sideHeading.Children.Add(new TextBlock { Text = "同机多实例可能受 CRI 原生端口限制", FontSize = 10, Foreground = _p.Muted, TextWrapping = TextWrapping.Wrap });
        side.Children.Add(sideHeading);
        _sessions = new StackPanel { Spacing = 3 };
        var scroll = new ScrollViewer { Content = _sessions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); side.Children.Add(scroll);
        var sideFoot = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        sideFoot.Children.Add(Label("数据来源", 11, _p.Muted));
        sideFoot.Children.Add(Label("CRI Native Monitor", 13, _p.Voice));
        sideFoot.Children.Add(new TextBlock { Text = "原生播放与控制观测\n可选 SDK 补充内存与回调", FontSize = 11, Foreground = _p.Muted, LineHeight = 19 });
        Grid.SetRow(sideFoot, 2); side.Children.Add(sideFoot); _body.Children.Add(Surface(side, _p.Panel));

        var main = _main = new Grid { RowDefinitions = new RowDefinitions(_diagnostics ? "49,48,*,32,200" : "49,48,*,32,0") };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(14, 8) };
        foreach (var (id, title) in new[] { ("Timeline", "声音时间线"), ("AISAC", "控制"), ("Mixing", "混音"), ("Location", "空间"), ("Performance", "资源") })
        {
            var mode = id;
            nav.Children.Add(Action(title, () => { _mode = mode; SaveView(); Build(); RestoreView(); Refresh(); }, _mode == id));
        }
        var workspaceBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        workspaceBar.Children.Add(nav);
        var drawers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6,8,14,8) };
        drawers.Children.Add(Action("诊断", () => ApplyUiAction("diagnostics", "toggle")));
        drawers.Children.Add(Action("详情", () => { _showInspector = !_showInspector; Responsive(); }));
        Grid.SetColumn(drawers,1); workspaceBar.Children.Add(drawers);
        main.Children.Add(Surface(workspaceBar, _p.Canvas));
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), Margin = new Thickness(16, 7) };
        _filter = new TextBox { PlaceholderText = "筛选名称、对象、类型或内容…", FontSize = 12, Background = _p.Panel, BorderBrush = _p.Border, MinWidth = 120 };
        _filter.TextChanged += (_, _) => { if (!_rebuilding) UpdateEvents(); };
        toolbar.Children.Add(_filter);
        _live = Action("Live 跟随", ToggleView); _live.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_live, 1); toolbar.Children.Add(_live);
        var fit = Action("全览", Fit); fit.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(fit, 2); toolbar.Children.Add(fit);
        Grid.SetRow(toolbar, 1); main.Children.Add(toolbar);
        _timeline = new TimelineControl { Palette = _p, Mode = _mode };
        _timeline.EventSelected += SelectEvent;
        _timeline.SelectionCleared += () => { _selected = null; _showInspector = false; Inspector(); Responsive(); };
        _timeline.ViewChanged += () => { if (!_timeline.Live) _playing = false; _live.Content = ButtonContent(_timeline.Live ? "Live 跟随" : "返回实时"); UpdateRange(); UpdateEvents(); };
        Grid.SetRow(_timeline, 2); main.Children.Add(_timeline);
        _range = Label("事件证据  /  单调时间基准", 11, _p.Muted); _range.Margin = new Thickness(16, 0);
        var rangeBorder = Surface(_range, _p.Alternate); Grid.SetRow(rangeBorder, 3); main.Children.Add(rangeBorder);
        _events = new ListBox { Background = _p.Canvas, BorderThickness = new Thickness(0), FontSize = 11, Foreground = _p.Text };
        _events.SelectionChanged += (_, _) => { if (_events.SelectedItem is ListBoxItem { Tag: WireEvent item }) SelectEvent(item); };
        Grid.SetRow(_events, 4); main.Children.Add(_events); Grid.SetColumn(main, 1); _body.Children.Add(main);

        _details = new StackPanel { Spacing = 12, Margin = new Thickness(20, 23) };
        _inspector = Surface(new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, _p.Panel);
        Grid.SetColumn(_inspector, 2); _body.Children.Add(_inspector);
        Grid.SetRow(_body, 2); root.Children.Add(_body);
        _status = Label("正在启动接收器…", 11, _p.Muted); _status.Margin = new Thickness(15, 0);
        var footer = new StackPanel();
        _status.MinHeight = 29; footer.Children.Add(_status);
        _savedPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(15,0,15,6), IsVisible = false };
        _savedNotice = Label("",11,_p.Good); _savedNotice.TextTrimming = TextTrimming.CharacterEllipsis; _savedNotice.MaxWidth = 940;
        _savedPanel.Children.Add(_savedNotice);
        _savedPanel.Children.Add(Action("打开文件夹", () =>
        {
            if (string.IsNullOrEmpty(_lastSavedPath)) return;
            var folder = Path.GetDirectoryName(_lastSavedPath);
            if (folder != null && Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }));
        footer.Children.Add(_savedPanel); var status = Surface(footer, _p.Shell); Grid.SetRow(status, 3); root.Children.Add(status);
        Content = root;
        _timeline.Events = _snapshot;
        _sessionSignature = "!"; _rebuilding = false;
        Inspector(); Responsive(); UpdateEvents();
    }

    private void Responsive()
    {
        if (_body == null) return;
        var wide = Bounds.Width >= 1260;
        _body.ColumnDefinitions[2].Width = new GridLength(_showInspector ? 300 : 0);
        _inspector.IsVisible = _showInspector;
        _body.ColumnDefinitions[0].Width = new GridLength(!wide && _showInspector ? 0 : Bounds.Width < 1100 ? 165 : 208);
    }
    private void SaveView()
    {
        if (_session == null) return;
        _views[_session] = new ViewState { Filter = _filter.Text ?? "", End = _timeline.End, Span = _timeline.Span, Live = _timeline.Live, Selected = _selected, SelectionStart = _timeline.SelectionStart, SelectionEnd = _timeline.SelectionEnd };
    }
    private void RestoreView()
    {
        if (_session == null || !_views.TryGetValue(_session, out var v)) return;
        _filter.Text = v.Filter; _timeline.End = v.End; _timeline.Span = v.Span; _timeline.Live = v.Live; _selected = v.Selected; _timeline.Selected = v.Selected;
        _timeline.SetSelection(v.SelectionStart, v.SelectionEnd);
    }
    private void SelectSession(Session session)
    {
        SaveView(); _session = session; _playing = false; _selected = null; _timeline.Selected = null; _timeline.SetSelection(null,null); _filter.Text = ""; _timeline.Live = !session.IsReplay;
        _timeline.Span = 30; _timeline.End = Math.Max(30, session.LastTime);
        RestoreView(); _lastTotal = -1; _sessionSignature = ""; Refresh(); Inspector();
    }
    private void SelectEvent(WireEvent item)
    {
        _selected = item; _timeline.Selected = item;
        _showInspector = true; Responsive(); _timeline.InvalidateVisual(); Inspector(); UpdateRange();
    }
    private void Load(string path)
    {
        try { var session = _collector.LoadRecording(path); SelectSession(session); Fit(); _error = null; }
        catch (Exception ex) { _error = "打开失败：" + ex.Message; }
    }
    public string ProblemDescription { get; private set; } = "";
    public (double Start, double End) CurrentTimeRange()
    {
        EnsureUiThread();
        if (_timeline.SelectionStart is { } a && _timeline.SelectionEnd is { } b && Math.Abs(a-b) > .000001)
            return (Math.Max(0, Math.Min(a,b)), Math.Max(0, Math.Max(a,b)));
        return (Math.Max(0, _timeline.Start), Math.Max(0, _timeline.End));
    }
    private async Task ExportProblem()
    {
        if (_session == null || ExportProblemAsync == null) { _error = "请先选择会话；问题包导出服务尚未就绪"; return; }
        var target = _session;
        var dialog = new Window { Title = "导出问题包", Width = 540, Height = 310, CanResize = false, WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Background = _p.Panel, Icon = Icon };
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Margin = new Thickness(20) };
        layout.Children.Add(new TextBlock { Text = "描述遇到的问题（选填）", Foreground = _p.Text, FontSize = 16, Margin = new Thickness(0,0,0,14) });
        var description = new TextBox { Name = "ProblemDescriptionInput", Text = ProblemDescription, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
            PlaceholderText = "例如：音效突然停止；发生场景、预期表现与复现步骤…" };
        Grid.SetRow(description,1); layout.Children.Add(description);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0,14,0,0) };
        actions.Children.Add(Action("取消",()=>dialog.Close(false)));
        actions.Children.Add(Action("导出",()=>dialog.Close(true),true));
        Grid.SetRow(actions,2);layout.Children.Add(actions);dialog.Content=layout;
        if (!await dialog.ShowDialog<bool>(this)) return;
        ProblemDescription = (description.Text ?? "").Trim();
        try { _lastSavedPath = await ExportProblemAsync(target); _savedPaths = [_lastSavedPath]; _error = null; Refresh(); }
        catch (Exception ex) { _error = "导出失败：" + ex.Message; }
    }

    public byte[] CapturePng(string? panel = null)
    {
        EnsureUiThread();
        Control visual = (panel ?? "window").ToLowerInvariant() switch
        {
            "window" => this, "workspace" or "timeline" or "playback" or "control" or "mixing" or "space" or "resources" => _timeline,
            "inspector" or "details" => _inspector, "sessions" => _sessions, "diagnostics" => _events,
            _ => throw new ArgumentException("未知面板：" + panel)
        };
        if (!visual.IsVisible || visual.Bounds.Width < 1 || visual.Bounds.Height < 1) throw new InvalidOperationException("面板当前不可见");
        var scale = RenderScaling;
        using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(visual.Bounds.Width * scale), (int)Math.Ceiling(visual.Bounds.Height * scale)), new Vector(96 * scale, 96 * scale));
        bitmap.Render(visual);
        using var stream = new MemoryStream(); bitmap.Save(stream, PngBitmapEncoderOptions.Default); return stream.ToArray();
    }
    public object UiState()
    {
        EnsureUiThread();
        return new { workspace = _mode, theme = _p.Light ? "light" : "dark", session = _session?.Id, live = _timeline.Live, filter = _filter.Text,
            selected = _selected?.seq, start = _timeline.Start, end = _timeline.End, span = _timeline.Span,
            selectionStart = _timeline.SelectionStart, selectionEnd = _timeline.SelectionEnd,
            recording = _session != null && RecordingTargets(_session).Any(s => s.Recording), connected = _session?.Connected ?? false,
            inspector = _showInspector, diagnostics = _diagnostics, width = Bounds.Width, height = Bounds.Height, dpi = 96 * RenderScaling,
            panels = new[] { "window", "workspace", "sessions", "inspector", "diagnostics" } };
    }
    public void ApplyUiAction(string action, string? value)
    {
        EnsureUiThread();
        switch (action.ToLowerInvariant())
        {
            case "workspace":
                _mode = (value ?? "").ToLowerInvariant() switch { "播放" or "声音时间线" or "playback" or "timeline" => "Timeline", "控制" or "control" or "aisac" => "AISAC", "混音" or "mixing" => "Mixing", "空间" or "space" or "location" => "Location", "资源" or "resources" or "performance" => "Performance", _ => throw new ArgumentException("未知工作区") };
                SaveView(); Build(); RestoreView(); break;
            case "live": _timeline.Live = !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) && value != "0"; _lastTotal = -1; break;
            case "theme":
                var light = (value ?? "toggle").ToLowerInvariant() switch { "light" or "浅色" => true, "dark" or "暗色" => false, "toggle" => !_p.Light, _ => throw new ArgumentException("theme 需要 light / dark / toggle") };
                SaveView(); _p = new Palette(light); Build(); RestoreView(); break;
            case "diagnostics":
                _diagnostics = (value ?? "toggle").ToLowerInvariant() switch { "true" or "1" or "open" => true, "false" or "0" or "close" => false, "toggle" => !_diagnostics, _ => throw new ArgumentException("diagnostics 需要 true / false / toggle") };
                _main.RowDefinitions[4].Height = new GridLength(_diagnostics ? 200 : 0); UpdateEvents(); break;
            case "filter": _filter.Text = value ?? ""; UpdateEvents(); break;
            case "session": SelectSession(_collector.Sessions.FirstOrDefault(s => s.Id == value) ?? throw new ArgumentException("会话不存在")); break;
            case "select":
                if (!long.TryParse(value, out var seq)) throw new ArgumentException("select 需要事件 seq");
                SelectEvent(_snapshot.FirstOrDefault(e => e.seq == seq) ?? throw new ArgumentException("事件不在当前缓存中")); break;
            case "range":
                var range = (value ?? "").Split(':', ',');
                if (range.Length != 2 || !double.TryParse(range[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var start) || !double.TryParse(range[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var end) || !double.IsFinite(start) || !double.IsFinite(end) || end <= start) throw new ArgumentException("range 格式为 start:end，单位秒");
                _timeline.End = end; _timeline.Span = Math.Clamp(end-start,.25,86400); _timeline.Live = false; break;
            default: throw new ArgumentException("未知 UI 操作：" + action);
        }
        Refresh();
    }
    private static void EnsureUiThread()
    { if (!Dispatcher.UIThread.CheckAccess()) throw new InvalidOperationException("UI 接口必须通过 Dispatcher.UIThread 调用"); }
    private Session[] RecordingTargets(Session selected) => selected.IsReplay ? [] :
        string.IsNullOrEmpty(selected.ClientId) || string.IsNullOrEmpty(selected.CaptureId) ? [selected] :
        _collector.Sessions.Where(s => !s.IsReplay && s.ClientId == selected.ClientId && s.CaptureId == selected.CaptureId).ToArray();
    private void StartRecordings(Session selected)
    {
        var started = new List<Session>();
        try
        {
            foreach (var target in RecordingTargets(selected).Where(s => !s.Recording))
            { target.StartRecording(_collector.RecordingsDirectory); started.Add(target); }
            if (!string.IsNullOrEmpty(selected.ClientId) && !string.IsNullOrEmpty(selected.CaptureId))
                _recordingGroups.Add((selected.ClientId, selected.CaptureId));
        }
        catch { foreach (var target in started) target.StopRecording(); throw; }
    }
    private void Record()
    {
        if (_session == null || _session.IsReplay) return;
        try
        {
            var targets = RecordingTargets(_session);
            if (targets.Any(s => s.Recording))
            {
                _recordingGroups.Remove((_session.ClientId, _session.CaptureId));
                var recorded = targets.Where(s => s.Recording).ToArray();
                foreach (var target in recorded) target.StopRecording();
                _savedPaths = recorded.Select(s => s.RecordingPath).ToArray();
                _lastSavedPath = _savedPaths.FirstOrDefault();
                _error = recorded.FirstOrDefault(s => s.Error != null)?.Error;
            }
            else { StartRecordings(_session); _lastSavedPath = null; _savedPaths = []; _error = null; }
            Refresh();
        }
        catch (Exception ex) { _error = "录制失败：" + ex.Message; }
    }
    private void Fit()
    {
        var history = _session?.Snapshot() ?? _snapshot;
        if (history.Length == 0) return;
        var min = history.Min(e => e.time); var max = history.Max(e => e.time);
        _timeline.Span = Math.Max(1, max - min) * 1.05; _timeline.End = max + _timeline.Span * .025; _timeline.Live = false;
        _timeline.InvalidateVisual(); UpdateRange();
    }
    private void ToggleView()
    {
        if (_session is null) return;
        if (_session.IsReplay)
        {
            _playing = !_playing;
            if (_playing && _snapshot.Length > 0 && _timeline.End >= _snapshot.Max(e => e.time))
            {
                _timeline.Span = Math.Min(10, Math.Max(1, _snapshot.Max(e => e.time) - _snapshot.Min(e => e.time)));
                _timeline.End = _snapshot.Min(e => e.time);
            }
        }
        else { _timeline.Live = !_timeline.Live; if (_timeline.Live) _lastTotal = -1; }
        Refresh();
    }
    private void Refresh()
    {
        var sessions = _collector.Sessions;
        foreach (var seen in sessions) if (!_sessionOrder.ContainsKey(seen)) _sessionOrder[seen] = _sessionOrder.Count;
        _serverStatus.Text = _collector.Status;
        foreach (var group in _recordingGroups.ToArray())
        {
            var targets = sessions.Where(s => !s.IsReplay && s.ClientId == group.Client && s.CaptureId == group.Capture).ToArray();
            if (targets.Any(s => s.Connected)) continue;
            _recordingGroups.Remove(group);
            var recorded = targets.Where(s => s.Recording).ToArray();
            foreach (var target in recorded) target.StopRecording();
            if (recorded.Length > 0)
            {
                _savedPaths = recorded.Select(s => s.RecordingPath).ToArray();
                _lastSavedPath = _savedPaths[0];
                _error = recorded.FirstOrDefault(s => s.Error != null)?.Error;
            }
        }
        _savedPanel.IsVisible = !string.IsNullOrEmpty(_lastSavedPath);
        _savedNotice.Text = string.IsNullOrEmpty(_lastSavedPath) ? "" : (_savedPaths.Length > 1 ? $"已保存 {_savedPaths.Length} 个通道日志：" : "已保存：") + _lastSavedPath;
        ToolTip.SetTip(_savedNotice, string.Join("\n", _savedPaths));
        foreach (var target in sessions.Where(s => !s.IsReplay && s.Connected && !s.Recording && _recordingGroups.Contains((s.ClientId, s.CaptureId))))
        {
            try { target.StartRecording(_collector.RecordingsDirectory); }
            catch (Exception ex) { _recordingGroups.Remove((target.ClientId, target.CaptureId)); _error = "补充通道录制失败：" + ex.Message; }
        }
        if (!_autoRecorded && Environment.GetCommandLineArgs().Contains("--record") && sessions.FirstOrDefault(s => !s.IsReplay && s.Connected) is { } recordTarget)
        {
            _autoRecorded = true;
            try { StartRecordings(recordTarget); }
            catch (Exception ex) { _error = "自动录制失败：" + ex.Message; }
        }
        if (_session is { IsReplay: false, Connected: false } previous && _timeline.Live && !string.IsNullOrEmpty(previous.ClientId))
        {
            var replacement = sessions.Where(s => !s.IsReplay && s.Connected && s.ClientId == previous.ClientId && s.CaptureId != previous.CaptureId)
                .OrderByDescending(s => ClientCardPresentation.Channel(s) == ClientCardPresentation.Channel(previous))
                .ThenByDescending(s => _sessionOrder[s]).FirstOrDefault();
            if (replacement != null)
            {
                if (_views.TryGetValue(replacement, out var replacementView)) replacementView.Live = true;
                SelectSession(replacement); return;
            }
        }
        if (_session == null && sessions.Length > 0 && ClientCardPresentation.Group(sessions).Length == 1) { SelectSession(ClientCardPresentation.Default(sessions)); return; }
        var signature = string.Join("|", sessions.Select(s => $"{s.Id}/{s.IsReplay}/{s.Connected}/{s.Capturing}/{s.Recording}/{s.Name}")) + _session?.GetHashCode();
        if (signature != _sessionSignature)
        {
            _sessionSignature = signature; _sessions.Children.Clear();
            if (sessions.Length == 0)
            {
                _sessions.Children.Add(new TextBlock { Text = "等待游戏接入\n\n在游戏中填写运行 CriScope\n的电脑 IP，再开启采集。\n同机默认 127.0.0.1。\n\n同一游戏的原生与扩展\n通道会显示在同一张卡片。", TextWrapping = TextWrapping.Wrap, Foreground = _p.Muted, Margin = new Thickness(18), FontSize = 12, LineHeight = 23 });
            }
            foreach (var group in ClientCardPresentation.Group(sessions.OrderBy(s => _sessionOrder[s])))
            {
                var preferred = ClientCardPresentation.Default(group);
                bool selected = _session != null && group.Contains(_session);
                var card = new StackPanel { Spacing = 6, Margin = new Thickness(8,3) };
                var lines = new StackPanel { Spacing = 5 };
                lines.Children.Add(Label(preferred.Name,13,selected?_p.Selection:_p.Text));
                lines.Children.Add(Label($"{preferred.Machine} · {preferred.Platform}",10));
                lines.Children.Add(Label($"PID {preferred.Pid}"+(preferred.IsReplay?" · 日志回放":""),10));
                var heading = Action("",()=>SelectSession(preferred)); heading.Content=lines;
                heading.HorizontalAlignment=HorizontalAlignment.Stretch;heading.HorizontalContentAlignment=HorizontalAlignment.Left;
                heading.BorderBrush=selected?_p.Selection:_p.Border;card.Children.Add(heading);
                foreach(var channel in group.GroupBy(ClientCardPresentation.Channel))
                {
                    var current=channel.OrderByDescending(s=>s.Connected).ThenByDescending(s=>_sessionOrder[s]).First();
                    var name=channel.Key=="native"?"原生":"SDK 扩展";
                    var control=Action("",()=>SelectSession(current));
                    control.Content=Label(name+" · "+(current.Recording?"记录中":current.Connected?"已接入":"已断开"),10,ReferenceEquals(current,_session)?_p.Selection:_p.Muted);
                    control.Padding=new Thickness(8,5);control.HorizontalAlignment=HorizontalAlignment.Stretch;
                    ToolTip.SetTip(control,current.ConnectionStatus+"\n"+current.Source);card.Children.Add(control);
                }
                _sessions.Children.Add(card);
            }
        }
        _record.IsEnabled = _session != null && !_session.IsReplay;
        if (_session is { } s)
        {
            _identity.Text = $"{s.Name}  /  {s.Platform}  /  PID {s.Pid}";
            var replay = s.IsReplay;
            var recordingTargets = RecordingTargets(s);
            var recordingCount = recordingTargets.Count(target => target.Recording);
            _states.Text = $"{(replay ? "历史录制" : s.ConnectionStatus)}  ·  {(recordingCount > 0 ? $"● {recordingCount} 通道记录中" : "录制空闲")}  ·  {(replay ? _playing ? "回放播放" : "回放暂停" : _timeline.Live ? "Live 跟随" : "浏览历史 · 采集仍继续")}";
            _states.TextTrimming = TextTrimming.CharacterEllipsis; ToolTip.SetTip(_states, _states.Text);
            _record.Content = ButtonContent(recordingCount > 0 ? "停止并保存" : "开始记录日志");
            ToolTip.SetTip(_record, $"记录本次采集的全部已接入通道（当前 {recordingTargets.Length} 个），各自保存独立日志；包含当前已知状态与后续事件，原始观测时间保留");
            if (_lastTotal != s.Total)
            {
                _lastTotal = s.Total; _snapshot = s.ViewSnapshot(); _timeline.Events = _snapshot; UpdateEvents();
            }
            if (_timeline.Live && _snapshot.Length > 0) _timeline.End = _snapshot.Max(e => e.time);
            _timeline.SupplementMetrics = !string.IsNullOrEmpty(s.ClientId) && !s.IsReplay ? sessions.Where(other => other != s && other.ClientId == s.ClientId && other.CaptureId == s.CaptureId && !other.IsReplay && other.Connected && ClientCardPresentation.Channel(other) == "sdk")
                .SelectMany(other => other.ViewSnapshot().Where(e => e.kind == "metric"))
                .GroupBy(e => e.name).Select(g => g.Last()).ToArray() : [];
            if (s.IsReplay && _playing && _snapshot.Length > 0)
            {
                _timeline.End += .3;
                if (_timeline.End >= _snapshot.Max(e => e.time)) _playing = false;
            }
            _status.Text = _error ?? s.Error ?? $"{s.ConnectionStatus}   |   {s.Total:N0} 事件   ·   丢失 {s.Dropped:N0}   ·   已逐出 {s.Evicted:N0}   ·   后台录制 {sessions.Count(x => x.Id != s.Id && x.Recording)}";
            _status.Foreground = _error != null || s.Error != null ? _p.Error : _p.Muted;
        }
        else _status.Text = _error ?? _collector.Status;
        _live.Content = ButtonContent(_session?.IsReplay == true ? _playing ? "暂停回放" : "播放回放" : _timeline.Live ? "Live 跟随" : "返回实时");
        _live.IsEnabled = _session != null;
        UpdateRange(); _timeline.InvalidateVisual();
    }
    private void UpdateRange() => _range.Text = _timeline.SelectionStart is { } a && _timeline.SelectionEnd is { } b && Math.Abs(a-b)>.000001
        ? $"选区 {Math.Min(a,b):0.000} — {Math.Max(a,b):0.000} s  ·  Δ {Math.Abs(b-a):0.000} s  ·  {_snapshot.Count(e=>e.time>=Math.Min(a,b)&&e.time<=Math.Max(a,b)):N0} 事件"
        : $"{(_timeline.Live ? "实时" : "浏览历史")}   /   {TimelineControl.TimeLabel(_timeline.Start)} — {TimelineControl.TimeLabel(_timeline.End)}   /   诊断抽屉独立于当前工作区";
    private void UpdateEvents()
    {
        if (_events == null) return;
        var query = (_filter.Text ?? "").Trim();
        var filtered = _snapshot.Where(e => query.Length == 0 || $"{e.kind} {e.name} {e.objectId} {e.detail}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (query.Length > 0)
        {
            // Keep lifecycle counterparts: a name filter must not erase a confirmed stop.
            var objects = filtered.Where(e => !string.IsNullOrEmpty(e.objectId)).Select(e => (e.source, e.entity, e.objectId)).ToHashSet();
            var matched = filtered.ToHashSet();
            filtered = _snapshot.Where(e => matched.Contains(e) || e.kind is "play" or "stop" && objects.Contains((e.source, e.entity, e.objectId))).ToArray();
        }
        _timeline.Events = filtered;
        _timeline.Discontinuities = _snapshot.Where(e => e.kind == "gap" || e.kind == "state" && e.value <= 0).ToArray();
        var a = _timeline.SelectionStart; var b = _timeline.SelectionEnd;
        var results = filtered.Where(e => e.kind is "log" or "gap" or "state" or "beat" or "sequence" or "block" &&
            (!a.HasValue || !b.HasValue || Math.Abs(a.Value-b.Value)<.000001 || e.time>=Math.Min(a.Value,b.Value) && e.time<=Math.Max(a.Value,b.Value)))
            .TakeLast(250).Reverse().ToArray();
        var items = new List<ListBoxItem>();
        foreach (var e in results)
        {
            var line = new Grid { ColumnDefinitions = new ColumnDefinitions("90,84,*"), Margin = new Thickness(4, 2) };
            line.Children.Add(Label(e.time.ToString("0.000"), 11, _p.Muted));
            var kind = Label(e.kind.ToUpperInvariant(), 10, e.kind == "gap" ? _p.Error : _p.Signal); Grid.SetColumn(kind, 1); line.Children.Add(kind);
            var text = Label($"{e.name}  {e.objectId}  {(e.kind is "metric" or "aisac" ? e.value.ToString("0.###") : e.detail)}", 11);
            text.TextTrimming = TextTrimming.CharacterEllipsis; Grid.SetColumn(text, 2); line.Children.Add(text);
            items.Add(new ListBoxItem { Content = line, Tag = e, Padding = new Thickness(8, 3) });
        }
        _events.ItemsSource = items;
        if (results.Length == 0) _events.ItemsSource = new[] { new ListBoxItem { Content = Label(_snapshot.Length == 0 ? "暂无诊断 · 等待音频来源" : "没有匹配的诊断事件", 12, _p.Muted), IsEnabled = false } };
    }
    private void Inspector()
    {
        _details.Children.Clear(); _details.Children.Add(Label("证据 / INSPECTOR", 11, _p.Muted));
        if (_selected is not { } e)
        {
            _details.Children.Add(Label("从一个事件开始", 21));
            _details.Children.Add(new TextBlock { Text = "点击时间轴标记或事件行，检查对象、时间和值。每个事件始终归属当前会话。", TextWrapping = TextWrapping.Wrap, Foreground = _p.Muted, FontSize = 12, LineHeight = 22 });
            _details.Children.Add(new Border { Height = 1, Background = _p.Border, Margin = new Thickness(0, 10) });
            _details.Children.Add(Label("采集与证据边界", 14));
            _details.Children.Add(new TextBlock { Text = "• 实际 Voice 与 Cue 请求分开\n• AISAC 显示最后观测到的写入\n• 热接入不补造此前的历史\n• 缺失数据保持未提供\n• SDK 补充使用独立时钟", FontSize = 12, Foreground = _p.Muted, LineHeight = 25, TextWrapping = TextWrapping.Wrap });
            return;
        }
        _details.Children.Add(Label(e.kind.ToUpperInvariant(), 25, _p.Signal));
        _details.Children.Add(new TextBlock { Text = e.name, FontSize = 17, Foreground = _p.Text, TextWrapping = TextWrapping.Wrap });
        void Field(string label, string value)
        {
            _details.Children.Add(Label(label.ToUpperInvariant(), 10, _p.Muted));
            _details.Children.Add(new SelectableTextBlock { Text = string.IsNullOrEmpty(value) ? "未提供" : value, Foreground = _p.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        Field("来源", e.source);
        if (e.kind == "aisac") Field("AISAC 语义", "最近收到的控制值；更新时间见上方，不代表连接前所有参数已恢复");
        Field("时间", TimelineControl.TimeLabel(e.time));
        if (!string.IsNullOrEmpty(e.parentId)) Field("关联对象", e.parentId);
        if (e.kind is "aisac" or "metric") Field("值", e.kind == "metric" ? MetricPresentation.Value(e) : e.value.ToString("G9"));
        if (e.kind == "position") Field("坐标 X / Y / Z", $"{e.x:0.###} / {e.y:0.###} / {e.z:0.###}");
        Field("详情", e.detail);
        _details.Children.Add(Action("跳至此事件时间", () => { _timeline.Live = false; _timeline.End = e.time + _timeline.Span / 2; Refresh(); }));
        var technical = new StackPanel { Spacing = 10 };
        technical.Children.Add(new SelectableTextBlock { Text = $"Session  {e.session}\nEvent  #{e.seq}\nObject  {e.objectId}\nEntity  {e.entity}\nParent  {e.parentId}\nCue  {e.cue}\nClock  {e.time:G17}\n\n{e.raw}", Foreground = _p.Muted, FontSize = 11, TextWrapping = TextWrapping.Wrap });
        _details.Children.Add(new Expander { Header = "技术详情 / 原始字段", Content = technical, Foreground = _p.Muted, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Stretch });
        _details.Children.Add(Action("复制原始事件 JSON", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(JsonSerializer.Serialize(e)); }));
    }
}
