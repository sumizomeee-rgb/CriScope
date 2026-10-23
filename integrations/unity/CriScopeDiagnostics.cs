using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
using System;
#endif

namespace CriScope.Unity
{
    /// <summary>Runtime host. Collection, transport and metadata live in the shared SDK session.</summary>
    public sealed class CriScopeDiagnostics : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
        private static CriScopeDiagnostics current;
        private static string lastStatus = "Disabled";
        private static string host = "127.0.0.1";
        private CriScopeCaptureSession session;
        public static bool CaptureEnabled { get { return current != null && current.session != null && current.session.CaptureEnabled; } }
        public static string Host { get { return host; } }
        public static string Status { get { return current == null || current.session == null ? lastStatus : current.session.Status; } }
        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1")
        {
            if (!enabled)
            {
                if (current != null) { var old = current; old.Close(); Destroy(old.gameObject); }
                return true;
            }
            if (!Application.isPlaying) { lastStatus = "Requires Play Mode"; return false; }
            string nextHost = string.IsNullOrWhiteSpace(targetHost) ? "127.0.0.1" : targetHost.Trim();
            if (Uri.CheckHostName(nextHost) == UriHostNameType.Unknown) { lastStatus = "Invalid receiver hostname"; return false; }
            if (CaptureEnabled) return string.Equals(host, nextHost, StringComparison.OrdinalIgnoreCase);
            SetCaptureEnabled(false);
            GameObject go = null;
            CriScopeCaptureSession started = null;
            try
            {
                started = CriScopeCaptureSession.Start(nextHost, Application.isEditor ? "Unity Editor" : "Unity Player", () => Time.realtimeSinceStartup);
                go = new GameObject("CriScope SDK diagnostics");
                DontDestroyOnLoad(go);
                var component = go.AddComponent<CriScopeDiagnostics>();
                component.session = started;
                current = component;
                host = nextHost;
                started.Tick();
                return true;
            }
            catch (Exception error)
            {
                if (started != null) started.Dispose();
                if (go != null) Destroy(go);
                lastStatus = "Cannot start: " + error.Message;
                Debug.LogWarning("CriScope: " + lastStatus);
                return false;
            }
        }
        private void Update()
        {
            if (session == null) return;
            session.Tick();
            if (!session.CaptureEnabled) { Close(); Destroy(gameObject); }
        }
        private void OnApplicationQuit() { Close(); }
        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }
        private void Close()
        {
            if (session != null) { session.Dispose(); lastStatus = session.Status; session = null; }
            if (current == this) current = null;
        }
#else
        public static bool CaptureEnabled { get { return false; } }
        public static string Host { get { return ""; } }
        public static string Status { get { return "Not included in this build"; } }
        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1") { return !enabled; }
#endif
        public static bool Connect(string address = "127.0.0.1") { return SetCaptureEnabled(true, address); }
        public static void Disconnect() { SetCaptureEnabled(false); }
    }
}
