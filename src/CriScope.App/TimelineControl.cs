using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
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
    public WireEvent[] AssociationEvents { get; set; } = [];
    public Func<WireEvent, (PlaybackGroup[] Owners, string EmptyLabel)>? ResolveSettingEvidence { get; set; }
    public WireEvent[] Discontinuities { get; set; } = [];
    public WireEvent[] SupplementMetrics { get; set; } = [];
    public WireEvent? Selected { get; set; }
    public ControlIdentityLabels ControlLabels { get; set; } = new();
    public double End { get; set; } = 30;
    public double Span { get; set; } = 30;
    public bool Live { get; set; } = true;
    public bool SourceConnected { get; set; } = true;
    public bool BusHistoryIsSparse { get; set; }
    public double TimeOrigin { get; set; }
    public HashSet<string> ControlKinds { get; } = new(ControlPresentation.Kinds, StringComparer.Ordinal);
    public bool ShowSources { get; set; } = true;
    public bool ShowDistanceListeners { get; set; } = true;
    public bool ShowBaseListeners { get; set; }
    public Func<double,string>? WallStamp { get; set; }
    public Func<double,DateTimeOffset?>? ClockTimeAt { get; set; }
    public bool PreferWallTime { get; set; } = true;
    public bool ShowingWallTime => PreferWallTime && ClockTimeAt?.Invoke(End) is not null;
    public Rect TimeAxisToggleBounds => new(8,(UiMetrics.TimelineRulerHeight-UiMetrics.IconTarget)/2,UiMetrics.IconTarget,UiMetrics.IconTarget);
    public bool TimeAxisToggleHovered => _axisToggleHovered;
    public string DisplayStamp(double time) => ShowingWallTime && ClockTimeAt?.Invoke(time) is { } value
        ? value.ToLocalTime().ToString("MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture) : Stamp(time);
    private string ControlRowStamp(double time) => ShowingWallTime && ClockTimeAt?.Invoke(time) is { } value
        ? value.ToLocalTime().ToString("HH:mm:ss.fff",CultureInfo.InvariantCulture) : Stamp(time);
    private string ControlRowTipStamp(double time) => ShowingWallTime && ClockTimeAt?.Invoke(time) is { } value
        ? value.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture) : Stamp(time);
    public event Action<bool>? TimeAxisModeChanged;
    public string Stamp(double time) => TimeLabel(time-TimeOrigin);
    public void SetControlKind(string kind, bool enabled)
    { if(!ControlPresentation.Kinds.Contains(kind))throw new ArgumentException("未知控制类型",nameof(kind));if(enabled)ControlKinds.Add(kind);else ControlKinds.Remove(kind);_vertical=0;ViewChanged?.Invoke();InvalidateVisual(); }
    public void SetSpatialLayer(string layer, bool enabled)
    {
        switch(layer){case "source":ShowSources=enabled;break;case "distance-listener":ShowDistanceListeners=enabled;break;case "listener":ShowBaseListeners=enabled;break;default:throw new ArgumentException("未知空间图层",nameof(layer));}
        _vertical=0;ViewChanged?.Invoke();InvalidateVisual();
    }
    public void ShowVoiceDetails(string id){_expanded.Add("playback:"+id);InvalidateVisual();}
    public string Mode { get; set; } = "Timeline";
    public event Action<WireEvent>? EventSelected;
    public event Action<WireEvent>? PlaybackRequested;
    public event Action? SelectionCleared;
    public event Action? ViewChanged;
    public double? SelectionStart { get; private set; }
    public double? SelectionEnd { get; private set; }
    public void SetSelection(double? start, double? end) { SelectionStart=start; SelectionEnd=end; InvalidateVisual(); }
    private DateTime _flashUntil;
    public string SpatialFocusId { get; set; } = "";
    private readonly List<(Rect Rect, WireEvent Event)> _links = [];
    private static readonly Cursor LinkCursor = new(StandardCursorType.Hand);
    private Rect? _hoverLinkRect;
    public double VerticalOffset { get => _vertical; set => _vertical = Math.Max(0, value); }
    public string[] ExpandedKeys { get => _expanded.ToArray(); set { _expanded.Clear(); foreach(var key in value) _expanded.Add(key); } }
    public void FocusEvent(WireEvent item)
    {
        Selected = item; _flashUntil = DateTime.UtcNow.AddSeconds(1.2); _vertical = 0;
        if(Mode == "Timeline")
        {
            var id = item.entity == "voice" ? item.parentId : item.objectId;
            foreach(var group in PlaybackGroups().Where(g => g.End == null || g.End.time >= Start))
            {
                if(group.Id == id) { if(item.entity == "voice") _expanded.Add("playback:" + id); break; }
                _vertical += PlaybackRowHeight + (_expanded.Contains("playback:"+group.Id) ? Math.Max(1,group.Voices.Count(v=>v.LastOrDefault(e=>e.kind=="stop") is not {} stop || stop.time>=Start))*38 : 0);
            }
        }
        else if(Mode == "AISAC")
        {
            foreach(var group in ControlRows())
            {
                if(group.Rows.Any(r=>r.Records.Any(e=>e.session==item.session && e.seq==item.seq))) { _expanded.Add(group.Key); break; }
                _vertical += 40 + (_expanded.Contains(group.Key) ? group.Rows.Sum(r=>ControlRowHeight(r)) : 0);
            }
        }
        else if(Mode == "Location")
        {
            SpatialFocusId=item.objectId;
            ShowSources = true;
            var cluster = SpatialPresentation.Group(Events,End,ShowBaseListeners,true,ShowDistanceListeners).FirstOrDefault(g=>g.Items.Any(e=>e.objectId==item.objectId));
            foreach(var key in _expanded.Where(k=>k.StartsWith("spatial:")).ToArray()) _expanded.Remove(key);
            if(cluster is { Items.Length: > 1 }) { _expanded.Add(cluster.Key); _vertical = Math.Max(0,Array.FindIndex(cluster.Items,e=>e.objectId==item.objectId)*34-34); }
        }
        InvalidateVisual();
    }
    private void Highlight(DrawingContext c, Rect rect)
    {
        c.FillRectangle(Palette.Hover, rect);
        c.DrawRectangle(null, new Pen(Palette.Selection, DateTime.UtcNow < _flashUntil ? 2.5 : 1), rect, 3, 3);
    }
    private void HighlightRow(DrawingContext c, Rect rect)
    {
        c.FillRectangle(Palette.Hover,rect);
        c.FillRectangle(Palette.Muted,new Rect(rect.X,rect.Y+3,3,Math.Max(0,rect.Height-6)));
    }
    private bool IsSelected(WireEvent item) => Selected is {} selected && selected.session==item.session && selected.seq==item.seq;
    public Rect SpatialOverlayBounds { get; private set; }
    public double SpatialOverlayMaxScroll { get; private set; }
    private Rect _spatialListViewport, _spatialClose, _spatialScrollTrack, _spatialScrollThumb;
    private readonly List<(Rect rect, WireEvent item)> _spatialListHits = [];
    private string? _spatialExpandedKey;
    private bool _spatialScrollDragging;
    public void CloseSpatialList()
    {
        foreach(var key in _expanded.Where(k=>k.StartsWith("spatial:",StringComparison.Ordinal)).ToArray())_expanded.Remove(key);
        _vertical=0; SpatialOverlayBounds=default; SpatialOverlayMaxScroll=0; InvalidateVisual();
    }
    private void SetSpatialScrollFromPointer(double y)
    {
        _vertical=Math.Clamp((y-_scrollGrab-_spatialScrollTrack.Y)/Math.Max(1,_spatialScrollTrack.Height-_spatialScrollThumb.Height),0,1)*SpatialOverlayMaxScroll;
        InvalidateVisual();
    }
    private Point? _drag;
    private double _dragEnd, _vertical, _contentHeight;
    private bool _panning, _space, _scrollDragging;
    private double _scrollGrab;
    private int _busCount, _busChannels;
    private readonly List<(Rect rect, WireEvent item)> _hits = [];
    private readonly List<(Rect rect, string tip)> _playbackTips = [];
    private readonly List<Rect> _relatedCueBounds = [];
    private readonly List<(Rect rect, string key)> _expandHits = [];
    private readonly HashSet<string> _expanded = [];
    private readonly List<(Rect rect, string key)> _toggleHits = [];
    private readonly List<(Rect rect, WireEvent? item, string? key)> _spatialHits = [];
    private bool _axisToggleHovered, _axisKeyboardFocus;
    private Rect? _hoverToggleRect;
    private WireEvent[]? _groupEvents, _groupGaps, _controlEvents, _controlAssociationEvents;
    private double _groupEnd=double.NaN, _controlEnd=double.NaN;
    private WireEvent[]? _categoryAssociationEvents;
    private double _categoryEnd=double.NaN;
    private Dictionary<(string Session,string Channel,string Playback),WireEvent[]> _categoriesByPlayback=[];
    private Dictionary<string,WireEvent> _cueInfoByPlayback=[];
    private WireEvent[] _categoryCatalogs=[];
    private int _controlMask;
    private PlaybackGroup[] _playbackGroups=[];
    private ControlGroup[] _controlRows=[];
    public void InvalidateSettingEvidence()
    {
        _controlEvents=null;
        InvalidateVisual();
    }
    private PlaybackGroup[] PlaybackGroups()
    {
        if(!ReferenceEquals(_groupEvents,Events)||!ReferenceEquals(_groupGaps,Discontinuities)||_groupEnd!=End)
        { _groupEvents=Events;_groupGaps=Discontinuities;_groupEnd=End;_playbackGroups=PlaybackPresentation.Group(Events.Concat(Discontinuities),End); }
        return _playbackGroups;
    }
    private WireEvent[] CategoriesFor(PlaybackGroup group,WireEvent anchor)
    {
        var evidence=AssociationEvents.Length>0?AssociationEvents:Events;
        if(!ReferenceEquals(_categoryAssociationEvents,evidence)||_categoryEnd!=End)
        {
            _categoryAssociationEvents=evidence;_categoryEnd=End;
            _categoriesByPlayback=evidence.Where(e=>e.kind=="category"&&e.parentId.Length>0&&e.time<=End)
                .GroupBy(e=>(e.session,e.channel,e.parentId))
                .ToDictionary(g=>g.Key,g=>g.GroupBy(e=>e.objectId,StringComparer.Ordinal)
                    .Select(items=>items.OrderBy(e=>e.time).ThenBy(e=>e.seq).Last())
                    .OrderBy(e=>e.name,StringComparer.Ordinal).ThenBy(e=>e.objectId,StringComparer.Ordinal).ToArray());
            _cueInfoByPlayback=evidence.Where(e=>e.kind=="cue-info"&&e.time<=End)
                .Select(e=>(Playback:e.parentId.Length>0?e.parentId:e.objectId,Event:e))
                .Where(item=>item.Playback.StartsWith("playback:",StringComparison.Ordinal))
                .GroupBy(item=>item.Playback,StringComparer.Ordinal)
                .ToDictionary(g=>g.Key,g=>g.OrderBy(item=>item.Event.time).ThenBy(item=>item.Event.seq).Last().Event,StringComparer.Ordinal);
            _categoryCatalogs=evidence.Where(e=>e.kind=="category-catalog"&&e.time<=End)
                .OrderBy(e=>e.time).ThenBy(e=>e.seq).ToArray();
        }
        return _categoriesByPlayback.GetValueOrDefault((anchor.session,anchor.channel,group.Id))??[];
    }
    private PrimaryGroupCategory? PrimaryCategoryFor(PlaybackGroup group,WireEvent[] categories)
    {
        var observedAt=categories.Length>0?categories.Max(e=>e.time):AssociationPresentation.Anchor(group)?.time??End;
        var catalog=_categoryCatalogs.LastOrDefault(e=>e.time<=observedAt+.1);
        _cueInfoByPlayback.TryGetValue(group.Id,out var cueInfo);
        if(cueInfo?.name!=group.Name)cueInfo=null;
        return PlaybackCategoryPresentation.Resolve(catalog,cueInfo,categories);
    }
    private ControlGroup[] ControlRows()
    {
        var mask=0;for(int i=0;i<ControlPresentation.Kinds.Length;i++)if(ControlKinds.Contains(ControlPresentation.Kinds[i]))mask|=1<<i;
        var relationEvidence=AssociationEvents.Length>0?AssociationEvents:Events;
        if(!ReferenceEquals(_controlEvents,Events)||!ReferenceEquals(_controlAssociationEvents,relationEvidence)||_controlEnd!=End||_controlMask!=mask)
        {
            _controlEvents=Events;_controlAssociationEvents=relationEvidence;_controlEnd=End;_controlMask=mask;
            _controlRows=ControlPresentation.Group(Events,End,ControlKinds,ControlLabels,relationEvidence,ResolveSettingEvidence);
        }
        return _controlRows;
    }
    private const double LabelWidth = UiMetrics.TimelineLabelWidth;
    private const double PlaybackRowHeight = 44;
    private double PlotWidth => Math.Max(40, Bounds.Width-LabelWidth-22);
    private double MixingViewportHeight => Math.Max(1,Bounds.Height-106);
    private double MixingMaxScroll => Math.Max(0,_contentHeight+10-MixingViewportHeight);
    private double ResourcesMaxScroll => Math.Max(0,_contentHeight+28-Bounds.Height);
    private Rect MixingScrollTrack => new(Bounds.Width-18,76,18,MixingViewportHeight);
    private Rect MixingScrollThumb
    {
        get { var track=MixingScrollTrack;var thumb=Math.Max(28,track.Height*track.Height/Math.Max(track.Height,_contentHeight+10));return new Rect(Bounds.Width-16,track.Y+(MixingMaxScroll<=0?0:_vertical/MixingMaxScroll)*(track.Height-thumb),14,thumb); }
    }
    public double ViewSpan => Live?Math.Max(.25,Math.Min(Span,End-TimeOrigin)):Span;
    public double Start => End-ViewSpan;
    private double X(double time) => LabelWidth+(time-Start)/ViewSpan*PlotWidth;
    private static readonly Typeface Font = new("Segoe UI, Microsoft YaHei UI");
    private static readonly Geometry ExpandedChevron=Geometry.Parse("M0,0 L4,4 L8,0");
    private static readonly Geometry CollapsedChevron=Geometry.Parse("M1,0 L5,4 L1,8");
    private void SelectTimeAxis(bool wall)
    {
        if(PreferWallTime==wall)return;
        PreferWallTime=wall;
        if(_axisToggleHovered)ToolTip.SetTip(this,TimeAxisToggleTip());
        TimeAxisModeChanged?.Invoke(wall);ViewChanged?.Invoke();InvalidateVisual();
    }
    public void SetTimeAxisMode(bool wall) => SelectTimeAxis(wall);
    private bool TimeAxisToggleVisible => (Events.Length>0||SupplementMetrics.Length>0) && (Mode is "Timeline" or "AISAC" or "Performance");
    private string TimeAxisToggleTip() => PreferWallTime
        ? ShowingWallTime ? "当前：钟表时间 · 点击切换为相对时间" : "偏好：钟表时间 · 当前无时钟映射 · 点击切换为相对时间"
        : "当前：相对时间 · 点击切换为钟表时间";
    private void DrawTimeAxisToggle(DrawingContext c)
    {
        var target=TimeAxisToggleBounds;
        if(_axisToggleHovered)c.DrawRectangle(Palette.Hover,null,target,7,7);
        if(_axisKeyboardFocus)c.DrawRectangle(null,new Pen(Palette.Selection,1.5),target.Deflate(1),6,6);
        var center=target.Center;
        var iconBrush=ShowingWallTime?Palette.Text:Palette.Muted;
        var pen=new Pen(iconBrush,1.4);
        c.DrawEllipse(null,pen,center,7.5,7.5);
        c.DrawLine(pen,center,new Point(center.X,center.Y-4));
        c.DrawLine(pen,center,new Point(center.X+3.2,center.Y+1.7));
    }
    private void SetAxisToggleHovered(bool hovered)
    {
        if(_axisToggleHovered==hovered)return;
        _axisToggleHovered=hovered;InvalidateVisual();
    }
    private static double WallTickStep(double span,double width)
    {
        var pixels=span<1?110:span>=3600?100:78;
        var rough=span/Math.Clamp(width/pixels,2,10);
        var magnitude=Math.Pow(10,Math.Floor(Math.Log10(rough)));
        return rough/magnitude<=1?magnitude:rough/magnitude<=2?2*magnitude:rough/magnitude<=5?5*magnitude:10*magnitude;
    }
    private static string WallTickLabel(DateTimeOffset wall,double step)
    {
        var local=wall.ToLocalTime();
        return local.ToString(step<1?"HH:mm:ss.fff":step<60?"HH:mm:ss":step<3600?"HH:mm":"MM-dd HH:mm",CultureInfo.InvariantCulture);
    }
    public static string TimeLabel(double v)
    {
        if(!double.IsFinite(v))return "—";
        var sign=v<0?"−":"";var value=Math.Round(Math.Abs(v)*1000,MidpointRounding.AwayFromZero)/1000;
        return value>=3600?$"{sign}{(long)(value/3600):00}:{(int)(value/60)%60:00}:{value%60:00.000}":$"{sign}{(int)(value/60):00}:{value%60:00.000}";
    }
    private bool VisibleRow(double y,double height,double top=68) => y+height>top&&y<Bounds.Height-28;
    private void ToggleExpansion(string key)
    {
        bool had=_expanded.Contains(key);
        ToolTip.SetTip(this,null);ToolTip.SetIsOpen(this,false);
        SetHoveredLink(null);
        if(key.StartsWith("spatial:",StringComparison.Ordinal)){foreach(var old in _expanded.Where(k=>k.StartsWith("spatial:",StringComparison.Ordinal)).ToArray())_expanded.Remove(old);_vertical=0;}
        if(had)_expanded.Remove(key);else _expanded.Add(key);InvalidateVisual();
    }
    private void SetHoveredLink(Rect? rect)
    {
        if(_hoverLinkRect==rect)return;
        _hoverLinkRect=rect;
        Cursor=rect.HasValue?LinkCursor:null;
        InvalidateVisual();
    }
    private void SetHoveredToggle(Rect? rect)
    {
        if(_hoverToggleRect==rect)return;
        _hoverToggleRect=rect;InvalidateVisual();
    }
    private static string ToggleTip(string key) => key.StartsWith("control:",StringComparison.Ordinal)
        ? $"筛选 {ControlPresentation.KindLabel(key[8..])} · 可多选"
        : key switch {
            "source"=>"显示或隐藏音源",
            "distance-listener"=>"显示或隐藏衰减监听点",
            _=>"显示或隐藏基础监听器" };
    private WireEvent? UpdateLinkHover(Point point)
    {
        var link=_links.LastOrDefault(h=>h.Rect.Contains(point));
        SetHoveredLink(link.Event==null?null:link.Rect);
        return link.Event;
    }

    public TimelineControl()
    {
        ClipToBounds=true; Focusable=true; MinHeight=220;
        GotFocus += (_,_)=>{_axisKeyboardFocus=true;InvalidateVisual();};
        PointerWheelChanged += (_,e) => {
            SetHoveredLink(null);
            if(Mode=="Location")
            {
                if(SpatialOverlayBounds.Contains(e.GetPosition(this)))_vertical=Math.Clamp(_vertical-e.Delta.Y*34,0,SpatialOverlayMaxScroll);
                InvalidateVisual();e.Handled=true;return;
            }
            if(Mode!="Mixing" && e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                var f=Math.Clamp((e.GetPosition(this).X-LabelWidth)/PlotWidth,0,1); var anchor=Start+f*ViewSpan;
                Span=Math.Clamp(Span*(e.Delta.Y>0?.8:1.25),.25,86400); if(!Live)End=anchor+Span*(1-f);
            } else if(Mode!="Mixing" && e.KeyModifiers.HasFlag(KeyModifiers.Shift)){End-=e.Delta.Y*Span*.1;Live=false;}
            else if(Mode=="Performance")
            {
                var next=Math.Clamp(_vertical-e.Delta.Y*36,0,ResourcesMaxScroll);
                if(next==_vertical){e.Handled=true;return;}
                _vertical=next;
            }
            else _vertical=Math.Clamp(_vertical-e.Delta.Y*36,0,Mode=="Mixing"?MixingMaxScroll:Math.Max(0,_contentHeight+100-Bounds.Height));
            if(Mode!="Mixing"||e.KeyModifiers.HasFlag(KeyModifiers.Control)||e.KeyModifiers.HasFlag(KeyModifiers.Shift))ViewChanged?.Invoke();InvalidateVisual();e.Handled=true;
        };
        PointerPressed += (_,e) => {
            Focus();_axisKeyboardFocus=false;var p=e.GetPosition(this);
            if(TimeAxisToggleVisible&&TimeAxisToggleBounds.Contains(p))
            {SelectTimeAxis(!PreferWallTime);e.Handled=true;return;}
            if(Mode=="Location"&&SpatialOverlayBounds.Contains(p))
            {
                SetHoveredLink(null);
                ToolTip.SetTip(this,null);ToolTip.SetIsOpen(this,false);
                if(_spatialClose.Contains(p)){CloseSpatialList();e.Handled=true;return;}
                if(SpatialOverlayMaxScroll>0&&_spatialScrollTrack.Contains(p))
                {
                    _scrollGrab=_spatialScrollThumb.Contains(p)?p.Y-_spatialScrollThumb.Y:_spatialScrollThumb.Height/2;
                    _spatialScrollDragging=true;SetSpatialScrollFromPointer(p.Y);e.Pointer.Capture(this);e.Handled=true;return;
                }
                var row=_spatialListHits.LastOrDefault(h=>h.rect.Contains(p));
                if(_spatialListViewport.Contains(p)&&row.item!=null){Selected=row.item;EventSelected?.Invoke(row.item);InvalidateVisual();}
                e.Handled=true;return;
            }
            var link = _links.LastOrDefault(h=>h.Rect.Contains(p)); if(link.Event!=null){PlaybackRequested?.Invoke(link.Event);e.Handled=true;return;}
            var toggle=_toggleHits.LastOrDefault(h=>h.rect.Contains(p));
            if(toggle.key!=null)
            {
                if(toggle.key.StartsWith("control:")){var kind=toggle.key[8..];SetControlKind(kind,!ControlKinds.Contains(kind));}
                else if(toggle.key=="source")SetSpatialLayer(toggle.key,!ShowSources);
                else if(toggle.key=="distance-listener")SetSpatialLayer(toggle.key,!ShowDistanceListeners);
                else SetSpatialLayer(toggle.key,!ShowBaseListeners);
                e.Handled=true;return;
            }
            if(Mode=="Location")
            {
                var spatial=_spatialHits.LastOrDefault(h=>h.rect.Contains(p));
                if(spatial.key!=null){ToggleExpansion(spatial.key);e.Handled=true;return;}
                if(spatial.item!=null){Selected=spatial.item;EventSelected?.Invoke(spatial.item);InvalidateVisual();e.Handled=true;return;}
            }
            if(Mode=="Mixing"&&MixingMaxScroll>0&&MixingScrollTrack.Contains(p)&&e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            { var thumb=MixingScrollThumb;_scrollGrab=thumb.Contains(p)?p.Y-thumb.Y:thumb.Height/2;_scrollDragging=true;SetMixingScrollFromPointer(p.Y);e.Pointer.Capture(this);e.Handled=true;return; }
            _panning=_space||e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;
            if(_panning||e.KeyModifiers.HasFlag(KeyModifiers.Shift)){_drag=p;_dragEnd=End;e.Pointer.Capture(this);return;}
            var expand=_expandHits.LastOrDefault(h=>h.rect.Contains(p));
            if(expand.key!=null){ToggleExpansion(expand.key);return;}
            var hit=_hits.LastOrDefault(h=>h.rect.Contains(p));
            if(hit.item!=null){Selected=hit.item;EventSelected?.Invoke(hit.item);}else{Selected=null;SetSelection(null,null);SelectionCleared?.Invoke();}
            InvalidateVisual();
        };
        PointerMoved += (_,e) => {
            var p=e.GetPosition(this);
            if(_drag==null&&!_spatialScrollDragging&&!_scrollDragging&&TimeAxisToggleVisible&&TimeAxisToggleBounds.Contains(p))
            {SetHoveredToggle(null);SetHoveredLink(null);SetAxisToggleHovered(true);Cursor=LinkCursor;ToolTip.SetTip(this,TimeAxisToggleTip());return;}
            if(_axisToggleHovered){SetAxisToggleHovered(false);Cursor=null;}
            if(_spatialScrollDragging){SetHoveredToggle(null);SetHoveredLink(null);SetSpatialScrollFromPointer(p.Y);e.Handled=true;return;}
            if(_scrollDragging){SetHoveredToggle(null);SetHoveredLink(null);SetMixingScrollFromPointer(p.Y);e.Handled=true;return;}
            if(Mode=="Location"&&SpatialOverlayBounds.Contains(p)){SetHoveredToggle(null);SetHoveredLink(null);ToolTip.SetTip(this,null);ToolTip.SetIsOpen(this,false);return;}
            var toggle=_drag==null?_toggleHits.LastOrDefault(h=>h.rect.Contains(p)):default;
            SetHoveredToggle(toggle.key==null?null:toggle.rect);
            if(toggle.key!=null){SetHoveredLink(null);Cursor=LinkCursor;ToolTip.SetTip(this,ToggleTip(toggle.key));return;}
            var linkEvent=_drag==null?UpdateLinkHover(p):null;
            if(linkEvent!=null){ToolTip.SetTip(this,"定位声音时间线 · "+linkEvent.name);return;}
            if(_drag!=null)SetHoveredLink(null);
            if(_drag is not {} start){
                var playbackTip=Mode=="Timeline"?_playbackTips.LastOrDefault(h=>h.rect.Contains(p)).tip:null;
                if(playbackTip!=null){ToolTip.SetTip(this,playbackTip);return;}
                var hit=_hits.LastOrDefault(h=>h.rect.Contains(p));ToolTip.SetTip(this,hit.item==null?null:$"{hit.item.name}\n{(Mode=="AISAC"?ControlRowTipStamp(hit.item.time):DisplayStamp(hit.item.time))} · {hit.item.kind}\n{hit.item.detail}");return;
            }
            var delta=p.X-start.X;if(Math.Abs(delta)<8)return;
            if(_panning){End=_dragEnd-delta/PlotWidth*ViewSpan;Live=false;}
            else{SelectionStart=Start+Math.Clamp((start.X-LabelWidth)/PlotWidth,0,1)*ViewSpan;SelectionEnd=Start+Math.Clamp((p.X-LabelWidth)/PlotWidth,0,1)*ViewSpan;}
            ViewChanged?.Invoke();InvalidateVisual();
        };
        PointerExited += (_,_)=>{SetAxisToggleHovered(false);SetHoveredToggle(null);SetHoveredLink(null);Cursor=null;ToolTip.SetTip(this,null);};
        PointerReleased += (_,e)=>{_drag=null;_scrollDragging=false;_spatialScrollDragging=false;e.Pointer.Capture(null);};
        KeyDown += (_,e)=>{if(e.Key==Key.Enter&&_axisKeyboardFocus&&TimeAxisToggleVisible){SelectTimeAxis(!PreferWallTime);e.Handled=true;}else if(e.Key==Key.Escape&&Mode=="Location"&&_spatialExpandedKey!=null){CloseSpatialList();e.Handled=true;}else if(e.Key==Key.Space){_space=true;e.Handled=true;}};
        KeyUp += (_,e)=>{if(e.Key==Key.Space){_space=false;e.Handled=true;}};
        LostFocus += (_,_)=>{_space=false;_axisKeyboardFocus=false;InvalidateVisual();};
    }
    private void SetMixingScrollFromPointer(double y)
    { var track=MixingScrollTrack;var thumb=MixingScrollThumb;_vertical=Math.Clamp((y-_scrollGrab-track.Y)/Math.Max(1,track.Height-thumb.Height),0,1)*MixingMaxScroll;InvalidateVisual(); }
    private void Text(DrawingContext c,string text,double x,double y,IBrush? brush=null,double size=11)
        =>c.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Font,size,brush??Palette.Muted),new Point(x,y));
    private static double TextWidth(string value,double size)
        =>new FormattedText(value,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Font,size,Brushes.White).Width;
    private static string Elide(string value,double width,double size)
    {
        double Measure(string text)=>TextWidth(text,size);
        if(Measure(value)<=width)return value;
        const string ellipsis="…";
        if(Measure(ellipsis)>width)return "";
        int low=0,high=value.Length;
        while(low<high){var mid=(low+high+1)/2;if(Measure(value[..mid]+ellipsis)<=width)low=mid;else high=mid-1;}
        return value[..low]+ellipsis;
    }
    private void RowName(DrawingContext c,string name,double y,IBrush? brush=null)
    {using var clip=c.PushClip(new Rect(12,y,LabelWidth-24,23));Text(c,name,12,y,brush??Palette.Text,12);}
    private void Empty(DrawingContext c,string title,string description)
    {Text(c,title,26,105,Palette.Text,18);Text(c,description,26,141,size:12);}
    public override void Render(DrawingContext c)
    {
        base.Render(c);c.FillRectangle(Palette.Canvas,new Rect(Bounds.Size));_links.Clear();_hits.Clear();_playbackTips.Clear();_relatedCueBounds.Clear();_expandHits.Clear();_toggleHits.Clear();_spatialHits.Clear();_spatialListHits.Clear();SpatialOverlayBounds=default;SpatialOverlayMaxScroll=0;_spatialExpandedKey=null;
        if(Events.Length==0&&SupplementMetrics.Length==0){Empty(c,"等待音频观测","在游戏里开启 CriScope 采集，或打开已有日志。");return;}
        if(Mode=="Location"){DrawLocations(c);return;}if(Mode=="Mixing"){DrawMixing(c);DrawScrollHint(c);return;}
        var axisStart=Math.Max(TimeOrigin,Start);
        var clockAtStart=ShowingWallTime?ClockTimeAt?.Invoke(axisStart):null;
        if(clockAtStart is { } clockStart)
        {
            var step=WallTickStep(ViewSpan,PlotWidth);
            var wallStart=clockStart.ToUnixTimeMilliseconds()/1000d;
            for(double seconds=Math.Ceiling(wallStart/step)*step;seconds<=wallStart+(End-axisStart)+step*.000001;seconds+=step)
            {
                var t=axisStart+seconds-wallStart;var x=X(t);
                c.DrawLine(new Pen(Palette.TimelineGrid,.6),new Point(x,34),new Point(x,Bounds.Height-28));
                var wall=DateTimeOffset.FromUnixTimeMilliseconds((long)Math.Round(seconds*1000));
                Text(c,WallTickLabel(wall,step),x+4,10,size:10);
            }
        }
        else
        {
            double step=Math.Pow(10,Math.Floor(Math.Log10(ViewSpan/8)));if(ViewSpan/step>16)step*=5;else if(ViewSpan/step>10)step*=2;
            for(double t=Math.Max(TimeOrigin,TimeOrigin+Math.Ceiling((Start-TimeOrigin)/step)*step);t<=End;t+=step){var x=X(t);c.DrawLine(new Pen(Palette.TimelineGrid,.6),new Point(x,34),new Point(x,Bounds.Height-28));Text(c,Stamp(t),x+4,10,size:10);}
        }
        c.DrawLine(new Pen(Palette.Border),new Point(0,UiMetrics.TimelineRulerHeight),new Point(Bounds.Width,UiMetrics.TimelineRulerHeight));
        DrawTimeAxisToggle(c);
        // Guides are background geometry: cards, labels and event markers must paint above them.
        if(SelectionStart is {} a&&SelectionEnd is {} b&&Math.Abs(a-b)>.000001){var l=Math.Clamp(X(Math.Min(a,b)),LabelWidth,LabelWidth+PlotWidth);var r=Math.Clamp(X(Math.Max(a,b)),LabelWidth,LabelWidth+PlotWidth);c.DrawRectangle(null,new Pen(Palette.Selection,1.5),new Rect(l,35,Math.Max(0,r-l),Math.Max(0,Bounds.Height-64)));}
        if(Selected is {} selected&&selected.time>=Start&&selected.time<=End) {
            var x=X(selected.time);
            if(Mode=="AISAC")
            {
                c.DrawLine(new Pen(Palette.Selection,1.5),new Point(x,28),new Point(x,34));
                c.DrawEllipse(Palette.Selection,null,new Point(x,28),2,2);
            }
            else c.DrawLine(new Pen(Palette.Selection,1),new Point(x,35),new Point(x,Bounds.Height-28));
        }
        if(Mode=="AISAC")DrawControls(c);else if(Mode=="Performance")DrawResources(c);else DrawTracks(c);
        c.FillRectangle(Palette.Canvas,new Rect(0,Bounds.Height-28,Bounds.Width,28));Text(c,"Ctrl 缩放 · 空格拖动 · Shift 选区",16,Bounds.Height-20,size:10);
        if(Live)Text(c,SourceConnected?"实时":"已断开",Bounds.Width-64,Bounds.Height-21,SourceConnected?Palette.Good:Palette.Request,10);
    }
    private void DrawTracks(DrawingContext c)
    {
        Text(c,"播放实例",16,45,Palette.Text,12);
        Text(c,"声音活动",LabelWidth+10,45,Palette.Muted,11);
        c.DrawLine(new Pen(Palette.TimelineGrid,1),new Point(LabelWidth,34),new Point(LabelWidth,Bounds.Height-28));
        var groups=PlaybackGroups();
        double y=75-_vertical;int row=0;
        using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var group in groups)
        {
            var observedEnd=group.End?.time??group.Discontinuity?.time;
            if(observedEnd is {} ended&&ended<Start)continue;
            var anchor=group.Request??group.Voices.SelectMany(v=>v).FirstOrDefault()??group.End;
            if(anchor==null)continue;
            var key="playback:"+group.Id;bool expanded=_expanded.Contains(key);
            var voiceRows=expanded?group.Voices.Where(v=>v.LastOrDefault(e=>e.kind=="stop") is not {} stop||stop.time>=Start).ToArray():[];
            double h=PlaybackRowHeight+(expanded?Math.Max(1,voiceRows.Length)*38:0);
            if(!VisibleRow(y,h)){y+=h;row+=1+(expanded?Math.Max(1,voiceRows.Length):0);continue;}
            if(row++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,LabelWidth,PlaybackRowHeight));
            var selectedId = Selected?.entity == "voice" ? Selected.parentId : Selected?.objectId;
            if(selectedId == group.Id) HighlightRow(c,new Rect(1,y+1,Bounds.Width-22,PlaybackRowHeight-2));
            var categories=CategoriesFor(group,anchor);
            var primaryCategory=PrimaryCategoryFor(group,categories);
            var categoryBrush=primaryCategory is null?Palette.Muted:Palette.Category(primaryCategory.Ordinal);
            if(primaryCategory!=null)c.FillRectangle(categoryBrush,new Rect(5,y+8,3,28));
            using(c.PushTransform(Matrix.CreateTranslation(15,y+18)))
                c.DrawGeometry(null,new Pen(Palette.Muted,1.2),expanded?ExpandedChevron:CollapsedChevron);
            using(c.PushClip(new Rect(29,y+2,LabelWidth-39,18)))
                Text(c,Elide(group.Name,LabelWidth-42,12),30,y+3,Palette.Text,12);
            var status=!SourceConnected&&group.ActiveVoiceCount>0?"已断开 · 保留最后状态":group.StatusLabel;
            var duration=group.DurationAt(End);
            var durationText=duration is {} seconds?seconds.ToString("0.000",CultureInfo.InvariantCulture)+" 秒":"";
            var categoryText=string.Join(" / ",categories.Select(e=>e.name));
            var summary=string.Join(" · ",new[]{status,durationText}.Where(text=>text.Length>0));
            using(c.PushClip(new Rect(29,y+19,LabelWidth-39,20)))
                Text(c,Elide(summary,LabelWidth-40,10),30,y+23,group.ActiveVoiceCount>0&&SourceConnected?Palette.Request:Palette.Muted,10);
            string FullStamp(double time)=>ClockTimeAt?.Invoke(time) is { } wall
                ? wall.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff",CultureInfo.InvariantCulture) : Stamp(time);
            var endedLabel=group.EndedAt??group.InstanceEndedAt;
            var fullTime=group.StartedAt is {} started?"开始播放："+FullStamp(started):"开始播放：未记录";
            if(group.RequestAt is {} requestedAt)fullTime="请求播放："+FullStamp(requestedAt)+"\n"+fullTime;
            if(endedLabel is {} endedTime)fullTime+="\n结束播放："+FullStamp(endedTime);
            var tip=group.Name+"\n"+summary+(categoryText.Length>0?"\nCategory："+categoryText:"\nCategory：实例归属未记录")+"\n"+fullTime;
            var visibleTop=Math.Max(y,68);
            var visibleBottom=Math.Min(y+PlaybackRowHeight,Bounds.Height-28);
            if(visibleBottom>visibleTop)_playbackTips.Add((new Rect(4,visibleTop,Math.Max(0,Bounds.Width-26),visibleBottom-visibleTop),tip));
            _expandHits.Add((new Rect(9,y,17,PlaybackRowHeight),key));_hits.Add((new Rect(27,y,LabelWidth-27,PlaybackRowHeight),anchor));
            // A Cue request is a point observation, not evidence of continuous voice allocation.
            if(group.Request is {} request&&request.time>=Start&&request.time<=End)
            {
                var px=X(request.time);var py=y+PlaybackRowHeight/2;var pen=new Pen(Palette.Request,1.5);
                c.DrawLine(pen,new Point(px,py-6),new Point(px+5,py));c.DrawLine(pen,new Point(px+5,py),new Point(px,py+6));
                c.DrawLine(pen,new Point(px,py+6),new Point(px-5,py));c.DrawLine(pen,new Point(px-5,py),new Point(px,py-6));
                if(IsSelected(request))c.DrawEllipse(null,new Pen(Palette.Selection,2),new Point(px,py),9,9);
                _hits.Add((new Rect(px-6,y,12,PlaybackRowHeight),request));
            }
            foreach(var interval in group.VoiceIntervals)
                DrawInterval(interval.Begin,interval.End,group.End,y,PlaybackRowHeight,
                    categoryBrush,interval.Begin.kind!="play"||PlaybackPresentation.IsUnknownStart(interval.Begin));
            y+=PlaybackRowHeight;
            if(!expanded)continue;
            foreach(var voice in voiceRows)
            {
                var begin=voice.FirstOrDefault(e=>e.kind=="play")??voice[0];var stop=voice.LastOrDefault(e=>e.kind=="stop");
                if(!VisibleRow(y,38)){y+=38;row++;continue;}
                if(row++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,LabelWidth,38));
                bool unknown=begin.kind!="play"||PlaybackPresentation.IsUnknownStart(begin);
                Text(c,"↳ Voice "+(Array.IndexOf(group.Voices,voice)+1),28,y+3,Palette.Voice,11);
                var state=stop!=null?"已结束":group.End!=null?"实例已结束":group.HasEvidenceGap?"采集中断":SourceConnected?"播放中":"已断开";
                var elapsed=stop!=null&&!unknown?(stop.time-begin.time).ToString("0.000",CultureInfo.InvariantCulture)+" 秒":unknown?"开始时间未记录":"+"+Stamp(begin.time);
                Text(c,state+" · "+elapsed,28,y+21,size:9);
                DrawInterval(begin,stop,group.End,y,38,Palette.Voice,unknown);
                _hits.Add((new Rect(24,y,LabelWidth-24,38),begin));y+=38;
            }
            if(voiceRows.Length==0){Text(c,group.Voices.Length==0?"此请求没有关联的 Voice 记录":"当前范围没有 Voice 区间",28,y+10,size:10);y+=38;}
        }
        _contentHeight=y+_vertical-75;
        if(row==0)Empty(c,"当前范围没有播放实例","可调整时间范围查看历史。");

        void DrawInterval(WireEvent begin,WireEvent? voiceStop,WireEvent? instanceEnd,double top,double height,IBrush brush,bool unknown)
        {
            if(!VisibleRow(top,height))return;
            var stop=voiceStop;
            if(instanceEnd!=null&&(stop==null||instanceEnd.time<stop.time))stop=instanceEnd;
            var end=Math.Min(stop?.time??End,End);
            var gap=Discontinuities.Where(d=>(d.session.Length==0||begin.session.Length==0||d.session==begin.session)
                &&(d.channel.Length==0||begin.channel.Length==0||d.channel==begin.channel)
                &&(d.time>begin.time||d.time==begin.time&&d.seq>begin.seq)&&d.time<end).MinBy(d=>d.time);
            if(gap!=null){end=gap.time;stop=null;}
            var left=Math.Max(LabelWidth,X(begin.time));var right=Math.Min(LabelWidth+PlotWidth,X(end));
            if(right<left)return;
            var center=top+height/2;var width=Math.Max(2,right-left);
            c.FillRectangle(brush,new Rect(left,center-3,width,6));
            if(unknown){c.DrawLine(new Pen(brush,1.5),new Point(left+7,center-6),new Point(left,center));c.DrawLine(new Pen(brush,1.5),new Point(left,center),new Point(left+7,center+6));}
            else if(begin.time>=Start)c.DrawLine(new Pen(brush,2),new Point(left,center-9),new Point(left,center+9));
            if(stop!=null)c.DrawLine(new Pen(Palette.Muted,2),new Point(right,center-9),new Point(right,center+9));
            else c.DrawEllipse(Palette.Canvas,new Pen(brush,1.5),new Point(right,center),4,4);
            if(IsSelected(begin)&&begin.time>=Start)c.DrawEllipse(null,new Pen(Palette.Selection,2),new Point(left,center),11,11);
            if(stop!=null&&IsSelected(stop))c.DrawEllipse(null,new Pen(Palette.Selection,2),new Point(right,center),11,11);
            _hits.Add((new Rect(left,top,Math.Max(8,right-left),height),begin));
            if(stop!=null)_hits.Add((new Rect(right-5,top,10,height),stop));
        }
    }

    private void DrawControls(DrawingContext c)
    {
        double chipX=12,chipY=40;
        foreach(var kind in ControlPresentation.Kinds)
        {
            var width=kind=="sequence"?112:kind=="selector"?104:kind=="aisac"?90:kind=="beat"?104:82;
            if(chipX+width>Bounds.Width-12){chipX=12;chipY+=31;}
            DrawToggle(c,new Rect(chipX,chipY,width,25),ControlPresentation.KindLabel(kind),ControlKinds.Contains(kind),"control:"+kind,Palette.Control(kind));
            chipX+=width+7;
        }
        var groups=ControlRows();
        if(groups.Length==0){_contentHeight=0;Empty(c,ControlKinds.Count==0?"尚未选择控制类型":"当前筛选没有记录",ControlKinds.Count==0?"选择上方类型以查看记录。":"可调整筛选或时间范围。");return;}
        var contentTop=chipY+33;
        double y=contentTop-_vertical;int i=0;
        using var clip=c.PushClip(new Rect(0,contentTop-5,Bounds.Width,Math.Max(0,Bounds.Height-contentTop-23)));
        foreach(var group in groups)
        {
            bool expanded=_expanded.Contains(group.Key);
            double height=40+(expanded?group.Rows.Sum(r=>ControlRowHeight(r)):0);
            if(!VisibleRow(y,height,contentTop-5)){y+=height;continue;}
            c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,40));
            using(c.PushClip(new Rect(12,y+1,Math.Max(0,Bounds.Width-36),38)))
            {
                using(c.PushTransform(Matrix.CreateTranslation(12,y+8)))c.DrawGeometry(null,new Pen(Palette.Muted,1.5),Geometry.Parse(expanded?"M0,0 L5,5 L10,0":"M2,0 L7,5 L2,10"));
                Text(c,group.Name,28,y+3,group.Rows.Select(r=>r.Kind).Distinct().Count()==1?Palette.Control(group.Rows[0].Kind):Palette.Text,12);
                Text(c,group.Summary,27,y+22,Palette.Muted,10);
            }
            _expandHits.Add((new Rect(0,y,Math.Max(0,Bounds.Width-20),40),group.Key));
            y+=40;
            if(!expanded)continue;
            foreach(var row in group.Rows)
            {
                double rowHeight=ControlRowHeight(row);
                if(!VisibleRow(y,rowHeight,contentTop-5)){y+=rowHeight;continue;}
                var last=row.Latest;var samples=row.Records.Where(e=>e.time>=Start).ToArray();
                var brush=Palette.Control(last.kind);
                if(i++%2==0)c.FillRectangle(Palette.Panel,new Rect(0,y,Bounds.Width,rowHeight));
                if(Selected is {} selected && row.Records.Any(e=>e.seq==selected.seq&&e.session==selected.session)) HighlightRow(c,new Rect(24,y+1,Bounds.Width-46,rowHeight-2));
                var pointY=y+rowHeight-10;
                c.DrawLine(new Pen(Palette.Border,.7),new Point(LabelWidth+8,pointY),new Point(Bounds.Width-22,pointY));
                if(Selected is {} cursor && cursor.time>=Start&&cursor.time<=End)
                    c.DrawLine(new Pen(Palette.Selection,1),new Point(X(cursor.time),pointY-9),new Point(X(cursor.time),pointY+9));
                using(c.PushClip(new Rect(26,y+1,LabelWidth-32,rowHeight)))
                {
                    Text(c,row.Name,28,y+4,brush,12);
                    Text(c,$"{row.Records.Length} 条"+(samples.Length==0?" · 本窗无新记录":""),28,y+25,size:10);
                }
                var owners=RelatedOwners(row);
                double left=LabelWidth+10,width=Math.Max(0,Math.Min(370,Bounds.Width-left-22));
                if(owners.Length==1)DrawRelatedCue(c,owners[0],left,y+22,width);
                else if(owners.Length>1) {
                    var key="related:"+row.Key;
                    using(c.PushClip(new Rect(left,y+22,width,28)))
                        Text(c,(_expanded.Contains(key)?"▾ ":"▸ ")+$"写入时最近 · {owners[0].Name}（共 {owners.Length} 个）",left,y+26,Palette.Voice,11);
                    _expandHits.Add((new Rect(left,y+22,width,28),key));
                    if(_expanded.Contains(key))for(int ownerIndex=0;ownerIndex<owners.Length;ownerIndex++)
                        DrawRelatedCue(c,owners[ownerIndex],left+10,y+54+ownerIndex*32,Math.Max(0,width-10));
                }
                else if(row.Kind is "aisac" or "selector" && !last.objectId.StartsWith("category",StringComparison.OrdinalIgnoreCase))
                    Text(c,row.EmptyRelationshipLabel,left,y+30,size:10);
                var recent=(last.kind is "aisac" or "selector"?"最近设置：":"最近记录：")+ControlPresentation.Value(last);
                var time=ControlRowStamp(last.time);
                double recentX=LabelWidth+10,available=Math.Max(0,Bounds.Width-recentX-22),timeWidth=TextWidth(time,10);
                bool showTime=available>=timeWidth+12+TextWidth("最近设置：",12);
                var visibleRecent=Elide(recent,Math.Max(0,available-(showTime?timeWidth+12:0)),12);
                using(c.PushClip(new Rect(recentX,y+1,available,22)))
                {
                    Text(c,visibleRecent,recentX,y+5,Palette.Text,12);
                    if(showTime)Text(c,time,recentX+TextWidth(visibleRecent,12)+12,y+6,size:10);
                }
                _hits.Add((new Rect(24,y,Math.Max(0,Bounds.Width-44),rowHeight),last));
                // Discrete settings and callbacks; no interpolated curve or mixed parent value.
                foreach(var e in samples)
                {
                    var x=X(e.time);var pointBrush=e.kind=="sequence"?Palette.SequenceTag(e.name):brush;c.DrawEllipse(e.estimatedTime?null:pointBrush,e.estimatedTime?new Pen(pointBrush,1.3):null,new Point(x,pointY),3,3);
                    if(IsSelected(e))c.DrawEllipse(null,new Pen(Palette.Selection,2),new Point(x,pointY),6,6);
                    _hits.Add((new Rect(x-5,pointY-8,10,16),e));
                }

                y+=rowHeight;
            }
        }
        _contentHeight=y+_vertical-contentTop;
    }
    private PlaybackGroup[] RelatedOwners(ControlRow row)=>row.SettingOwners;
    private double ControlRowHeight(ControlRow row)=>74+(_expanded.Contains("related:"+row.Key)?RelatedOwners(row).Length*32:0);
    private void DrawRelatedCue(DrawingContext c,PlaybackGroup owner,double x,double y,double width)
    {
        _relatedCueBounds.Add(new Rect(x,y,width,28));
        var identity=ControlLabels.Get("播放实例",JsonSerializer.Serialize(new[]{owner.Request?.session??"",owner.Id})).Replace("播放实例 ","");
        var target=AssociationPresentation.Anchor(owner);
        bool hasLink=width>=28 && target is {} anchor && Events.Any(e=>e.session==anchor.session&&e.seq==anchor.seq);
        double idWidth=width>=130?TextWidth(identity,11):0;
        var label=Elide("设置时 · "+owner.Name,Math.Max(0,width-idWidth-(idWidth>0?14:0)-36),11);
        double buttonX=x+Math.Min(Math.Max(0,width-28),TextWidth(label,11)+(idWidth>0?idWidth+14:8));
        using(c.PushClip(new Rect(x,y,width,28)))
        {
            Text(c,label,x,y+4,Palette.Text,11);
            if(idWidth>0)Text(c,identity,x+TextWidth(label,11)+8,y+4,Palette.Muted,11);
            if(hasLink)
            {
                var buttonRect=new Rect(buttonX,y,28,28);
                var hovered=_hoverLinkRect==buttonRect;
                if(hovered)c.FillRectangle(Palette.Hover,buttonRect);
                c.DrawRectangle(null,new Pen(hovered?Palette.Text:Palette.Border,hovered?1.2:.8),buttonRect,4,4);
                using(c.PushTransform(Matrix.CreateScale(.65,.65)*Matrix.CreateTranslation(buttonX+6,y+5)))
                    c.DrawGeometry(null,new Pen(hovered?Palette.Text:Palette.Muted,1.6),Geometry.Parse(VisualLanguage.Path(IconKind.Locate)));
                _links.Add((buttonRect,target!));
            }
            else Text(c,"历史",buttonX,y+5,Palette.Muted,10);
        }
    }
    private void DrawToggle(DrawingContext c,Rect rect,string label,bool enabled,string key,IBrush brush)
    {
        var hovered=_hoverToggleRect==rect;
        c.DrawRectangle(hovered?Palette.Hover:enabled?Palette.Panel:Palette.Canvas,
            new Pen(hovered?Palette.Muted:Palette.Border,.8),rect,5,5);
        if(enabled)c.FillRectangle(brush,new Rect(rect.X+2,rect.Y+6,2,rect.Height-12));
        if(key is "source" or "distance-listener" or "listener") {
            using(c.PushTransform(Matrix.CreateScale(.55,.55)*Matrix.CreateTranslation(rect.X+8,rect.Y+5)))
                c.DrawGeometry(null,new Pen(enabled?Palette.Text:Palette.Muted,2),key=="source"?SpeakerIcon:EarIcon);
            Text(c,label,rect.X+29,rect.Y+5,enabled?Palette.Text:Palette.Muted,11);
        } else {
            var kind=key.StartsWith("control:")?key.Substring(8):"";
            using(c.PushTransform(Matrix.CreateScale(.5,.5)*Matrix.CreateTranslation(rect.X+8,rect.Y+6)))
                c.DrawGeometry(null,new Pen(enabled?Palette.Text:Palette.Muted,1.8),Geometry.Parse(VisualLanguage.Path(VisualLanguage.EventIcon(kind))));
            Text(c,label,rect.X+25,rect.Y+5,enabled?Palette.Text:Palette.Muted,11);
        }
        _toggleHits.Add((rect,key));
    }
    private void DrawSeries(DrawingContext c,WireEvent[] all,double y,double height,IBrush brush,bool stepped)
    {
        var samples=all.Where(e=>e.time>=Start&&e.time<=End&&double.IsFinite(e.value)).ToArray();if(samples.Length==0)return;var min=samples.Min(e=>e.value);var max=samples.Max(e=>e.value);
        if(min==max){min-=Math.Max(.1,Math.Abs(min)*.1);max+=Math.Max(.1,Math.Abs(max)*.1);}else Text(c,samples[0].kind=="metric"?$"自动范围 {MetricPresentation.Value(samples[0],min)} – {MetricPresentation.Value(samples[0],max)}":$"自动范围 {min:0.###} – {max:0.###}",LabelWidth+8,y+2,size:9);
        Point? previous=null;double pt=0;foreach(var e in samples){var p=new Point(X(e.time),y+height-8-(e.value-min)/(max-min)*(height-26));if(previous is {} prev&&!Discontinuities.Any(d=>d.time>=pt&&d.time<=e.time)&&(stepped||e.time-pt<3)){if(stepped){c.DrawLine(new Pen(brush,1.5),prev,new Point(p.X,prev.Y));c.DrawLine(new Pen(brush,1.5),new Point(p.X,prev.Y),p);}else c.DrawLine(new Pen(brush,1.5),prev,p);}c.DrawEllipse(brush,null,p,2.5,2.5);_hits.Add((new Rect(p.X-5,p.Y-6,10,12),e));previous=p;pt=e.time;}
    }
    internal static string MetricName(WireEvent e) => MetricPresentation.Name(e);
    private void DrawResources(DrawingContext c)
    {
        Text(c,"资源用量",16,45,Palette.Text,12);
        var groups=Events.Where(e=>e.kind=="metric"&&e.time<=End&&double.IsFinite(e.value)).GroupBy(e=>e.objectId+"/"+e.name).Select(g=>g.OrderBy(e=>e.time).ToArray()).Concat(SupplementMetrics.Where(e=>double.IsFinite(e.value)).Select(e=>new[]{e})).ToArray();
        if(groups.Length==0){_contentHeight=0;_vertical=0;Empty(c,"资源指标尚未提供","可通过 SDK 扩展提供 Atom / FS 内存指标。");return;}
        string Section(WireEvent e) {
            var n=(e.name+e.objectId).ToLowerInvariant();
            if(e.entity=="voice-pool")return "pool-config";
            if(n.Contains("memory"))return "memory";
            if(n.Contains("stream")||n.Contains("流式"))return "streaming";
            if(e.entity=="loudness")return "loudness";
            if(n.Contains("voice")||n.Contains("声部")||n.Contains("播放实例"))return "usage";
            return "technical-metrics";
        }
        var sections=new List<(string Id,string Title,bool Fold,WireEvent[][] Rows)>();
        foreach(var (id,title,fold) in new[]{("memory","内存",false),("streaming","Streaming",false),("usage","播放用量",false),("technical-metrics","技术性能指标",true),("loudness","响度",true),("pool-config","原生声池配置 · 待核验",true)})
        {
            var rows=groups.Where(g=>Section(g[^1])==id).ToArray();
            if(rows.Length==0)continue;
            var visibleRows=rows.Where(all=>!MetricPresentation.IsStreamingPoolCapacity(all[^1])
                ||!rows.Any(g=>g[^1].session==all[^1].session&&MetricPresentation.IsStreamingPoolUsed(g[^1]))).ToArray();
            sections.Add((id,title,fold,visibleRows));
        }
        bool CanExpandMetric(WireEvent[] all)
        {
            var last=all[^1];
            return !SupplementMetrics.Contains(last)&&!MetricPresentation.IsStreamingPoolUsed(last)
                &&last.entity!="voice-pool"&&all.Select(e=>e.time).Distinct().Take(2).Count()>1;
        }
        // Measure before painting. Clamping after painting caused a second invalidation at the scroll end.
        _contentHeight=76;
        foreach(var section in sections)
        {
            _contentHeight+=32;
            if(!section.Fold||_expanded.Contains(section.Id))
                foreach(var all in section.Rows)
                {
                    var last=all[^1];var key="metric:"+last.name+last.objectId;
                    _contentHeight+=CanExpandMetric(all)&&_expanded.Contains(key)?116:42;
                }
            _contentHeight+=12;
        }
        _vertical=Math.Clamp(_vertical,0,ResourcesMaxScroll);
        double y=76-_vertical;
        using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var (id,title,fold,rows) in sections)
        {
            bool open=!fold||_expanded.Contains(id);
            if(VisibleRow(y,32)) {
                c.FillRectangle(Palette.Panel,new Rect(8,y,Math.Max(0,Bounds.Width-16),32));
                Text(c,(fold?(open?"▾  ":"▸  "):"")+title,18,y+8,Palette.Text,12);
                if(fold)_expandHits.Add((new Rect(8,y,Math.Max(0,Bounds.Width-16),32),id));
            }
            y+=32;
            if(open)foreach(var all in rows)
            {
                var last=all[^1];
                bool pool=MetricPresentation.IsStreamingPoolUsed(last);
                var capacity=pool?groups.Select(g=>g[^1]).FirstOrDefault(e=>e.session==last.session&&MetricPresentation.IsStreamingPoolCapacity(e)):null;
                var key="metric:"+last.name+last.objectId;
                bool canExpand=CanExpandMetric(all);
                bool expanded=canExpand&&_expanded.Contains(key);
                double h=expanded?116:42;
                if(VisibleRow(y,h)) {
                    c.FillRectangle(Palette.Alternate,new Rect(18,y,Math.Max(0,Bounds.Width-36),h));
                    c.DrawLine(new Pen(Palette.Border,2),new Point(18,y),new Point(18,y+h));
                    Text(c,(canExpand?(expanded?"▾  ":"▸  "):"")+MetricName(last),30,y+10,Palette.Text,11);
                    var value=pool?MetricPresentation.StreamingPool(last,capacity):MetricPresentation.Value(last);
                    var valueWidth=Math.Max(0,Bounds.Width-LabelWidth-58);
                    using(c.PushClip(new Rect(LabelWidth+18,y+5,valueWidth,29)))
                        Text(c,Elide(value,valueWidth,15),LabelWidth+18,y+9,Palette.Text,15);
                    if(canExpand)_expandHits.Add((new Rect(20,y,LabelWidth,h),key));
                    _hits.Add((new Rect(20,y,Math.Max(0,Bounds.Width-40),h),last));
                    if(expanded)DrawSeries(c,all,y+31,75,Palette.Voice,false);
                }
                y+=h;
            }
            y+=12;
        }
    }
    private void DrawMixing(DrawingContext c)
    {
        var buses=MixingPresentation.Latest(Events,End);
        _busCount=buses.Length;_busChannels=buses.Sum(b=>b.Channels.Length);
        Text(c,"Bus 电平",16,19,Palette.Text,16);
        Text(c,BusHistoryIsSparse ? "事件采样 · 非连续电平" : "Peak / RMS · 返回槽位最大值",16,49,Palette.Muted,11);
        if(buses.Length==0){_contentHeight=0;_vertical=0;Empty(c,"Bus 电平尚未提供","确认来源已启用 Bus 监控。");return;}
        double contentHeight=buses.Sum(b=>42+(_expanded.Contains("bus:"+b.Event.objectId)?b.Channels.Length*40:0));
        _contentHeight=contentHeight;_vertical=Math.Clamp(_vertical,0,MixingMaxScroll);
        double y=86-_vertical;
        using var clip=c.PushClip(new Rect(0,75,Math.Max(0,Bounds.Width-18),MixingViewportHeight));
        foreach(var bus in buses)
        {
            var e=bus.Event;var key="bus:"+e.objectId;var expanded=_expanded.Contains(key);
            if(!VisibleRow(y,42+(expanded?bus.Channels.Length*40:0),75)){y+=42+(expanded?bus.Channels.Length*40:0);continue;}
            c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width-18,40));
            RowName(c,(expanded?"− ":"+ ")+e.name,y+3,Palette.Voice);
            Text(c,$"{bus.Channels.Length} 通道 · {Stamp(e.time)}",12,y+21,size:10);
            if(bus.Channels.Length==0)Text(c,"分声道数据未提供",LabelWidth+8,y+10);
            else DrawMeter(bus.MaxPeak,bus.MaxRms,y+2);
            if(y+40>75&&y<Bounds.Height-30)
            { _expandHits.Add((new Rect(0,y,LabelWidth,40),key));_hits.Add((new Rect(LabelWidth,y,Math.Max(0,Bounds.Width-LabelWidth-18),40),e)); }
            y+=42;
            if(!expanded)continue;
            foreach(var ch in bus.Channels)
            {
                if(!VisibleRow(y,40,75)){y+=40;continue;}
                Text(c,"↳ "+ch.Label,30,y+5,Palette.Muted,11);
                DrawMeter(ch.Peak,ch.Rms,y);
                if(y+38>75&&y<Bounds.Height-30)_hits.Add((new Rect(0,y,Math.Max(0,Bounds.Width-18),38),e));
                y+=40;
            }
        }

        void DrawMeter(double peakValue,double rmsValue,double top)
        {
            static double Db(double value)=>value>0?Math.Clamp(20*Math.Log10(value),-96,6):-96;
            var peak=Db(peakValue);var rms=Db(rmsValue);var width=Math.Max(40,PlotWidth-210);
            c.FillRectangle(Palette.Panel,new Rect(LabelWidth+8,top+8,width,9));
            c.FillRectangle(Palette.Voice,new Rect(LabelWidth+8,top+8,Math.Clamp((rms+96)/102,0,1)*width,9));
            var px=LabelWidth+8+Math.Clamp((peak+96)/102,0,1)*width;
            c.DrawLine(new Pen(peak>=0?Palette.Error:Palette.Request,2),new Point(px,top+4),new Point(px,top+21));
            var readout=$"P {(peakValue<=0?"−∞":peak.ToString("0.0"))} · R {(rmsValue<=0?"−∞":rms.ToString("0.0"))} dBFS";
            var readoutX=LabelWidth+width+18;
            var readoutWidth=Math.Max(0,Bounds.Width-readoutX-22);
            using(c.PushClip(new Rect(readoutX,top+3,readoutWidth,20)))
                Text(c,Elide(readout,readoutWidth,10),readoutX,top+6,size:10);
        }
    }
    private void DrawScrollHint(DrawingContext c)
    {
        c.FillRectangle(Palette.Panel,new Rect(0,Bounds.Height-30,Bounds.Width,30));
        Text(c,$"{_busCount} Bus · {_busChannels} 通道",16,Bounds.Height-21,Palette.Muted,11);
        if(MixingMaxScroll<=0)return;
        c.FillRectangle(Palette.Border,MixingScrollTrack);
        c.FillRectangle(Palette.Muted,MixingScrollThumb);
    }
    private static readonly Geometry EarIcon=Geometry.Parse(VisualLanguage.Path(IconKind.Listener));
    private static readonly Geometry SpeakerIcon=Geometry.Parse(VisualLanguage.Path(IconKind.Source));
    private void DrawLocations(DrawingContext c)
    {
        Text(c,"空间 · XZ 俯视",16,19,Palette.Text,16);
        DrawToggle(c,new Rect(16,48,108,28),"音源",ShowSources,"source",Palette.Voice);
        DrawToggle(c,new Rect(134,48,153,28),"衰减监听点",ShowDistanceListeners,"distance-listener",Palette.Listener);
        DrawToggle(c,new Rect(298,48,155,28),"基础监听器",ShowBaseListeners,"listener",Palette.Listener);
        var clusters=SpatialPresentation.Group(Events,End,ShowBaseListeners,ShowSources,ShowDistanceListeners);
        var positions=clusters.SelectMany(g=>g.Items).ToArray();
        if(positions.Length==0)
        {
            _contentHeight=0;
            Empty(c,!ShowSources&&!ShowDistanceListeners&&!ShowBaseListeners?"所有空间图层已隐藏":"所选图层暂无位置", "可切换上方图层查看。");return;
        }
        bool hasListener=clusters.Any(g=>g.Entity=="distance-listener");
        Text(c,ShowDistanceListeners&&!hasListener?"衰减监听点尚未收到完整参数":$"{positions.Count(e=>e.entity=="source")} 个音源 · {positions.Count(e=>e.entity!="source")} 个监听点",16,85,ShowDistanceListeners&&!hasListener?Palette.Request:Palette.Muted);
        var expanded=clusters.FirstOrDefault(g=>_expanded.Contains(g.Key));
        double overlayWidth=Math.Min(310,Math.Max(180,Bounds.Width-40));
        double overlayHeight=Math.Min(480,Math.Max(80,Bounds.Height-144));
        if(expanded!=null)
        {
            SpatialOverlayBounds=new Rect(Math.Max(20,Bounds.Width-overlayWidth-16),112,overlayWidth,overlayHeight);
            _spatialExpandedKey=expanded.Key;
        }
        double plotRight=Bounds.Width-30;
        double cx=(positions.Min(e=>e.x)+positions.Max(e=>e.x))/2,cz=(positions.Min(e=>e.z)+positions.Max(e=>e.z))/2;
        var extent=Math.Max(2,positions.Max(e=>Math.Max(Math.Abs(e.x-cx),Math.Abs(e.z-cz))))*1.25;
        if(positions.FirstOrDefault(e=>e.objectId==SpatialFocusId) is {} focus) {cx=focus.x;cz=focus.z;}
        var center=new Point((40+plotRight)/2,(Bounds.Height+100)/2);
        var scale=Math.Max(1,Math.Min(plotRight-100,Bounds.Height-190))/(2*extent);
        for(int i=-2;i<=2;i++){var x=center.X+i*extent*scale/2;var z=center.Y+i*extent*scale/2;c.DrawLine(new Pen(Palette.Border,.7),new Point(x,115),new Point(x,Bounds.Height-30));c.DrawLine(new Pen(Palette.Border,.7),new Point(40,z),new Point(plotRight,z));}
        Text(c,"+Z ↑",44,115,size:10);Text(c,"+X →",Math.Max(44,plotRight-42),Bounds.Height-50,size:10);
        Text(c,$"每格 {extent/2:0.##} 坐标单位",44,Bounds.Height-50,size:10);
        var labels=new List<Rect>();
        if(expanded!=null)labels.Add(SpatialOverlayBounds.Inflate(4));
        var anchors=new List<(Point point,bool listener,SpatialCluster cluster)>();
        // Reserve listener labels first, then fit sources around them. Markers and popup have separate layers.
        foreach(var cluster in clusters.OrderBy(g=>g.Entity=="source"?1:0))DrawCluster(cluster);
        foreach(var marker in anchors.OrderBy(a=>a.listener?1:0))
        {
            var brush=marker.listener?Palette.Listener:Palette.Voice;
            c.DrawEllipse(Palette.Canvas,new Pen(brush,1.4),marker.point,4,4);
            if(marker.listener)DrawSpatialIcon(c,new Point(marker.point.X,marker.point.Y-14),true);
            if(Selected is {} selected&&marker.cluster.Items.Any(e=>e.entity==selected.entity&&e.objectId==selected.objectId))c.DrawEllipse(null,new Pen(Palette.Selection,2),marker.point,9,9);
            var hit=new Rect(marker.point.X-10,marker.point.Y-(marker.listener?26:10),20,marker.listener?36:20);
            if(marker.cluster.Items.Length==1)_hits.Add((hit,marker.cluster.Items[0]));
            _spatialHits.Add((hit,marker.cluster.Items.Length==1?marker.cluster.Items[0]:null,marker.cluster.Items.Length>1?marker.cluster.Key:null));
        }
        if(clusters.Any(cluster=>cluster.Items.Length>1))Text(c,"同点对象可展开",16,Bounds.Height-21,size:10);
        _contentHeight=0;
        if(expanded!=null)DrawExpanded(expanded);

        void DrawCluster(SpatialCluster cluster)
        {
            var e=cluster.Items[0];bool listener=cluster.Entity!="source";
            var anchor=new Point(center.X+(e.x-cx)*scale,center.Y-(e.z-cz)*scale);
            anchors.Add((anchor,listener,cluster));
            double labelWidth=Math.Min(250,Math.Max(120,plotRight-52));
            var area=new Rect(26,112,Math.Max(labelWidth,plotRight-42),Math.Max(46,Bounds.Height-166));
            var label=SpatialPresentation.PlaceLabel(anchor,labelWidth,area,labels);
            // In crowded scenes keep the measured marker available instead of drawing overlapping full labels.
            if(label==null)return;
            labels.Add(label.Value);
            double left=label.Value.X,top=label.Value.Y;
            var brush=listener?Palette.Listener:Palette.Voice;
            c.DrawLine(new Pen(brush,.8),anchor,new Point(left,top+23));
            c.FillRectangle(Palette.Panel,label.Value);
            if(Selected is {} selected && cluster.Items.Any(item=>item.objectId==selected.objectId && item.entity==selected.entity)) Highlight(c,label.Value);
            DrawSpatialIcon(c,new Point(left+16,top+23),listener);
            using(var labelClip=c.PushClip(new Rect(left+32,top+3,labelWidth-38,40)))
            {
                Text(c,cluster.Items.Length>1?$"{cluster.Items.Length} 个{(listener?"监听器":"音源")} · 展开列表":e.name,left+34,top+5,brush);
                Text(c,$"X {e.x:0.##} · Y {e.y:0.##} · Z {e.z:0.##}",left+34,top+25,size:10);
            }
            if(cluster.Items.Length>1)_spatialHits.Add((label.Value,null,cluster.Key));
            else {_hits.Add((label.Value,e));_spatialHits.Add((label.Value,e,null));}
        }
        void DrawExpanded(SpatialCluster cluster)
        {
            var panel=SpatialOverlayBounds;
            double left=panel.X,top=panel.Y,width=panel.Width;
            c.FillRectangle(Palette.Canvas,new Rect(left-4,top-4,width+8,panel.Height+8));
            c.DrawRectangle(Palette.Panel,new Pen(Palette.Border),panel,5,5);
            Text(c,$"{(cluster.Entity!="source"?"监听器":"音源")}列表 · {cluster.Items.Length} 个",left+12,top+10,Palette.Text,13);
            _spatialClose=new Rect(panel.Right-36,top+4,30,30);
            c.DrawLine(new Pen(Palette.Text,1.4),new Point(panel.Right-26,top+14),new Point(panel.Right-16,top+24));
            c.DrawLine(new Pen(Palette.Text,1.4),new Point(panel.Right-16,top+14),new Point(panel.Right-26,top+24));
            _spatialListViewport=new Rect(left+1,top+38,width-14,Math.Max(1,panel.Height-44));
            SpatialOverlayMaxScroll=Math.Max(0,cluster.Items.Length*34-_spatialListViewport.Height);
            _vertical=Math.Clamp(_vertical,0,SpatialOverlayMaxScroll);
            using(var clip=c.PushClip(_spatialListViewport))
            for(int i=0;i<cluster.Items.Length;i++)
            {
                var item=cluster.Items[i];var y=_spatialListViewport.Top+i*34-_vertical;
                if(y+34<=_spatialListViewport.Top||y>=_spatialListViewport.Bottom)continue;
                if(i%2==0)c.FillRectangle(Palette.Alternate,new Rect(left+1,y,width-14,34));
                if(Selected?.objectId==item.objectId&&Selected.entity==item.entity)HighlightRow(c,new Rect(left+1,y+1,width-14,32));
                Text(c,item.name,left+12,y+3,Palette.Text,11);
                Text(c,$"对象 {i+1} · X {item.x:0.##} · Y {item.y:0.##} · Z {item.z:0.##}",left+12,y+19,size:9);
                var hitTop=Math.Max(y,_spatialListViewport.Top);var hitBottom=Math.Min(y+34,_spatialListViewport.Bottom);
                if(hitBottom>hitTop)_spatialListHits.Add((new Rect(left+1,hitTop,width-14,hitBottom-hitTop),item));
            }
            if(SpatialOverlayMaxScroll>0)
            {
                _spatialScrollTrack=new Rect(panel.Right-12,_spatialListViewport.Top,10,_spatialListViewport.Height);
                var thumbHeight=Math.Max(24,_spatialListViewport.Height*_spatialListViewport.Height/(cluster.Items.Length*34));
                _spatialScrollThumb=new Rect(_spatialScrollTrack.X,_spatialScrollTrack.Y+_vertical/SpatialOverlayMaxScroll*(_spatialScrollTrack.Height-thumbHeight),10,thumbHeight);
                c.FillRectangle(Palette.Border,_spatialScrollTrack);c.FillRectangle(Palette.Muted,_spatialScrollThumb);
            }
        }
    }
    private void DrawSpatialIcon(DrawingContext c,Point point,bool listener)
    {
        using var transform=c.PushTransform(Matrix.CreateScale(.78,.78)*Matrix.CreateTranslation(point.X-11,point.Y-11));
        c.DrawGeometry(null,new Pen(listener?Palette.Listener:Palette.Voice,1.8),listener?EarIcon:SpeakerIcon);
    }

}
