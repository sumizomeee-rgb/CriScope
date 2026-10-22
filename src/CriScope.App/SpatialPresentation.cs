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

    public static Point PlaceMarker(Point anchor, Rect area, IReadOnlyList<Rect> occupied, Vector? preferred = null)
    {
        var offsets=new List<Vector>();
        if(preferred is {} prior)offsets.Add(prior);
        offsets.Add(new());
        foreach(var radius in new[]{28d,56d,84d})
            foreach(var (x,y) in new[]{(1,0),(-1,0),(0,-1),(0,1),(1,-1),(-1,1),(1,1),(-1,-1)})
                offsets.Add(new Vector(x*radius,y*radius));
        foreach(var offset in offsets)
        {
            var point=anchor+offset;
            var box=new Rect(point.X-11,point.Y-11,22,22);
            if(area.Contains(box.TopLeft)&&area.Contains(box.BottomRight)&&!occupied.Any(r=>r.Inflate(2).Intersects(box)))return point;
        }
        return anchor;
    }

    public static Rect? PlaceCompactLabel(Point anchor, double width, Rect area, IReadOnlyList<Rect> occupied, Vector? preferred = null)
    {
        const double height=24;
        bool Available(Rect rect)=>area.Contains(rect.TopLeft)&&area.Contains(rect.BottomRight)&&!occupied.Any(r=>r.Inflate(2).Intersects(rect));
        if(preferred is {} offset)
        {
            var prior=new Rect(anchor+offset,new Size(width,height));
            if(Available(prior))return prior;
        }
        foreach(var dy in new[]{-12d,12d,-36d,36d,-60d,60d})
        foreach(var gap in new[]{16d,44d,72d,100d})
        foreach(var right in new[]{true,false})
        {
            var left=right?anchor.X+gap:anchor.X-width-gap;
            var candidate=new Rect(left,anchor.Y+dy,width,height);
            if(candidate.Left<area.Left||candidate.Top<area.Top||candidate.Right>area.Right||candidate.Bottom>area.Bottom)continue;
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
