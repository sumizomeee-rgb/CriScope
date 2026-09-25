#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
using System;
using System.IO;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;

namespace CriScope.Unity
{
    /// <summary>Opaque native framing; verifies the accepted server socket belongs to this process.</summary>
    internal sealed class CriScopeNativeRelay : IDisposable
    {
        private readonly int expectedPid;
        private readonly int port;
        private readonly long expectedStart;
        private readonly Func<byte[], bool> receive;
        private readonly ManualResetEvent stop = new ManualResetEvent(false);
        private TcpClient client;
        private Thread reader;
        private bool subscribed;
        private volatile bool disposed;
        private volatile string failure;
        public string Failure { get { return failure; } }
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);
        public CriScopeNativeRelay(int pid, Func<byte[], bool> receive, int port = 2002)
        { expectedPid = pid; this.receive = receive; this.port = port; using (var process = Process.GetCurrentProcess()) expectedStart = process.StartTime.ToUniversalTime().Ticks; }
        public void Start()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (IntPtr.Size != 8) throw new NotSupportedException("This native relay handshake has been verified on Windows x64 only");
            client = new TcpClient(AddressFamily.InterNetwork);
            var connect = client.ConnectAsync(IPAddress.Loopback, port);
            var connecting = Stopwatch.StartNew();
            while (!connect.IsCompleted && !disposed && connecting.ElapsedMilliseconds < 500) stop.WaitOne(25);
            if (!connect.IsCompleted || disposed) throw new IOException("本机 Monitor :2002 未就绪");
            connect.GetAwaiter().GetResult();
            client.NoDelay = true; client.SendTimeout = 300;
            var local = (IPEndPoint)client.Client.LocalEndPoint;
            var server = (IPEndPoint)client.Client.RemoteEndPoint;
            int owner = 0;
            for (int i = 0; i < 20 && owner == 0 && !disposed; i++) { owner = FindServerOwner(server, local); if (owner == 0) stop.WaitOne(10); }
            if (owner != expectedPid) throw new IOException(owner == 0 ? "无法核验原生连接所属进程，已拒绝采集" : "原生端口属于另一进程 " + owner + "，当前进程 " + expectedPid + "；已拒绝采集");
            using (var process = Process.GetProcessById(owner))
                if (process.HasExited || process.StartTime.ToUniversalTime().Ticks != expectedStart) throw new IOException("原生连接进程生命周期不匹配，已拒绝采集");
            // No command is sent before owner verification; never STOP another process's recorder.
            SendCommand(111, null, 0x01000000);
            SendCommand(22, new byte[] { 0, 129, 255, 255, 255, 255 }, 0);
            subscribed = true;
            reader = new Thread(Read) { IsBackground = true, Name = "CriScope native reader" }; reader.Start();
#else
            throw new NotSupportedException("Native socket ownership verification currently supports Windows only");
#endif
        }
        private static int FindServerOwner(IPEndPoint local, IPEndPoint remote)
        {
            int size = 0; uint code = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 4, 0); // OWNER_PID_CONNECTIONS
            if (code != 0 && code != 122) throw new IOException("TCP owner query failed: " + code);
            IntPtr table = Marshal.AllocHGlobal(size);
            try
            {
                code = GetExtendedTcpTable(table, ref size, false, 2, 4, 0);
                if (code != 0) throw new IOException("TCP owner query failed: " + code);
                int count = Marshal.ReadInt32(table); byte[] localBytes = local.Address.GetAddressBytes(), remoteBytes = remote.Address.GetAddressBytes();
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(table, 4 + 24 * i);
                    if (Marshal.ReadInt32(row) != 5) continue;
                    if (((Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9)) != local.Port || ((Marshal.ReadByte(row, 16) << 8) | Marshal.ReadByte(row, 17)) != remote.Port) continue;
                    bool match = true;
                    for (int n = 0; n < 4; n++) if (Marshal.ReadByte(row, 4 + n) != localBytes[n] || Marshal.ReadByte(row, 12 + n) != remoteBytes[n]) match = false;
                    if (match) return Marshal.ReadInt32(row, 20);
                }
                return 0;
            }
            finally { Marshal.FreeHGlobal(table); }
        }
        private void SendCommand(int command, byte[] payload, int control)
        {
            int header = IntPtr.Size == 8 ? 32 : 24, size = header + (payload == null ? 0 : payload.Length), padding = (8 - size % 8) % 8;
            byte[] bytes = new byte[size + padding]; CriScopeBridge.Write32(bytes, 0, bytes.Length);
            bytes[4] = (byte)(command >> 8); bytes[5] = (byte)command; bytes[6] = (byte)IntPtr.Size;
            bytes[18] = (byte)(padding >> 8); bytes[19] = (byte)padding;
            CriScopeBridge.Write32(bytes, 20, control);
            if (payload != null) Buffer.BlockCopy(payload, 0, bytes, header, payload.Length);
            client.GetStream().Write(bytes, 0, bytes.Length);
        }
        private void Read()
        {
            try
            {
                var stream = client.GetStream(); var prefix = new byte[4];
                while (!disposed)
                {
                    ReadExact(stream, prefix, 0, 4);
                    int size = (prefix[0] << 24) | (prefix[1] << 16) | (prefix[2] << 8) | prefix[3];
                    if (size < 24 || size > 8 * 1024 * 1024) throw new IOException("Invalid native frame length " + size);
                    var frame = new byte[size]; Buffer.BlockCopy(prefix, 0, frame, 0, 4); ReadExact(stream, frame, 4, size - 4);
                    if ((frame[6] != 4 && frame[6] != 8) || (frame[6] == 8 && size < 32)) throw new IOException("Invalid native pointer/header size");
                    if (!receive(frame)) throw new IOException("Native queue rejected a whole frame; ending capture");
                }
            }
            catch (Exception e) { if (!disposed) failure = e.Message; }
        }
        private void ReadExact(NetworkStream stream, byte[] bytes, int offset, int count)
        {
            while (count > 0 && !disposed) { int n = stream.Read(bytes, offset, count); if (n == 0) throw new EndOfStreamException("Native connection closed"); offset += n; count -= n; }
            if (disposed) throw new OperationCanceledException();
        }
        public void Dispose()
        {
            if (disposed) return; disposed = true; stop.Set();
            if (client != null)
            {
                if (subscribed) try { SendCommand(24, null, 0); } catch (Exception) { }
                client.Close();
            }
            if (reader != null && reader != Thread.CurrentThread && !reader.Join(1500)) throw new TimeoutException("Native reader did not stop within 1.5 seconds");
            stop.Dispose();
        }
    }
}
#endif
