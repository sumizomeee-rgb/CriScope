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
                    var end = State().GetProperty("end").GetDouble();
                    foreach (var workspace in new[] { "播放", "控制", "混音", "空间", "资源" })
                    {
                        window.ApplyUiAction("workspace", workspace);
                        Check(State().GetProperty("live").GetBoolean() && State().GetProperty("selected").GetInt64() == 1 && Field<TextBox>("_filter").Text == "Alpha"
                            && State().GetProperty("end").GetDouble() == end, "工作区 " + workspace + " 保留实时/选择/筛选/范围");
                    }
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
                    Check(ReferenceEquals(Field<Session>("_session"),newSdk) && State().GetProperty("live").GetBoolean(), "Live 跟随重连的新 CaptureId 并优先同通道");
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
                    var listenerHit=pointHits.Single(h=>h.item.entity=="distance-listener");
                    var sourceHit=expansionHits.Single(h=>h.key.StartsWith("spatial:"));
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
