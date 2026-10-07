using CriScope.Core;

namespace CriScope.App;

public sealed record PlaybackActivityRange(double Start, double End, WireEvent Begin,
    WireEvent? EndEvent, WireEvent? Gap, bool UnknownStart)
{
    public bool Continues => EndEvent == null && Gap == null;
    public bool HasObservedEnd => EndEvent?.entity == "voice" && EndEvent.kind == "stop";
}

/// <summary>Observed Voice activity only; requests and missing evidence never fill a silent interval.</summary>
public static class PlaybackActivityPresentation
{
    public static PlaybackActivityRange Voice(WireEvent begin, WireEvent? voiceEnd,
        WireEvent? instanceEnd, double through, IEnumerable<WireEvent> discontinuities)
    {
        var ending = voiceEnd;
        if (instanceEnd != null && (ending == null || instanceEnd.time < ending.time)) ending = instanceEnd;
        if (ending?.time > through) ending = null;
        var end = Math.Min(ending?.time ?? through, through);
        var gap = discontinuities.Where(item =>
            (item.session.Length == 0 || begin.session.Length == 0 || item.session == begin.session) &&
            (item.channel.Length == 0 || begin.channel.Length == 0 || item.channel == begin.channel) &&
            (item.time > begin.time || item.time == begin.time && item.seq > begin.seq) &&
            (item.time < end || item.time == end && (ending == null || item.seq < ending.seq)))
            .MinBy(item => (item.time, item.seq));
        if (gap != null) { end = gap.time; ending = null; }
        return new(begin.time, end, begin, ending, gap,
            begin.kind != "play" || PlaybackPresentation.IsUnknownStart(begin));
    }

    public static PlaybackActivityRange[] Union(IEnumerable<PlaybackActivityRange> ranges)
    {
        var result = new List<PlaybackActivityRange>();
        foreach (var range in ranges.Where(item => item.End >= item.Start)
            .OrderBy(item => item.Start).ThenBy(item => item.Begin.seq))
        {
            if (result.Count == 0 || range.Start > result[^1].End ||
                range.Start == result[^1].End && result[^1].Gap != null)
            { result.Add(range); continue; }
            var prior = result[^1];
            var edge = range.End > prior.End || range.End == prior.End && BoundaryRank(range) > BoundaryRank(prior)
                ? range : prior;
            result[^1] = prior with {
                End = edge.End, EndEvent = edge.EndEvent, Gap = edge.Gap,
                UnknownStart = prior.UnknownStart || range.Start == prior.Start && range.UnknownStart
            };
        }
        return result.ToArray();
    }

    private static int BoundaryRank(PlaybackActivityRange range) => range.Gap != null ? 3 :
        range.EndEvent != null && !range.HasObservedEnd ? 2 : range.Continues ? 1 : 0;
}
