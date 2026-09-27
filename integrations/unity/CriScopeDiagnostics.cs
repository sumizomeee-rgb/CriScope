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
        private string monitorNote;
        private bool subscribed, closing;
        private float nextSample, nextMemorySample, nextCategoryCatalogSample, lastCategoryCatalogEmit;
        [Serializable] private sealed class CategoryEntry { public string name; public int index, groupNo, ordinal; }
        [Serializable] private sealed class CategoryCatalog { public CategoryEntry[] firstGroupCategories; public int firstGroupNo; public string basis = "acf-category-catalog"; }
        [Serializable] private sealed class CueMetadata { public string[] categories; public CategoryEntry[] categoryDetails; public int firstGroupNo; public string basis = "cue-config"; }
        private int firstCategoryGroupNo = -1;
        private string categoryCatalogSignature;
        private readonly Dictionary<ushort, int> firstGroupOrdinals = new Dictionary<ushort, int>();
        private readonly Dictionary<uint, int> blocks = new Dictionary<uint, int>();
        private readonly HashSet<uint> tracked = new HashSet<uint>();
        private readonly Dictionary<uint, float> cueMetadata = new Dictionary<uint, float>();
        private readonly List<uint> ended = new List<uint>();
        public static bool CaptureEnabled { get { return current != null && current.bridge != null; } }
        public static string Host { get { return host; } }
        public static string Status { get { return current == null ? lastStatus : current.bridge.Status + (string.IsNullOrEmpty(current.monitorNote) ? "" : " | " + current.monitorNote); } }

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
            // The active destination is immutable. Disable first, edit it, then enable.
            if (CaptureEnabled) return host == nextHost;
            SetCaptureEnabled(false);
            CriScopeMonitor owner = new CriScopeMonitor();
            GameObject go = null;
            try
            {
                if (!CriAtomPlugin.IsLibraryInitialized()) throw new InvalidOperationException("Atom has not initialized");
                string nativeNote = null;
                try { owner.Start(); }
                catch (Exception e) { nativeNote = "原生启动不可用: " + e.Message; }
                go = new GameObject("CriScope SDK diagnostics");
                DontDestroyOnLoad(go);
                var component = go.AddComponent<CriScopeDiagnostics>();
                current = component;
                component.monitor = owner;
                component.monitorNote = nativeNote;
                host = nextHost;
                component.bridge = new CriScopeBridge(host, Application.isEditor ? "Unity Editor" : "Unity Player", Application.platform.ToString(), System.Diagnostics.Process.GetCurrentProcess().Id, nativeNote);
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
            SampleCategoryCatalog();
            if (Time.realtimeSinceStartup >= nextMemorySample)
            {
                nextMemorySample = Time.realtimeSinceStartup + 1f;
                Metric("memory.atom.bytes", Common.GetAtomMemoryUsage(), "SDK Atom allocator; bytes");
                Metric("memory.fs.bytes", Common.GetFsMemoryUsage(), "SDK file-system allocator; bytes; not all audio memory");
            }
            var stream = CriAtomExVoicePool.GetNumUsedVoices(CriAtomExVoicePool.VoicePoolId.StandardStreaming);
            Metric("voices.streaming.used", stream.numUsedVoices, "SDK StandardStreaming pool");
            Metric("voices.streaming.capacity", stream.numPoolVoices, "SDK StandardStreaming pool capacity");
            // Public CRI components expose their latest playback. This is not a complete native playback enumeration.
            foreach (var source in FindObjectsOfType<CriAtomSource>())
            {
                if (source.player == null) continue;
                var currentPlayback = source.player.GetLastPlaybackId();
                uint playbackId = currentPlayback.id;
                tracked.Add(playbackId);
                float metadataAt;
                if (playbackId == 0 || playbackId == uint.MaxValue || currentPlayback.status == CriAtomExPlayback.Status.Removed || (cueMetadata.TryGetValue(playbackId, out metadataAt) && Time.realtimeSinceStartup - metadataAt < 30)) continue;
                var acb = CriAtom.GetAcb(source.cueSheet);
                CriAtomEx.CueInfo cue;
                if (acb != null && !string.IsNullOrEmpty(source.cueName) && acb.GetCueInfo(source.cueName, out cue))
                {
                    cueMetadata[playbackId] = Time.realtimeSinceStartup;
                    var categoryNames = new List<string>();
                    var categoryDetails = new List<CategoryEntry>();
                    if (cue.categories != null) foreach (ushort index in cue.categories)
                    {
                        CriAtomExAcf.CategoryInfo category;
                        if (index == ushort.MaxValue || !CriAtomExAcf.GetCategoryInfoByIndex(index, out category) || string.IsNullOrEmpty(category.name) || categoryNames.Contains(category.name)) continue;
                        categoryNames.Add(category.name);
                        int ordinal;
                        if (category.groupNo <= int.MaxValue)
                            categoryDetails.Add(new CategoryEntry { name = category.name, index = index, groupNo = (int)category.groupNo, ordinal = firstGroupOrdinals.TryGetValue(index, out ordinal) ? ordinal : -1 });
                    }
                    bridge.Emit(new WireEvent { kind = "cue-info", objectId = "playback:" + playbackId, name = source.cueName, value = cue.length, raw = JsonUtility.ToJson(new CueMetadata { categories = categoryNames.ToArray(), categoryDetails = categoryDetails.ToArray(), firstGroupNo = firstCategoryGroupNo }),
                        detail = "SDK Cue 标注时长（毫秒）；组件最新播放的可选信息，不等于 Voice 实际历时" });
                }
            }
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
            foreach (var id in ended) { tracked.Remove(id); cueMetadata.Remove(id); }
        }
        private void SampleCategoryCatalog()
        {
            float now = Time.realtimeSinceStartup;
            if (now < nextCategoryCatalogSample) return;
            nextCategoryCatalogSample = now + .5f;
            int count;
            try { count = CriAtomExAcf.GetNumCategories(); }
            catch (Exception) { firstCategoryGroupNo = -1; firstGroupOrdinals.Clear(); return; }
            var all = new List<CategoryEntry>();
            for (int index = 0; index < count && index <= ushort.MaxValue; index++)
            {
                CriAtomExAcf.CategoryInfo info;
                if (!CriAtomExAcf.GetCategoryInfoByIndex((ushort)index, out info) || string.IsNullOrEmpty(info.name) || info.groupNo > int.MaxValue) continue;
                all.Add(new CategoryEntry { name = info.name, index = index, groupNo = (int)info.groupNo, ordinal = -1 });
            }
            int first = int.MaxValue;
            foreach (var entry in all) if (entry.groupNo < first) first = entry.groupNo;
            firstCategoryGroupNo = first == int.MaxValue ? -1 : first;
            firstGroupOrdinals.Clear();
            var members = new List<CategoryEntry>();
            foreach (var entry in all) if (entry.groupNo == firstCategoryGroupNo)
            {
                entry.ordinal = members.Count;
                members.Add(entry);
                firstGroupOrdinals[(ushort)entry.index] = entry.ordinal;
            }
            string signature = firstCategoryGroupNo + ":" + string.Join("|", members.ConvertAll(e => e.index + "/" + e.name).ToArray());
            if (signature == categoryCatalogSignature && now - lastCategoryCatalogEmit < 60f) return;
            categoryCatalogSignature = signature;
            lastCategoryCatalogEmit = now;
            bridge.Emit(new WireEvent { kind = "category-catalog", name = "ACF Category Group", raw = JsonUtility.ToJson(new CategoryCatalog { firstGroupNo = firstCategoryGroupNo, firstGroupCategories = members.ToArray() }),
                detail = "SDK ACF Category 顺序；仅用于实例颜色归属" });
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
            bool owned = monitor != null && monitor.Owned;
            try
            {
                if (bridge != null) { bridge.Emit(new WireEvent { kind = "state", name = "SDK diagnostics disabled", value = 0 }); bridge.Dispose(); }
            }
            finally
            {
                bridge = null;
                if (monitor != null) monitor.Dispose();
                lastStatus = owned ? "已关闭 · 本功能启动的 Monitor 已释放" : monitor != null && monitor.Status != null ? "已关闭 · 保留游戏原有 Monitor" : "已关闭";
                if (current == this) current = null;
            }
        }
#else
        // No polling, transport, callback subscription, or native private adapter in production builds.
        public static bool CaptureEnabled { get { return false; } }
        public static string Host { get { return ""; } }
        public static string Status { get { return "Not included in this build"; } }
        public static bool SetCaptureEnabled(bool enabled, string targetHost = "127.0.0.1") { return !enabled; }
#endif
        public static bool Connect(string address = "127.0.0.1") { return SetCaptureEnabled(true, address); }
        public static void Disconnect() { SetCaptureEnabled(false); }
    }
}
