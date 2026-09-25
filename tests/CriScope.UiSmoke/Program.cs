using System.Collections.Concurrent;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
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
            var collector = new Collector(directory);
            var a = CreateSession(1001, "Alpha");
            var b = CreateSession(1002, "Beta");
            var sessions = (ConcurrentDictionary<string, Session>)typeof(Collector).GetField("sessions", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(collector)!;
            sessions[a.Id] = a; sessions[b.Id] = b;
            var window = new MainWindow(collector);
            desktop.MainWindow = window;
            window.Opened += async (_, _) =>
            {
                int passed = 0;
                try
                {
                    await Task.Delay(400);
                    Select(a);
                    Check(ReferenceEquals(Field<Session>("_session"), a) && Field<WireEvent[]>("_snapshot").All(e => e.session == a.Id), "同名会话 A 选择与数据归属");
                    Field<TextBox>("_filter").Text = "Alpha";
                    await Task.Delay(50);
                    Select(b);
                    Check(ReferenceEquals(Field<Session>("_session"), b) && Field<WireEvent[]>("_snapshot").All(e => e.session == b.Id), "同名会话 B 独立选择与数据归属");
                    Check(string.IsNullOrEmpty(Field<TextBox>("_filter").Text), "首次切换会话不泄露筛选");
                    Field<TextBox>("_filter").Text = "Beta";
                    await Task.Delay(50);
                    Select(a);
                    Check(Field<TextBox>("_filter").Text == "Alpha", "返回会话恢复其筛选");

                    Click(Field<Button>("_live"));
                    var frozen = Field<WireEvent[]>("_snapshot");
                    a.Accept(Event(a, 2, "Alpha-later"));
                    await Task.Delay(650);
                    Check(ReferenceEquals(frozen, Field<WireEvent[]>("_snapshot")) && frozen.Length == 1, "冻结期间接收新事件不改变浏览快照");
                    var eventList = Field<ListBox>("_events");
                    eventList.SelectedItem = eventList.Items.Cast<ListBoxItem>().First(item => item.Tag is WireEvent);
                    var selected = Field<WireEvent>("_selected");
                    var originalTheme = window.RequestedThemeVariant;
                    Click(window.GetVisualDescendants().OfType<Button>().Single(button => button.Content is string text && text is "浅色" or "暗色"));
                    await Task.Delay(350);
                    Check(window.RequestedThemeVariant != originalTheme && ReferenceEquals(Field<Session>("_session"), a)
                        && ReferenceEquals(Field<WireEvent>("_selected"), selected) && Field<TextBox>("_filter").Text == "Alpha"
                        && ReferenceEquals(frozen, Field<WireEvent[]>("_snapshot")), "切换主题保留会话、事件选择、筛选及冻结快照");
                    Click(Field<Button>("_live"));
                    Check(Field<WireEvent[]>("_snapshot").Length == 2, "返回 Live 更新已接收事件");

                    Click(Field<Button>("_record"));
                    Check(a.Recording && !b.Recording, "录制按钮只启动所选会话 A");
                    a.Accept(Event(a, 3, "Alpha-recorded"));
                    Select(b);
                    Check(Field<TextBox>("_filter").Text == "Beta" && a.Recording && !b.Recording, "切换会话保留筛选与后台录制");
                    Click(Field<Button>("_record"));
                    Check(a.Recording && b.Recording, "会话 B 独立启动录制");
                    b.Accept(Event(b, 2, "Beta-recorded"));
                    Click(Field<Button>("_record"));
                    Check(a.Recording && !b.Recording, "停止 B 不影响 A 录制");
                    Select(a); Click(Field<Button>("_record"));
                    Check(!a.Recording && File.Exists(a.RecordingPath), "停止 A 完成真实录制文件");

                    // 文件选择器属于 OS UI；在其回调边界调用同一 Load 路径。
                    typeof(MainWindow).GetMethod("Load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [a.RecordingPath]);
                    await Task.Delay(350);
                    var replay = Field<Session>("_session");
                    Check(replay.IsReplay && Field<WireEvent[]>("_snapshot").Single().name == "Alpha-recorded"
                        && !Field<Button>("_record").IsEnabled && Field<string?>("_error") == null, "真实录制可打开且历史禁止录制");
                    Console.WriteLine($"结果：{passed}/{passed} UI 行为检查通过");
                    desktop.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"FAIL：已通过 {passed} 项；{ex}");
                    desktop.Shutdown(1);
                }
                finally
                {
                    collector.Dispose();
                    if (Directory.Exists(directory)) Directory.Delete(directory, true);
                }

                T Field<T>(string name) => (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(window)!;
                void Select(Session target)
                {
                    var buttons = Field<StackPanel>("_sessions").Children.OfType<Button>();
                    Click(buttons.Single(button => button.Content is StackPanel lines && lines.Children.OfType<TextBlock>().Any(text => text.Text!.Contains("PID " + target.Pid))));
                }
                void Check(bool condition, string name) { if (!condition) throw new Exception(name); passed++; Console.WriteLine("PASS：" + name); }
            };
        }
        base.OnFrameworkInitializationCompleted();
    }

    static void Click(Button button)
    {
        if (!button.IsEnabled) throw new InvalidOperationException("不能点击禁用按钮：" + button.Content);
        button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    }
    static Session CreateSession(int pid, string eventName)
    {
        var hello = new WireEvent { kind = "hello", name = "同名客户端", session = Guid.NewGuid().ToString("N"), pid = pid, platform = "WindowsEditor", value = 1 };
        var session = new Session(hello); session.Accept(hello); session.Accept(Event(session, 1, eventName)); return session;
    }
    static WireEvent Event(Session session, long sequence, string name) => new() { kind = "play", session = session.Id, seq = sequence, time = sequence, name = name, objectId = "test-object" };
}
