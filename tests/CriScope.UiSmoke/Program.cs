using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.VisualTree;
using Avalonia.LogicalTree;
using CriScope.App;
using CriScope.Core;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => AppBuilder.Configure<SmokeApp>().UsePlatformDetect().StartWithClassicDesktopLifetime(args);
}

public sealed class SmokeApp : Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string directory = Path.Combine(Path.GetTempPath(), "CriScope-ui-" + Guid.NewGuid().ToString("N"));
            // No listener or native connection: these sessions are explicit UI fixtures.
            var collector = new Collector(directory);
            var a = CreateSession(1001, "Alpha"); var b = CreateSession(1002, "Beta");
            var sessions = (ConcurrentDictionary<string, Session>)typeof(Collector).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collector)!;
            sessions[a.Id] = a; sessions[b.Id] = b;
            var preferencesPath = Path.Combine(directory, "ui-preferences.json");
            var window = new MainWindow(collector, preferencesPath); desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                int passed = 0;
                try
                {
                    var lifecycle = new[] {
                        new WireEvent {kind="request",entity="cue",objectId="pb1",name="Same Cue",time=1},
                        new WireEvent {kind="request",entity="cue",objectId="pb2",name="Same Cue",time=2},
                        new WireEvent {kind="play",entity="voice",objectId="v1",parentId="pb1",time=1},
                        new WireEvent {kind="play",entity="voice",objectId="v2",parentId="pb1",time=1},
                        new WireEvent {kind="play",entity="voice",objectId="v3",parentId="pb2",time=2}};
                    var playbackGroups = PlaybackPresentation.Group(lifecycle, 10);
                    Check(playbackGroups.Length == 2 && playbackGroups.Single(g=>g.Id=="pb1").Voices.Length == 2, "同名 Cue 的两个 Playback 不合并，Voice 归所属实例");
                    Check(PlaybackPresentation.Group([new WireEvent {kind="stop",entity="voice",objectId="unknown",time=2}],10).Single().UnknownStart, "缺失 Playback 开始保持未知");
                    Check(PlaybackPresentation.LatestLabel("beat").Contains("节拍") && !PlaybackPresentation.LatestLabel("beat").Contains("写入"), "节拍使用事件语义文案");
                    var clientId = Guid.NewGuid().ToString("N");
                    void Connected(Session target, bool value) => typeof(Session).GetProperty("Connected")!.SetValue(target,value);
                    Session Meta(string client, string channel, string capture="")
                    {
                        var target = new Session(new WireEvent {kind="hello",session=Guid.NewGuid().ToString("N"),clientId=client,captureId=capture,channel=channel,pid=99,name="test"});
                        Connected(target,true); return target;
                    }
                    var native=Meta(clientId,"native"); var sdk=Meta(clientId,"sdk"); var other=Meta(Guid.NewGuid().ToString("N"),"native");
                    Check(ClientCardPresentation.Group([native,sdk,other]).Length==2 && ClientCardPresentation.Default([sdk,native])==native, "客户端按 ClientId 归组，默认原生通道，不按相同 PID 合并");
                    Check(ClientCardPresentation.Status([native,sdk])=="在线", "卡片明确标示在线");
                    Connected(native,false); Connected(sdk,false);
                    Check(ClientCardPresentation.Status([native,sdk])=="已断开", "卡片明确标示断线");
                    Connected(native,true); Connected(sdk,true);
                    var spatial=new[]{new WireEvent {kind="position",entity="source",objectId="s1",time=1},new WireEvent {kind="position",entity="source",objectId="s2",time=1},new WireEvent {kind="position",entity="listener",objectId="l",time=1},new WireEvent {kind="position",entity="distance-listener",objectId="l",time=1}};
                    var discontinuous = spatial.Append(new WireEvent {kind="gap",time=3,seq=10}).ToArray();
                    Check(SpatialPresentation.Group(discontinuous,10).Length==0 && SpatialPresentation.Group(discontinuous,2).Length==2, "缺失后清除空间旧点，历史视野仍按历史边界显示");
                    Check(SpatialPresentation.Group(spatial.Append(new WireEvent {kind="state",entity="capture-segment",time=3}),10).Length==0, "新采集段不沿用之前位置");
                    var spatialGroups=SpatialPresentation.Group(spatial,10);
                    Check(spatialGroups.Length==2 && spatialGroups.Single(g=>g.Entity=="source").Items.Length==2 && SpatialPresentation.Group(spatial,10,true).Length==3, "同点音源和监听点分别聚合，基础监听器显式可选");
                    Check(SpatialPresentation.Group(spatial.Append(new WireEvent {kind="remove",entity="listener",objectId="l",time=2}),10).All(g=>g.Entity=="source"), "销毁监听器清除衰减点，不留假位置");
                    Check(MetricPresentation.Value(new WireEvent { objectId = "stream.bps", value = 1023000 }) == "1.023 Mbit/s", "原生 bit/s 显示 Mbit/s");
                    Check(MetricPresentation.Value(new WireEvent { detail = "bit/s", value = 48000 }) == "48 kbit/s", "小流量显示 kbit/s");
                    Check(MetricPresentation.Value(new WireEvent { objectId = "CpuLoad", value = .5 }) == "0.5 %" && MetricPresentation.Value(new WireEvent { objectId = "AverageServerTime", value = 320 }) == "320 µs", "CPU 与服务耗时单位");
                    Check(MetricPresentation.Value(new WireEvent { name = "memory.atom.bytes", value = 1048576 }) == "1.00 MiB", "内存字节按 MiB 显示");
                    Check(MetricPresentation.Name(new WireEvent { objectId = "stream.used" }) == "流式播放声部（原生）" && MetricPresentation.Name(new WireEvent { name = "voices.streaming.used" }) == "Streaming 声池", "原生声部与 SDK 声池口径分开");
                    Check(MetricPresentation.StreamingPool(new WireEvent { value = 1 }, new WireEvent { value = 16 }) == "1 / 16" && MetricPresentation.StreamingPool(new WireEvent { value = 1 }, null) == "1 / 未提供", "Streaming 池用量容量及缺失容量");
                    await Task.Delay(400);
                    var cards=typeof(MainWindow).GetField("_sessions",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window) as StackPanel;
                    Check(cards!.GetVisualDescendants().OfType<TextBlock>().Any(label=>label.Text?.Contains("● 已断开")==true), "断线状态在客户端卡片中可见");
                    Check(!cards!.GetVisualDescendants().OfType<TextBlock>().Any(label=>label.Text?.Contains("UI TEST FIXTURE ·")==true), "机器名不占用卡片主视区");
                    var timelineTab=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="声音时间线");
                    Check(!timelineTab.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Single().Data!.ToString()!.Contains("L20,12"), "时间线页签不用播放三角");
                    Check(window.WindowDecorations==WindowDecorations.BorderOnly && window.CanResize && window.ShowInTaskbar, "自定义标题栏保留 resize 与任务栏");
                    var maximize=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="最大化 / 还原");
                    Click(maximize); await Task.Delay(120); Check(window.WindowState==WindowState.Maximized,"自定义最大化按钮");
                    Click(maximize); await Task.Delay(120); Check(window.WindowState==WindowState.Normal,"自定义还原按钮");
                    Select(a);
                    Check(ReferenceEquals(Field<Session>("_session"), a) && Field<WireEvent[]>("_snapshot").All(e => e.session == a.Id), "同名会话 A 数据归属");
                    window.ApplyUiAction("filter", "Alpha"); Select(b);
                    Check(string.IsNullOrEmpty(Field<TextBox>("_filter").Text), "首次切换会话不泄露筛选");
                    window.ApplyUiAction("filter", "Beta"); Select(a);
                    Check(Field<TextBox>("_filter").Text == "Alpha", "每会话保留筛选");

                    window.ApplyUiAction("range", "0:5");
                    a.Accept(Event(a, 2, "Alpha-later")); await Task.Delay(650);
                    Check(State().GetProperty("end").GetDouble() == 5 && !State().GetProperty("live").GetBoolean() && Field<WireEvent[]>("_snapshot").Length == 2,
                        "浏览历史固定范围，同时继续接收数据");
                    window.ApplyUiAction("live", "true"); window.ApplyUiAction("select", "1");
                    Check(State().GetProperty("live").GetBoolean() && State().GetProperty("selected").GetInt64() == 1,
                        "选择事件不退出实时且不跳到起点");
                    window.ApplyUiAction("range", "0:9999"); window.ApplyUiAction("live", "true");
                    Check(State().GetProperty("span").GetDouble()==9999 && State().GetProperty("live").GetBoolean(),"返回实时保留用户选择的时间范围");
                    window.ApplyUiAction("control-kind","selector:false");
                    window.ApplyUiAction("spatial-layer","sources:false");
                    window.ApplyUiAction("workspace","控制"); window.ApplyUiAction("workspace","空间");
                    Check(!Field<TimelineControl>("_timeline").ControlKinds.Contains("selector")&&!Field<TimelineControl>("_timeline").ShowSources,"控制快捷筛选与空间图层跨工作区保留");
                    window.ApplyUiAction("control-kind","selector:true"); window.ApplyUiAction("spatial-layer","sources:true");
                    Check(TimelineControl.TimeLabel(3661.25)=="01:01:01.250","长时采集使用时分秒而不是累计分钟");
                    window.ApplyUiAction("workspace","控制");
                    await Task.Delay(150);
                    var axisTimeline=Field<TimelineControl>("_timeline");
                    Check(axisTimeline.PreferWallTime&&axisTimeline.ShowingWallTime&&axisTimeline.ClockTimeAt?.Invoke(a.TimeOrigin) is {} clockPoint&&axisTimeline.DisplayStamp(a.TimeOrigin).Contains(clockPoint.ToLocalTime().ToString("HH:mm:ss")),"有接收锚点时默认展示真实钟表时间");
                    ToggleButton AxisMode() => window.GetVisualDescendants().OfType<ToggleButton>().Single(button=>button.Name=="TimeAxisToggle");
                    Check(AxisMode().IsChecked==true
                        &&AxisMode().GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().Any()
                        &&ToolTip.GetTip(AxisMode())?.ToString()?.StartsWith("当前：钟表时间")==true
                        &&ToolTip.GetTip(AxisMode())?.ToString()?.Contains("相对时间")==true,
                        "时间轴默认钟表时间，使用 SVG 图标切换并以提示说明另一模式");
                    var axisScreenshot=Path.GetFullPath(".local/ui-check/axis-mode-aligned.png");
                    Directory.CreateDirectory(Path.GetDirectoryName(axisScreenshot)!);
                    File.WriteAllBytes(axisScreenshot,window.CapturePng("window"));
                    void CheckAxisLayout(string caseName)
                    {
                        var button=AxisMode();
                        var toolbar=(Grid)button.Parent!;
                        var range=Field<ComboBox>("_windowRange");
                        var axisPosition=button.TranslatePoint(new Point(0,0),window)!.Value;
                        var toolbarPosition=toolbar.TranslatePoint(new Point(0,0),window)!.Value;
                        var rangePosition=range.TranslatePoint(new Point(0,0),window)!.Value;
                        var timelinePosition=Field<TimelineControl>("_timeline").TranslatePoint(new Point(0,0),window)!.Value;
                        Check(button.Bounds.Width==32&&button.Bounds.Height==32
                            &&axisPosition.X>=rangePosition.X+range.Bounds.Width+2
                            &&axisPosition.X+button.Bounds.Width<=toolbarPosition.X+toolbar.Bounds.Width+.5
                            &&axisPosition.Y>=toolbarPosition.Y-.5
                            &&axisPosition.Y+button.Bounds.Height<=toolbarPosition.Y+toolbar.Bounds.Height+.5
                            &&axisPosition.Y+button.Bounds.Height<timelinePosition.Y,
                            $"{caseName}：时间切换按钮完整位于工具栏内，避开范围选择和画布（按钮 {axisPosition}，工具栏 {toolbarPosition}/{toolbar.Bounds.Size}，画布 {timelinePosition}）");
                    }
                    CheckAxisLayout("标准窗口");
                    var inspectorBefore=State().GetProperty("inspector").GetBoolean();
                    if(inspectorBefore) Click(Field<Button>("_detailsToggle"));
                    window.Width=window.MinWidth;await Task.Delay(220);
                    CheckAxisLayout("最窄窗口");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/axis-mode-narrow.png"),window.CapturePng("window"));
                    Click(Field<Button>("_detailsToggle"));await Task.Delay(260);
                    CheckAxisLayout("最窄窗口打开详情");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/axis-mode-narrow-details.png"),window.CapturePng("window"));
                    var categoryIdField=typeof(MainWindow).GetField("_categoryId",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    var categoryNameField=typeof(MainWindow).GetField("_categoryName",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    categoryIdField.SetValue(window,"long-category");
                    categoryNameField.SetValue(window,"Long_Category_Name_abcdefghijklmnopqrstuvwxyz");
                    typeof(MainWindow).GetMethod("UpdateEvents",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    await Task.Delay(100);
                    var chip=Field<Button>("_categoryChip");
                    var filter=Field<TextBox>("_filter");
                    var chipPosition=chip.TranslatePoint(new Point(0,0),window)!.Value;
                    var filterPosition=filter.TranslatePoint(new Point(0,0),window)!.Value;
                    var fit=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()?.StartsWith("一次性缩放")==true);
                    var fitPosition=fit.TranslatePoint(new Point(0,0),window)!.Value;
                    var bar=(Grid)AxisMode().Parent!;
                    var barPosition=bar.TranslatePoint(new Point(0,0),window)!.Value;
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/axis-mode-narrow-category.png"),window.CapturePng("window"));
                    Check(chip.IsVisible&&chip.Bounds.Width<=150
                        &&chip.Content is StackPanel chipContent&&chipContent.Children.OfType<TextBlock>().Last().Text=="×"
                        &&ToolTip.GetTip(chip)?.ToString()?.Contains("Long_Category_Name_abcdefghijklmnopqrstuvwxyz")==true
                        &&chipPosition.X+chip.Bounds.Width+6<=filterPosition.X+.5&&filter.Bounds.Width>=120
                        &&fitPosition.X+fit.Bounds.Width<=barPosition.X+bar.Bounds.Width+.5,
                        $"长 Category 在最窄抽屉窗口可省略，清除键、搜索框和工具栏末端不越界（chip {chipPosition}/{chip.Bounds.Size}，filter {filterPosition}/{filter.Bounds.Size}，fit {fitPosition}/{fit.Bounds.Size}，bar {barPosition}/{bar.Bounds.Size}）");
                    categoryIdField.SetValue(window,"");categoryNameField.SetValue(window,"");
                    typeof(MainWindow).GetMethod("UpdateEvents",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    window.Width=1100;await Task.Delay(220);
                    CheckAxisLayout("中等窗口打开详情");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/axis-mode-medium-details.png"),window.CapturePng("window"));
                    Click(Field<Button>("_detailsToggle"));window.Width=1480;await Task.Delay(220);
                    if(inspectorBefore) Click(Field<Button>("_detailsToggle"));
                    AxisMode().IsChecked=false;
                    Check(!axisTimeline.ShowingWallTime&&axisTimeline.DisplayStamp(a.TimeOrigin)==axisTimeline.Stamp(a.TimeOrigin)
                        &&Field<TextBlock>("_range").Text!.Contains("采集相对时间")
                        &&ToolTip.GetTip(AxisMode())?.ToString()?.StartsWith("当前：相对时间")==true
                        &&ToolTip.GetTip(AxisMode())?.ToString()?.Contains("钟表时间")==true,
                        "时间轴切换到相对时间并更新图标提示");
                    var storedType=typeof(MainWindow).Assembly.GetType("CriScope.App.TimeAxisPreferences")!;
                    var stored=Activator.CreateInstance(storedType,[preferencesPath])!;
                    Check(!((bool)storedType.GetMethod("LoadWallTime")!.Invoke(stored,null)!),"相对时间选择写入持久偏好，重启后可重新读取");
                    window.ApplyUiAction("workspace","播放");axisTimeline=Field<TimelineControl>("_timeline");
                    Check(!axisTimeline.PreferWallTime&&AxisMode().IsChecked==false,"时间轴显示偏好跨工作区保留");
                    AxisMode().IsChecked=true;
                    Check(axisTimeline.ShowingWallTime&&Field<TextBlock>("_range").Text!.Contains("钟表时间")
                        &&ToolTip.GetTip(AxisMode())?.ToString()?.StartsWith("当前：钟表时间")==true,
                        "时间轴可切回钟表时间并更新提示");
                    var noClockAxis=new TimelineControl {TimeOrigin=10,End=20,ClockTimeAt=_=>null};
                    Check(!noClockAxis.ShowingWallTime&&noClockAxis.DisplayStamp(20)==noClockAxis.Stamp(20),"没有钟表锚点时回退相对刻度");
                    var projectionNative=new[]{new WireEvent {kind="request",entity="cue",objectId="pb",time=100,observedTime=10,raw="{\"parameters\":[{\"name\":\"CriAtomExPlaybackId\",\"value\":12}]}"},new WireEvent {kind="metric",time=101,observedTime=11}};
                    var sdkBeat=new WireEvent {kind="beat",objectId="playback:12",time=10.5,observedTime=10.5,seq=7};
                    var projected=ClientTimelineProjection.Combine(projectionNative,[sdkBeat]).Single(e=>e.kind=="beat");
                    Check(projected.parentId=="pb"&&projected.time==100.5&&projected.originalTime==10.5&&projected.estimatedTime&&sdkBeat.time==10.5,"SDK事件按对应实例与时钟锚点投影，原始数据不变");
                    var noMetadata=ClientTimelineProjection.Combine(projectionNative,[new WireEvent {kind="beat",objectId="playback:99",time=10.5}]).Single(e=>e.kind=="beat");
                    Check(noMetadata.parentId==""&&noMetadata.detail.Contains("未关联"),"缺少实例关联不凭Cue名猜测");
                    Check(ClientCardPresentation.Address(new Session(new WireEvent {session="ip",detail="127.0.0.1:7362"}))=="127.0.0.1","客户端卡片显示IP而非临时端口或用户名");
                    Check(Field<TimelineControl>("_timeline").TimeOrigin==a.TimeOrigin,"静态会话切工作区仍保留固定时间起点");
                    var wrongCue=ClientTimelineProjection.Combine(projectionNative,[new WireEvent {kind="cue-info",name="different",objectId="playback:12",time=10.5}]).Single(e=>e.kind=="cue-info");
                    Check(wrongCue.parentId=="","Cue标注时长必须同时匹配实例和Cue名称");
                    var oldAnchor=new WireEvent {kind="play",time=100,observedTime=10,detail="连接时已存在 Voice；起点未知"};
                    Check(ClientTimelineProjection.Combine([oldAnchor],[sdkBeat]).All(e=>e.kind!="beat"),"热接入补发的未知起点不能用作SDK时钟锚点");
                    Check(SpatialPresentation.Group(spatial,10,false,false,true).All(g=>g.Entity!="source")&&SpatialPresentation.Group(spatial,10,false,true,false).All(g=>g.Entity=="source"),"音源与衰减监听点可独立隐藏");
                    var end = State().GetProperty("end").GetDouble();
                    foreach (var workspace in new[] { "播放", "控制", "混音", "空间", "资源" })
                    {
                        window.ApplyUiAction("workspace", workspace);
                        Check(State().GetProperty("live").GetBoolean() && State().GetProperty("selected").GetInt64() == 1 && Field<TextBox>("_filter").Text == "Alpha"
                            && State().GetProperty("end").GetDouble() == end, "工作区 " + workspace + " 保留实时/选择/筛选/范围");
                    }
                    var meterChannels=Enumerable.Range(0,16).Select(ch=>new {channel=ch,peak=ch==3?.8:0d,rms=ch==5?.4:0d}).ToArray();
                    var meterRaw=JsonSerializer.Serialize(new {channels=meterChannels});
                    var mixingSession=Meta(Guid.NewGuid().ToString("N"),"native","mixing");sessions[mixingSession.Id]=mixingSession;
                    for(var bus=0;bus<15;bus++)mixingSession.Accept(new WireEvent {session=mixingSession.Id,seq=bus+1,kind="bus",source="cri-native",entity="bus",objectId="bus"+bus,name="Bus "+bus,time=2.5,raw=meterRaw});
                    var meterBuses=MixingPresentation.Latest(mixingSession.Snapshot(),3);
                    Check(meterBuses.Length==15&&meterBuses.Sum(bus=>bus.Channels.Length)==240&&meterBuses.All(bus=>bus.MaxPeak==.8&&bus.MaxRms==.4),
                        "15 个 Bus 与 240 个原生返回槽位分开计数，概览分别取槽位最大 Peak/RMS");
                    window.ApplyUiAction("filter","");
                    Select(mixingSession);
                    window.ApplyUiAction("workspace","混音");await Task.Delay(150);
                    var mixingTimeline=Field<TimelineControl>("_timeline");
                    var busExpandHits=(List<(Rect rect,string key)>)typeof(TimelineControl).GetField("_expandHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mixingTimeline)!;
                    Check((double)typeof(TimelineControl).GetField("_contentHeight",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mixingTimeline)! == 15*42
                        && busExpandHits.Count(h=>h.key.StartsWith("bus:"))<=15,"默认每个 Bus 只显示一条可展开汇总行");
                    var busExpanded=(HashSet<string>)typeof(TimelineControl).GetField("_expanded",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mixingTimeline)!;
                    busExpanded.Add("bus:bus0");mixingTimeline.InvalidateVisual();await Task.Delay(100);
                    Check((double)typeof(TimelineControl).GetField("_contentHeight",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mixingTimeline)! == 15*42+16*40,
                        "展开单个 Bus 后显示原生返回的 16 个槽位");
                    var selectedBefore=mixingTimeline.Selected;var endBefore=State().GetProperty("end").GetDouble();
                    typeof(TimelineControl).GetMethod("SetMixingScrollFromPointer",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(mixingTimeline,[mixingTimeline.Bounds.Height]);
                    Check((double)typeof(TimelineControl).GetField("_vertical",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(mixingTimeline)!>0
                        && State().GetProperty("live").GetBoolean()&&ReferenceEquals(mixingTimeline.Selected,selectedBefore)&&State().GetProperty("end").GetDouble()==endBefore,
                        "滚动条位置可拖到底，且不改变 Live、时间范围与选中事件");
                    Select(a);
                    window.ApplyUiAction("filter","Alpha");
                    var theme = window.RequestedThemeVariant;
                    window.ApplyUiAction("theme", "toggle");
                    Check(window.RequestedThemeVariant != theme && State().GetProperty("selected").GetInt64() == 1 && State().GetProperty("live").GetBoolean(),
                        "主题语义操作保留上下文");
                    window.ApplyUiAction("diagnostics", "true");
                    Check(State().GetProperty("diagnostics").GetBoolean(), "诊断抽屉语义打开");
                    await Task.Delay(150);
                    Check(window.CapturePng("diagnostics").Length > 200, "诊断面板真实 PNG");
                    window.ApplyUiAction("diagnostics", "false");
                    await Task.Delay(150);
                    bool hiddenRejected = false;
                    try { window.CapturePng("diagnostics"); } catch (InvalidOperationException) { hiddenRejected = true; }
                    Check(hiddenRejected, "隐藏面板明确拒绝截图");

                    window.ApplyUiAction("workspace", "playback"); window.ApplyUiAction("filter", "");
                    await Task.Delay(200);
                    var stateBefore = State().GetRawText(); var png = window.CapturePng();
                    using (var bitmap = new Bitmap(new MemoryStream(png)))
                        Check(bitmap.PixelSize.Width > 900 && bitmap.PixelSize.Height > 500, "完整窗口真实视觉树 PNG 尺寸");
                    Check(window.IsVisible && State().GetRawText() == stateBefore, "截图不关闭窗口、不改变 UI 状态");
                    using (var bitmap = new Bitmap(new MemoryStream(window.CapturePng("workspace"))))
                        Check(bitmap.PixelSize.Width < window.Bounds.Width * window.RenderScaling, "面板截图只包含工作区");
                    var evidence = Path.GetFullPath(".local/ui-check/ui-smoke-v2.png"); Directory.CreateDirectory(Path.GetDirectoryName(evidence)!); File.WriteAllBytes(evidence, png);
                    Console.WriteLine("PNG：" + evidence);
                    bool wrongThreadRejected = await Task.Run(() => { try { window.UiState(); return false; } catch (InvalidOperationException) { return true; } });
                    Check(wrongThreadRejected, "UI 接口拒绝非 Dispatcher 线程");

                    Check(!window.GetVisualDescendants().OfType<Button>().Any(button=>ToolTip.GetTip(button)?.ToString() is "开始录制" or "停止并保存"), "顶部不再提供手动录制开关");
                    var logMenu=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="更多操作");
                    Check(((StackPanel)((Flyout)logMenu.Flyout!).Content!).Children.OfType<Button>().Any(button=>
                        button.Content is StackPanel caption&&caption.Children.OfType<TextBlock>().Any(label=>label.Text=="导入日志…")
                        &&ToolTip.GetTip(button)?.ToString()?.Contains("只读历史会话")==true), "更多操作以导入文案和说明呈现历史日志入口");
                    Check(((StackPanel)((Flyout)logMenu.Flyout!).Content!).Children.OfType<Button>().Any(button=>ToolTip.GetTip(button)?.ToString()=="打开日志目录"), "更多操作保留自动日志目录入口");
                    a.StartAutomaticRecording(directory);a.Accept(Event(a, 3, "Alpha-recorded"));Select(a);
                    Check(a.Recording && !b.Recording && File.Exists(a.RecordingPath) && Field<StackPanel>("_savedPanel").IsVisible
                        && Field<TextBlock>("_savedNotice").Text=="本次日志 · 1 个通道"
                        && ToolTip.GetTip(Field<TextBlock>("_savedNotice"))?.ToString()?.Contains(a.RecordingPath)==true,
                        "自动日志显示简短状态，悬停可查看完整文件路径");
                    a.StopRecording();
                    Check(!a.Recording && File.Exists(a.RecordingPath), "结束后保留可回放的自动日志");
                    var pairNative=Meta(clientId,"native","take-1"); var pairSdk=Meta(clientId,"sdk","take-1");
                    var olderSdk=Meta(clientId,"sdk","take-0");
                    sessions[pairNative.Id]=pairNative; sessions[pairSdk.Id]=pairSdk; sessions[olderSdk.Id]=olderSdk;
                    pairNative.StartAutomaticRecording(directory);pairSdk.StartAutomaticRecording(directory);
                    Select(pairNative);
                    Check(pairNative.Recording && pairSdk.Recording && !olderSdk.Recording, "同次采集的原生与 SDK 通道各有独立自动日志");
                    var late=Meta(clientId,"sdk","take-1"); sessions[late.Id]=late;late.StartAutomaticRecording(directory);await Task.Delay(650);
                    Select(pairSdk); Check(State().GetProperty("recording").GetBoolean(), "切换同卡通道保留整体自动记录状态");
                    Check(Field<TextBlock>("_savedNotice").Text=="本次日志 · 3 个通道"
                        && new[]{pairNative.RecordingPath,pairSdk.RecordingPath,late.RecordingPath}.All(path=>ToolTip.GetTip(Field<TextBlock>("_savedNotice"))?.ToString()?.Contains(path)==true),
                        "同卡多通道日志保留简短状态和全部路径提示");
                    Connected(pairNative,false);pairNative.StopRecording();await Task.Delay(350);
                    Check(!pairNative.Recording && pairSdk.Recording, "单个通道结束后其余通道仍在记录");
                    Connected(pairSdk,false);Connected(late,false);pairSdk.StopRecording();late.StopRecording();await Task.Delay(650);
                    var disconnectedPaths = new[]{pairNative.RecordingPath,pairSdk.RecordingPath,late.RecordingPath};
                    Check(!pairNative.Recording && !pairSdk.Recording && !late.Recording && !State().GetProperty("recording").GetBoolean(), "全部通道断开后退出自动记录状态");
                    foreach(var path in disconnectedPaths) { using var exclusive=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
                    Check(disconnectedPaths.All(File.Exists), "断开自动收尾后独立日志已释放文件句柄");
                    var reconnected=Meta(clientId,"native","take-2"); sessions[reconnected.Id]=reconnected;
                    var lateDisconnected=Meta(clientId,"sdk","take-1"); Connected(lateDisconnected,false); sessions[lateDisconnected.Id]=lateDisconnected;
                    await Task.Delay(650);
                    Check(!reconnected.Recording && !lateDisconnected.Recording && !pairNative.Recording && pairNative.RecordingPath==disconnectedPaths[0], "UI 夹具中重连的新采集不误继承旧日志");
                    Select(a);
                    window.ApplyUiAction("range", "1:3");
                    Check(window.CurrentTimeRange()==(1d,3d), "问题包导出获取当前时间范围");
                    string? problemDescription=null;
                    window.ExportProblemAsync = _ => { problemDescription=window.ProblemDescription;return Task.FromResult(a.RecordingPath); };
                    var moreButton=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="更多操作");
                    var exportButton=((StackPanel)((Flyout)moreButton.Flyout!).Content!).Children.OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="导出问题包…");
                    Click(exportButton);await Task.Delay(150);
                    var exportDialog=desktop.Windows.Single(w=>w!=window);
                    exportDialog.GetVisualDescendants().OfType<TextBox>().Single().Text="测试问题描述";
                    Click(exportDialog.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="导出"));await Task.Delay(150);
                    Check(problemDescription=="测试问题描述"&&desktop.Windows.Count==1,"导出问题包对话框传递描述且关闭对话框");
                    typeof(MainWindow).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [a.RecordingPath]);
                    await Task.Delay(200);
                    Check(Field<Session>("_session").IsReplay && Field<WireEvent[]>("_snapshot").Any(e => e.name == "Alpha-recorded")
                        && !window.GetVisualDescendants().OfType<Button>().Any(button=>ToolTip.GetTip(button)?.ToString()=="开始录制"),
                        "自动日志重新打开后保持只读回放");
                    var followClient=Guid.NewGuid().ToString("N");
                    var oldCapture=Meta(followClient,"sdk","old"); sessions[oldCapture.Id]=oldCapture;
                    Select(oldCapture); window.ApplyUiAction("range","0:5"); Connected(oldCapture,false);
                    var newNative=Meta(followClient,"native","new"); var newSdk=Meta(followClient,"sdk","new");
                    sessions[newNative.Id]=newNative; sessions[newSdk.Id]=newSdk; await Task.Delay(650);
                    Check(ReferenceEquals(Field<Session>("_session"),oldCapture), "浏览历史时新采集接入不自动跳段");
                    window.ApplyUiAction("live","true");
                    Check(ReferenceEquals(Field<Session>("_session"),newNative) && State().GetProperty("live").GetBoolean(), "Live 跟随重连的新 CaptureId 并展示完整客户端");
                    Connected(newSdk,false); Connected(newNative,false);
                    var newestNative=Meta(followClient,"native","newest"); sessions[newestNative.Id]=newestNative; await Task.Delay(650);
                    Check(ReferenceEquals(Field<Session>("_session"),newestNative), "新采集没有同通道时 Live 选择可用通道");
                    var spatialSession=Meta(Guid.NewGuid().ToString("N"),"native","spatial"); sessions[spatialSession.Id]=spatialSession;
                    spatialSession.Accept(new WireEvent {session=spatialSession.Id,seq=1,kind="position",entity="distance-listener",objectId="listener",name="衰减监听点",time=1,x=0,z=3});
                    spatialSession.Accept(new WireEvent {session=spatialSession.Id,seq=2,kind="position",entity="source",objectId="source1",name="测试音源 A",time=1,x=0,z=3});
                    spatialSession.Accept(new WireEvent {session=spatialSession.Id,seq=3,kind="position",entity="source",objectId="source2",name="测试音源 B",time=1,x=0,z=3});
                    Select(spatialSession); window.ApplyUiAction("workspace", "空间");
                    var spatialTimeline=Field<TimelineControl>("_timeline");
                    await Task.Delay(100);
                    var spatialPng=window.CapturePng("workspace");
                    var pointHits=(List<(Rect rect,WireEvent item)>)typeof(TimelineControl).GetField("_hits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(spatialTimeline)!;
                    var expansionHits=(List<(Rect rect,string key)>)typeof(TimelineControl).GetField("_expandHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(spatialTimeline)!;
                    var listenerHit=pointHits.Single(h=>h.item.entity=="distance-listener" && h.rect.Height==46);
                    var spatialHits=(List<(Rect rect,WireEvent? item,string? key)>)typeof(TimelineControl).GetField("_spatialHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(spatialTimeline)!;
                    var sourceHit=spatialHits.Single(h=>h.key?.StartsWith("spatial:")==true && h.rect.Height==46);
                    Check(!listenerHit.rect.Intersects(sourceHit.rect) && listenerHit.rect.Height==46 && sourceHit.rect.Height==46, "同坐标监听点与音源堆叠标注及点击区不重叠");
                    Check(spatialTimeline.Events.All(e=>e.x==0&&e.z==3), "空间标注错位不改变真实坐标");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-same-point.png"),spatialPng);
                    var requestOnly=new WireEvent {kind="request",entity="cue",objectId="request-only",name="g_ui_default",time=1};
                    var requestGroup=PlaybackPresentation.Group([requestOnly],120).Single();
                    Check(requestGroup.VoiceIntervals.Length==0,"只有播放请求时不生成延续到当前的Voice区间");
                    var voiceBegin=new WireEvent {kind="play",entity="voice",objectId="v",parentId="request-only",time=2};
                    var voiceEnd=new WireEvent {kind="stop",entity="voice",objectId="v",parentId="request-only",time=2.483};
                    var shortGroup=PlaybackPresentation.Group([requestOnly,voiceBegin,voiceEnd],120).Single();
                    Check(shortGroup.VoiceIntervals.Single().End?.time==2.483 && shortGroup.End==null,"缺少Playback释放也按真实Voice释放结束声音条");
                    var pendingSession=Meta(Guid.NewGuid().ToString("N"),"native","pending");sessions[pendingSession.Id]=pendingSession;
                    pendingSession.Accept(new WireEvent {session=pendingSession.Id,seq=1,kind="request",entity="cue",objectId="pending",name="g_ui_default",time=1});
                    pendingSession.Accept(new WireEvent {session=pendingSession.Id,seq=2,kind="metric",name="CPU",time=120,value=1});
                    Select(pendingSession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","90:120");
                    await Task.Delay(100);var requestPng=window.CapturePng("workspace");
                    var pendingHits=(List<(Rect rect,WireEvent item)>)typeof(TimelineControl).GetField("_hits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Field<TimelineControl>("_timeline"))!;
                    Check(!pendingHits.Any(h=>h.item.kind=="request" && h.rect.X>=214),"真实绘制不把窗口前的孤立请求画成跨窗口长条");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/request-only.png"),requestPng);
                    var boundarySession=Meta(Guid.NewGuid().ToString("N"),"native","capture-boundary");sessions[boundarySession.Id]=boundarySession;
                    var boundaryRequest=new WireEvent {session=boundarySession.Id,source="cri-native",seq=1,kind="request",entity="cue",objectId="boundary-playback",name="g_ui_default",time=1};
                    var boundaryVoice=new WireEvent {session=boundarySession.Id,source="cri-native",seq=2,kind="play",entity="voice",objectId="boundary-voice",parentId=boundaryRequest.objectId,name="g_ui_default",time=1.1};
                    var captureBoundary=new WireEvent {session=boundarySession.Id,source="cri-native",seq=3,kind="log",entity="capture-segment",name="原生采集开始",time=3};
                    boundarySession.Accept(boundaryRequest);boundarySession.Accept(boundaryVoice);boundarySession.Accept(captureBoundary);
                    boundarySession.Accept(new WireEvent {session=boundarySession.Id,source="cri-native",seq=4,kind="metric",name="CPU",time=20,value=1});
                    var interruptedGroup=PlaybackPresentation.Group(boundarySession.ViewSnapshot(),20).Single();
                    Check(interruptedGroup.HasEvidenceGap&&interruptedGroup.StatusLabel.Contains("状态待确认")&&interruptedGroup.DurationAt(20)==null,
                        "采集重启后旧 Voice 的结束未知，不计算到窗口末端的持续时长");
                    Select(boundarySession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:20");await Task.Delay(100);
                    var boundaryTimeline=Field<TimelineControl>("_timeline");
                    Check(boundaryTimeline.Discontinuities.Contains(captureBoundary),"采集段边界传入时间轴的区间裁切证据");
                    window.CapturePng("workspace");
                    var boundaryHits=(List<(Rect rect,WireEvent item)>)typeof(TimelineControl).GetField("_hits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(boundaryTimeline)!;
                    var voiceBar=boundaryHits.Single(h=>ReferenceEquals(h.item,boundaryVoice));
                    var boundaryX=214+(captureBoundary.time-boundaryTimeline.Start)/boundaryTimeline.ViewSpan*Math.Max(40,boundaryTimeline.Bounds.Width-214-22);
                    Check(Math.Abs(voiceBar.rect.Right-boundaryX)<2,"旧 Voice 的紫色区间在采集段边界截断，不延伸到当前窗口末端");
                    window.ApplyUiAction("range","10:20");window.CapturePng("workspace");
                    Check(!boundaryHits.Any(h=>h.item.objectId==boundaryRequest.objectId||h.item.objectId==boundaryVoice.objectId),
                        "采集中断前的旧播放行不占据后续历史范围");
                    var carry = CreateSession(1003, "Long BGM"); sessions[carry.Id] = carry;
                    carry.Accept(new WireEvent { kind = "aisac", source = "cri-native", session = carry.Id, seq = 2, time = 2, name = "Distance", objectId = "player", entity = "player", value = .25 });
                    carry.Accept(new WireEvent { kind = "metric", source = "cri-native", session = carry.Id, seq = 3, time = 200, name = "CPU", value = 1 });
                    Select(carry);
                    Check(Field<WireEvent[]>("_snapshot").Any(e => e.kind == "play" && e.time == 1) && Field<WireEvent[]>("_snapshot").Any(e => e.kind == "aisac" && e.time == 2),
                        "120 秒历史逐出后 UI 保留真实时间的 BGM 和 AISAC 状态");
                    typeof(MainWindow).GetMethod("Fit", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
                    Check(State().GetProperty("span").GetDouble() < 10, "全览不被 carry 的旧时间拉宽");
                    var clockClient=Guid.NewGuid().ToString("N");var clockNative=Meta(clockClient,"native","clock");var clockSdk=Meta(clockClient,"sdk","clock");
                    sessions[clockNative.Id]=clockNative;sessions[clockSdk.Id]=clockSdk;
                    clockNative.Accept(new WireEvent {session=clockNative.Id,seq=1,kind="metric",time=100,observedTime=10,name="CRI CPU",value=1});
                    var sdkMemory=new WireEvent {session=clockSdk.Id,seq=1,kind="metric",source="cri-sdk",time=10,name="memory.atom.bytes",value=1048576};clockSdk.Accept(sdkMemory);
                    Connected(clockNative,false);Select(clockSdk);Connected(clockNative,true);await Task.Delay(400);
                    Check(ReferenceEquals(Field<Session>("_session"),clockNative),"SDK先接入后原生就绪自动切到同客户端完整视图");
                    window.ApplyUiAction("workspace","资源");
                    Check(Field<TimelineControl>("_timeline").TimeOrigin==100&&Field<TimelineControl>("_timeline").SupplementMetrics.Any(e=>e.name=="memory.atom.bytes"),"无新数据切页保留时间起点和SDK内存指标");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[sdkMemory]);
                    Check(Field<StackPanel>("_details").GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text?.Contains("SDK 独立时钟")==true)&&!Field<StackPanel>("_details").GetVisualDescendants().OfType<Button>().Any(x=>ToolTip.GetTip(x)?.ToString()=="跳至此事件时间"),"SDK指标独立时间不误跳原生时间轴");
                    var inspectSession=Meta(Guid.NewGuid().ToString("N"),"native","inspect");sessions[inspectSession.Id]=inspectSession;
                    var inspectRequest=new WireEvent {session=inspectSession.Id,seq=1,kind="request",entity="cue",objectId="inspect-pb",name="Silent Cue",time=200};
                    inspectSession.Accept(inspectRequest);
                    inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=2,kind="play",entity="voice",objectId="inspect-v",parentId="inspect-pb",time=200.02});
                    inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=3,kind="stop",entity="voice",objectId="inspect-v",parentId="inspect-pb",time=200.5});
                    inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=4,kind="stop",entity="cue",objectId="inspect-pb",time=200.51,endReason="natural"});
                    Select(inspectSession);window.ApplyUiAction("range","199:205");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[inspectRequest]);
                    var detailKind=Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind");
                    var detailName=Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-name");
                    Check(detailKind.Text=="Silent Cue"&&detailName.Text=="播放实例"&&detailKind.ContextMenu!=null
                        &&detailKind.FontSize==16&&detailName.FontSize==11
                        &&!Field<Border>("_inspector").GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="事件详情"),
                        "可复制事件名为抽屉主标题，类型作为次级说明，不重复通用标题");
                    await Task.Delay(220);
                    var kindOrigin=detailKind.TranslatePoint(new Point(),window)!.Value;
                    var nameOrigin=detailName.TranslatePoint(new Point(),window)!.Value;
                    var bodyOrigin=Field<StackPanel>("_details").TranslatePoint(new Point(),window)!.Value;
                    Check(Math.Abs(kindOrigin.X-nameOrigin.X)<2&&Math.Abs(nameOrigin.X-bodyOrigin.X)<2&&nameOrigin.Y>kindOrigin.Y,
                        "抽屉名称、类型和正文共用左边界，类型位于第二行");
                    var endedFacts=Field<StackPanel>("_details").Children.ToList();
                    var endedTimeSection=endedFacts.FindIndex(x=>x.Tag?.ToString()=="section:playback-time");
                    var endedReason=endedFacts.FindIndex(x=>x.Tag?.ToString()=="row:结束原因");
                    var endedDuration=endedFacts.FindIndex(x=>x.Tag?.ToString()=="row:播放历时");
                    Check(endedTimeSection>=0&&endedDuration>endedTimeSection&&endedReason>endedDuration,"结束原因跟随播放时间数据");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/ended-detail.png"),window.CapturePng("window"));
                    window.ApplyUiAction("workspace","控制");
                    Check(Field<StackPanel>("_details").GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text is "播放历时" or "已播放" or "本次观测"),"暂停历史切页后详情仍使用恢复的时间范围，保留播放起止");
                    window.ApplyUiAction("live","true");
                    inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=5,kind="metric",time=210,name="CPU",value=1});
                    await Task.Delay(400);
                    Check((double)typeof(MainWindow).GetField("_lastInspectorEnd",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!>=210,"首次选择后详情会随最新观测刷新");
                    var retainedExpanders=Field<StackPanel>("_details").Children.OfType<Expander>().ToArray();
                    foreach(var expander in retainedExpanders)expander.IsExpanded=true;
                    await Task.Delay(150);
                    var drawerScroll=Field<Border>("_inspector").GetVisualDescendants().OfType<ScrollViewer>().First();
                    drawerScroll.Offset=new Vector(0,100);await Task.Delay(100);var drawerOffset=drawerScroll.Offset;
                    var retainedName=Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind");
                    retainedName.SelectionStart=0;retainedName.SelectionEnd=6;
                    for(int n=0;n<30;n++)
                    {
                        inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=6+n,kind="metric",time=211+n,name="CPU",value=1});
                        window.ApplyUiAction("live","true");
                    }
                    await Task.Delay(150);
                    Check(retainedExpanders.All(x=>x.IsExpanded&&Field<StackPanel>("_details").Children.Contains(x)),"连续30次刷新不重建或关闭抽屉折叠项");
                    Check(drawerScroll.Offset==drawerOffset&&ReferenceEquals(Field<WireEvent>("_selected"),inspectRequest),"实时刷新保留抽屉滚动和选中事件");
                    var refreshedName=Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind");
                    Check(ReferenceEquals(retainedName,refreshedName)&&retainedName.SelectedText=="Silent",
                        $"实时刷新保留顶栏名称选区（同一控件={ReferenceEquals(retainedName,refreshedName)}；当前选区={retainedName.SelectedText}）");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[inspectSession.ViewSnapshot().First(x=>x.seq==2)]);
                    Check(Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind").Text=="Silent Cue",
                        "声部事件没有名称时从同一播放实例显示已记录的 Cue 名");
                    Check(drawerScroll.Offset.Y==0,"切换事件后抽屉回到标题位置");
                    var selectedRows=Field<StackPanel>("_details").Children.ToList();
                    Check(selectedRows.FindIndex(x=>x.Tag?.ToString()=="row:发生时间")>=0&&selectedRows.FindIndex(x=>x.Tag?.ToString()=="row:发生时间")<selectedRows.FindIndex(x=>x.Tag?.ToString()=="row:状态"),"所选事件的发生时间保持在播放概况之前");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v041-drawer-retained.png"),window.CapturePng("window"));
                    var closeDrawer=Field<Border>("_inspector").GetVisualDescendants().OfType<Button>().Single(x=>ToolTip.GetTip(x)?.ToString()=="关闭详情");
                    Click(closeDrawer);
                    Check(!State().GetProperty("inspector").GetBoolean()&&State().GetProperty("live").GetBoolean(),"抽屉自身关闭按钮生效且不停止实时跟随");
                    var detailToggle=Field<Button>("_detailsToggle");
                    Click(detailToggle);Click(detailToggle);Click(detailToggle);
                    await Task.Delay(240);
                    Check(State().GetProperty("inspector").GetBoolean()&&Field<Border>("_inspector") is {IsEnabled:true} openedDrawer&&openedDrawer.Bounds.Width>=371,
                        "快速反复切换后宽屏详情抽屉完整展开");
                    var wideWidth=window.Width;window.Width=1000;await Task.Delay(260);
                    Check(Field<Border>("_inspector").Bounds.Width is >=343 and <=345
                        && Field<Grid>("_body").ColumnDefinitions[0].ActualWidth<1,
                        "窄窗抽屉保持可读宽度并收起侧栏");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/drawer-narrow.png"),window.CapturePng("window"));
                    window.Width=wideWidth;await Task.Delay(260);
                    Click(detailToggle);await Task.Delay(240);
                    Check(!State().GetProperty("inspector").GetBoolean()&&Field<Border>("_inspector") is {IsEnabled:false} hiddenDrawer&&hiddenDrawer.Bounds.Width<=1,
                        "详情抽屉收起后不残留可交互区域");
                    Check(window.GetVisualDescendants().OfType<Button>().Any(x=>x.Content is StackPanel sp&&sp.Children.OfType<TextBlock>().Any(b=>b.Text=="全览")),"缩放按钮恢复全览短文案");
                    var controlsFixture=new[] {
                        new WireEvent {kind="aisac",session="n",seq=1,time=1,objectId="p1",name="Attack",value=0},
                        new WireEvent {kind="aisac",session="n",seq=2,time=2,objectId="p2",name="Attack",value=.05},
                        new WireEvent {kind="aisac",session="n",seq=3,time=3,objectId="p2",name="Attack",value=.1},
                        new WireEvent {kind="request",entity="cue",session="n",seq=4,time=4,objectId="pb1",parentId="p1",name="Same Cue"},
                        new WireEvent {kind="request",entity="cue",session="n",seq=5,time=5,objectId="pb2",parentId="p1",name="Same Cue"},
                        new WireEvent {kind="beat",session="s",seq=6,time=6,objectId="callback-a",parentId="pb1",name="BeatSync",value=120,detail="bar=2; beat=3"},
                        new WireEvent {kind="sequence",session="s",seq=7,time=7,objectId="callback-b",parentId="pb1",name="FirstTag",value=1},
                        new WireEvent {kind="sequence",session="s",seq=8,time=8,objectId="callback-c",parentId="pb1",name="SecondTag",value=2},
                        new WireEvent {kind="beat",session="s",seq=9,time=9,objectId="callback-d",parentId="pb2",name="BeatSync"},
                        new WireEvent {kind="sequence",session="s",seq=10,time=10,objectId="callback-e",name="Unlinked"},
                        new WireEvent {kind="block",entity="control",session="n",seq=11,time=11,objectId="p1",name="请求下一 Block",value=2},
                        new WireEvent {kind="block",session="s",seq=12,time=12,objectId="pb1",parentId="pb1",name="Current block",value=1},
                        new WireEvent {kind="aisac",session="n",seq=13,time=13,objectId="category:8",name="CategoryVolume",value=.5}};
                    var controlLabels=new ControlIdentityLabels();
                    var grouped=ControlPresentation.Group(controlsFixture,20,labels:controlLabels);
                    var attack=grouped.Single(g=>g.Name=="Attack");
                    Check(attack.Rows.Length==2&&attack.Summary.Contains("2 个 Player")&&attack.Rows.All(r=>r.Name.StartsWith("Player #")),"同名AISAC只一个父组，数量与Player编号明确区分");
                    Check(attack.Rows[0].Latest.value==0&&attack.Rows[1].Records.Length==2&&attack.Rows[1].Latest.value==.1,"各Player设置记录和值保持独立，不制造混合参数值");
                    Check(grouped.Count(g=>g.Name.StartsWith("Same Cue"))==2&&grouped.First(g=>g.Name.StartsWith("Same Cue")).Rows.Single(r=>r.Kind=="sequence").Records.Length==2,"同名Cue的两次播放不合并，同实例Sequence标签合为事件类型");
                    Check(grouped.Single(g=>g.Name=="未关联播放实例").Rows.Single().Latest.name=="Unlinked","无明确Playback关联的回调保持未关联");
                    Check(grouped.Single(g=>g.Name.Contains("Block 请求")).Rows.Single().Latest.value==2&&grouped.First(g=>g.Name.StartsWith("Same Cue")).Rows.Single(r=>r.Kind=="block").Latest.value==1,"Player的Block请求与Playback位置采样分开展示");
                    Check(grouped.Single(g=>g.Name=="CategoryVolume").Rows.Single().Name.StartsWith("Category #"),"原生Category作用域不冒充Player");
                    var atWrite=new WireEvent[] {
                        new() {kind="request",entity="cue",session="n",seq=1,time=1,objectId="pb-a",parentId="player",name="Cue A"},
                        new() {kind="aisac",session="n",seq=2,time=2,objectId="player",name="Volume",value=.2},
                        new() {kind="request",entity="cue",session="n",seq=3,time=3,objectId="pb-b",parentId="player",name="Cue B"},
                        new() {kind="aisac",session="n",seq=4,time=3,objectId="player",name="Volume",value=.4},
                        new() {kind="stop",entity="cue",session="n",seq=5,time=3,objectId="pb-a"},
                        new() {kind="aisac",session="n",seq=6,time=3,objectId="player",name="Volume",value=.6},
                        new() {kind="request",entity="cue",session="n",seq=7,time=3,objectId="pb-c",parentId="player",name="Cue C"},
                        new() {kind="aisac",session="n",seq=8,time=4,objectId="category:8",name="CategoryVolume",value=.8},
                        new() {kind="request",entity="cue",session="other",seq=1,time=1,objectId="pb-other",parentId="player",name="Other session"}};
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(atWrite,atWrite[1]).Select(p=>p.Name).SequenceEqual(["Cue A"]),"AISAC 写入时刻不显示后来启动的 Cue");
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(atWrite,atWrite[3]).Select(p=>p.Name).SequenceEqual(["Cue B","Cue A"]),"同 Player 并发时最新实例排前且保留全部");
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(atWrite,atWrite[5]).Select(p=>p.Name).SequenceEqual(["Cue B"]),"同时间戳先结束与后启动按事件序号区分");
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(atWrite,atWrite[7]).Length==0,"Category AISAC 不伪装成 Player 播放关联");
                    var filteredSetting=ControlPresentation.Group(atWrite.Where(e=>e.kind=="aisac"),5,associationEvents:atWrite).Single(g=>g.Name=="Volume").Rows.Single();
                    Check(filteredSetting.SettingOwners.Select(p=>p.Name).SequenceEqual(["Cue B"]),"筛选掉播放事件后 AISAC 仍用完整证据计算写入时归属");
                    var interrupted=new WireEvent[] {atWrite[0],new() {kind="gap",session="n",seq=2,time=1.5},new() {kind="aisac",session="n",seq=3,time=2,objectId="player",name="Volume"}};
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(interrupted,interrupted[2]).Length==0,"证据缺口后不臆测 AISAC 写入时的 Cue");
                    var laterGap=new WireEvent[] {atWrite[0],atWrite[1],new() {kind="gap",session="n",seq=3,time=2}};
                    Check(AssociationPresentation.PlayerPlaybacksAtSetting(laterGap,atWrite[1]).Select(p=>p.Name).SequenceEqual(["Cue A"]),"同时间戳写入后的缺口不反向改写历史归属");
                    var retainedPlayer=attack.Rows[1].Name;
                    Check(ControlPresentation.Group(controlsFixture.Where(e=>e.objectId!="p1"),20,new HashSet<string>{"aisac"},controlLabels).Single(g=>g.Name=="Attack").Rows.Single().Name==retainedPlayer,"过滤和旧记录退出缓存不会重排Player编号");
                    Check(ControlPresentation.Value(controlsFixture[5])=="120 BPM · 小节 2 · 拍 3","Beat摘要展示BPM小节拍数而非SDK原始前缀");
                    var controlSession=Meta(Guid.NewGuid().ToString("N"),"native","grouping");sessions[controlSession.Id]=controlSession;
                    foreach(var item in controlsFixture) {item.session=controlSession.Id;controlSession.Accept(item);}
                    Select(controlSession);window.ApplyUiAction("workspace","控制");window.ApplyUiAction("range","0:20");
                    var groupedTimeline=Field<TimelineControl>("_timeline");
                    var firstGroup=ControlPresentation.Group(groupedTimeline.Events,20,labels:groupedTimeline.ControlLabels).First();
                    typeof(TimelineControl).GetMethod("ToggleExpansion",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(groupedTimeline,[firstGroup.Key]);
                    await Task.Delay(100);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v041-grouping.png"),window.CapturePng("window"));
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[controlsFixture[2]]);
                    Check(Field<StackPanel>("_details").Children.OfType<Expander>().Any(x=>x.Tag?.ToString()=="more-information"&&!x.IsExpanded),"控制次要记录归入统一更多信息");
                    Check(Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind").Text=="Attack"
                        &&Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-name").Text=="AISAC 设置",
                        "AISAC 参数名称为主标题，类型在次级位置呈现");
                    await Task.Delay(150);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v041-history.png"),window.CapturePng("window"));
                    var sharedSetting=new WireEvent {session=controlSession.Id,seq=14,kind="aisac",objectId="p1",name="Shared",time=14,value=1};controlSession.Accept(sharedSetting);
                    window.ApplyUiAction("live","true");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[sharedSetting]);
                    typeof(MainWindow).GetMethod("Inspector",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var ownerButtons=Field<StackPanel>("_details").GetVisualDescendants().OfType<Button>().Where(x=>x.Tag?.ToString()?.StartsWith("owner:")==true).ToArray();
                    Check(ownerButtons.Length==2&&ownerButtons.Select(b=>b.Tag).Distinct().Count()==2,"同名Cue关联按钮按实例身份保留，不复用到另一次播放");
                    Check(ownerButtons.All(b=>b.ContextMenu!=null)&&Field<StackPanel>("_details").GetLogicalDescendants().OfType<Grid>().Where(g=>g.Tag?.ToString()?.StartsWith("owner:")==true).All(g=>g.Children.OfType<SelectableTextBlock>().Any()),"关联的 Cue 名、实例编号及导航入口均可复制");
                    var sharedGroup=ControlPresentation.Group(groupedTimeline.Events,groupedTimeline.End,labels:groupedTimeline.ControlLabels).Single(g=>g.Name=="Shared");
                    var expandedShared=(HashSet<string>)typeof(TimelineControl).GetField("_expanded",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)!;
                    expandedShared.Add(sharedGroup.Key);expandedShared.Add("related:"+sharedGroup.Rows.Single().Key);
                    groupedTimeline.InvalidateVisual();await Task.Delay(120);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/control-related-cue.png"),window.CapturePng("workspace"));
                    var relatedCards=(List<Rect>)typeof(TimelineControl).GetField("_relatedCueBounds",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)!;
                    var controlHits=(List<(Rect rect,WireEvent item)>)typeof(TimelineControl).GetField("_hits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)!;
                    var controlPoints=controlHits.Where(h=>h.item.kind=="aisac"&&h.rect.Width<=12).Select(h=>h.rect).ToArray();
                    var controlChips=(List<(Rect rect,string key)>)typeof(TimelineControl).GetField("_toggleHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)!;
                    Check(relatedCards.Count>0&&controlPoints.Length>0&&relatedCards.All(card=>controlPoints.All(point=>!card.Intersects(point)))
                        &&controlChips.Count>0&&relatedCards.All(card=>controlChips.All(chip=>!card.Intersects(chip.rect))),
                        "AISAC 样本点、关联 Cue 卡片和顶部筛选项占据独立点击区域");
                    var cueLinks=(List<(Rect Rect,WireEvent Event)>)typeof(TimelineControl).GetField("_links",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)!;
                    var hoverLink=typeof(TimelineControl).GetMethod("UpdateLinkHover",BindingFlags.Instance|BindingFlags.NonPublic)!;
                    Check(cueLinks.Count>0&&hoverLink.Invoke(groupedTimeline,[cueLinks[0].Rect.Center]) is WireEvent
                        &&groupedTimeline.Cursor!=null&&typeof(TimelineControl).GetField("_hoverLinkRect",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline) is Rect,
                        "关联 Cue 跳转区域悬停时出现指针反馈");
                    Check(hoverLink.Invoke(groupedTimeline,[new Point(0,0)])==null&&groupedTimeline.Cursor==null
                        &&typeof(TimelineControl).GetField("_hoverLinkRect",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(groupedTimeline)==null,
                        "鼠标移出跳转区域后恢复普通指针");
                    Click(ownerButtons[0]);Check(Field<WireEvent>("_selected").objectId=="pb2","写入时最新实例链接指向正确播放实例");
                    var blockRequest=new WireEvent {kind="block",entity="control",objectId="pb1",name="请求下一 Block",time=15,value=3};
                    var blockGroup=ControlPresentation.Group(controlsFixture.Append(blockRequest),20).First(g=>g.Name.StartsWith("Same Cue"));
                    Check(blockGroup.Rows.Any(r=>r.Name=="请求下一 Block")&&blockGroup.Rows.Any(r=>r.Name=="Block 位置采样"),"Playback级Block请求同样不伪装成位置采样");
                    var navSession=Meta(Guid.NewGuid().ToString("N"),"native","nav");sessions[navSession.Id]=navSession;
                    var navRequest=new WireEvent {session=navSession.Id,seq=1,time=1,kind="request",entity="cue",objectId="2:playback:1:9",parentId="2:p",name="Music Fixture"};
                    var navSource=new WireEvent {session=navSession.Id,seq=3,time=2,kind="position",entity="source",objectId="2:source",epoch=2,name="Music Fixture",x=3,z=5,raw="{\"derived\":{\"links\":[{\"playback\":\"playback:1:9\",\"cue\":\"Music Fixture\"}]}}"};
                    navSource.detail="CRI 音源世界坐标：名称来自原生 Voice/Cue 关联，不是 GameObject 名称。";
                    navRequest.raw="{\"parameters\":[{\"name\":\"Acb Name\",\"value\":\"MusicSheet\"}]}";
                    Check(CueMetadataPresentation.AcbName(navRequest)=="MusicSheet","CueSheet读取所属实例原生ACB名");
                    Check(CueMetadataPresentation.AcbName(new WireEvent{raw="{}"})=="","缺少ACB不猜测CueSheet");
                    var hotAttach=new WireEvent{kind="request",detail="连接时已有播放；起点未知"};
                    Check(EventLogPresentation.Action(hotAttach)=="接入时已在播放"&&!EventLogPresentation.Includes(hotAttach,"请求播放"),"热接入不伪装成新播放请求");
                    navSession.Accept(navRequest);
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=2,time=1.1,kind="play",entity="voice",objectId="2:v",parentId=navRequest.objectId,name="Music Fixture"});navSession.Accept(navSource);
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=4,time=2,kind="category",entity="category",objectId="2:category-index:1",parentId=navRequest.objectId,name="Music"});
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=5,time=3,kind="aisac",objectId="2:p",name="Distance",value=.25});
                    Select(navSession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:3");window.ApplyUiAction("select","1");
                    var beforeNav=State().GetRawText();window.ApplyUiAction("navigate","space");
                    Check(State().GetProperty("workspace").GetString()=="Location"&&Field<WireEvent>("_selected").objectId=="2:source"&&State().GetProperty("end").GetDouble()==3,"播放到空间使用epoch关联并保留历史时刻");
                    var spatialMore=(StackPanel)Field<StackPanel>("_details").Children.OfType<Expander>().Single(x=>x.Tag?.ToString()=="more-information").Content!;
                    Check(spatialMore.Children.OfType<SelectableTextBlock>().Any(x=>x.Tag?.ToString()=="field:详情")&&!spatialMore.Children.OfType<Grid>().Any(x=>x.Tag?.ToString()=="row:详情"),"空间来源说明在更多信息中占满正文宽度");
                    var rawFold=spatialMore.Children.OfType<Expander>().Single(x=>x.Tag?.ToString()=="technical");
                    Check(!rawFold.IsExpanded&&((StackPanel)rawFold.Content!).Children.OfType<Border>().Any(x=>x.Tag?.ToString()=="raw-content"&&x.Child is ScrollViewer scroll&&scroll.MaxHeight==180),"原始内容默认隐藏且展开后有固定阅读高度");
                    var spatialFold=Field<StackPanel>("_details").Children.OfType<Expander>().Single(x=>x.Tag?.ToString()=="more-information");
                    spatialFold.IsExpanded=true;await Task.Delay(150);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/position-more.png"),window.CapturePng("window"));
                    rawFold.IsExpanded=true;await Task.Delay(150);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/position-raw.png"),window.CapturePng("window"));
                    rawFold.IsExpanded=false;
                    spatialFold.IsExpanded=false;
                    window.ApplyUiAction("navigate","playback");
                    Check(State().GetProperty("workspace").GetString()=="Timeline"&&Field<WireEvent>("_selected").objectId==navRequest.objectId,"空间反向定位精确Playback");
                    window.ApplyUiAction("back",null);Check(State().GetProperty("workspace").GetString()=="Location","返回上一位置恢复空间视图");
                    window.ApplyUiAction("back",null);Check(State().GetProperty("workspace").GetString()=="Timeline"&&State().GetProperty("end").GetDouble()==3,"多级返回恢复原时间范围");
                    Check(AssociationPresentation.SourcesFor(navSession.ViewSnapshot(),["1:playback:1:9"],3).Length==0,"空间关系不跨epoch误关联");
                    Check(AssociationPresentation.SourcesFor(navSession.ViewSnapshot(),[navRequest.objectId],1.5).Length==0,"不使用未来位置填历史");
                    Check(AssociationPresentation.Categories(navSession.ViewSnapshot(),navRequest.objectId,3).Single().name=="Music","Category属于具体播放实例");
                    var categoryLabel=Field<StackPanel>("_details").GetLogicalDescendants().OfType<SelectableTextBlock>().First(b=>b.Tag?.ToString()?.StartsWith("category-info:")==true);
                    Check(categoryLabel.Text=="Music"&&categoryLabel.ContextMenu!=null&&State().GetProperty("category").GetString()=="","Category 可选取和右键复制，且不改变筛选");
                    window.ApplyUiAction("workspace","Logs");window.ApplyUiAction("log-search","开始播放 Music");
                    Check(Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Count(i=>i.Tag is WireEvent)==1,"日志支持中文动作与CueName组合搜索");
                    window.ApplyUiAction("log-follow","false");
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=6,time=4,kind="play",entity="voice",objectId="2:v2",parentId=navRequest.objectId,name="Music Fixture"});
                    await Task.Delay(650);
                    Check(Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Count(i=>i.Tag is WireEvent)==1,"暂停日志刷新仍采集且列表保持稳定");
                    window.ApplyUiAction("log-follow","true");
                    Check(Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Count(i=>i.Tag is WireEvent)==1,"同一实例新增Voice不重复生成开始播放日志");
                    window.ApplyUiAction("log-search","");
                    window.ApplyUiAction("theme","light");await Task.Delay(150);
                    var loggedStart=Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Single(i=>i.Tag is WireEvent e&&e.seq==1);
                    Check(((Grid)loggedStart.Content!).Children.OfType<TextBlock>().First().Text==navSession.EstimateWallTime((WireEvent)loggedStart.Tag!)!.Value.ToLocalTime().ToString("HH:mm:ss.fff"),"日志使用会话锚点换算的毫秒钟表时间");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v05-log-fixture.png"),window.CapturePng("window"));
                    Check(ControlPresentation.Group(navSession.ViewSnapshot(),4).Single(g=>g.Name=="Distance").Rows.Single().TargetLabel.Contains("Music Fixture"),"Player子行关联当前Cue名");
                    var noLink=WireEvent.Parse(navSource.ToJson());noLink.raw="{\"derived\":{\"links\":[]}}";noLink.time=5;noLink.seq=7;
                    Check(AssociationPresentation.SourcePlaybacks(navSession.ViewSnapshot().Append(noLink),navSource,5).Length==0,"旧选中点不保留已消失的播放关联");
                    Check(EventLogPresentation.Project(lifecycle).Count(e=>e.kind=="play")==2,"三个Voice归并为两个实例开始");
                    Check(EventLogPresentation.Project(lifecycle,true).Count(e=>e.kind=="play")==3,"声部明细保留全部分配记录");
                    var selectedCard=Field<StackPanel>("_sessions").GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="selected-client");
                    Check(selectedCard.BorderThickness.Left==1,"客户端选中框有实际厚度");
                    window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("select","1");
                    Check(Field<StackPanel>("_details").Children.OfType<Border>().Any(b=>b.Tag?.ToString()=="section:playback-time")&&Field<StackPanel>("_details").Children.OfType<Grid>().Any(g=>g.Tag?.ToString()=="row:已播放"),"播放时间以紧凑属性行呈现");
                    var stopRequest=new WireEvent {kind="stop-request",entity="cue",objectId=navRequest.objectId,time=2,endReason="playback-stop",session=navSession.Id};
                    var stopping=PlaybackPresentation.Group(new[]{navRequest,new WireEvent{kind="play",entity="voice",objectId="vv",parentId=navRequest.objectId,time=1.1},stopRequest},3).Single();
                    Check(stopping.End==null&&stopping.StatusLabel=="停止中"&&stopping.StopRequestedAt==2,"停止请求不提前结束实例");
                    var labelA=SpatialPresentation.PlaceLabel(new Avalonia.Point(80,80),180,new Avalonia.Rect(0,0,500,400),[]);
                    var labelB=SpatialPresentation.PlaceLabel(new Avalonia.Point(80,80),180,new Avalonia.Rect(0,0,500,400),[labelA!.Value]);
                    Check(labelB!=null&&!labelA.Value.Intersects(labelB.Value),"同点标签避让");
                    var manySources=Enumerable.Range(0,30).Select(i=>new WireEvent{kind="position",entity="source",objectId="overlay"+i,time=1,seq=i+1,name="同点声音 "+i}).ToArray();
                    var overlaySession=Meta(Guid.NewGuid().ToString("N"),"native","overlay-test");sessions[overlaySession.Id]=overlaySession;
                    foreach(var item in manySources){item.session=overlaySession.Id;overlaySession.Accept(item);}
                    overlaySession.Accept(new WireEvent{kind="position",entity="distance-listener",objectId="listener",time=1,seq=31,session=overlaySession.Id,name="监听点"});
                    Select(overlaySession);window.ApplyUiAction("workspace","空间");
                    var overlayTimeline=Field<TimelineControl>("_timeline");
                    overlayTimeline.FocusEvent(manySources[0]);overlayTimeline.InvalidateVisual();await Task.Delay(180);
                    window.CapturePng("window");
                    Check(overlayTimeline.SpatialOverlayBounds.Height>0&&overlayTimeline.SpatialOverlayMaxScroll>0,"同点列表限高且支持滚动");
                    Check(ToolTip.GetTip(overlayTimeline)==null,"列表打开没有旧tooltip遮挡");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v06-spatial-overlay.png"),window.CapturePng("window"));
                    var beforeOverlay=((List<(Rect rect,WireEvent? item,string? key)>)typeof(TimelineControl).GetField("_spatialHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlayTimeline)!).Select(x=>x.rect).ToArray();
                    overlayTimeline.CloseSpatialList();window.CapturePng("window");
                    var afterOverlay=((List<(Rect rect,WireEvent? item,string? key)>)typeof(TimelineControl).GetField("_spatialHits",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(overlayTimeline)!).Select(x=>x.rect).ToArray();
                    Check(beforeOverlay.SequenceEqual(afterOverlay),"空间列表开关不改变画布对象位置");
                    Check(overlayTimeline.ExpandedKeys.All(k=>!k.StartsWith("spatial:")),"空间列表可独立关闭");
                    Check(EventLogPresentation.Includes(new WireEvent{kind="stop-request"},"请求停止")&&!EventLogPresentation.Includes(new WireEvent{kind="play"},"请求停止"),"停止请求可单独筛选");
                    Check(!EventLogPresentation.Includes(new WireEvent{kind="stop-request"},"播放结束"),"结束筛选不混入停止请求");
                    Check(!EventLogPresentation.Includes(new WireEvent{kind="log",name="SoundVoice_Volume"},"异常与连接")&&EventLogPresentation.Includes(new WireEvent{kind="error"},"异常与连接"),"异常分类不混入原始协议");
                    Check(EventLogPresentation.Includes(new WireEvent{kind="log",name="SoundVoice_Volume"},"原始协议（高级）")&&!EventLogPresentation.Includes(new WireEvent{kind="log",name="SoundVoice_Volume"},"全部"),"原始协议只在高级分类显示");
                    Check(AssociationPresentation.CueCategories(new WireEvent{raw="{\"basis\":\"cue-config\",\"categories\":[\"Music\",\"Music\",\"Volume_Music\"]}"}).SequenceEqual(new[]{"Music","Volume_Music"}),"Cue配置分类去重并保留顺序");
                    navSession.Accept(new WireEvent{session=navSession.Id,seq=20,time=3,kind="cue-info",name="Music Fixture",objectId=navRequest.objectId,parentId=navRequest.objectId,value=89846});
                    Select(navSession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:4");window.ApplyUiAction("select","1");
                    await Task.Delay(350);
                    var drawer=Field<StackPanel>("_details");
                    var timeSection=drawer.Children.ToList().FindIndex(x=>x.Tag?.ToString()=="section:playback-time");
                    var elapsedRow=drawer.Children.ToList().FindIndex(x=>x.Tag?.ToString()=="row:已播放");
                    var cueSection=drawer.Children.ToList().FindIndex(x=>x.Tag?.ToString()=="section:cue-info");
                    var cueDuration=drawer.Children.OfType<Grid>().Single(x=>x.Tag?.ToString()=="row:Cue 时长");
                    Check(timeSection>=0&&timeSection<elapsedRow&&elapsedRow<cueSection&&cueSection<drawer.Children.IndexOf(cueDuration)&&cueDuration.Children.OfType<SelectableTextBlock>().Single().FontSize==13,"观测历时先于 Cue 配置信息，Cue 时长不再放大");
                    Check(drawer.Children.OfType<Expander>().Count()==1 && drawer.Children.OfType<Expander>().Single().Header?.ToString()=="更多信息","详情仅有一个更多信息折叠");
                    var cueText=Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Single(x=>x.Tag?.ToString()=="inspector-kind");
                    Check(cueText.Text=="Music Fixture"&&cueText.ContextMenu!=null,"顶栏 Cue 名称可选择并复制完整名称");
                    Check(drawer.GetLogicalDescendants().OfType<TextBlock>().Where(x=>!string.IsNullOrWhiteSpace(x.Text)).All(x=>x.ContextMenu!=null),"抽屉正文、属性标签与高级记录的每段文字均可右键复制");
                    Check(!drawer.Children.Any(x=>x.Tag?.ToString() is "inspector-title" or "playback-heading"),"Cue 名称和实例编号在正文不重复出现");
                    Check(drawer.Children.OfType<Grid>().Where(x=>x.Tag?.ToString()?.StartsWith("row:")==true).All(x=>x.Children.OfType<SelectableTextBlock>().Any()),"所有属性值均可选取");
                    var sheetText=drawer.Children.OfType<StackPanel>().Single(x=>x.Tag?.ToString()=="cue-sheet").Children.OfType<SelectableTextBlock>().Single();
                    Check(sheetText.Text=="MusicSheet" && sheetText.ContextMenu!=null,"CueSheet 名称支持独立复制");
                    sheetText.SelectionStart=0;sheetText.SelectionEnd=5;
                    typeof(MainWindow).GetMethod("Inspector",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var keptSheet=Field<StackPanel>("_details").Children.OfType<StackPanel>().Single(x=>x.Tag?.ToString()=="cue-sheet").Children.OfType<SelectableTextBlock>().Single();
                    Check(ReferenceEquals(sheetText,keptSheet)&&keptSheet.SelectedText=="Music","实时更新保留 CueSheet 选区");
                    var categoryGrid=drawer.Children.OfType<Grid>().FirstOrDefault(x=>x.Tag?.ToString()=="categories");
                    Check(categoryGrid==null||categoryGrid.Children[0].VerticalAlignment==Avalonia.Layout.VerticalAlignment.Top,"Category 标签与首行对齐");
                    var morePanel=(StackPanel)drawer.Children.OfType<Expander>().Single().Content!;
                    Check(morePanel.Children.OfType<Expander>().Count()==1&&morePanel.Children.OfType<Expander>().Single().Tag?.ToString()=="technical"&&!morePanel.Children.OfType<Expander>().Single().IsExpanded,"原始记录使用独立的高级入口，默认收起");
                    Check(drawer.Children.OfType<StackPanel>().Any(p=>p.Tag?.ToString()=="inspector-links"),"所有主要定位集中在同一区域");
                    Check(!drawer.Children.OfType<Grid>().Any(g=>g.Tag?.ToString() is "row:事件发生于" or "row:结束原因"),"播放中无重复时间及尚未结束行");
                    Check(!window.GetVisualDescendants().OfType<Button>().Any(b=>ToolTip.GetTip(b)?.ToString()=="日志面板"),"日志只有一个常驻入口");
                    Check(Field<Border>("_inspector").GetVisualDescendants().OfType<SelectableTextBlock>().Any(x=>x.Tag?.ToString()=="inspector-instance"&&x.ContextMenu!=null),"实例编号在固定顶栏可选取复制");
                    Check(!drawer.Children.OfType<Grid>().Any(g=>g.Tag?.ToString()=="row:结束播放"),"播放中不显示红色空结束行");
                    foreach(var testTheme in new[]{"dark","light"}) {
                        window.ApplyUiAction("theme",testTheme);await Task.Delay(180);
                        var fold=Field<StackPanel>("_details").GetVisualDescendants().OfType<Expander>().First(f=>f.Tag?.ToString()=="more-information");
                        var heading=fold.GetVisualDescendants().OfType<Button>().First();
                        Check(heading.ContextMenu!=null,"折叠标题可右键复制："+testTheme);
                        Click(heading);await Task.Delay(650);
                        Check(fold.IsExpanded&&((Avalonia.Media.RotateTransform)fold.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First().RenderTransform!).Angle==90,"折叠展开经过刷新箭头同步："+testTheme);
                        var primaryValue=Field<StackPanel>("_details").Children.OfType<Grid>().Single(g=>g.Tag?.ToString()=="row:状态").Children.OfType<SelectableTextBlock>().Single();
                        var secondaryValue=((StackPanel)fold.Content!).Children.OfType<Grid>().Single(g=>g.Tag?.ToString()=="row:请求播放").Children.OfType<SelectableTextBlock>().Single();
                        Check(Math.Abs(primaryValue.TranslatePoint(new Point(),window)!.Value.X-secondaryValue.TranslatePoint(new Point(),window)!.Value.X)<2,
                            "更多信息展开后的属性值与一级字段同列："+testTheme);
                        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v11-detail-"+testTheme+".png"),window.CapturePng("window"));
                        Click(heading);await Task.Delay(350);
                        Check(!fold.IsExpanded&&((Avalonia.Media.RotateTransform)fold.GetVisualDescendants().OfType<Avalonia.Controls.Shapes.Path>().First().RenderTransform!).Angle==0,"折叠收起经过刷新箭头同步："+testTheme);
                    }
                    window.ApplyUiAction("theme","dark");
                    // Historical event details must remain tied to the selected event, not live End.
                    window.ApplyUiAction("workspace","Logs");window.ApplyUiAction("select","2");await Task.Delay(650);
                    var eventDrawer=Field<StackPanel>("_details");
                    Check(eventDrawer.Children.OfType<Grid>().Any(g=>g.Tag?.ToString()=="row:发生时间"),"日志选中事件保留自身发生时间");
                    Check(Field<ListBox>("_events").SelectedItem is ListBoxItem {Tag:WireEvent selectedLog} && selectedLog.seq==2,"日志高亮与抽屉事件保持一致");
                    var eventMore=(StackPanel)eventDrawer.Children.OfType<Expander>().Single().Content!;
                    var historicalDuration=eventMore.Children.OfType<Grid>().First(g=>g.Tag?.ToString()=="row:已播放").Children.OfType<SelectableTextBlock>().Single().Text;
                    Check(historicalDuration=="0.000 秒","历史开始事件详情不随实时播放累计");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v11-event-detail.png"),window.CapturePng("window"));
                    for(int i=0;i<12;i++)navSession.Accept(new WireEvent{session=navSession.Id,seq=30+i,time=3+i*.01,kind="aisac",objectId="2:p",name="Distance",value=i});
                    window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:4");window.ApplyUiAction("select","1");await Task.Delay(350);
                    var preview=(StackPanel)Field<StackPanel>("_details").Children.OfType<Expander>().Single().Content!;
                    Check(preview.Children.OfType<Grid>().Count(g=>g.Children.OfType<Button>().Any())<=3,"大量关联设置只预览最近三条");
                    navSession.Accept(new WireEvent{session=navSession.Id,seq=99,time=3.5,kind="aisac",objectId="unrelated-player",name="Unrelated",value=5});
                    typeof(MainWindow).GetMethod("ShowRelatedLog",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[navRequest.objectId]);await Task.Delay(350);
                    Check(Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Count(i=>i.Tag is WireEvent e && e.kind=="aisac")>=12,"全部关联记录包含Player上的AISAC设置");
                    var settingRow=Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().First(i=>i.Tag is WireEvent e&&e.kind=="aisac");
                    Check(((Grid)settingRow.Content!).Children.OfType<TextBlock>().Any(t=>Grid.GetColumn(t)==2&&t.Text!.StartsWith("Player #")),"AISAC 日志显示作用 Player 而不是参数名称");
                    var kindMenu=window.GetVisualDescendants().OfType<ComboBox>().First(x=>x.ItemsSource?.Cast<object>().Contains("播放相关（全部）")==true);
                    Check(kindMenu.ItemTemplate!=null,"日志筛选共享 SVG 颜色模板");
                    kindMenu.IsDropDownOpen=true;await Task.Delay(180);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v11-filter.png"),window.CapturePng("window"));kindMenu.IsDropDownOpen=false;

                    Check(!Field<ListBox>("_events").ItemsSource!.Cast<ListBoxItem>().Any(i=>i.Tag is WireEvent e && e.name=="Unrelated"),"关联日志不混入其他Player的设置");
                    // Long names and two same-name instances must preserve separate navigation targets.
                    foreach(var request in controlSession.ViewSnapshot().Where(e=>e.kind=="request"))request.name="Long_Music_Cue_同名并发实例_abcdefghijklmnopqrstuvwxyz";
                    Select(controlSession);window.ApplyUiAction("workspace","AISAC");window.ApplyUiAction("filter","");window.ApplyUiAction("range","0:20");
                    var compactTimeline=Field<TimelineControl>("_timeline");
                    var controlRows=ControlPresentation.Group(Field<WireEvent[]>("_snapshot"),20,labels:compactTimeline.ControlLabels);
                    compactTimeline.ExpandedKeys=controlRows.Select(g=>g.Key).Concat(controlRows.SelectMany(g=>g.Rows).Select(r=>"related:"+r.Key)).ToArray();
                    window.Width=1000;window.Height=740;compactTimeline.InvalidateVisual();await Task.Delay(250);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v11-multi-narrow.png"),window.CapturePng("window"));
                    Check(compactTimeline.Bounds.Width>300,"窄窗口保留控制工作区");
                    window.Width=1480;window.Height=900;await Task.Delay(150);
                    var resourceSession=Meta(Guid.NewGuid().ToString("N"),"native","resource-test");sessions[resourceSession.Id]=resourceSession;
                    resourceSession.Accept(new WireEvent{session=resourceSession.Id,seq=1,time=1,kind="metric",name="CRI CPU",objectId="CpuLoad",value=1,detail="%"});
                    resourceSession.Accept(new WireEvent{session=resourceSession.Id,seq=2,time=2,kind="metric",name="CRI CPU",objectId="CpuLoad",value=2,detail="%"});
                    resourceSession.Accept(new WireEvent{session=resourceSession.Id,seq=3,time=2,kind="metric",name="流式声池容量",entity="voice-pool",objectId="pool",value=65552});
                    Select(resourceSession);window.ApplyUiAction("workspace","Performance");window.ApplyUiAction("range","0:3");
                    var resourceTimeline=Field<TimelineControl>("_timeline");resourceTimeline.ExpandedKeys=["technical-metrics","pool-config","metric:CRI CPUCpuLoad"];
                    resourceTimeline.InvalidateVisual();await Task.Delay(200);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v08-resources.png"),window.CapturePng("window"));
                    var unknownSession=Meta(Guid.NewGuid().ToString("N"),"native","unknown-start");sessions[unknownSession.Id]=unknownSession;
                    var unknownRequest=new WireEvent {session=unknownSession.Id,seq=1,time=1,kind="request",entity="cue",objectId="hot-pb",name="热接入声音",detail="连接时已有播放；起点未知"};
                    unknownSession.Accept(unknownRequest);
                    unknownSession.Accept(new WireEvent {session=unknownSession.Id,seq=2,time=1.1,kind="play",entity="voice",objectId="hot-voice",parentId="hot-pb",name="热接入声音",detail="连接时已存在 Voice；起点未知"});
                    Select(unknownSession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:3");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[unknownRequest]);
                    var unknownStart=Field<StackPanel>("_details").Children.OfType<Grid>().Single(x=>x.Tag?.ToString()=="row:开始播放");
                    var palette=Field<object>("_p");var good=palette.GetType().GetProperty("Good")!.GetValue(palette)!.ToString();
                    Check(unknownStart.Children.OfType<SelectableTextBlock>().Single() is {Text:"开始发生在记录之前"} startValue&&startValue.Foreground?.ToString()==good&&unknownStart.Children.OfType<TextBlock>().First(x=>x.Text=="开始播放").Foreground?.ToString()==good,"起点未知但有播放证据时开始标签和值仍使用绿色");
                    await Task.Delay(400);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/unknown-start.png"),window.CapturePng("window"));
                    var categoryVisual=Meta(Guid.NewGuid().ToString("N"),"native","category-visual");sessions[categoryVisual.Id]=categoryVisual;
                    var categoryCases=new (string Id,string Name,double At,string[] Categories)[] {
                        ("single","UI_Click",1,["SFX_UI"]),
                        ("multi","Music_Loop",2,["Music","Volume_Music","Trigger_Music"]),
                        ("unknown","Unclassified_Cue",3,[])
                    };
                    int categorySeq=0;
                    foreach(var item in categoryCases)
                    {
                        var playbackId="visual-"+item.Id;var voiceId="visual-voice-"+item.Id;
                        categoryVisual.Accept(new WireEvent {session=categoryVisual.Id,seq=++categorySeq,time=item.At,kind="request",entity="cue",objectId=playbackId,name=item.Name});
                        categoryVisual.Accept(new WireEvent {session=categoryVisual.Id,seq=++categorySeq,time=item.At+.02,kind="play",entity="voice",objectId=voiceId,parentId=playbackId,name=item.Name});
                        for(int index=0;index<item.Categories.Length;index++)
                            categoryVisual.Accept(new WireEvent {session=categoryVisual.Id,seq=++categorySeq,time=item.At+.03+index*.001,kind="category",entity="category",objectId="category-index:"+(Array.IndexOf(new[]{"SFX_UI","Music","Volume_Music","Trigger_Music"},item.Categories[index])+1),parentId=playbackId,name=item.Categories[index]});
                        categoryVisual.Accept(new WireEvent {session=categoryVisual.Id,seq=++categorySeq,time=item.At+.38,kind="stop",entity="voice",objectId=voiceId,parentId=playbackId,name=item.Name});
                        categoryVisual.Accept(new WireEvent {session=categoryVisual.Id,seq=++categorySeq,time=item.At+.4,kind="stop",entity="cue",lifecycle="stopped",objectId=playbackId,name=item.Name});
                    }
                    Select(categoryVisual);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:4");window.ApplyUiAction("filter","");await Task.Delay(200);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/category-tracks-dark.png"),window.CapturePng("window"));
                    var categoryTips=(List<(Rect rect,string tip)>)typeof(TimelineControl).GetField("_playbackTips",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(Field<TimelineControl>("_timeline"))!;
                    Check(categoryTips.Any(t=>t.rect.X<=5&&t.tip.Contains("Category：SFX_UI"))
                        &&categoryTips.Any(t=>new[]{"Music","Volume_Music","Trigger_Music"}.All(t.tip.Contains))
                        &&categoryTips.Any(t=>t.tip.Contains("Category：实例归属未记录")),
                        "单分类、多分类和未知归属的色标区域都有准确说明");
                    window.Width=980;await Task.Delay(250);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/category-tracks-narrow.png"),window.CapturePng("window"));
                    window.Width=1480;window.ApplyUiAction("theme","light");await Task.Delay(250);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/category-tracks-light.png"),window.CapturePng("window"));
                    Console.WriteLine($"结果：{passed}/{passed} UI 检查通过"); desktop.Shutdown(0);
                }
                catch (Exception ex) { Console.Error.WriteLine($"FAIL：已通过 {passed} 项；{ex}"); desktop.Shutdown(1); }
                finally { collector.Dispose(); if (Directory.Exists(directory)) Directory.Delete(directory, true); }
                T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                JsonElement State() => JsonSerializer.SerializeToElement(window.UiState());
                void Select(Session target) => window.ApplyUiAction("session", target.Id);
                void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS：" + name); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }
    static void Click(Button button)
    { if (!button.IsEnabled) throw new InvalidOperationException("不能点击禁用按钮"); button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); }
    static Session CreateSession(int pid, string name)
    {
        var hello = new WireEvent { kind = "hello", name = "UI TEST FIXTURE", source = "cri-native", session = Guid.NewGuid().ToString("N"), pid = pid, platform = "Windows", value = 1 };
        var session = new Session(hello); session.Accept(hello); session.Accept(Event(session, 1, name)); return session;
    }
    static WireEvent Event(Session s, long sequence, string name) => new() { kind = "play", entity = "voice", source = "cri-native", session = s.Id, seq = sequence, time = sequence, name = name, objectId = "test-object" };
}
