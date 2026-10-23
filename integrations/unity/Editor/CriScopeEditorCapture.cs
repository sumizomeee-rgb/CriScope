#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using CriWare;

namespace CriScope.Unity.Editor
{
    /// <summary>Editor lifecycle adapter. The host owns initialization of its audio preview environment.</summary>
    [InitializeOnLoad]
    public static class CriScopeEditorCapture
    {
        private static CriScopeCaptureSession session;
        private static string host = "127.0.0.1", lastStatus = "未开启";
        public static Action InitializeAudio;
        public static string ClientName = "Unity Editor";
        static CriScopeEditorCapture()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Disconnect;
            EditorApplication.quitting += Disconnect;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }
        public static bool CaptureEnabled { get { return session != null && session.CaptureEnabled; } }
        public static string Host { get { return host; } }
        public static string Status { get { return session == null ? lastStatus : session.Status; } }
        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1")
        {
            if (!enabled) { Disconnect(); return true; }
            string nextHost = string.IsNullOrWhiteSpace(targetHost) ? "127.0.0.1" : targetHost.Trim();
            if (Uri.CheckHostName(nextHost) == UriHostNameType.Unknown)
            { if (!CaptureEnabled) lastStatus = "无法开启：服务 IP 或主机名格式无效"; return false; }
            if (CaptureEnabled) return string.Equals(host, nextHost, StringComparison.OrdinalIgnoreCase);
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying)
            { lastStatus = "请等待脚本编译、资源导入或 Play 模式切换完成"; return false; }
            Disconnect();
            try
            {
                if (!CriAtomPlugin.IsLibraryInitialized() && InitializeAudio != null) InitializeAudio();
                session = CriScopeCaptureSession.Start(nextHost, ClientName, () => EditorApplication.timeSinceStartup);
                host = nextHost;
                EditorApplication.update += Tick;
                return true;
            }
            catch (Exception error)
            { Disconnect(); lastStatus = "无法开启：" + error.Message; Debug.LogWarning("[CriScope] " + lastStatus); return false; }
        }
        private static void Tick()
        {
            if (session == null) return;
            try
            {
                if (!Application.isPlaying && CriAtomPlugin.IsLibraryInitialized())
                { CriAtomPlugin.ExecuteQueuedEventCallbacks(); CriAtomPlugin.ExecuteQueuedBeatSyncCallbacks(); }
                session.Tick();
                if (!session.CaptureEnabled) Disconnect();
            }
            catch (Exception error)
            { Disconnect(); lastStatus = "采集已停止：" + error.Message; Debug.LogWarning("[CriScope] " + lastStatus); }
        }
        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode && state != PlayModeStateChange.ExitingPlayMode) return;
            if (!CaptureEnabled) return;
            Disconnect(); lastStatus = "已关闭：切换 Play 模式后请重新开启采集";
        }
        public static void Disconnect()
        {
            EditorApplication.update -= Tick;
            if (session == null) return;
            session.Dispose(); lastStatus = session.Status; session = null;
        }
    }
}
#endif
