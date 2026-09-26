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
    private StreamWriter? writer;
    private DateTime flushed = DateTime.UtcNow;
    private long watermark;
    private long recordedThroughSequence;
    private double recordedThroughTime;
    public const int MaxLiveEvents = 120000;
    public const double LiveSeconds = 120;
    public const int MaxViewStates = 8192;
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
    public bool Recording { get { lock (gate) return writer != null; } }
    public string RecordingPath { get; private set; } = "";
    public string? Error { get; private set; }
    public long Total { get; private set; }
    public long Dropped { get; private set; }
    public long Evicted { get; private set; }
    public double LastTime { get; private set; }
    public long Watermark { get { lock(gate) return watermark; } }
    public Session(WireEvent hello, TimeProvider? timeProvider = null) { Id=hello.session; Name=hello.name; Platform=hello.platform; Pid=hello.pid; Source=string.IsNullOrWhiteSpace(hello.source)?"CRI SDK":hello.source; Endpoint=hello.detail; ClientId=hello.clientId; Machine=hello.machine; CaptureId=hello.captureId; Channel=hello.channel; clock=timeProvider??TimeProvider.System; RestoreClock(hello); }
    public WireEvent[] Snapshot() { lock(gate) return events.ToArray(); }
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
            UpdateViewState(e);
            if(writer!=null)
            {
                try {
                    // Persist receiver clock metadata alongside unchanged source fields and native raw evidence.
                    writer.WriteLine(e.ToJson());
                    recordedThroughSequence=e.seq; recordedThroughTime=LastTime;
                    if((DateTime.UtcNow-flushed).TotalSeconds>=1) {writer.Flush();flushed=DateTime.UtcNow;}
                } catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) { Error="录制写盘失败："+ex.Message; CloseWriter(); }
            }
            return true;
        }
    }
    internal void SetCaptureState(bool connected, bool capturing, string status) { lock(gate) { Connected=connected; Capturing=capturing; ConnectionStatus=status; } }
    public void StartRecording(string directory)
    {
        lock(gate)
        {
            if(IsReplay) throw new InvalidOperationException("历史记录不可再次录制");
            if(writer!=null) return;
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,$"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Pid}_{Guid.NewGuid():N}.criscope");
            var next=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            try {
                next.WriteLine(RecordingHeader("CriScope/1; explicit baseline retains original observation times; subsequent events are incremental").ToJson());
                foreach(var ev in Baseline()) next.WriteLine(ev.ToJson());
                next.Flush();
            }
            catch {next.Dispose();throw;}
            writer=next;RecordingPath=path;Error=null;
            recordedThroughSequence=watermark;recordedThroughTime=LastTime;
        }
    }
    public (WireEvent[] Events, bool FromRecording, bool HasUnrecordedGap, long ContextAfterSequence, long RecordedThroughSequence, double RecordedThroughTime) EvidenceSnapshot()
    {
        string path; long endSequence, fileLength, recordedEnd; double recordedTime; WireEvent[] live;
        lock(gate)
        {
            writer?.Flush(); path=RecordingPath; endSequence=watermark;
            live=events.ToArray();recordedEnd=recordedThroughSequence;recordedTime=recordedThroughTime;
            if(string.IsNullOrEmpty(path) || !File.Exists(path)) return (live, false, false, 0, 0, 0);
            fileLength=new FileInfo(path).Length;
        }
        var lines = new List<WireEvent>();
        using var reader = new StreamReader(new ReadWindowStream(new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite),fileLength));
        string? line;
        while((line=reader.ReadLine())!=null)
        {
            var ev=WireEvent.Parse(line);
            if(ev.seq>endSequence) break;
            if(ev.kind!="hello") lines.Add(ev);
            if(lines.Count>2000000) throw new InvalidDataException("问题包超过200万事件，请分段记录");
        }
        var newTail=live.Where(e=>e.seq>recordedEnd).ToArray();
        bool missing=newTail.Length>0 && newTail[0].seq>recordedEnd+1;
        // Prefer recorded entries for duplicate sequence numbers: explicit recording
        // baselines must retain their baseline flag and original observation time.
        var merged=lines.Concat(live).GroupBy(e=>e.seq).Select(g=>g.First()).OrderBy(e=>e.seq).ToArray();
        return (merged,true,missing,missing?newTail[0].seq:0,recordedEnd,recordedTime);
    }
    public WireEvent[] Baseline()
    {
        lock(gate)
            return viewState.Where(pair=>!endedVoices.ContainsKey(pair.Key)).Select(pair=>pair.Value).OrderBy(e=>e.seq)
                .Select(e=> {var copy=WireEvent.Parse(e.ToJson());copy.baseline=true;return copy;}).ToArray();
    }
    public void StopRecording() { lock(gate) CloseWriter(); }
    private void CloseWriter() {var old=writer;writer=null;try {old?.Dispose();} catch(Exception ex) {Error="结束写盘失败："+ex.Message;} }
    public void Dispose()=>StopRecording();
}
