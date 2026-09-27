namespace CriScope.Core;

/// <summary>
/// Reads a sequence range from ordered JSONL recording segments. Each file is
/// streamed through a fixed-length view of its complete lines; a truncated
/// final line is not evidence. No recording is loaded into a live Session.
/// </summary>
public static class RecordingEvidenceReader
{
    public static IEnumerable<WireEvent> ReadRange(
        IEnumerable<string> recordingPaths, long firstSequence, long lastSequence,
        string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (firstSequence < 1) throw new ArgumentOutOfRangeException(nameof(firstSequence));
        if (lastSequence < firstSequence) yield break;
        long highest = 0;
        foreach (var path in recordingPaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            bool headerSeen = false;
            foreach (var e in RecordingLines.Read(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!headerSeen)
                {
                    if (e.kind != "hello" || e.value != 1)
                        throw new InvalidDataException($"录制段缺少 CriScope/1 文件头：{Path.GetFileName(path)}");
                    if (sessionId != null && e.session != sessionId)
                        throw new InvalidDataException($"录制段属于另一会话：{Path.GetFileName(path)}");
                    headerSeen = true;
                    continue;
                }
                if (e.kind == "hello")
                    throw new InvalidDataException($"录制段包含重复文件头：{Path.GetFileName(path)}");
                if (sessionId != null && e.session != sessionId)
                    throw new InvalidDataException($"录制段事件属于另一会话：{Path.GetFileName(path)}");
                if (e.seq < 1)
                    throw new InvalidDataException($"录制段事件缺少有效序号：{Path.GetFileName(path)}");
                // Rotation writes an active-state baseline into the new
                // segment. Earlier original entries win over these copies.
                if (e.seq <= highest)
                {
                    if (e.baseline) continue;
                    throw new InvalidDataException($"录制段事件序号倒退或重复：{Path.GetFileName(path)}");
                }
                highest = e.seq;
                if (e.seq > lastSequence) yield break;
                if (e.seq >= firstSequence) yield return e;
            }
            if (!headerSeen)
                throw new InvalidDataException($"录制段没有完整文件头：{Path.GetFileName(path)}");
        }
    }
}

/// <summary>Reconstructs an older setting from disk without retaining its full event history.</summary>
public static class HistoricalAssociation
{
    // The rotation baseline carries active requests into the containing
    // segment, so most lookups need at most one bounded segment. A last-played
    // Cue that ended before this segment is recovered separately, backwards.
    public static SettingRelationshipEvidence Resolve(
        IReadOnlyList<RecordingSegmentInfo> segments, WireEvent setting,
        CancellationToken cancellationToken = default)
    {
        int target = -1;
        for (int i = 0; i < segments.Count; i++)
            if (segments[i].FirstIncrementalSequence <= setting.seq &&
                setting.seq <= segments[i].LastIncrementalSequence)
            { target = i; break; }
        if (target < 0)
            return new(setting, [], RelationshipEvidenceStatus.Unavailable,
                "这次设置不在已保存的录制段中；无法补回未录到的事件");

        var result = Resolve([segments[target].Path], setting, cancellationToken);
        if (target == 0 || segments[target].StartsNewConnection ||
            result.Status == RelationshipEvidenceStatus.Unavailable || result.RecentCue != null)
            return result;

        try
        {
            // A discontinuity inside the target segment makes any older Cue
            // clue unsafe, even if the Player handle number happens to match.
            if (HasBoundary(segments[target].Path, setting, setting.seq, cancellationToken)) return result;
            for (int i = target - 1; i >= 0; i--)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (segments[i + 1].StartsNewConnection) break;
                var (cue, boundary) = LastCueInSegment(segments[i].Path, setting, cancellationToken);
                if (cue != null) return result with { RecentCue = cue };
                if (boundary || segments[i].StartsNewConnection) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return result with { Reason = result.Reason + "；更早 Cue 线索读取失败：" + ex.Message };
        }
        return result;
    }

    public static SettingRelationshipEvidence Resolve(
        IEnumerable<string> recordingPaths, WireEvent setting, CancellationToken cancellationToken = default)
    {
        if (setting.kind is not ("aisac" or "selector") || setting.seq < 1)
            return new(setting, [], RelationshipEvidenceStatus.Unavailable, "不是可回推的 Player 设置事件");

        var index = new CaptureRelationshipIndex(1);
        bool any = false;
        bool found = false;
        long highest = 0;
        try
        {
            foreach (var path in recordingPaths)
            {
                bool headerSeen = false;
                foreach (var e in RecordingLines.Read(path))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (!headerSeen)
                    {
                        if (e.kind != "hello" || e.value != 1 || e.session != setting.session)
                            throw new InvalidDataException($"录制段文件头与设置会话不符：{Path.GetFileName(path)}");
                        headerSeen = true;
                        if (any && e.detail.Contains("connectionStart=true", StringComparison.Ordinal))
                            index.MarkIncomplete();
                        continue;
                    }
                    if (e.kind == "hello" || e.session != setting.session || e.seq < 1)
                        throw new InvalidDataException($"录制段事件身份或序号无效：{Path.GetFileName(path)}");
                    if (e.seq <= highest)
                    {
                        if (e.baseline) continue;
                        throw new InvalidDataException($"录制段事件序号倒退或重复：{Path.GetFileName(path)}");
                    }
                    highest = e.seq;
                    if (e.seq > setting.seq) break;
                    if (!any && e.seq > 1) index.MarkIncomplete();
                    any = true;
                    index.Observe(e);
                    if (e.seq == setting.seq)
                    {
                        found = e.kind == setting.kind && e.objectId == setting.objectId && e.name == setting.name;
                        break;
                    }
                }
                if (!headerSeen) throw new InvalidDataException($"录制段没有完整文件头：{Path.GetFileName(path)}");
                if (found || highest > setting.seq) break;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new(setting, [], RelationshipEvidenceStatus.Unavailable, "物理日志不可完整读取：" + ex.Message);
        }

        if (!found || !index.TryGet(setting, out var evidence))
            return new(setting, [], RelationshipEvidenceStatus.Unavailable,
                any ? "物理日志中没有这次设置的完整记录" : "物理日志尚无可读取事件");
        return evidence;
    }

    private static bool HasBoundary(string path, WireEvent setting, long throughSequence,
        CancellationToken cancellationToken)
    {
        foreach (var e in RecordingEvidenceReader.ReadRange([path], 1, throughSequence,
                     setting.session, cancellationToken))
            if (IsBoundary(e, setting)) return true;
        return false;
    }

    private static (WireEvent? Cue, bool Boundary) LastCueInSegment(string path,
        WireEvent setting, CancellationToken cancellationToken)
    {
        WireEvent? latest = null;
        bool boundary = false;
        foreach (var e in RecordingEvidenceReader.ReadRange([path], 1, long.MaxValue,
                     setting.session, cancellationToken))
        {
            if (IsBoundary(e, setting))
            {
                latest = null;
                boundary = true;
            }
            else if (e.kind == "request" && e.entity == "cue" && e.parentId == setting.objectId &&
                     e.epoch == setting.epoch)
                latest = e;
        }
        return (latest, boundary);
    }

    private static bool IsBoundary(WireEvent e, WireEvent setting) =>
        e.kind == "gap" || e.entity == "capture-segment" ||
        e.kind == "log" && e.objectId == setting.objectId &&
        e.name is ("ExPlayer_Create" or "ExPlayer_Create_Success" or "ExPlayer_Destroy");
}
