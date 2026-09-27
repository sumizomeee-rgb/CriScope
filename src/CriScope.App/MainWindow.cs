using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
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

public sealed partial class MainWindow : Window
{
    private readonly Collector _collector;
    private Palette _p = new(false);
    private readonly DispatcherTimer _timer;
    private readonly Dictionary<Session, ViewState> _views = [];
    private readonly Dictionary<Session, ControlIdentityLabels> _controlLabels = [];
    private readonly Dictionary<Session, long> _sessionOrder = [];
    private Session? _session;
    private TimelineControl _timeline = null!;
    private StackPanel _sessions = null!;
    private StackPanel _details = null!;
    private ScrollViewer _detailsScroll = null!;
    private SelectableTextBlock _inspectorKind = null!, _inspectorName = null!, _inspectorInstance = null!;
    private ListBox _events = null!;
    private TextBlock _states = null!, _status = null!, _range = null!;
    private TextBox _filter = null!;
    private Button _live = null!;
    private Grid _body = null!;
    private Border _inspector = null!;
    private Button _detailsToggle = null!;
    private readonly bool _animateInterface = SystemAnimationsEnabled();
    private string _sessionSignature = "!";
    private string _mode = "Timeline";
    private WireEvent[] _snapshot = [];
    private WireEvent? _selected;
    private WireEvent? _inspectedEvent;
    private string? _error;
    private long _lastTotal = -1;
    private bool _rebuilding;
    private bool _showInspector;
    private bool _playing;
    private bool _diagnostics;
    private TextBlock _serverStatus = null!, _savedNotice = null!;
    private StackPanel _savedPanel = null!;
    private string? _lastSavedPath;
    private string[] _savedPaths = [];
    private string? _visibleLogPath;
    public Func<Session, Task<string>>? ExportProblemAsync { get; set; }
    private Grid _main = null!;
    private ComboBox _windowRange = null!;
    private long _lastSupplementTotal = -1;
    private WireEvent[] _supplementMetrics = [];
    private double _lastInspectorEnd = double.NaN;
    private readonly TimeAxisPreferences _timeAxisPreferences;
    private bool _preferWallTimeAxis = true;

    [DllImport("user32.dll", EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint parameter, out int value, uint update);
    private static bool SystemAnimationsEnabled()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try { return SystemParametersInfo(0x1042, 0, out var enabled, 0) && enabled != 0; }
        catch (DllNotFoundException) { return true; }
        catch (EntryPointNotFoundException) { return true; }
    }

    private sealed class ViewState
    {
        public string Filter = "";
        public string CategoryId="", CategoryName="", SpatialFocus="";
        public WireEvent? FilterException;
        public double Vertical;
        public string[] Expanded=[];
        public bool Inspector;
        public double End = 30, Span = 30;
        public bool Live = true;
        public WireEvent? Selected;
        public double? SelectionStart, SelectionEnd;
        public string[] ControlKinds = ["aisac", "selector", "block", "beat", "sequence"];
        public bool ShowSources = true, ShowListeners = true, ShowBase;
    }

