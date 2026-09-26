using System.Globalization;
using System.Text.Json;

namespace CriScope.Core;

/// <summary>Maps native evidence without equating cue requests with allocated sound voices.</summary>
public sealed class NativeEventMapper(string session)
{
    private readonly Dictionary<string, string> categoryNames = new();
    private long sequence;
    private ulong captureStart;
    private double lastTime;
    private readonly Dictionary<string, (string Name, string Player)> playbacks = new();
    private readonly Dictionary<string, (string Name, int Cue)> playerCues = new();
    private readonly Dictionary<string, string> voices = new();
    private readonly Dictionary<string, (float X, float Y, float Z)> positions = new();
    private readonly Dictionary<string, int> suppressed = new();
    private readonly Dictionary<string, (string Value, double Time)> spatialState = new();
    private readonly Dictionary<string, Dictionary<string, (object Value, ulong Time)>> listeners = new();
    private readonly Dictionary<string, string> voiceSources = new();
    private readonly Dictionary<string, ulong> positionTimes = new();
    private readonly Dictionary<string, string> positionEvidence = new();
    private double lastSummary;
    private int segment;
    public long Sequence => sequence;

    public WireEvent Diagnostic(string text, string evidence = "") => new()
    {
        kind = "error", session = session, seq = ++sequence, time = lastTime, source = "cri-native",
        name = "原生协议诊断", detail = text, raw = evidence
    };

