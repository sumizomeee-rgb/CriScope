namespace CriScope.Core;

// This is evidence captured at the setting's sequence, not a claim that the
// Player was idle when the array is empty. A hot connection or a gap can hide
// an already running Playback.
public enum RelationshipEvidenceStatus { Observed, ObservedNoOpenPlayback, Partial, Unavailable }

public sealed record SettingRelationshipEvidence(
    WireEvent Setting,
    WireEvent[] Events,
    RelationshipEvidenceStatus Status,
    string Reason,
    WireEvent? RecentCue = null);

/// <summary>
/// A small, bounded index of the latest control setting per Player/control.
/// Playback ownership is frozen when the setting arrives, before the live
/// two-minute event queue can discard its request and Voice evidence.
/// </summary>
public sealed class CaptureRelationshipIndex
{
    private sealed class PlaybackState(WireEvent request)
    {
        public WireEvent Request { get; } = request;
        public Dictionary<string, WireEvent> ActiveVoices { get; } = new(StringComparer.Ordinal);
        public WireEvent? LastVoiceStart { get; set; }
        public WireEvent? LastVoiceStop { get; set; }
        public WireEvent? StopRequest { get; set; }
    }

    private readonly Dictionary<string, PlaybackState> active = new(StringComparer.Ordinal);
    private readonly Dictionary<string, WireEvent> latestCueByPlayer = new(StringComparer.Ordinal);
    private readonly Queue<(string Player, long Sequence)> cueOrder = new();
    private readonly Dictionary<(string Kind, string Player, string Name), SettingRelationshipEvidence> settings = new();
    private readonly Queue<((string Kind, string Player, string Name) Key, long Sequence)> settingOrder = new();
    private readonly int maxSettings;
    private long lastSequence;
    private bool continuityUnknown;
    private bool baselineInProgress;
    private string continuityReason = "";
    private const int MaxActivePlaybacks = 8192;
    private const int MaxRememberedPlayers = 8192;
    private const int MaxVoicesPerPlayback = 256;

    public CaptureRelationshipIndex(int maxSettings = 8192)
    {
        if (maxSettings < 1) throw new ArgumentOutOfRangeException(nameof(maxSettings));
        this.maxSettings = maxSettings;
    }

    public void MarkIncomplete()
    {
        active.Clear();
        latestCueByPlayer.Clear();
        cueOrder.Clear();
        continuityUnknown = true;
        continuityReason = "连接边界或录制前史不完整；只能确认当前已保存的证据";
    }

    public void Observe(WireEvent e)
    {
        if (e.kind == "hello" || e.seq <= lastSequence) return;
        // A rotated segment can stand alone with sparse historical state
        // baselines. Those old original seq values are not missing events.
        // The first subsequent incremental record starts after the segment's
        // previous watermark, rather than after the last baseline's seq.
        if (e.baseline)
        {
            baselineInProgress = true;
            continuityUnknown = true;
            continuityReason = "从分段基线恢复活跃状态；更早的历史播放线索需另查";
        }
        else if (lastSequence > 0 && e.seq != lastSequence + 1 && !baselineInProgress)
        {
            active.Clear();
            latestCueByPlayer.Clear();
            cueOrder.Clear();
            continuityUnknown = true;
            continuityReason = "事件序号存在缺口；只能确认缺口后的实例";
        }
        if (!e.baseline) baselineInProgress = false;
        lastSequence = e.seq;

        if (e.kind == "gap" || e.entity == "capture-segment")
        {
            active.Clear();
            latestCueByPlayer.Clear();
            cueOrder.Clear();
            continuityUnknown = true;
            continuityReason = e.kind == "gap" ? "采集存在缺口；只能确认缺口后的实例" :
                "采集段重新开始；连接时已有播放的关联可能未记录";
            return;
        }

        // The native mapper leaves Player lifecycle calls inspectable as log
        // events. A recycled handle must not inherit the prior Player's Cues.
        if (e.objectId.Length > 0 && e.kind == "log" &&
            e.name is "ExPlayer_Create" or "ExPlayer_Create_Success" or "ExPlayer_Destroy")
        {
            foreach (var id in active.Where(p => p.Value.Request.parentId == e.objectId)
                         .Select(p => p.Key).ToArray()) active.Remove(id);
            latestCueByPlayer.Remove(e.objectId);
            return;
        }

        if (e.kind == "request" && e.entity == "cue" && e.objectId.Length > 0 && e.parentId.Length > 0)
        {
            if (active.Count >= MaxActivePlaybacks && !active.ContainsKey(e.objectId))
            {
                active.Remove(active.Keys.First());
                continuityUnknown = true;
                continuityReason = "活跃实例索引达到上限；关联可能不完整";
            }
            active[e.objectId] = new PlaybackState(e);
            latestCueByPlayer[e.parentId] = e;
            cueOrder.Enqueue((e.parentId, e.seq));
            while (latestCueByPlayer.Count > MaxRememberedPlayers && cueOrder.Count > 0)
            {
                var old = cueOrder.Dequeue();
                if (latestCueByPlayer.TryGetValue(old.Player, out var current) && current.seq == old.Sequence)
                    latestCueByPlayer.Remove(old.Player);
            }
            if (cueOrder.Count > MaxRememberedPlayers * 4) CompactCueOrder();
        }
        else if (e.entity == "voice" && e.parentId.Length > 0 && active.TryGetValue(e.parentId, out var playback))
        {
            if (e.kind == "play")
            {
                playback.LastVoiceStart = e;
                if (playback.ActiveVoices.Count >= MaxVoicesPerPlayback && !playback.ActiveVoices.ContainsKey(e.objectId))
                {
                    playback.ActiveVoices.Remove(playback.ActiveVoices.Keys.First());
                    continuityUnknown = true;
                    continuityReason = "Voice 索引达到上限；发声状态可能不完整";
                }
                playback.ActiveVoices[e.objectId] = e;
            }
            else if (e.kind == "stop")
            {
                if (playback.ActiveVoices.Remove(e.objectId, out var begin)) playback.LastVoiceStart = begin;
                playback.LastVoiceStop = e;
            }
        }
        else if (e.kind == "stop-request" && active.TryGetValue(e.objectId, out var stopping))
            stopping.StopRequest = e;

        if (EventSemantics.IsPlaybackEnd(e)) active.Remove(e.objectId);

        if (e.kind is "aisac" or "selector" && e.objectId.Length > 0 &&
            !e.objectId.StartsWith("category:", StringComparison.OrdinalIgnoreCase) &&
            !e.objectId.StartsWith("category-index:", StringComparison.OrdinalIgnoreCase))
            Freeze(e);
    }

