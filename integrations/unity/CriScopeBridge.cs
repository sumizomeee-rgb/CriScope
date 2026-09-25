// Optional Unity debug transport. No CRI or application-specific dependency.
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
    [Serializable]
    public sealed class WireEvent
    {
        public string kind, session, name, objectId, detail, platform;
        public long seq;
        public double time, value, x, y, z;
        public int cue, pid;
    }

    /// <summary>Main-thread producer, background TCP sender, bounded ACK/replay buffer.</summary>
    public sealed class CriScopeBridge : IDisposable
    {
        private sealed class Pending { public long Sequence, Dropped, FirstLost, LastLost; public string Json; }
        private readonly object gate = new object();
        private readonly Queue<Pending> pending = new Queue<Pending>();
        private static readonly Stopwatch clock = Stopwatch.StartNew();
        private static readonly string session = Guid.NewGuid().ToString("N");
        private readonly string host, hello;
        private readonly Thread worker;
        private volatile bool disposed;
        private volatile string status = "Connecting";
        private static long sequence;
        private const int Capacity = 8192;
        public string Status { get { return status; } }

        public CriScopeBridge(string host, string clientName, string platform, int pid)
        {
            this.host = host;
            hello = JsonUtility.ToJson(new WireEvent { kind = "hello", session = session,
                name = clientName, platform = platform, pid = pid, value = 1 });
            worker = new Thread(Run) { IsBackground = true, Name = "CriScope transport" };
            worker.Start();
        }

        // Invoke only on Unity main thread (JsonUtility and caller-owned data).
        public void Emit(WireEvent item)
        {
            if (disposed) return;
            lock (gate)
            {
                if (pending.Count >= Capacity - 1)
                {
                    var a = pending.Dequeue();
                    var b = pending.Dequeue();
                    long first = Math.Min(a.Dropped > 0 ? a.FirstLost : a.Sequence, b.Dropped > 0 ? b.FirstLost : b.Sequence);
                    long last = Math.Max(a.Dropped > 0 ? a.LastLost : a.Sequence, b.Dropped > 0 ? b.LastLost : b.Sequence);
                    long dropped = (a.Dropped > 0 ? a.Dropped : 1) + (b.Dropped > 0 ? b.Dropped : 1);
                    Enqueue(new WireEvent { kind = "gap", name = "Transport overflow", value = dropped,
                        detail = "Unacknowledged events within " + first + ".." + last + "; delivery uncertain" }, dropped, first, last);
                }
                Enqueue(item);
            }
        }

        private void Enqueue(WireEvent item, long dropped = 0, long firstLost = 0, long lastLost = 0)
        {
            item.session = session;
            item.seq = Interlocked.Increment(ref sequence);
            item.time = clock.Elapsed.TotalSeconds;
            if (item.detail != null && item.detail.Length > 2048) item.detail = item.detail.Substring(0, 2048);
            pending.Enqueue(new Pending { Sequence = item.seq, Json = JsonUtility.ToJson(item), Dropped = dropped, FirstLost = firstLost, LastLost = lastLost });
        }

        private void Run()
        {
            double closingAt = double.MaxValue;
            while (true)
            {
                if (disposed && closingAt == double.MaxValue) closingAt = clock.Elapsed.TotalSeconds + 1;
                if (clock.Elapsed.TotalSeconds >= closingAt) break;
                try
                {
                    using (var client = new TcpClient())
                    {
                        var connect = client.BeginConnect(host, 18961, null, null);
                        using (connect.AsyncWaitHandle)
                        {
                            if (!connect.AsyncWaitHandle.WaitOne(1000)) throw new IOException("Connect timeout");
                            client.EndConnect(connect);
                        }
                        client.NoDelay = true;
                        client.SendTimeout = 500;
                        client.ReceiveTimeout = 500;
                        using (var stream = client.GetStream())
                        using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 8192))
                        {
                            writer.NewLine = "\n";
                            writer.WriteLine(hello);
                            writer.Flush();
                            status = "Connected";
                            long sent = 0;
                            double lastReplay = clock.Elapsed.TotalSeconds;
                            var input = new StringBuilder();
                            var bytes = new byte[4096];
                            while (true)
                            {
                                if (disposed && closingAt == double.MaxValue) closingAt = clock.Elapsed.TotalSeconds + 1;
                                if (clock.Elapsed.TotalSeconds >= closingAt) return;
                                if (clock.Elapsed.TotalSeconds - lastReplay >= 2) { sent = 0; lastReplay = clock.Elapsed.TotalSeconds; }
                                var batch = new List<Pending>(128);
                                lock (gate)
                                {
                                    if (disposed && pending.Count == 0) return;
                                    foreach (var item in pending)
                                        if (item.Sequence > sent) { batch.Add(item); if (batch.Count == 128) break; }
                                }
                                foreach (var item in batch) { writer.WriteLine(item.Json); sent = item.Sequence; }
                                if (batch.Count != 0) writer.Flush();
                                if (client.Client.Poll(0, SelectMode.SelectRead) && client.Available == 0) throw new IOException("Disconnected");
                                while (stream.DataAvailable)
                                {
                                    int count = stream.Read(bytes, 0, bytes.Length);
                                    if (count == 0) throw new IOException("Disconnected");
                                    input.Append(Encoding.UTF8.GetString(bytes, 0, count));
                                    string data = input.ToString();
                                    int end;
                                    while ((end = data.IndexOf('\n')) >= 0)
                                    {
                                        string line = data.Substring(0, end);
                                        data = data.Substring(end + 1);
                                        int colon = line.IndexOf(':');
                                        long ack;
                                        if (line.Contains("\"ack\"") && colon >= 0 && long.TryParse(line.Substring(colon + 1).Trim(' ', '\r', '}'), out ack))
                                            lock (gate) { while (pending.Count > 0 && pending.Peek().Sequence <= ack && ack <= sequence) pending.Dequeue(); }
                                    }
                                    input.Length = 0;
                                    input.Append(data);
                                    if (input.Length > 16384) throw new IOException("Invalid ACK");
                                }
                                Thread.Sleep(20);
                            }
                        }
                    }
                }
                catch (Exception ex) { status = "Reconnecting: " + ex.Message; Thread.Sleep(250); }
            }
            status = "Stopped";
        }

        public void Dispose() { disposed = true; }
    }
}
