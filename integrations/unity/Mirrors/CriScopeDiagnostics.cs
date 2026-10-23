using UnityEngine;

namespace CriScope.Unity
{
    // Resource-project mirror for Inspector script identity; runtime capture lives in Dev.
    public sealed class CriScopeDiagnostics : MonoBehaviour
    {
        public static bool CaptureEnabled { get { return false; } }
        public static string Host { get { return ""; } }
        public static string Status { get { return "Disabled"; } }

        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1") { return !enabled; }
        public static bool Connect(string address = "127.0.0.1") { return false; }
        public static void Disconnect() { }
    }
}
