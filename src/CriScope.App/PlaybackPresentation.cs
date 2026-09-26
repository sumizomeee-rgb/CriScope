using System.Globalization;
using CriScope.Core;

namespace CriScope.App;

public sealed record PlaybackGroup(string Id, WireEvent? Request, WireEvent? End, WireEvent[][] Voices)
{
    private (WireEvent Begin, WireEvent? End)[]? intervals;
    public double ObservedThrough { get; init; }
    public WireEvent? StopEvidence { get; init; }
    public WireEvent? Discontinuity { get; init; }
    public WireEvent? ReleaseEvidence { get; init; }
    public string Name => Request?.name ?? Voices.SelectMany(v => v).FirstOrDefault()?.name ?? End?.name ?? "播放实例（名称未记录）";
    public string PlayerId => Request?.parentId ?? "";
    public (WireEvent Begin, WireEvent? End)[] VoiceIntervals => intervals ??= Voices.Select(v =>
        (v.FirstOrDefault(e => e.kind == "play") ?? v[0], v.LastOrDefault(e => e.kind == "stop"))).ToArray();
    public bool UnknownStart => Request == null || PlaybackPresentation.IsUnknownStart(Request) || VoiceIntervals.Any(v => v.Begin.kind != "play" || PlaybackPresentation.IsUnknownStart(v.Begin));
    public bool HasEvidenceGap => Discontinuity != null;
    public double? RequestAt => Request != null && !PlaybackPresentation.IsUnknownStart(Request) ? Request.time : null;
    public double? StartedAt => VoiceIntervals.Length > 0 && VoiceIntervals.All(v => v.Begin.kind == "play" && !PlaybackPresentation.IsUnknownStart(v.Begin))
        ? VoiceIntervals.Min(v => v.Begin.time) : null;
    // Voice lifetime and Cue instance lifetime are different evidence; neither is a file duration.
    public double? EndedAt => VoiceIntervals.Length > 0 && VoiceIntervals.All(v => v.End != null) ? VoiceIntervals.Max(v => v.End!.time) : null;
    public double? InstanceEndedAt => End?.time;
    public double? InstanceReleasedAt => ReleaseEvidence?.time;
    public string EndReason => StopEvidence != null ? EventSemantics.EndReason(StopEvidence) :
        End != null && Voices.SelectMany(v=>v).Where(e=>e.kind=="stop").Select(e=>e.endReason).Distinct().ToArray() is {Length:1} reasons ? reasons[0] : "";
    public double? StopRequestedAt => StopEvidence?.kind == "stop-request" ? StopEvidence.time : null;
    public string EndReasonLabel => ReasonLabel(EndReason);
    public static string ReasonLabel(string reason) => reason switch {
        "natural" => "自然结束", "playback-stop" => "主动停止此实例", "playback-stop-immediate" => "立即停止此实例",
        "player-stop" => "播放器主动停止", "player-stop-immediate" => "播放器立即停止", "playback-limit" => "播放数量限制", _ => "未确认" };
    public string CausePlaybackId => StopEvidence == null ? "" : EventSemantics.CauseId(StopEvidence);
    public int ActiveVoiceCount => End != null || HasEvidenceGap ? 0 : VoiceIntervals.Count(v => v.Begin.kind == "play" && v.End == null);
    public string StatusLabel => EndReason == "playback-limit" && End != null ? "数量限制终止" : End != null ? (VoiceIntervals.Length == 0 ? Request == null || HasEvidenceGap ? "已结束 · Voice 记录不完整" : "未分配 Voice · 已结束" : "已结束")
        : HasEvidenceGap ? "采集中断 · 状态待确认" : ActiveVoiceCount > 0 ? StopRequestedAt != null ? "停止中" : "播放中"
        : VoiceIntervals.Length > 0 ? "声部已结束 · 实例待结束" : "等待 Voice 分配";
    public double? DurationAt(double end)
    {
        if (StartedAt is not { } start || HasEvidenceGap) return null;
        // A Cue end without the matching Voice release is not an observed Voice duration.
        if (EndedAt is { } stopped) return Math.Max(0, Math.Min(end, stopped) - start);
        return End == null ? Math.Max(0, Math.Min(end, ObservedThrough) - start) : null;
    }
    public double? InstanceDurationAt(double end) => RequestAt is { } start && (!HasEvidenceGap || End != null)
        ? Math.Max(0, Math.Min(end, InstanceEndedAt ?? ObservedThrough) - start) : null;
    public string DetailSummary
    {
        get
        {
            var duration = DurationAt(ObservedThrough);
            string timing = duration is { } seconds ? " · 声部历时 " + seconds.ToString("0.000", CultureInfo.InvariantCulture) + " 秒" : "";
            string boundary = StartedAt == null && UnknownStart ? " · 开始未记录" : Request == null ? " · 请求未记录" : "";
            return StatusLabel + " · " + Voices.Length + " 个 Voice" + timing + boundary;
        }
    }
}

/// <summary>Only observed settings are returned, never inferred final DSP values.</summary>
public sealed record PlaybackControls(WireEvent[] BeforeStart, WireEvent[] DuringPlayback);

