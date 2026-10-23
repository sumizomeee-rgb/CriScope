using System;

namespace CriScope.Unity
{
    // Resource-project mirror: no native monitor lifecycle.
    internal sealed class CriScopeMonitor : IDisposable
    {
        public string Status { get; private set; }
        public bool Owned { get { return false; } }
        public void Start() { }
        public void Dispose() { }
    }
}
