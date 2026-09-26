using System.Globalization;
using CriScope.Core;

namespace CriScope.App;

public sealed record ControlRow(string Key, string Kind, string Name, string TargetLabel, WireEvent[] Records)
{
    public WireEvent Latest => Records[^1];
}

public static class ControlPresentation
{
    public static readonly string[] Kinds = ["aisac", "selector", "block", "beat", "sequence"];
    public static string KindLabel(string kind) => kind switch
    {
        "aisac" => "AISAC", "selector" => "Selector", "block" => "Block",
        "beat" => "Beat", "sequence" => "Sequence", _ => kind
    };

    public static ControlRow[] Group(IEnumerable<WireEvent> events, double end, ISet<string>? enabledKinds = null)
    {
        var source = events.Where(e => e.time <= end && Kinds.Contains(e.kind)).ToArray();
        var targets = source.Select(e => (e.session, e.objectId)).Distinct().OrderBy(k => k.session).ThenBy(k => k.objectId)
            .Select((k, i) => (k, label: "作用对象 " + (i + 1))).ToDictionary(x => x.k, x => x.label);
        return source.Where(e => enabledKinds == null || enabledKinds.Contains(e.kind))
            .GroupBy(e => (e.session, e.kind, e.objectId, e.name))
            .Select(g => new ControlRow("control:" + string.Join("|", g.Key.session, g.Key.kind, g.Key.objectId, g.Key.name),
                g.Key.kind, g.Key.name, string.IsNullOrEmpty(g.Key.objectId) ? "作用对象未提供" : targets[(g.Key.session, g.Key.objectId)],
                g.OrderBy(e => e.time).ThenBy(e => e.seq).ToArray()))
            .OrderBy(r => Array.IndexOf(Kinds, r.Kind)).ThenBy(r => r.TargetLabel).ThenBy(r => r.Name).ToArray();
    }

    public static string Value(WireEvent e) => e.kind switch
    {
        "aisac" => e.value.ToString("0.####", CultureInfo.InvariantCulture),
        "selector" => string.IsNullOrWhiteSpace(e.detail) ? "标签未提供" : e.detail,
        "block" => e.name + " · " + e.value.ToString("0.####", CultureInfo.InvariantCulture),
        "beat" or "sequence" => string.IsNullOrWhiteSpace(e.detail) ? e.name : e.detail,
        _ => e.name
    };
}
