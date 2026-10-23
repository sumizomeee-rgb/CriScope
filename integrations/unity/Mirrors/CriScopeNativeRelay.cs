using System;

namespace CriScope.Unity
{
    // Resource-project mirror: no socket or native relay.
    internal sealed class CriScopeNativeRelay : IDisposable
    {
        public string Failure { get { return ""; } }
        public CriScopeNativeRelay(int pid, Func<byte[], bool> receive, int port = 2002) { }
        public void Start() { }
        public void Dispose() { }
    }
}
