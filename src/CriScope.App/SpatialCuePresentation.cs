using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

public sealed record SpatialCue(string PlaybackId, string Name, double Time, string TimeLabel);
public sealed record SpatialSourceCues(SpatialCue[] Current, SpatialCue? Last, bool Active)
{
    public string Caption => Current.Length > 0 ? Current[0].Name + (Current.Length > 1 ? $" +{Current.Length - 1}" : "")
        : Last != null ? "上次 · " + Last.Name : "";
}
public sealed class SpatialCueIndex
{
    internal readonly Dictionary<(string Session, string Source), SpatialSourceCues> Sources = [];
    internal readonly Dictionary<(string Session, string Playback), PlaybackGroup> Playbacks = [];
    public SpatialSourceCues For(WireEvent source) => Sources.GetValueOrDefault((source.session, source.objectId), new([], null, false));
    public PlaybackGroup? Playback(string session, string id) => Playbacks.GetValueOrDefault((session, id));
}

public static class SpatialCuePresentation
{
    public static SpatialCueIndex Build(WireEvent[] events, double end)
    {
        var result = new SpatialCueIndex();
        foreach (var playback in PlaybackPresentation.Group(events, end))
            if (AssociationPresentation.Anchor(playback) is { } anchor)
                result.Playbacks[(anchor.session, playback.Id)] = playback;
        var sources = SpatialPresentation.Group(events, end, false, true, false).SelectMany(g => g.Items).ToArray();
        var nativeLast = new HashSet<(string Session, string Source)>();
        foreach (var source in sources)
        {
            var (current, last, hasLast) = Read(source, result);
            var active = current.Any(c => result.Playback(source.session, c.PlaybackId) is { ActiveVoiceCount: > 0, HasEvidenceGap: false });
            result.Sources[(source.session, source.objectId)] = new(current, hasLast ? last : current.MaxBy(c => c.Time), active);
            if (hasLast) nativeLast.Add((source.session, source.objectId));
        }
        // Older recordings lack lastPlayback. Recover names only from explicit earlier source links,
        // within this object's current lifecycle and capture segment, never from a matching Cue name.
        var available = events.Where(e => e.time <= end).ToArray();
        var boundary = available.Where(e => e.kind == "gap" || e.entity == "capture-segment").MaxBy(e => (e.time, e.seq));
        var resets = available.Where(e => e.entity == "source" && e.kind is "remove" or "spatial-remove")
            .GroupBy(e => (e.session, e.objectId)).ToDictionary(g => g.Key, g => g.MaxBy(e => (e.time, e.seq))!);
        foreach (var e in available)
        {
            var key = (e.session, e.objectId);
            if (e.kind != "position" || e.entity != "source" || !result.Sources.ContainsKey(key) || nativeLast.Contains(key) ||
                e.raw.Length == 0 || e.raw.Contains("\"links\":[]", StringComparison.Ordinal)) continue;
            if (boundary != null && Before(e, boundary) || resets.TryGetValue(key, out var reset) && Before(e, reset)) continue;
            foreach (var cue in Read(e, result).Current)
            {
                var state = result.Sources[key];
                if (state.Last == null || cue.Time >= state.Last.Time) result.Sources[key] = state with { Last = cue };
            }
        }
        return result;
    }
    private static bool Before(WireEvent e, WireEvent boundary) => e.time < boundary.time || e.time == boundary.time && e.seq <= boundary.seq;
    private static (SpatialCue[] Current, SpatialCue? Last, bool HasLast) Read(WireEvent source, SpatialCueIndex index)
    {
        if (string.IsNullOrWhiteSpace(source.raw)) return ([], null, false);
        try
        {
            using var doc = JsonDocument.Parse(source.raw);
            if (!doc.RootElement.TryGetProperty("derived", out var derived)) return ([], null, false);
            var current = new List<SpatialCue>();
            if (derived.TryGetProperty("links", out var links) && links.ValueKind == JsonValueKind.Array)
                foreach (var link in links.EnumerateArray())
                    if (Cue(link, source, index) is { } cue) current.Add(cue);
            var last = derived.TryGetProperty("lastPlayback", out var recorded) && recorded.ValueKind == JsonValueKind.Object
                ? Cue(recorded, source, index) : null;
            return (current.OrderByDescending(c => c.Time).DistinctBy(c => c.PlaybackId).ToArray(), last,
                recorded.ValueKind==JsonValueKind.Object);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException) { return ([], null, false); }
    }
    private static SpatialCue? Cue(JsonElement link, WireEvent source, SpatialCueIndex index)
    {
        if (!link.TryGetProperty("playback", out var p) || p.GetString() is not { Length: > 0 } id) return null;
        if (source.epoch > 0) id = source.epoch + ":" + id;
        var playback = index.Playback(source.session, id);
        var name = link.TryGetProperty("cue", out var n) ? n.GetString() ?? "" : "";
        if (name.Length == 0 || name == "Cue 名称未知") name = playback?.Request!=null ? playback.Name : "";
        if (name.Length == 0 || name == "Cue 名称未知") return null;
        if (playback?.StartedAt is { } start) return new(id, name, start, "开始播放");
        var time = link.TryGetProperty("observedAt", out var t) && t.TryGetDouble(out var observed) && double.IsFinite(observed)
            ? observed : source.time;
        return new(id, name, time, "关联观测");
    }
}
