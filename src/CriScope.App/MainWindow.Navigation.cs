using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private readonly Stack<(Session? Session, string Mode, ViewState View)> _navigation = new();
    private string _categoryId = "", _categoryName = "";
    private Button? _backButton, _categoryChip;
    private WireEvent? _filterException;
    private double ContextTime => _selected is { } e && (ControlPresentation.Kinds.Contains(e.kind) || _mode == "Logs") ? e.time : _timeline.End;

    private string PlaybackLabel(PlaybackGroup playback) => playback.Name+" · "+_timeline.ControlLabels.Get("播放实例",System.Text.Json.JsonSerializer.Serialize(new[]{playback.Request?.session??playback.Voices.SelectMany(v=>v).FirstOrDefault()?.session??"",playback.Id}));
    private void RememberPosition()
    {
        SaveView();
        if (_session != null && _views.TryGetValue(_session, out var view)) _navigation.Push((_session, _mode, view));
    }
    private void Back()
    {
        if (!_navigation.TryPop(out var prior) || prior.Session == null) return;
        SaveView(); _session = prior.Session; _mode = prior.Mode; _views[_session] = prior.View;
        Build(); RestoreView(); _lastTotal = -1; Refresh(); Inspector();
    }
    private void Navigate(string mode, WireEvent item, bool atEvent = false)
    {
        var time = ContextTime;
        bool eventContext = _selected != null && (ControlPresentation.Kinds.Contains(_selected.kind) || _mode == "Logs");
        RememberPosition();
        _mode = mode; SaveView(); Build(); RestoreView();
        if (atEvent || time < _timeline.Start || time > _timeline.End)
        {
            _timeline.Live = false; _timeline.End = atEvent ? item.time + _timeline.Span * .35 : time;
        }
        // Spatial state must be evaluated at the selected callback/log event, never at future samples.
        if((mode == "Location" || eventContext && !atEvent) && time != _timeline.End) { _timeline.Live = false; _timeline.End = time; }
        if(mode == "AISAC") _timeline.ControlKinds.Add(item.kind);
        _filterException = item;
        UpdateEvents();
        SelectEvent(item); _timeline.FocusEvent(item);
        UpdateRange();
    }
    private void FilterCategory(WireEvent category)
    {
        RememberPosition(); _categoryId = category.objectId; _categoryName = category.name;
        _mode = "Timeline"; SaveView(); Build(); RestoreView(); UpdateEvents(); Refresh();
    }
    private NavigationButton NavigationAction(string caption,Action action,string relatedName="")
    {
        var button=new NavigationButton {Caption=caption,RelatedName=relatedName,Content=NavigationContent(caption),Height=32,
            Padding=new Thickness(8,0),Background=Brushes.Transparent,Foreground=_p.Text,BorderThickness=new Thickness(0),
            HorizontalAlignment=HorizontalAlignment.Left,VerticalContentAlignment=VerticalAlignment.Center};
        ToolTip.SetTip(button,caption+(relatedName.Length>0?" · "+relatedName:""));
        button.PointerEntered+=(_,_)=>button.Background=_p.Hover;
        button.PointerExited+=(_,_)=>button.Background=Brushes.Transparent;
        button.Click+=(_,_)=>action();return button;
    }
    private void AddAssociations(StackPanel details, WireEvent selected, PlaybackGroup? playback)
    {
        double time = ContextTime;
        var links = new StackPanel { Tag = "association-links", Spacing = 4 };
        void Link(string text, string mode, WireEvent target, bool eventTime = false)
        {
            var b = NavigationAction(mode=="Location"?"定位空间音源":"定位播放实例", () => Navigate(mode, target, eventTime),text);
            b.Tag = $"navigate:{mode}:{target.session}:{target.seq}:{eventTime}";
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left;
            links.Children.Add(b);
        }
        var groups = PlaybackPresentation.Group(_snapshot, _timeline.End);
        var owners = selected.kind == "position" && selected.entity == "source"
            ? AssociationPresentation.SourcePlaybacks(_snapshot, selected, time)
            : playback != null ? new[] { playback }
            : selected.kind is "aisac" or "selector" ? SettingRelationship(selected).Owners : [];
        foreach(var owner in owners)
        {
            if(!(_mode=="Logs" && selected.entity=="cue") && !(playback==null && selected.kind is "aisac" or "selector") && (_mode!="Timeline" || playback == null) && AssociationPresentation.Anchor(owner) is {} anchor)
                Link(PlaybackLabel(owner), "Timeline", anchor, _mode == "Logs");
        }
        if(playback != null)
        {
            var playerLabel = _timeline.ControlLabels.Get("Player", System.Text.Json.JsonSerializer.Serialize(new[] { playback.Request?.session ?? "", playback.PlayerId }));

            var controls = PlaybackPresentation.ControlsFor(playback, _snapshot, _timeline.End);

        }
        if(selected.kind != "position")
        {
            var sourceEvidence = selected.kind is "aisac" or "selector"
                ? AssociationPresentation.EvidenceThrough(_snapshot, selected) : _snapshot;
            var sources = AssociationPresentation.SourcesFor(sourceEvidence, owners.Select(p=>p.Id), time);
            foreach(var source in sources)
                Link(sources.Length == 1 ? "" : $"音源 · X {source.x:0.#} / Z {source.z:0.#}", "Location", source);
            if(owners.Length>0 && sources.Length==0)
            {
                var unavailable=Label("空间关联未提供",11,_p.Muted);
                ToolTip.SetTip(unavailable,"当前时刻没有明确的 3D Source 关联；不据此判定为 2D 音效。"); links.Children.Add(unavailable);
            }
        }
        if(links.Children.Count>0)
        {
            details.Children.Add(Label("关联与定位",12,_p.Muted));
            details.Children.Add(links);
        }
    }
}
