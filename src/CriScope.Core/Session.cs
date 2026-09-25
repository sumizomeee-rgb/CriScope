using System.Text;
namespace CriScope.Core;

public sealed class Session : IDisposable
{
    private readonly object gate = new();
    private readonly Queue<WireEvent> events = new();
    private StreamWriter? writer;
    private DateTime flushed = DateTime.UtcNow;
    private long watermark;
    public const int MaxLiveEvents = 120000;
    public const double LiveSeconds = 120;
    public string Id { get; }
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
    public Session(WireEvent hello) { Id=hello.session; Name=hello.name; Platform=hello.platform; Pid=hello.pid; }
    public WireEvent[] Snapshot() { lock(gate) return events.ToArray(); }
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
    public void StartRecording(string directory)
    {
        lock(gate)
        {
            if(IsReplay) throw new InvalidOperationException("历史记录不可再次录制");
            if(writer!=null) return;
            Directory.CreateDirectory(directory);
            var path=Path.Combine(directory,$"{DateTime.Now:yyyyMMdd-HHmmss-fff}_{Pid}_{Guid.NewGuid():N}.criscope");
            var next=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read),new UTF8Encoding(false));
            try { next.WriteLine(new WireEvent {kind="hello",session=Id,name=Name,pid=Pid,platform=Platform,value=1,detail="CriScope/1; recording begins here; no pre-roll"}.ToJson());next.Flush(); }
            catch {next.Dispose();throw;}
            writer=next;RecordingPath=path;Error=null;
        }
    }
    public void StopRecording() { lock(gate) CloseWriter(); }
    private void CloseWriter() {var old=writer;writer=null;try {old?.Dispose();} catch(Exception ex) {Error="结束写盘失败："+ex.Message;} }
    public void Dispose()=>StopRecording();
}
