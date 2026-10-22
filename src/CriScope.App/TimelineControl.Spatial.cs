using Avalonia;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class TimelineControl
{
    public sealed record SpatialMarker(Rect Hit, string Entity, string Caption, bool Active, int Count, Rect? CaptionBounds);
    public List<SpatialMarker> SpatialMarkers { get; } = [];
    private readonly List<(Rect Rect, string Tip)> _spatialCueTips = [];
    private WireEvent[]? _spatialCueEvents;
    private double _spatialCueEnd = double.NaN;
    private SpatialCueIndex _spatialCues = new();
    public SpatialCueIndex SpatialCues()
    {
        if (!ReferenceEquals(_spatialCueEvents, AssociationEvents) || _spatialCueEnd != End)
        {
            _spatialCueEvents = AssociationEvents; _spatialCueEnd = End;
            _spatialCues = SpatialCuePresentation.Build(AssociationEvents, End);
        }
        return _spatialCues;
    }
    public string? SpatialTipAt(Point point) => _spatialCueTips.LastOrDefault(h => h.Rect.Contains(point)).Tip;
    private string SpatialCueTip(WireEvent point)
    {
        var coordinates = $"X {point.x:0.###} · Y {point.y:0.###} · Z {point.z:0.###}";
        if (point.entity != "source") return (point.entity == "distance-listener" ? "衰减监听点" : "基础监听器") +
            "\n" + coordinates + (SpatialHistory != null ? "\n历史位置" : "");
        if (SpatialHistory is { } history) return $"音源 · 历史位置\n{history.Name}\n{coordinates}\n{history.Moment.Phase} · {DisplayStamp(history.Moment.At)}";
        var state = SpatialCues().For(point);
        var status = !SourceConnected ? "已断开 · 最后状态" : state.Active ? "正在播放" : state.Current.Length > 0 ? "关联已记录，播放状态未确认" : "当前未观测到播放关联";
        var current = state.Current.Length > 0 ? "\n当前关联：" + string.Join(" · ", state.Current.Select(c => c.Name)) : "";
        var last = state.Last is { } cue ? $"\n上次播放：{cue.Name}\n{cue.TimeLabel} · {DisplayStamp(cue.Time)}" : "\n上次播放：未记录";
        return $"音源 · {status}\n{coordinates}{current}{last}";
    }
    public SpatialViewport SpatialView { get; set; } = new();
    public HistoricalSpatialScene? SpatialHistory { get; set; }
    private Point? _spatialPanStart;
    public void FitSpatial()
    {
        SpatialFocusId = ""; SpatialView.Fit(); InvalidateVisual();
    }
    public void FocusListeners()
    {
        var points = SpatialHistory?.Moment.Points ?? Events;
        var listeners = SpatialPresentation.Group(points, SpatialHistory?.Moment.At ?? End, true, false, true)
            .SelectMany(g => g.Items).ToArray();
        if (listeners.Length == 0) return;
        ShowBaseListeners = listeners.Any(e => e.entity == "listener");
        ShowDistanceListeners = listeners.Any(e => e.entity == "distance-listener");
        SpatialFocusId = ""; SpatialView.Focus(listeners); InvalidateVisual();
    }
    public void ZoomSpatial(Point anchor, double delta)
    {
        SpatialFocusId = ""; SpatialView.Zoom(anchor, delta); InvalidateVisual();
    }
    public void PanSpatial(Vector pixels)
    {
        SpatialFocusId = ""; SpatialView.Pan(pixels); InvalidateVisual();
    }
}
