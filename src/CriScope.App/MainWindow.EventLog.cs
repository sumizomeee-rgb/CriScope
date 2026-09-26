using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private string _logQuery = "", _logKind = "全部", _logSignature = "";
    private bool _logFollowing = true, _updatingLog, _logVoices;
    private WireEvent[] _logFrozen = [];
    private int _logLimit = 1000;
    private TextBlock? _logStatus;
    private Button? _logFollowButton;
    private TextBox? _logSearch;
    private Control BuildEventLog()
    {
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,26,*,26") };
        var bar = new Grid { ColumnDefinitions = new ColumnDefinitions("*,100,Auto,Auto,Auto"), Margin = new Thickness(12,5), ColumnSpacing=8 };
        var search = _logSearch = new TextBox { Text=_logQuery, PlaceholderText="搜索日志：开始播放 CueName、实例结束、AISAC…",FontSize=12 };
        search.TextChanged+=(_,_)=>{_logQuery=search.Text??""; UpdateEventLog();}; bar.Children.Add(search);
        var kind = new ComboBox {ItemsSource=new[]{"全部","播放","控制","回调","诊断"},SelectedItem=_logKind,FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch};
        kind.SelectionChanged+=(_,_)=>{_logKind=kind.SelectedItem as string??"全部";UpdateEventLog();}; Grid.SetColumn(kind,1);bar.Children.Add(kind);
        _logFollowButton=Action(_logFollowing?"暂停刷新":"继续实时",ToggleLogFollow); Grid.SetColumn(_logFollowButton,2);bar.Children.Add(_logFollowButton);
        var earlier=Action("更早记录",()=>{_logLimit=Math.Min(20000,_logLimit+1000);UpdateEventLog();});Grid.SetColumn(earlier,3);bar.Children.Add(earlier);
        var voices=new CheckBox {Content="声部明细",IsChecked=_logVoices,FontSize=11,VerticalAlignment=VerticalAlignment.Center};
        voices.IsCheckedChanged+=(_,_)=>{_logVoices=voices.IsChecked==true;UpdateEventLog();};Grid.SetColumn(voices,4);bar.Children.Add(voices);
        ToolTip.SetTip(voices,"默认每个实例显示一次开始与结束；展开各 Voice 的分配和释放记录。");
        panel.Children.Add(bar);
        var headings=new Grid{ColumnDefinitions=new ColumnDefinitions("108,92,280,*"),ColumnSpacing=8,Margin=new Thickness(12,0),Background=_p.Alternate};
        foreach(var (title,column) in new[]{("采集时间",0),("动作",1),("Cue / 对象",2),("内容",3)})
        {var label=Label(title,10,_p.Muted);Grid.SetColumn(label,column);headings.Children.Add(label);}
        Grid.SetRow(headings,1);panel.Children.Add(headings);
        _events = new ListBox { Background=_p.Canvas,BorderThickness=new Thickness(0),FontSize=11,Foreground=_p.Text,AutoScrollToSelectedItem=false };
        _events.SelectionChanged+=(_,_)=>{if(!_updatingLog && _events.SelectedItem is ListBoxItem{Tag:WireEvent item})SelectEvent(item);};
        _events.DoubleTapped+=(_,_)=>{if(_events.SelectedItem is ListBoxItem{Tag:WireEvent item})LocateLogEvent(item);};
        _events.AddHandler(PointerWheelChangedEvent,(_,_)=>{if(_logFollowing)ToggleLogFollow();},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Grid.SetRow(_events,2);panel.Children.Add(_events);
        _logStatus=Label("",10,_p.Muted);_logStatus.Margin=new Thickness(14,4);_logStatus.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(_logStatus,3);panel.Children.Add(_logStatus);
        _logSignature=""; return panel;
    }
    private void ToggleLogFollow()
    {
        _logFollowing=!_logFollowing;
        if(!_logFollowing)_logFrozen=_snapshot.ToArray();
        if(_logFollowButton!=null)_logFollowButton.Content=ButtonContent(_logFollowing?"暂停刷新":"继续实时");
        UpdateEventLog();
    }
    private void LocateLogEvent(WireEvent e)
    {
        if(ControlPresentation.Kinds.Contains(e.kind)){Navigate("AISAC",e,true);return;}
        var id=e.entity=="cue"?e.objectId:e.parentId;
        var owner=PlaybackPresentation.Group(_snapshot,Math.Max(e.time,_timeline.End)).FirstOrDefault(p=>p.Id==id);
        if(owner!=null && AssociationPresentation.Anchor(owner) is {} anchor)
        {
            Navigate("Timeline",anchor);
            _timeline.Live=false;_timeline.End=e.time+_timeline.Span*.35;UpdateEvents();_timeline.FocusEvent(anchor);UpdateRange();
        }
        else if(e.kind=="position")Navigate("Location",e,true);
        else SelectEvent(e);
    }
    private void UpdateEventLog()
    {
        if(_events==null || !(_diagnostics || _mode=="Logs"))return;
        var source=_logFollowing?_snapshot:_logFrozen;
        var owners=PlaybackPresentation.Group(source,double.MaxValue).ToDictionary(p=>p.Id);
        string Cue(WireEvent e)=>owners.GetValueOrDefault(e.entity=="cue"?e.objectId:e.parentId)?.Name??"";
        var matching=EventLogPresentation.Project(source,_logVoices).Where(e=>EventLogPresentation.Includes(e,_logKind)&&EventLogPresentation.Matches(e,_logQuery,Cue(e))).ToArray();
        var results=matching.TakeLast(_logLimit).Reverse().ToArray();
        if(_logStatus!=null) {
            var frozenKeys=_logFollowing?null:_logFrozen.Select(e=>(e.session,e.seq)).ToHashSet();
            var newCount=_logFollowing?0:_snapshot.Count(e=>!e.baseline && !frozenKeys!.Contains((e.session,e.seq)));
            _logStatus.Text=$"{(_logFollowing?"实时更新":"暂停刷新 · 仍在采集 · 新增 "+newCount+" 条")}  |  当前缓存：显示 {results.Length:N0} / 匹配 {matching.Length:N0}"+(matching.Length>results.Length?" · 显示已截断":"");
            ToolTip.SetTip(_logStatus,"搜索当前缓存全部记录；继续实时仅能补上仍在缓存中的记录。历史录制可通过打开日志检索。");
        }
        double timeOrigin=_session?.TimeOrigin??_timeline.TimeOrigin;
        var signature=(_p.Light?"L":"D")+timeOrigin+_logQuery+_logKind+_logVoices+string.Join(',',results.Select(e=>e.session+":"+e.seq));
        if(signature==_logSignature)return;_logSignature=signature;
        _updatingLog=true;
        try
        {
            var items=new List<ListBoxItem>();
            foreach(var e in results)
            {
                var line=new Grid{ColumnDefinitions=new ColumnDefinitions("108,92,280,*"),ColumnSpacing=8,Margin=new Thickness(0,2)};
                line.Children.Add(Label((e.estimatedTime?"约 ":"")+TimelineControl.TimeLabel(e.time-timeOrigin),11,_p.Muted));
                var action=Label(EventLogPresentation.Action(e),11,ControlPresentation.Kinds.Contains(e.kind)?_p.Control(e.kind):e.kind is "gap" or "error"?_p.Error:_p.Voice);Grid.SetColumn(action,1);line.Children.Add(action);
                var logOwner=owners.GetValueOrDefault(e.entity=="cue"?e.objectId:e.parentId);
                var name=Label(logOwner!=null?PlaybackLabel(logOwner):e.name,13);name.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(name,2);line.Children.Add(name);
                string detail=EventLogPresentation.Detail(e);
                if(_logVoices&&e.entity=="voice")detail="Voice · "+detail;
                var owner=owners.GetValueOrDefault(e.entity=="cue"?e.objectId:e.parentId);
                if(owner!=null && EventSemantics.IsPlaybackEnd(e)) {
                    detail=owner.EndReason.Length>0?owner.EndReasonLabel:"播放实例已结束";
                    if(owner.DurationAt(e.time) is {} duration)detail+=$" · {duration:0.000} 秒";
                }
                if(ControlPresentation.Kinds.Contains(e.kind)&&e.kind!="sequence")detail=e.name+" = "+detail;
                var value=Label(detail,11,e.kind=="sequence"?_p.SequenceTag(e.name):_p.Muted);value.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetColumn(value,3);line.Children.Add(value);
                var item=new ListBoxItem {Tag=e,Content=line,Padding=new Thickness(12,4)};
                ToolTip.SetTip(item,$"{e.name}\n{detail}\n双击定位，单击查看详情");items.Add(item);
            }
            if(items.Count==0)items.Add(new ListBoxItem{Content=Label("没有匹配记录；可以调整搜索词或日志分类",12,_p.Muted),IsEnabled=false});
            _events.ItemsSource=items;
            _events.SelectedItem=items.FirstOrDefault(i=>i.Tag is WireEvent e && _selected is {} selected && e.session==selected.session && e.seq==selected.seq);
        }
        finally{_updatingLog=false;}
    }
}
