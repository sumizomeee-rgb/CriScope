using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Controls.Templates;
using Avalonia.Controls.Presenters;
using Avalonia.Data;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private string _relatedPlayback = "", _relatedSession = "";
    private string _logQuery = "", _logKind = "全部", _logSignature = "";
    private bool _logFollowing = true, _updatingLog, _logVoices;
    private WireEvent[] _logFrozen = [];
    private int _logLimit = 1000;
    private TextBlock? _logStatus;
    private Button? _logFollowButton;
    private TextBox? _logSearch;
    private Control BuildEventLog()
    {
        if(_relatedSession!=_session?.Id)_relatedPlayback="";
        var panel = new Grid { RowDefinitions = new RowDefinitions("Auto,26,*,26") };
        var bar = new WrapPanel {Orientation=Orientation.Horizontal,Margin=new Thickness(12,5)};
        var search = _logSearch = new TextBox { Width=280, Text=_logQuery, PlaceholderText="搜索日志：开始播放 CueName、实例结束、AISAC…",FontSize=12 };
        search.TextChanged+=(_,_)=>{_logQuery=search.Text??""; UpdateEventLog();}; bar.Children.Add(search);
        var kind = new ComboBox {Width=150,ItemsSource=new[]{"全部","播放相关（全部）","请求播放","接入时已在播放","开始播放","请求停止","播放结束","控制","回调","异常与连接","原始协议（高级）"},SelectedItem=_logKind,FontSize=11,HorizontalAlignment=HorizontalAlignment.Stretch};
        kind.SelectionChanged+=(_,_)=>{_logKind=kind.SelectedItem as string??"全部";UpdateEventLog();}; Grid.SetColumn(kind,1);bar.Children.Add(kind);
        _logFollowButton=Action(_logFollowing?"暂停刷新":"继续实时",ToggleLogFollow); Grid.SetColumn(_logFollowButton,2);bar.Children.Add(_logFollowButton);
        ToolTip.SetTip(search,_relatedPlayback.Length>0?"仅关联实例；点击关联筛选 × 可退出":"搜索当前缓存中的事件");
        var earlier=Action("更早记录",()=>{_logLimit=Math.Min(20000,_logLimit+1000);UpdateEventLog();});Grid.SetColumn(earlier,3);bar.Children.Add(earlier);
        var voices=new CheckBox {Content="Voice 明细",IsChecked=_logVoices,FontSize=11,VerticalAlignment=VerticalAlignment.Center};
        voices.IsCheckedChanged+=(_,_)=>{_logVoices=voices.IsChecked==true;UpdateEventLog();};Grid.SetColumn(voices,4);bar.Children.Add(voices);
        ToolTip.SetTip(voices,"默认每个实例显示一次开始与结束；展开各 Voice 的分配和释放记录。");
        var clearRelated=Action("关联筛选 ×",()=>{_relatedPlayback="";Build();RestoreView();Refresh();});
        clearRelated.IsVisible=_relatedPlayback.Length>0;Grid.SetColumn(clearRelated,5);bar.Children.Add(clearRelated);
        foreach(var control in bar.Children){control.Margin=new Thickness(0,0,8,4);control.MinHeight=32;}
        panel.Children.Add(bar);
        var headings=new Grid{ColumnDefinitions=new ColumnDefinitions("108,132,240,*,32"),ColumnSpacing=8,Margin=new Thickness(12,0),Background=_p.Alternate};
        foreach(var (title,column) in new[]{("发生时间",0),("动作",1),("Cue / 对象",2),("内容",3)})
        {var label=Label(title,10,_p.Muted);Grid.SetColumn(label,column);headings.Children.Add(label);}
        Grid.SetRow(headings,1);panel.Children.Add(headings);
        _events = new ListBox { Background=_p.Canvas,BorderThickness=new Thickness(0),FontSize=11,Foreground=_p.Text,AutoScrollToSelectedItem=false };
        var selectedStyle=new Style(selector=>selector.OfType<ListBoxItem>().Class(":selected"));
        selectedStyle.Setters.Add(new Setter(BackgroundProperty,_p.Hover));
        selectedStyle.Setters.Add(new Setter(BorderBrushProperty,_p.Semantic(SemanticColor.Selected)));
        selectedStyle.Setters.Add(new Setter(BorderThicknessProperty,new Thickness(3,0,0,0)));
        _events.Styles.Add(selectedStyle);
        _events.SelectionChanged+=(_,_)=>{if(!_updatingLog && _events.SelectedItem is ListBoxItem{Tag:WireEvent item})SelectEvent(item);};
        _events.DoubleTapped+=(_,_)=>{if(_events.SelectedItem is ListBoxItem{Tag:WireEvent item})LocateLogEvent(item);};
        _events.AddHandler(PointerWheelChangedEvent,(_,e)=>{if(_logFollowing && e.Delta.Y<0)ToggleLogFollow();},Avalonia.Interactivity.RoutingStrategies.Tunnel);
        Grid.SetRow(_events,2);panel.Children.Add(_events);
        _logStatus=Label("",10,_p.Muted);_logStatus.Margin=new Thickness(14,4);_logStatus.TextTrimming=TextTrimming.CharacterEllipsis;Grid.SetRow(_logStatus,3);panel.Children.Add(_logStatus);
        _logSignature=""; return panel;
    }
    private void ShowRelatedLog(string playbackId)
    {
        RememberPosition();_relatedPlayback=playbackId;_relatedSession=_session?.Id??"";_logQuery="";_logKind="全部";_logFollowing=true;_mode="Logs";
        Build();RestoreView();Refresh();
        if(_logStatus!=null)_logStatus.Text="关联实例记录 · "+_logStatus.Text;
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
        var relatedOwner=owners.GetValueOrDefault(_relatedPlayback);
        var relatedControls=relatedOwner==null?null:PlaybackPresentation.ControlsFor(relatedOwner,source,double.MaxValue);
        var relatedKeys=relatedControls?.BeforeStart.Concat(relatedControls.DuringPlayback).Select(e=>(e.session,e.seq)).ToHashSet();
        var matching=EventLogPresentation.Project(source,_logVoices).Where(e=>(_relatedPlayback.Length==0 || e.objectId==_relatedPlayback || e.parentId==_relatedPlayback || relatedKeys?.Contains((e.session,e.seq))==true) &&EventLogPresentation.Includes(e,_logKind)&&EventLogPresentation.Matches(e,_logQuery,Cue(e))).ToArray();
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
                var line=new Grid{ColumnDefinitions=new ColumnDefinitions("108,132,240,*,32"),ColumnSpacing=8,Margin=new Thickness(0,2)};
                line.Children.Add(Label(WallTime(e.time,_session),11,_p.Muted));
                var badge=VisualLanguage.EventBadge(e,_logVoices&&e.entity=="voice"?(e.kind=="play"?"Voice 分配":"Voice 释放"):EventLogPresentation.Action(e),_p);
                Grid.SetColumn(badge,1);line.Children.Add(badge);
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
                var locate=IconAction("定位此事件",IconKind.Locate,()=>LocateLogEvent(e));locate.Width=32;locate.Height=32;Grid.SetColumn(locate,4);line.Children.Add(locate);
                var item=new ListBoxItem {Tag=e,Content=line,Padding=new Thickness(12,0)};
                item.Template=new FuncControlTemplate<ListBoxItem>((owner,scope)=>{
                    var presenter=new ContentPresenter();presenter.Bind(ContentPresenter.ContentProperty,new Binding("Content"){Source=owner});
                    var border=new Border {Child=presenter};
                    border.Bind(Border.BackgroundProperty,new Binding("Background"){Source=owner});
                    border.Bind(Border.BorderBrushProperty,new Binding("BorderBrush"){Source=owner});
                    border.Bind(Border.BorderThicknessProperty,new Binding("BorderThickness"){Source=owner});
                    border.Bind(Border.PaddingProperty,new Binding("Padding"){Source=owner});return border;
                });
                ToolTip.SetTip(item,$"{e.name}\n{detail}\n时间由接收锚点换算；详情中可查看原始时钟。\n双击定位，单击查看详情");items.Add(item);
            }
            if(items.Count==0)items.Add(new ListBoxItem{Content=Label("没有匹配记录；可以调整搜索词或日志分类",12,_p.Muted),IsEnabled=false});
            _events.ItemsSource=items;
            _events.SelectedItem=items.FirstOrDefault(i=>i.Tag is WireEvent e && _selected is {} selected && e.session==selected.session && e.seq==selected.seq);
        }
        finally{_updatingLog=false;}
    }
}
