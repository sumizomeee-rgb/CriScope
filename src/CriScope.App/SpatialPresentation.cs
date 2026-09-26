using CriScope.Core;
using Avalonia;
namespace CriScope.App;

public sealed record SpatialCluster(string Key, string Entity, WireEvent[] Items);
public static class SpatialPresentation
{
    // Labels may move; measured anchors never do. No room means marker-only, never an overlapping card.
    public static Rect? PlaceLabel(Point anchor, double width, Rect area, IReadOnlyList<Rect> occupied)
    {
        double left=Math.Clamp(anchor.X+22,area.Left,Math.Max(area.Left,area.Right-width));
        double top=Math.Clamp(anchor.Y-23,area.Top,Math.Max(area.Top,area.Bottom-46));
        var xs=new[]{left,Math.Clamp(anchor.X-width-22,area.Left,Math.Max(area.Left,area.Right-width)),area.Left};
        for(int step=0;step<=Math.Ceiling(area.Height/50);step++)
        foreach(var direction in step==0?new[]{1}:new[]{1,-1})
        foreach(var x in xs)
        {
            var y=top+step*50*direction;
            if(y<area.Top||y+46>area.Bottom)continue;
            var candidate=new Rect(x,y,width,46);
            if(!occupied.Any(rect=>rect.Inflate(2).Intersects(candidate)))return candidate;
        }
        return null;
    }

    public static SpatialCluster[] Group(IEnumerable<WireEvent> events, double end, bool showBaseListeners = false, bool showSources = true, bool showDistanceListeners = true)
    {
        var available = events.Where(e => e.time <= end).ToArray();
        var boundary = available.Where(e => e.kind == "gap" || e.entity == "capture-segment")
            .OrderBy(e => e.time).ThenBy(e => e.seq).LastOrDefault();
        if (boundary != null) available = available.Where(e => e.time > boundary.time || e.time == boundary.time && e.seq > boundary.seq).ToArray();
        var removed = available.Where(e => e.kind is "spatial-remove" or "remove")
            .GroupBy(e => (e.entity,e.objectId)).ToDictionary(g => g.Key, g => g.Max(e => e.time));
        return available
            .Where(e => e.kind == "position" && (showDistanceListeners && e.entity == "distance-listener" || showSources && e.entity == "source" || showBaseListeners && e.entity == "listener") && double.IsFinite(e.x) && double.IsFinite(e.z))
            .GroupBy(e => (e.entity,e.objectId)).Select(g => g.OrderBy(e => e.time).ThenBy(e => e.seq).Last())
            .Where(e => (!removed.TryGetValue((e.entity,e.objectId),out var time) || time < e.time)
                && (e.entity != "distance-listener" || !removed.TryGetValue(("listener",e.objectId),out var listenerTime) || listenerTime < e.time))
            .GroupBy(e => (e.entity,X:Math.Round(e.x,3),Z:Math.Round(e.z,3)))
            .Select(g => new SpatialCluster($"spatial:{g.Key.entity}:{g.Key.X:R}:{g.Key.Z:R}",g.Key.entity,g.ToArray()))
            .OrderBy(g => g.Entity == "source" ? 0 : g.Entity == "listener" ? 1 : 2).ToArray();
    }

}
