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
    private void AddAssociations(StackPanel details, WireEvent selected, PlaybackGroup? playback)
    {
        double time = ContextTime;
        var links = new StackPanel { Spacing = 5 };
        void Link(string text, string mode, WireEvent target, bool eventTime = false)
        {
            var b = Action(text, () => Navigate(mode, target, eventTime));
            b.Tag = $"navigate:{mode}:{target.session}:{target.seq}:{eventTime}";
            b.HorizontalAlignment = HorizontalAlignment.Stretch; b.HorizontalContentAlignment = HorizontalAlignment.Left;
            links.Children.Add(b);
        }
        var groups = PlaybackPresentation.Group(_snapshot, _timeline.End);
        var owners = selected.kind == "position" && selected.entity == "source"
            ? AssociationPresentation.SourcePlaybacks(_snapshot, selected, time)
            : playback != null ? new[] { playback }
            : selected.kind is "aisac" or "selector" ? AssociationPresentation.PlayerPlaybacks(_snapshot, selected.objectId, time) : [];
        foreach(var owner in owners)
        {
            if((_mode!="Timeline" || playback == null) && AssociationPresentation.Anchor(owner) is {} anchor)
                Link("声音轨道 · " + owner.Name + " · " + (owner.RequestAt is {} at ? _timeline.Stamp(at) : "开始未记录"), "Timeline", anchor, _mode == "Logs");
        }
        if(playback != null)
        {
            var playerLabel = _timeline.ControlLabels.Get("Player", System.Text.Json.JsonSerializer.Serialize(new[] { playback.Request?.session ?? "", playback.PlayerId }));
            if(playback.PlayerId.Length>0) links.Children.Add(Label(playerLabel + " · 关联控制",11,_p.Muted));
            var controls = PlaybackPresentation.ControlsFor(playback, _snapshot, _timeline.End);
            foreach(var category in AssociationPresentation.Categories(_snapshot,playback.Id,_timeline.End))
            {
                var b = Action("Category · " + category.name, () => FilterCategory(category)); b.Tag="category-filter:"+category.objectId;
                ToolTip.SetTip(b,"筛选此 Category 的声音轨道"); links.Children.Add(b);
            }
        }
        if(selected.kind != "position")
        {
            var sources = AssociationPresentation.SourcesFor(_snapshot, owners.Select(p=>p.Id), time);
            foreach(var source in sources)
                Link(sources.Length == 1 ? "在空间中查看" : $"音源 · X {source.x:0.#} / Z {source.z:0.#}", "Location", source);
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
