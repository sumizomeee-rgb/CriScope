using System;

namespace CriScope.Unity
{
    // Serialization-project API mirror. No sampling, callbacks, transport or native calls.
    internal sealed class CriScopeCaptureSession : IDisposable
    {
        public bool CaptureEnabled { get { return false; } }
        public string Status { get { return "Disabled"; } }
        public static CriScopeCaptureSession Start(string host, string clientName, Func<double> clock) { return null; }
        public void Tick() { }
        public void Dispose() { }
    }
}
