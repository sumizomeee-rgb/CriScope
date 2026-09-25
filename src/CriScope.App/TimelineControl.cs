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
    public string Mode { get; set; } = "Timeline";
    public event Action<WireEvent>? EventSelected;
    public event Action? SelectionCleared;
    public event Action? ViewChanged;
    public double? SelectionStart { get; private set; }
    public double? SelectionEnd { get; private set; }
    public void SetSelection(double? start, double? end) { SelectionStart=start; SelectionEnd=end; InvalidateVisual(); }
    private Point? _drag;
    private double _dragEnd, _vertical, _contentHeight;
    private bool _panning, _space;
    private int _busChannels;
    private readonly List<(Rect rect, WireEvent item)> _hits = [];
    private readonly List<(Rect rect, string key)> _expandHits = [];
    private readonly HashSet<string> _expanded = [];
    private const double LabelWidth = 214;
    private double PlotWidth => Math.Max(40, Bounds.Width-LabelWidth-22);
    public double Start => End-Span;
    private double X(double time) => LabelWidth+(time-Start)/Span*PlotWidth;
    private static readonly Typeface Font = new("Segoe UI, Microsoft YaHei UI");
    public static string TimeLabel(double v) => $"{(int)(Math.Max(0,v)/60):00}:{Math.Max(0,v)%60:00.000}";

    public TimelineControl()
    {
        ClipToBounds=true; Focusable=true; MinHeight=220;
        PointerWheelChanged += (_,e) => {
            if(e.KeyModifiers.HasFlag(KeyModifiers.Control)) {
                var f=Math.Clamp((e.GetPosition(this).X-LabelWidth)/PlotWidth,0,1); var anchor=Start+f*Span;
                Span=Math.Clamp(Span*(e.Delta.Y>0?.8:1.25),.25,86400); if(!Live)End=anchor+Span*(1-f);
            } else if(e.KeyModifiers.HasFlag(KeyModifiers.Shift)){End-=e.Delta.Y*Span*.1;Live=false;}
            else _vertical=Math.Clamp(_vertical-e.Delta.Y*36,0,Math.Max(0,_contentHeight+100-Bounds.Height));
            ViewChanged?.Invoke();InvalidateVisual();e.Handled=true;
        };
        PointerPressed += (_,e) => {
            Focus();var p=e.GetPosition(this);_panning=_space||e.GetCurrentPoint(this).Properties.IsMiddleButtonPressed;
            if(_panning||e.KeyModifiers.HasFlag(KeyModifiers.Shift)){_drag=p;_dragEnd=End;e.Pointer.Capture(this);return;}
            var expand=_expandHits.LastOrDefault(h=>h.rect.Contains(p));
            if(expand.key!=null){if(!_expanded.Add(expand.key))_expanded.Remove(expand.key);InvalidateVisual();return;}
            var hit=_hits.LastOrDefault(h=>h.rect.Contains(p));
            if(hit.item!=null){Selected=hit.item;EventSelected?.Invoke(hit.item);}else{Selected=null;SetSelection(null,null);SelectionCleared?.Invoke();}
            InvalidateVisual();
        };
        PointerMoved += (_,e) => {
            var p=e.GetPosition(this);
            if(_drag is not {} start){var hit=_hits.LastOrDefault(h=>h.rect.Contains(p));ToolTip.SetTip(this,hit.item==null?null:$"{hit.item.name}\n{TimeLabel(hit.item.time)} · {hit.item.kind}\n{hit.item.detail}");return;}
            var delta=p.X-start.X;if(Math.Abs(delta)<8)return;
            if(_panning){End=_dragEnd-delta/PlotWidth*Span;Live=false;}
            else{SelectionStart=Start+Math.Clamp((start.X-LabelWidth)/PlotWidth,0,1)*Span;SelectionEnd=Start+Math.Clamp((p.X-LabelWidth)/PlotWidth,0,1)*Span;}
            ViewChanged?.Invoke();InvalidateVisual();
        };
        PointerReleased += (_,e)=>{_drag=null;e.Pointer.Capture(null);};
        KeyDown += (_,e)=>{if(e.Key==Key.Space){_space=true;e.Handled=true;}};
        KeyUp += (_,e)=>{if(e.Key==Key.Space){_space=false;e.Handled=true;}};
        LostFocus += (_,_)=>_space=false;
    }
    private void Text(DrawingContext c,string text,double x,double y,IBrush? brush=null,double size=11)
        =>c.DrawText(new FormattedText(text,CultureInfo.InvariantCulture,FlowDirection.LeftToRight,Font,size,brush??Palette.Muted),new Point(x,y));
    private void RowName(DrawingContext c,string name,double y,IBrush? brush=null)
    {using var clip=c.PushClip(new Rect(12,y,LabelWidth-24,23));Text(c,name,12,y,brush??Palette.Text,12);}
    private void Empty(DrawingContext c,string title,string description)
    {Text(c,title,26,105,Palette.Text,18);Text(c,description,26,141,size:12);}
    public override void Render(DrawingContext c)
    {
        base.Render(c);c.FillRectangle(Palette.Canvas,new Rect(Bounds.Size));_hits.Clear();_expandHits.Clear();
        if(Events.Length==0&&SupplementMetrics.Length==0){Empty(c,"等待音频观测","连接 CRI Monitor，或打开录制。尚未收到的数据不会显示为 0。");return;}
        if(Mode=="Location"){DrawLocations(c);return;}if(Mode=="Mixing"){DrawMixing(c);DrawScrollHint(c);return;}
        double step=Math.Pow(10,Math.Floor(Math.Log10(Span/8)));if(Span/step>16)step*=5;else if(Span/step>10)step*=2;
        for(double t=Math.Ceiling(Start/step)*step;t<=End;t+=step){var x=X(t);c.DrawLine(new Pen(Palette.Border,.5),new Point(x,31),new Point(x,Bounds.Height-28));Text(c,TimeLabel(t),x+4,10,size:10);}
        Text(c,"会话相对时间",12,10,size:10);c.DrawLine(new Pen(Palette.Border),new Point(0,34),new Point(Bounds.Width,34));
        if(Mode=="AISAC")DrawControls(c);else if(Mode=="Performance")DrawResources(c);else DrawTracks(c);
        if(SelectionStart is {} a&&SelectionEnd is {} b&&Math.Abs(a-b)>.000001){var l=Math.Clamp(X(Math.Min(a,b)),LabelWidth,LabelWidth+PlotWidth);var r=Math.Clamp(X(Math.Max(a,b)),LabelWidth,LabelWidth+PlotWidth);c.DrawRectangle(null,new Pen(Palette.Selection,1.5),new Rect(l,35,Math.Max(0,r-l),Math.Max(0,Bounds.Height-64)));}
        if(Selected is {} selected&&selected.time>=Start&&selected.time<=End)c.DrawLine(new Pen(Palette.Selection,1),new Point(X(selected.time),35),new Point(X(selected.time),Bounds.Height-28));
        c.FillRectangle(Palette.Canvas,new Rect(0,Bounds.Height-28,Bounds.Width,28));Text(c,"Ctrl 滚轮缩放 · 中键 / 空格拖动浏览历史 · Shift 拖动选区",12,Bounds.Height-21,size:10);
        if(Live)Text(c,"NOW",Bounds.Width-54,Bounds.Height-21,Palette.Good,10);
    }
    private void DrawTracks(DrawingContext c)
    {
        Text(c,"● 实际 Voice",14,45,Palette.Voice);Text(c,"◆ Cue 请求",124,45,Palette.Request);Text(c,"○ 结束未观测",230,45);
        var groups=Events.Where(e=>e.kind is "play" or "stop"&&e.time<=End&&!string.IsNullOrEmpty(e.objectId)).GroupBy(e=>e.objectId);
        double y=75-_vertical;int row=0;using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var group in groups){var life=group.OrderBy(e=>e.time).ToArray();if(life[^1].kind=="stop"&&life[^1].time<Start)continue;
            if(row++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,36));var first=life.FirstOrDefault(e=>e.kind=="play")??life[0];
            RowName(c,string.IsNullOrEmpty(first.name)?"Voice "+first.objectId:first.name,y+3);Text(c,first.kind=="play"?"Voice · 已确认分配":"Voice · 起点未观测",12,y+21,size:9);_hits.Add((new Rect(0,y,LabelWidth,36),first));
            WireEvent? opened=null;
            foreach(var e in life){if(e.kind=="play"){if(opened!=null)Bar(opened,e.time,false);opened=e;}else if(opened!=null){Bar(opened,e.time,true);if(e.time>=Start)_hits.Add((new Rect(X(e.time)-5,y,10,36),e));opened=null;}else if(e.time>=Start){var x=X(e.time);c.DrawLine(new Pen(Palette.Error,2),new Point(x,y+7),new Point(x,y+29));_hits.Add((new Rect(x-5,y,12,36),e));}}
            if(opened!=null)Bar(opened,End,false);
            void Bar(WireEvent start,double end,bool stopped){var gap=Discontinuities.Where(d=>d.time>start.time&&d.time<end).OrderBy(d=>d.time).FirstOrDefault();if(gap!=null){end=gap.time;stopped=false;}var l=Math.Max(LabelWidth,X(start.time));var r=Math.Min(LabelWidth+PlotWidth,X(end));if(r<l)return;c.FillRectangle(Palette.Voice,new Rect(l,y+13,Math.Max(2,r-l),10));if(start.time>=Start)c.DrawLine(new Pen(Palette.Voice,2),new Point(l,y+8),new Point(l,y+28));if(stopped)c.DrawLine(new Pen(Palette.Error,2),new Point(r,y+8),new Point(r,y+28));else c.DrawEllipse(Palette.Canvas,new Pen(Palette.Voice,1.4),new Point(r,y+18),4,4);_hits.Add((new Rect(l,y,Math.Max(8,r-l),36),start));}y+=36;
        }
        foreach(var group in Events.Where(e=>e.kind=="request"&&e.time>=Start&&e.time<=End).GroupBy(e=>e.name)){RowName(c,string.IsNullOrEmpty(group.Key)?"Cue 请求":group.Key,y+7,Palette.Request);foreach(var e in group){var x=X(e.time);c.DrawEllipse(Palette.Request,null,new Point(x,y+17),4,4);_hits.Add((new Rect(x-7,y,14,34),e));}y+=34;}
        _contentHeight=y+_vertical-75;if(row==0&&!Events.Any(e=>e.kind=="request"))Empty(c,"尚未观测到 Voice 生命周期","热接入不补造此前的开始。控制和资源数据可在相应工作区查看。");
    }
    private void DrawControls(DrawingContext c)
    {
        Text(c,"● AISAC 最后写入",14,45,Palette.Selection);Text(c,"● Selector / Block",160,45,Palette.Request);Text(c,"● Beat / Sequence",310,45,Palette.Good);
        var groups=Events.Where(e=>e.time<=End&&e.kind is "aisac" or "selector" or "block" or "beat" or "sequence").GroupBy(e=>(e.kind,e.name,e.objectId)).ToArray();
        if(groups.Length==0){Empty(c,"控制状态尚未观测","连接前的值不保证可恢复；SDK 扩展可补充节拍与 Block 观察。");return;}
        double y=73-_vertical;int i=0;using var clip=c.PushClip(new Rect(0,68,Bounds.Width,Math.Max(0,Bounds.Height-96)));
        foreach(var group in groups){var key=group.Key.ToString();var expanded=_expanded.Contains(key);double h=expanded?116:36;var all=group.OrderBy(e=>e.time).ToArray();var last=all[^1];var brush=last.kind=="aisac"?Palette.Selection:last.kind is "beat" or "sequence"?Palette.Good:Palette.Request;
            if(i++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,h));RowName(c,(expanded?"− ":"+ ")+last.name,y+3,brush);
            var display=last.kind=="aisac"?last.value.ToString("0.####",CultureInfo.InvariantCulture):last.detail;Text(c,$"{display[..Math.Min(display.Length,23)]} · {TimeLabel(last.time)}",12,y+21,size:9);_expandHits.Add((new Rect(0,y,LabelWidth,h),key));
            var samples=all.Where(e=>e.time>=Start).ToArray();if(expanded&&last.kind=="aisac")DrawSeries(c,all,y+8,h-22,brush,true);else foreach(var e in samples){var x=X(e.time);c.DrawEllipse(brush,null,new Point(x,y+18),3,3);_hits.Add((new Rect(x-5,y,10,h),e));}
            if(samples.Length==0)Text(c,"最后已知写入 · 本时间窗无新事件",LabelWidth+12,y+10,size:10);y+=h;
        }_contentHeight=y+_vertical-73;
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
            if(i++%2==0)c.FillRectangle(Palette.Alternate,new Rect(0,y,Bounds.Width,h));
            RowName(c,(canExpand?(expanded?"− ":"+ "):"")+MetricName(last),y+6);
            Text(c,pool?MetricPresentation.StreamingPool(last,capacity):MetricPresentation.Value(last),LabelWidth+14,y+7,Palette.Good,18);
            Text(c,pool?"SDK · StandardStreaming 池":supplement?"SDK 补充 · 独立时钟":"最后观测 "+TimeLabel(last.time),LabelWidth+205,y+12,size:10);
            if(canExpand)_expandHits.Add((new Rect(0,y,LabelWidth,h),key));
            _hits.Add((new Rect(0,y,Bounds.Width,h),last));
            if(expanded)DrawSeries(c,all,y+31,75,Palette.Good,false);
            y+=h;
        }if(!groups.Any(g=>(g[^1].name+g[^1].objectId).Contains("memory",StringComparison.OrdinalIgnoreCase)))Text(c,"Atom / FS 内存：此来源尚未提供",16,y+15,size:12);RowName(c,(_expanded.Contains("technical-metrics")?"− ":"+ ")+"技术性能指标",y+42);_expandHits.Add((new Rect(0,y+36,LabelWidth,36),"technical-metrics"));_contentHeight=y+_vertical+42;
    }
    private void DrawMixing(DrawingContext c)
    {
        _busChannels=0;
        Text(c,"混音 / BUS METER",22,20,Palette.Text,16);Text(c,"Peak ┃  RMS ▰  · 分声道 · 线性幅值换算 dBFS",22,50,Palette.Voice);
        var buses=Events.Where(e=>e.kind=="bus"&&e.time<=End).GroupBy(e=>e.objectId).Select(g=>g.OrderBy(e=>e.time).Last()).ToArray();if(buses.Length==0){Empty(c,"Bus 电平尚未提供","未观察到的路由与效果参数不会补造。请确认来源已启用相应监控。");return;}
        double y=86-_vertical;using var clip=c.PushClip(new Rect(0,75,Bounds.Width,Math.Max(0,Bounds.Height-100)));
        foreach(var e in buses){RowName(c,e.name,y+3,Palette.Voice);var channels=new List<(string name,double peak,double rms)>();try{using var doc=JsonDocument.Parse(e.raw);if(doc.RootElement.TryGetProperty("channels",out var array))foreach(var ch in array.EnumerateArray())channels.Add(("CH "+ch.GetProperty("channel"),ch.GetProperty("peak").GetDouble(),ch.GetProperty("rms").GetDouble()));}catch(JsonException){}catch(InvalidOperationException){}catch(KeyNotFoundException){}
            _busChannels+=channels.Count;if(channels.Count==0){Text(c,"分声道数据未提供",LabelWidth,y+6);y+=44;continue;}foreach(var ch in channels){double Db(double v)=>v>0?Math.Clamp(20*Math.Log10(v),-96,6):-96;var rms=Db(ch.rms);var peak=Db(ch.peak);Text(c,ch.name,12,y+25,size:10);c.FillRectangle(Palette.Alternate,new Rect(LabelWidth,y+12,PlotWidth,10));c.FillRectangle(Palette.Voice,new Rect(LabelWidth,y+12,Math.Clamp((rms+96)/102,0,1)*PlotWidth,10));var px=LabelWidth+Math.Clamp((peak+96)/102,0,1)*PlotWidth;c.DrawLine(new Pen(peak>=0?Palette.Error:Palette.Request,2),new Point(px,y+7),new Point(px,y+27));Text(c,$"Peak {(ch.peak==0?"−∞":peak.ToString("0.0"))} · RMS {(ch.rms==0?"−∞":rms.ToString("0.0"))} dBFS",LabelWidth,y+27,size:10);_hits.Add((new Rect(0,y,Bounds.Width,45),e));y+=48;}y+=14;
        }_contentHeight=y+_vertical-86;
    }
    private void DrawScrollHint(DrawingContext c)
    {
        c.FillRectangle(Palette.Panel,new Rect(0,Bounds.Height-30,Bounds.Width,30));
        Text(c,$"↕ 滚轮上下浏览全部声道 · 当前来源共 {_busChannels} 声道",18,Bounds.Height-23,Palette.Text,12);
        var view=Math.Max(1,Bounds.Height-106);
        if(_contentHeight<=view)return;
        var thumb=Math.Max(28,view*view/_contentHeight);
        var top=76+Math.Clamp(_vertical/Math.Max(1,_contentHeight-view),0,1)*(view-thumb);
        c.FillRectangle(Palette.Border,new Rect(Bounds.Width-7,76,3,view));
        c.FillRectangle(Palette.Muted,new Rect(Bounds.Width-8,top,5,thumb));
    }
    private void DrawLocations(DrawingContext c)
    {
        Text(c,"空间 / XZ 俯视",22,20,Palette.Text,16);Text(c,"● Source   ◇ Listener   · 世界坐标 / 最后观测",22,50,Palette.Listener);
        var positions=Events.Where(e=>e.kind=="position"&&e.time<=End&&double.IsFinite(e.x)&&double.IsFinite(e.z)).GroupBy(e=>e.entity+e.objectId).Select(g=>g.OrderBy(e=>e.time).Last()).ToArray();if(positions.Length==0){Empty(c,"空间位置尚未观测","Source、Listener 和方向分别来自观测；缺失位置不会被设为原点。");return;}
        bool Listener(WireEvent e)=>e.entity=="listener";bool hasListener=positions.Any(Listener);Text(c,hasListener?"Listener 位置已观测 · 方向缺失时不绘制朝向":"Listener 位置未提供 · 当前显示 Source 世界坐标",22,76,hasListener?Palette.Muted:Palette.Request);
        double cx=(positions.Min(e=>e.x)+positions.Max(e=>e.x))/2,cz=(positions.Min(e=>e.z)+positions.Max(e=>e.z))/2;var extent=Math.Max(2,positions.Max(e=>Math.Max(Math.Abs(e.x-cx),Math.Abs(e.z-cz))))*1.25;var center=new Point(Bounds.Width/2,(Bounds.Height+100)/2);var scale=Math.Max(1,Math.Min(Bounds.Width-140,Bounds.Height-180))/(2*extent);
        for(int i=-2;i<=2;i++){var x=center.X+i*extent*scale/2;var z=center.Y+i*extent*scale/2;c.DrawLine(new Pen(Palette.Border,.7),new Point(x,110),new Point(x,Bounds.Height-30));c.DrawLine(new Pen(Palette.Border,.7),new Point(40,z),new Point(Bounds.Width-30,z));}
        foreach(var cluster in positions.GroupBy(e=>(Math.Round(e.x,3),Math.Round(e.z,3)))){var e=cluster.FirstOrDefault(Listener)??cluster.First();var p=new Point(center.X+(e.x-cx)*scale,center.Y-(e.z-cz)*scale);var brush=Listener(e)?Palette.Listener:Palette.Voice;c.DrawEllipse(Palette.Canvas,new Pen(brush,2),p,Listener(e)?8:5,Listener(e)?8:5);Text(c,cluster.Count()>1?$"{cluster.Count()} 个对象 · 点击检查":e.name,p.X+12,p.Y-10,brush);Text(c,$"X {e.x:0.##} · Y {e.y:0.##} · Z {e.z:0.##}",p.X+12,p.Y+8,size:10);_hits.Add((new Rect(p.X-10,p.Y-12,140,40),e));}
    }
}
