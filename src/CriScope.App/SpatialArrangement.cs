using Avalonia;

namespace CriScope.App;

public sealed record SpatialPlacement(SpatialCluster Cluster, Point Anchor, Point Icon, Rect Hit, Rect? Label);

// Reserve one stable slot per object, independent of Cue text and playback state.
internal sealed class SpatialArrangement
{
    private sealed class Slot(long order)
    {
        public long Order = order;
        public Vector? IconOffset;
        public Vector? LabelOffset;
        public Slot Clone() => (Slot)MemberwiseClone();
    }
    private readonly Dictionary<(string Session, string Entity, string Id), Slot> _slots = [];
    private long _next;
    public SpatialArrangement Clone()
    {
        var copy = new SpatialArrangement { _next = _next };
        foreach (var pair in _slots) copy._slots.Add(pair.Key, pair.Value.Clone());
        return copy;
    }
    public SpatialPlacement[] Layout(SpatialCluster[] clusters, SpatialViewport view)
    {
        var objects = clusters.SelectMany(c => c.Items).Select(e => (e.session, e.entity, e.objectId)).ToHashSet();
        // Bound retired slots while retaining all objects in the current scene.
        if (_slots.Count > 4096)
            foreach (var key in _slots.Where(p => !objects.Contains(p.Key)).OrderBy(p => p.Value.Order)
                .Take(_slots.Count - 4096).Select(p => p.Key).ToArray()) _slots.Remove(key);
        foreach (var point in objects.OrderBy(p => p.entity == "source" ? 0 : 1)
            .ThenBy(p => p.entity, StringComparer.Ordinal).ThenBy(p => p.objectId, StringComparer.Ordinal))
            if (!_slots.ContainsKey(point)) _slots.Add(point, new Slot(_next++));
        var items = clusters.Select(c => (Cluster: c, Slot: c.Items
            .Select(e => _slots[(e.session, e.entity, e.objectId)]).MinBy(s => s.Order)!)).OrderBy(p => p.Slot.Order).ToArray();
        var occupied = new List<Rect>();
        var placed = new Dictionary<Slot, SpatialPlacement>();
        // Existing icons retain priority. On first entry, place ALL icons before reserving any labels.
        foreach (var item in items.Where(p => p.Slot.IconOffset != null)) PlaceIcon(item.Cluster, item.Slot);
        var labelWidth = Math.Min(230, Math.Max(28, view.Plot.Width - 32));
        foreach (var item in items.Where(p => p.Slot.LabelOffset != null))
        {
            if (!placed.TryGetValue(item.Slot, out var marker)) continue;
            var label = new Rect(marker.Icon + item.Slot.LabelOffset!.Value, new Size(labelWidth, 24));
            if (view.Plot.Contains(label.TopLeft) && view.Plot.Contains(label.BottomRight) &&
                !occupied.Any(r => r.Inflate(2).Intersects(label)))
            {
                placed[item.Slot] = marker with { Label = label }; occupied.Add(label);
            }
        }
        // New objects fit around retained icons and labels, rather than displacing their neighbours.
        foreach (var item in items.Where(p => p.Slot.IconOffset == null)) PlaceIcon(item.Cluster, item.Slot);
        foreach (var item in items.Where(p => p.Cluster.Entity == "source"))
        {
            if (!placed.TryGetValue(item.Slot, out var marker) || marker.Label != null) continue;
            // Empty sources reserve the same label space as playing sources.
            var label = SpatialPresentation.PlaceCompactLabel(marker.Icon, labelWidth, view.Plot, occupied, item.Slot.LabelOffset);
            if (label is not { } rect) continue;
            item.Slot.LabelOffset = rect.TopLeft - marker.Icon;
            placed[item.Slot] = marker with { Label = rect }; occupied.Add(rect);
        }
        return items.Where(p => placed.ContainsKey(p.Slot)).Select(p => placed[p.Slot]).ToArray();

        void PlaceIcon(SpatialCluster cluster, Slot slot)
        {
            var anchor = view.Project(cluster.Items[0]);
            if (!view.Plot.Contains(anchor)) return;
            var icon = SpatialPresentation.PlaceMarker(anchor, view.Plot, occupied, slot.IconOffset);
            slot.IconOffset = icon - anchor;
            var hit = new Rect(icon.X - 12, icon.Y - 12, 24, 24); occupied.Add(hit);
            placed.Add(slot, new(cluster, anchor, icon, hit, null));
        }
    }
}
