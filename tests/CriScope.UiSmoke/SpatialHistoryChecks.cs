using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using CriScope.App;
using CriScope.Core;

internal static class SpatialHistoryChecks
{
    private static WireEvent[] Fixture(string session)
    {
        string Native(params (string Name, object Value)[] fields) => JsonSerializer.Serialize(new
            { parameters = fields.Select(f => new { name = f.Name, value = f.Value }) });
        string Link(string playback) => JsonSerializer.Serialize(new { derived = new { links = new[] { new { playback } } } });
        WireEvent E(long seq, double time, string kind, string entity, string id, string parent = "", string raw = "", double x = 0, double y = 0) =>
            new() { session = session, source = "cri-native", seq = seq, time = time, kind = kind, entity = entity,
                objectId = id, parentId = parent, raw = raw, name = entity == "cue" ? "瞬停测试声音" : entity, x = x, y = y };
        var ended = E(11, 1.012, "log", "cue", "pb1"); ended.lifecycle = "released";
        var pan = E(4, .995, "log", "player", "player", raw: Native(("PanType", 2))); pan.name = "ExPlayer_SetPanType";
        var info = E(15, 2.1, "cue-info", "cue", "pb1"); info.name = "瞬停测试声音";
        info.raw = "{\"basis\":\"cue-config\",\"panType\":1,\"minAttenuationDistance\":1,\"maxAttenuationDistance\":50}";
        return [
            E(1,.1,"position","listener","0x22"), E(2,.1,"position","distance-listener","0x22"),
            E(3,.99,"position","source","0x11",x:3,y:4), pan,
            E(5,1,"request","cue","pb1","player"),
            E(6,1.001,"play","voice","v1","pb1",Native(("CriAtomEx3dSourceHn","0x11"),("CriAtomEx3dListenerHn","0x22"))),
            E(7,1.005,"position","source","0x11",raw:Link("pb1"),x:6,y:8),
            E(8,1.01,"stop","voice","v1","pb1"),
            E(9,1.01,"position","source","0x11",raw:Link(""),x:900,y:900),
            E(10,1.011,"remove","source","0x11"), ended,
            E(12,2,"position","listener","0x22",x:1000),
            E(13,2,"request","cue","pb2","player"),
            E(14,2.001,"position","source","0x11",raw:Link("pb2"),x:20000), info
        ];
    }
    public static void Run(Action<bool, string> check)
    {
        var events = Fixture("spatial-history-fixture");
        var playback = PlaybackPresentation.Group(events, 3).Single(p => p.Id == "pb1");
        var report = PlaybackSpatialPresentation.Resolve(events, playback, 3, events[^1]);
        check(report.CuePan.StartsWith("Pos3d") && report.Start.PlayerPan.StartsWith("Auto"), "Cue Pan Type 与 Player 设置分开呈现，不把请求冒充最终状态");
        check(report.Start.Sources.Single().x == 3 && report.Last.Sources.Single().x == 6 && report.Last.Listeners.All(e => e.x == 0),
            "短音效保存开始和结束前现场，不受停止后移动、销毁及同名 Cue 复用影响");
        check(report.Start.Distances.All(d => d.Value == 5 && d.Bound) && report.Last.Distances.All(d => d.Value == 10),
            "空间距离包含 Y 轴，明确绑定与全局参考分开");
        var unbound = events.Select(e => e.seq == 6 ? new WireEvent { session=e.session, seq=e.seq,time=e.time,kind=e.kind,entity=e.entity,objectId=e.objectId,parentId=e.parentId,
            raw="{\"parameters\":[{\"name\":\"CriAtomEx3dSourceHn\",\"value\":\"0x11\"}]}" } : e).ToArray();
        check(PlaybackSpatialPresentation.Resolve(unbound,playback,3).Last.Distances.All(d=>!d.Bound),"单个全局监听器不自动推断为实际绑定监听器");
        var reusedListener=events.Select(e=>{var copy=e.Clone();if(copy.seq>=8)copy.seq+=2;return copy;})
            .Concat(new[]{new WireEvent {session=events[0].session,seq=8,time=1.006,kind="remove",entity="listener",objectId="0x22"},
                new WireEvent {session=events[0].session,seq=9,time=1.007,kind="position",entity="listener",objectId="0x22"}})
            .OrderBy(e=>e.time).ThenBy(e=>e.seq).ToArray();
        var reusedPlayback=PlaybackPresentation.Group(reusedListener,3).Single(p=>p.Id=="pb1");
        check(PlaybackSpatialPresentation.Resolve(reusedListener,reusedPlayback,3).Last.Distances.All(d=>!d.Bound),
            "监听器销毁后复用同一句柄，不沿用旧对象的绑定证据");
        var absent = PlaybackSpatialPresentation.Resolve(events.Where(e=>e.kind!="position"),playback,3,
            new WireEvent {raw="{\"basis\":\"cue-config\"}"});
        check(absent.CuePan=="未记录"&&absent.Start.Sources.Length==0&&absent.Last.Distances.Length==0,
            "旧日志缺少 Pan 与位置时显示未记录，不默认成 Pan3d 或原点");
        var lateMetadata=PlaybackSpatialPresentation.UpdateConfiguration(absent,events[^1]);
        check(lateMetadata.CuePan.StartsWith("Pos3d")&&ReferenceEquals(lateMetadata.Last,absent.Last),
            "迟到的 Cue 元数据更新 Pan Type，同时保留原来的历史位置快照");
        var zeroPan=PlaybackSpatialPresentation.Resolve(events,playback,3,new WireEvent {raw="{\"panType\":0}"});
        check(zeroPan.CuePan.StartsWith("Pan3d"),"Pan Type 的数值零是有效枚举，不是缺失值");
        var linked=events.Single(e=>e.seq==7).Clone();linked.raw="{\"derived\":{\"positionEvidence\":\"{\\\"submittedAtMicroseconds\\\":990000}\"}}";
        check(PlaybackSpatialPresentation.SampleTime(linked)==.99,"关联补发不改写原始位置提交时间");
        var interrupted = events.Append(new WireEvent {session=events[0].session,seq=8,time=1.007,kind="gap"}).OrderBy(e=>e.time).ThenBy(e=>e.seq).ToArray();
        var interruptedPlayback=PlaybackPresentation.Group(interrupted,3).Single(p=>p.Id=="pb1");
        check(PlaybackSpatialPresentation.Resolve(interrupted,interruptedPlayback,3).Evidence.Contains("结束位置未确认"),"采集中断的快照保留不完整标记，不声称确认结束现场");

        var viewport=new SpatialViewport();var far=new WireEvent {x=10000,z=-5000};
        var plot=new Rect(40,120,640,400);viewport.Layout([new WireEvent(),far],plot);
        check(plot.Contains(viewport.Project(far))&&plot.Contains(viewport.Project(new WireEvent())),"空间自动全览包含远处监听器");
        var anchor=new Point(200,200);var before=viewport.WorldAt(anchor);viewport.Zoom(anchor,4);
        check(Math.Abs(viewport.WorldAt(anchor).X-before.X)<1e-8&&Math.Abs(viewport.WorldAt(anchor).Y-before.Y)<1e-8&&!viewport.Automatic,"滚轮缩放以鼠标位置为锚点，不让目标漂移");
        var original=viewport.Project(far);viewport.Pan(new Vector(70,-30));
        check(Math.Abs(viewport.Project(far).X-original.X-70)<1e-8&&Math.Abs(viewport.Project(far).Y-original.Y+30)<1e-8,"拖拽平移按屏幕像素移动空间视图");
        viewport.Focus([far]);check(Math.Abs(viewport.Project(far).X-plot.Center.X)<1e-8&&Math.Abs(viewport.Project(far).Y-plot.Center.Y)<1e-8,"定位远处监听器可重新找回画外目标");
        viewport.Fit();viewport.Layout([new WireEvent {objectId="source"},far],plot,"source");
        check(plot.Contains(viewport.Project(far)),"定位音源后按新中心重算范围，监听器不会因旧范围被裁掉");

        var directory=Path.Combine(Path.GetTempPath(),"CriScope-spatial-"+Guid.NewGuid().ToString("N"));
        try
        {
            using var session=new Session(new WireEvent {kind="hello",session=events[0].session,value=1});
            session.StartRecording(directory);
            foreach(var e in events)session.Accept(e);
            session.Accept(new WireEvent {session=session.Id,seq=16,time=500,kind="metric"});
            check(session.Snapshot().All(e=>e.objectId!="pb1"),"历史测试先让短音效离开实时缓存");
            var restored=PlaybackSpatialPresentation.FromRecording(session,playback,3,events[^1],CancellationToken.None);
            check(restored.Last.Sources.Single().x==6&&restored.Start.Distances.All(d=>d.Value==5)&&restored.Evidence=="录制文件",
                "实时缓存淘汰后从录制恢复同一次播放的空间证据");
        }
        finally
        {
            if(!Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()),StringComparison.OrdinalIgnoreCase))throw new Exception("Unexpected test directory");
            if(Directory.Exists(directory))Directory.Delete(directory,true);
        }
    }

    public static async Task RunUi(MainWindow window, Action<Session> register, Action<bool,string> check)
    {
        T Field<T>(string name)=>(T)typeof(MainWindow).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(window)!;
        void Click(Button button)=>button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        using var session=new Session(new WireEvent {kind="hello",session=Guid.NewGuid().ToString("N"),source="cri-native",channel="native",name="空间历史验证",value=1});
        var fixture=Fixture(session.Id);
        foreach(var e in fixture.Where(e=>e.kind!="cue-info"))session.Accept(e);register(session);
        window.ApplyUiAction("session",session.Id);window.ApplyUiAction("workspace","Timeline");window.ApplyUiAction("filter","");window.ApplyUiAction("select","5");
        await Task.Delay(180);
        var details=Field<StackPanel>("_details");
        check(details.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t=>t.Tag?.ToString()=="spatial:Pan Type · Cue"&&t.Text=="未记录"),
            "Cue 元数据尚未到达时，结束实例先显示未记录");
        session.Accept(fixture[^1]);await Task.Delay(650);
        details=Field<StackPanel>("_details");
        check(details.GetVisualDescendants().OfType<SelectableTextBlock>().Any(t=>t.Tag?.ToString()=="spatial:Pan Type · Cue"&&t.Text!.StartsWith("Pos3d")),
            "选中已结束轨道可在抽屉看到 Pan Type 与历史空间记录");
        check(details.GetLogicalDescendants().OfType<Grid>().Count(row=>row.Tag?.ToString()?.StartsWith("row:spatial:到")==true)==4,
            "开始和结束两组监听距离使用独立字段，不会在抽屉刷新时互相覆盖");
        var action=details.GetVisualDescendants().OfType<Button>().Single(b=>b.Tag?.ToString()?.StartsWith("spatial-history:结束前:")==true);
        Click(action);await Task.Delay(180);
        var timeline=Field<TimelineControl>("_timeline");
        check(timeline.Mode=="Location"&&timeline.SpatialHistory?.Moment.Sources.Single().x==6&&!timeline.Live&&timeline.End==1.01,
            "查看结束前空间显示已销毁音源的历史影子，并固定在正确时刻");
        check(Field<StackPanel>("_details").GetVisualDescendants().OfType<SelectableTextBlock>().Any(t=>t.Tag?.ToString()=="field:状态"&&t.Text=="历史空间 · 已结束"),
            "历史影子保留原实例已结束状态，不退回实时等待状态");
        var end=timeline.End;timeline.ZoomSpatial(timeline.SpatialView.Plot.Center,4);timeline.PanSpatial(new Vector(80,40));
        check(timeline.End==end&&!timeline.Live,"空间缩放和平移不改变所查历史时刻");
        var viewport=timeline.SpatialView.Clone();window.ApplyUiAction("theme","dark");await Task.Delay(100);
        timeline=Field<TimelineControl>("_timeline");
        check(!timeline.SpatialView.Automatic&&timeline.SpatialView.CenterX==viewport.CenterX&&timeline.SpatialHistory!=null,
            "切换主题保留空间缩放、平移和历史影子");
        window.ApplyUiAction("fit",null);await Task.Delay(100);
        check(timeline.SpatialView.Automatic&&timeline.SpatialHistory!=null&&timeline.End==end,"空间全览保留历史现场并重新容纳所有点");
        Field<ScrollViewer>("_detailsScroll").Offset=new Vector(0,300);
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-history-dark.png"),window.CapturePng("window"));
        window.ApplyUiAction("theme","light");await Task.Delay(100);
        File.WriteAllBytes(Path.GetFullPath(".local/ui-check/spatial-history-light.png"),window.CapturePng("window"));
        window.ApplyUiAction("live","true");timeline=Field<TimelineControl>("_timeline");
        check(timeline.SpatialHistory==null&&timeline.Live,"返回实时明确退出历史影子，不把旧位置冒充实时对象");
        end=timeline.End;timeline.ZoomSpatial(timeline.SpatialView.Plot.Center,1);
        check(timeline.Live&&timeline.End==end,"实时空间缩放不会暂停跟随或停止采集");
        window.ApplyUiAction("back",null);await Task.Delay(100);
        check(Field<TimelineControl>("_timeline").Mode=="Timeline"&&Field<WireEvent>("_selected").objectId=="pb1",
            "从历史空间返回原播放轨道与选中实例");
    }
}
