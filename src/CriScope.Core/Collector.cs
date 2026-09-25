using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
namespace CriScope.Core;

public sealed partial class Collector : IDisposable
{
    readonly ConcurrentDictionary<string,Session> sessions=new();
    readonly ConcurrentDictionary<string,TcpClient> active=new();
    readonly CancellationTokenSource stop=new();
    TcpListener? listener;
    public Session[] Sessions => sessions.Values.OrderBy(s=>s.IsReplay).ThenBy(s=>s.Name).ToArray();
    public string Status {get;private set;}="尚未启动";
    public string RecordingsDirectory {get;}
    public Collector(string directory) {RecordingsDirectory=directory;}
    public void Start(int port=18961)
    {
        listener=new TcpListener(IPAddress.Any,port);
        try {listener.Start();} catch(SocketException e) {Status=$"接入端口 {port} 不可用：{e.Message}（检查其他 CriScope 实例）";throw;}
        Status=$"等待游戏接入 · 端口 {port}";
        _=AcceptLoop();
    }
    async Task AcceptLoop()
    {
        try {while(!stop.IsCancellationRequested) {var client=await listener!.AcceptTcpClientAsync(stop.Token);_=ReadClient(client);}}
        catch(Exception e) when(e is OperationCanceledException or SocketException or ObjectDisposedException) {if(!stop.IsCancellationRequested) Status=e.Message;}
    }
    async Task ReadLegacyClient(TcpClient client)
    {
        Session? session=null;
        try
        {
            client.NoDelay=true;
            using var stream=client.GetStream();
            using var reader=new StreamReader(stream,new UTF8Encoding(false,true),false,8192,true);
            var lines=new BoundedLines(reader);
            using var writer=new StreamWriter(stream,new UTF8Encoding(false),8192,true){AutoFlush=true};
            using var handshake=CancellationTokenSource.CreateLinkedTokenSource(stop.Token);
            handshake.CancelAfter(TimeSpan.FromSeconds(10));
            var first=await lines.Read(handshake.Token);
            var hello=WireEvent.Parse(first??throw new InvalidDataException("缺少握手"));
            if(hello.kind!="hello" || hello.value!=1 || !Guid.TryParse(hello.session,out _) || hello.name==null || hello.platform==null || hello.name.Length>4096 || hello.platform.Length>100 || hello.pid<0) throw new InvalidDataException("不支持的协议或会话身份");
            session=sessions.GetOrAdd(hello.session,_=>new Session(hello));
            if(session.Pid!=hello.pid || session.Platform!=hello.platform || session.Name!=hello.name) throw new InvalidDataException("重连实例元数据不一致");
            if(active.TryGetValue(session.Id,out var previous)) previous.Dispose();
            active[session.Id]=client;session.SetCaptureState(true,true,"SDK 扩展已连接");session.Accept(hello);
            await writer.WriteLineAsync("{\"ack\":"+session.Watermark+"}");
            while(!stop.IsCancellationRequested)
            {
                var line=await lines.Read(stop.Token);
                if(line==null) break;
                var e=WireEvent.Parse(line);
                if(e.kind=="hello"||!double.IsFinite(e.time)||e.time<0||e.seq<=0||e.name==null||e.detail==null||e.name.Length>4096||e.detail.Length>32768) throw new InvalidDataException("非法事件");
                session.Accept(e,line);
                await writer.WriteLineAsync("{\"ack\":"+session.Watermark+"}");
            }
        }
        catch(Exception e) when(e is IOException or SocketException or OperationCanceledException or ObjectDisposedException or System.Text.Json.JsonException or InvalidDataException or DecoderFallbackException)
        {if(!stop.IsCancellationRequested) Status="连接结束："+e.Message;}
        finally
        {
            if(session!=null && active.TryGetValue(session.Id,out var current) && ReferenceEquals(current,client)) {session.SetCaptureState(false,false,"SDK 扩展已断开");active.TryRemove(session.Id,out _);}
            client.Dispose();
        }
    }
    // 限制单行，避免损坏/错误客户端制造无限字符串分配。
    sealed class BoundedLines(StreamReader reader)
    {
        readonly char[] buffer=new char[8192];int offset,count;
        public async Task<string?> Read(CancellationToken token)
        {
            var line=new StringBuilder();
            while(true) {
                if(offset==count) {count=await reader.ReadAsync(buffer.AsMemory(),token);offset=0;if(count==0){if(line.Length==0)return null;throw new InvalidDataException("截断协议行");}}
                int end=Array.IndexOf(buffer,'\n',offset,count-offset);
                int length=(end<0?count:end)-offset;
                if(line.Length+length>65536)throw new InvalidDataException("事件超过64KiB");
                line.Append(buffer,offset,length);offset+=length;
                if(end>=0){offset++;return line.ToString().TrimEnd('\r');}
            }
        }
    }
    public Session LoadRecording(string path)
    {
        using var r=new StreamReader(path);
        var h=WireEvent.Parse(r.ReadLine()??throw new InvalidDataException("空文件"));
        if(h.kind!="hello"||h.value!=1)throw new InvalidDataException("不是受支持的CriScope记录");
        var replay=new Session(h){IsReplay=true};
        string? line;
        while((line=r.ReadLine())!=null) {if(line.Length>65536)throw new InvalidDataException("记录行过长");if(replay.Total>=2000000)throw new InvalidDataException("当前回放上限为200万事件，请分段录制");replay.Accept(WireEvent.Parse(line));}
        sessions["replay:"+Guid.NewGuid()]=replay;
        return replay;
    }
    public void Dispose() {stop.Cancel();DisposeNative();listener?.Stop();foreach(var c in active.Values)c.Dispose();foreach(var c in bridgeClients.Values)c.Dispose();foreach(var s in sessions.Values)s.Dispose();}
}
