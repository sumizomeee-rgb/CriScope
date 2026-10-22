using System.Globalization;
using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

public sealed record SpatialDistance(WireEvent Source, WireEvent Listener, double Value, bool Bound);
public sealed record PlaybackSpatialMoment(string Phase, double At, WireEvent[] Sources, WireEvent[] Listeners,
    SpatialDistance[] Distances, string PlayerPan)
{
    public WireEvent[] Points => Sources.Concat(Listeners).ToArray();
}
public sealed record PlaybackSpatialReport(PlaybackGroup Playback, PlaybackSpatialMoment Start,
    PlaybackSpatialMoment Last, string CuePan, string Range, string Evidence);
public sealed record HistoricalSpatialScene(string PlaybackId, string Name, PlaybackSpatialMoment Moment);

/// <summary>Reconstructs one playback, retaining bounded spatial state rather than a recording's raw history.</summary>
public static class PlaybackSpatialPresentation
{
    public static string PanLabel(int value) => value switch
    {
        0 => "Pan3d（声像定位）", 1 => "Pos3d（空间定位）", 2 => "Auto（自动）",
        -1 => "Unknown（未知）", _ => $"未知枚举值 {value}"
    };

    public static PlaybackSpatialReport Resolve(IEnumerable<WireEvent> events, PlaybackGroup playback,
        double through, WireEvent? configuration = null, string evidence = "内存窗口")
    {
        var anchor = AssociationPresentation.Anchor(playback);
        var begin = playback.Voices.SelectMany(v => v).Where(e => e.kind == "play").MinBy(e => (e.time, e.seq)) ?? anchor;
        var stop = playback.Voices.SelectMany(v => v).Where(e => e.kind == "stop").MaxBy(e => (e.time, e.seq));
        if (playback.EndedAt == null) stop = playback.End;
        if (playback.Discontinuity is { } gap && (stop == null || gap.time <= stop.time)) stop = gap;
        double startAt = begin?.time ?? through, endAt = Math.Min(through, stop?.time ?? through);
        var start = new Frame(playback, "开始时", startAt, begin?.seq ?? long.MaxValue);
        var last = new Frame(playback, playback.HasEvidenceGap ? "中断前" : stop != null ? "结束前" : "当前观测", endAt,
            stop?.seq is > 0 ? stop.seq - 1 : long.MaxValue);
        foreach (var e in events)
        {
            if (anchor != null && (e.session != anchor.session || e.epoch != anchor.epoch)) continue;
            start.Observe(e); last.Observe(e);
        }
        var (pan, range) = Configuration(configuration);
        if (playback.UnknownStart) evidence += " · 播放开始未完整记录";
        if (playback.HasEvidenceGap) evidence += " · 采集中断，结束位置未确认";
        return new(playback, start.Finish(), last.Finish(), pan, range, evidence);
    }

    public static PlaybackSpatialReport FromRecording(Session session, PlaybackGroup playback,
        double through, WireEvent? configuration, CancellationToken cancellationToken)
    {
        if (!session.FlushRecordingForRead()) throw new IOException(session.Error);
        var anchor = AssociationPresentation.Anchor(playback) ?? throw new InvalidDataException("播放实例缺少记录标识");
        var paths = session.RecordingPaths;
        long watermark = session.Watermark;
        var lifecycle = RecordingEvidenceReader.ReadRange(paths, 1, watermark, session.Id, cancellationToken)
            .Where(e => e.epoch == anchor.epoch && (e.objectId == playback.Id || e.parentId == playback.Id ||
                e.kind == "gap" || e.entity == "capture-segment"))
            .Where(e => e.kind is "request" or "play" or "stop" or "stop-request" or "gap" ||
                EventSemantics.IsPlaybackEnd(e) || e.entity == "capture-segment").ToArray();
        var restored = PlaybackPresentation.Group(lifecycle, through).FirstOrDefault(p => p.Id == playback.Id) ?? playback;
        return Resolve(RecordingEvidenceReader.ReadRange(paths, 1, watermark, session.Id, cancellationToken),
            restored, through, configuration, "录制文件");
    }

    public static PlaybackSpatialReport UpdateConfiguration(PlaybackSpatialReport report, WireEvent info)
    {
        var (pan,range)=Configuration(info);
        return report with {CuePan=pan,Range=range};
    }

    private static (string Pan, string Range) Configuration(WireEvent? info)
    {
        if (string.IsNullOrWhiteSpace(info?.raw)) return ("未记录", "未记录");
        try
        {
            using var doc = JsonDocument.Parse(info.raw);
            var root = doc.RootElement;
            var pan = root.TryGetProperty("panType", out var p) && p.TryGetInt32(out var value) ? PanLabel(value) : "未记录";
            var range = root.TryGetProperty("minAttenuationDistance", out var min) && min.TryGetDouble(out var a) &&
                root.TryGetProperty("maxAttenuationDistance", out var max) && max.TryGetDouble(out var b) &&
                double.IsFinite(a) && double.IsFinite(b) ? $"{a:0.###} — {b:0.###} 坐标单位（Cue 配置）" : "未记录";
            return (pan, range);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return ("未记录", "未记录"); }
    }

