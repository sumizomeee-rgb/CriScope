using System;

namespace CriScope.Unity
{
    [Serializable]
    public sealed class WireEvent
    {
        public string kind, session, name, objectId, detail, platform, raw;
        public string parentId, entity, lifecycle;
        public string source = "cri-sdk";
        public long seq;
        public double time, value, x, y, z;
        public int cue, pid;
    }

    // Resource-project mirror: signatures only, no transport or native capture.
    public sealed class CriScopeBridge : IDisposable
    {
        public string Status { get { return "Disabled"; } }
        public bool WorkerAlive { get { return false; } }
        public string LastError { get; private set; }

        public CriScopeBridge(string host, string clientName, string platform, int pid, string nativeSetupReason = null)
            : this(host, clientName, platform, pid, nativeSetupReason, 18961, 2002) { }

        internal CriScopeBridge(string host, string clientName, string platform, int pid, string nativeSetupReason, int servicePort, int nativePort) { }
        public void Emit(WireEvent item) { }
        public void Dispose() { }
    }
}
