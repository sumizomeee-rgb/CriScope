using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CriScope.Core;

namespace CriScope.App;

public sealed class TimelineControl : Control
{
    internal Palette Palette { get; set; } = new(false);
    public WireEvent[] Events { get; set; } = [];
    // Kept separate from the search-filtered events so filtering cannot hide capture breaks.
    public WireEvent[] Discontinuities { get; set; } = [];
    public WireEvent? Selected { get; set; }
    public double End { get; set; } = 30;
    public double Span { get; set; } = 30;
    public bool Live { get; set; } = true;
    public string Mode { get; set; } = "Timeline";
    public event Action<WireEvent>? EventSelected;
    public event Action? ViewChanged;
    public double? SelectionStart { get; private set; }
    public double? SelectionEnd { get; private set; }
    public void SetSelection(double? start, double? end) { SelectionStart = start; SelectionEnd = end; InvalidateVisual(); }
    private Point? _drag;
    private double _dragEnd;
    private bool _panning;
    private bool _space;
    private double _vertical;
    private double _contentHeight = 450;
    private readonly List<(Rect rect, WireEvent item)> _hits = [];
    private const double LabelWidth = 144;
    private double PlotWidth => Math.Max(40, Bounds.Width - LabelWidth - 22);
    public double Start => End - Span;
    private double X(double time) => LabelWidth + (time - Start) / Span * PlotWidth;
    private static readonly Typeface Font = new("Cascadia Mono, Consolas");