public static class PlaybackPresentation
{
    public static PlaybackGroup[] Group(IEnumerable<WireEvent> events, double end)
    {
        var source = events.Where(e => e.time <= end && (e.kind == "request" && e.entity == "cue"
            || e.entity == "voice" && e.kind is "play" or "stop" || e.kind == "stop-request" || e.kind == "gap" || e.entity == "capture-segment" || EventSemantics.IsPlaybackEnd(e)))
            .OrderBy(e => e.time).ThenBy(e => e.seq).ToArray();
        var requests = source.Where(e => e.kind == "request" && e.entity == "cue").GroupBy(e => e.objectId).ToDictionary(g => g.Key, g => g.First());
        var endings = source.Where(EventSemantics.IsPlaybackEnd).GroupBy(e => e.objectId).ToDictionary(g => g.Key, g => g.ToArray());
        var voices = source.Where(e => e.entity == "voice" && e.kind is "play" or "stop")
            .GroupBy(e => e.objectId).Select(g => g.ToArray()).ToArray();
        var byPlayback = voices.GroupBy(v => v.Select(e => e.parentId).FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? "unlinked:" + v[0].objectId).ToDictionary(g => g.Key, g => g.ToArray());
        var gaps = source.Where(e => e.kind == "gap" || e.entity == "capture-segment").ToArray();
        return requests.Keys.Concat(byPlayback.Keys).Concat(endings.Keys).Distinct().Select(id =>
        {
            var request = requests.GetValueOrDefault(id);
            var groupVoices = byPlayback.GetValueOrDefault(id) ?? [];
            var ends = endings.GetValueOrDefault(id) ?? [];
            var ending = ends.FirstOrDefault();
            var first = request ?? groupVoices.SelectMany(v => v).MinBy(e => e.time) ?? ending;
            var boundary = ending == null ? end : Math.Max(ending.time, groupVoices.SelectMany(v => v).Where(e => e.kind == "stop").Select(e => e.time).DefaultIfEmpty(ending.time).Max());
            var gap = first == null ? null : gaps.FirstOrDefault(e => SameEvidenceStream(e, first) && Later(e, first) && e.time <= boundary);
            return new PlaybackGroup(id, request, ending, groupVoices)
            {
                ObservedThrough = end,
                StopEvidence = source.FirstOrDefault(e => e.objectId == id && e.kind == "stop-request") ?? ends.FirstOrDefault(e => EventSemantics.EndReason(e).Length > 0),
                ReleaseEvidence = ends.LastOrDefault(EventSemantics.IsPlaybackReleased),
                Discontinuity = gap
            };
        }).OrderBy(g => g.Request?.time ?? g.Voices.SelectMany(v => v).FirstOrDefault()?.time ?? g.End?.time ?? end).ToArray();
    }

    public static PlaybackControls ControlsFor(PlaybackGroup group, IEnumerable<WireEvent> events, double end)
    {
        var anchor = group.Request ?? group.Voices.SelectMany(v => v).MinBy(e => e.time);
        if (anchor == null) return new([], []);
        double until = Math.Min(end, group.InstanceEndedAt ?? end);
        var source = events.Where(e => e.time <= until && (e.kind is "aisac" or "selector" or "block" or "beat" or "sequence" or "gap" || e.entity == "capture-segment")).OrderBy(e => e.time).ThenBy(e => e.seq).ToArray();
        var before = new Dictionary<(string Kind, string Name), WireEvent>();
        var during = new List<WireEvent>();
        foreach (var e in source)
        {
            bool afterStart = Later(e, anchor);
            if (e.kind == "gap" || e.entity == "capture-segment")
            {
                if (!afterStart && SameEvidenceStream(e, anchor)) before.Clear();
                continue;
            }
            bool player = group.PlayerId.Length > 0 && e.objectId == group.PlayerId;
            bool instance = e.objectId == group.Id || e.parentId == group.Id;
            bool setting = e.kind is "aisac" or "selector" || e.kind == "block" && player;
            bool instanceEvent = e.kind is "block" or "beat" or "sequence";
            if (!(setting && player || instanceEvent && instance)) continue;
            if (afterStart)
            {
                during.Add(e);
                continue;
            }
            // An unknown start cannot place a setting before this playback with confidence.
            if (group.Request == null || IsUnknownStart(group.Request) || !setting || !player) continue;
            if (e.kind == "selector" && e.detail.Contains("清除全部", StringComparison.Ordinal))
                foreach (var key in before.Keys.Where(k => k.Kind == "selector").ToArray()) before.Remove(key);
            before[(e.kind, e.name)] = e;
        }
        return new(before.Values.OrderBy(e => e.kind).ThenBy(e => e.name).ToArray(), during.ToArray());
    }

    public static bool IsUnknownStart(WireEvent e) => e.detail.Contains("起点未知", StringComparison.Ordinal);
    private static bool SameEvidenceStream(WireEvent a, WireEvent b) =>
        (a.session.Length == 0 || b.session.Length == 0 || a.session == b.session) &&
        (a.channel.Length == 0 || b.channel.Length == 0 || a.channel == b.channel);
    private static bool Later(WireEvent a, WireEvent b) => a.time > b.time || a.time == b.time && a.seq > b.seq;
    public static string LatestLabel(string kind) => kind switch
    {
        "aisac" => "最近设置值", "selector" => "最近设置标签", "block" => "最近 Block 事件",
        "beat" => "最近节拍事件", "sequence" => "最近 Sequence 标签", _ => "最近收到的数据"
    };
}
