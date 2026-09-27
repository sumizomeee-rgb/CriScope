using System.Text;
namespace CriScope.Core;

public sealed partial class Session : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<WireEvent> events = new();
    // Latest known state is separate from the event window: it is never replayed into
    // pagination, Total or Watermark; explicit recording baselines retain original timestamps.
    private readonly Dictionary<string, WireEvent> viewState = new();
    private readonly Dictionary<string, double> endedVoices = new();
    private readonly SortedSet<(double Time, string Key)> endedOrder = new();
    private readonly CaptureRelationshipIndex relationshipIndex = new();
    private readonly List<RecordingSegmentInfo> recordingSegments = new();
    private StreamWriter? writer;
    private DateTime flushed = DateTime.UtcNow;
    private long watermark;
    private long recordedThroughSequence;
    private double recordedThroughTime;
    private long recordedThroughOrdinal;
    private long pendingThroughSequence;
    private double pendingThroughTime;
    private long pendingThroughOrdinal;
    private long pendingPhysicalThroughSequence;
    private long recordingOrdinal;
    private long knownUnrecordedGapAfterSequence;
    private long lastGapReservedThroughSequence;
    private long pendingFirstSegmentSequence;
    private bool automaticRecording;
    private bool busSamplesAreEventTriggered;
    private double busWindowStartTime = double.NegativeInfinity;
    private double busWindowEndTime = double.NegativeInfinity;
    private readonly HashSet<string> busesRecordedInWindow = new(StringComparer.Ordinal);
    private long segmentIncrementalBytes;
    private int segmentEvents;
    private long automaticBytes;
    private DateTime lastSpaceCheck = DateTime.MinValue;
    private AutomaticRecordingOptions recordingOptions = new();
    public const long AutoSegmentBytes = 64L * 1024 * 1024;
    public const int AutoSegmentEvents = 200000;
    public const long MaxAutomaticBytesPerSession = 2L * 1024 * 1024 * 1024;
    public const long MinimumFreeSpaceBytes = 512L * 1024 * 1024;
    public const int MaxLiveEvents = 120000;
    public const double LiveSeconds = 120;
    public const int MaxViewStates = 8192;
    private const string EventTriggeredBusPolicy = "busPolicy=event-triggered-500ms";
    private const double BusWindowSeconds = 0.5;
    public long ViewStatesEvicted { get; private set; }
    public string ClientId { get; }
    public string Machine { get; }
    public string CaptureId { get; }
    public string Channel { get; }
    public string Id { get; }
    public string Source { get; }
    public string Endpoint { get; }
    public string ConnectionStatus { get; internal set; } = "等待数据";
    public string Name { get; private set; }
    public string Platform { get; private set; }
    public int Pid { get; private set; }
    public bool Connected { get; internal set; }
    public bool Capturing { get; private set; }
    public bool IsReplay { get; internal set; }
    public bool BusSamplesAreEventTriggered { get { lock(gate) return busSamplesAreEventTriggered; } }
    public bool Recording { get { lock (gate) return writer != null; } }
    public string RecordingPath { get; private set; } = "";
    public string[] RecordingPaths { get { lock (gate) return recordingSegments.Select(s => s.Path).ToArray(); } }
    public RecordingSegmentInfo[] RecordingSegments { get { lock (gate) return recordingSegments.ToArray(); } }
    public string? Error { get; private set; }
    public long Total { get; private set; }
    public long Dropped { get; private set; }
    public long Evicted { get; private set; }
    public double LastTime { get; private set; }
    public long Watermark { get { lock(gate) return watermark; } }
    public Session(WireEvent hello, TimeProvider? timeProvider = null) { Id=hello.session; Name=hello.name; Platform=hello.platform; Pid=hello.pid; Source=string.IsNullOrWhiteSpace(hello.source)?"CRI SDK":hello.source; Endpoint=hello.detail; ClientId=hello.clientId; Machine=hello.machine; CaptureId=hello.captureId; Channel=hello.channel; busSamplesAreEventTriggered=hello.detail?.Contains(EventTriggeredBusPolicy,StringComparison.Ordinal)==true; clock=timeProvider??TimeProvider.System; RestoreClock(hello); }
    public WireEvent[] Snapshot() { lock(gate) return events.ToArray(); }
    public bool FlushRecordingForRead()
    {
        lock(gate)
        {
            if(writer==null) return true;
            try {writer.Flush();flushed=DateTime.UtcNow;MarkDurable();return true;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {Error="自动录制读取前刷盘失败，磁盘证据可能不完整："+ex.Message;CloseWriter();return false;}
        }
    }
    public bool TryGetSettingRelationship(WireEvent setting, out SettingRelationshipEvidence evidence)
    { lock(gate) return relationshipIndex.TryGet(setting,out evidence); }
    public WireEvent[] ViewSnapshot()
    {
        lock(gate)
        {
            if(IsReplay) return events.ToArray();
            var visible = events.ToArray();
            var inWindow = visible.Select(e=>e.seq).ToHashSet();
            return viewState.Values.Where(e=>!inWindow.Contains(e.seq)).Concat(visible).OrderBy(e=>e.seq).ToArray();
        }
    }
    private void UpdateViewState(WireEvent e)
    {
        if(IsReplay) return;
        if(e.entity=="capture-segment" || e.kind=="gap") {viewState.Clear();endedVoices.Clear();endedOrder.Clear();return;}
        string? key = e.kind switch {
            "play" => "voice|"+e.objectId,
            "request" => "cue|"+e.objectId,
              "category" => "category|"+e.parentId+"|"+e.objectId,
              "category-info" => "category-info|"+e.objectId,
            "aisac" or "selector" => e.kind+"|"+e.objectId+"|"+e.name,
            "position" or "remove" => "position|"+e.entity+"|"+e.objectId,
            "metric" => "metric|"+e.objectId+"|"+e.name,
            "bus" => "bus|"+e.objectId,
            _ => null };
        if(e.kind=="selector" && e.detail.Contains("清除全部"))
            foreach(var oldKey in viewState.Keys.Where(k=>k.StartsWith("selector|"+e.objectId+"|",StringComparison.Ordinal)).ToArray()) viewState.Remove(oldKey);
        if(e.kind=="stop" && viewState.ContainsKey("voice|"+e.objectId)) MarkEnded("voice|"+e.objectId,e.time);
        if(EventSemantics.IsPlaybackEnd(e))
          {
              MarkEnded("cue|"+e.objectId,e.time);
              foreach(var categoryKey in viewState.Keys.Where(k=>k.StartsWith("category|"+e.objectId+"|",StringComparison.Ordinal)).ToArray()) MarkEnded(categoryKey,e.time);
          }
        if(key!=null)
        {
            viewState[key]=e;
            if(e.kind is "play" or "request") RemoveEnded(key);
            if(viewState.Count>MaxViewStates)
            {
                var oldest=viewState.MinBy(pair=>pair.Value.seq).Key;
                viewState.Remove(oldest);RemoveEnded(oldest);ViewStatesEvicted++;
            }
        }
        // Keep the original start while a matching stop is still visible, then discard it.
        double oldestVisible=events.Count>0?events.Peek().time:LastTime;
        while(endedOrder.Count>0 && endedOrder.Min.Time<oldestVisible)
        {
            var ended=endedOrder.Min;
            viewState.Remove(ended.Key);RemoveEnded(ended.Key);
        }
    }
    private void MarkEnded(string key, double time)
    {
        if(!viewState.ContainsKey(key)) return;
        RemoveEnded(key);endedVoices[key]=time;endedOrder.Add((time,key));
    }
    private void RemoveEnded(string key)
    {
        if(endedVoices.Remove(key,out var time)) endedOrder.Remove((time,key));
    }
    public bool Accept(WireEvent e, string? raw = null)
    {
        lock(gate)
        {
            if (e.session != Id) throw new InvalidDataException("连接中的会话身份发生变化");
            if(e.seq <= watermark && e.kind != "hello") return false;
            // 协议 v1 的桥仅在采集开启时连接；重连不能依赖早已 ACK 的 state=1。
            if(e.kind == "hello") { Name=e.name; Platform=e.platform; Pid=e.pid; Capturing=!IsReplay; return true; }
            UpdateTiming(e);
            watermark=Math.Max(watermark,e.seq);
            LastTime=Math.Max(LastTime,e.time);
            if(e.kind=="state") Capturing=e.value>0;
            if(e.kind=="gap") Dropped+=(long)Math.Max(0,e.value);
            events.Enqueue(e); Total++;
            if(!IsReplay)
                while(events.Count>MaxLiveEvents || events.Count>1 && events.Peek().time<LastTime-LiveSeconds) {events.Dequeue();Evicted++;}
            if(writer!=null)
            {
                try {
                    bool persist = !automaticRecording || !busSamplesAreEventTriggered || ShouldPersistEventTriggeredBus(e);
                    if(!persist)
                    {
                        pendingThroughSequence=e.seq;pendingThroughTime=LastTime;
                        if((DateTime.UtcNow-flushed).TotalSeconds>=1)
                        {writer.Flush();flushed=DateTime.UtcNow;MarkDurable();}
                    }
                    else
                    {
                        if(automaticRecording && (segmentIncrementalBytes>=recordingOptions.SegmentBytes || segmentEvents>=recordingOptions.SegmentEvents))
                        {
                            var directory=Path.GetDirectoryName(RecordingPath)!;
                            CloseWriter();
                            OpenAutomaticSegment(directory,e.seq,false);
                        }
                        if(writer!=null) WriteRecordedEvent(e);
                    }
                } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException or System.Text.Json.JsonException)
                { Error=$"自动录制中断，序号 {e.seq} 起存在磁盘证据缺口："+ex.Message; CloseWriter(); }
            }
            UpdateViewState(e);
            if(!IsReplay) relationshipIndex.Observe(e);
            return true;
        }
    }
    private bool ShouldPersistEventTriggeredBus(WireEvent e)
    {
        if(e.entity=="capture-segment" || e.kind=="gap") ResetBusWindow();
        if(IsBusSnapshotTrigger(e) && double.IsFinite(e.time))
        {
            // Multiple discrete events in a window share the same subsequent Bus snapshot.
            if(e.time>busWindowEndTime)
            {
                busWindowStartTime=e.time;
                busWindowEndTime=e.time+BusWindowSeconds;
                busesRecordedInWindow.Clear();
            }
        }
        if(e.kind!="bus") return true;
        return e.time>=busWindowStartTime && e.time<=busWindowEndTime &&
            busesRecordedInWindow.Add(e.objectId);
    }
    private static bool IsBusSnapshotTrigger(WireEvent e) => e.kind is
        "request" or "play" or "stop" or "stop-request" or "aisac" or "category" or "selector" or "remove" or "block" or "error" ||
        e.kind=="log" && (e.entity=="control" || e.entity=="cue" && e.lifecycle=="released");
    private void ResetBusWindow()
    {
        busWindowStartTime=double.NegativeInfinity;
        busWindowEndTime=double.NegativeInfinity;
        busesRecordedInWindow.Clear();
    }
    internal void SetCaptureState(bool connected, bool capturing, string status) { lock(gate) { Connected=connected; Capturing=capturing; ConnectionStatus=status; } }
    public void StartAutomaticRecording(string directory,AutomaticRecordingOptions? options=null)
    {
        lock(gate)
        {
            if(IsReplay) throw new InvalidOperationException("历史记录不可再次录制");
            if(writer!=null) return;
            recordingOptions=options??new AutomaticRecordingOptions();
            recordingOptions.Validate();
            automaticRecording=true;
            busSamplesAreEventTriggered |= Channel=="native";
            ResetBusWindow();
            bool newGap=recordingSegments.Count>0 && watermark>recordedThroughSequence && watermark>lastGapReservedThroughSequence;
            if(recordingSegments.Count>0)
            {
                if(newGap && knownUnrecordedGapAfterSequence==0)
                    knownUnrecordedGapAfterSequence=watermark+1;
                // The disconnected game may have changed its Player state.
                // Do not carry an old active Playback into this connection.
                viewState.Clear();endedVoices.Clear();endedOrder.Clear();
                relationshipIndex.MarkIncomplete();
            }
            try
            {
                OpenAutomaticSegment(directory,watermark+1,true);
                if(newGap) {recordingOrdinal++;lastGapReservedThroughSequence=watermark;}
            }
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or InvalidOperationException)
            { Error=$"自动录制未开始，序号 {watermark+1} 起存在磁盘证据缺口："+ex.Message; automaticRecording=false; }
        }
    }
    private void OpenAutomaticSegment(string directory,long firstSequence,bool startsNewConnection)
    {
        if(automaticBytes>=recordingOptions.MaxSessionBytes)
            throw new IOException($"单通道自动录制已达到 {recordingOptions.MaxSessionBytes/(1024*1024)} MiB 上限；旧文件已保留");
        Directory.CreateDirectory(directory);
        EnsureFreeSpace(directory);
        int ordinal=recordingSegments.Count+1;
        var safeId=Guid.TryParse(Id,out var parsed)?parsed.ToString("N"):Guid.NewGuid().ToString("N");
        var safeChannel=Channel is "native" or "sdk" ? Channel : "events";
        var path=Path.Combine(directory,$"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Pid}_{safeId}_{safeChannel}_{ordinal:D4}_{Guid.NewGuid():N}.criscope");
        var baseline=ordinal==1 ? Array.Empty<WireEvent>() : Baseline();
        var baselineLines=baseline.Select(ev=>ev.ToJson()).ToArray();
        string busPolicy=Channel=="native" ? $"; {EventTriggeredBusPolicy}; busWindow=post-trigger-next-sample; bus history is sparse" : "";
        string header=RecordingHeader($"CriScope/1; automatic; segment={ordinal}; connectionStart={startsNewConnection.ToString().ToLowerInvariant()}; previousThroughSeq={recordedThroughSequence}; previousThroughRecordOrdinal={recordedThroughOrdinal}; nextExpectedSeq={firstSequence}; baseline retains original observation times{busPolicy}").ToJson();
        long written=Utf8LineBytes(header)+baselineLines.Sum(Utf8LineBytes);
        if(automaticBytes+written>recordingOptions.MaxSessionBytes)
            throw new IOException($"单通道自动录制已达到 {recordingOptions.MaxSessionBytes/(1024*1024)} MiB 上限；旧文件已保留");
        var next=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)) { NewLine="\n" };
        try
        {
            next.WriteLine(header);
            foreach(var line in baselineLines) next.WriteLine(line);
            next.Flush();
        }
        catch { next.Dispose(); throw; }
        writer=next;RecordingPath=path;flushed=DateTime.UtcNow;lastSpaceCheck=flushed;
        segmentIncrementalBytes=0;segmentEvents=0;pendingFirstSegmentSequence=firstSequence;automaticBytes+=written;
        pendingPhysicalThroughSequence=firstSequence-1;
        recordingSegments.Add(new RecordingSegmentInfo(path,firstSequence,firstSequence-1,baseline.Length,true,startsNewConnection));
    }
    public void StartRecording(string directory)
    {
        lock(gate)
        {
            if(IsReplay) throw new InvalidOperationException("历史记录不可再次录制");
            if(writer!=null) return;
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,$"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Pid}_{Guid.NewGuid():N}.criscope");
            var next=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false)) { NewLine="\n" };
            long written=0;
            var baseline=Baseline();
            try {
                var header=RecordingHeader("CriScope/1; explicit baseline retains original observation times; subsequent events are incremental").ToJson();
                next.WriteLine(header);written+=Utf8LineBytes(header);
                foreach(var ev in baseline) {var line=ev.ToJson();next.WriteLine(line);written+=Utf8LineBytes(line);}
                next.Flush();
            }
            catch {next.Dispose();throw;}
            writer=next;RecordingPath=path;Error=null;
            automaticRecording=false;segmentIncrementalBytes=0;segmentEvents=0;pendingFirstSegmentSequence=watermark+1;flushed=DateTime.UtcNow;
            recordingSegments.Add(new RecordingSegmentInfo(path,watermark+1,watermark,baseline.Length,true));
            recordedThroughSequence=watermark;recordedThroughTime=LastTime;
            pendingThroughSequence=watermark;pendingThroughTime=LastTime;
            pendingThroughOrdinal=recordedThroughOrdinal;
        }
    }
    private void WriteRecordedEvent(WireEvent e)
    {
        long nextOrdinal=checked(recordingOrdinal+1);
        var line=e.CloneForRecording(nextOrdinal).ToJson();long bytes=Utf8LineBytes(line);
        if(automaticRecording)
        {
            if(automaticBytes+bytes>recordingOptions.MaxSessionBytes)
                throw new IOException($"单通道自动录制已达到 {recordingOptions.MaxSessionBytes/(1024*1024)} MiB 上限；旧文件已保留");
            if((DateTime.UtcNow-lastSpaceCheck).TotalSeconds>=1)
            { EnsureFreeSpace(Path.GetDirectoryName(RecordingPath)!);lastSpaceCheck=DateTime.UtcNow; }
        }
        recordingOrdinal=nextOrdinal;
        writer!.WriteLine(line);
        if(segmentEvents==0) pendingFirstSegmentSequence=e.seq;
        segmentIncrementalBytes+=bytes;segmentEvents++;
        if(automaticRecording) automaticBytes+=bytes;
        pendingThroughSequence=e.seq;pendingThroughTime=LastTime;
        pendingThroughOrdinal=nextOrdinal;pendingPhysicalThroughSequence=e.seq;
        if((DateTime.UtcNow-flushed).TotalSeconds>=1)
        {writer.Flush();flushed=DateTime.UtcNow;MarkDurable();}
    }
    private void MarkDurable()
    {
        recordedThroughSequence=pendingThroughSequence;recordedThroughTime=pendingThroughTime;
        recordedThroughOrdinal=pendingThroughOrdinal;
        if(recordingSegments.Count==0) return;
        int last=recordingSegments.Count-1;
        if(segmentEvents>0)
            recordingSegments[last]=recordingSegments[last] with
            {FirstIncrementalSequence=pendingFirstSegmentSequence,LastIncrementalSequence=pendingPhysicalThroughSequence};
    }
    private static long Utf8LineBytes(string line)=>Encoding.UTF8.GetByteCount(line)+1;
    private void EnsureFreeSpace(string directory)
    {
        if(recordingOptions.MinimumFreeBytes==0) return;
        var root=Path.GetPathRoot(Path.GetFullPath(directory));
        if(string.IsNullOrEmpty(root)) throw new IOException("无法确定录制目录所在磁盘");
        var available=new DriveInfo(root).AvailableFreeSpace;
        if(available<recordingOptions.MinimumFreeBytes)
            throw new IOException($"磁盘剩余空间低于 {recordingOptions.MinimumFreeBytes/(1024*1024)} MiB 保留线；旧文件已保留");
    }
    public (WireEvent[] Events, bool FromRecording, bool HasUnrecordedGap, long ContextAfterSequence, long RecordedThroughSequence, double RecordedThroughTime) EvidenceSnapshot()
    {
        string[] paths; long endSequence, recordedEnd, durableOrdinal, knownGap; double recordedTime; WireEvent[] live;
        lock(gate)
        {
            FlushRecordingForRead(); paths=recordingSegments.Select(s=>s.Path).ToArray(); endSequence=watermark;
            live=events.ToArray();recordedEnd=recordedThroughSequence;recordedTime=recordedThroughTime;durableOrdinal=recordedThroughOrdinal;knownGap=knownUnrecordedGapAfterSequence;
            if(paths.Length==0) return (live, false, false, 0, 0, 0);
        }
        var lines = new Dictionary<long,WireEvent>();
        long afterGap=knownGap,lastIncremental=0,lastRecordOrdinal=0;
        foreach(var path in paths)
        {
            foreach(var ev in RecordingLines.Read(path))
            {
                if(ev.kind=="hello" || ev.seq>endSequence) continue;
                if(!ev.baseline)
                {
                    // New recordings number only physical incremental rows. Source seq gaps
                    // can be deliberate Bus omissions; ordinal gaps mean missing disk rows.
                    if(ev.recordOrdinal>0)
                    {
                        if(ev.recordOrdinal!=lastRecordOrdinal+1 && afterGap==0) afterGap=ev.seq;
                        lastRecordOrdinal=Math.Max(lastRecordOrdinal,ev.recordOrdinal);
                    }
                    else if(lastIncremental>0 && ev.seq>lastIncremental+1 && afterGap==0) afterGap=ev.seq;
                    lastIncremental=Math.Max(lastIncremental,ev.seq);
                }
                if(!lines.TryGetValue(ev.seq,out var old) || old.baseline && !ev.baseline) lines[ev.seq]=ev;
                if(lines.Count>2000000) throw new InvalidDataException("问题包超过200万事件；请选择较短区间或分段导出");
            }
        }
        if(lastRecordOrdinal<durableOrdinal && afterGap==0)
            afterGap=lastIncremental+1;
        var newTail=live.Where(e=>e.seq>recordedEnd).ToArray();
        if(newTail.Length>0 && newTail[0].seq>recordedEnd+1 && afterGap==0) afterGap=newTail[0].seq;
        bool missing=afterGap>0;
        // Prefer recorded entries for duplicate sequence numbers: explicit recording
        // baselines must retain their baseline flag and original observation time.
        var merged=lines.Values.Concat(live).GroupBy(e=>e.seq).Select(g=>g.First()).OrderBy(e=>e.seq).ToArray();
        return (merged,true,missing,afterGap,recordedEnd,recordedTime);
    }
    public WireEvent[] Baseline()
    {
        lock(gate)
            return viewState.Where(pair=>!endedVoices.ContainsKey(pair.Key)).Select(pair=>pair.Value).OrderBy(e=>e.seq)
                .Select(e=> {var copy=WireEvent.Parse(e.ToJson());copy.baseline=true;return copy;}).ToArray();
    }
    public void StopRecording() { lock(gate) { automaticRecording=false; ResetBusWindow(); CloseWriter(); } }
    private void CloseWriter()
    {
        var old=writer;writer=null;
        if(old!=null && recordingSegments.Count>0)
        {
            int last=recordingSegments.Count-1;
            recordingSegments[last]=recordingSegments[last] with {IsOpen=false};
        }
        try {old?.Dispose();if(old!=null) MarkDurable();}
        catch(Exception ex) {Error=(Error==null?"":""+Error+"；")+"结束写盘失败："+ex.Message;}
    }
    public void Dispose()=>StopRecording();
}
