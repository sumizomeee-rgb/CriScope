using CriScope.Core;

namespace CriScope.App;

public sealed record PlaybackGroup(string Id, WireEvent? Request, WireEvent? End, WireEvent[][] Voices)
{
    public string Name => Request?.name ?? Voices.SelectMany(v => v).FirstOrDefault()?.name ?? "关联未提供的 Voice";
    public bool UnknownStart => Request == null || Request.detail.Contains("起点未知", StringComparison.Ordinal);
}

public static class PlaybackPresentation
{
    public static PlaybackGroup[] Group(IEnumerable<WireEvent> events, double end)
    {
        var source = events.Where(e => e.time <= end).ToArray();
        var requests = source.Where(e => e.kind == "request" && e.entity == "cue").GroupBy(e => e.objectId).ToDictionary(g => g.Key, g => g.First());
        var ends = source.Where(e => e.entity == "cue" && (e.kind == "stop" || e.detail.Contains("播放实例释放", StringComparison.Ordinal))).GroupBy(e => e.objectId).ToDictionary(g => g.Key, g => g.Last());
        var voices = source.Where(e => e.entity == "voice" && e.kind is "play" or "stop")
            .GroupBy(e => e.objectId).Select(g => g.OrderBy(e => e.time).ToArray()).ToArray();
        var byPlayback = voices.GroupBy(v => v.Select(e => e.parentId).FirstOrDefault(id => !string.IsNullOrEmpty(id)) ?? "unlinked:" + v[0].objectId).ToDictionary(g => g.Key, g => g.ToArray());
        return requests.Keys.Concat(byPlayback.Keys).Distinct().Select(id => new PlaybackGroup(id, requests.GetValueOrDefault(id), ends.GetValueOrDefault(id), byPlayback.GetValueOrDefault(id) ?? []))
            .OrderBy(g => g.Request?.time ?? g.Voices[0][0].time).ToArray();
    }
    public static string LatestLabel(string kind) => kind switch
    {
        "aisac" => "最近收到的控制值", "selector" => "最近收到的标签", "block" => "最近收到的 Block 状态",
        "beat" => "最近收到的节拍", "sequence" => "最近收到的序列事件", _ => "最近收到的数据"
    };
}
