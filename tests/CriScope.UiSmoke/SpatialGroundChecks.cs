using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CriScope.App;
using CriScope.Core;

internal static class SpatialGroundChecks
{
    public static void Run(Action<bool,string> check)
    {
        var origin=new WireEvent {kind="position",entity="source",objectId="s"};
        var raised=new WireEvent {kind="position",entity="distance-listener",objectId="l",y=999,z=3};
        var view=new SpatialViewport();var plot=new Rect(40,120,800,500);view.Layout([origin,raised],plot);
        var a=view.Project(origin);var b=view.Project(raised);
        check(a.X==b.X&&Math.Abs(a.Y-b.Y-3*view.Scale)<1e-8,"地面投影保持 XZ 等比例，Y 高度不会挤占水平距离");
        var measure=SpatialPresentation.Measure(origin,new WireEvent {x=3,y=12,z=4});
        check(measure.Horizontal==5&&measure.Height==12&&measure.Spatial==13,"水平距离、高差和三维距离分别计算，避免把高度当成地面距离");
        var coincident=new WireEvent {kind="position",entity="distance-listener",objectId="l",y=1.7};
        var clusters=SpatialPresentation.Group([origin,coincident],1);
        view.Fit();view.Layout([origin,coincident],plot);var placements=view.Arrange(clusters);
        check(placements[0].Anchor==placements[1].Anchor&&placements[0].Icon!=placements[1].Icon&&placements.All(p=>p.Anchor!=p.Icon)&&
            placements.Where(p=>p.Anchor!=p.Icon).All(p=>SpatialPresentation.Leader(p.Anchor,p.Icon).First()==p.Anchor),
            "同点音源和监听点共用真实锚点，拥挤标注的细引线从真实锚点出发");
        var close=origin.Clone();close.objectId="s2";close.x=.0004;
        check(SpatialPresentation.Group([origin,close],1).Length==2,"近邻坐标不按小数位合并，放大后仍保持真实水平距离");
    }
    private static WireEvent[] Fixture(string session)
    {
        var events=new List<WireEvent>();
        void Add(string kind,string entity,string id,string parent="",string name="",double y=0,double z=0,string raw="")=>events.Add(new WireEvent {
            session=session,source="cri-native",seq=events.Count+1,time=1+(events.Count+1)*.01,kind=kind,entity=entity,
            objectId=id,parentId=parent,name=name,y=y,z=z,raw=raw});
        Add("position","distance-listener","ear",name:"衰减监听点",y:1.7,z:3);
        Add("request","cue","top","player",name:"floor_top_cue");Add("play","voice","top-voice","top");
        for(int i=0;i<7;i++)Add("position","source","top-"+i,z:3,
            raw:i==0?"{\"derived\":{\"links\":[{\"playback\":\"top\",\"cue\":\"floor_top_cue\"}]}}":"{\"derived\":{\"links\":[]}}");
        Add("request","cue","bottom","player2",name:"floor_bottom_cue");Add("play","voice","bottom-voice","bottom");
        Add("position","source","bottom-source",raw:"{\"derived\":{\"links\":[{\"playback\":\"bottom\",\"cue\":\"floor_bottom_cue\"}]}}");
        return events.ToArray();
    }
    public static async Task RunUi(MainWindow window,Action<Session> register,Action<bool,string> check)
    {
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        using var session=new Session(new WireEvent {kind="hello",session=Guid.NewGuid().ToString("N"),name="地面俯视验证",source="cri-native",channel="native"});
        typeof(Session).GetProperty("Connected")!.SetValue(session,true);
        var events=Fixture(session.Id);foreach(var e in events)session.Accept(e);register(session);
        window.ApplyUiAction("session",session.Id);window.ApplyUiAction("workspace","Location");window.ApplyUiAction("filter","");
        window.ApplyUiAction("spatial-layer","distance-listener:true");window.ApplyUiAction("spatial-layer","listener:false");
        window.ApplyUiAction("select",events.Single(e=>e.objectId=="bottom-source").seq.ToString());
        if(Field<bool>("_showInspector"))Field<Button>("_detailsToggle").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        window.ApplyUiAction("theme","dark");window.ApplyUiAction("fit",null);await Task.Delay(180);
        var timeline=Field<TimelineControl>("_timeline");window.CapturePng("workspace");
        var top=timeline.SpatialMarkers.Single(m=>m.Caption=="floor_top_cue");
        var bottom=timeline.SpatialMarkers.Single(m=>m.Caption=="floor_bottom_cue");
        var ear=timeline.SpatialMarkers.Single(m=>m.Entity=="distance-listener");
        check(top.Anchor==ear.Anchor&&top.Count==7&&!top.Hit.Intersects(ear.Hit)&&!top.Hit.Contains(top.Anchor)&&!ear.Hit.Contains(ear.Anchor),"真实 UI 同点音源与耳朵共用坐标实点，图标用细引线分开且可各自点击");
        check(top.Anchor.X==bottom.Anchor.X&&Math.Abs(bottom.Anchor.Y-top.Anchor.Y-3*timeline.SpatialView.Scale)<1e-8,
            "真实 UI 的 Z3 与 Z0 保持三单位地面间距，不受监听器高度和标注偏移影响");
        check(timeline.SpatialTipAt(bottom.Hit.Center)?.Contains("水平 3 · 高差 1.7 · 三维 3.448")==true,
            "Hover 显示水平距离、高差及三维距离，参考关系不冒充绑定");
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-ground-dark.png"),window.CapturePng("workspace"));
        window.ApplyUiAction("theme","light");await Task.Delay(120);
        timeline=Field<TimelineControl>("_timeline");window.CapturePng("workspace");
        check(timeline.SpatialMarkers.Single(m=>m.Caption=="floor_top_cue").Anchor==top.Anchor,"切换主题后真实地面坐标与标注保持稳定");
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-ground-light.png"),window.CapturePng("workspace"));
    }
}
