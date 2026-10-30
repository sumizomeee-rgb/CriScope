using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using CriScope.Unity;

static class Program
{
    static void Check(bool condition, string message)
    { if (!condition) throw new Exception(message); Console.WriteLine("PASS " + message); }
    static TcpListener Reserve()
    {
        while (true)
        {
            var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            if (((IPEndPoint)listener.LocalEndpoint).Port is not (2002 or 2003 or 18961 or 18962)) return listener;
            listener.Stop();
        }
    }
    static async Task Until(Func<bool> condition)
    {
        using var deadline = new CancellationTokenSource(9000);
        while (!condition()) { deadline.Token.ThrowIfCancellationRequested(); await Task.Delay(20); }
    }
    static async Task Peer()
    {
        var listener = Reserve(); Console.WriteLine(((IPEndPoint)listener.LocalEndpoint).Port);
        try
        {
            using var accepted = await listener.AcceptTcpClientAsync();
            using var deadline = new CancellationTokenSource(5000);
            var data = new byte[128];
            Console.WriteLine("bytes=" + await accepted.GetStream().ReadAsync(data, deadline.Token));
        }
        finally { listener.Stop(); }
    }
    static async Task Main(string[] args)
    {
        if (args.Contains("--peer")) { await Peer(); return; }
        if (!OperatingSystem.IsWindows()) { Console.WriteLine("SKIP Windows socket-owner checks"); return; }
        var lookup = typeof(CriScopeNativeRelay).GetMethod("FindServerOwner", BindingFlags.Static | BindingFlags.NonPublic);
        int pid = Environment.ProcessId;
        for (int i = 0; i < 10; i++)
        {
            var listener = Reserve();
            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
                using var server = await listener.AcceptTcpClientAsync();
                if ((int)lookup.Invoke(null, [server.Client.LocalEndPoint, server.Client.RemoteEndPoint]) != pid)
                    throw new Exception("Own socket PID was misidentified");
            }
            finally { listener.Stop(); }
        }
        Check(true, "actual SDK owner lookup identifies the current process on ten fresh sockets");

        var start = new ProcessStartInfo(Environment.ProcessPath)
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        start.ArgumentList.Add("--peer");
        using (var peer = Process.Start(start))
        {
            try
            {
                int port = int.Parse(await peer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
                var conflict = CriScopeMonitor.PortConflict(pid, port, port);
                Check(conflict != null && conflict.Contains(peer.Id.ToString()) && conflict.Contains(Environment.MachineName)
                    && conflict.Contains(peer.ProcessName), "preflight names the actual conflicting process and game machine");
                string error = null;
                using (var relay = new CriScopeNativeRelay(pid, _ => true, port))
                    try { relay.Start(); } catch (IOException ex) { error = ex.Message; }
                Check(error != null && error.Contains("PID " + peer.Id) && error.Contains("PID " + pid),
                    "first native connection to another process is refused with both identities");
                var receipt = await peer.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
                await peer.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
                Check(receipt == "bytes=0" && peer.ExitCode == 0, "refusal sends no VERSION, START or STOP command to the other process");
            }
            finally { if (!peer.HasExited) { peer.Kill(); await peer.WaitForExitAsync(); } }
        }

        int attempts = 0; bool released = false, present = false;
        var recovery = new CriScopeMonitorRecovery(() => {
            attempts++; if (!released) throw new IOException("CriAtomViewer holds the port"); present = true;
        }, () => present);
        Check(recovery.Tick(0, false) && recovery.Reason.Contains("CriAtomViewer") && attempts == 1,
            "Monitor startup failure remains actionable while SDK transport stays available");
        Check(!recovery.Tick(4.99, false) && attempts == 1, "Monitor retry is bounded to once every five seconds");
        released = true;
        Check(recovery.Tick(5, false) && recovery.Reason == null && attempts == 2,
            "releasing a conflicting port automatically retries Monitor initialization without toggling capture");
        present = false;
        Check(!recovery.Tick(10, true) && attempts == 2, "healthy native capture never reinitializes its Monitor");
        Check(recovery.Tick(10, false) && attempts == 3 && present,
            "a missing Monitor is rechecked after native capture loses its connection");
        int unsupportedAttempts = 0;
        bool externalMonitor = false;
        var unsupported = new CriScopeMonitorRecovery(() => { unsupportedAttempts++; throw new NotSupportedException("Unknown DLL"); }, () => externalMonitor);
        unsupported.Tick(0, false); unsupported.Tick(100, false);
        Check(unsupportedAttempts == 1, "unknown native DLL is refused without repeated private entrypoint attempts");
        externalMonitor = true;
        Check(unsupported.Tick(105, false) && unsupported.Reason == null && unsupportedAttempts == 1,
            "a project-owned IGP listener can recover without any unsupported private initialization");

        var receiver = Reserve(); var native = Reserve();
        try
        {
            int receiverPort = ((IPEndPoint)receiver.LocalEndpoint).Port, nativePort = ((IPEndPoint)native.LocalEndpoint).Port;
            using var bridge = new CriScopeBridge("127.0.0.1", "Native recovery fixture", "Windows", pid,
                "Viewer conflict", receiverPort, nativePort);
            using var receiverSocket = await receiver.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(5));
            var drain = Task.Run(async () => { try { var data = new byte[4096]; while (await receiverSocket.GetStream().ReadAsync(data) > 0) { } } catch (IOException) { } catch (ObjectDisposedException) { } });
            await Task.Delay(250);
            Check(!native.Pending() && !bridge.NativeConnected, "blocked Monitor setup does not probe another native server");
            bridge.SetNativeSetupReason(null);
            using var nativeSocket = await native.AcceptTcpClientAsync().WaitAsync(TimeSpan.FromSeconds(8));
            await Until(() => bridge.NativeConnected);
            Check(bridge.NativeConnected, "clearing startup failure automatically connects the actual SDK relay on the same bridge session");
            bridge.Dispose(); receiverSocket.Close(); await drain;
            Check(!bridge.WorkerAlive, "recovery leaves no sender worker after capture is disabled");
        }
        finally { receiver.Stop(); native.Stop(); }
    }
}
