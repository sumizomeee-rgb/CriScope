using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
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
                    Check(MetricPresentation.Value(new WireEvent { objectId = "stream.bps", value = 1023000 }) == "1.023 Mbit/s", "原生 bit/s 显示 Mbit/s");
                    Check(MetricPresentation.Value(new WireEvent { detail = "bit/s", value = 48000 }) == "48 kbit/s", "小流量显示 kbit/s");
                    Check(MetricPresentation.Value(new WireEvent { objectId = "CpuLoad", value = .5 }) == "0.5 %" && MetricPresentation.Value(new WireEvent { objectId = "AverageServerTime", value = 320 }) == "320 µs", "CPU 与服务耗时单位");
                    Check(MetricPresentation.Value(new WireEvent { name = "memory.atom.bytes", value = 1048576 }) == "1.00 MiB", "内存字节按 MiB 显示");
                    Check(MetricPresentation.Name(new WireEvent { objectId = "stream.used" }) == "流式播放声部（原生）" && MetricPresentation.Name(new WireEvent { name = "voices.streaming.used" }) == "Streaming 声池", "原生声部与 SDK 声池口径分开");
                    Check(MetricPresentation.StreamingPool(new WireEvent { value = 1 }, new WireEvent { value = 16 }) == "1 / 16" && MetricPresentation.StreamingPool(new WireEvent { value = 1 }, null) == "1 / 未提供", "Streaming 池用量容量及缺失容量");
                    await Task.Delay(400);
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
                    typeof(MainWindow).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [a.RecordingPath]);
                    await Task.Delay(200);
                    Check(Field<Session>("_session").IsReplay && Field<WireEvent[]>("_snapshot").Single().name == "Alpha-recorded" && !Field<Button>("_record").IsEnabled,
                        "真实录制重新打开且回放禁止重复录制");
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
