using UnityEngine;
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using CriWare;
#endif

namespace CriScope.Unity
{
    /// <summary>Opt-in SDK observations. No application manager, AudioInfo, or play/pause hooks.</summary>
    public sealed class CriScopeDiagnostics : MonoBehaviour
    {
#if UNITY_EDITOR || DEVELOPMENT_BUILD || CRISCOPE_DIAGNOSTICS
        private static CriScopeDiagnostics current;
        private static string lastStatus = "Disabled";
        private static string host = "127.0.0.1";
        private CriScopeBridge bridge;
        private CriScopeMonitor monitor;
        private bool subscribed, closing;
        private float nextSample;
        private readonly Dictionary<uint, int> blocks = new Dictionary<uint, int>();
        private readonly HashSet<uint> tracked = new HashSet<uint>();
        private readonly List<uint> ended = new List<uint>();
        public static bool CaptureEnabled { get { return current != null && current.bridge != null; } }
        public static string Host { get { return host; } }
        public static string Status { get { return current == null ? lastStatus : current.monitor.Status + " | SDK " + current.bridge.Status; } }

        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1")
        {
            if (!enabled)
            {
                if (current != null) { var old = current; old.Close(); Destroy(old.gameObject); }
                return true;
            }
            if (!Application.isPlaying) { lastStatus = "Requires Play Mode"; return false; }
            string nextHost = string.IsNullOrEmpty(targetHost) ? "127.0.0.1" : targetHost.Trim();
            if (Uri.CheckHostName(nextHost) == UriHostNameType.Unknown) { lastStatus = "Invalid receiver hostname"; return false; }
            if (CaptureEnabled && host == nextHost) return true;
            SetCaptureEnabled(false);
            CriScopeMonitor owner = new CriScopeMonitor();
            GameObject go = null;
            try
            {
                owner.Start();
                go = new GameObject("CriScope SDK diagnostics");
                DontDestroyOnLoad(go);
                var component = go.AddComponent<CriScopeDiagnostics>();
                current = component;
                component.monitor = owner;
                host = nextHost;
                component.bridge = new CriScopeBridge(host, "CRI SDK", Application.platform.ToString(), System.Diagnostics.Process.GetCurrentProcess().Id);
                CriAtomExBeatSync.OnCallback += component.Beat;
                CriAtomExSequencer.OnCallback += component.Sequence;
                Application.logMessageReceived += component.Log;
                component.subscribed = true;
                component.bridge.Emit(new WireEvent { kind = "state", name = "SDK diagnostics enabled", value = 1,
                    detail = owner.Status + "; memory / streaming pools / BeatSync / Sequence / observed block. SDK dispatch clock, not native Monitor clock." });
                component.Sample();
                return true;
            }
            catch (Exception e)
            {
                if (current != null) current.Close(); else owner.Dispose();
                if (go != null) Destroy(go);
                lastStatus = "Cannot start: " + e.Message;
                Debug.LogWarning("CriScope: " + lastStatus);
                return false;
            }
        }
        private void Update()
        {
            if (bridge == null || Time.realtimeSinceStartup < nextSample) return;
            nextSample = Time.realtimeSinceStartup + 0.5f;
            try { Sample(); }
            catch (Exception e) { bridge.Emit(new WireEvent { kind = "warning", name = "SDK sample unavailable", detail = e.Message }); }
        }
        private void Sample()
        {
            if (!CriAtomPlugin.IsLibraryInitialized()) { SetCaptureEnabled(false); return; }
            Metric("memory.atom.bytes", Common.GetAtomMemoryUsage(), "SDK Atom allocator; bytes");
            Metric("memory.fs.bytes", Common.GetFsMemoryUsage(), "SDK file-system allocator; bytes; not all audio memory");
            var stream = CriAtomExVoicePool.GetNumUsedVoices(CriAtomExVoicePool.VoicePoolId.StandardStreaming);
            Metric("voices.streaming.used", stream.numUsedVoices, "SDK StandardStreaming pool");
            Metric("voices.streaming.capacity", stream.numPoolVoices, "SDK StandardStreaming pool capacity");
            // Public CRI components expose their latest playback. This is not a complete native playback enumeration.
            foreach (var source in FindObjectsOfType<CriAtomSource>())
                if (source.player != null) tracked.Add(source.player.GetLastPlaybackId().id);
            ended.Clear();
            foreach (uint id in tracked)
            {
                if (id == 0 || id == uint.MaxValue) { ended.Add(id); continue; }
                var playback = new CriAtomExPlayback(id);
                var status = playback.GetStatus();
                if (status == CriAtomExPlayback.Status.Removed) { ended.Add(id); blocks.Remove(id); continue; }
                int block = playback.GetCurrentBlockIndex();
                int previous;
                if (block >= 0 && (!blocks.TryGetValue(id, out previous) || previous != block))
                {
                    blocks[id] = block;
                    bridge.Emit(new WireEvent { kind = "block", objectId = "playback:" + id, name = "Current block", value = block,
                        detail = "SDK observed index; 500 ms poll, not exact audio transition time" });
                }
            }
            foreach (var id in ended) tracked.Remove(id);
        }
        private void Metric(string name, double value, string detail) { bridge.Emit(new WireEvent { kind = "metric", name = name, value = value, detail = detail }); }
        private void Beat(ref CriAtomExBeatSync.Info info)
        {
            if (closing || bridge == null) return;
            tracked.Add(info.playbackId);
            bridge.Emit(new WireEvent { kind = "beat", objectId = "playback:" + info.playbackId, name = "BeatSync", value = info.bpm,
                detail = "SDK dispatch; bar=" + info.barCount + "; beat=" + info.beatCount + "; beatsPerBar=" + info.numBeats +
                    "; progress=" + info.beatProgress.ToString(CultureInfo.InvariantCulture) + "; offsetMs=" + info.offset });
        }
        private void Sequence(ref CriAtomExSequencer.CriAtomExSequenceEventInfo info)
        {
            if (closing || bridge == null) return;
            tracked.Add(info.playbackId);
            bridge.Emit(new WireEvent { kind = "sequence", objectId = "playback:" + info.playbackId,
                name = info.tag == IntPtr.Zero ? "Sequence" : Marshal.PtrToStringAnsi(info.tag), value = info.id,
                detail = "SDK dispatch; sequencePosition=" + info.position + "; eventId=" + info.id });
        }
        private void Log(string message, string stack, LogType type)
        {
            if (bridge == null || closing || message == null) return;
            if (message.Contains("W2021090201") || message.Contains("W2021090202"))
                bridge.Emit(new WireEvent { kind = "warning", name = "CRI callback queue overflow", detail = message });
        }
        private void OnApplicationQuit() { Close(); }
        private void OnDisable() { Close(); }
        private void OnDestroy() { Close(); }
        private void Close()
        {
            if (closing) return;
            closing = true;
            if (subscribed)
            {
                CriAtomExBeatSync.OnCallback -= Beat;
                CriAtomExSequencer.OnCallback -= Sequence;
                Application.logMessageReceived -= Log;
                subscribed = false;
            }
            if (bridge != null) { bridge.Emit(new WireEvent { kind = "state", name = "SDK diagnostics disabled", value = 0 }); bridge.Dispose(); bridge = null; }
            bool owned = monitor != null && monitor.Owned;
            if (monitor != null) monitor.Dispose();
            lastStatus = owned ? "Disabled; owned Monitor stopped" : "Disabled; pre-existing Monitor preserved";
            if (current == this) current = null;
        }
#else
        // No polling, transport, callback subscription, or native private adapter in production builds.
        public static bool CaptureEnabled { get { return false; } }
        public static string Host { get { return ""; } }
        public static string Status { get { return "Not included in this build"; } }
        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1") { return !enabled; }
#endif
    }
}