    public TimelineControl()
    {
        ClipToBounds = true;
        Focusable = true;
        MinHeight = 220;
        PointerWheelChanged += (_, e) =>
        {
            if (!e.KeyModifiers.HasFlag(KeyModifiers.Control))
            {
                if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) { End -= e.Delta.Y * Span * .1; Live = false; }
                else _vertical = Math.Clamp(_vertical - e.Delta.Y * 36, 0, Math.Max(0, _contentHeight + 106 - Bounds.Height));
                ViewChanged?.Invoke(); InvalidateVisual(); e.Handled = true; return;
            }
            var fraction = Math.Clamp((e.GetPosition(this).X - LabelWidth) / PlotWidth, 0, 1);
            var anchor = Start + fraction * Span;
            Span = Math.Clamp(Span * (e.Delta.Y > 0 ? .8 : 1.25), .25, 86400);
            End = anchor + Span * (1 - fraction);
            Live = false;
            ViewChanged?.Invoke();
            InvalidateVisual();
            e.Handled = true;
        };
        PointerPressed += (_, e) =>
        {
            Focus();
            var p = e.GetPosition(this);
            var hit = _hits.LastOrDefault(h => p.Y >= 68 && p.Y < Bounds.Height - 30 && h.rect.Contains(p));
            if (hit.item != null) { Selected = hit.item; EventSelected?.Invoke(hit.item); InvalidateVisual(); }
            _panning = _space || e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;
            if (!_panning && hit.item != null) return;
            _drag = p; _dragEnd = End; e.Pointer.Capture(this);
            if (!_panning) { SelectionStart = Start + Math.Clamp((p.X - LabelWidth) / PlotWidth, 0, 1) * Span; SelectionEnd = SelectionStart; }
        };
        PointerMoved += (_, e) =>
        {
            if (_drag is not { } start) return;
            var delta = e.GetPosition(this).X - start.X;
            if (Math.Abs(delta) < 3) return;
            if (_panning) End = _dragEnd - delta / PlotWidth * Span;
            else SelectionEnd = Start + Math.Clamp((e.GetPosition(this).X - LabelWidth) / PlotWidth, 0, 1) * Span;
            Live = false;
            ViewChanged?.Invoke(); InvalidateVisual();
        };
        PointerReleased += (_, e) => { _drag = null; e.Pointer.Capture(null); };
        KeyDown += (_, e) => { if (e.Key == Key.Space) { _space = true; e.Handled = true; } };
        KeyUp += (_, e) => { if (e.Key == Key.Space) { _space = false; e.Handled = true; } };
        LostFocus += (_, _) => _space = false;
    }

    private void Text(DrawingContext c, string text, double x, double y, IBrush? brush = null, double size = 11)
    {
        c.DrawText(new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            Font, size, brush ?? Palette.Muted), new Point(x, y));
    }

    public override void Render(DrawingContext c)
    {
        base.Render(c);
        c.FillRectangle(Palette.Canvas, Bounds.WithX(0).WithY(0));
        _hits.Clear();
        if (Events.Length == 0)
        {
            Text(c, "等待真实音频数据", 28, 58, Palette.Text, 20);
            Text(c, "在游戏 GM 中打开 CriScope 采集，事件将在这里出现。", 28, 100, size: 12);
            Text(c, "采集由游戏控制  /  录制由当前会话控制  /  冻结只影响视图", 28, 132, size: 11);
            return;
        }
        var step = Math.Pow(10, Math.Floor(Math.Log10(Span / 8)));
        if (Span / step > 16) step *= 5;
        else if (Span / step > 10) step *= 2;
        for (var t = Math.Ceiling(Start / step) * step; t <= End; t += step)
        {
            var x = X(t);
            c.DrawLine(new Pen(Palette.Border, .6), new Point(x, 31), new Point(x, Bounds.Height));
            Text(c, t.ToString("0.00", CultureInfo.InvariantCulture) + "s", x + 4, 10);
        }
        c.DrawLine(new Pen(Palette.Border), new Point(0, 34), new Point(Bounds.Width, 34));
        Text(c, "MONOTONIC / s", 12, 10, size: 10);
        var visible = Events.Where(e => e.time >= Start && e.time <= End).ToArray();
        var bins = new int[Math.Max(1, (int)(PlotWidth / 4))];
        foreach (var e in visible) bins[Math.Clamp((int)((e.time - Start) / Span * bins.Length), 0, bins.Length - 1)]++;
        var max = Math.Max(1, bins.Max());
        for (var i = 0; i < bins.Length; i++)
            if (bins[i] > 0) c.FillRectangle(Palette.Signal, new Rect(LabelWidth + i * 4, 60 - 20.0 * bins[i] / max, 2, 20.0 * bins[i] / max));
        Text(c, "事件密度", 12, 43);
        if (Mode == "AISAC" || Mode == "Performance") DrawCurves(c, visible);
        else if (Mode == "Location") DrawLocations(c, visible);
        else DrawTracks(c, visible);
        if (Mode is "Timeline" or "Logs") Text(c, "Ctrl+滚轮缩放 · 中键平移 · 框选 · 端点=停止/最后观测，非 Voice", 12, Bounds.Height - 22, size: 10);
        if (Mode is "AISAC" or "Performance") Text(c, "滚轮浏览全部曲线 · 左侧为最后采样值与范围 · 点击采样点检查来源", 12, Bounds.Height - 22, size: 10);
        if (SelectionStart is { } a && SelectionEnd is { } b && Math.Abs(a - b) > .000001)
        {
            var left = Math.Clamp(X(Math.Min(a,b)), LabelWidth, LabelWidth + PlotWidth);
            var right = Math.Clamp(X(Math.Max(a,b)), LabelWidth, LabelWidth + PlotWidth);
            c.DrawRectangle(null, new Pen(Palette.Selection, 1.5), new Rect(left, 34, Math.Max(0, right-left), Math.Max(0, Bounds.Height-35)));
        }
        if (Selected is { } selected && selected.time >= Start && selected.time <= End)
            c.DrawLine(new Pen(Palette.Selection, 1.5), new Point(X(selected.time), 34), new Point(X(selected.time), Bounds.Height));
        if (Live)
        {
            c.DrawLine(new Pen(Palette.Signal, 1), new Point(X(End), 34), new Point(X(End), Bounds.Height));
            Text(c, "LIVE", Math.Max(LabelWidth, Bounds.Width - 58), Bounds.Height - 22, Palette.Signal);
        }
    }

    private void DrawTracks(DrawingContext c, WireEvent[] visible)
    {
        var groups = new[] { ("aisac", "AISAC WRITE"), ("pause", "PAUSE / RESUME"), ("snapshot", "STATE SAMPLES"), ("metric", "METRIC SAMPLES"), ("position", "3D SAMPLES"), ("log", "LOG / STATUS") };
        var lives = Events.Where(e => e.kind is "play" or "stop" or "snapshot" && !string.IsNullOrEmpty(e.objectId))
            .GroupBy(e => e.objectId).Where(g => g.Any(e => e.kind is "play" or "snapshot"))
            .Select(g => g.OrderBy(e => e.time).ToArray()).Where(g => g[0].time <= End && g[^1].time >= Start).Take(100).ToArray();
        var row = 34.0;
        _contentHeight = lives.Length * row + groups.Length * row;
        using var clip = c.PushClip(new Rect(0, 68, Bounds.Width, Math.Max(0, Bounds.Height-98)));
        for (var i = 0; i < lives.Length; i++)
        {
            var life = lives[i];
            var first = life.FirstOrDefault(e => e.kind == "play") ?? life[0];
            var last = life.Last();
            var y = 76 + i * row - _vertical;
            if (i % 2 == 0) c.FillRectangle(Palette.Alternate, new Rect(0, y, Bounds.Width, row));
            var title = $"{first.objectId} / {first.name}";
            Text(c, title.Length > 21 ? title[..20] + "…" : title, 10, y + 3, Palette.Text, 10);
            Text(c, $"Cue {first.cue} · {(first.kind == "play" ? "REQUEST" : "OBSERVED")}", 10, y + 18, size: 9);
            var x = Math.Max(LabelWidth, X(first.time)); var right = Math.Min(LabelWidth + PlotWidth, X(last.time));
            if (right >= x)
            {
                c.FillRectangle(first.kind == "play" ? Palette.Signal : Palette.Muted, new Rect(x, y + 17, Math.Max(2,right-x), 5));
                _hits.Add((new Rect(x, y, Math.Max(12,right-x), row),first));
            }
            if (first.time >= Start) c.FillRectangle(Palette.Signal,new Rect(x,y+7,2,23));
            if (last.kind == "stop") c.FillRectangle(Palette.Signal,new Rect(right,y+10,2,16));
            else c.DrawEllipse(Palette.Muted,null,new Point(right,y+19),3,3);
        }
        for (var i = 0; i < groups.Length; i++)
        {
            var y = 76 + (lives.Length + i) * row - _vertical;
            if (i % 2 == 0) c.FillRectangle(Palette.Alternate, new Rect(0, y, Bounds.Width, row));
            Text(c, groups[i].Item2, 12, y + 10, size: 10);
            var matches = visible.Where(e => groups[i].Item1 switch {
                "pause" => e.kind is "pause" or "resume", "log" => e.kind is "log" or "state" or "hello" or "gap", _ => e.kind == groups[i].Item1 });
            var drawnPixels = new HashSet<int>();
            foreach (var e in matches)
            {
                var x = X(e.time);
                if (!drawnPixels.Add((int)(x / 3))) continue;
                var sample = e.kind is "snapshot" or "metric" or "position";
                var brush = e.kind == "gap" ? Palette.Error : e.kind == "aisac" ? Palette.Selection : sample ? Palette.Muted : Palette.Signal;
                c.FillRectangle(brush, new Rect(x, sample ? y+row/2 : y + 7, sample ? 1 : 2, sample ? 4 : row - 14));
                if (!sample) c.FillRectangle(brush, new Rect(x, y + row / 2 - 2, 9, 4));
                _hits.Add((new Rect(x - 3, y, 13, row), e));
            }
        }
    }

    private void DrawCurves(DrawingContext c, WireEvent[] visible)
    {
        var kind = Mode == "AISAC" ? "aisac" : "metric";
        var series = visible.Where(e => e.kind == kind && double.IsFinite(e.value))
            .GroupBy(e => (e.name, e.objectId)).ToArray();
        if (series.Length == 0) { Text(c, "当前时间窗未收到" + (kind == "aisac" ? " AISAC 写值" : "性能指标"), 24, 108, Palette.Text, 15); return; }
        var height = Math.Max(80, (Bounds.Height - 105) / series.Length);
        _contentHeight = height * series.Length;
        using var clip = c.PushClip(new Rect(0, 68, Bounds.Width, Math.Max(0, Bounds.Height-94)));
        for (var i = 0; i < series.Length; i++)
        {
            var samples = series[i].OrderBy(e => e.time).ToArray();
            var min = samples.Min(e => e.value); var max = samples.Max(e => e.value);
            var y = 78 + height * i - _vertical;
            var name = series[i].Key.name;
            Text(c, name.Length>20?name[..19]+"…":name, 10, y, Palette.Text, 10);
            Text(c, series[i].Key.objectId, 10, y + 17, size: 9);
            Text(c, samples[^1].value.ToString("0.###"), 10, y + 33, Palette.Signal, 14);
            Text(c, $"{min:0.##} … {max:0.##}", 10, y + 53, size: 9);
            Point? previous = null;
            double previousTime = 0;
            foreach (var e in samples)
            {
                var p = new Point(X(e.time), y + height - 12 - (e.value - min) / Math.Max(.000001, max - min) * (height - 22));
                var interrupted = Discontinuities.Any(b => b.time >= previousTime && b.time <= e.time)
                    || kind == "metric" && e.time - previousTime > 2;
                if (previous is { } last && !interrupted)
                {
                    if (kind == "aisac")
                    {
                        c.DrawLine(new Pen(Palette.Selection, 1.3), last, new Point(p.X, last.Y));
                        c.DrawLine(new Pen(Palette.Selection, 1.3), new Point(p.X, last.Y), p);
                    }
                    else c.DrawLine(new Pen(Palette.Selection, 1.3), last, p);
                }
                c.FillRectangle(Palette.Signal, new Rect(p.X - 2, p.Y - 2, 4, 4));
                _hits.Add((new Rect(p.X - 5, p.Y - 7, 10, 14), e)); previous = p; previousTime = e.time;
            }
        }
    }

    private void DrawLocations(DrawingContext c, WireEvent[] visible)
    {
        var positions = visible.Where(e => e.kind == "position" && double.IsFinite(e.x) && double.IsFinite(e.z)).GroupBy(e => e.objectId).Select(g => g.Last()).ToArray();
        Text(c, "世界坐标 X / Z 俯视投影 · 当前时间窗最后一个位置", 20, 83, Palette.Text, 12);
        if (positions.Length == 0) { Text(c, "此时间窗尚无来源位置数据", 20, 118); return; }
        var extent = Math.Max(1, positions.Max(e => Math.Max(Math.Abs(e.x), Math.Abs(e.z)))) * 1.2;
        var center = new Point(Bounds.Width / 2, (Bounds.Height + 110) / 2);
        var scale = Math.Min(Bounds.Width - 100, Bounds.Height - 140) / (2 * extent);
        c.DrawLine(new Pen(Palette.Border), new Point(20, center.Y), new Point(Bounds.Width - 20, center.Y));
        c.DrawLine(new Pen(Palette.Border), new Point(center.X, 110), new Point(center.X, Bounds.Height - 20));
        foreach (var cluster in positions.GroupBy(e => (Math.Round(e.x,3), Math.Round(e.z,3))))
        {
            var e = cluster.First();
            var p = new Point(center.X + e.x * scale, center.Y - e.z * scale);
            c.DrawEllipse(Palette.Signal, null, p, 4, 4);
            Text(c, cluster.Count()>1 ? $"{cluster.Count()} 个对象重合 · X {e.x:0.##} / Z {e.z:0.##}" : string.IsNullOrEmpty(e.name) ? e.objectId : e.name, p.X + 8, p.Y - 8);
            _hits.Add((new Rect(p.X - 8, p.Y - 8, 16, 16), e));
        }
    }
}