    public IEnumerable<WireEvent> Map(NativePacket packet)
    {
        var p = packet.Parameters;
        string S(string key) => Convert.ToString(p.FirstOrDefault(v => v.Name == key)?.Value, CultureInfo.InvariantCulture) ?? "";
        double N(string key) => double.TryParse(S(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var n) && double.IsFinite(n) ? n : 0;
        bool Has(string key) => p.Any(v => v.Name == key);
        string f = packet.Function;
        if (f == "StartLogging")
        {
            captureStart = packet.TimeMicroseconds;
            segment++;
            categoryNames.Clear(); playbacks.Clear(); playerCues.Clear(); voices.Clear(); positions.Clear(); spatialState.Clear();
            listeners.Clear(); voiceSources.Clear(); positionTimes.Clear(); positionEvidence.Clear();
        }
        double time = Math.Max(captureStart, packet.TimeMicroseconds) / 1_000_000d;
        lastTime = Math.Max(lastTime, time);
        if (time - lastSummary >= 5 && suppressed.Count > 0)
        {
            yield return new WireEvent { kind = "log", session = session, seq = ++sequence, time = time, source = "cri-native",
                name = "重复采样汇总", detail = $"省略 {suppressed.Values.Sum()} 条重复位置或限频姿态更新；原始更新未保存",
                raw = JsonSerializer.Serialize(suppressed) };
            suppressed.Clear(); lastSummary = time;
        }
        string playback = Has("ExPlaybackId_unique64") ? $"playback:{segment}:" + S("ExPlaybackId_unique64") : $"playback-id:{segment}:" + S("CriAtomExPlaybackId");
        string player = S("CriAtomExPlayerHn");
        string raw = JsonSerializer.Serialize(new { function = f, functionId = packet.FunctionId, timeMicroseconds = packet.TimeMicroseconds,
            command = packet.Command, payloadHex = packet.PayloadHex, parameters = p.Select(v => new { id = v.Id, name = v.Name, value = v.Value }) });
        WireEvent E(string kind, string name, string id = "", double value = 0, string entity = "", string parent = "", string detail = "") => new()
        {
            kind = kind, session = session, seq = ++sequence, time = time, name = name, objectId = id,
            value = value, source = "cri-native", entity = entity, parentId = parent, raw = raw, detail = detail
        };
        WireEvent? SourcePosition(string id)
        {
            if (!positions.TryGetValue(id, out var pos)) return null;
            var links = voiceSources.Where(v => v.Value == id && voices.ContainsKey(v.Key)).Select(v => voices[v.Key]).Distinct()
                .Select(owner => new { playback = owner, cue = playbacks.GetValueOrDefault(owner).Name ?? "Cue 名称未知" }).ToArray();
            var ev = E("position", links.Length == 0 ? "音源（未观测到播放关联）" : string.Join(" · ", links.Select(v => v.cue).Distinct()),
                id, entity: "source", detail: "CRI 音源世界坐标；名称来自原生 Voice/Cue 关联，不是 GameObject 名称");
            ev.x = pos.X; ev.y = pos.Y; ev.z = pos.Z;
            ev.raw = JsonSerializer.Serialize(new { native = JsonSerializer.Deserialize<JsonElement>(raw),
                derived = new { links, positionObservedAtMicroseconds = positionTimes.GetValueOrDefault(id), positionEvidence = positionEvidence.GetValueOrDefault(id) } });
            return ev;
        }
        if (packet.Command != 31)
        {
            yield return E("log", "原生连接消息", detail: $"command={packet.Command}; control={packet.Control}");
            yield break;
        }
        if (f.StartsWith("UnknownFunction:", StringComparison.Ordinal))
        {
            yield return E("error", "未知原生函数", detail: f); yield break;
        }
        if (f == "StartLogging")
        {
            yield return E("log", "原生采集开始", entity: "capture-segment", value: segment,
                detail: $"SDK {S("VersionString")}；采集段 {segment}；连接时已有声音的起点未知；旧段关联已清空"); yield break;
        }
        if (f.StartsWith("Ex3d", StringComparison.Ordinal))
        {
            bool listener = f.Contains("Listener", StringComparison.Ordinal);
            string spatialId = S(listener ? "CriAtomEx3dListenerHn" : "CriAtomEx3dSourceHn");
            if (f.EndsWith("_Destroy", StringComparison.Ordinal))
            {
                positions.Remove(spatialId); positionTimes.Remove(spatialId); positionEvidence.Remove(spatialId); listeners.Remove(spatialId);
                foreach (var key in spatialState.Keys.Where(k => k.StartsWith(spatialId + "/", StringComparison.Ordinal)).ToArray()) spatialState.Remove(key);
                foreach (var key in voiceSources.Where(v => v.Value == spatialId).Select(v => v.Key).ToArray()) voiceSources.Remove(key);
                yield return E("remove", listener ? "Listener 已销毁" : "音源已销毁", spatialId, entity: listener ? "listener" : "source", detail: "原生对象销毁");
                if (listener) yield return E("remove", "衰减监听点已移除", spatialId, entity: "distance-listener", detail: "所属 Listener 已销毁");
                yield break;
            }
            if (listener && spatialId.Length > 0)
            {
                if (!listeners.TryGetValue(spatialId, out var state)) listeners[spatialId] = state = new();
                foreach (var value in p.Where(v => v.Name is "3dPosVector_Position" or "3dPosVector_FocusPoint" or "3dDistanceFocusLevel" or "3dDirectionFocusLevel"))
                    state[value.Name] = (value.Value, packet.TimeMicroseconds);
                if (f == "Ex3dListener_Update" && state.TryGetValue("3dDistanceFocusLevel", out var level))
                {
                    double weight = Convert.ToDouble(level.Value, CultureInfo.InvariantCulture);
                    float[]? basePos = state.GetValueOrDefault("3dPosVector_Position").Value as float[];
                    float[]? focus = state.GetValueOrDefault("3dPosVector_FocusPoint").Value as float[];
                    if (weight >= 0 && weight <= 1 && (weight == 1 || basePos is { Length: 3 }) && (weight == 0 || focus is { Length: 3 }))
                    {
                        var distance = E("position", "衰减监听点", spatialId, entity: "distance-listener", detail: "Listener Update 提交；(1−权重)×Listener + 权重×Focus");
                        double Axis(int index) => weight == 0 ? basePos![index] : weight == 1 ? focus![index] : (1-weight)*basePos![index]+weight*focus![index];
                        distance.x=Axis(0);distance.y=Axis(1);distance.z=Axis(2);
                        distance.raw=JsonSerializer.Serialize(new { native=JsonSerializer.Deserialize<JsonElement>(raw), derived=new {
                            formula="(1-distanceFocusLevel)*listenerPosition+distanceFocusLevel*focusPoint", committedAtMicroseconds=packet.TimeMicroseconds,
                            inputs=state.Select(v=>new { name=v.Key, value=v.Value.Value, observedAtMicroseconds=v.Value.Time }) } });
                        yield return distance;
                    }
                }
            }
            foreach (var (label, keys) in new[] {
                ("朝向", new[] { "3dPosVector_Forward", "3dPosVector_Upward" }),
                ("聚焦", new[] { "3dPosVector_FocusPoint", "3dDistanceFocusLevel", "3dDirectionFocusLevel" }),
                ("速度", new[] { "3dPosVector_Velocity" }) })
            {
                var state = p.Where(v => keys.Contains(v.Name)).ToArray();
                if (state.Length == 0) continue;
                string key = spatialId + "/" + label + "/" + string.Join(",", state.Select(v => v.Name));
                string value = JsonSerializer.Serialize(state.Select(v => new { v.Name, v.Value }));
                if (spatialState.TryGetValue(key, out var prior) && prior.Value == value)
                    suppressed[f + "/" + label] = suppressed.GetValueOrDefault(f + "/" + label) + 1;
                else
                {
                    spatialState[key] = (value, time);
                    yield return E("log", (listener ? "Listener " : "音源 ") + label, spatialId,
                        entity: listener ? "listener" : "source", detail: "原生字段变更；完整向量见原生详情");
                }
            }
            if (!Has("3dPosVector_Position") && (f.Contains("SetOrientation") || f.Contains("SetVelocity") || f.Contains("Focus"))) yield break;
        }
        if (f is "ExPlayer_SetCueId" or "ExPlayer_SetCueName" or "ExPlayer_SetCueIndex")
        {
            var name = S("cue_name");
            if (name.Length == 0) name = S("cue_id");
            playerCues[player] = (name, Has("cue_id") ? (int)N("cue_id") : -1);
        }
        if (f == "ExPlaybackId")
        {
            var name = S("cue_name");
            if (name.Length == 0) name = playerCues.GetValueOrDefault(player).Name ?? "Cue";
            playbacks[playback] = (name, player);
            var ev = E("request", name, playback, entity: "cue", parent: player,
                detail: packet.TimeMicroseconds < captureStart ? "连接时已有播放；起点未知" : "CRI Cue 播放实例；不等同于实际 Voice");
            ev.lifecycle = "created";
            ev.cue = playerCues.TryGetValue(player, out var selectedCue) ? selectedCue.Cue : -1;
            yield return ev;
            foreach (var source in voiceSources.Where(v => voices.GetValueOrDefault(v.Key) == playback).Select(v => v.Value).Distinct())
                if (SourcePosition(source) is { } sourceEvent) yield return sourceEvent;
            yield break;
        }
        if (f is "SoundVoice_Allocate" or "SoundVoice_FreeVoice")
        {
            string id = $"voice:{segment}:" + S("CriAtomSoundVoiceId_unique64");
            string? changedSource = null;
            if (f == "SoundVoice_Allocate")
            {
                voices[id] = playback;
                if (Has("CriAtomEx3dSourceHn") && S("CriAtomEx3dSourceHn") is not ("" or "0x0"))
                    voiceSources[id] = changedSource = S("CriAtomEx3dSourceHn");
            }
            else if (voices.Remove(id, out var owner)) playback = owner;
            if (f == "SoundVoice_FreeVoice" && voiceSources.Remove(id, out var oldSource)) changedSource = oldSource;
            var info = playbacks.GetValueOrDefault(playback);
            var voiceEvent = E(f == "SoundVoice_Allocate" ? "play" : "stop", info.Name ?? "Voice", id, entity: "voice", parent: playback,
                detail: f == "SoundVoice_FreeVoice" ? "原生 Voice 释放；reason=" + S("VoiceStopReason") :
                packet.TimeMicroseconds <= captureStart ? "连接时已存在 Voice；起点未知" : "原生 Voice 分配");
            voiceEvent.lifecycle = f == "SoundVoice_Allocate" ? "allocated" : "released";
            yield return voiceEvent;
            if (changedSource != null && SourcePosition(changedSource) is { } changedPosition) yield return changedPosition;
            yield break;
        }
        if (f == "ExCue_StopByLimit")
        {
            var ev = E("stop", playbacks.GetValueOrDefault(playback).Name ?? "Cue", playback, entity: "cue",
                detail: "播放数量限制触发停止；关联实例见 causeId；实例释放另行记录");
            ev.lifecycle = "stopped"; ev.endReason = "playback-limit";
            ev.causeId = Has("cause ExPlaybackId_unique64") ? $"playback:{segment}:" + S("cause ExPlaybackId_unique64") :
                Has("cause CriAtomExPlaybackId") ? $"playback-id:{segment}:" + S("cause CriAtomExPlaybackId") : "";
            yield return ev; yield break;
        }
        if (f == "ExPlaybackInfo_FreeInfo")
        {
            var ev = E("log", playbacks.GetValueOrDefault(playback).Name ?? "Cue 结束", playback, entity: "cue", detail: "CRI 播放实例释放");
            ev.lifecycle = "released";
            yield return ev;
            playbacks.Remove(playback); yield break;
        }
        if (f == "ExCategoryConfig" && Has("Index"))
        {
            var index = S("Index"); categoryNames[index] = S("CategoryName");
            yield return E("category-info", S("CategoryName"), "category-index:" + index, entity: "category", detail: "原生 Category 配置"); yield break;
        }
        if (f == "ExCategory_IncrementNumPlaybackCues" && Has("Index") && (Has("ExPlaybackId_unique64") || Has("CriAtomExPlaybackId")))
        {
            var index = S("Index");
            yield return E("category", categoryNames.GetValueOrDefault(index, "Category 索引 " + index),
                "category-index:" + index, entity: "category", parent: playback, detail: "原生播放归属"); yield break;
        }
        if (f.Contains("Aisac", StringComparison.Ordinal) && Has("AisacControlValue"))
        {
            yield return E("aisac", Has("AisacControlName") ? S("AisacControlName") : "AISAC " + S("AisacControlId"),
                player.Length > 0 ? player : "category:" + (Has("CategoryId") ? S("CategoryId") : S("CategoryName")), N("AisacControlValue"), "control", detail: "当前收到的控制值"); yield break;
        }
        if (f.Contains("Selector", StringComparison.Ordinal))
        {
            yield return E("selector", Has("SelectorName") ? S("SelectorName") : "全部 Selector", player, entity: "control",
                detail: f.Contains("Clear") ? "清除全部选择" : f.Contains("Unset") ? "清除选择" : S("LabelName")); yield break;
        }
        if (f.Contains("Block", StringComparison.Ordinal) && Has("BlockIndex"))
        {
            yield return E("block", f.Contains("Next") ? "请求下一 Block" : "设置首个 Block", player.Length > 0 ? player : playback,
                N("BlockIndex"), "control", detail: "请求值；不是已发生的 Block 切换回调"); yield break;
        }
        if (f == "CpuLoadAndNumUsedVoices")
        {
            foreach (var (key, name, unit) in new[] { ("CpuLoad", "CRI CPU", "%"), ("NumUsedVoices", "播放声部", "个"),
                ("AverageServerTime", "CRI 服务平均耗时", "µs"), ("MaxServerTime", "CRI 服务峰值耗时", "µs"), ("NumUsedPlayers", "播放实例", "个") })
                if (Has(key)) yield return E("metric", name, key, N(key), "resource", detail: unit);
            yield break;
        }
        if (f == "StreamingInfo")
        {
            if (Has("NumUsedVoices")) yield return E("metric", "流式播放声部", "stream.used", N("NumUsedVoices"), "resource", detail: "个");
            if (Has("TotalBps")) yield return E("metric", "流式读取速率", "stream.bps", N("TotalBps"), "resource", detail: "bit/s");
            yield break;
        }
        if (f.Contains("LoudnessInfo", StringComparison.Ordinal))
        {
            foreach (var (key, name) in new[] { ("MomentaryValue", "瞬时响度"), ("ShortTermValue", "短时响度"), ("IntegratedValue", "综合响度") })
                if (Has(key)) yield return E("metric", name, "rack:" + S("RackId") + "/" + key, N(key), "loudness", detail: "LKFS");
            yield break;
        }
        if (f.EndsWith("VoicePoolConfig", StringComparison.Ordinal) && Has("num_voices"))
        {
            yield return E("metric", N("streaming_flag") != 0 ? "流式声池容量" : "内存声池容量", S("CriAtomExVoicePoolHn"), N("num_voices"), "voice-pool", detail: "个；单个声池容量");
            yield break;
        }
        if (f.Contains("BusAnalyze", StringComparison.Ordinal))
        {
            var peaks = p.Where(v => v.Name == "PeakLevel").Select(v => Convert.ToDouble(v.Value)).ToArray();
            var rms = p.Where(v => v.Name == "RmsLevel").Select(v => Convert.ToDouble(v.Value)).ToArray();
            var hold = p.Where(v => v.Name == "PeakHoldLevel").Select(v => Convert.ToDouble(v.Value)).ToArray();
            if (peaks.Length != rms.Length || peaks.Length != hold.Length || (Has("NumCh") && (int)N("NumCh") != peaks.Length))
            { yield return E("error", "Bus 声道数据不完整", detail: "保留原始包；不补零"); yield break; }
            var ev = E("bus", S("BusName").Length > 0 ? S("BusName") : "Bus " + S("BusNo"),
                "rack:" + S("RackId") + "/bus:" + S("BusNo"), peaks.DefaultIfEmpty().Max(), "bus", detail: "线性幅值；0 表示静音");
            ev.raw = JsonSerializer.Serialize(new { function = f, timeMicroseconds = packet.TimeMicroseconds,
                channels = peaks.Select((peak, i) => new { channel = i + 1, peak, rms = rms[i], hold = hold[i] }), parameters = p });
            yield return ev; yield break;
        }
        if (f.StartsWith("Ex3d", StringComparison.Ordinal) && Has("3dPosVector_Position"))
        {
            bool listener = f.Contains("Listener", StringComparison.Ordinal);
            string id = S(listener ? "CriAtomEx3dListenerHn" : "CriAtomEx3dSourceHn");
            var coordinates = (float[])p.First(v => v.Name == "3dPosVector_Position").Value;
            if (coordinates.All(float.IsFinite))
            {
                var position = (coordinates[0], coordinates[1], coordinates[2]);
                positionTimes[id] = packet.TimeMicroseconds; positionEvidence[id] = raw;
                if (positions.TryGetValue(id, out var previous) && previous == position)
                {
                    suppressed[f] = suppressed.GetValueOrDefault(f) + 1; yield break;
                }
                positions[id] = position;
                if (!listener) { if (SourcePosition(id) is { } sourceEvent) yield return sourceEvent; yield break; }
                var ev = E("position", listener ? "Listener" : "音源", id, entity: listener ? "listener" : "source", detail: "世界坐标");
                ev.x = coordinates[0]; ev.y = coordinates[1]; ev.z = coordinates[2]; yield return ev;
            }
            yield break;
        }
        if (f.Contains("Pause", StringComparison.Ordinal))
        {
            yield return E("log", "暂停状态请求", player.Length > 0 ? player : playback, entity: "control", detail: f); yield break;
        }
        if (f.Contains("Error", StringComparison.Ordinal) || f.Contains("Warning", StringComparison.Ordinal))
        {
            yield return E("error", "CRI 诊断", detail: string.Join("; ", p.Select(v => v.Name + "=" + v.Value))); yield break;
        }
        // Every recognized but not yet projected log remains inspectable as native evidence.
        yield return E("log", f, player.Length > 0 ? player : Has("CriAtomExPlaybackId") ? playback : "", detail: "原生明细");
    }
}
