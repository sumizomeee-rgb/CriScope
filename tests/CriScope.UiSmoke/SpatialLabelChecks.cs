using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using CriScope.App;
using CriScope.Core;

internal static class SpatialLabelChecks
{
    private const string LastName="c_character_last_played_long_cue_for_spatial_inspection";
    private static WireEvent[] Fixture(string session)
    {
        var events=new List<WireEvent>();
        WireEvent Add(string kind,string entity,string id,string parent="",string name="",double x=0,double z=0,string raw="")
        {
            var e=new WireEvent {session=session,source="cri-native",seq=events.Count+1,time=(events.Count+1)*.01,
                kind=kind,entity=entity,objectId=id,parentId=parent,name=name,x=x,y=25.85,z=z,raw=raw};
            events.Add(e);return e;
        }
        string Link(string id,string name) => JsonSerializer.Serialize(new {derived=new {links=new[]{new {playback=id,cue=name}}}});
        Add("position","listener","listener",x:128.36,z:120.5,name:"Listener");
        Add("position","distance-listener","listener",x:128.36,z:120.5,name:"衰减监听点");
        Add("request","cue","active","player",name:"c_liRK2_buff");
        Add("play","voice","voice-active","active");
        Add("position","source","source-active",name:"c_liRK2_buff",x:128.35,z:120.44,raw:Link("active","c_liRK2_buff"));
        Add("request","cue","last","player2",name:LastName);
        Add("play","voice","voice-last","last");
        Add("position","source","source-last",name:LastName,x:118,z:114,raw:Link("last",LastName));
        Add("stop","voice","voice-last","last");
        Add("log","cue","last").lifecycle="released";
        Add("position","source","source-last",name:"音源（未观测到播放关联）",x:118,z:114,
            raw:JsonSerializer.Serialize(new {derived=new {links=Array.Empty<object>(),lastPlayback=new {playback="last",cue=LastName,observedAt=.07}}}));
        for(int i=0;i<6;i++)Add("position","source","same-point-"+i,name:"音源（未观测到播放关联）",z:3,raw:"{\"derived\":{\"links\":[]}}");
        for(int i=0;i<6;i++)Add("position","source","unknown-"+i,name:"音源（未观测到播放关联）",x:i==0?2:126+i*.6,z:i==0?7:113+i*1.5,raw:"{\"derived\":{\"links\":[]}}");
        return events.ToArray();
    }
    public static void Run(Action<bool,string> check)
    {
        var events=Fixture("spatial-labels");var index=SpatialCuePresentation.Build(events,1);
        var active=events.Single(e=>e.objectId=="source-active");var idle=events.Last(e=>e.objectId=="source-last");
        check(index.For(active).Active&&index.For(active).Caption=="c_liRK2_buff","空间常态保留当前 Cue，只有真实活动 Voice 才标为播放中");
        check(!index.For(idle).Active&&index.For(idle).Caption=="上次 · "+LastName,"空闲音源保留上次 Cue，并与当前播放明确区分");
        check(index.For(events.Last()).Caption==""&&!index.For(events.Last()).Active,"没有关联证据的音源只有中性标记，不产生常驻诊断文案");
        var legacy=events.Select(e=>{var copy=e.Clone();if(copy.objectId=="source-last"&&copy.seq==11)copy.raw="{\"derived\":{\"links\":[]}}";return copy;}).ToArray();
        check(SpatialCuePresentation.Build(legacy,1).For(idle).Last?.Name==LastName,"旧记录从明确的历史关联恢复上次 Cue");
        var reset=new WireEvent {session=idle.session,seq=24,time=.24,kind="remove",entity="source",objectId=idle.objectId};
        var recreated=idle.Clone();recreated.seq=25;recreated.time=.25;recreated.raw="{\"derived\":{\"links\":[]}}";
        check(SpatialCuePresentation.Build(legacy.Concat(new[]{reset,recreated}).ToArray(),1).For(recreated).Last==null,"源对象销毁并复用后不继承旧 Cue");
        var unknownLast=idle.Clone();unknownLast.seq=25;unknownLast.time=.25;
        unknownLast.raw="{\"derived\":{\"links\":[],\"lastPlayback\":{\"playback\":\"unknown\",\"cue\":\"Cue 名称未知\",\"observedAt\":0.25}}}";
        check(SpatialCuePresentation.Build(legacy.Append(unknownLast).ToArray(),1).For(unknownLast).Last==null,
            "最近一次播放名称缺失时，不把更早的已知 Cue 冒充最近一次");
        var future=SpatialCuePresentation.Build(events,.06);
        check(future.For(idle).Last==null,"历史时刻不会显示未来才发生的上次 Cue");
        var iconAreas=new List<Rect>();
        for(int i=0;i<20;i++)
        {
            var icon=SpatialPresentation.PlaceMarker(new Point(700+i*.2,300),new Rect(40,120,760,450),iconAreas);
            iconAreas.Add(new Rect(icon.X-12,icon.Y-12,24,24));
        }
        check(iconAreas.SelectMany((rect,i)=>iconAreas.Skip(i+1).Select(other=>!rect.Intersects(other))).All(clear=>clear),
            "密集点位的图标及 Hover 区域在图边也不相互覆盖");
        var label=SpatialPresentation.PlaceCompactLabel(new Point(100,100),120,new Rect(0,0,400,300),[]);
        check(label is {Height:24}&&Math.Abs(label.Value.X-100)<140,"空间 Cue 使用靠近标记的一行紧凑排版");
        var durable=idle.Clone();durable.seq=30;durable.time=300;
        check(SpatialCuePresentation.Build([durable],301).For(durable).Last?.Name==LastName,"新录制的上次 Cue 元数据可独立于实时播放窗口保留");
        var view=new SpatialViewport();var clusters=SpatialPresentation.Group(events,1);
        view.Layout(clusters.SelectMany(c=>c.Items),new Rect(40,122,1000,500));
        var arranged=view.Arrange(clusters);
        string Id(SpatialPlacement p)=>p.Cluster.Entity+":"+string.Join(",",p.Cluster.Items.Select(e=>e.objectId).Order());
        var slots=arranged.ToDictionary(Id,p=>(p.Hit,p.Label));
        var changed=clusters.Select(c=>c with {Items=c.Items.Select(e=>{var clone=e.Clone();clone.name="different Cue";clone.raw="";return clone;}).ToArray()}).Reverse().ToArray();
        check(view.Arrange(changed).All(p=>slots[Id(p)]==(p.Hit,p.Label)),"Cue 文字、状态与输入顺序改变时，图标及固定标签槽位保持原位");
        var newcomer=new SpatialCluster("new","source",[new WireEvent {session=events[0].session,entity="source",objectId="000-new",x=128.35,z=120.44}]);
        check(view.Arrange(clusters.Prepend(newcomer).ToArray()).Where(p=>p.Cluster!=newcomer).All(p=>slots[Id(p)]==(p.Hit,p.Label)),
            "新音源进入密集区域时，已有对象的槽位不重新排队");
        check(view.Clone().Arrange(clusters).All(p=>slots[Id(p)]==(p.Hit,p.Label)),"切换主题克隆视图时保留对象槽位");
        var projected=view.Project(active);var center=(view.CenterX,view.CenterZ,view.Extent);
        var far=new WireEvent {x=100000,z=-100000};view.Layout([active,far],view.Plot);
        check(center==(view.CenterX,view.CenterZ,view.Extent)&&view.Project(active)==projected,"新点位和坐标范围变化不会自动重取景");
        view.Fit();view.Layout([active,far],view.Plot);
        check(view.Plot.Contains(view.Project(far))&&view.Plot.Contains(view.Project(active)),"按全览仍能找回远处的新对象");
    }
    public static async Task RunUi(MainWindow window,Action<Session> register,Action<bool,string> check)
    {
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        using var session=new Session(new WireEvent {kind="hello",session=Guid.NewGuid().ToString("N"),name="空间排版验证",channel="native",source="cri-native"});
        typeof(Session).GetProperty("Connected")!.SetValue(session,true);
        foreach(var e in Fixture(session.Id))session.Accept(e);register(session);
        window.ApplyUiAction("session",session.Id);window.ApplyUiAction("workspace","Location");window.ApplyUiAction("filter","");
        if(Field<bool>("_showInspector"))Field<Button>("_detailsToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.ApplyUiAction("spatial-layer","distance-listener:true");window.ApplyUiAction("fit",null);
        window.ApplyUiAction("theme","dark");await Task.Delay(220);
        var timeline=Field<TimelineControl>("_timeline");window.CapturePng("window");
        var current=timeline.SpatialMarkers.Single(m=>m.Caption=="c_liRK2_buff");
        var last=timeline.SpatialMarkers.Single(m=>m.Caption=="上次 · "+LastName);
        var ear=timeline.SpatialMarkers.First(m=>m.Entity=="distance-listener");
        check(timeline.SpatialMarkers.SelectMany((m,i)=>timeline.SpatialMarkers.Skip(i+1).Select(n=>!m.Hit.Intersects(n.Hit))).All(clear=>clear),
            "密集真实画面先安排图标，监听点与所有音源的图标点击区互不覆盖");
        check(current.Active&&!last.Active&&ear.Caption==""&&timeline.SpatialMarkers.Any(m=>m.Count==6&&m.Caption==""),
            "真实空间画面区分活动与空闲颜色，耳朵无常驻文案，同点对象保留数量角标");
        check(current.CaptionBounds is {} currentLabel&&Math.Abs(currentLabel.Center.Y-current.Hit.Center.Y)<=12,
            "密集点位优先为当前 Cue 保留紧贴图标的一行名称");
        check(timeline.SpatialMarkers.All(m=>!m.Caption.Contains("未观测")&&!m.Caption.Contains("衰减监听")),"常态图中移除对象类型、坐标及未关联说明卡片");
        check(timeline.SpatialTipAt(ear.Hit.Center)?.Contains("衰减监听点")==true&&timeline.SpatialTipAt(last.Hit.Center)?.Contains(LastName)==true,
            $"Hover 耳朵可看类型与坐标，空闲音源可看完整上次 Cue（耳朵：{timeline.SpatialTipAt(ear.Hit.Center)}；上次：{timeline.SpatialTipAt(last.Hit.Center)}）");
        var hits=timeline.SpatialMarkers.Select(m=>m.Hit).ToHashSet();
        void Accept(long seq,string kind,string entity,string id,string parent="",string name="",string raw="")=>session.Accept(new WireEvent {
            session=session.Id,source="cri-native",seq=seq,time=seq*.01,kind=kind,entity=entity,objectId=id,parentId=parent,name=name,
            x=128.35,y=25.85,z=120.44,raw=raw});
        Accept(24,"stop","voice","voice-active","active");
        Accept(25,"position","source","source-active",raw:"{\"derived\":{\"links\":[],\"lastPlayback\":{\"playback\":\"active\",\"cue\":\"c_liRK2_buff\",\"observedAt\":0.04}}}");
        window.ApplyUiAction("filter","");await Task.Delay(180);window.CapturePng("workspace");
        var stopped=timeline.SpatialMarkers.Single(m=>m.Caption=="上次 · c_liRK2_buff");
        check(!stopped.Active&&stopped.Hit==current.Hit&&stopped.CaptionBounds==current.CaptionBounds&&hits.SetEquals(timeline.SpatialMarkers.Select(m=>m.Hit)),
            "真实 UI 中播放转停止只改变状态和文字，不移动图标、Cue 标签及邻居");
        Accept(26,"request","cue","active2","player",name:"tick");Accept(27,"play","voice","voice-active2","active2");
        Accept(28,"position","source","source-active",raw:"{\"derived\":{\"links\":[{\"playback\":\"active2\",\"cue\":\"tick\"}]}}");
        window.ApplyUiAction("filter","");await Task.Delay(180);window.CapturePng("workspace");
        var replayed=timeline.SpatialMarkers.Single(m=>m.Caption=="tick");
        check(replayed.Active&&replayed.Hit==current.Hit&&replayed.CaptionBounds==current.CaptionBounds&&hits.SetEquals(timeline.SpatialMarkers.Select(m=>m.Hit)),
            "同一音源切换长短不同的 Cue 后，真实 UI 仍保留相同排布");
        timeline.Selected=session.Snapshot().Single(e=>e.seq==25);
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-stability-dark.png"),window.CapturePng("workspace"));
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-labels-dark.png"),window.CapturePng("workspace"));
        window.ApplyUiAction("theme","light");await Task.Delay(150);
        timeline=Field<TimelineControl>("_timeline");window.CapturePng("workspace");
        check(timeline.SpatialMarkers.Single(m=>m.Caption=="tick").Hit==current.Hit&&hits.SetEquals(timeline.SpatialMarkers.Select(m=>m.Hit)),
            "真实 UI 切换主题后取景和对象排布保持一致");
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-stability-light.png"),window.CapturePng("workspace"));
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-labels-light.png"),window.CapturePng("workspace"));
        window.ApplyUiAction("select","11");await Task.Delay(100);
        check(Field<StackPanel>("_details").GetVisualDescendants().OfType<SelectableTextBlock>()
            .Any(t=>t.Tag?.ToString()=="field:上次播放 Cue"&&t.Text==LastName),"点击空闲音源后抽屉能查看上次播放 Cue");
        var jump=Field<StackPanel>("_details").GetVisualDescendants().OfType<Button>()
            .Single(b=>b.Tag?.ToString()?.StartsWith("last-source-playback:")==true);
        jump.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        check(Field<TimelineControl>("_timeline").Mode=="Timeline"&&Field<WireEvent>("_selected").objectId=="last","上次 Cue 的定位入口指向确切播放实例");
    }
}
