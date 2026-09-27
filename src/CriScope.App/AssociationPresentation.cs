using System.Text.Json;
using CriScope.Core;

namespace CriScope.App;

public sealed record SettingOwnerResolution(
    PlaybackGroup[] Owners,
    RelationshipEvidenceStatus Status,
    string Source,
    string Reason,
    WireEvent? LastPlayedRequest);

/// <summary>Explicit identities only. Names are labels, never relationship keys.</summary>
public static class AssociationPresentation
{
    public static WireEvent? Anchor(PlaybackGroup p) => p.Request ?? p.Voices.SelectMany(v => v).FirstOrDefault() ?? p.End;
    public static bool ActiveAt(PlaybackGroup p, double time) => Anchor(p) is { } a && a.time <= time && (p.InstanceEndedAt == null || p.InstanceEndedAt >= time) && !p.HasEvidenceGap;
    public static PlaybackGroup[] PlayerPlaybacks(IEnumerable<WireEvent> events, string player, double time) => player.Length == 0 ? [] :
        PlaybackPresentation.Group(events, time).Where(p => p.PlayerId == player && ActiveAt(p, time)).ToArray();
    private static bool SeenBy(WireEvent evidence, WireEvent marker) =>
        evidence.session == marker.session && evidence.seq > 0 && marker.seq > 0
            ? evidence.seq <= marker.seq
            : evidence.time < marker.time || evidence.time == marker.time && evidence.seq <= marker.seq;
    public static WireEvent[] EvidenceThrough(IEnumerable<WireEvent> events, WireEvent marker) =>
        events.Where(e => e.session == marker.session && SeenBy(e, marker)).ToArray();
    private static WireEvent[] SettingEvidence(IEnumerable<WireEvent> events, WireEvent setting)
    {
        var through = EvidenceThrough(events, setting);
        var boundary = through.Where(e => e.kind == "gap" || e.entity == "capture-segment" ||
            e.kind == "log" && e.objectId == setting.objectId &&
            e.name is ("ExPlayer_Create" or "ExPlayer_Create_Success" or "ExPlayer_Destroy"))
            .MaxBy(e => e.seq);
        return boundary == null ? through : through.Where(e => e.seq > boundary.seq).ToArray();
    }
    // A setting belongs to a reusable Player handle. Only evidence seen through this
    // exact event can establish which Playback instances existed when it was written.
    public static PlaybackGroup[] PlayerPlaybacksAtSetting(IEnumerable<WireEvent> events, WireEvent setting)
    {
        var source = SettingEvidence(events, setting);
        double through = source.Select(e => e.time).DefaultIfEmpty(setting.time).Max();
        var groups = PlaybackPresentation.Group(source, Math.Max(setting.time, through));
        return PlayerPlaybacksAtSetting(groups, setting);
    }
    public static PlaybackGroup[] PlayerPlaybacksAtSetting(IEnumerable<PlaybackGroup> groups, WireEvent setting)
    {
        if (setting.kind is not ("aisac" or "selector") || setting.objectId.Length == 0 ||
            setting.objectId.StartsWith("category:", StringComparison.OrdinalIgnoreCase) ||
            setting.objectId.StartsWith("category-index:", StringComparison.OrdinalIgnoreCase)) return [];
        return groups.Where(p => p.PlayerId == setting.objectId && p.Request is { } request &&
                request.session == setting.session && request.epoch == setting.epoch && SeenBy(request, setting) &&
                (p.End == null || !SeenBy(p.End, setting)) &&
                (p.Discontinuity == null || !SeenBy(p.Discontinuity, setting)))
            .OrderByDescending(p => p.Request!.time).ThenByDescending(p => p.Request!.seq).ToArray();
    }

