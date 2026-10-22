using Avalonia.Controls;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private void AddSourceCueDetails(StackPanel details, WireEvent source, Action<string, string> field)
    {
        if (_timeline.SpatialHistory is { } history)
        {
            field("历史 Cue", history.Name);
            return;
        }
        var index = _timeline.SpatialCues();
        var state = index.For(source);
        if (state.Current.Length > 0) field("当前关联 Cue", string.Join(" · ", state.Current.Select(c => c.Name)));
        field("上次播放 Cue", state.Last?.Name ?? "未记录");
        if (state.Last is not { } cue) return;
        field("上次" + cue.TimeLabel, WallTime(cue.Time, _session));
        if (index.Playback(source.session, cue.PlaybackId) is { } playback && AssociationPresentation.Anchor(playback) is { } anchor)
        {
            var button = NavigationAction("查看上次播放", () => Navigate("Timeline", anchor, true), cue.Name);
            button.Tag = "last-source-playback:" + source.session + ":" + cue.PlaybackId;
            details.Children.Add(button);
        }
    }
}
