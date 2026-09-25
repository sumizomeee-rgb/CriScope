using System.Net;
using System.Runtime.InteropServices;

namespace CriScope.Core;

public sealed partial class Collector
{
    readonly object nativeGate = new();
    readonly Dictionary<string, NativeCapture> nativeCaptures = new(StringComparer.OrdinalIgnoreCase);
    sealed class NativeCapture(Session session, CancellationTokenSource cancellation, NativeConnection connection)
    {
        public Session Session { get; } = session;
        public CancellationTokenSource Cancellation { get; } = cancellation;
        public NativeConnection Connection { get; } = connection;
        public Task Run { get; set; } = Task.CompletedTask;
    }

    public async Task<Session> ConnectNativeAsync(string host, int port = 2002)
    {
        host = host.Trim();
        if (string.IsNullOrEmpty(host) || host.Length > 255 || port is < 1 or > 65535)
            throw new ArgumentException("请输入有效的 CRI Monitor 主机与端口");
        var endpoint = $"{host}:{port}";
        NativeCapture capture;
        var ready = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (nativeGate)
        {
            ObjectDisposedException.ThrowIf(stop.IsCancellationRequested, this);
            if (nativeCaptures.TryGetValue(endpoint, out var existing) && !existing.Cancellation.IsCancellationRequested)
                return existing.Session;
            var hello = new WireEvent { kind = "hello", session = Guid.NewGuid().ToString("N"), value = 1,
                name = "CRI Monitor", source = "CRI Monitor", platform = "Native TCP", pid = LocalOwner(host, port), detail = endpoint };
            var session = new Session(hello);
            session.SetCaptureState(false, false, "正在连接 " + endpoint);
            sessions[session.Id] = session;
            var cancellation = CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            string? failure = null;
            var connection = new NativeConnection(host, port, session.Id, e =>
            {
                session.Accept(e);
                if (e.kind == "error" && e.detail.StartsWith("原生连接结束", StringComparison.Ordinal)) failure = e.detail;
                else if (e.name == "原生连接消息") session.SetCaptureState(true, false, "原生端口已连接，等待日志 · " + endpoint);
                else session.SetCaptureState(true, true, "原生采集中 · " + endpoint);
                ready.TrySetResult();
            }, message => session.ConnectionStatus = message);
            capture = new NativeCapture(session, cancellation, connection);
            nativeCaptures[endpoint] = capture;
            capture.Run = Task.Run(async () =>
            {
                string final = "原生连接已结束";
                try { await connection.Run(cancellation.Token); }
                catch (OperationCanceledException) { final = "原生采集已停止"; }
                catch (Exception ex) when (ex is not OutOfMemoryException) { final = "连接失败：" + ex.Message; }
                finally
                {
                    session.SetCaptureState(false, false, failure ?? (cancellation.IsCancellationRequested ? "原生采集已停止" : final));
                    ready.TrySetResult();
                    connection.Dispose();
                    lock (nativeGate)
                        if (nativeCaptures.TryGetValue(endpoint, out var current) && ReferenceEquals(current, capture))
                            nativeCaptures.Remove(endpoint);
                }
            });
        }
        await Task.WhenAny(ready.Task, Task.Delay(TimeSpan.FromSeconds(8), stop.Token));
        if (!ready.Task.IsCompleted)
            capture.Session.ConnectionStatus = "尚未收到原生日志；检查 Monitor 状态或其他采集客户端";
        return capture.Session;
    }

    public void DisconnectNative(Session session)
    {
        lock (nativeGate)
            foreach (var capture in nativeCaptures.Values.Where(c => ReferenceEquals(c.Session, session)).ToArray())
                capture.Cancellation.Cancel();
    }

    void DisposeNative()
    {
        Task[] ending;
        lock (nativeGate)
        {
            foreach (var capture in nativeCaptures.Values.ToArray()) capture.Cancellation.Cancel();
            ending = nativeCaptures.Values.Select(c => c.Run).ToArray();
        }
        // Give the bounded STOP_LOG_RECORD finally path time to finish at process exit.
        try { Task.WaitAll(ending, TimeSpan.FromMilliseconds(500)); } catch (AggregateException) { }
    }

    // Local PID is provenance only. Never use it to merge uncalibrated clocks.
    static int LocalOwner(string host, int port)
    {
        if (!OperatingSystem.IsWindows() || !(host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))) return 0;
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
        if (size <= 0 || size > 16 * 1024 * 1024) return 0;
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0) != 0) return 0;
            int rows = Marshal.ReadInt32(buffer);
            for (int i = 0; i < rows && 4L + (i + 1L) * 24 <= size; i++)
            {
                var row = IntPtr.Add(buffer, 4 + i * 24);
                int rowPort = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9);
                if (rowPort == port) return Marshal.ReadInt32(row, 20);
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return 0;
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    static extern uint GetExtendedTcpTable(IntPtr table, ref int size, [MarshalAs(UnmanagedType.Bool)] bool order,
        int family, int tableClass, uint reserved);
}
