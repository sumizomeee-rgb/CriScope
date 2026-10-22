using Avalonia;
using CriScope.Core;

namespace CriScope.App;

public sealed class SpatialViewport
{
    public bool Automatic { get; private set; } = true;
    public double CenterX { get; private set; }
    public double CenterZ { get; private set; }
    public double Extent { get; private set; } = 2.5;
    public double Scale { get; private set; } = 1;
    public Rect Plot { get; private set; }
    // Fit mode computes the camera once. Incoming playback updates never reframe it.
    private bool _fitPending = true;
    private SpatialArrangement _arrangement = new();
    public SpatialViewport Clone()
    {
        var copy = (SpatialViewport)MemberwiseClone();
        copy._arrangement = _arrangement.Clone();
        return copy;
    }
    public SpatialPlacement[] Arrange(SpatialCluster[] clusters) => _arrangement.Layout(clusters, this);
    public void Fit() { Automatic = true; _fitPending = true; _arrangement = new(); }
    public void Layout(IEnumerable<WireEvent> events, Rect plot, string focusId = "")
    {
        Plot = plot;
        var points = events.Where(e => double.IsFinite(e.x) && double.IsFinite(e.z)).ToArray();
        if (_fitPending && points.Length > 0)
        {
            CenterX = points.Min(e => e.x) / 2 + points.Max(e => e.x) / 2;
            CenterZ = points.Min(e => e.z) / 2 + points.Max(e => e.z) / 2;
            if (points.FirstOrDefault(e => e.objectId == focusId) is { } focus) { CenterX = focus.x; CenterZ = focus.z; }
            // Recompute the radius around the chosen center, so focusing cannot lose distant listeners.
            Extent = Math.Max(2, points.Max(e => Math.Max(Math.Abs(e.x - CenterX), Math.Abs(e.z - CenterZ)))) * 1.25;
            _fitPending = false;
        }
        Scale = Math.Max(1, Math.Min(plot.Width, plot.Height)) / (2 * Extent);
    }
    public Point Project(WireEvent e) => new(Plot.Center.X + (e.x - CenterX) * Scale, Plot.Center.Y - (e.z - CenterZ) * Scale);
    public Point WorldAt(Point point) => new(CenterX + (point.X - Plot.Center.X) / Scale, CenterZ - (point.Y - Plot.Center.Y) / Scale);
    public void Zoom(Point anchor, double wheel)
    {
        if (!Plot.Contains(anchor) || !double.IsFinite(wheel) || wheel == 0) return;
        var world = WorldAt(anchor);
        Extent = Math.Clamp(Extent * Math.Pow(1.2, -Math.Clamp(wheel, -20, 20)), .001, 1e12);
        Scale = Math.Max(1, Math.Min(Plot.Width, Plot.Height)) / (2 * Extent);
        CenterX = world.X - (anchor.X - Plot.Center.X) / Scale;
        CenterZ = world.Y + (anchor.Y - Plot.Center.Y) / Scale;
        Automatic = false; _fitPending = false;
    }
    public void Pan(Vector pixels)
    {
        CenterX -= pixels.X / Scale; CenterZ += pixels.Y / Scale; Automatic = false; _fitPending = false;
    }
    public void Focus(IEnumerable<WireEvent> listeners)
    {
        var points = listeners.ToArray();
        if (points.Length == 0) return;
        CenterX = points.Min(e => e.x) / 2 + points.Max(e => e.x) / 2;
        CenterZ = points.Min(e => e.z) / 2 + points.Max(e => e.z) / 2;
        Extent = Math.Max(2, points.Max(e => Math.Max(Math.Abs(e.x - CenterX), Math.Abs(e.z - CenterZ)))) * 1.25;
        Automatic = false; _fitPending = false;
        Scale = Math.Max(1, Math.Min(Plot.Width, Plot.Height)) / (2 * Extent);
    }
}
