using CriScope.Core;

namespace CriScope.App;

public static class EventLogPresentation
{
    public static string Action(WireEvent e) => e.kind switch
    {
        "request" => "请求播放", "play" => "开始播放", "stop" when e.entity == "voice" => "声部结束",
        "stop" => "停止播放", "log" when EventSemantics.IsPlaybackEnd(e) => "实例结束",
        "aisac" => "AISAC", "selector" => "Selector", "block" => "Block", "beat" => "BeatSync",
        "sequence" => "Sequence", "category" => "Category", "error" => "错误", "gap" => "采集缺口", "state" => "连接状态", _ => "诊断"
    };
    public static bool Includes(WireEvent e, string category) => category switch
    {
        "播放" => e.kind is "request" or "play" or "stop" || EventSemantics.IsPlaybackEnd(e),
        "控制" => e.kind is "aisac" or "selector" or "category",
        "回调" => e.kind is "beat" or "sequence" or "block",
        "诊断" => e.kind is "log" or "error" or "gap" or "state",
        _ => e.kind is "request" or "play" or "stop" or "aisac" or "selector" or "category" or "beat" or "sequence" or "block" or "error" or "gap" or "state" || EventSemantics.IsPlaybackEnd(e)
    };
    public static string Detail(WireEvent e) => ControlPresentation.Kinds.Contains(e.kind) ? ControlPresentation.Value(e) :
        e.kind=="category" ? e.name : e.kind is "request" or "play" ? "" : EventSemantics.IsPlaybackEnd(e) ? "播放实例已结束" : e.detail;
    public static bool Matches(WireEvent e, string query, string cue = "") => query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .All(word => $"{Action(e)} {(e.kind=="stop"||EventSemantics.IsPlaybackEnd(e)?"停止 结束":"")} {e.kind} {e.name} {cue} {e.objectId} {e.parentId} {Detail(e)}".Contains(word, StringComparison.OrdinalIgnoreCase));
}
