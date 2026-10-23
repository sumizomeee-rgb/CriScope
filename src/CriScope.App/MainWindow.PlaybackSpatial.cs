using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<(string Session, string Playback, long Start), PlaybackSpatialReport> _spatialReports = [];
    private readonly HashSet<(string Session, string Playback, long Start)> _spatialLoading = [];
    private readonly Dictionary<(string Session, string Playback, long Start), string> _spatialReadErrors = [];

    private static (string Session, string Playback, long Start) SpatialKey(PlaybackGroup playback)
    {
        var anchor = AssociationPresentation.Anchor(playback);
        return (anchor?.session ?? "", playback.Id, playback.Request?.seq ?? 0);
    }
    private PlaybackSpatialReport SpatialReport(PlaybackGroup playback, WireEvent? configuration)
    {
        var key = SpatialKey(playback);
        if (_spatialReports.TryGetValue(key, out var saved))
        {
            if(configuration!=null)_spatialReports[key]=saved=PlaybackSpatialPresentation.UpdateConfiguration(saved,configuration);
            return saved;
        }
        var report = PlaybackSpatialPresentation.Resolve(_snapshot, playback, _timeline.End, configuration);
        var session = _collector.Sessions.FirstOrDefault(s => s.Id == key.Session);
        // An ended selection is stable even after the live event window evicts it.
        if (playback.End != null || playback.EndedAt != null)
        {
            _spatialReports[key] = report;
            if (session is { IsReplay: false } && session.RecordingPaths.Length > 0)
                ReadSpatialReport(session, playback, _timeline.End, configuration, key);
            if (_spatialReports.Count > 256) _spatialReports.Remove(_spatialReports.Keys.First());
        }
        return report;
    }
    private async void ReadSpatialReport(Session session, PlaybackGroup playback, double through, WireEvent? configuration,
        (string Session, string Playback, long Start) key)
    {
        if (!_spatialLoading.Add(key)) return;
        try
        {
            var report = await Task.Run(() => PlaybackSpatialPresentation.FromRecording(session, playback, through,
                configuration, _settingEvidenceCancellation.Token), _settingEvidenceCancellation.Token);
            if (_settingEvidenceCancellation.IsCancellationRequested) return;
            _spatialReports[key] = report;
            if (_selected is { } selected && selected.session == key.Session &&
                (selected.objectId == key.Playback || selected.parentId == key.Playback)) Inspector();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _spatialReadErrors[key] = "历史补全失败：" + ex.Message;
        }
        finally
        {
            _spatialLoading.Remove(key);
            if (!_settingEvidenceCancellation.IsCancellationRequested && _selected?.session == key.Session) Inspector();
        }
    }
    private PlaybackGroup? ArchivedSpatialPlayback(WireEvent selected) => _spatialReports.Values
        .FirstOrDefault(r => SpatialKey(r.Playback).Session == selected.session &&
            r.Playback.Id == (selected.entity == "cue" ? selected.objectId : selected.parentId))?.Playback;

    private void AddPlaybackSpatial(StackPanel details, PlaybackGroup playback, WireEvent? configuration)
    {
        var report = SpatialReport(playback, configuration);
        var key = SpatialKey(playback);
        var section = new StackPanel { Tag = "playback-spatial:" + key, Spacing = 5, Margin = new Thickness(0, 10, 0, 4) };
        section.Children.Add(InspectorSection("播放时空间状态", "spatial-heading"));
        var target=section;
        void Row(string title, string value, string identity="")
        {
            var row = new Grid { Tag = "row:spatial:" + title + ":" + identity, ColumnDefinitions = new ColumnDefinitions(UiMetrics.InspectorColumns),
                ColumnSpacing = UiMetrics.InspectorFieldGap, MinHeight = UiMetrics.InspectorRowHeight };
            row.Children.Add(InspectorValue(title, UiMetrics.CaptionSize, _p.Muted));
            var text = InspectorValue(value, UiMetrics.BodySize); text.Tag = "spatial:" + title;
            Grid.SetColumn(text, 1); row.Children.Add(text); target.Children.Add(row);
        }
        Row("Pan Type · Cue", report.CuePan);
        Row("距离范围", report.Range);
        ToolTip.SetTip(section.Children.Last(), "来自 Cue 的配置；运行时覆盖和实际衰减增益另需明确记录。坐标单位不自动视为米。");
        foreach (var moment in new[] { report.Last, report.Start })
        {
            target=new StackPanel {Spacing=5};
            section.Children.Add(new Expander {Tag="spatial-phase:"+moment.Phase,Header=moment.Phase+" · "+WallTime(moment.At,_session),
                Content=target,IsExpanded=moment!=report.Start,FontSize=12});
            if (moment.Sources.Length == 0) Row(moment.Phase + "音源", "未记录关联位置");
            foreach (var source in moment.Sources)
            {
                var group = new StackPanel { Spacing = 9, Margin = new Thickness(10) };
                var sourceTitle = moment.Sources.Length == 1 ? "音源" : "音源 " + source.objectId;
                group.Children.Add(SpatialObject(sourceTitle, source, moment.At));
                foreach (var distance in moment.Distances.Where(d => ReferenceEquals(d.Source, source)))
                {
                    group.Children.Add(new Border { Height = 1, Background = _p.Border });
                    group.Children.Add(SpatialObject(distance.Listener.entity == "distance-listener" ? "衰减监听点" : "基础监听器",
                        distance.Listener, moment.At, distance));
                }
                target.Children.Add(new Grid { Tag = "spatial-source:" + moment.Phase + ":" + source.objectId,
                    Background = _p.Alternate, Children = { group } });
            }
            if (moment.Sources.Length > 0 && moment.Listeners.Length == 0) Row("监听点", "该时刻未记录", moment.Phase);
            if (moment.PlayerPan != "未记录") Row("Player Pan 设置", moment.PlayerPan + "（请求记录）", moment.Phase);
            var view = NavigationAction("查看空间", () =>
            {
                var latest=SpatialReport(playback, CueConfigurationFor(playback));
                ShowPlaybackSpace(latest, moment.Phase=="开始时" ? latest.Start : latest.Last);
            });
            view.Tag = "spatial-history:" + moment.Phase + ":" + moment.Sources.Length; view.IsEnabled = moment.Sources.Length > 0;
            target.Children.Insert(0,view);
        }
        var evidence = _spatialLoading.Contains(key) ? "正在从录制补全…" :
            _spatialReadErrors.GetValueOrDefault(key, report.Evidence);
        var note = InspectorValue(evidence, 11, _p.Muted); note.Tag = "spatial-evidence"; section.Children.Add(note);
        details.Children.Add(section);
    }
    private StackPanel SpatialObject(string title, WireEvent point, double at, SpatialDistance? distance = null)
    {
        var listener = point.entity != "source";
        var color = listener ? _p.Listener : _p.Voice;
        var content = new StackPanel { Tag = "spatial-object:" + point.entity + ":" + point.objectId, Spacing = 4 };
        var heading = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 10 };
        var identity = new Grid { ColumnDefinitions = new ColumnDefinitions("16,*"), ColumnSpacing = 6 };
        identity.Children.Add(VisualLanguage.Glyph(listener ? IconKind.Listener : IconKind.Source, color, 15));
        var name = InspectorValue(title, 12, color); Grid.SetColumn(name, 1); identity.Children.Add(name);
        ToolTip.SetTip(identity, title + " · " + point.objectId);
        heading.Children.Add(identity);
        if (distance != null)
        {
            var measured = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
            measured.Children.Add(InspectorValue("3D", 11, _p.Muted));
            var number = InspectorValue($"{distance.Value:0.###}", 16); number.FontWeight = FontWeight.Medium;
            number.Tag = "spatial-distance"; measured.Children.Add(number);
            measured.Children.Add(InspectorValue("坐标单位", 11, _p.Muted));
            ToolTip.SetTip(measured, "3D 直线距离，包含高度差；单位来自游戏坐标。");
            Grid.SetColumn(measured, 1); heading.Children.Add(measured);
        }
        content.Children.Add(heading);
        var position = InspectorValue(Coordinates(point), 12, _p.Muted); position.Tag = "spatial-position";
        content.Children.Add(position);
        var metadata = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        var sample = InspectorValue("位置样本 · " + PlaybackSpatialPresentation.SampleAge(point, at), 11, _p.Muted);
        sample.Tag = "spatial-sample"; ToolTip.SetTip(sample, SampleLabel(point, at)); metadata.Children.Add(sample);
        if (distance != null)
        {
            var binding = InspectorValue(distance.Bound ? "绑定已记录" : "参考距离", 11, _p.Muted);
            binding.Tag = "spatial-binding";
            ToolTip.SetTip(binding, distance.Bound ? "已记录音源与此监听器的绑定。" : "未记录音源与此监听器的绑定；此距离仅供参考。");
            Grid.SetColumn(binding, 1); metadata.Children.Add(binding);
        }
        content.Children.Add(metadata);
        return content;
    }
    private static string Coordinates(WireEvent e) => $"X {e.x:0.###} · Y {e.y:0.###} · Z {e.z:0.###}";
    private string SampleLabel(WireEvent e, double at)
    {
        var sample = PlaybackSpatialPresentation.SampleTime(e);
        return "采样时间 · " + WallTime(sample, _session) + $"\n早于该观测时刻 {Math.Max(0, at - sample) * 1000:0.###} ms";
    }
    private void ShowPlaybackSpace(PlaybackSpatialReport report, PlaybackSpatialMoment moment)
    {
        RememberPosition();
        _mode = "Location"; Build(); RestoreView();
        _timeline.Live = false; _timeline.End = moment.At;
        _timeline.SpatialHistory = new(report.Playback.Id, report.Playback.Name, moment);
        _timeline.ShowSources = true; _timeline.ShowBaseListeners = true; _timeline.ShowDistanceListeners = true;
        _timeline.FitSpatial();
        _selected = AssociationPresentation.Anchor(report.Playback); _timeline.Selected = _selected;
        UpdateEvents(); Refresh(); Inspector();
    }
}
