using CriScope.Core;
namespace CriScope.App;

public sealed record SpatialCluster(string Key, string Entity, WireEvent[] Items);
public static class SpatialPresentation
{
    public static SpatialCluster[] Group(IEnumerable<WireEvent> events, double end, bool showBaseListeners = false)
    {
        var available = events.Where(e => e.time <= end).ToArray();
        var boundary = available.Where(e => e.kind == "gap" || e.entity == "capture-segment")
            .OrderBy(e => e.time).ThenBy(e => e.seq).LastOrDefault();
        if (boundary != null) available = available.Where(e => e.time > boundary.time || e.time == boundary.time && e.seq > boundary.seq).ToArray();
        var removed = available.Where(e => e.kind is "spatial-remove" or "remove").ToArray();
        return available
        .Where(e => e.kind == "position" && (e.entity is "distance-listener" or "source" || showBaseListeners && e.entity == "listener") && double.IsFinite(e.x) && double.IsFinite(e.z))
        .GroupBy(e => (e.entity, e.objectId)).Select(g => g.OrderBy(e => e.time).Last())
        .Where(e => !removed.Any(r => r.time >= e.time && r.objectId == e.objectId && (r.entity == e.entity || e.entity == "distance-listener" && r.entity == "listener")))
        .GroupBy(e => (e.entity, X: Math.Round(e.x, 3), Z: Math.Round(e.z, 3)))
        .Select(g => new SpatialCluster($"spatial:{g.Key.entity}:{g.Key.X:R}:{g.Key.Z:R}", g.Key.entity, g.ToArray())).ToArray();
    }
}
