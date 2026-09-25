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
        Width = 1480; Height = 900; MinWidth = 980; MinHeight = 650;
        FontFamily = new FontFamily("Segoe UI, Microsoft YaHei UI");
        var launch = Environment.GetCommandLineArgs();
        _p = new Palette(launch.Contains("--light"));
        var modeIndex = Array.IndexOf(launch, "--view");
        if (modeIndex >= 0 && modeIndex + 1 < launch.Length && new[] { "Timeline", "AISAC", "Performance", "Location", "Logs" }.Contains(launch[modeIndex + 1])) _mode = launch[modeIndex + 1];
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
                Close();
            }
        };
        KeyDown += (_, e) =>
        {
            if (e.KeyModifiers.HasFlag(KeyModifiers.Control) && e.Key == Key.F) { _filter.Focus(); e.Handled = true; }
            else if (e.Key == Key.Home && e.Source is not TextBox) { Fit(); e.Handled = true; }
            else if (e.Key == Key.Space && e.Source is not TextBox) { ToggleView(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _filter.Text = ""; _timeline.SetSelection(null, null); UpdateEvents(); e.Handled = true; }
        };
    }

    private TextBlock Label(string text, double size = 12, IBrush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? _p.Text, VerticalAlignment = VerticalAlignment.Center };
    private Button Action(string text, Action action, bool accent = false)
    {
        var b = new Button { Content = text, FontSize = 12, Padding = new Thickness(12, 7),
            Background = accent ? _p.Selection : Brushes.Transparent, Foreground = accent ? _p.Canvas : _p.Text,
            BorderBrush = _p.Border, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
        b.Click += (_, _) => action(); return b;
    }
    private Border Surface(Control child, IBrush background, Thickness? padding = null) => new()
    { Child = child, Background = background, Padding = padding ?? new Thickness(0), BorderBrush = _p.Border, BorderThickness = new Thickness(0, 0, 0, 1) };

    private void Build()
    {
        _rebuilding = true;
        RequestedThemeVariant = _p.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        Background = _p.Canvas;
        var root = new Grid { RowDefinitions = new RowDefinitions("58,*,29") };
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("208,*,Auto"), Margin = new Thickness(16, 0) };
        var brand = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9, VerticalAlignment = VerticalAlignment.Center };
        brand.Children.Add(Label("◩", 29, _p.Signal));
        brand.Children.Add(Label("CriScope", 23));
        top.Children.Add(brand);
        var identity = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        _identity = Label("音频观测台", 13);
        _states = Label("未连接  ·  游戏采集 —  ·  桌面录制空闲  ·  Live 跟随", 11, _p.Muted);
        identity.Children.Add(_identity); identity.Children.Add(_states); Grid.SetColumn(identity, 1); top.Children.Add(identity);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        _record = Action("● 录制当前会话", Record);
        actions.Children.Add(_record);
        actions.Children.Add(Action("打开录制", async () =>
        {
            try
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(_collector.RecordingsDirectory);
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "打开 CriScope 录制", AllowMultiple = false, SuggestedStartLocation = folder,
                    FileTypeFilter = new[] { new FilePickerFileType("CriScope 录制") { Patterns = new[] { "*.criscope", "*.jsonl" } }, FilePickerFileTypes.All } });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Load(path);
            }
            catch (Exception ex) { _error = ex.Message; }
        }));
        actions.Children.Add(Action(_p.Light ? "暗色" : "浅色", () => { SaveView(); _p = new Palette(!_p.Light); Build(); RestoreView(); Refresh(); }));
        Grid.SetColumn(actions, 2); top.Children.Add(actions); root.Children.Add(Surface(top, _p.Shell));

        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("208,*,282") };
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var sideHeading = new StackPanel { Margin = new Thickness(18, 23, 12, 18), Spacing = 6 };
        sideHeading.Children.Add(Label("会话 / SESSIONS", 11, _p.Muted));
        sideHeading.Children.Add(Label("客户端与录制", 17)); side.Children.Add(sideHeading);
        _sessions = new StackPanel { Spacing = 3 };
        var scroll = new ScrollViewer { Content = _sessions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); side.Children.Add(scroll);
        var sideFoot = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        sideFoot.Children.Add(Label("数据来源", 11, _p.Muted));
        sideFoot.Children.Add(Label("Haru Bridge", 13, _p.Signal));
        sideFoot.Children.Add(new TextBlock { Text = "播放请求与业务状态\nCRI 内部 Voice 事件未接入", FontSize = 11, Foreground = _p.Muted, LineHeight = 19 });
        Grid.SetRow(sideFoot, 2); side.Children.Add(sideFoot); _body.Children.Add(Surface(side, _p.Panel));

        var main = new Grid { RowDefinitions = new RowDefinitions("49,48,*,32,220") };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, Margin = new Thickness(14, 8) };
        foreach (var (id, title) in new[] { ("Timeline", "时间轴"), ("AISAC", "AISAC"), ("Performance", "性能"), ("Location", "3D 位置"), ("Logs", "日志") })
        {
            var mode = id;
            nav.Children.Add(Action(title, () => { _mode = mode; SaveView(); Build(); RestoreView(); Refresh(); }, _mode == id));
        }
        nav.Children.Add(Action("证据", () => { _showInspector = !_showInspector; Responsive(); }));
        main.Children.Add(Surface(nav, _p.Canvas));
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
        _timeline.ViewChanged += () => { _playing = false; _live.Content = "冻结浏览"; UpdateRange(); UpdateEvents(); };
        Grid.SetRow(_timeline, 2); main.Children.Add(_timeline);
        _range = Label("事件证据  /  单调时间基准", 11, _p.Muted); _range.Margin = new Thickness(16, 0);
        var rangeBorder = Surface(_range, _p.Alternate); Grid.SetRow(rangeBorder, 3); main.Children.Add(rangeBorder);
        _events = new ListBox { Background = _p.Canvas, BorderThickness = new Thickness(0), FontSize = 11, Foreground = _p.Text };
        _events.SelectionChanged += (_, _) => { if (_events.SelectedItem is ListBoxItem { Tag: WireEvent item }) SelectEvent(item); };
        Grid.SetRow(_events, 4); main.Children.Add(_events); Grid.SetColumn(main, 1); _body.Children.Add(main);

        _details = new StackPanel { Spacing = 12, Margin = new Thickness(20, 23) };
        _inspector = Surface(new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled }, _p.Panel);
        Grid.SetColumn(_inspector, 2); _body.Children.Add(_inspector);
        Grid.SetRow(_body, 1); root.Children.Add(_body);
        _status = Label("正在启动接收器…", 11, _p.Muted); _status.Margin = new Thickness(15, 0);
        var status = Surface(_status, _p.Shell); Grid.SetRow(status, 2); root.Children.Add(status);
        Content = root;
        _timeline.Events = _snapshot;
        _sessionSignature = "!"; _rebuilding = false;
        Inspector(); Responsive(); UpdateEvents();
    }

    private void Responsive()
    {
        if (_body == null) return;
        var wide = Bounds.Width >= 1260;
        _body.ColumnDefinitions[2].Width = new GridLength(wide || _showInspector ? 282 : 0);
        _inspector.IsVisible = wide || _showInspector;
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
        if (item.time < _timeline.Start || item.time > _timeline.End) { _timeline.Live = false; _timeline.End = item.time + _timeline.Span / 2; }
        _showInspector = true; Responsive(); _timeline.InvalidateVisual(); Inspector(); UpdateRange();
    }
    private void Load(string path)
    {
        try { var session = _collector.LoadRecording(path); SelectSession(session); Fit(); _error = null; }
        catch (Exception ex) { _error = "打开失败：" + ex.Message; }
    }
    private void Record()
    {
        if (_session == null) return;
        try
        {
            if (_session.Recording) _session.StopRecording(); else _session.StartRecording(_collector.RecordingsDirectory);
            _error = null; Refresh();
        }
        catch (Exception ex) { _error = "录制失败：" + ex.Message; }
    }
    private void Fit()
    {
        if (_snapshot.Length == 0) return;
        var min = _snapshot.Min(e => e.time); var max = _snapshot.Max(e => e.time);
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
        if (!_autoRecorded && Environment.GetCommandLineArgs().Contains("--record") && sessions.FirstOrDefault(s => !s.IsReplay && s.Connected) is { } recordTarget)
        {
            _autoRecorded = true;
            try { recordTarget.StartRecording(_collector.RecordingsDirectory); }
            catch (Exception ex) { _error = "自动录制失败：" + ex.Message; }
        }
        if (_session == null && sessions.Length == 1) { SelectSession(sessions[0]); return; }
        var signature = string.Join("|", sessions.Select(s => $"{s.Id}/{s.IsReplay}/{s.Connected}/{s.Capturing}/{s.Recording}/{s.Name}")) + _session?.GetHashCode();
        if (signature != _sessionSignature)
        {
            _sessionSignature = signature; _sessions.Children.Clear();
            if (sessions.Length == 0)
            {
                _sessions.Children.Add(new TextBlock { Text = "等待游戏连接\n\n打开游戏中的音频调试\n→ CriScope 采集开关", TextWrapping = TextWrapping.Wrap, Foreground = _p.Muted, Margin = new Thickness(18), FontSize = 12, LineHeight = 23 });
            }
            foreach (var session in sessions)
            {
                var lines = new StackPanel { Spacing = 6 };
                lines.Children.Add(Label(session.Name, 13, ReferenceEquals(session, _session) ? _p.Selection : _p.Text));
                lines.Children.Add(Label($"{session.Platform}  /  PID {session.Pid}", 10, _p.Muted));
                lines.Children.Add(Label($"{(session.IsReplay ? "历史录制" : session.Connected ? "已连接" : "已断线")}  ·  {(session.Recording ? "● REC" : session.Capturing ? "采集中" : "采集关闭")}", 10, session.Recording ? _p.Error : _p.Muted));
                var button = Action("", () => SelectSession(session)); button.Content = lines; button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(8, 1); button.Padding = new Thickness(10, 13);
                button.BorderBrush = ReferenceEquals(session, _session) ? _p.Selection : Brushes.Transparent;
                button.Background = ReferenceEquals(session, _session) ? _p.Alternate : Brushes.Transparent; _sessions.Children.Add(button);
            }
        }
        _record.IsEnabled = _session != null && !_session.IsReplay;
        if (_session is { } s)
        {
            _identity.Text = $"{s.Name}  /  {s.Platform}  /  PID {s.Pid}";
            var replay = s.IsReplay;
            _states.Text = $"{(replay ? "历史录制" : s.Connected ? "已连接" : "已断线")}  ·  游戏采集{(replay ? "—" : s.Capturing ? "开启" : "关闭")}  ·  {(s.Recording ? "● 当前会话录制中" : "桌面录制空闲")}  ·  {(replay ? _playing ? "回放播放" : "回放暂停" : _timeline.Live ? "Live 跟随" : "冻结浏览")}";
            _record.Content = s.Recording ? "■ 停止当前会话录制" : "● 录制当前会话";
            ToolTip.SetTip(_record, string.IsNullOrEmpty(s.RecordingPath) ? "仅录制所选会话；从按下时开始，不回写此前缓存" : s.RecordingPath);
            if (_lastTotal != s.Total && (_timeline.Live || _lastTotal < 0))
            {
                _lastTotal = s.Total; _snapshot = s.Snapshot(); _timeline.Events = _snapshot; UpdateEvents();
            }
            if (_timeline.Live && _snapshot.Length > 0) _timeline.End = _snapshot.Max(e => e.time) + _timeline.Span * .035;
            if (s.IsReplay && _playing && _snapshot.Length > 0)
            {
                _timeline.End += .3;
                if (_timeline.End >= _snapshot.Max(e => e.time)) _playing = false;
            }
            _status.Text = _error ?? s.Error ?? $"{_collector.Status}   |   {s.Total:N0} 事件   ·   丢失 {s.Dropped:N0}   ·   已逐出 {s.Evicted:N0}   ·   后台录制 {sessions.Count(x => x.Id != s.Id && x.Recording)}";
            _status.Foreground = _error != null || s.Error != null ? _p.Error : _p.Muted;
        }
        else _status.Text = _error ?? _collector.Status;
        _live.Content = _session?.IsReplay == true ? _playing ? "暂停回放" : "播放回放" : _timeline.Live ? "Live 跟随" : "冻结浏览";
        _live.IsEnabled = _session != null;
        UpdateRange(); _timeline.InvalidateVisual();
    }
    private void UpdateRange() => _range.Text = _timeline.SelectionStart is { } a && _timeline.SelectionEnd is { } b && Math.Abs(a-b)>.000001
        ? $"选区 {Math.Min(a,b):0.000} — {Math.Max(a,b):0.000} s  ·  Δ {Math.Abs(b-a):0.000} s  ·  {_snapshot.Count(e=>e.time>=Math.Min(a,b)&&e.time<=Math.Max(a,b)):N0} 事件"
        : $"事件证据   /   {_timeline.Start:0.000} — {_timeline.End:0.000} s   /   最近 250 条筛选结果";
    private void UpdateEvents()
    {
        if (_events == null) return;
        var query = (_filter.Text ?? "").Trim();
        var filtered = _snapshot.Where(e => query.Length == 0 || $"{e.kind} {e.name} {e.objectId} {e.detail}".Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
        _timeline.Events = filtered;
        _timeline.Discontinuities = _snapshot.Where(e => e.kind == "gap" || e.kind == "state" && e.value <= 0).ToArray();
        var a = _timeline.SelectionStart; var b = _timeline.SelectionEnd;
        var results = filtered.Where(e => (_mode switch { "Logs" => e.kind is "log" or "gap" or "state", "AISAC" => e.kind == "aisac", "Performance" => e.kind == "metric", "Location" => e.kind == "position", _ => true }) &&
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
        if (results.Length == 0) _events.ItemsSource = new[] { new ListBoxItem { Content = Label(_snapshot.Length == 0 ? "暂无事件 · 等待游戏端采集" : "没有匹配的事件 · 尝试清除筛选", 12, _p.Muted), IsEnabled = false } };
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
            _details.Children.Add(new TextBlock { Text = "• PLAY 表示业务播放请求\n• AISAC 写值 / 开启时缓存快照，见详情\n• 指标来自实际游戏端采样\n• 热开启不补回之前的历史\n• 未接入的 Voice／Bus 字段不可用", FontSize = 12, Foreground = _p.Muted, LineHeight = 25, TextWrapping = TextWrapping.Wrap });
            return;
        }
        _details.Children.Add(Label(e.kind.ToUpperInvariant(), 25, _p.Signal));
        _details.Children.Add(new TextBlock { Text = e.name, FontSize = 17, Foreground = _p.Text, TextWrapping = TextWrapping.Wrap });
        void Field(string label, string value)
        {
            _details.Children.Add(Label(label.ToUpperInvariant(), 10, _p.Muted));
            _details.Children.Add(new SelectableTextBlock { Text = string.IsNullOrEmpty(value) ? "未提供" : value, Foreground = _p.Text, FontSize = 12, TextWrapping = TextWrapping.Wrap });
        }
        Field("来源", "Haru Bridge / 业务钩子与公开采样");
        if (e.kind == "aisac") Field("AISAC 语义", "写值 / 开启时缓存快照，见详情");
        Field("时间 / 事件编号", $"{e.time:0.000000} s  /  #{e.seq}");
        Field("会话", _session?.Id ?? e.session);
        Field("对象 / Cue", $"{e.objectId}  /  {e.cue}");
        if (e.kind is "aisac" or "metric") Field("值", e.value.ToString("G9"));
        if (e.kind == "position") Field("坐标 X / Y / Z", $"{e.x:0.###} / {e.y:0.###} / {e.z:0.###}");
        Field("详情", e.detail);
        _details.Children.Add(Action("复制原始事件 JSON", async () => { if (Clipboard != null) await Clipboard.SetTextAsync(JsonSerializer.Serialize(e)); }));
    }
}
