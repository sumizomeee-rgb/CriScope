using System.Globalization;
using CriScope.Core;

namespace CriScope.App;

public sealed record ControlRow(string Key, string Kind, string Name, string TargetLabel, WireEvent[] Records)
{
    public WireEvent Latest => Records[^1];
}

public sealed record ControlGroup(string Key, string Name, string Summary, ControlRow[] Rows);

// Display aliases survive filtering and cache eviction within a capture.
public sealed class ControlIdentityLabels
{
    private readonly Dictionary<(string Type, string Id), string> labels = [];
    private readonly Dictionary<string, int> counters = [];
    public string Get(string type, string id)
    {
        var key = (type, id);
        if (!labels.TryGetValue(key, out var label))
        {
            int number = counters.GetValueOrDefault(type) + 1;
            counters[type] = number;
            labels[key] = label = type + " #" + number;
        }
        return label;
    }
}

public static class ControlPresentation
{
    public static readonly string[] Kinds = ["aisac", "selector", "block", "beat", "sequence"];
    public static string KindLabel(string kind) => kind switch
    {
        "aisac" => "AISAC", "selector" => "Selector", "block" => "Block",
        "beat" => "BeatSync", "sequence" => "Sequence", _ => kind
    };

    public static ControlGroup[] Group(IEnumerable<WireEvent> events, double end, ISet<string>? enabledKinds = null, ControlIdentityLabels? labels = null)
    {
        labels ??= new ControlIdentityLabels();
        var all = events.Where(e => e.time <= end).ToArray();
        var source = all.Where(e => Kinds.Contains(e.kind)).OrderBy(e => e.time).ThenBy(e => e.seq).ToArray();
        var targets = source.Where(e => e.kind is "aisac" or "selector" || e.kind == "block" && e.entity == "control")
            .Select(e => (e.session, e.objectId)).Distinct().ToDictionary(k => k, k => TargetLabel(k.session, k.objectId, labels));
        var playbacks = PlaybackPresentation.Group(all, end).ToDictionary(g => g.Id);
        var players = all.Where(e => e.kind is "aisac" or "selector").Select(e => (e.session, e.objectId))
            .Concat(all.Where(e => e.kind == "request" && e.entity == "cue").Select(e => (e.session, e.parentId))).ToHashSet();
        var result = new List<ControlGroup>();
        var selected = source.Where(e => enabledKinds == null || enabledKinds.Contains(e.kind)).ToArray();
        foreach (var parameter in selected.Where(e => e.kind is "aisac" or "selector").GroupBy(e => (e.kind, e.name)).OrderBy(g => Array.IndexOf(Kinds, g.Key.kind)).ThenBy(g => g.Key.name))
        {
            var rows = parameter.GroupBy(e => (e.session, e.objectId)).Select(g => new ControlRow(
                Key("setting", g.Key.session, g.Key.objectId, parameter.Key.kind, parameter.Key.name), parameter.Key.kind,
                targets[g.Key], "", g.ToArray())).ToArray();
            var counts = rows.GroupBy(r => r.Name.Split(" #")[0]).Select(g => g.Key == "对象未提供" ? g.Key : $"{g.Count()} 个 {g.Key}");
            result.Add(new ControlGroup(Key("parameter", parameter.Key.kind, parameter.Key.name), parameter.Key.name,
                KindLabel(parameter.Key.kind) + " · " + string.Join("、", counts) + $" · {parameter.Count()} 次设置", rows));
        }
        var callbacks = selected.Where(e => e.kind is "beat" or "sequence" or "block").Select(e =>
        {
            // Only explicit playback identity establishes ownership, never nearby time or a shared Cue name.
            var owner = e.parentId.Length > 0 ? playbacks.GetValueOrDefault(e.parentId) : playbacks.GetValueOrDefault(e.objectId);
            bool request = e.kind == "block" && e.entity == "control";
            bool playerRequest = owner == null && request
                && (players.Contains((e.session, e.objectId)) || e.objectId.Split(':').Contains("player"));
            var identity = owner != null ? "playback:" + owner.Id : playerRequest ? Key("player-block", e.session, e.objectId) : "unlinked";
            string title;
            if (owner != null)
            {
                var anchor = owner.Request ?? owner.Voices.SelectMany(v => v).FirstOrDefault() ?? owner.End;
                var alias = labels.Get("播放实例", Key(anchor?.session ?? "", owner.Id));
                title = owner.Name + " · " + alias;
            }
            else title = playerRequest ? targets[(e.session, e.objectId)] + " · Block 请求" : "未关联播放实例";
            return (Event: e, Identity: identity, Title: title, Request: request);
        });
        foreach (var playback in callbacks.GroupBy(x => x.Identity))
        {
            var rows = playback.GroupBy(x => (x.Event.kind, x.Request, Action: x.Event.kind == "block" ? x.Event.name : ""))
                .OrderBy(g => Array.IndexOf(Kinds, g.Key.kind)).Select(g => new ControlRow(
                    Key("callback", playback.Key, g.Key.kind, g.Key.Request.ToString(), g.Key.Action), g.Key.kind,
                    g.Key.kind == "block" ? g.Key.Request ? g.Key.Action : "Block 位置采样" : KindLabel(g.Key.kind), "", g.Select(x => x.Event).ToArray())).ToArray();
            result.Add(new ControlGroup(Key("events", playback.Key), playback.First().Title,
                string.Join(" · ", rows.Select(r => r.Name + " " + r.Records.Length)), rows));
        }
        return result.ToArray();
    }

    private static string Key(params string[] parts) => System.Text.Json.JsonSerializer.Serialize(parts);
    private static string TargetLabel(string session, string id, ControlIdentityLabels labels) => id.Length == 0 ? "对象未提供"
        : labels.Get(id.Split(':').Contains("category") ? "Category" : "Player", Key(session, id));

    public static string Value(WireEvent e) => e.kind switch
    {
        "aisac" => e.value.ToString("0.####", CultureInfo.InvariantCulture),
        "selector" => string.IsNullOrWhiteSpace(e.detail) ? "标签未提供" : e.detail,
        "block" => "Block " + e.value.ToString("0.####", CultureInfo.InvariantCulture),
        "beat" => $"{e.value:0.##} BPM" + Part(e, "bar", " · 小节 ") + Part(e, "beat", " · 拍 "),
        "sequence" => e.name + " · 事件 " + e.value.ToString("0.####", CultureInfo.InvariantCulture),
        _ => e.name
    };
    private static string Part(WireEvent e, string key, string label)
    {
        var part = e.detail.Split(';').Select(s => s.Trim()).FirstOrDefault(s => s.StartsWith(key + "=", StringComparison.Ordinal));
        return part == null ? "" : label + part[(key.Length + 1)..];
    }
}
