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
    public WireEvent[] Discontinuities { get; set; } = [];
    public WireEvent[] SupplementMetrics { get; set; } = [];
    public WireEvent? Selected { get; set; }
    public double End { get; set; } = 30;
    public double Span { get; set; } = 30;
    public bool Live { get; set; } = true;
    public bool SourceConnected { get; set; } = true;
    public double TimeOrigin { get; set; }
    public HashSet<string> ControlKinds { get; } = new(ControlPresentation.Kinds, StringComparer.Ordinal);
    public bool ShowSources { get; set; } = true;
    public bool ShowDistanceListeners { get; set; } = true;
    public bool ShowBaseListeners { get; set; }
    public string Stamp(double time) => TimeLabel(time-TimeOrigin);
    public void SetControlKind(string kind, bool enabled)
    { if(!ControlPresentation.Kinds.Contains(kind))throw new ArgumentException("未知控制类型",nameof(kind));if(enabled)ControlKinds.Add(kind);else ControlKinds.Remove(kind);_vertical=0;ViewChanged?.Invoke();InvalidateVisual(); }
    public void SetSpatialLayer(string layer, bool enabled)
    {
        switch(layer){case "source":ShowSources=enabled;break;case "distance-listener":ShowDistanceListeners=enabled;break;case "listener":ShowBaseListeners=enabled;break;default:throw new ArgumentException("未知空间图层",nameof(layer));}
        _vertical=0;ViewChanged?.Invoke();InvalidateVisual();
    }
    public string Mode { get; set; } = "Timeline";
    public event Action<WireEvent>? EventSelected;
    public event Action? SelectionCleared;
    public event Action? ViewChanged;
    public double? SelectionStart { get; private set; }
    public double? SelectionEnd { get; private set; }
    public void SetSelection(double? start, double? end) { SelectionStart=start; SelectionEnd=end; InvalidateVisual(); }
    private Point? _drag;
    private double _dragEnd, _vertical, _contentHeight;
    private bool _panning, _space, _scrollDragging;
    private double _scrollGrab;
    private int _busCount, _busChannels;
    private readonly List<(Rect rect, WireEvent item)> _hits = [];
    private readonly List<(Rect rect, string key)> _expandHits = [];
    private readonly HashSet<string> _expanded = [];
    private readonly List<(Rect rect, string key)> _toggleHits = [];
    private readonly List<(Rect rect, WireEvent? item, string? key)> _spatialHits = [];
    private WireEvent[]? _groupEvents, _groupGaps, _controlEvents;
    private double _groupEnd=double.NaN, _controlEnd=double.NaN;
    private int _controlMask;
    private PlaybackGroup[] _playbackGroups=[];
    private ControlRow[] _controlRows=[];
    private PlaybackGroup[] PlaybackGroups()
    {
        if(!ReferenceEquals(_groupEvents,Events)||!ReferenceEquals(_groupGaps,Discontinuities)||_groupEnd!=End)
        { _groupEvents=Events;_groupGaps=Discontinuities;_groupEnd=End;_playbackGroups=PlaybackPresentation.Group(Events.Concat(Discontinuities),End); }
        return _playbackGroups;
    }
    private ControlRow[] ControlRows()
    {
        var mask=0;for(int i=0;i<ControlPresentation.Kinds.Length;i++)if(ControlKinds.Contains(ControlPresentation.Kinds[i]))mask|=1<<i;
        if(!ReferenceEquals(_controlEvents,Events)||_controlEnd!=End||_controlMask!=mask)
        { _controlEvents=Events;_controlEnd=End;_controlMask=mask;_controlRows=ControlPresentation.Group(Events,End,ControlKinds); }
        return _controlRows;
    }
    private const double LabelWidth = 214;
    private double PlotWidth => Math.Max(40, Bounds.Width-LabelWidth-22);
    private double MixingViewportHeight => Math.Max(1,Bounds.Height-106);
    private double MixingMaxScroll => Math.Max(0,_contentHeight+10-MixingViewportHeight);
    private Rect MixingScrollTrack => new(Bounds.Width-18,76,18,MixingViewportHeight);
    private Rect MixingScrollThumb
    {
        get { var track=MixingScrollTrack;var thumb=Math.Max(28,track.Height*track.Height/Math.Max(track.Height,_contentHeight+10));return new Rect(Bounds.Width-16,track.Y+(MixingMaxScroll<=0?0:_vertical/MixingMaxScroll)*(track.Height-thumb),14,thumb); }
    }
    public double Start => End-Span;
    private double X(double time) => LabelWidth+(time-Start)/Span*PlotWidth;
    private static readonly Typeface Font = new("Segoe UI, Microsoft YaHei UI");
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
        if(key.StartsWith("spatial:",StringComparison.Ordinal)){foreach(var old in _expanded.Where(k=>k.StartsWith("spatial:",StringComparison.Ordinal)).ToArray())_expanded.Remove(old);_vertical=0;}
        if(had)_expanded.Remove(key);else _expanded.Add(key);InvalidateVisual();
    }

    public TimelineControl()
    {
        ClipToBounds=true; Focusable=true; MinHeight=220;
        PointerWheelChanged += (_,e) => {
            if(e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                var f=Math.Clamp((e.GetPosition(this).X-LabelWidth)/PlotWidth,0,1); var anchor=Start+f*Span;
                Span=Math.Clamp(Span*(e.Delta.Y>0?.8:1.25),.25,86400); if(!Live)End=anchor+Span*(1-f);
            } else if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){End-=e.Delta.Y*Span*.1;Live=false;}
            else _vertical=Math.Clamp(_vertical-e.Delta.Y*36,0,Mode=="Mixing"?MixingMaxScroll:Math.Max(0,_contentHeight+100-Bounds.Height));
            if(Mode!="Mixing"||e.KeyModifiers.HasFlag(KeyModifiers.Control)||e.KeyModifiers.HasFlag(KeyModifiers.Shift))ViewChanged?.Invoke();InvalidateVisual();e.Handled=true;
        };
        PointerPressed += (_,e) => {
            Focus();var p=e.GetPosition(this);
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
            if(_scrollDragging){SetMixingScrollFromPointer(p.Y);e.Handled=true;return;}
            if(_drag is not {} start){var hit=_hits.LastOrDefault(h=>h.rect.Contains(p));ToolTip.SetTip(this,hit.item==null?null:$"{hit.item.name}\n{Stamp(hit.item.time)} · {hit.item.kind}\n{hit.item.detail}");return;}
            var delta=p.X-start.X;if(Math.Abs(delta)<8)return;
            if(_panning){End=_dragEnd-delta/PlotWidth*Span;Live=false;}
            else{SelectionStart=Start+Math.Clamp((start.X-LabelWidth)/PlotWidth,0,1)*Span;SelectionEnd=Start+Math.Clamp((p.X-LabelWidth)/PlotWidth,0,1)*Span;}
            ViewChanged?.Invoke();InvalidateVisual();
        };
        PointerReleased += (_,e)=>{_drag=null;_scrollDragging=false;e.Pointer.Capture(null);};
        KeyDown += (_,e)=>{if(e.Key==Key.Space){_space=true;e.Handled=true;}};
        KeyUp += (_,e)=>{if(e.Key==Key.Space){_space=false;e.Handled=true;}};
        LostFocus += (_,_)=>_space=false;
    }
    private void SetMixingScrollFromPointer(double y)
    { var track=MixingScrollTrack;var thumb=MixingScrollThumb;_vertical=Math.Clamp((y-_scrollGrab-track.Y)/Math.Max(1,track.Height-thumb.Height),0,1)*MixingMaxScroll;InvalidateVisual(); }
    private void Text(DrawingContext c,string text,double x,double y,IBrush? brush=null,double size=11)
        =>c.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Font,size,brush??Palette.Muted),new Point(x,y));
    private void RowName(DrawingContext c,string name,double y,IBrush? brush=null)
    {using var clip=c.PushClip(new Rect(12,y,LabelWidth-24,23));Text(c,name,12,y,brush??Palette.Text,12);}
    private void Empty(DrawingContext c,string title,string description)
    {Text(c,title,26,105,Palette.Text,18);Text(c,description,26,141,size:12);}
    public override void Render(DrawingContext c)
    {
        base.Render(c);c.FillRectangle(Palette.Canvas,new Rect(Bounds.Size));_hits.Clear();_expandHits.Clear();_toggleHits.Clear();_spatialHits.Clear();
        if(Events.Length==0&&SupplementMetrics.Length==0){Empty(c,"等待音频观测","在游戏里开启 CriScope 采集，或打开已有日志。");return;}
        if(Mode=="Location"){DrawLocations(c);return;}if(Mode=="Mixing"){DrawMixing(c);DrawScrollHint(c);return;}
        double step=Math.Pow(10,Math.Floor(Math.Log10(Span/8)));if(Span/step>16)step*=5;else if(Span/step>10)step*=2;
        for(double t=TimeOrigin+Math.Ceiling((Start-TimeOrigin)/step)*step;t<=End;t+=step){var x=X(t);c.DrawLine(new Pen(Palette.Border,.5),new Point(x,31),new Point(x,Bounds.Height-28));Text(c,Stamp(t),x+4,10,size:10);}
        Text(c,"距本次采集开始",12,10,size:10);c.DrawLine(new Pen(Palette.Border),new Point(0,34),new Point(Bounds.Width,34));
        if(Mode=="AISAC")DrawControls(c);else if(Mode=="Performance")DrawResources(c);else DrawTracks(c);
        if(SelectionStart is {} a&&SelectionEnd is {} b&&Math.Abs(a-b)>.000001){var l=Math.Clamp(X(Math.Min(a,b)),LabelWidth,LabelWidth+PlotWidth);var r=Math.Clamp(X(Math.Max(a,b)),LabelWidth,LabelWidth+PlotWidth);c.DrawRectangle(null,new Pen(Palette.Selection,1.5),new Rect(l,35,Math.Max(0,r-l),Math.Max(0,Bounds.Height-64)));}
        if(Selected is {} selected&&selected.time>=Start&&selected.time<=End)c.DrawLine(new Pen(Palette.Selection,1),new Point(X(selected.time),35),new Point(X(selected.time),Bounds.Height-28));
        c.FillRectangle(Palette.Canvas,new Rect(0,Bounds.Height-28,Bounds.Width,28));Text(c,"Ctrl 滚轮缩放 · 中键 / 空格拖动浏览历史 · Shift 拖动选区",12,Bounds.Height-21,size:10);
        if(Live)Text(c,SourceConnected?"实时":"已断开",Bounds.Width-64,Bounds.Height-21,SourceConnected?Palette.Good:Palette.Request,10);
    }
    private void DrawTracks(DrawingContext c)
    {
        Text(c,"● Voice 播放区间",14,45,Palette.Selection);
        Text(c,"◇ 播放请求 · 展开查看 Voice",158,45,Palette.Voice);
        Text(c,"声音结束后仍保留在所选历史范围",385,45);
        var groups=PlaybackGroups();
        double y=75-_vertical;int row=0;
        using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var group in groups)
        {
            if(group.End is {} ended&&ended.time<Start)continue;
            var anchor=group.Request??group.Voices.SelectMany(v=>v).FirstOrDefault()??group.End;
            if(anchor==null)continue;
            var key="playback:"+group.Id;bool expanded=_expanded.Contains(key);
            var voiceRows=expanded?group.Voices.Where(v=>v.LastOrDefault(e=>e.kind=="stop") is not {} stop||stop.time>=Start).ToArray():[];
            double h=54+(expanded?Math.Max(1,voiceRows.Length)*38:0);
            if(!VisibleRow(y,h)){y+=h;row+=1+(expanded?Math.Max(1,voiceRows.Length):0);continue;}
            if(row++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,54));
            RowName(c,(expanded?"− ":"+ ")+group.Name,y+3);
            var status=!SourceConnected&&group.ActiveVoiceCount>0?"已断开 · 保留最后状态":group.StatusLabel;
            using(c.PushClip(new Rect(27,y+18,LabelWidth-37,35)))
            {
                Text(c,status+" · "+group.Voices.Length+" Voice",27,y+20,group.ActiveVoiceCount>0&&SourceConnected?Palette.Good:Palette.Muted,10);
                var duration=group.DurationAt(End);
                var durationText=duration is {} elapsed?(group.EndedAt!=null?"持续 ":"已持续 ")+elapsed.ToString("0.000",CultureInfo.InvariantCulture)+" 秒":"历时未能完整确认";
                Text(c,durationText,27,y+37,size:9);
            }
            var beginLabel=group.StartedAt is {} began?"+"+Stamp(began):"开始时间未记录";
            var endedLabel=group.EndedAt??group.InstanceEndedAt;
            using(c.PushClip(new Rect(LabelWidth+8,y+1,Math.Max(0,PlotWidth-16),16)))Text(c,beginLabel+(endedLabel is {} finished?" → +"+Stamp(finished):" → "+status),LabelWidth+10,y+3,size:9);
            _expandHits.Add((new Rect(0,y,24,54),key));_hits.Add((new Rect(24,y,LabelWidth-24,54),anchor));
            // A Cue request is a point observation, not evidence of continuous voice allocation.
            if(group.Request is {} request&&request.time>=Start&&request.time<=End)
            {
                var px=X(request.time);var py=y+28;var pen=new Pen(Palette.Request,1.5);
                c.DrawLine(pen,new Point(px,py-6),new Point(px+5,py));c.DrawLine(pen,new Point(px+5,py),new Point(px,py+6));
                c.DrawLine(pen,new Point(px,py+6),new Point(px-5,py));c.DrawLine(pen,new Point(px-5,py),new Point(px,py-6));
                _hits.Add((new Rect(px-6,y,12,54),request));
            }
            foreach(var interval in group.VoiceIntervals)
                DrawInterval(interval.Begin,interval.End,group.End,y,54,Palette.Selection,
                    interval.Begin.kind!="play"||PlaybackPresentation.IsUnknownStart(interval.Begin));
            y+=54;
            if(!expanded)continue;
            foreach(var voice in voiceRows)
            {
                var begin=voice.FirstOrDefault(e=>e.kind=="play")??voice[0];var stop=voice.LastOrDefault(e=>e.kind=="stop");
                if(!VisibleRow(y,38)){y+=38;row++;continue;}
                if(row++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,38));
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
        if(row==0)Empty(c,"当前范围没有播放实例","每次 Cue 播放独立成行；静音 Voice 同样保留。可调整时间范围查看历史。");

        void DrawInterval(WireEvent begin,WireEvent? voiceStop,WireEvent? instanceEnd,double top,double height,IBrush brush,bool unknown)
        {
            if(!VisibleRow(top,height))return;
            var stop=voiceStop;
            if(instanceEnd!=null&&(stop==null||instanceEnd.time<stop.time))stop=instanceEnd;
            var end=Math.Min(stop?.time??End,End);
            var gap=Discontinuities.Where(d=>d.time>begin.time&&d.time<end).MinBy(d=>d.time);
            if(gap!=null){end=gap.time;stop=null;}
            var left=Math.Max(LabelWidth,X(begin.time));var right=Math.Min(LabelWidth+PlotWidth,X(end));
            if(right<left)return;
            var center=top+height/2;c.FillRectangle(brush,new Rect(left,center-4,Math.Max(2,right-left),8));
            if(unknown){Text(c,"?",left+3,top+1,Palette.Request,13);c.DrawLine(new Pen(Palette.Canvas,2),new Point(left+4,center-5),new Point(left+9,center+5));}
            else if(begin.time>=Start)c.DrawLine(new Pen(brush,2),new Point(left,center-9),new Point(left,center+9));
            if(stop!=null)c.DrawLine(new Pen(Palette.Error,2),new Point(right,center-9),new Point(right,center+9));
            else c.DrawEllipse(Palette.Canvas,new Pen(brush,1.5),new Point(right,center),4,4);
            _hits.Add((new Rect(left,top,Math.Max(8,right-left),height),begin));
            if(stop!=null)_hits.Add((new Rect(right-5,top,10,height),stop));
        }
    }

    private void DrawControls(DrawingContext c)
    {
        double chipX=12,chipY=40;
        foreach(var kind in ControlPresentation.Kinds)
        {
            var width=kind=="sequence"?112:kind=="selector"?104:kind=="aisac"?90:82;
            if(chipX+width>Bounds.Width-12){chipX=12;chipY+=31;}
            DrawToggle(c,new Rect(chipX,chipY,width,25),ControlPresentation.KindLabel(kind),ControlKinds.Contains(kind),"control:"+kind,kind=="aisac"?Palette.Selection:kind is "beat" or "sequence"?Palette.Good:Palette.Request);
            chipX+=width+7;
        }
        if(chipX+215<Bounds.Width)Text(c,"可多选 · 展开看记录 · ○ SDK 约时",chipX+8,chipY+6,size:10);
        var groups=ControlRows();
        if(groups.Length==0){_contentHeight=0;Empty(c,ControlKinds.Count==0?"尚未选择控制类型":"当前筛选没有记录",ControlKinds.Count==0?"点击上方分类，可同时查看多种控制。":"显示采集到的设置和事件；连接之前的值不会补造。");return;}
        var contentTop=chipY+33;
        double y=contentTop-_vertical;int i=0;
        using var clip=c.PushClip(new Rect(0,contentTop-5,Bounds.Width,Math.Max(0,Bounds.Height-contentTop-23)));
        foreach(var row in groups)
        {
            var last=row.Latest;var key=row.Key;bool expanded=_expanded.Contains(key);
            var samples=row.Records.Where(e=>e.time>=Start).ToArray();
            var shown=expanded?(samples.Length==0?new[]{last}:samples):[];
            bool compact=PlotWidth<400;
            var rowHeight=compact?66d:49d;var recordsTop=rowHeight+27;
            double h=expanded?recordsTop+shown.Length*28:rowHeight;
            if(!VisibleRow(y,h,contentTop-5)){y+=h;i++;continue;}
            var brush=last.kind=="aisac"?Palette.Selection:last.kind is "beat" or "sequence"?Palette.Good:Palette.Request;
            if(i++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,h));
            RowName(c,(expanded?"− ":"+ ")+row.Name,y+4,brush);
            Text(c,row.TargetLabel+" · "+row.Records.Length+" 条记录",12,y+26,size:10);
            using(c.PushClip(new Rect(LabelWidth+8,y+1,Math.Max(0,compact?PlotWidth-16:PlotWidth-220),25)))
                Text(c,(last.kind is "aisac" or "selector"?"最近设置：":"最近事件：")+ControlPresentation.Value(last),LabelWidth+10,y+5,Palette.Text,12);
            Text(c,(last.estimatedTime?"SDK 约于 +":"于 +")+Stamp(last.time),compact?LabelWidth+10:Math.Max(LabelWidth+180,Bounds.Width-202),y+(compact?25:6),size:10);
            _expandHits.Add((new Rect(0,y,24,rowHeight),key));
            _hits.Add((new Rect(24,y,LabelWidth-24,rowHeight),last));
            // Writes are discrete observations. No curve is inferred between independent settings.
            foreach(var e in samples)
            {var x=X(e.time);c.DrawEllipse(e.estimatedTime?null:brush,e.estimatedTime?new Pen(brush,1.3):null,new Point(x,y+rowHeight-14),3,3);_hits.Add((new Rect(x-5,y+rowHeight-24,10,22),e));}
            if(samples.Length==0)Text(c,"当前范围无新记录 · 上方保留最近一次观测",LabelWidth+10,y+rowHeight-20,size:10);
            if(expanded)
            {
                Text(c,samples.Length==0?"最近一次记录在当前时间范围之前":samples.Length==1?"当前范围只有一次记录":$"当前范围 {samples.Length} 次记录 · 按发生顺序",LabelWidth+10,y+rowHeight+4,Palette.Muted,10);
                for(int n=0;n<shown.Length;n++)
                {
                    var e=shown[n];var top=y+recordsTop+n*28;
                    if(!VisibleRow(top,28,contentTop-5))continue;
                    Text(c,(e.estimatedTime?"约 +":"+")+Stamp(e.time),28,top+5,size:10);
                    using(c.PushClip(new Rect(LabelWidth+8,top,Math.Max(0,PlotWidth-10),28)))Text(c,ControlPresentation.Value(e),LabelWidth+10,top+4,brush,11);
                    _hits.Add((new Rect(24,top,Math.Max(0,Bounds.Width-32),28),e));
                }
            }
            y+=h;
        }
        _contentHeight=y+_vertical-contentTop;
    }
    private void DrawToggle(DrawingContext c,Rect rect,string label,bool enabled,string key,IBrush brush)
    {
        c.FillRectangle(enabled?Palette.Panel:Palette.Canvas,rect);
        c.DrawRectangle(null,new Pen(enabled?brush:Palette.Border,enabled?1.3:1),rect,4,4);
        Text(c,(enabled?"✓ ":"  ")+label,rect.X+9,rect.Y+5,enabled?brush:Palette.Muted,11);
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
        Text(c,"资源用量 · 名称展开趋势 · SDK 补充仅展示最新值",14,45,Palette.Good);
        var allGroups=Events.Where(e=>e.kind=="metric"&&e.time<=End&&double.IsFinite(e.value)).GroupBy(e=>e.objectId+"/"+e.name).Select(g=>g.OrderBy(e=>e.time).ToArray()).Concat(SupplementMetrics.Where(e=>double.IsFinite(e.value)).Select(e=>new[]{e})).OrderBy(g=>(g[^1].name+g[^1].objectId).Contains("memory",StringComparison.OrdinalIgnoreCase)?0:(g[^1].name+g[^1].objectId).Contains("stream",StringComparison.OrdinalIgnoreCase)?1:2).ToArray();
        bool Primary(WireEvent e) { var n=(e.name+e.objectId).ToLowerInvariant(); return n.Contains("memory")||n.Contains("stream")||n.Contains("voice")||n.Contains("声部"); }
        var groups=allGroups.Where(g=>Primary(g[^1])||_expanded.Contains("technical-metrics")).ToArray();
        if(allGroups.Length==0){Empty(c,"资源指标尚未提供","原生会话提供可观测指标；Atom / FS 内存需可选 SDK 扩展。");return;}double y=76-_vertical;int i=0;
        using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var all in groups)
        {
            var last=all[^1];
            if(MetricPresentation.IsStreamingPoolCapacity(last) && groups.Any(g=>g[^1].session==last.session && MetricPresentation.IsStreamingPoolUsed(g[^1])))continue;
            var pool=MetricPresentation.IsStreamingPoolUsed(last);
            var capacity=pool?groups.Select(g=>g[^1]).FirstOrDefault(e=>e.session==last.session && MetricPresentation.IsStreamingPoolCapacity(e)):null;
            var supplement=SupplementMetrics.Contains(last);
            var key="metric:"+last.name+last.objectId;
            bool canExpand=!supplement&&!pool;
            bool expanded=canExpand&&_expanded.Contains(key);
            double h=expanded?116:48;
            if(!VisibleRow(y,h)){y+=h;i++;continue;}
            if(i++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,h));
            RowName(c,(canExpand?(expanded?"− ":"+ "):"")+MetricName(last),y+6);
            Text(c,pool?MetricPresentation.StreamingPool(last,capacity):MetricPresentation.Value(last),LabelWidth+14,y+7,Palette.Good,18);
            Text(c,pool?"SDK · StandardStreaming 池":supplement?"SDK 补充 · 独立时钟":"最后观测 "+Stamp(last.time),LabelWidth+205,y+12,size:10);
            if(canExpand)_expandHits.Add((new Rect(0,y,LabelWidth,h),key));
            _hits.Add((new Rect(0,y,Bounds.Width,h),last));
            if(expanded)DrawSeries(c,all,y+31,75,Palette.Good,false);
            y+=h;
        }if(!groups.Any(g=>(g[^1].name+g[^1].objectId).Contains("memory",StringComparison.OrdinalIgnoreCase)))Text(c,"Atom / FS 内存：此来源尚未提供",16,y+15,size:12);RowName(c,(_expanded.Contains("technical-metrics")?"− ":"+ ")+"技术性能指标",y+42);_expandHits.Add((new Rect(0,y+36,LabelWidth,36),"technical-metrics"));_contentHeight=y+_vertical+42;
    }
    private void DrawMixing(DrawingContext c)
    {
        var buses=MixingPresentation.Latest(Events,End);
        _busCount=buses.Length;_busChannels=buses.Sum(b=>b.Channels.Length);
        Text(c,"混音 / BUS METER",22,20,Palette.Text,16);
        Text(c,"每 Bus 一行 · Peak / RMS：分别取返回槽位最大值（概览，非混合信号）",22,50,Palette.Voice,11);
        if(buses.Length==0){_contentHeight=0;_vertical=0;Empty(c,"Bus 电平尚未提供","未观察到的路由与效果参数不会补造。请确认来源已启用相应监控。");return;}
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
            Text(c,$"{bus.Channels.Length} 个返回槽位 · {Stamp(e.time)}",12,y+21,size:9);
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
            Text(c,$"P {(peakValue<=0?"−∞":peak.ToString("0.0"))} · R {(rmsValue<=0?"−∞":rms.ToString("0.0"))} dBFS",LabelWidth+width+18,top+6,size:10);
        }
    }
    private void DrawScrollHint(DrawingContext c)
    {
        c.FillRectangle(Palette.Panel,new Rect(0,Bounds.Height-30,Bounds.Width,30));
        Text(c,$"↕ 滚轮 / 拖动右侧滚动条 · 已观测 {_busCount} 个 Bus / {_busChannels} 个返回通道槽位",18,Bounds.Height-23,Palette.Text,12);
        if(MixingMaxScroll<=0)return;
        c.FillRectangle(Palette.Border,MixingScrollTrack);
        c.FillRectangle(Palette.Muted,MixingScrollThumb);
    }
    private static readonly Geometry EarIcon=Geometry.Parse("M17,18 C17,22 14,25 10,23 C7,21 11,18 8,15 C4,11 6,3 12,2 C21,0 25,10 19,15 M11,14 C8,10 11,6 15,7 C19,8 17,12 14,13 L13,18");
    private static readonly Geometry SpeakerIcon=Geometry.Parse("M3,9 H8 L14,4 V22 L8,17 H3 Z M18,8 Q24,13 18,18 M21,4 Q31,13 21,22");
    private void DrawLocations(DrawingContext c)
    {
        Text(c,"空间 / XZ 俯视",22,20,Palette.Text,16);
        DrawToggle(c,new Rect(22,48,108,28),"音源",ShowSources,"source",Palette.Voice);
        DrawToggle(c,new Rect(140,48,153,28),"衰减监听点",ShowDistanceListeners,"distance-listener",Palette.Listener);
        DrawToggle(c,new Rect(304,48,155,28),"基础监听器",ShowBaseListeners,"listener",Palette.Listener);
        var clusters=SpatialPresentation.Group(Events,End,ShowBaseListeners,ShowSources,ShowDistanceListeners);
        var positions=clusters.SelectMany(g=>g.Items).ToArray();
        if(positions.Length==0)
        {
            _contentHeight=0;
            Empty(c,!ShowSources&&!ShowDistanceListeners&&!ShowBaseListeners?"所有空间图层已隐藏":"所选图层暂无位置", "使用上方开关切换图层；尚未收到的位置不会被设为原点。");return;
        }
        bool hasListener=clusters.Any(g=>g.Entity=="distance-listener");
        Text(c,ShowDistanceListeners&&!hasListener?"衰减监听点尚未收到完整参数":$"{positions.Count(e=>e.entity=="source")} 个音源 · {positions.Count(e=>e.entity!="source")} 个监听点 · 监听点始终置顶",22,85,ShowDistanceListeners&&!hasListener?Palette.Request:Palette.Muted);
        double cx=(positions.Min(e=>e.x)+positions.Max(e=>e.x))/2,cz=(positions.Min(e=>e.z)+positions.Max(e=>e.z))/2;
        var extent=Math.Max(2,positions.Max(e=>Math.Max(Math.Abs(e.x-cx),Math.Abs(e.z-cz))))*1.25;
        var center=new Point(Bounds.Width/2,(Bounds.Height+100)/2);
        var scale=Math.Max(1,Math.Min(Bounds.Width-200,Bounds.Height-190))/(2*extent);
        for(int i=-2;i<=2;i++){var x=center.X+i*extent*scale/2;var z=center.Y+i*extent*scale/2;c.DrawLine(new Pen(Palette.Border,.7),new Point(x,115),new Point(x,Bounds.Height-30));c.DrawLine(new Pen(Palette.Border,.7),new Point(40,z),new Point(Bounds.Width-30,z));}
        var expanded=clusters.FirstOrDefault(g=>_expanded.Contains(g.Key));
        _contentHeight=expanded==null?0:expanded.Items.Length*34+40;
        // Annotation rendering and hit ordering use the same layers; a newer source cannot cover a listener.
        foreach(var cluster in clusters.Where(g=>g.Entity=="source"))DrawCluster(cluster);
        if(expanded?.Entity=="source")DrawExpanded(expanded);
        foreach(var cluster in clusters.Where(g=>g.Entity!="source"))DrawCluster(cluster);
        if(expanded!=null&&expanded.Entity!="source")DrawExpanded(expanded);
        Text(c,"耳朵 = 监听点 · 扬声器 = 音源 · 同点对象按类型分开聚合",22,Bounds.Height-23,size:11);

        void DrawCluster(SpatialCluster cluster)
        {
            var e=cluster.Items[0];bool listener=cluster.Entity!="source";
            var peers=clusters.Where(other=>Math.Abs(other.Items[0].x-e.x)<.001&&Math.Abs(other.Items[0].z-e.z)<.001)
                .OrderBy(other=>other.Entity=="distance-listener"?0:other.Entity=="listener"?1:2).ToArray();
            var anchor=new Point(center.X+(e.x-cx)*scale,center.Y-(e.z-cz)*scale);
            // Marker stays at the measured coordinate; only the annotation is displaced.
            double labelWidth=Math.Min(270,Bounds.Width-52),stackHeight=peers.Length*54;
            double left=Math.Clamp(anchor.X+22,26,Math.Max(26,Bounds.Width-labelWidth-16));
            double top=Math.Clamp(anchor.Y-stackHeight/2,112,Math.Max(112,Bounds.Height-stackHeight-32))+Array.IndexOf(peers,cluster)*54;
            var label=new Rect(left,top,labelWidth,46);var p=new Point(left+16,top+23);
            var brush=listener?Palette.Listener:Palette.Voice;
            c.DrawLine(new Pen(brush,.8),anchor,new Point(left,top+23));
            c.DrawEllipse(Palette.Canvas,new Pen(brush,1.2),anchor,3,3);
            c.FillRectangle(Palette.Panel,label);
            if(listener)c.DrawRectangle(null,new Pen(Palette.Listener,.8),label,3,3);
            DrawSpatialIcon(c,p,listener);
            using(var labelClip=c.PushClip(new Rect(left+32,top+3,labelWidth-38,40)))
            {
                Text(c,cluster.Items.Length>1?$"{cluster.Items.Length} 个{(listener?"监听器":"音源")} · 展开列表":e.name,left+34,top+5,brush);
                Text(c,$"X {e.x:0.##} · Y {e.y:0.##} · Z {e.z:0.##}",left+34,top+25,size:10);
            }
            if(cluster.Items.Length>1){_expandHits.Add((label,cluster.Key));_spatialHits.Add((label,null,cluster.Key));}
            else {_hits.Add((label,e));_spatialHits.Add((label,e,null));}
        }
        void DrawExpanded(SpatialCluster cluster)
        {
            double left=Math.Max(20,Bounds.Width-290),top=112;
            c.FillRectangle(Palette.Panel,new Rect(left,top,280,Math.Max(30,Bounds.Height-top-30)));
            Text(c,$"{(cluster.Entity!="source"?"监听器":"音源")}列表 · {cluster.Items.Length} 个",left+12,top+10,Palette.Text,13);
            using var clip=c.PushClip(new Rect(left,top+38,280,Math.Max(0,Bounds.Height-top-72)));
            for(int i=0;i<cluster.Items.Length;i++)
            {
                var item=cluster.Items[i];var y=top+40+i*34-_vertical;
                if(!VisibleRow(y,34,top+38))continue;
                if(i%2==0)c.FillRectangle(Palette.Alternate,new Rect(left,y,280,34));
                Text(c,item.name,left+12,y+3,Palette.Text,11);
                Text(c,$"对象 {i+1} · Y {item.y:0.##}",left+12,y+19,size:9);
                var hitTop=Math.Max(y,top+38);var hitBottom=Math.Min(y+34,Bounds.Height-30);
                if(hitBottom>hitTop){var rect=new Rect(left,hitTop,280,hitBottom-hitTop);_hits.Add((rect,item));_spatialHits.Add((rect,item,null));}
            }
        }
    }
    private void DrawSpatialIcon(DrawingContext c,Point point,bool listener)
    {
        using var transform=c.PushTransform(Matrix.CreateScale(.78,.78)*Matrix.CreateTranslation(point.X-11,point.Y-11));
        c.DrawGeometry(null,new Pen(listener?Palette.Listener:Palette.Voice,1.8),listener?EarIcon:SpeakerIcon);
    }

}
