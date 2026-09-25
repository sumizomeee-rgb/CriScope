using System.Net.Sockets;

namespace CriScope.Core;

/// <summary>One explicitly requested native monitor session; never auto-enables the game's Monitor.</summary>
public sealed class NativeConnection(string host, int port, string session, Action<WireEvent> receive, Action<string> status) : IDisposable
{
    private readonly TcpClient client = new();
    private readonly CancellationTokenSource lifetime = new();
    private readonly TaskCompletionSource connected = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int running;
    public Task Connected => connected.Task;

    public async Task Run(CancellationToken cancellationToken)
    {
        if (Interlocked.Exchange(ref running, 1) != 0) throw new InvalidOperationException("NativeConnection can only run once");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, lifetime.Token);
        var token = linked.Token;
        var mapper = new NativeEventMapper(session);
        NetworkStream? stream = null;
        bool receivedLog = false;
        var decodeErrors = new Dictionary<string, (int Count, long LastReported)>();
        try
        {
            status($"连接原生 Monitor {host}:{port}");
            await client.ConnectAsync(host, port, token).ConfigureAwait(false);
            client.NoDelay = true;
            stream = client.GetStream();
            // Observed official-client order: version handshake, then all-log subscription.
            await stream.WriteAsync(NativeProtocol.Command(111), token).ConfigureAwait(false);
            await stream.WriteAsync(NativeProtocol.Command(22), token).ConfigureAwait(false);
            connected.TrySetResult();
            status("原生端口已连接，等待数据（其他 Profiler 占用时可能无数据）");
            byte[] prefix = new byte[4];
            while (true)
            {
                await stream.ReadExactlyAsync(prefix, token).ConfigureAwait(false);
                int size = NativeProtocol.ReadFrameLength(prefix);
                byte[] frame = new byte[size];
                prefix.CopyTo(frame, 0);
                await stream.ReadExactlyAsync(frame.AsMemory(4), token).ConfigureAwait(false);
                NativePacket packet;
                try { packet = NativeProtocol.Decode(frame); }
                catch (InvalidDataException ex)
                {
                    var prior = decodeErrors.GetValueOrDefault(ex.Message);
                    long now = Environment.TickCount64;
                    int count = prior.Count + 1;
                    if (prior.Count == 0 || now - prior.LastReported >= 5000)
                    {
                        receive(mapper.Diagnostic(ex.Message + $"; occurrences={count}", "hex-prefix=" + Convert.ToHexString(frame.AsSpan(0, Math.Min(frame.Length, 2048))) + $"; totalBytes={frame.Length}"));
                        decodeErrors[ex.Message] = (count, now);
                    }
                    else decodeErrors[ex.Message] = (count, prior.LastReported);
                    continue; // Length was validated; the next frame remains aligned.
                }
                if (packet.Command == 31 && !receivedLog) { receivedLog = true; status("原生采集中"); }
                foreach (var ev in mapper.Map(packet)) receive(ev);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { connected.TrySetCanceled(token); }
        catch (Exception ex) when (ex is IOException or SocketException or InvalidDataException or ObjectDisposedException)
        {
            if (token.IsCancellationRequested) connected.TrySetCanceled(token);
            else
            {
                connected.TrySetException(ex);
                // Run reports transport errors itself. Mark this optional handshake task observed
                // even if a consumer only awaits Run; callers awaiting Connected still receive it.
                _ = connected.Task.Exception;
                receive(mapper.Diagnostic("原生连接结束：" + ex.Message));
                status("原生连接失败或已断开：" + ex.Message);
            }
        }
        finally
        {
            if (stream is not null)
            {
                try
                {
                    using var deadline = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
                    await stream.WriteAsync(NativeProtocol.Command(24), deadline.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException) { }
            }
            client.Dispose();
            status("原生采集已断开；游戏侧 Monitor 是否卸载由游戏诊断开关决定");
        }
    }

    public void Dispose()
    {
        // Cancellation releases pending socket reads, allowing Run's bounded STOP/close finally path.
        lifetime.Cancel();
        if (Volatile.Read(ref running) == 0) { client.Dispose(); connected.TrySetCanceled(); }
    }
}