    public bool TryGet(WireEvent setting, out SettingRelationshipEvidence evidence)
    {
        var key = (setting.kind, setting.objectId, setting.name);
        if (settings.TryGetValue(key, out var latest) && latest.Setting.session == setting.session &&
            latest.Setting.seq == setting.seq)
        {
            evidence = latest;
            return true;
        }
        evidence = null!;
        return false;
    }

    private void Freeze(WireEvent setting)
    {
        var owners = active.Values.Where(p => p.Request.parentId == setting.objectId &&
                                                  p.Request.session == setting.session &&
                                                  p.Request.epoch == setting.epoch)
            .OrderBy(p => p.Request.seq).ToArray();
        var events = new List<WireEvent>(owners.Length * 2);
        foreach (var owner in owners)
        {
            events.Add(owner.Request);
            if (owner.LastVoiceStart is { } lastStart && !owner.ActiveVoices.ContainsKey(lastStart.objectId))
                events.Add(lastStart);
            if (owner.LastVoiceStop is { } lastStop) events.Add(lastStop);
            events.AddRange(owner.ActiveVoices.Values);
            if (owner.StopRequest is { } stopRequest) events.Add(stopRequest);
        }
        var status = continuityUnknown ? RelationshipEvidenceStatus.Partial :
            owners.Length > 0 ? RelationshipEvidenceStatus.Observed : RelationshipEvidenceStatus.ObservedNoOpenPlayback;
        var reason = continuityUnknown ? continuityReason :
            owners.Length > 0 ? "写入时已观测到这些播放实例；不保证连接前的实例完整" :
            "写入前未观测到关联播放实例；无法证明 Player 空闲";
        var recentCue = latestCueByPlayer.GetValueOrDefault(setting.objectId);
        if (recentCue?.session != setting.session || recentCue.epoch != setting.epoch || recentCue.seq >= setting.seq)
            recentCue = null;
        var key = (setting.kind, setting.objectId, setting.name);
        settings[key] = new SettingRelationshipEvidence(setting,
            events.DistinctBy(ev => ev.seq).OrderBy(ev => ev.time).ThenBy(ev => ev.seq).ToArray(), status, reason, recentCue);
        settingOrder.Enqueue((key, setting.seq));
        while (settings.Count > maxSettings && settingOrder.Count > 0)
        {
            var old = settingOrder.Dequeue();
            if (settings.TryGetValue(old.Key, out var current) && current.Setting.seq == old.Sequence)
                settings.Remove(old.Key);
        }
        // Repeated writes to the same key can otherwise grow the FIFO forever.
        if (settingOrder.Count > maxSettings * 4) CompactOrder();
    }

    private void CompactOrder()
    {
        var current = settings.Select(p => (p.Key, p.Value.Setting.seq)).OrderBy(p => p.seq).ToArray();
        settingOrder.Clear();
        foreach (var item in current) settingOrder.Enqueue((item.Key, item.seq));
    }

    private void CompactCueOrder()
    {
        var current = latestCueByPlayer.Select(p => (p.Key, p.Value.seq)).OrderBy(p => p.seq).ToArray();
        cueOrder.Clear();
        foreach (var item in current) cueOrder.Enqueue((item.Key, item.seq));
    }
}
