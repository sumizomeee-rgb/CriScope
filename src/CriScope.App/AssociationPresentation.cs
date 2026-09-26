using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

/// <summary>Explicit identities only. Names are labels, never relationship keys.</summary>
public static class AssociationPresentation
{
    public static WireEvent? Anchor(PlaybackGroup p) => p.Request ?? p.Voices.SelectMany(v => v).FirstOrDefault() ?? p.End;
    public static bool ActiveAt(PlaybackGroup p, double time) => Anchor(p) is { } a && a.time <= time && (p.InstanceEndedAt == null || p.InstanceEndedAt >= time) && !p.HasEvidenceGap;
    public static PlaybackGroup[] PlayerPlaybacks(IEnumerable<WireEvent> events, string player, double time) => player.Length == 0 ? [] :
        PlaybackPresentation.Group(events, time).Where(p => p.PlayerId == player && ActiveAt(p, time)).ToArray();

    public static string[] SourcePlaybackIds(WireEvent source)
    {
        try
        {
            using var doc = JsonDocument.Parse(source.raw);
            if (!doc.RootElement.TryGetProperty("derived", out var derived) || !derived.TryGetProperty("links", out var links)) return [];
            return links.EnumerateArray().Select(l => l.GetProperty("playback").GetString() ?? "")
                .Where(s => s.Length > 0).Select(s => source.epoch > 0 ? source.epoch + ":" + s : s).Distinct().ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return []; }
    }
    public static WireEvent[] SourcesFor(IEnumerable<WireEvent> events, IEnumerable<string> playbackIds, double time)
    {
        var ids = playbackIds.ToHashSet(StringComparer.Ordinal);
        return SpatialPresentation.Group(events, time, false, true, false).SelectMany(c => c.Items)
            .Where(s => SourcePlaybackIds(s).Any(ids.Contains)).ToArray();
    }
    public static PlaybackGroup[] SourcePlaybacks(IEnumerable<WireEvent> events, WireEvent source, double time)
    {
        // Re-read the source at the chosen time; a stale selected marker must not keep an old link alive.
        var current = SpatialPresentation.Group(events, time, false, true, false).SelectMany(c => c.Items)
            .FirstOrDefault(e => e.objectId == source.objectId && e.session == source.session);
        var ids = current == null ? [] : SourcePlaybackIds(current);
        return PlaybackPresentation.Group(events, time).Where(p => ids.Contains(p.Id)).ToArray();
    }
    public static WireEvent[] Categories(IEnumerable<WireEvent> events, string playback, double time) => events
        .Where(e => e.time <= time && e.kind == "category" && e.parentId == playback)
        .GroupBy(e => e.objectId).Select(g => g.OrderBy(e => e.time).ThenBy(e => e.seq).Last()).ToArray();
}
