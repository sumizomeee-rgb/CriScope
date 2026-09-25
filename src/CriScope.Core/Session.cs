using System.Text;
namespace CriScope.Core;

public sealed class Session : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<WireEvent> events = new();
    // Latest known state is separate from the event window: it is never replayed into
    // recordings, pagination, Total or Watermark, and retains its original timestamp.
    private readonly Dictionary<string, WireEvent> viewState = new();
    private readonly Dictionary<string, double> endedVoices = new();
    private StreamWriter? writer;
    private DateTime flushed = DateTime.UtcNow;
    private long watermark;
    public const int MaxLiveEvents = 120000;
    public const double LiveSeconds = 120;
    public const int MaxViewStates = 8192;
    public long ViewStatesEvicted { get; private set; }
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
    public Session(WireEvent hello) { Id=hello.session; Name=hello.name; Platform=hello.platform; Pid=hello.pid; Source=string.IsNullOrWhiteSpace(hello.source)?"CRI SDK":hello.source; Endpoint=hello.detail; }
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
        if(e.entity=="capture-segment") {viewState.Clear();endedVoices.Clear();return;}
        string? key = e.kind switch {
            "play" => "voice|"+e.objectId,
            "request" => "cue|"+e.objectId,
            "aisac" or "selector" => e.kind+"|"+e.objectId+"|"+e.name,
            "position" => "position|"+e.entity+"|"+e.objectId,
            "metric" => "metric|"+e.objectId+"|"+e.name,
            "bus" => "bus|"+e.objectId,
            _ => null };
        if(e.kind=="selector" && e.detail.Contains("清除全部"))
            foreach(var oldKey in viewState.Keys.Where(k=>k.StartsWith("selector|"+e.objectId+"|",StringComparison.Ordinal)).ToArray()) viewState.Remove(oldKey);
        if(e.kind=="stop" && viewState.ContainsKey("voice|"+e.objectId)) endedVoices["voice|"+e.objectId]=e.time;
        if(key!=null)
        {
            viewState[key]=e;
            if(e.kind=="play") endedVoices.Remove(key);
            if(viewState.Count>MaxViewStates)
            {
                var oldest=viewState.MinBy(pair=>pair.Value.seq).Key;
                viewState.Remove(oldest);endedVoices.Remove(oldest);ViewStatesEvicted++;
            }
        }
        // Keep the original start while a matching stop is still visible, then discard it.
        double oldestVisible=events.Count>0?events.Peek().time:LastTime;
        foreach(var ended in endedVoices.Where(pair=>pair.Value<oldestVisible).Select(pair=>pair.Key).ToArray())
        {viewState.Remove(ended);endedVoices.Remove(ended);}
    }
    public bool Accept(WireEvent e, string? raw = null)
    {
        lock(gate)
        {
            if (e.session != Id) throw new InvalidDataException("连接中的会话身份发生变化");
            if(e.seq <= watermark && e.kind != "hello") return false;
            // 协议 v1 的桥仅在采集开启时连接；重连不能依赖早已 ACK 的 state=1。
            if(e.kind == "hello") { Name=e.name; Platform=e.platform; Pid=e.pid; Capturing=!IsReplay; return true; }
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
                    writer.WriteLine(raw ?? e.ToJson());
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
            try { next.WriteLine(new WireEvent {kind="hello",session=Id,name=Name,pid=Pid,platform=Platform,source=Source,value=1,detail="CriScope/1; recording begins here; no pre-roll"}.ToJson());next.Flush(); }
            catch {next.Dispose();throw;}
            writer=next;RecordingPath=path;Error=null;
        }
    }
    public void StopRecording() { lock(gate) CloseWriter(); }
    private void CloseWriter() {var old=writer;writer=null;try {old?.Dispose();} catch(Exception ex) {Error="结束写盘失败："+ex.Message;} }
    public void Dispose()=>StopRecording();
}
