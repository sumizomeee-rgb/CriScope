#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace CriScope.Unity
{
    // Version-specific adapter. Never patches executable code or loads another Atom instance.
    internal sealed class CriScopeMonitor : IDisposable
    {
        private const string SupportedHash = "94e63faec16f5aa251ecb415a9a1d0d37debd811d7347157618e2d62830e282d";
        private bool owned;
        private FinalizeMonitor finish;
        public string Status { get; private set; }
        public bool Owned { get { return owned; } }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void InitializeMonitor(IntPtr config, IntPtr work, int bytes);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void FinalizeMonitor();
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr table, ref int size, bool order, int family, int tableClass, uint reserved);

        internal static bool HasLocalListener()
        {
            int pid = Process.GetCurrentProcess().Id;
            foreach (var row in LocalListeners()) if (row.Port == 2002 && row.Pid == pid) return true;
            return false;
        }
        private struct ListenerOwner { public int Port, Pid; }
        private static ListenerOwner[] LocalListeners()
        {
            int size = 0;
            uint result = GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 3, 0);
            if (result != 122 && result != 0) throw new IOException("Cannot inspect Monitor listener: " + result);
            IntPtr buffer = Marshal.AllocHGlobal(size);
            try
            {
                result = GetExtendedTcpTable(buffer, ref size, false, 2, 3, 0);
                if (result != 0) throw new IOException("Cannot inspect Monitor listener: " + result);
                int count = Marshal.ReadInt32(buffer);
                if (count < 0 || count > (size - 4) / 24) throw new IOException("Invalid Monitor listener table");
                var rows = new System.Collections.Generic.List<ListenerOwner>();
                for (int i = 0; i < count; i++)
                {
                    IntPtr row = IntPtr.Add(buffer, 4 + i * 24);
                    rows.Add(new ListenerOwner { Port = (Marshal.ReadByte(row, 8) << 8) | Marshal.ReadByte(row, 9), Pid = Marshal.ReadInt32(row, 20) });
                }
                return rows.ToArray();
            }
            finally { Marshal.FreeHGlobal(buffer); }
        }
        internal static string PortConflict(int pid, int dataPort = 2002, int controlPort = 2003)
        {
            var owners = new System.Collections.Generic.SortedDictionary<int, string>();
            foreach (var row in LocalListeners())
                if ((row.Port == dataPort || row.Port == controlPort) && row.Pid != pid)
                    owners[row.Pid] = CriScopeNativeRelay.DescribeProcess(row.Pid);
            if (owners.Count == 0) return null;
            return "游戏电脑 " + Environment.MachineName + " 的 CRI 端口 " + dataPort + "/" + controlPort + " 被 " +
                string.Join("、", new System.Collections.Generic.List<string>(owners.Values).ToArray()) +
                " 使用；当前 " + CriScopeNativeRelay.DescribeProcess(pid) + "。释放该工具的原生预览端口后，CriScope 会自动重试。";
        }

        public void Start()
        {
#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            if (!CriWare.CriAtomPlugin.IsLibraryInitialized()) throw new InvalidOperationException("Atom has not initialized.");
            var conflict = PortConflict(Process.GetCurrentProcess().Id);
            if (conflict != null) throw new IOException(conflict);
            if (HasLocalListener()) { Status = "Target Monitor listener :2002 (connection still requires PID verification)"; return; }
            if (IntPtr.Size != 8) throw new NotSupportedException("Monitor hot toggle requires Windows x64.");
            ProcessModule module = null;
            using (var process = Process.GetCurrentProcess())
                foreach (ProcessModule candidate in process.Modules)
                    if (string.Equals(candidate.ModuleName, "cri_ware_unity.dll", StringComparison.OrdinalIgnoreCase)) { module = candidate; break; }
            if (module == null) throw new InvalidOperationException("Loaded CRI module was not found.");
            string hash;
            using (var sha = SHA256.Create())
            using (var input = File.OpenRead(module.FileName))
                hash = BitConverter.ToString(sha.ComputeHash(input)).Replace("-", "").ToLowerInvariant();
            if (hash != SupportedHash) throw new NotSupportedException("Unrecognized CRI DLL; enable In-Game Preview at initialization. Hot toggle refused. SHA256=" + hash);
            IntPtr init = IntPtr.Add(module.BaseAddress, 0x3eb68), done = IntPtr.Add(module.BaseAddress, 0x3eb38);
            CheckBytes(init, "48895C24084889742410574883EC3048");
            CheckBytes(done, "4883EC28E89F84FDFF85C07513488D15");
            // The verified native Initialize/Finalize both guard this singleton. A missing
            // listener alone does not prove that someone else's Monitor is uninitialized.
            IntPtr singleton = IntPtr.Add(module.BaseAddress, 0x22e830);
            if (Marshal.ReadIntPtr(singleton) != IntPtr.Zero)
                throw new InvalidOperationException("Monitor already initialized but :2002 is unavailable; existing instance was not changed.");
            finish = (FinalizeMonitor)Marshal.GetDelegateForFunctionPointer(done, typeof(FinalizeMonitor));
            var start = (InitializeMonitor)Marshal.GetDelegateForFunctionPointer(init, typeof(InitializeMonitor));
            start(IntPtr.Zero, IntPtr.Zero, 0);
            if (Marshal.ReadIntPtr(singleton) == IntPtr.Zero) throw new IOException("Native Monitor initialization failed.");
            owned = true;
            if (!HasLocalListener()) { Dispose(); throw new IOException("Monitor initialization did not open :2002."); }
            Status = "Monitor :2002 (started by CriScope; version-locked private adapter)";
#else
            throw new NotSupportedException("This hot-toggle adapter supports Windows only. Native capture remains available with IGP enabled.");
#endif
        }
        private static void CheckBytes(IntPtr pointer, string expected)
        {
            for (int i = 0; i < expected.Length / 2; i++)
                if (Marshal.ReadByte(pointer, i) != Convert.ToByte(expected.Substring(i * 2, 2), 16))
                    throw new InvalidOperationException("Loaded Monitor entrypoint differs from verified instructions; refused.");
        }
        public void Dispose()
        {
            if (!owned) return;
            owned = false;
            if (CriWare.CriAtomPlugin.IsLibraryInitialized()) finish();
            Status = "Monitor stopped";
        }
    }

    // Called on the Unity main thread. A failed TCP retry cannot initialize a missing Monitor.
    internal sealed class CriScopeMonitorRecovery
    {
        private readonly Action start;
        private readonly Func<bool> available;
        private double nextAttempt = double.NegativeInfinity;
        private bool ready, unsupported;
        internal string Reason { get; private set; }
        internal CriScopeMonitorRecovery(Action start, Func<bool> available)
        { this.start = start; this.available = available; }
        internal bool Tick(double now, bool nativeConnected)
        {
            if (nativeConnected || now < nextAttempt) return false;
            nextAttempt = now + 5;
            try
            {
                if (unsupported)
                {
                    // A project may later enable its own supported IGP path; never retry a private call.
                    if (!available()) return false;
                    ready = true; unsupported = false; Reason = null; return true;
                }
                if (ready && available()) return false;
                start(); ready = true; Reason = null;
            }
            catch (Exception error)
            {
                ready = false; Reason = "原生启动不可用：" + error.Message;
                unsupported = error is NotSupportedException;
            }
            return true;
        }
    }
}
#endif
