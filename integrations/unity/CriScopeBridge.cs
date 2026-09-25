#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;
namespace CriScope.Unity
{
    [Serializable] public sealed class WireEvent
    {
        public string kind, session, name, objectId, detail, platform;
        public string source = "cri-sdk";
        public long seq;
        public double time, value, x, y, z;
        public int cue, pid;
    }
    /// <summary>CSB3 outbound relay. Emit is called only from Unity's main thread.</summary>
    public sealed class CriScopeBridge : IDisposable
    {
        [Serializable] private sealed class Hello { public int version = 3, pid; public string clientId, captureId, name, machine, platform; }
        private sealed class Frame { public byte Channel; public byte[] Data; public long Sequence, Observed; public Gap Evidence; }
        private sealed class Gap { public string Capture, Reason; public byte Channel; public long First, Last, Count; public int Epoch; }
        private readonly object gate = new object();
        private readonly Queue<Frame> queue = new Queue<Frame>();
        private readonly List<Gap> carryGaps = new List<Gap>();
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly ManualResetEvent shutdown = new ManualResetEvent(false);
        private readonly AutoResetEvent wake = new AutoResetEvent(false);
        private readonly string host, helloTemplate, nativeSetupReason;
        private readonly int pid;
        private readonly int servicePort, nativePort;
        private readonly Thread worker;
        private TcpClient remote;
        private CriScopeNativeRelay relay;
        private volatile bool disposed;
        private volatile string status = "等待连接服务";
        private string capture, captureFault;
        private long sequence, sdkSequence, offlineSdk;
        private int queuedBytes;
        private const int MaxBytes = 8 * 1024 * 1024, MaxFrames = 8192;
        public string Status { get { return status; } }
        public bool WorkerAlive { get { return worker.IsAlive; } }
        public string LastError { get; private set; }
        public CriScopeBridge(string host, string clientName, string platform, int pid, string nativeSetupReason = null)
            : this(host, clientName, platform, pid, nativeSetupReason, 18961, 2002) { }
        internal CriScopeBridge(string host, string clientName, string platform, int pid, string nativeSetupReason, int servicePort, int nativePort)
        {
            this.host = host; this.pid = pid; this.nativeSetupReason = nativeSetupReason;
            this.servicePort = servicePort; this.nativePort = nativePort;
            // Process environment survives domain reload. Initialized only on explicit enable.
            long processStart;
            using (var process = Process.GetCurrentProcess()) processStart = process.StartTime.ToUniversalTime().Ticks;
            string key = "CRISCOPE_CLIENT_ID_" + pid + "_" + processStart, identity = Environment.GetEnvironmentVariable(key);
            Guid parsed;
            if (!Guid.TryParseExact(identity, "N", out parsed)) { identity = Guid.NewGuid().ToString("N"); Environment.SetEnvironmentVariable(key, identity, EnvironmentVariableTarget.Process); }
            helloTemplate = JsonUtility.ToJson(new Hello { pid = pid, clientId = identity, captureId = "CAPTURE_PLACEHOLDER", name = clientName, machine = Environment.MachineName, platform = platform });
            worker = new Thread(Run) { IsBackground = true, Name = "CriScope relay sender" }; worker.Start();
        }
        public void Emit(WireEvent item)
        {
            if (disposed) return;
            lock (gate)
            {
                if (capture == null || captureFault != null) { offlineSdk++; return; }
                item.session = capture; item.seq = ++sdkSequence; item.time = clock.Elapsed.TotalSeconds;
                if (item.detail != null && item.detail.Length > 2048) item.detail = item.detail.Substring(0, 2048);
                EnqueueLocked(2, Encoding.UTF8.GetBytes(JsonUtility.ToJson(item)));
            }
        }
        private bool Enqueue(byte channel, byte[] data)
        {
            lock (gate) { if (disposed || capture == null || captureFault != null) return false; return EnqueueLocked(channel, data); }
        }
        private bool EnqueueLocked(byte channel, byte[] data, Gap evidence = null)
        {
            var frame = new Frame { Channel = channel, Data = data, Sequence = ++sequence, Observed = (long)(clock.ElapsedTicks * (1000000.0 / Stopwatch.Frequency)), Evidence = evidence };
            if (data.Length > MaxBytes || queuedBytes + data.Length > MaxBytes || queue.Count >= MaxFrames)
            {
                AddGap(frame, "bounded relay queue overflow"); captureFault = "采集队列溢出，重新建立采集段"; wake.Set(); return false;
            }
            queue.Enqueue(frame); queuedBytes += data.Length; wake.Set(); return true;
        }
        private static string Quote(string s)
        {
            var b = new StringBuilder("\"");
            foreach (char c in s ?? "") if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c);
            return b.Append('"').ToString();
        }
        private void ChannelStatus(int channel, bool connected, string message)
        { Enqueue(3, Encoding.UTF8.GetBytes("{\"channel\":" + channel + ",\"connected\":" + (connected ? "true" : "false") + ",\"status\":" + Quote(message) + "}")); }
        private void AddGap(Frame frame, string reason)
        {
            if (frame.Evidence != null)
            {
                var old = frame.Evidence;
                var combined = carryGaps.Find(g => g.Channel == old.Channel && (g.Capture == old.Capture || g.Epoch == 0));
                if (combined == null && carryGaps.Count >= 32) combined = carryGaps.Find(g => g.Channel == old.Channel);
                if (combined == null) carryGaps.Add(old);
                else
                {
                    combined.Count += old.Count;
                    if (combined.Capture != old.Capture || combined.Epoch == 0) { combined.Capture = ""; combined.First = combined.Last = 0; combined.Epoch = 0; combined.Reason = "multiple disconnected captures; delivery uncertain"; }
                    else { combined.First = Math.Min(combined.First, old.First); combined.Last = Math.Max(combined.Last, old.Last); }
                }
                return;
            }
            Gap gap = carryGaps.Find(g => g.Capture == capture && g.Channel == frame.Channel && g.Reason == reason);
            if (gap == null)
            {
                if (carryGaps.Count >= 32)
                {
                    gap = carryGaps.Find(g => g.Channel == frame.Channel);
                    if (gap != null) { gap.Capture = ""; gap.First = gap.Last = 0; gap.Epoch = 0; gap.Reason = "multiple disconnected captures; delivery uncertain"; }
                }
                if (gap == null) { gap = new Gap { Capture = capture, Channel = frame.Channel, First = frame.Sequence, Last = frame.Sequence, Epoch = 1, Reason = reason }; carryGaps.Add(gap); }
            }
            gap.Count++; if (gap.Epoch != 0) { gap.First = Math.Min(gap.First, frame.Sequence); gap.Last = Math.Max(gap.Last, frame.Sequence); }
        }
        private void Run()
        {
            int failures = 0;
            try
            {
                while (!disposed)
                {
                    Frame sending = null;
                    try
                    {
                        var client = new TcpClient(); lock (gate) remote = client;
                        var connect = client.ConnectAsync(host, servicePort);
                        var connecting = Stopwatch.StartNew();
                        while (!connect.IsCompleted && !disposed && connecting.ElapsedMilliseconds < 1500) shutdown.WaitOne(25);
                        if (!connect.IsCompleted || disposed) throw new IOException("服务未就绪");
                        connect.GetAwaiter().GetResult();
                        client.NoDelay = true; client.SendTimeout = 750;
                        using (var stream = client.GetStream())
                        {
                            string id = Guid.NewGuid().ToString("N"); byte[] hello = Encoding.UTF8.GetBytes(helloTemplate.Replace("CAPTURE_PLACEHOLDER", id));
                            if (hello.Length > 16384) throw new IOException("Hello exceeds 16 KiB");
                            stream.Write(new byte[] { 67, 83, 66, 51 }, 0, 4);
                            byte[] length = new byte[4]; Write32(length, 0, hello.Length); stream.Write(length, 0, 4); stream.Write(hello, 0, hello.Length);
                            lock (gate)
                            {
                                capture = id; captureFault = null; sequence = 0;
                                if (offlineSdk != 0) { carryGaps.Add(new Gap { Capture = "", Channel = 2, Count = offlineSdk, Reason = "SDK observations while receiver unavailable" }); offlineSdk = 0; }
                                foreach (var gap in carryGaps)
                                {
                                    string json = "{\"first\":" + gap.First + ",\"last\":" + gap.Last + ",\"count\":" + gap.Count + ",\"channel\":" + gap.Channel + ",\"epoch\":" + gap.Epoch + ",\"captureId\":" + Quote(gap.Capture) + ",\"reason\":" + Quote(gap.Reason) + "}";
                                    EnqueueLocked(4, Encoding.UTF8.GetBytes(json), gap);
                                }
                                carryGaps.Clear();
                            }
                            ChannelStatus(2, true, "SDK 补充已连接"); failures = 0; status = "服务已连接 · 等待本机 CRI";
                            double retryNativeAt = 0;
                            while (!disposed)
                            {
                                lock (gate) if (captureFault != null) throw new IOException(captureFault);
                                if (relay != null && relay.Failure != null) throw new IOException("原生采集段结束: " + relay.Failure);
                                if (relay == null && clock.Elapsed.TotalSeconds >= retryNativeAt)
                                {
                                    try
                                    {
                                        var next = new CriScopeNativeRelay(pid, data => Enqueue(1, data), nativePort); lock (gate) relay = next;
                                        next.Start(); ChannelStatus(1, true, "已核验本机 CRI 进程 · 原生采集中"); status = "服务已连接 · 原生 + SDK 采集中";
                                    }
                                    catch (Exception ex)
                                    {
                                        if (relay != null) relay.Dispose(); lock (gate) relay = null;
                                        status = "服务已连接 · 原生不可用: " + ex.Message + (nativeSetupReason == null ? "" : "; " + nativeSetupReason); ChannelStatus(1, false, status); retryNativeAt = clock.Elapsed.TotalSeconds + 5;
                                    }
                                }
                                for (int i = 0; i < 128; i++)
                                {
                                    lock (gate) { if (queue.Count == 0) break; sending = queue.Dequeue(); queuedBytes -= sending.Data.Length; }
                                    byte[] header = new byte[25]; Write32(header, 0, sending.Data.Length); header[4] = sending.Channel;
                                    Write64(header, 5, sending.Sequence); Write32(header, 13, 1); Write64(header, 17, sending.Observed);
                                    stream.Write(header, 0, header.Length); stream.Write(sending.Data, 0, sending.Data.Length); sending = null;
                                }
                                if (client.Client.Poll(0, SelectMode.SelectRead)) { if (client.Available == 0) throw new IOException("服务断开"); throw new IOException("Unexpected receiver payload"); }
                                WaitHandle.WaitAny(new WaitHandle[] { wake, shutdown }, 20);
                            }
                        }
                    }
                    catch (Exception ex) { LastError = ex.ToString(); if (!disposed) status = "等待重连: " + ex.Message; }
                    finally
                    {
                        CriScopeNativeRelay ending; TcpClient endingRemote;
                        lock (gate)
                        {
                            if (sending != null) AddGap(sending, "network write failed; delivery uncertain");
                            while (queue.Count != 0) AddGap(queue.Dequeue(), "capture disconnected before send");
                            queuedBytes = 0; capture = null; ending = relay; relay = null; endingRemote = remote; remote = null;
                        }
                        try { if (ending != null) ending.Dispose(); }
                        catch (Exception e) { status = "原生收尾失败: " + e.Message; disposed = true; }
                        finally { if (endingRemote != null) endingRemote.Close(); }
                    }
                    if (!disposed) shutdown.WaitOne(Math.Min(5000, 250 * (1 << Math.Min(++failures, 4))));
                }
            }
            finally { status = "已关闭"; }
        }
        internal static void Write32(byte[] data, int offset, int value) { for (int i = 3; i >= 0; i--) { data[offset + i] = (byte)value; value >>= 8; } }
        internal static void Write64(byte[] data, int offset, long value) { for (int i = 7; i >= 0; i--) { data[offset + i] = (byte)value; value >>= 8; } }
        public void Dispose()
        {
            if (disposed) return; disposed = true; shutdown.Set(); wake.Set();
            lock (gate) if (remote != null) remote.Close();
            if (!worker.Join(4000)) throw new TimeoutException("CriScope relay worker did not stop within 4 seconds");
            shutdown.Dispose(); wake.Dispose();
        }
    }
}
#endif
