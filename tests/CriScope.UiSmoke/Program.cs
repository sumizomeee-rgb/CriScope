using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
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
            var window = new MainWindow(collector); desktop.MainWindow = window;
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
                    Check(State().GetProperty("span").GetDouble()==30 && State().GetProperty("live").GetBoolean(),"返回实时恢复30秒，不沿用巨大历史范围");
                    window.ApplyUiAction("control-kind","selector:false");
                    window.ApplyUiAction("spatial-layer","sources:false");
                    window.ApplyUiAction("workspace","控制"); window.ApplyUiAction("workspace","空间");
                    Check(!Field<TimelineControl>("_timeline").ControlKinds.Contains("selector")&&!Field<TimelineControl>("_timeline").ShowSources,"控制快捷筛选与空间图层跨工作区保留");
                    window.ApplyUiAction("control-kind","selector:true"); window.ApplyUiAction("spatial-layer","sources:true");
                    Check(TimelineControl.TimeLabel(3661.25)=="01:01:01.250","长时采集使用时分秒而不是累计分钟");
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

                    Click(Field<Button>("_record")); Check(a.Recording && !b.Recording, "录制只启动当前会话 A");
                    a.Accept(Event(a, 3, "Alpha-recorded")); Select(b); Click(Field<Button>("_record"));
                    Check(a.Recording && b.Recording, "会话 B 独立录制且 A 继续");
                    b.Accept(Event(b, 2, "Beta-recorded")); Click(Field<Button>("_record"));
                    Check(a.Recording && !b.Recording, "停止 B 不影响 A"); Select(a); Click(Field<Button>("_record"));
                    Check(!a.Recording && File.Exists(a.RecordingPath), "停止 A 完成录制文件");
                    Check(Field<StackPanel>("_savedPanel").IsVisible && Field<TextBlock>("_savedNotice").Text!.Contains(a.RecordingPath), "停止并保存明确显示日志路径");
                    var pairNative=Meta(clientId,"native","take-1"); var pairSdk=Meta(clientId,"sdk","take-1");
                    var olderSdk=Meta(clientId,"sdk","take-0");
                    sessions[pairNative.Id]=pairNative; sessions[pairSdk.Id]=pairSdk; sessions[olderSdk.Id]=olderSdk;
                    Select(pairNative); Click(Field<Button>("_record"));
                    Check(pairNative.Recording && pairSdk.Recording && !olderSdk.Recording, "一次开始同时录制本次采集双通道，不跨 CaptureId");
                    var late=Meta(clientId,"sdk","take-1"); sessions[late.Id]=late; await Task.Delay(650);
                    Check(late.Recording, "同次采集稍晚接入的通道自动加入日志记录");
                    Select(pairSdk); Check(State().GetProperty("recording").GetBoolean(), "切换同卡通道保留整体录制状态");
                    Click(Field<Button>("_record"));
                    Check(!pairNative.Recording && !pairSdk.Recording && !late.Recording && new[]{pairNative.RecordingPath,pairSdk.RecordingPath,late.RecordingPath}.Distinct().Count()==3 && File.Exists(pairNative.RecordingPath) && File.Exists(pairSdk.RecordingPath), "任一通道停止全部，同次采集保留独立日志文件");
                    Check(Field<TextBlock>("_savedNotice").Text!.Contains("3 个通道日志"), "多通道保存反馈明确文件数");
                    Click(Field<Button>("_record"));
                    Connected(pairNative,false); await Task.Delay(350);
                    Check(pairNative.Recording && pairSdk.Recording, "单个通道断开时仍保留本次采集记录");
                    Connected(pairSdk,false); Connected(late,false); await Task.Delay(650);
                    var disconnectedPaths = new[]{pairNative.RecordingPath,pairSdk.RecordingPath,late.RecordingPath};
                    Check(!pairNative.Recording && !pairSdk.Recording && !late.Recording && !State().GetProperty("recording").GetBoolean() && Field<StackPanel>("_savedPanel").IsVisible, "全部通道断开自动保存并退出录制状态");
                    foreach(var path in disconnectedPaths) { using var exclusive=File.Open(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None); }
                    Check(disconnectedPaths.All(File.Exists), "断开自动收尾后独立日志已释放文件句柄");
                    var reconnected=Meta(clientId,"native","take-2"); sessions[reconnected.Id]=reconnected;
                    var lateDisconnected=Meta(clientId,"sdk","take-1"); Connected(lateDisconnected,false); sessions[lateDisconnected.Id]=lateDisconnected;
                    await Task.Delay(650);
                    Check(!reconnected.Recording && !lateDisconnected.Recording && !pairNative.Recording && pairNative.RecordingPath==disconnectedPaths[0], "重连的新采集与旧断开通道不自动重开已结束日志");
                    Select(a);
                    window.ApplyUiAction("range", "1:3");
                    Check(window.CurrentTimeRange()==(1d,3d), "问题包导出获取当前时间范围");
                    string? problemDescription=null;
                    window.ExportProblemAsync = _ => { problemDescription=window.ProblemDescription;return Task.FromResult(a.RecordingPath); };
                    var exportButton=window.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="导出问题包");
                    Click(exportButton);await Task.Delay(150);
                    var exportDialog=desktop.Windows.Single(w=>w!=window);
                    exportDialog.GetVisualDescendants().OfType<TextBox>().Single().Text="测试问题描述";
                    Click(exportDialog.GetVisualDescendants().OfType<Button>().Single(button=>ToolTip.GetTip(button)?.ToString()=="导出"));await Task.Delay(150);
                    Check(problemDescription=="测试问题描述"&&desktop.Windows.Count==1,"导出问题包对话框传递描述且关闭对话框");
                    typeof(MainWindow).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [a.RecordingPath]);
                    await Task.Delay(200);
                    Check(Field<Session>("_session").IsReplay && Field<WireEvent[]>("_snapshot").Any(e => e.name == "Alpha-recorded") && !Field<Button>("_record").IsEnabled,
                        "真实录制重新打开且回放禁止重复录制");
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
                    Select(inspectSession);window.ApplyUiAction("range","199:205");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[inspectRequest]);
                    window.ApplyUiAction("workspace","控制");
                    Check(Field<StackPanel>("_details").GetVisualDescendants().OfType<TextBlock>().Any(x=>x.Text=="播放历时"),"暂停历史切页后详情仍使用恢复的时间范围，保留播放起止");
                    window.ApplyUiAction("live","true");
                    inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=4,kind="metric",time=210,name="CPU",value=1});
                    await Task.Delay(400);
                    Check((double)typeof(MainWindow).GetField("_lastInspectorEnd",BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!>=210,"首次选择后详情会随最新观测刷新");
                    var retainedExpanders=Field<StackPanel>("_details").Children.OfType<Expander>().ToArray();
                    foreach(var expander in retainedExpanders)expander.IsExpanded=true;
                    await Task.Delay(150);
                    var drawerScroll=Field<Border>("_inspector").GetVisualDescendants().OfType<ScrollViewer>().First();
                    drawerScroll.Offset=new Vector(0,100);await Task.Delay(100);var drawerOffset=drawerScroll.Offset;
                    for(int n=0;n<30;n++)
                    {
                        inspectSession.Accept(new WireEvent {session=inspectSession.Id,seq=5+n,kind="metric",time=211+n,name="CPU",value=1});
                        window.ApplyUiAction("live","true");
                    }
                    await Task.Delay(150);
                    Check(retainedExpanders.All(x=>x.IsExpanded&&Field<StackPanel>("_details").Children.Contains(x)),"连续30次刷新不重建或关闭抽屉折叠项");
                    Check(drawerScroll.Offset==drawerOffset&&ReferenceEquals(Field<WireEvent>("_selected"),inspectRequest),"实时刷新保留抽屉滚动和选中事件");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v041-drawer-retained.png"),window.CapturePng("window"));
                    var closeDrawer=Field<Border>("_inspector").GetVisualDescendants().OfType<Button>().Single(x=>ToolTip.GetTip(x)?.ToString()=="关闭详情");
                    Click(closeDrawer);
                    Check(!State().GetProperty("inspector").GetBoolean()&&State().GetProperty("live").GetBoolean(),"抽屉自身关闭按钮生效且不停止实时跟随");
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
                    Check(Field<StackPanel>("_details").Children.OfType<Expander>().Any(x=>x.Tag?.ToString()=="history"&&x.IsExpanded),"点击控制子行在抽屉直接显示设置记录");
                    await Task.Delay(150);
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v041-history.png"),window.CapturePng("window"));
                    var sharedSetting=new WireEvent {session=controlSession.Id,seq=14,kind="aisac",objectId="p1",name="Shared",time=14,value=1};controlSession.Accept(sharedSetting);
                    window.ApplyUiAction("live","true");
                    typeof(MainWindow).GetMethod("SelectEvent",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,[sharedSetting]);
                    typeof(MainWindow).GetMethod("Inspector",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(window,null);
                    var ownerButtons=Field<StackPanel>("_details").Children.OfType<Button>().Where(x=>x.Tag?.ToString()?.StartsWith("owner:")==true).ToArray();
                    Check(ownerButtons.Length==2&&ownerButtons.Select(b=>b.Tag).Distinct().Count()==2,"同名Cue关联按钮按实例身份保留，不复用到另一次播放");
                    Click(ownerButtons[1]);Check(Field<WireEvent>("_selected").objectId=="pb2","刷新后的同名关联链接仍指向正确实例");
                    var blockRequest=new WireEvent {kind="block",entity="control",objectId="pb1",name="请求下一 Block",time=15,value=3};
                    var blockGroup=ControlPresentation.Group(controlsFixture.Append(blockRequest),20).First(g=>g.Name.StartsWith("Same Cue"));
                    Check(blockGroup.Rows.Any(r=>r.Name=="请求下一 Block")&&blockGroup.Rows.Any(r=>r.Name=="Block 位置采样"),"Playback级Block请求同样不伪装成位置采样");
                    var navSession=Meta(Guid.NewGuid().ToString("N"),"native","nav");sessions[navSession.Id]=navSession;
                    var navRequest=new WireEvent {session=navSession.Id,seq=1,time=1,kind="request",entity="cue",objectId="2:playback:1:9",parentId="2:p",name="Music Fixture"};
                    var navSource=new WireEvent {session=navSession.Id,seq=3,time=2,kind="position",entity="source",objectId="2:source",epoch=2,name="Music Fixture",x=3,z=5,raw="{\"derived\":{\"links\":[{\"playback\":\"playback:1:9\",\"cue\":\"Music Fixture\"}]}}"};
                    navSession.Accept(navRequest);
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=2,time=1.1,kind="play",entity="voice",objectId="2:v",parentId=navRequest.objectId,name="Music Fixture"});navSession.Accept(navSource);
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=4,time=2,kind="category",entity="category",objectId="2:category-index:1",parentId=navRequest.objectId,name="Music"});
                    navSession.Accept(new WireEvent {session=navSession.Id,seq=5,time=3,kind="aisac",objectId="2:p",name="Distance",value=.25});
                    Select(navSession);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("range","0:3");window.ApplyUiAction("select","1");
                    var beforeNav=State().GetRawText();window.ApplyUiAction("navigate","space");
                    Check(State().GetProperty("workspace").GetString()=="Location"&&Field<WireEvent>("_selected").objectId=="2:source"&&State().GetProperty("end").GetDouble()==3,"播放到空间使用epoch关联并保留历史时刻");
                    window.ApplyUiAction("navigate","playback");
                    Check(State().GetProperty("workspace").GetString()=="Timeline"&&Field<WireEvent>("_selected").objectId==navRequest.objectId,"空间反向定位精确Playback");
                    window.ApplyUiAction("back",null);Check(State().GetProperty("workspace").GetString()=="Location","返回上一位置恢复空间视图");
                    window.ApplyUiAction("back",null);Check(State().GetProperty("workspace").GetString()=="Timeline"&&State().GetProperty("end").GetDouble()==3,"多级返回恢复原时间范围");
                    Check(AssociationPresentation.SourcesFor(navSession.ViewSnapshot(),["1:playback:1:9"],3).Length==0,"空间关系不跨epoch误关联");
                    Check(AssociationPresentation.SourcesFor(navSession.ViewSnapshot(),[navRequest.objectId],1.5).Length==0,"不使用未来位置填历史");
                    Check(AssociationPresentation.Categories(navSession.ViewSnapshot(),navRequest.objectId,3).Single().name=="Music","Category属于具体播放实例");
                    var categoryButton=Field<StackPanel>("_details").GetLogicalDescendants().OfType<Button>().First(b=>b.Tag?.ToString()?.StartsWith("category-filter:")==true);
                    Click(categoryButton);Check(State().GetProperty("category").GetString()=="Music","Category链接筛选轨道并显示条件");
                    window.ApplyUiAction("back",null);
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
                    Check(((Grid)loggedStart.Content!).Children.OfType<TextBlock>().First().Text=="00:00.000","切换主题后日志仍使用采集相对时间");
                    File.WriteAllBytes(Path.GetFullPath(".local/ui-check/v05-log-fixture.png"),window.CapturePng("window"));
                    Check(ControlPresentation.Group(navSession.ViewSnapshot(),4).Single(g=>g.Name=="Distance").Rows.Single().TargetLabel.Contains("Music Fixture"),"Player子行关联当前Cue名");
                    var noLink=WireEvent.Parse(navSource.ToJson());noLink.raw="{\"derived\":{\"links\":[]}}";noLink.time=5;noLink.seq=7;
                    Check(AssociationPresentation.SourcePlaybacks(navSession.ViewSnapshot().Append(noLink),navSource,5).Length==0,"旧选中点不保留已消失的播放关联");
                    Check(EventLogPresentation.Project(lifecycle).Count(e=>e.kind=="play")==2,"三个Voice归并为两个实例开始");
                    Check(EventLogPresentation.Project(lifecycle,true).Count(e=>e.kind=="play")==3,"声部明细保留全部分配记录");
                    var selectedCard=Field<StackPanel>("_sessions").GetLogicalDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()=="selected-client");
                    Check(selectedCard.BorderThickness.Left==1,"客户端选中框有实际厚度");
                    window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("select","1");
                    Check(Field<StackPanel>("_details").Children.OfType<Grid>().Any(g=>g.Tag?.ToString()=="row:播放历时"&&g.ColumnDefinitions.Count==2),"播放详情使用两列属性表");
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
                    overlayTimeline.CloseSpatialList();window.CapturePng("window");
                    Check(overlayTimeline.ExpandedKeys.All(k=>!k.StartsWith("spatial:")),"空间列表可独立关闭");
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