    public static string Parameter(WireEvent e, string key)
    {
        if (string.IsNullOrWhiteSpace(e.raw)) return "";
        try
        {
            using var doc = JsonDocument.Parse(e.raw);
            var root = doc.RootElement;
            if (root.TryGetProperty("native", out var native)) root = native;
            if (!root.TryGetProperty("parameters", out var fields) || fields.ValueKind != JsonValueKind.Array) return "";
            foreach (var p in fields.EnumerateArray())
                if (p.TryGetProperty("name", out var name) && name.GetString() == key && p.TryGetProperty("value", out var value))
                    return value.ToString();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return "";
    }

    public static double SampleTime(WireEvent e)
    {
        try
        {
            using var doc = JsonDocument.Parse(e.raw);
            if (doc.RootElement.TryGetProperty("derived", out var derived))
            {
                if (derived.TryGetProperty("committedAtMicroseconds", out var t) && t.TryGetDouble(out var committed)) return committed / 1e6;
                if (derived.TryGetProperty("positionEvidence", out var evidence) && evidence.ValueKind == JsonValueKind.String &&
                    evidence.GetString() is { Length: > 0 } json)
                {
                    using var position = JsonDocument.Parse(json);
                    if (position.RootElement.TryGetProperty("submittedAtMicroseconds", out t) && t.TryGetDouble(out committed)) return committed / 1e6;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { }
        return e.time;
    }

    private sealed class Frame(PlaybackGroup playback, string phase, double at, long sequence)
    {
        private readonly Dictionary<(string Entity, string Id), WireEvent> positions = [];
        private readonly Dictionary<string, WireEvent> remembered = [];
        private readonly Dictionary<string, (string Source, string Listener)> voices = [];
        private readonly HashSet<(string Source, string Listener)> pairs = [];
        private string playerPan = "未记录";
        private bool HasSource(string id) => voices.Values.Any(v => v.Source == id);
        public void Observe(WireEvent e)
        {
            if (e.time > at || e.time == at && e.seq > sequence) return;
            if (e.kind == "gap" || e.entity == "capture-segment")
            { positions.Clear(); remembered.Clear(); voices.Clear(); pairs.Clear(); playerPan = "未记录"; return; }
            if (playback.PlayerId.Length > 0 && e.objectId == playback.PlayerId && e.kind == "log")
            {
                if (e.name is "ExPlayer_Create" or "ExPlayer_Destroy" or "ExPlayer_ResetParameters") playerPan = "未记录";
                if (e.name == "ExPlayer_SetPanType" && int.TryParse(Parameter(e, "PanType"), out var value)) playerPan = PanLabel(value);
            }
            if (e.entity == "voice" && e.parentId == playback.Id)
            {
                if (e.kind == "play")
                {
                    var source = Handle(e, "CriAtomEx3dSourceHn");
                    var listener = Handle(e, "CriAtomEx3dListenerHn");
                    voices[e.objectId] = (source, listener);
                    if (source.Length > 0 && positions.TryGetValue(("source", source), out var point)) remembered[source] = point;
                    if (source.Length > 0 && listener.Length > 0) pairs.Add((source, listener));
                }
                else if (e.kind == "stop") voices.Remove(e.objectId);
            }
            if (e.kind is "remove" or "spatial-remove")
            {
                positions.Remove((e.entity, e.objectId));
                if (e.entity == "listener")
                {
                    positions.Remove(("distance-listener", e.objectId));
                    pairs.RemoveWhere(pair=>pair.Listener==e.objectId);
                }
                if (e.entity == "source") pairs.RemoveWhere(pair=>pair.Source==e.objectId);
                if (e.entity == "source")
                    foreach (var voice in voices.Where(v => v.Value.Source == e.objectId).Select(v => v.Key).ToArray()) voices.Remove(voice);
                return;
            }
            if (e.kind != "position" || e.entity is not ("source" or "listener" or "distance-listener") ||
                !double.IsFinite(e.x) || !double.IsFinite(e.y) || !double.IsFinite(e.z)) return;
            positions[(e.entity, e.objectId)] = e;
            if (e.entity == "source" && (HasSource(e.objectId) || AssociationPresentation.SourcePlaybackIds(e).Contains(playback.Id)))
                remembered[e.objectId] = e;
        }
        public PlaybackSpatialMoment Finish()
        {
            var sources = remembered.Values.ToArray();
            var listeners = positions.Values.Where(e => e.entity != "source").ToArray();
            var distances = (from source in sources from listener in listeners
                let dx = source.x - listener.x let dy = source.y - listener.y let dz = source.z - listener.z
                let distance = Math.Sqrt(dx * dx + dy * dy + dz * dz)
                where double.IsFinite(distance)
                select new SpatialDistance(source, listener, distance, pairs.Contains((source.objectId, listener.objectId)))).ToArray();
            return new(phase, at, sources, listeners, distances, playerPan);
        }
        private static string Handle(WireEvent e, string key)
        {
            var value = Parameter(e, key);
            if (value is "" or "0x0" or "0") return "";
            return e.epoch > 0 ? e.epoch + ":" + value : value;
        }
    }
}