    public MainWindow(Collector collector, string? preferencesPath = null)
    {
        _collector = collector;
        _timeAxisPreferences = new TimeAxisPreferences(preferencesPath);
        _preferWallTimeAxis = _timeAxisPreferences.LoadWallTime();
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
        if (modeIndex >= 0 && modeIndex + 1 < launch.Length && new[] { "Timeline", "AISAC", "Performance", "Location", "Mixing", "Logs" }.Contains(launch[modeIndex + 1])) _mode = launch[modeIndex + 1];
        Build();
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
        Closed += (_, _) => { _timer.Stop(); _settingEvidenceCancellation.Cancel(); };
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
            else if (e.Key == Key.L && e.Source is not TextBox) { ReturnLive(); Refresh(); e.Handled = true; }
            else if (e.Key == Key.Escape && _showInspector) { _showInspector = false; Responsive(); e.Handled = true; }
            else if (e.Key == Key.Escape) { _filter.Text = ""; _timeline.SetSelection(null, null); UpdateEvents(); e.Handled = true; }
        };
    }

    private TextBlock Label(string text, double size = 12, IBrush? color = null) => new()
    { Text = text, FontSize = size, Foreground = color ?? _p.Text, VerticalAlignment = VerticalAlignment.Center };
    private Button Action(string text, Action action, bool accent = false)
    {
        var b = new Button { Content = ButtonContent(text), FontSize = 12, Padding = new Thickness(12, 7),
            Background = accent ? _p.Alternate : Brushes.Transparent, Foreground = _p.Text,
            BorderBrush = accent ? _p.Selection : _p.Border, BorderThickness = accent ? new Thickness(0,0,0,2) : new Thickness(0), CornerRadius = new CornerRadius(4) };
        b.PointerEntered += (_, _) => { if (!accent) b.Background = _p.Hover; };
        b.PointerExited += (_, _) => { if (!accent) b.Background = Brushes.Transparent; };
        ToolTip.SetTip(b, text);
        b.Click += (_, _) => action(); return b;
    }
    private Control ButtonContent(string text, bool accent = false)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        var icon = text switch {
            "声音时间线"=>IconKind.Timeline, "控制"=>IconKind.Controls, "混音"=>IconKind.Mixer,
            "空间"=>IconKind.Space, "资源"=>IconKind.Resources, "事件日志"=>IconKind.Log,
            "返回上一位置"=>IconKind.Back, "暂停刷新" or "暂停回放"=>IconKind.Pause,
            "播放回放"=>IconKind.Play, "继续实时" or "返回实时" or "跟随最新" or "跟随最新 · 开"=>IconKind.Live,
            "全览"=>IconKind.Fit, "导入日志…" or "打开日志目录" or "打开文件夹"=>IconKind.Open,
            "导出问题包…" or "导出"=>IconKind.Export, "更早记录"=>IconKind.Down,
            "详情"=>IconKind.Details, "查看 Voice 轨道"=>IconKind.Locate, _=>IconKind.None };
        if(icon!=IconKind.None)panel.Children.Add(VisualLanguage.Glyph(icon,accent?_p.Canvas:_p.Text));
        panel.Children.Add(Label(text,12,accent ? _p.Canvas : _p.Text)); return panel;
    }
    private Control CategoryChipContent(string text)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        content.Children.Add(new TextBlock { Text = text, FontSize = 11, Foreground = _p.Selection,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 82, VerticalAlignment = VerticalAlignment.Center });
        content.Children.Add(Label("×", 13, _p.Selection));
        return content;
    }
    private Border Surface(Control child, IBrush background, Thickness? padding = null) => new()
    { Child = child, Background = background, Padding = padding ?? new Thickness(0), BorderBrush = _p.Border, BorderThickness = new Thickness(0, 0, 0, 1) };

    private void Build()
    {
        _rebuilding = true;
        RequestedThemeVariant = _p.Light ? ThemeVariant.Light : ThemeVariant.Dark;
        Background = _p.Canvas;
        var root = new Grid { RowDefinitions = new RowDefinitions("36,40,*,Auto") };
        root.Children.Add(new WindowTitleBar(this, _p));
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(16, 0) };
        _states = Label("未连接", 12, _p.Muted);
        _states.TextTrimming = TextTrimming.CharacterEllipsis;
        top.Children.Add(_states);
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        var importLog=Action("导入日志…", async () =>
        {
            try
            {
                var folder = await StorageProvider.TryGetFolderFromPathAsync(_collector.RecordingsDirectory);
                var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions { Title = "导入 CriScope 日志 · 只读历史查看", AllowMultiple = false, SuggestedStartLocation = folder,
                    FileTypeFilter = new[] { new FilePickerFileType("CriScope 日志") { Patterns = new[] { "*.criscope", "*.jsonl" } }, FilePickerFileTypes.All } });
                if (files.Count > 0 && files[0].TryGetLocalPath() is { } path) Load(path);
            }
            catch (Exception ex) { _error = ex.Message; }
        });
        ToolTip.SetTip(importLog,"选择一个 CriScope 日志文件，在左侧新增只读历史会话；不会移动或修改原文件");
        var openLogDirectory=Action("打开日志目录", OpenLogDirectory);
        var export=Action("导出问题包…",async()=>await ExportProblem());
        var more=IconAction("更多操作",IconKind.More,()=>{});
        var menu=new Flyout {Content=new StackPanel {Spacing=4,Children={importLog,openLogDirectory,export}}};
        more.Flyout=menu;importLog.Click+=(_,_)=>menu.Hide();openLogDirectory.Click+=(_,_)=>menu.Hide();export.Click+=(_,_)=>menu.Hide();actions.Children.Add(more);
        actions.Children.Add(IconAction(_p.Light?"切换到深色":"切换到浅色",_p.Light?IconKind.Moon:IconKind.Sun,()=>{SaveView();_p=new Palette(!_p.Light);Build();RestoreView();Refresh();}));
        Grid.SetColumn(actions, 1); top.Children.Add(actions); var topSurface = Surface(top, _p.Shell); Grid.SetRow(topSurface, 1); root.Children.Add(topSurface);

        _body = new Grid { ColumnDefinitions = new ColumnDefinitions("208,*,Auto") };
        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var sideHeading = new StackPanel { Margin = new Thickness(18, 23, 12, 18), Spacing = 6 };
        sideHeading.Children.Add(Label("会话", 17));
        _serverStatus = new TextBlock { Text = "等待游戏接入 · 端口 18961", TextWrapping = TextWrapping.Wrap, Foreground = _p.Good, FontSize = 11, Margin = new Thickness(0,8,0,0) };
        ToolTip.SetTip(_serverStatus,"同机多实例可能受 CRI 原生端口限制");
        sideHeading.Children.Add(_serverStatus);
        side.Children.Add(sideHeading);
        _sessions = new StackPanel { Spacing = 3 };
        var scroll = new ScrollViewer { Content = _sessions, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); side.Children.Add(scroll);
        var sideFoot = new StackPanel { Margin = new Thickness(18), Spacing = 8 };
        var sourceLabel=Label("CRI Native Monitor", 11, _p.Muted);
        ToolTip.SetTip(sourceLabel,"数据来源：原生播放与控制观测；可选 SDK 补充内存与回调");
        sideFoot.Children.Add(sourceLabel);
        Grid.SetRow(sideFoot, 2); side.Children.Add(sideFoot); _body.Children.Add(Surface(side, _p.Panel));

        var main = _main = new Grid { RowDefinitions = new RowDefinitions(_diagnostics ? "44,44,*,28,260" : "44,44,*,28,0") };
        var nav = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3, Margin = new Thickness(10, 5) };
        foreach (var (id, title) in new[] { ("Timeline", "声音时间线"), ("AISAC", "控制"), ("Mixing", "混音"), ("Location", "空间"), ("Performance", "资源"), ("Logs", "事件日志") })
        {
            var mode = id;
            nav.Children.Add(Action(title, () => { _mode = mode; SaveView(); Build(); RestoreView(); Refresh(); }, _mode == id));
        }
        var workspaceBar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        _backButton=Action("返回上一位置",Back);_backButton.IsVisible=_navigation.Count>0;
        _backButton.Content=Label("← 返回",12,_p.Text);
        nav.Children.Insert(0,_backButton);
        workspaceBar.Children.Add(nav);
        var drawers = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(6,8,14,8) };
        // Event log has one visible entry: the workspace tab.
        _detailsToggle = new Button { Content = ButtonContent("详情"), FontSize = 12, Padding = new Thickness(10, 5),
            Foreground = _p.Text, CornerRadius = new CornerRadius(4) };
        _detailsToggle.PointerEntered += (_, _) => { if (!_showInspector) _detailsToggle.Background = _p.Hover; };
        _detailsToggle.PointerExited += (_, _) => UpdateDetailsToggle();
        _detailsToggle.Click += (_, _) => { _showInspector = !_showInspector; Responsive(); };
        drawers.Children.Add(_detailsToggle);
        Grid.SetColumn(drawers,1); workspaceBar.Children.Add(drawers);
        main.Children.Add(Surface(workspaceBar, _p.Canvas));
        var toolbar = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,Auto,Auto"), Margin = new Thickness(14, 5) };
        _filter = new TextBox { PlaceholderText = "筛选名称、对象、类型或内容…", FontSize = 12, Background = _p.Panel, BorderBrush = _p.Border, MinWidth = 120 };
        _filter.TextChanged += (_, _) => { if (!_rebuilding) { _filterException=null; UpdateEvents(); } };
        _categoryChip=Action("清除筛选",()=>{_categoryId="";_categoryName="";_filterException=null;UpdateEvents();});
        _categoryChip.MaxWidth=150;_categoryChip.Padding=new Thickness(8,4);_categoryChip.Margin=new Thickness(0,0,8,0);
        _categoryChip.IsVisible=_categoryId.Length>0;toolbar.Children.Add(_categoryChip);
        Grid.SetColumn(_filter,1); toolbar.Children.Add(_filter);
        _live = Action("跟随最新", ToggleView); _live.Margin = new Thickness(8, 0, 0, 0);
        Grid.SetColumn(_live, 2); toolbar.Children.Add(_live);
        _windowRange = new ComboBox { ItemsSource = new[] { "最近 10 秒", "最近 30 秒", "最近 2 分钟", "自定义范围" }, SelectedIndex = 1, MinWidth = 118, FontSize = 11, Margin = new Thickness(8,0,0,0) };
        _windowRange.SelectionChanged += (_,_) => { if (_rebuilding || _timeline == null || _windowRange.SelectedIndex > 2) return; _timeline.Span = new[] {10d,30d,120d}[_windowRange.SelectedIndex]; Refresh(); };
        Grid.SetColumn(_windowRange, 3); toolbar.Children.Add(_windowRange);
        var fit = Action("全览", ()=>ApplyUiAction("fit",null)); fit.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(fit, 5); toolbar.Children.Add(fit);
        if(_mode is "Location" or "Mixing"){fit.IsVisible=false;_windowRange.IsVisible=false;}
        ToolTip.SetTip(fit, "一次性缩放到当前保留的事件范围，并暂停跟随；不停止采集");
        Grid.SetRow(toolbar, 1); main.Children.Add(toolbar);
        if(_mode=="Logs"){toolbar.IsVisible=false;main.RowDefinitions[1].Height=new GridLength(0);}
        _timeline = new TimelineControl { Palette = _p, Mode = _mode, TimeOrigin = _session?.TimeOrigin ?? 0, PreferWallTime = _preferWallTimeAxis };
        _timeline.ResolveSettingEvidence = TimelineSettingRelationship;
        _timeline.EventSelected += SelectEvent;
        _timeline.PlaybackRequested += item=>Navigate("Timeline",item);
        _timeline.TimeAxisModeChanged += wall=>{_preferWallTimeAxis=wall;_timeAxisPreferences.Save(wall);};
        _timeline.SelectionCleared += () => { _selected = null; Inspector(); };
        _timeline.ViewChanged += () => { if (!_timeline.Live) _playing = false; _live.Content = ButtonContent(_timeline.Live ? "跟随最新 · 开" : "返回实时"); UpdateRange(); UpdateEvents(); };
        var axisMode = new ToggleButton
        {
            Name = "TimeAxisToggle", IsChecked = _preferWallTimeAxis,
            Width = 32, Height = 32, MinHeight = 0, Margin = new Thickness(8, 0, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(0), CornerRadius = new CornerRadius(6)
        };
        Border? axisVisual = null;
        axisMode.Template = new FuncControlTemplate<ToggleButton>((owner, _) =>
        {
            var glyph = new ContentPresenter { HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center };
            glyph.Bind(ContentPresenter.ContentProperty, new Binding("Content") { Source = owner });
            axisVisual = new Border { CornerRadius = new CornerRadius(6), Background = _p.Alternate,
                BorderBrush = owner.IsChecked == true ? _p.Selection : _p.Border,
                BorderThickness = new Thickness(1), Child = glyph };
            return axisVisual;
        });
        void SyncAxisMode()
        {
            var wall = axisMode.IsChecked == true;
            axisMode.Content = VisualLanguage.Glyph(wall ? IconKind.ClockTime : IconKind.RelativeTime, _p.Selection, 18);
            if (axisVisual != null) axisVisual.BorderBrush = wall ? _p.Selection : _p.Border;
            ToolTip.SetTip(axisMode, wall
                ? "当前：钟表时间（接收时钟估算）\n点击切换为相对时间；无时钟锚点时暂显示相对刻度"
                : "当前：相对时间（从采集起点计）\n点击切换为钟表时间");
        }
        void ChangeAxisMode()
        {
            SyncAxisMode();
            if (!_rebuilding) _timeline.SetTimeAxisMode(axisMode.IsChecked == true);
        }
        axisMode.PropertyChanged += (_, e) =>
        {
            if (e.Property == ToggleButton.IsCheckedProperty) ChangeAxisMode();
        };
        axisMode.PointerEntered += (_, _) => { if (axisVisual != null) { axisVisual.Background = _p.Hover; axisVisual.BorderBrush = _p.Selection; } };
        axisMode.PointerExited += (_, _) => { if (axisVisual != null) { axisVisual.Background = _p.Alternate; axisVisual.BorderBrush = axisMode.IsChecked == true ? _p.Selection : _p.Border; } };
        SyncAxisMode();
        axisMode.IsVisible = _mode is "Timeline" or "AISAC" or "Performance";
        Grid.SetColumn(axisMode, 4); toolbar.Children.Add(axisMode);
        Grid.SetRow(_timeline, 2); main.Children.Add(_timeline);
        _timeline.IsVisible=_mode!="Logs";
        _range = Label("事件证据  /  单调时间基准", 11, _p.Muted); _range.Margin = new Thickness(16, 0);
        ToolTip.SetTip(_range,"时间窗口可包含已结束的历史记录；钟表时间由接收锚点估算");
        var rangeBorder = Surface(_range, _p.Alternate); Grid.SetRow(rangeBorder, 3); main.Children.Add(rangeBorder);
        var eventLog=BuildEventLog();
        Grid.SetRow(eventLog,_mode=="Logs"?2:4); main.Children.Add(eventLog);
        if(_mode=="Logs"){main.RowDefinitions[4].Height=new GridLength(0);main.RowDefinitions[3].Height=new GridLength(0);_range.IsVisible=false;}
        Grid.SetColumn(main, 1); _body.Children.Add(main);

        _details = new StackPanel { Spacing = 4, Margin = new Thickness(16, 12) };
        var drawer = new Grid { RowDefinitions = new RowDefinitions("Auto,*") };
        var drawerHeading = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnDefinitions = new ColumnDefinitions("*,Auto"),
            Margin = new Thickness(16, 9, 10, 0) };
        _inspectorKind = new SelectableTextBlock {Tag="inspector-kind",Text="详情",FontSize=16,FontWeight=FontWeight.SemiBold,
            Foreground=_p.Muted,VerticalAlignment=VerticalAlignment.Center,TextWrapping=TextWrapping.Wrap,
            TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=2};
        AddCopyMenu(_inspectorKind,"复制事件名称");
        drawerHeading.Children.Add(_inspectorKind);
        var closeDetails = new Button { Content = "×", FontSize = 22, Width = 32, Height = 32, Padding = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center };
        ToolTip.SetTip(closeDetails, "关闭详情");
        closeDetails.Click += (_, _) => { _showInspector = false; Responsive(); };
        Grid.SetColumn(closeDetails, 1); drawerHeading.Children.Add(closeDetails);
        _inspectorName = new SelectableTextBlock {Tag="inspector-name",Text="",FontSize=11,
            Foreground=_p.Muted,Margin=new Thickness(0,2,0,10)};
        AddCopyMenu(_inspectorName,"复制事件类型");
        Grid.SetRow(_inspectorName,1);drawerHeading.Children.Add(_inspectorName);
        _inspectorInstance = new SelectableTextBlock {Tag="inspector-instance",FontSize=11,Foreground=_p.Muted,
            VerticalAlignment=VerticalAlignment.Top,Margin=new Thickness(8,7,0,0),IsVisible=false};
        AddCopyMenu(_inspectorInstance,"复制播放实例编号");
        Grid.SetRow(_inspectorInstance,1);Grid.SetColumn(_inspectorInstance,1);drawerHeading.Children.Add(_inspectorInstance);
        drawer.Children.Add(new Border {Child=drawerHeading,BorderBrush=_p.Border,BorderThickness=new Thickness(0,0,0,1)});
        _detailsScroll = new ScrollViewer { Content = _details, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(_detailsScroll, 1); drawer.Children.Add(_detailsScroll);
        _inspector = Surface(drawer, _p.Panel);
        _inspector.Width = _showInspector ? InspectorWidth() : 0;
        _inspector.Opacity = _showInspector ? 1 : 0;
        _inspector.IsEnabled = _showInspector;
        _inspector.ClipToBounds = true;
        if (_animateInterface) _inspector.Transitions = new Transitions {
            new DoubleTransition { Property = WidthProperty, Duration = TimeSpan.FromMilliseconds(170) },
            new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(150) }
        };
        Grid.SetColumn(_inspector, 2); _body.Children.Add(_inspector);
        Grid.SetRow(_body, 2); root.Children.Add(_body);
        _status = Label("正在启动接收器…", 11, _p.Muted); _status.Margin = new Thickness(15, 0);
        var footer = new StackPanel();
        _status.MinHeight = 29; footer.Children.Add(_status);
        _savedPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, Margin = new Thickness(15,0,15,6), IsVisible = false };
        _savedNotice = Label("",11,_p.Good); _savedNotice.TextTrimming = TextTrimming.CharacterEllipsis; _savedNotice.MaxWidth = 260;
        AddCopyMenu(_savedNotice,()=>ToolTip.GetTip(_savedNotice)?.ToString()??"","复制日志路径");
        _savedPanel.Children.Add(_savedNotice);
        _savedPanel.Children.Add(Action("打开文件夹", () =>
        {
            if (string.IsNullOrEmpty(_visibleLogPath)) return;
            var folder = Path.GetDirectoryName(_visibleLogPath);
            if (folder != null && Directory.Exists(folder)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }));
        footer.Children.Add(_savedPanel); var status = Surface(footer, _p.Shell); Grid.SetRow(status, 3); root.Children.Add(status);
        Content = root;
        _timeline.Events = _snapshot;
        _sessionSignature = "!"; _rebuilding = false;
        _lastInspectorEnd = double.NaN;
        Inspector(); Responsive(); UpdateEvents();
    }

    private void Responsive()
    {
        if (_body == null) return;
        var wide = Bounds.Width >= 1260;
        _body.ColumnDefinitions[2].Width = GridLength.Auto;
        _inspector.Width = _showInspector ? InspectorWidth() : 0;
        _inspector.Opacity = _showInspector ? 1 : 0;
        _inspector.IsEnabled = _showInspector;
        _body.ColumnDefinitions[0].Width = new GridLength(!wide && _showInspector ? 0 : Bounds.Width < 1100 ? 165 : 208);
        UpdateDetailsToggle();
    }
    private double InspectorWidth() => Bounds.Width >= 1260 ? 372 : 344;
    private void UpdateDetailsToggle()
    {
        if (_detailsToggle == null) return;
        _detailsToggle.Background = _showInspector ? _p.Alternate : Brushes.Transparent;
        _detailsToggle.BorderBrush = _showInspector ? _p.Selection : _p.Border;
        _detailsToggle.BorderThickness = _showInspector ? new Thickness(0, 0, 0, 2) : new Thickness(0);
        ToolTip.SetTip(_detailsToggle, _showInspector ? "隐藏事件详情" : "显示事件详情");
    }
    private void SaveView()
    {
        if (_session == null) return;
        _views[_session] = new ViewState { Filter = _filter.Text ?? "", End = _timeline.End, Span = _timeline.Span, Live = _timeline.Live, Selected = _selected, SelectionStart = _timeline.SelectionStart, SelectionEnd = _timeline.SelectionEnd,
            SpatialFocus=_timeline.SpatialFocusId, CategoryId=_categoryId, CategoryName=_categoryName, FilterException=_filterException, Vertical=_timeline.VerticalOffset, Expanded=_timeline.ExpandedKeys, Inspector=_showInspector,
            ControlKinds=_timeline.ControlKinds.ToArray(), ShowSources=_timeline.ShowSources, ShowListeners=_timeline.ShowDistanceListeners, ShowBase=_timeline.ShowBaseListeners };
    }
    private void RestoreView()
    {
        if (_session == null || !_views.TryGetValue(_session, out var v)) return;
        _timeline.SpatialFocusId=v.SpatialFocus; _filter.Text = v.Filter; _categoryId=v.CategoryId; _categoryName=v.CategoryName; _filterException=v.FilterException; _timeline.VerticalOffset=v.Vertical; _timeline.ExpandedKeys=v.Expanded; _showInspector=v.Inspector; _timeline.End = v.End; _timeline.Span = v.Span; _timeline.Live = v.Live; _selected = v.Selected; _timeline.Selected = v.Selected;
        _timeline.SetSelection(v.SelectionStart, v.SelectionEnd);
        _timeline.ControlKinds.Clear(); foreach (var kind in v.ControlKinds) _timeline.ControlKinds.Add(kind);
        _timeline.ShowSources=v.ShowSources; _timeline.ShowDistanceListeners=v.ShowListeners; _timeline.ShowBaseListeners=v.ShowBase;
    }
    private void SelectSession(Session session)
    {
        SaveView(); _navigation.Clear();_categoryId="";_categoryName="";_filterException=null;_logFollowing=true;_logFrozen=[]; _session = session; _playing = false; _selected = null; _timeline.Selected = null; _timeline.SetSelection(null,null); _filter.Text = ""; _timeline.Live = !session.IsReplay;
        _timeline.Span = 30; _timeline.End = Math.Max(30, session.LastTime);
        RestoreView(); _lastTotal = -1; _lastSupplementTotal = -1; _sessionSignature = ""; Refresh(); Inspector();
    }
    private void SelectEvent(WireEvent item)
    {
        _selected = item; _timeline.Selected = item;
        if(_mode=="Logs" && !_updatingLog && _events.ItemsSource is {} rows) {
            _updatingLog=true;
            try {_events.SelectedItem=rows.Cast<ListBoxItem>().FirstOrDefault(row=>row.Tag is WireEvent value && value.session==item.session && value.seq==item.seq);}
            finally {_updatingLog=false;}
        }
        _showInspector = true; Responsive(); _timeline.InvalidateVisual(); Inspector(); UpdateRange();
    }
    private void Load(string path)
    {
        try { var session = _collector.LoadRecording(path); SelectSession(session); Fit(); _error = null; }
        catch (Exception ex) { _error = "导入日志失败：" + ex.Message; }
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
            selected = _selected?.seq, start = _timeline.Start, end = _timeline.End, span = _timeline.Span, timeOrigin = _timeline.TimeOrigin,
            controlKinds = _timeline.ControlKinds.ToArray(), showSources = _timeline.ShowSources, showListeners = _timeline.ShowDistanceListeners,
            selectionStart = _timeline.SelectionStart, selectionEnd = _timeline.SelectionEnd,
            recording = _session != null && RecordingTargets(_session).Any(s => s.Recording), connected = _session?.Connected ?? false,
            inspector = _showInspector, diagnostics = _diagnostics, width = Bounds.Width, height = Bounds.Height, dpi = 96 * RenderScaling,
            category = _categoryName, navigationDepth = _navigation.Count, logQuery = _logQuery, logFollowing = _logFollowing,
            panels = new[] { "window", "workspace", "sessions", "inspector", "diagnostics" } };
    }
    public void ApplyUiAction(string action, string? value)
    {
        EnsureUiThread();
        switch (action.ToLowerInvariant())
        {
            case "workspace":
                _mode = (value ?? "").ToLowerInvariant() switch { "播放" or "声音时间线" or "playback" or "timeline" => "Timeline", "控制" or "control" or "aisac" => "AISAC", "混音" or "mixing" => "Mixing", "空间" or "space" or "location" => "Location", "资源" or "resources" or "performance" => "Performance", "logs" or "日志" => "Logs", _ => throw new ArgumentException("未知工作区") };
                SaveView(); Build(); RestoreView(); break;
            case "live": if (!string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) && value != "0") ReturnLive(); else _timeline.Live=false; break;
            case "fit": if(_mode=="Location"){_timeline.SpatialFocusId="";_timeline.InvalidateVisual();}else Fit(); break;
            case "control-kind":
                var kindParts=(value??"").Split(':'); if(kindParts.Length!=2)throw new ArgumentException("control-kind 格式 aisac:true");
                _timeline.SetControlKind(kindParts[0],bool.Parse(kindParts[1])); break;
            case "spatial-layer":
                var layerParts=(value??"").Split(':'); if(layerParts.Length!=2)throw new ArgumentException("spatial-layer 格式 sources:true");
                _timeline.SetSpatialLayer(layerParts[0] switch { "sources" => "source", "listeners" => "distance-listener", _ => layerParts[0] },bool.Parse(layerParts[1])); break;
            case "theme":
                var light = (value ?? "toggle").ToLowerInvariant() switch { "light" or "浅色" => true, "dark" or "暗色" => false, "toggle" => !_p.Light, _ => throw new ArgumentException("theme 需要 light / dark / toggle") };
                SaveView(); _p = new Palette(light); Build(); RestoreView(); break;
            case "diagnostics":
                _diagnostics = (value ?? "toggle").ToLowerInvariant() switch { "true" or "1" or "open" => true, "false" or "0" or "close" => false, "toggle" => !_diagnostics, _ => throw new ArgumentException("diagnostics 需要 true / false / toggle") };
                _main.RowDefinitions[4].Height = new GridLength(_diagnostics && _mode!="Logs" ? 260 : 0); UpdateEvents(); break;
            case "back": Back(); break;
            case "log-search": _logQuery=value??"";if(_logSearch!=null)_logSearch.Text=_logQuery;UpdateEventLog();break;
            case "log-follow": if(_logFollowing!=bool.Parse(value??"true"))ToggleLogFollow();break;
            case "navigate":
                if(_selected is not {} navSelected)throw new ArgumentException("先选择一个事件");
                if(value=="space")
                {
                    var ownerIds=navSelected.entity=="cue"?new[]{navSelected.objectId}:navSelected.entity=="voice"?new[]{navSelected.parentId}:
                        (navSelected.kind is "aisac" or "selector"?SettingRelationship(navSelected).Owners:
                            AssociationPresentation.PlayerPlaybacks(_snapshot,navSelected.objectId,ContextTime)).Select(p=>p.Id).ToArray();
                    var sourceEvidence=navSelected.kind is "aisac" or "selector"
                        ? AssociationPresentation.EvidenceThrough(_snapshot,navSelected) : _snapshot;
                    var sources=AssociationPresentation.SourcesFor(sourceEvidence,ownerIds,ContextTime);
                    if(sources.Length!=1)throw new ArgumentException("需要唯一空间目标；请在详情中选择");Navigate("Location",sources[0]);
                }
                else if(value=="playback"&&navSelected.kind=="position")
                {
                    var owners=AssociationPresentation.SourcePlaybacks(_snapshot,navSelected,ContextTime);
                    if(owners.Length!=1 || AssociationPresentation.Anchor(owners[0]) is not {} target)throw new ArgumentException("需要唯一播放实例；请在详情中选择");Navigate("Timeline",target);
                }
                else LocateLogEvent(navSelected);
                break;
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
    private void OpenLogDirectory()
    {
        try
        {
            var folder = _session is { IsReplay: false } && !string.IsNullOrEmpty(_session.RecordingPath)
                ? Path.GetDirectoryName(_session.RecordingPath) : _collector.RecordingsDirectory;
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) { _error = "尚无日志文件"; Refresh(); return; }
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = folder, UseShellExecute = true });
        }
        catch (Exception ex) { _error = "打开日志目录失败：" + ex.Message; Refresh(); }
    }
    private Session[] RecordingTargets(Session selected) => selected.IsReplay ? [] :
        string.IsNullOrEmpty(selected.ClientId) || string.IsNullOrEmpty(selected.CaptureId) ? [selected] :
        _collector.Sessions.Where(s => !s.IsReplay && s.ClientId == selected.ClientId && s.CaptureId == selected.CaptureId).ToArray();
    private void Fit()
    {
        var history = _session?.Snapshot() ?? _snapshot;
        if (history.Length == 0) return;
        var min = history.Min(e => e.time); var max = history.Max(e => e.time);
        _timeline.Span = Math.Max(1, max - min) * 1.05; _timeline.End = max + _timeline.Span * .025; _timeline.Live = false;
        _timeline.InvalidateVisual(); UpdateRange();
    }
    private void ReturnLive()
    { _timeline.Live = true; _lastTotal = -1; }
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
        else { if (_timeline.Live) _timeline.Live=false; else ReturnLive(); }
        Refresh();
    }
    private void Refresh()
    {
        var sessions = _collector.Sessions;
        foreach (var seen in sessions) if (!_sessionOrder.ContainsKey(seen)) _sessionOrder[seen] = _sessionOrder.Count;
        var onlineClients=ClientCardPresentation.Group(sessions.Where(s=>s.Connected&&!s.IsReplay)).Length;
        _serverStatus.Text=onlineClients>0?$"{onlineClients} 个客户端在线 · 接收端口 18961":_collector.Status;
        var currentLogPaths = _session is { IsReplay: false } logSession
            ? RecordingTargets(logSession).Select(s => s.RecordingPath).Where(path => !string.IsNullOrWhiteSpace(path)).Distinct().ToArray() : [];
        var visiblePaths = currentLogPaths.Length > 0 ? currentLogPaths : _savedPaths;
        _visibleLogPath = visiblePaths.FirstOrDefault();
        _savedPanel.IsVisible = _visibleLogPath != null;
        _savedNotice.Text = _visibleLogPath == null ? "" : currentLogPaths.Length > 0
            ? $"本次日志 · {visiblePaths.Length} 个通道"
            : "问题包已导出";
        ToolTip.SetTip(_savedNotice, string.Join("\n", visiblePaths));
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
        if (_session is {IsReplay:false,Connected:true} sdkFirst && _timeline.Live && ClientCardPresentation.Channel(sdkFirst)=="sdk")
        {
            var primary=sessions.FirstOrDefault(s=>s.Connected&&!s.IsReplay&&s.ClientId==sdkFirst.ClientId&&s.CaptureId==sdkFirst.CaptureId&&ClientCardPresentation.Channel(s)=="native");
            if(primary!=null) { var filter=_filter.Text; SelectSession(primary); _filter.Text=filter; return; }
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
            var clientGroups = ClientCardPresentation.Group(sessions.OrderBy(s => _sessionOrder[s]));
            foreach (var group in clientGroups)
            {
                var preferred = ClientCardPresentation.Default(group);
                bool selected = _session != null && group.Contains(_session);
                var card = new StackPanel { Spacing = 6, Margin = new Thickness(8,3) };
                var duplicateName = clientGroups.Count(other => string.Equals(ClientCardPresentation.Default(other).Name, preferred.Name, StringComparison.OrdinalIgnoreCase)) > 1;
                var headingLine = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
                var clientName = Label(string.IsNullOrWhiteSpace(preferred.Name) ? "未命名客户端" : preferred.Name, 13, selected ? _p.Selection : _p.Text);
                clientName.TextTrimming = TextTrimming.CharacterEllipsis;
                headingLine.Children.Add(clientName);
                var cardStatus = ClientCardPresentation.Status(group);
                var statusColor = cardStatus == "在线" || cardStatus == "记录中" ? _p.Good : cardStatus == "已断开" ? _p.Error : _p.Signal;
                var status = Label("● " + cardStatus, 10, statusColor);
                Grid.SetColumn(status, 1); headingLine.Children.Add(status);
                var lines = new StackPanel { Spacing = 8 };
                lines.Children.Add(headingLine);
                var address=ClientCardPresentation.Address(preferred);
                if(address!="IP 未提供")lines.Children.Add(Label(address, 11, _p.Text));
                if (duplicateName && !preferred.IsReplay) lines.Children.Add(Label($"进程 {preferred.Pid}", 10, _p.Muted));
                var channels = new WrapPanel { Orientation = Orientation.Horizontal };
                foreach (var channel in group.GroupBy(ClientCardPresentation.Channel))
                {
                    var current=channel.OrderByDescending(s=>s.Connected).ThenByDescending(s=>_sessionOrder[s]).First();
                    if(!current.Connected)continue;
                    var name=channel.Key=="native"?"原生":"SDK";
                    var label=Label($"{name} {(current.Connected?"✓":"—")}",10,current.Connected?_p.Good:_p.Muted);
                    label.Margin=new Thickness(0,0,12,0); ToolTip.SetTip(label,current.ConnectionStatus); channels.Children.Add(label);
                }
                if(channels.Children.Count>0)lines.Children.Add(channels);
                var heading = Action("",()=>SelectSession(preferred)); heading.Content=lines;
                heading.HorizontalAlignment=HorizontalAlignment.Stretch;heading.HorizontalContentAlignment=HorizontalAlignment.Left;
                heading.Padding=new Thickness(10,12);heading.BorderBrush=selected?_p.Selection:_p.Border;
                heading.BorderThickness=new Thickness(selected?1:0);heading.Background=selected?_p.Alternate:Brushes.Transparent;
                heading.Tag=selected?"selected-client":"client";
                heading.PointerExited+=(_,_)=>heading.Background=selected?_p.Alternate:Brushes.Transparent;
                ToolTip.SetTip(heading, $"{preferred.Name}\n{preferred.Machine} · {preferred.Platform}\nPID {preferred.Pid}\n同一客户端的原生与 SDK 数据合并浏览");
                card.Children.Add(heading);
                _sessions.Children.Add(card);
            }
        }
        if (_session is { } s)
        {
            var replay = s.IsReplay;
            var recordingTargets = RecordingTargets(s);
            var recordingCount = recordingTargets.Count(target => target.Recording);
            var recordingErrors = recordingTargets.Where(target => !string.IsNullOrWhiteSpace(target.Error))
                .Select(target => $"{(target.Channel == "native" ? "原生" : target.Channel == "sdk" ? "SDK" : "事件")}：{target.Error}").ToArray();
            var connection=replay?"历史录制":s.ConnectionStatus;
            _states.Text=recordingErrors.Length>0?$"{connection} · 自动日志异常"
                : recordingCount>0?$"{connection} · ● {recordingCount} 通道自动记录中":connection;
            _states.Foreground=recordingErrors.Length>0?_p.Error:recordingCount>0?_p.Good:_p.Muted;
            _states.TextTrimming = TextTrimming.CharacterEllipsis;
            ToolTip.SetTip(_states, recordingErrors.Length>0?string.Join("\n",recordingErrors):_states.Text);
            var supplements = !string.IsNullOrEmpty(s.ClientId) && !string.IsNullOrEmpty(s.CaptureId) && !s.IsReplay && ClientCardPresentation.Channel(s)=="native"
                ? sessions.Where(other=>other!=s && other.ClientId==s.ClientId && other.CaptureId==s.CaptureId && !other.IsReplay && ClientCardPresentation.Channel(other)=="sdk").ToArray() : [];
            var supplementTotal=supplements.Sum(other=>other.Total);
            if (_lastTotal != s.Total || _lastSupplementTotal != supplementTotal)
            {
                _lastTotal=s.Total; _lastSupplementTotal=supplementTotal;
                var nativeEvents=s.ViewSnapshot();
                _snapshot=ClientTimelineProjection.Combine(nativeEvents,supplements.SelectMany(other=>other.ViewSnapshot()).ToArray());
                _timeline.TimeOrigin=s.TimeOrigin;
                _timeline.SourceConnected=s.Connected || s.IsReplay;
                _supplementMetrics=supplements.SelectMany(other=>other.ViewSnapshot().Where(e=>e.kind=="metric"))
                    .GroupBy(e=>e.name).Select(g=>g.Last()).ToArray();
                UpdateEvents();
            }
            _timeline.SourceConnected=s.Connected || s.IsReplay;
            _timeline.TimeOrigin=s.TimeOrigin;
            _timeline.SupplementMetrics=_supplementMetrics;
            _timeline.WallStamp=time=>WallTime(time,s);
            _timeline.ClockTimeAt=time=>s.EstimateWallTime(new WireEvent {time=time});
            if (!_controlLabels.TryGetValue(s, out var labels)) _controlLabels[s] = labels = new ControlIdentityLabels();
            _timeline.ControlLabels = labels;
            if (_timeline.Live && _snapshot.Length > 0) _timeline.End = _snapshot.Max(e => e.time);
            if (_showInspector && _selected!=null && (double.IsNaN(_lastInspectorEnd) || Math.Abs(_timeline.End-_lastInspectorEnd)>.5)) { _lastInspectorEnd=_timeline.End; Inspector(); }
            if (s.IsReplay && _playing && _snapshot.Length > 0)
            {
                _timeline.End += .3;
                if (_timeline.End >= _snapshot.Max(e => e.time)) _playing = false;
            }
            _status.Text = _error ?? s.Error ?? $"{s.ConnectionStatus}   |   {s.Total:N0} 事件   ·   丢失 {s.Dropped:N0}   ·   缓存保留 {_snapshot.Length:N0}";
            _status.Foreground = _error != null || s.Error != null ? _p.Error : _p.Muted;
        }
        else _status.Text = _error ?? _collector.Status;
        _live.Content = ButtonContent(_session?.IsReplay == true ? _playing ? "暂停回放" : "播放回放" : _timeline.Live ? "跟随最新 · 开" : "返回实时");
        _live.IsEnabled = _session != null;
        var preset = Math.Abs(_timeline.Span-10)<.01 ? 0 : Math.Abs(_timeline.Span-30)<.01 ? 1 : Math.Abs(_timeline.Span-120)<.01 ? 2 : 3;
        _rebuilding=true; _windowRange.SelectedIndex=preset; _rebuilding=false;
        ToolTip.SetTip(_live, _timeline.Live ? "点击暂停画面跟随；采集与记录继续" : "回到最新事件，保留当前时间范围；快捷键 L");
        _live.BorderBrush=_timeline.Live?_p.Good:_p.Border;
        UpdateRange(); _timeline.InvalidateVisual();
    }
    private void UpdateRange()
    {
        var basis=_timeline.ShowingWallTime?"钟表时间（接收估算）":"采集相对时间";
        _range.Text=_timeline.SelectionStart is { } a && _timeline.SelectionEnd is { } b && Math.Abs(a-b)>.000001
            ? $"选区 · {basis} {_timeline.DisplayStamp(Math.Min(a,b))} — {_timeline.DisplayStamp(Math.Max(a,b))} · 持续 {Math.Abs(b-a):0.000} 秒"
            : $"{(_timeline.Live ? "跟随最新" : "暂停跟随")}  ·  {basis} {_timeline.DisplayStamp(_timeline.Start)} — {_timeline.DisplayStamp(_timeline.End)}  ·  窗口 {_timeline.ViewSpan:0.#} 秒";
    }
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
            var matchingPlayers=filtered.Where(e=>e.kind is "aisac" or "selector").Select(e=>e.objectId).ToHashSet();
            var linkedRequests=_snapshot.Where(e=>e.kind=="request"&&e.entity=="cue"&&matchingPlayers.Contains(e.parentId));
            var playbackIds=filtered.Concat(linkedRequests).Where(e=>e.entity=="cue").Select(e=>e.objectId).Concat(filtered.Where(e=>e.entity=="voice").Select(e=>e.parentId)).ToHashSet();
            filtered = _snapshot.Where(e => matched.Contains(e) || objects.Contains((e.source,e.entity,e.objectId)) || playbackIds.Contains(e.objectId) || playbackIds.Contains(e.parentId)).ToArray();
        }
        if(_categoryId.Length>0)
        {
            var ids=_snapshot.Where(e=>e.kind=="category"&&e.objectId==_categoryId&&e.time<=_timeline.End).Select(e=>e.parentId).ToHashSet();
            filtered=filtered.Where(e=>ids.Contains(e.objectId)||ids.Contains(e.parentId)).ToArray();
        }
        if(_filterException is {} target)
        {
            var id=target.entity=="voice"?target.parentId:target.objectId;
            filtered=filtered.Concat(_snapshot.Where(e=>e.objectId==id||e.parentId==id||e==target)).Distinct().ToArray();
        }
        if(_categoryChip!=null)
        {
            _categoryChip.IsVisible=_categoryId.Length>0||_filterException!=null&&query.Length>0;
            _categoryChip.Content=CategoryChipContent(_categoryId.Length>0?_categoryName:"临时定位");
            ToolTip.SetTip(_categoryChip,_categoryId.Length>0
                ? "Category: "+_categoryName+(_filterException!=null?" · 临时定位":"")+"\n点击清除筛选"
                : "临时显示定位目标\n点击清除筛选");
        }
        _timeline.AssociationEvents = _snapshot;
        _timeline.BusHistoryIsSparse = _session is { IsReplay: true, BusSamplesAreEventTriggered: true };
        _timeline.Events = filtered;
        _timeline.Discontinuities = _snapshot.Where(e => e.kind == "gap" || e.entity == "capture-segment" || e.kind == "state" && e.value <= 0).ToArray();
        UpdateEventLog();
        _timeline.InvalidateVisual();
    }
    private void Inspector()
    {
        UpdateInspectorHeading();
        var content = new StackPanel();
        BuildInspector(content);
        OrganizeInspector(content);
        StyleFolds(content);
        if (!ReferenceEquals(_inspectedEvent, _selected)) { _details.Children.Clear(); _detailsScroll.Offset = new Vector(0,0); }
        InspectorPresentation.Update(_details, content);
        _inspectedEvent = _selected;
    }
    private static string InspectorKindLabel(WireEvent e) => e.kind switch
    {
        "request" => "播放实例", "play" => "开始播放", "stop" => "声部结束", "stop-request" => "请求停止",
        "aisac" => "AISAC 设置", "selector" => "Selector 设置", "block" => "Block 事件",
        "beat" => "节拍事件", "sequence" => "序列事件", "position" => "空间位置",
        "metric" => "资源指标", "cue-info" => "Cue 信息", "gap" => "数据缺口",
        "log" => "原始协议记录", "error" => "错误", "state" => "连接状态", _ => "事件记录"
    };
    private void UpdateInspectorHeading()
    {
        if (_selected is not { } e)
        {
            _inspectorKind.Text="详情";
            _inspectorKind.Foreground=_p.Muted;
            _inspectorName.Text="";
            _inspectorInstance.Text="";
            _inspectorInstance.IsVisible=false;
            return;
        }
        var kind=InspectorKindLabel(e);
        var playbackId=e.kind is "request" or "play" or "stop"
            ? e.entity=="cue"?e.objectId:e.parentId : "";
        var name=e.name;
        if(string.IsNullOrWhiteSpace(name) && playbackId.Length>0)
            name=_snapshot.FirstOrDefault(x=>x.session==e.session&&x.kind=="request"&&x.entity=="cue"&&x.objectId==playbackId)?.name??"";
        if(string.IsNullOrWhiteSpace(name))name="名称未记录";
        if(_inspectorKind.Text!=name)_inspectorKind.Text=name;
        _inspectorKind.Foreground=ControlPresentation.Kinds.Contains(e.kind)?_p.Control(e.kind)
            :e.kind=="request"?_p.Request:e.kind is "play" or "stop" or "stop-request"?_p.Semantic(VisualLanguage.EventColor(e)):_p.Text;
        ToolTip.SetTip(_inspectorKind,name);
        if(_inspectorName.Text!=kind)_inspectorName.Text=kind;
        if(e.kind=="request" && e.entity=="cue" && e.detail.StartsWith("CRI Cue 播放实例",StringComparison.Ordinal))
            ToolTip.SetTip(_inspectorName,e.detail);
        else ToolTip.SetTip(_inspectorName,kind);
        var instance=playbackId.Length>0
            ? _timeline.ControlLabels.Get("播放实例",JsonSerializer.Serialize(new[]{e.session,playbackId})).Replace("播放实例 ","") : "";
        if(_inspectorInstance.Text!=instance)_inspectorInstance.Text=instance;
        _inspectorInstance.IsVisible=instance.Length>0;
    }
    private void BuildInspector(StackPanel details)
    {
        if (_selected is not { } e)
        {
            details.Children.Add(Label("选择一个事件", 21));
            var introduction=new TextBlock {Text="点击时间轴或事件日志中的记录，查看时间、状态与关联对象。",TextWrapping=TextWrapping.Wrap,Foreground=_p.Muted,FontSize=12,LineHeight=22};
            ToolTip.SetTip(introduction,"观测只呈现已收到的证据：Cue 请求与 Voice 分开；热接入不补造历史；缺失数据标为未提供；SDK 补充使用独立时钟。");
            details.Children.Add(introduction);
            return;
        }
        void Field(string label, string value)
        {
            var row=new Grid {Tag="row:"+label, ColumnDefinitions=new ColumnDefinitions("90,*"), ColumnSpacing=10, MinHeight=28, Margin=new Thickness(0)};
            var title=Label(label,12,_p.Muted);title.VerticalAlignment=VerticalAlignment.Top;row.Children.Add(title);
            if(label=="开始播放" && value!="尚未分配 Voice")title.Foreground=_p.Semantic(SemanticColor.Started);
            if(label is "结束播放" or "请求停止")title.Foreground=_p.Semantic(SemanticColor.Ended);
            if(label=="已播放" || label=="状态" && value.Contains("播放中",StringComparison.Ordinal))title.Foreground=_p.Request;
            var text=new SelectableTextBlock {Tag="field:"+label,Text=string.IsNullOrEmpty(value)?"未提供":value,Foreground=_p.Text,FontSize=13,TextWrapping=TextWrapping.Wrap};
            if(label=="开始播放" && value=="开始发生在记录之前")text.Foreground=_p.Semantic(SemanticColor.Started);
            if(label=="已播放" || label=="状态" && value.Contains("播放中",StringComparison.Ordinal))text.Foreground=_p.Request;
            Grid.SetColumn(text,1);row.Children.Add(text);details.Children.Add(row);
        }
        var eventSession=_collector.Sessions.FirstOrDefault(s=>s.Id==e.session)??_session;
        bool independentClock=eventSession!=_session&&!e.estimatedTime;
        var clockSession=independentClock?eventSession:_session;
        if(_mode=="Logs" || e.entity!="cue" || e.kind is not ("request" or "play" or "stop" or "stop-request" or "log")) {
            Field("发生时间", WallTime(e.time,clockSession));
            ToolTip.SetTip(details.Children.Last(),"钟表时间由接收锚点换算："+(clockSession?.FormatWallTime(e)??"未提供"));
        }
        bool controlEvent=ControlPresentation.Kinds.Contains(e.kind);
        bool eventContext=controlEvent || _mode=="Logs";
        if(_mode=="Logs")Field("事件",EventLogPresentation.Action(e));
        if(controlEvent && e.kind is not ("aisac" or "selector"))
            Field(e.kind=="sequence"?"回调内容":"事件内容",ControlPresentation.Value(e));
        var playbackId=e.entity=="cue"?e.objectId:e.parentId;
        var inspectedAt=eventContext?e.time:_timeline.End;
        var groups=PlaybackPresentation.Group(_snapshot,inspectedAt);
        var playback=groups.FirstOrDefault(g=>g.Id==playbackId);
        var eventDetails=details;
        if(playback!=null && eventContext) {
            Field("所属声音",PlaybackLabel(playback));
            details=new StackPanel {Spacing=4};
        }
        if(playback!=null)
        {
            var sheet=CueMetadataPresentation.AcbName(playback.Request);
            Field("CueSheet / ACB",sheet.Length>0?sheet:"未获取");
            ToolTip.SetTip(details.Children.Last(),"原生 Monitor 提供的 ACB 名称；可能与引擎注册的 CueSheet 别名不同。");
            var categories=AssociationPresentation.Categories(_snapshot,playback.Id,inspectedAt);
            var configuredCue=_snapshot.LastOrDefault(x=>x.kind=="cue-info"&&x.name==playback.Name&&(x.parentId==playback.Id||x.objectId==playback.Id));
            if(categories.Length==0){
                var names=AssociationPresentation.CueCategories(configuredCue);
                Field(names.Length>0?"Cue 分类":"Category",names.Length>0?string.Join(" · ",names):"未获取");
                ToolTip.SetTip(details.Children.Last(),names.Length>0?"来自此 Cue 的 ACB 配置；不是当前实例的运行时覆盖分类。":"尚无此播放实例的 Category 归属记录；不代表未设置分类。");
            }
            else {
                var categoryRow=new Grid {Tag="categories",ColumnDefinitions=new ColumnDefinitions("90,*"),ColumnSpacing=10,Margin=new Thickness(0,6)};
                var categoryLabel=Label("Category",12,_p.Muted);categoryLabel.VerticalAlignment=VerticalAlignment.Top;categoryRow.Children.Add(categoryLabel);
                var chips=new WrapPanel();Grid.SetColumn(chips,1);categoryRow.Children.Add(chips);
                foreach(var category in categories){var chip=InspectorValue(category.name,13);chip.Tag="category-info:"+category.objectId;chip.Margin=new Thickness(0,0,10,5);ToolTip.SetTip(chip,category.name);chips.Children.Add(chip);}
                details.Children.Add(categoryRow);
            }
            Field("状态", !_timeline.SourceConnected && playback.End==null ? "已断开 · 最后状态" : (eventContext || !_timeline.Live ? "该时刻 · " : "")+(playback.End!=null?"已结束":playback.StatusLabel));

            if(playback.RequestAt is {} playRequested)Field("请求播放",WallTime(playRequested,clockSession));
            Field("开始播放", playback.StartedAt is {} started?WallTime(started,clockSession):playback.UnknownStart?"开始发生在记录之前":"尚未分配 Voice");
            if(playback.End!=null)Field("结束播放", playback.EndedAt is {} stopped?WallTime(stopped,clockSession):playback.End!=null?"实例已结束，声部释放时间未记录":"—");
            var elapsedLabel=playback.UnknownStart?"本次观测":playback.End==null?"已播放":"播放历时";
            var elapsed=playback.UnknownStart&&!playback.HasEvidenceGap&&AssociationPresentation.Anchor(playback) is {} first?Math.Max(0,Math.Min(inspectedAt,playback.InstanceEndedAt??inspectedAt)-first.time):playback.DurationAt(inspectedAt);
            Field(elapsedLabel,elapsed is {} duration?$"{duration:0.000} 秒":playback.Voices.Length==0&&playback.End==null?"尚未开始":"未完整记录");
            ToolTip.SetTip(details.Children.Last(),"按原始事件时钟计算，截止当前观测位置，可能包含暂停或间隔；起点未知时仅计本次观测时长。");
            var cueInfo=_snapshot.LastOrDefault(x=>x.kind=="cue-info"&&x.name==playback.Name&&(x.parentId==playback.Id||x.objectId==playback.Id));
            Field("Cue 时长",cueInfo==null?"未获取":cueInfo.value<0?"无限／不定长":$"{cueInfo.value/1000:0.000} 秒");
            ToolTip.SetTip(details.Children.Last(),"Cue 配置标注的时长，不是当前播放实例的预计结束时间。");
            if(playback.End!=null)Field("结束原因",playback.EndReasonLabel);
            if(playback.StopRequestedAt is {} requestedAt)Field("请求停止", WallTime(requestedAt,clockSession));
            if(playback.CausePlaybackId.Length>0)
            {
                var cause=groups.FirstOrDefault(g=>g.Id==playback.CausePlaybackId);
                Field("由谁触发",cause==null?"记录未保留":PlaybackLabel(cause));
                if(cause?.Request is {} causeRequest) { var row=(Grid)details.Children.Last();row.ColumnDefinitions=new ColumnDefinitions("90,*,28");var button=IconAction("定位触发此结束的播放实例",IconKind.Locate,()=>Navigate("Timeline",causeRequest,true));button.Width=28;button.Height=28;button.Tag="cause:"+cause.Id;Grid.SetColumn(button,2);row.Children.Add(button); }
            }
            AddAssociations(details,e,playback);
            var playerDetails=new StackPanel {Spacing=6};
            playerDetails.Children.Add(InspectorValue(_timeline.ControlLabels.Get("Player",JsonSerializer.Serialize(new[]{playback.Request?.session??"",playback.PlayerId}))+ $" · Voice {playback.Voices.Length} 个",12));
            playerDetails.Children.Add(Action("查看 Voice 轨道",()=>{if(AssociationPresentation.Anchor(playback) is {} target){Navigate("Timeline",target);_timeline.ShowVoiceDetails(playback.Id);}}));
            details.Children.Add(new Expander {Tag="timing",Header="播放器与 Voice",Content=playerDetails,FontSize=13});
            var controls=PlaybackPresentation.ControlsFor(playback,_snapshot,inspectedAt);
            var controlList=new StackPanel {Spacing=8};
            void AddControl(WireEvent item,string phase)
            {
                string value=item.kind=="aisac"?item.value.ToString("0.####",CultureInfo.InvariantCulture):item.kind=="selector"?item.detail:item.name+" · "+item.detail;
                var link=new Button {Tag=$"event:{item.session}:{item.seq}",Content=new TextBlock {Text=$"{EventLogPresentation.Action(item)} · {item.name} = {value}\n{WallTime(item.time,clockSession)}",TextWrapping=TextWrapping.Wrap,FontSize=11},HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};
                link.Click+=(_,_)=>Navigate("AISAC",item,true);controlList.Children.Add(link);
            }
            var related=controls.BeforeStart.Concat(controls.DuringPlayback).ToArray();
            foreach(var item in related.TakeLast(3))AddControl(item,"");
            if(related.Length>3)controlList.Children.Add(Action("查看全部关联记录",()=>ShowRelatedLog(playback.Id)));
            if(controlList.Children.Count==0)controlList.Children.Add(Label("尚未收到与此实例关联的控制记录",11,_p.Muted));
            details.Children.Add(new Expander {Tag="controls",Header=$"关联控制与回调（{controls.BeforeStart.Length+controls.DuringPlayback.Length}）",Content=controlList,FontSize=12});
        }
        else if(e.kind is "aisac" or "selector")
        {
            var relation=SettingRelationship(e);
            var owners=relation.Owners;
            var category=e.objectId.StartsWith("category",StringComparison.OrdinalIgnoreCase);
            var relationLabel=TimelineSettingRelationship(e).EmptyLabel;
            Field(e.kind=="aisac"?"设置值":"设置标签",e.kind=="aisac"?e.value.ToString("0.####",CultureInfo.InvariantCulture):e.detail);
            Field("写入时关联",owners.Length==1?owners[0].Name:owners.Length>1?owners[0].Name+$" · 另 {owners.Length-1} 个实例":category?"Category 级设置":relationLabel);
            ToolTip.SetTip(details.Children.Last(),relation.Reason+"；关联表示当时已观测到播放实例，不代表实际 Voice 正在发声。来源："+relation.Source);
            if(owners.Length==0 && relation.LastPlayedRequest is { } recent)
                Field("此前最近播放",recent.name+" · 历史线索");
            foreach(var owner in owners.Take(8))if(owner.Request is {} request && _snapshot.Any(item=>item.session==request.session&&item.seq==request.seq)) { var button=NavigationAction("定位播放实例",()=>Navigate("Timeline",request),PlaybackLabel(owner)); button.Tag="owner:"+owner.Id; details.Children.Add(button); }
        }
        if(!ReferenceEquals(details,eventDetails)) {
            var playbackDetails=details;details=eventDetails;
            details.Children.Add(new Expander {Tag="playback-summary",Header="事件时刻的播放概况",Content=playbackDetails,FontSize=12});
        }
        if (ControlPresentation.Kinds.Contains(e.kind))
        {
            var controlGroups=ControlPresentation.Group(_snapshot,_timeline.End,labels:_timeline.ControlLabels);
            var selectedRow=controlGroups.SelectMany(g=>g.Rows.Select(r=>(Group:g,Row:r)))
                .FirstOrDefault(x=>x.Row.Records.Any(r=>r.session==e.session&&r.seq==e.seq));
            if(selectedRow.Row is {} row)
            {
                Field("记录归属",selectedRow.Group.Name+" / "+row.Name);
                var history=new StackPanel {Spacing=6};
                if(row.Records.Length>3)history.Children.Add(InspectorValue($"最近 3 / {row.Records.Length} 条",10,_p.Muted));
                foreach(var item in row.Records.TakeLast(3))
                {
                    var link=new Button {Tag=$"event:{item.session}:{item.seq}",Content=new TextBlock {Text=$"{WallTime(item.time,clockSession)}  {ControlPresentation.Value(item)}",TextWrapping=TextWrapping.Wrap,FontSize=11},HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Left};
                    link.Click+=(_,_)=>{SelectEvent(item);_timeline.FocusEvent(item);};history.Children.Add(link);
                }
                details.Children.Add(new Expander {Tag="history",Header=$"{(row.Kind is "aisac" or "selector"?"设置记录":"事件记录")}（{row.Records.Length}）",IsExpanded=true,Content=history,FontSize=12});
            }
        }
        if (e.kind == "metric") Field("值", MetricPresentation.Value(e));
        if (e.kind == "position") Field("坐标 X / Y / Z", $"{e.x:0.###} / {e.y:0.###} / {e.z:0.###}");
        if(playback==null && e.kind is not ("aisac" or "selector") &&
            !(e.kind=="request" && e.entity=="cue" && e.detail.StartsWith("CRI Cue 播放实例",StringComparison.Ordinal)))
            Field("详情", e.detail);
        if(!independentClock && _mode!="Logs") {
            var locateTime=playback!=null&&!controlEvent?playback.StartedAt??AssociationPresentation.Anchor(playback)?.time??e.time:e.time;
            var locateLabel=playback!=null&&!controlEvent?playback.StartedAt!=null?"定位开始播放":"定位首次观测":"定位事件时间";
            details.Children.Add(NavigationAction(locateLabel, () => { _timeline.Live = false; _timeline.End = locateTime + _timeline.Span / 2; Refresh(); }));
        }
        else if(independentClock) details.Children.Add(InspectorValue("此项使用 SDK 独立时钟，不直接跳转原生时间轴。",11,_p.Muted));
        if(playback==null)AddAssociations(details,e,playback);
        if(_mode=="Logs")details.Children.Add(NavigationAction("定位该事件",()=>LocateLogEvent(e)));
        var technical = new StackPanel { Spacing = 8 };
        technical.Children.Add(InspectorValue("接收时间估算："+(clockSession?.FormatWallTime(e)??"未提供"),11,_p.Muted));
        var identifiers=new (string Name,string Value)[] {
            ("Session",e.session),("Event","#"+e.seq),("Object",e.objectId),("Entity",e.entity),
            ("Parent",e.parentId),("Cue",e.cue.ToString(CultureInfo.InvariantCulture)),("Clock",e.time.ToString("G17",CultureInfo.InvariantCulture))};
        technical.Children.Add(new SelectableTextBlock {Text=string.Join("\n",identifiers.Where(x=>x.Value.Length>0).Select(x=>x.Name.PadRight(8)+" "+x.Value)),
            Foreground=_p.Muted,FontSize=11,TextWrapping=TextWrapping.Wrap});
        var copyActions=new WrapPanel {Orientation=Orientation.Horizontal};
        var copyRaw=Action("复制原始内容",async ()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(e.raw);});
        copyRaw.IsEnabled=e.raw.Length>0;copyActions.Children.Add(copyRaw);
        var copyEvent=Action("复制事件 JSON",async ()=>{if(Clipboard!=null)await Clipboard.SetTextAsync(JsonSerializer.Serialize(e));});
        copyEvent.Margin=new Thickness(6,0,0,0);copyActions.Children.Add(copyEvent);
        technical.Children.Add(copyActions);
        technical.Children.Add(Label("原始内容",11,_p.Muted));
        var rawText=new SelectableTextBlock {Text=e.raw.Length>0?e.raw:"未保留原始内容",Foreground=_p.Muted,FontSize=10,
            FontFamily=new FontFamily("Cascadia Mono, Consolas"),TextWrapping=TextWrapping.Wrap};
        var rawScroll=new ScrollViewer {Content=rawText,MaxHeight=180,HorizontalScrollBarVisibility=Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled};
        technical.Children.Add(new Border {Tag="raw-content",Background=_p.Alternate,Padding=new Thickness(8),Child=rawScroll});
        details.Children.Add(new Expander {Tag="technical",Header="原始记录（高级）",Content=technical,
            Foreground=_p.Muted,FontSize=12,HorizontalAlignment=HorizontalAlignment.Stretch});
    }
}
