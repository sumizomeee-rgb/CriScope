using Avalonia.Threading;
using CriScope.Core;

namespace CriScope.App;

public sealed partial class MainWindow
{
    private readonly Dictionary<(string Session, long Sequence), SettingOwnerResolution> _settingEvidence = [];
    private readonly HashSet<(string Session, long Sequence)> _settingEvidenceLoading = [];
    private readonly CancellationTokenSource _settingEvidenceCancellation = new();

    private SettingOwnerResolution SettingRelationship(WireEvent setting)
    {
        if (setting.objectId.StartsWith("category:", StringComparison.OrdinalIgnoreCase) ||
            setting.objectId.StartsWith("category-index:", StringComparison.OrdinalIgnoreCase))
            return new([], RelationshipEvidenceStatus.ObservedNoOpenPlayback, "category", "Category 级设置", null);
        var key = (setting.session, setting.seq);
        if (_settingEvidence.TryGetValue(key, out var cached)) return cached;

        var session = _session?.Id == setting.session ? _session :
            _collector.Sessions.FirstOrDefault(s => s.Id == setting.session);
        if (session == null)
            return new([], RelationshipEvidenceStatus.Unavailable, "live-window", "关联会话未提供", null);

        var result = AssociationPresentation.ResolveAvailable(session, setting, _snapshot);
        _settingEvidence[key] = result;
        if (_settingEvidence.Count > Session.MaxViewStates)
            _settingEvidence.Remove(_settingEvidence.Keys.First());
        if (result.Source == "live-window" &&
            (result.Status is RelationshipEvidenceStatus.Unavailable or RelationshipEvidenceStatus.Partial) &&
            session.RecordingPaths.Length > 0)
            LoadSettingRelationshipFromDisk(session, setting, key);
        return result;
    }

    private (PlaybackGroup[] Owners, string EmptyLabel) TimelineSettingRelationship(WireEvent setting)
    {
        var result = SettingRelationship(setting);
        if (result.Source == "category") return ([], "Category 级设置");
        if (result.Owners.Length > 0) return (result.Owners, "");
        if (result.Status == RelationshipEvidenceStatus.ObservedNoOpenPlayback)
            return ([], result.LastPlayedRequest is { } previous
                ? "此前最近播放 · " + previous.name + "（已结束）" : "写入时未观察到播放实例");
        if (result.LastPlayedRequest is { } recent)
            return ([], "此前最近播放 · " + recent.name + "（关联待确认）");
        return ([], _settingEvidenceLoading.Contains((setting.session, setting.seq))
            ? "正在查询历史日志…" :
            result.Status == RelationshipEvidenceStatus.Partial ? "写入时关联证据不完整" : "历史关联证据不足");
    }

    private async void LoadSettingRelationshipFromDisk(Session session, WireEvent setting,
        (string Session, long Sequence) key)
    {
        if (!_settingEvidenceLoading.Add(key)) return;
        try
        {
            var result = await Task.Run(() => AssociationPresentation.ResolveFromRecording(
                session, setting, _settingEvidenceCancellation.Token), _settingEvidenceCancellation.Token);
            if (_settingEvidenceCancellation.IsCancellationRequested) return;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _settingEvidence[key] = result;
                _timeline.InvalidateSettingEvidence();
                if (_selected is { } selected && selected.session == key.Session && selected.seq == key.Sequence)
                    Inspector();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (_settingEvidenceCancellation.IsCancellationRequested) return;
            _settingEvidence[key] = new([], RelationshipEvidenceStatus.Unavailable,
                "recording", "历史日志查询失败：" + ex.Message, null);
            _timeline.InvalidateSettingEvidence();
            if (_selected is { } selected && selected.session == key.Session && selected.seq == key.Sequence)
                Inspector();
        }
        finally { _settingEvidenceLoading.Remove(key); }
    }
}