    /// <summary>Fast path for redraws. This never performs file I/O.</summary>
    public static SettingOwnerResolution ResolveAvailable(Session session, WireEvent setting,
        IEnumerable<WireEvent> live)
    {
        if (session.TryGetSettingRelationship(setting, out var frozen))
            return Materialize(frozen, "capture-index");

        var evidence = SettingEvidence(live, setting);
        if (!evidence.Any(e => e.seq == setting.seq && e.kind == setting.kind && e.objectId == setting.objectId))
            return new([], RelationshipEvidenceStatus.Unavailable, "live-window",
                "设置事件已离开内存窗口，需要读取物理日志", null);

        var owners = PlayerPlaybacksAtSetting(evidence, setting);
        bool completeWindow = evidence.Length > 0 && evidence.Min(e => e.seq) == 1 &&
            !evidence.Any(e => e.kind == "gap" || e.entity == "capture-segment");
        var status = completeWindow
            ? owners.Length > 0 ? RelationshipEvidenceStatus.Observed : RelationshipEvidenceStatus.ObservedNoOpenPlayback
            : owners.Length > 0 ? RelationshipEvidenceStatus.Partial : RelationshipEvidenceStatus.Unavailable;
        var recent = evidence.Where(e => e.kind == "request" && e.entity == "cue" &&
                                         e.parentId == setting.objectId && e.epoch == setting.epoch)
            .OrderByDescending(e => e.seq).FirstOrDefault();
        return new(owners, status, "live-window",
            completeWindow ? owners.Length > 0 ? "写入时已观测到关联播放实例" :
                "写入时未观测到播放实例；无法证明 Player 空闲" :
                "内存窗口的历史证据不完整；应读取物理日志确认", recent);
    }

    /// <summary>Disk path for an explicitly selected older setting; call off the UI thread.</summary>
    public static SettingOwnerResolution ResolveFromRecording(Session session, WireEvent setting,
        CancellationToken cancellationToken = default)
    {
        bool flushed = session.FlushRecordingForRead();
        var evidence = HistoricalAssociation.Resolve(session.RecordingSegments, setting, cancellationToken);
        if (!flushed && evidence.Status == RelationshipEvidenceStatus.Unavailable)
            evidence = evidence with { Reason = evidence.Reason + "；当前录制缓冲刷新失败" };
        return Materialize(evidence, "recording");
    }

    private static SettingOwnerResolution Materialize(SettingRelationshipEvidence evidence, string source)
    {
        var owners = evidence.Events.Length == 0 ? [] :
            PlayerPlaybacksAtSetting(evidence.Events, evidence.Setting);
        return new(owners, evidence.Status, source, evidence.Reason, evidence.RecentCue);
    }

    public static string[] SourcePlaybackIds(WireEvent source)
    {
        try
        {
            using var doc = JsonDocument.Parse(source.raw);
            if (!doc.RootElement.TryGetProperty("derived", out var derived) || !derived.TryGetProperty("links", out var links)) return [];
            return links.EnumerateArray().Select(l => l.GetProperty("playback").GetString() ?? "")
                .Where(s => s.Length > 0).Select(s => source.epoch > 0 ? source.epoch + ":" + s : s).Distinct().ToArray();
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException) { return []; }
    }
    public static WireEvent[] SourcesFor(IEnumerable<WireEvent> events, IEnumerable<string> playbackIds, double time)
    {
        var ids = playbackIds.ToHashSet(StringComparer.Ordinal);
        return SpatialPresentation.Group(events, time, false, true, false).SelectMany(c => c.Items)
            .Where(s => SourcePlaybackIds(s).Any(ids.Contains)).ToArray();
    }
    public static PlaybackGroup[] SourcePlaybacks(IEnumerable<WireEvent> events, WireEvent source, double time)
    {
        // Re-read the source at the chosen time; a stale selected marker must not keep an old link alive.
        var current = SpatialPresentation.Group(events, time, false, true, false).SelectMany(c => c.Items)
            .FirstOrDefault(e => e.objectId == source.objectId && e.session == source.session);
        var ids = current == null ? [] : SourcePlaybackIds(current);
        return PlaybackPresentation.Group(events, time).Where(p => ids.Contains(p.Id)).ToArray();
    }
    public static string[] CueCategories(WireEvent? info)
    {
        if(info==null||string.IsNullOrEmpty(info.raw))return [];
        try {
            using var doc=JsonDocument.Parse(info.raw);
            if(!doc.RootElement.TryGetProperty("basis",out var basis)||basis.GetString()!="cue-config"||!doc.RootElement.TryGetProperty("categories",out var categories))return [];
            return categories.EnumerateArray().Select(c=>c.GetString()??"").Where(c=>c.Length>0).Distinct().ToArray();
        } catch(Exception ex) when(ex is JsonException or InvalidOperationException){return [];}
    }
    public static WireEvent[] Categories(IEnumerable<WireEvent> events, string playback, double time) => events
        .Where(e => e.time <= time && e.kind == "category" && e.parentId == playback)
        .GroupBy(e => e.objectId).Select(g => g.OrderBy(e => e.time).ThenBy(e => e.seq).Last()).ToArray();
}
